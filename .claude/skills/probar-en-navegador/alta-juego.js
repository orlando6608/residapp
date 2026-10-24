// Da de alta un juego de cuentas de database/seed/centros_prueba.sql por el camino real, como en un centro nuevo: Plataforma entra con su
// invitación de arranque (database/operaciones/invitar_cuenta.sql), invita a las Administraciones de los dos centros y a la cuenta de varios
// centros, y cada Administración invita a su personal y a su familiar. Cada cuenta elige contraseña y prepara el código del móvil; sus
// credenciales quedan en el fichero de credenciales del entorno (comun.js), fuera del repositorio. Las que ya tienen credenciales se saltan.
// Uso: JUEGO=orlando ENLACE=<invitación de plataforma-orlando> [BASE_URL=…] node alta-juego.js
const { base, abrirChrome, contexto, errores, entrar, darDeAlta } = require("./comun");

const juego = process.env.JUEGO ?? "orlando";
const infijo = juego === "cj" ? "" : `-${juego}`;
const familiar = juego === "cj" ? "elena" : "pablo";
const plataforma = `plataforma-${juego}`;
const varios = `varios-centros${infijo}`;
const centros = {
  [`almendros${infijo}-administracion`]: ["direccion", "medicina", "enfermeria", "auxiliar", familiar].map((rol) => `almendros${infijo}-${rol}`),
  [`olmos${infijo}-administracion`]: [`olmos${infijo}-enfermeria`],
};

const hecho = [];
let browser;

// El enlace de la hoja de invitación que devuelve la aplicación (el del QR).
async function enlaceDeLaHoja(page) {
  const enlace = await page.getAttribute("#qr-invitacion", "data-direccion");
  if (!enlace) throw new Error(`No se ha emitido la invitación: ${page.url()}`);
  return enlace;
}

async function alta(sujeto, enlace) {
  const ctx = await contexto(browser);
  await darDeAlta(ctx, sujeto, enlace);
  await ctx.close();
  hecho.push(sujeto);
}

const tieneCredenciales = async (sujeto) => {
  const ctx = await contexto(browser);
  try {
    await (await entrar(ctx, sujeto, null)).close();
    return true;
  } catch (error) {
    if (String(error.message).startsWith("No hay credenciales")) return false;
    throw error;
  } finally {
    await ctx.close();
  }
};

(async () => {
  browser = await abrirChrome();
  if (!(await tieneCredenciales(plataforma))) {
    if (!process.env.ENLACE) throw new Error(`Falta ENLACE: la invitación de ${plataforma} (invitar_cuenta.sql).`);
    await alta(plataforma, process.env.ENLACE);
  }

  // Plataforma invita a la Administración de cada centro y a la cuenta de varios centros.
  const ctxPlataforma = await contexto(browser);
  const p = await entrar(ctxPlataforma, plataforma, null);
  for (const admin of Object.keys(centros)) {
    if (await tieneCredenciales(admin)) continue;
    await p.goto(`${base}/Plataforma`);
    for (const href of await p.$$eval('a[href*="/Plataforma/Centro"]', (as) => [...new Set(as.map((a) => a.getAttribute("href")))])) {
      await p.goto(base + href);
      const tarjeta = p.locator("section.card", { has: p.locator(`code:text-is("${admin}")`) });
      if ((await tarjeta.count()) === 0) continue;
      await Promise.all([p.waitForNavigation(), tarjeta.locator('form[action*="Invitacion"] button').click()]);
      await alta(admin, await enlaceDeLaHoja(p));
      break;
    }
  }
  if (!(await tieneCredenciales(varios))) {
    await p.goto(`${base}/Plataforma/Cuentas`);
    const fila = p.locator("li.list-group-item", { has: p.locator(`code:text-is("${varios}")`) });
    await Promise.all([p.waitForNavigation(), fila.locator('form[action*="InvitacionCuenta"] button').click()]);
    await alta(varios, await enlaceDeLaHoja(p));
  }
  await ctxPlataforma.close();

  // Cada Administración invita a su personal y a su familiar, como desde la ficha de la cuenta.
  for (const [admin, cuentas] of Object.entries(centros)) {
    const ctx = await contexto(browser);
    const a = await entrar(ctx, admin, null);
    for (const sujeto of cuentas) {
      if (await tieneCredenciales(sujeto)) continue;
      await a.goto(`${base}/Administracion/Usuarios`);
      const token = await a.getAttribute('input[name="__RequestVerificationToken"]', "value");
      const respuesta = await a.request.post(`${base}/Administracion/Invitacion`, {
        form: { __RequestVerificationToken: token, usuario: sujeto, volver: "/Administracion/Usuarios" },
      });
      const html = await respuesta.text();
      const enlace = /data-direccion="([^"]+)"/.exec(html)?.[1]?.replace(/&amp;/g, "&");
      if (!enlace) throw new Error(`${admin} no pudo invitar a ${sujeto} (HTTP ${respuesta.status()}).`);
      await alta(sujeto, enlace);
    }
    await ctx.close();
  }

  await browser.close();
  console.log(JSON.stringify({ dadasDeAlta: hecho, erroresConsola: errores }, null, 2));
  process.exit(errores.length ? 1 : 0);
})().catch(async (error) => {
  console.error(error);
  console.error(JSON.stringify({ dadasDeAlta: hecho }));
  await browser?.close();
  process.exit(1);
});
