// Recorrido de Importar y Exportar datos (ADR 0010) con una cuenta de Administración recién sembrada. Ejemplo para escribir otros recorridos.
// Uso: mapfile -t cuenta < <(bash sembrar.sh); SUJETO="${cuenta[0]}" ENLACE="${cuenta[1]}" node recorrido-intercambio.js
const fs = require("fs");
const path = require("path");
const { base, salida, abrirChrome, contexto, errores, entrar, captura, desborde } = require("./comun");

const datos = (fichero) => path.join(__dirname, "datos", fichero);
let actual;

(async () => {
  const browser = await abrirChrome();
  const informe = {};

  // 1. Escritorio, claro: estructura de punta a punta.
  const escritorio = await contexto(browser, "claro", { acceptDownloads: true });
  const page = (actual = await entrar(escritorio));
  await page.goto(`${base}/Administracion/Importar`);
  await captura(page, "01-importar");
  await page.setInputFiles("#fichero", datos("estructura-erp.csv"));
  await page.click('button:has-text("Analizar el fichero")');
  await page.waitForSelector("text=Columnas del fichero");
  informe.focoTrasAnalizar = await page.evaluate(() => document.activeElement?.id);
  informe.marcasAuto = await page.locator("text=Emparejado automáticamente").count();
  await captura(page, "02-estructura-emparejamiento");
  await page.click('button:has-text("Ver la vista previa")');
  await page.waitForSelector("#informe-titulo");
  informe.vistaPrevia = await page.locator("[data-intercambio-informe] table tbody tr").allInnerTexts();
  await captura(page, "03-estructura-vista-previa");
  await page.click('button:has-text("Importar")');
  await page.waitForSelector("text=Resultado de la importación");
  informe.resultadoEstructura = await page.locator("[data-intercambio-informe] .alert").innerText();
  await captura(page, "04-estructura-resultado");

  // 2. Residentes: un valor de sexo sin reconocer obliga a elegirlo; al elegirlo se vuelve a analizar y el foco vuelve al desplegable.
  await page.goto(`${base}/Administracion/Importar`);
  await page.click('label[for="entidad-Residentes"]');
  await page.setInputFiles("#fichero", datos("residentes-erp.csv"));
  await page.click('button:has-text("Analizar el fichero")');
  await page.waitForSelector("text=Columnas del fichero");
  informe.problemasAntes = await page.locator(".alert-warning li").allInnerTexts();
  informe.avisos = await page.locator(".alert-info li").allInnerTexts();
  await captura(page, "05-residentes-emparejamiento");
  const pendiente = page.locator('select[name^="SexoCodigos"]').filter({ has: page.locator('option[value=""]:checked') });
  const pendienteId = await pendiente.first().getAttribute("id");
  await page.selectOption(`#${pendienteId}`, "unknown");
  await page.waitForSelector('button:has-text("Ver la vista previa")');
  informe.focoTrasReanalizar = await page.evaluate(() => document.activeElement?.id);
  informe.problemasDespues = await page.locator(".alert-warning li").count();
  await page.click('button:has-text("Ver la vista previa")');
  await page.waitForSelector("#informe-titulo");
  informe.vistaPreviaResidentes = await page.locator("[data-intercambio-informe] table tbody tr").allInnerTexts();
  await captura(page, "06-residentes-vista-previa");
  await page.fill("#nombre-plantilla", "ERP de prueba");
  await page.click('button:has-text("Importar")');
  await page.waitForSelector("text=Resultado de la importación");
  informe.resultadoResidentes = await page.locator("[data-intercambio-informe] .alert").innerText();
  await captura(page, "07-residentes-resultado");

  // La plantilla se aplica sola al mismo fichero.
  await page.goto(`${base}/Administracion/Importar`);
  await page.click('label[for="entidad-Residentes"]');
  await page.setInputFiles("#fichero", datos("residentes-erp.csv"));
  await page.click('button:has-text("Analizar el fichero")');
  await page.waitForSelector("text=Columnas del fichero");
  informe.plantillaAplicada = await page.locator("text=Se ha aplicado la plantilla").innerText();

  // 3. Exportar: plantilla de otra aplicación y descargas.
  await page.goto(`${base}/Administracion/Exportar`);
  await captura(page, "08-exportar");
  await page.setInputFiles("#fichero-ejemplo", datos("ejemplo-destino.csv"));
  await page.click('button:has-text("Analizar el fichero")');
  await page.waitForSelector("text=Columnas del fichero de ejemplo");
  await page.selectOption("#exportar-formato-fecha", "YMD");
  await page.fill("#exportar-nombre-plantilla", "Programa destino");
  await captura(page, "09-exportar-plantilla");
  await Promise.all([page.waitForURL(/plantillaGuardada=/), page.click('button:has-text("Guardar la plantilla")')]);
  await page.waitForSelector("text=Plantilla «Programa destino» guardada.");
  await captura(page, "10-exportar-lista");
  informe.descargas = {};
  for (const [nombre, selector] of [
    ["censo", 'button:has-text("Descargar el censo de residentes")'],
    ["estructura", 'button:has-text("Descargar la estructura")'],
    ["plantilla", 'li:has-text("Programa destino") button:has-text("Descargar")'],
  ]) {
    const [descarga] = await Promise.all([page.waitForEvent("download"), page.click(selector)]);
    const fichero = path.join(salida, `${nombre}.csv`);
    await descarga.saveAs(fichero);
    informe.descargas[nombre] = { nombre: descarga.suggestedFilename(), contenido: fs.readFileSync(fichero, "utf8") };
  }
  await escritorio.close();

  // 4. Oscuro y 375 px: sin desplazamiento lateral.
  informe.desborde = {};
  for (const aspecto of ["oscuro", "movil", "movil-oscuro"]) {
    const ctx = await contexto(browser, aspecto);
    const p = (actual = await entrar(ctx));
    await p.goto(`${base}/Administracion/Importar`);
    await p.click('label[for="entidad-Residentes"]');
    await p.setInputFiles("#fichero", datos("residentes-erp.csv"));
    await p.click('button:has-text("Analizar el fichero")');
    await p.waitForSelector("text=Columnas del fichero");
    await p.click('button:has-text("Ver la vista previa")');
    await p.waitForSelector("#informe-titulo");
    informe.desborde[`importar-${aspecto}`] = await desborde(p);
    await captura(p, `11-importar-${aspecto}`);
    await p.goto(`${base}/Administracion/Exportar`);
    informe.desborde[`exportar-${aspecto}`] = await desborde(p);
    await captura(p, `12-exportar-${aspecto}`);
    await ctx.close();
  }

  await browser.close();
  informe.erroresConsola = errores;
  console.log(JSON.stringify(informe, null, 2));
  console.log(`Capturas en ${salida}`);
  if (errores.length > 0 || Object.values(informe.desborde).some((px) => px > 0)) process.exit(1);
})().catch(async (e) => {
  if (actual) await captura(actual, "fallo").catch(() => {});
  console.error(e);
  console.error(`Captura del fallo en ${path.join(salida, "fallo.png")}`);
  process.exit(1);
});
