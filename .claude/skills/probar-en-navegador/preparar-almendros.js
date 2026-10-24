// Deja la «Residencia Los Almendros» como la esperan las guías de pruebas de CJ, haciendo por la aplicación real lo que no se puede sembrar
// (database/seed/centros_prueba.sql): con la Enfermería del juego indicado firma el basal de Antonio y de Pilar, pone la foto de Manuel y
// deja a Pilar con historial (un evento cerrado y otro derivado a Urgencias con su informe firmado). Solo una vez por entorno: lo hecho no
// se puede deshacer. Comprueba antes lo que ya está y no lo repite.
// Uso: JUEGO=orlando [BASE_URL=…] node preparar-almendros.js   (las cuentas, dadas de alta con alta-juego.js)
const path = require("path");
const { base, salida, abrirChrome, contexto, errores, entrar, captura } = require("./comun");

const infijo = (process.env.JUEGO ?? "orlando") === "cj" ? "" : `-${process.env.JUEGO ?? "orlando"}`;
const enfermeria = `almendros${infijo}-enfermeria`;
const residente = (n) => `1A000000-0000-4000-8000-00000000000${n}`;
const antonio = residente(2), manuel = residente(5), pilar = residente(6);
const hoy = new Date().toISOString().slice(0, 10);
const hecho = [];

// Envía el formulario que contiene el botón y espera a la página siguiente.
async function pulsar(page, boton) {
  await Promise.all([page.waitForNavigation(), page.locator(boton).first().click()]);
}

// Elige en una lista la primera opción cuyo texto encaja.
async function elegir(page, lista, texto) {
  const opcion = await page.$$eval(`${lista} option`,
    (os, fuente) => os.find((o) => o.value && new RegExp(fuente, "i").test(o.textContent))?.value, texto.source);
  if (opcion === undefined) throw new Error(`No hay ninguna opción ${texto} en ${lista}.`);
  await page.selectOption(lista, opcion);
}

// Falla con el mensaje de la página si quedó un error de validación.
async function sinErrores(page, paso) {
  const texto = (await page.locator(".validation-summary-errors, .alert-danger, .text-danger:not(:empty)").allInnerTexts()).join(" ").trim();
  if (texto) {
    await captura(page, `fallo-${paso}`);
    throw new Error(`${paso}: ${texto}`);
  }
}

async function firmarBasal(page, residenteId) {
  // Con un basal vigente, la ficha ofrece «Iniciar reevaluación»: ya está hecho.
  await page.goto(`${base}/Enfermeria/Residente?residenteId=${residenteId}`);
  if (await page.locator('a:has-text("Iniciar reevaluación")').count()) return false;
  await page.goto(`${base}/EnfermeriaBasal/Draft?residenteId=${residenteId}`);
  if (await page.locator('form[action*="CrearBorrador"]').count()) {
    await elegir(page, 'select[name*="Motivo"]', /alta/i);
    await elegir(page, 'select[name*="FuenteInformacionComun"]', /directa/i);
    await page.fill('input[name*="FechaInformacionComun"]', hoy);
    await pulsar(page, 'form[action*="CrearBorrador"] button[type="submit"]');
    await sinErrores(page, "crear el borrador");
  }
  // Las nueve áreas: lo que viene elegido por defecto y, en cada grupo de casillas que no admite quedarse vacío, la primera.
  const areas = await page.$$eval('a[href*="/EnfermeriaBasal/Area"]', (as) => [...new Set(as.map((a) => a.getAttribute("href")))]);
  for (const area of areas) {
    await page.goto(base + area);
    for (const grupo of ["ManagementCodes", "UsualFormsCodes", "SleepPatternCodes", "AidCodes"]) {
      const casillas = page.locator(`input[type="checkbox"][name="${grupo}"]`);
      if ((await casillas.count()) && !(await page.locator(`input[name="${grupo}"]:checked`).count())) await casillas.first().check();
    }
    await pulsar(page, 'form[action*="/EnfermeriaBasal/Area"] button[type="submit"]');
    await sinErrores(page, `el área ${area}`);
  }
  // El Barthel: la primera opción de cada elemento.
  await page.goto(`${base}/EnfermeriaBasal/Barthel?residenteId=${residenteId}`);
  await page.fill('input[name="FechaValoracion"]', hoy);
  for (const nombre of await page.$$eval('input[type="radio"][name^="Opciones["]', (rs) => [...new Set(rs.map((r) => r.name))])) {
    await page.locator(`input[type="radio"][name="${nombre}"]`).first().check();
  }
  await pulsar(page, 'form[action*="/EnfermeriaBasal/Barthel"] button[type="submit"]');
  await sinErrores(page, "el Barthel");
  await page.goto(`${base}/EnfermeriaBasal/Confirmar?residenteId=${residenteId}`);
  await pulsar(page, 'form[action*="/Baseline/Sign"] button[type="submit"]');
  await sinErrores(page, "firmar el basal");
  return true;
}

// Registra un evento ordinario y guarda su valoración; devuelve la dirección de su detalle.
async function eventoValorado(page, residenteId, observacion, hallazgos) {
  await page.goto(`${base}/Enfermeria/RegistrarEvento?residenteId=${residenteId}`);
  await page.fill('textarea[name="Observacion"]', observacion);
  await pulsar(page, 'form[action*="RegistrarEvento"] button[type="submit"]');
  await sinErrores(page, "registrar el evento");
  const detalle = page.url();
  await pulsar(page, 'form[action*="EmpezarValoracion"] button[type="submit"]');
  await page.fill('textarea[name="Form.Hallazgos"]', hallazgos);
  await pulsar(page, 'form[action*="/Enfermeria/Valoracion"] button[type="submit"]');
  await sinErrores(page, "guardar la valoración");
  return new URL(detalle).searchParams.get("eventoId");
}

// Cierra el evento sin comunicar nada a la familia; tras una derivación a Urgencias no se puede, y se prepara la actualización para ella.
async function cerrar(page, eventoId) {
  await page.goto(`${base}/Enfermeria/Cerrar?eventoId=${eventoId}`);
  const noComunicar = page.locator('input[type="radio"][name="Form.Comunicacion"][value="NoComunicar"]');
  if (await noComunicar.count()) {
    await noComunicar.check({ force: true });
  } else {
    await elegir(page, 'select[name="Form.TipoComunicacion"]', /\S/);
    await page.fill('textarea[name="Form.TextoComunicacion"]', "Se ha trasladado a Urgencias por dificultad para respirar; os llamaremos con novedades (ficticio).");
  }
  await pulsar(page, 'form[action*="/Enfermeria/Cerrar"] button[type="submit"]');
  await sinErrores(page, "cerrar el evento");
}

(async () => {
  const browser = await abrirChrome();
  const page = await entrar(await contexto(browser), enfermeria, null);

  for (const [nombre, id] of [["Antonio", antonio], ["Pilar", pilar]]) {
    if (await firmarBasal(page, id)) hecho.push(`basal firmado de ${nombre}`);
  }

  // La foto de Manuel: una imagen ficticia (sus iniciales) generada aquí mismo.
  await page.goto(`${base}/Enfermeria/Residente?residenteId=${manuel}`);
  if (await page.locator("#foto-residente-fichero").count() && !(await page.locator('button:has-text("Quitar foto")').count())) {
    const imagen = await browser.newPage({ viewport: { width: 240, height: 240 } });
    await imagen.setContent('<body style="margin:0;display:grid;place-items:center;height:240px;background:#5b7f95;color:#fff;font:bold 96px sans-serif">MR</body>');
    const fichero = path.join(salida, "foto-manuel.png");
    await imagen.screenshot({ path: fichero });
    await imagen.close();
    await page.setInputFiles("#foto-residente-fichero", fichero);
    await page.check("#foto-residente-consentimiento");
    await pulsar(page, 'form:has(#foto-residente-fichero) button[type="submit"]');
    await sinErrores(page, "poner la foto");
    hecho.push("foto de Manuel");
  }

  // El historial de Pilar, si aún no tiene ningún evento.
  await page.goto(`${base}/Enfermeria/Historial?residenteId=${pilar}`);
  if (!(await page.content()).includes("Caída sin consecuencias")) {
    const primero = await eventoValorado(page, pilar, "Caída sin consecuencias al levantarse de la cama (ficticio).",
      "Sin dolor ni deformidad; deambula como siempre.");
    await cerrar(page, primero);
    hecho.push("evento cerrado de Pilar");

    const segundo = await eventoValorado(page, pilar, "Disnea brusca en reposo (ficticio).", "Saturación 86 % que no remonta con oxígeno.");
    await page.goto(`${base}/Enfermeria/ActivarProtocolo?eventoId=${segundo}`);
    await pulsar(page, 'form[action*="ActivarProtocolo"] button[type="submit"]');
    await sinErrores(page, "activar el protocolo");
    await page.goto(`${base}/Enfermeria/Derivar?eventoId=${segundo}`);
    await page.fill('textarea[name="Form.Motivo"]', "Desaturación que no remonta con oxigenoterapia (ficticio).");
    await page.fill('textarea[name="Form.Comunicaciones"]', "Aviso telefónico al 112 y a la familia (ficticio).");
    await pulsar(page, 'button[name="Form.Accion"][value="VistaPrevia"]');
    await sinErrores(page, "la vista previa del informe");
    await pulsar(page, 'button[name="Form.Accion"][value="Firmar"]');
    if (!(await page.content()).includes("Informe de derivación firmado.")) throw new Error("No se firmó el informe de derivación.");
    // Toda derivación exige documentar el intento de llamada a la familia antes de cerrar.
    await page.goto(`${base}/Enfermeria/Protocolo?eventoId=${segundo}`);
    await page.fill('input[name="Call.Contacto"]', "Su hija (ficticia)");
    await elegir(page, 'select[name="Call.Resultado"]', /\S/);
    await pulsar(page, 'form[action*="IntentoLlamada"] button[type="submit"]');
    await sinErrores(page, "registrar el intento de llamada");
    await cerrar(page, segundo);
    hecho.push("derivación de Pilar con informe firmado");
  }

  await captura(page, "preparar-almendros-final");
  await browser.close();
  console.log(JSON.stringify({ hecho, erroresConsola: errores }, null, 2));
  process.exit(errores.length ? 1 : 0);
})().catch((error) => {
  console.error(error);
  console.error(JSON.stringify({ hecho }));
  process.exit(1);
});
