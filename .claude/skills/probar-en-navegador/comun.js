// Utilidades para recorrer ResidApp en Chrome sin interfaz (skill probar-en-navegador).
const fs = require("fs");
const os = require("os");
const path = require("path");

const base = process.env.BASE_URL ?? "http://localhost:5203";
const salida = process.env.SALIDA ?? path.join(os.tmpdir(), "residapp-navegador");
fs.mkdirSync(salida, { recursive: true });

// playwright-core no es dependencia del repositorio: se toma de PLAYWRIGHT_CORE o de la caché de npx (la copia más reciente).
function playwright() {
  if (process.env.PLAYWRIGHT_CORE) return require(process.env.PLAYWRIGHT_CORE);
  const cache = path.join(process.env.LOCALAPPDATA ?? "", "npm-cache", "_npx");
  const copias = (fs.existsSync(cache) ? fs.readdirSync(cache) : [])
    .map((d) => path.join(cache, d, "node_modules", "playwright-core"))
    .filter((d) => fs.existsSync(path.join(d, "package.json")))
    .sort((a, b) => fs.statSync(a).mtimeMs - fs.statSync(b).mtimeMs);
  if (copias.length === 0) throw new Error("No encuentro playwright-core: ejecuta una vez «npx -y playwright-core@1.63.0 --version».");
  return require(copias.at(-1));
}

// Usa el Chrome instalado (channel "chrome"): no hace falta descargar navegadores de Playwright.
const abrirChrome = () => playwright().chromium.launch({ channel: "chrome", headless: true });

// Escritorio y móvil de 375 px, en claro y en oscuro.
const aspectos = {
  claro: { viewport: { width: 1280, height: 900 } },
  oscuro: { viewport: { width: 1280, height: 900 }, colorScheme: "dark" },
  movil: { viewport: { width: 375, height: 800 }, isMobile: true, hasTouch: true },
  "movil-oscuro": { viewport: { width: 375, height: 800 }, isMobile: true, hasTouch: true, colorScheme: "dark" },
};
const contexto = (browser, aspecto = "claro", opciones = {}) => browser.newContext({ ...aspectos[aspecto], locale: "es-ES", ...opciones });

// Errores de consola y de JavaScript de todas las páginas abiertas con entrar().
const errores = [];

// Código TOTP (RFC 6238: HMAC-SHA1, 30 segundos, 6 cifras) de una clave en base32, como una aplicación de autenticación.
function codigoTotp(clave, ahora = Date.now()) {
  const alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of clave.replace(/[\s=]/g, "").toUpperCase()) bits += alfabeto.indexOf(c).toString(2).padStart(5, "0");
  const bytes = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)));
  const contador = Buffer.alloc(8);
  contador.writeBigUInt64BE(BigInt(Math.floor(ahora / 30000)));
  const hash = require("crypto").createHmac("sha1", bytes).update(contador).digest();
  const desplazamiento = hash[hash.length - 1] & 0x0f;
  return String((hash.readUInt32BE(desplazamiento) & 0x7fffffff) % 1000000).padStart(6, "0");
}

// Credenciales de las cuentas dadas de alta por estos guiones (contraseña y clave del código del móvil), una por usuario. Viven fuera del
// repositorio, en un fichero por entorno (CREDENCIALES o ~/.residapp/credenciales-<servidor>.json): con ellas también puede entrar una
// persona, añadiendo la clave a su aplicación de autenticación.
const ficheroCredenciales = process.env.CREDENCIALES
  ?? path.join(os.homedir(), ".residapp", `credenciales-${new URL(base).host.replace(/[^a-z0-9.-]/gi, "_")}.json`);
const leerCredenciales = () => (fs.existsSync(ficheroCredenciales) ? JSON.parse(fs.readFileSync(ficheroCredenciales, "utf8")) : {});
function guardarCredencial(sujeto, credencial) {
  fs.mkdirSync(path.dirname(ficheroCredenciales), { recursive: true });
  fs.writeFileSync(ficheroCredenciales, JSON.stringify({ ...leerCredenciales(), [sujeto]: credencial }, null, 2));
}

// Escribe el código del móvil; si coincide con el último usado con esa clave, espera al siguiente (cambia cada 30 segundos).
const ultimoCodigo = {};
async function escribirCodigo(page, clave) {
  let codigo = codigoTotp(clave);
  while (codigo === ultimoCodigo[clave]) {
    await page.waitForTimeout(2000);
    codigo = codigoTotp(clave);
  }
  ultimoCodigo[clave] = codigo;
  await page.fill('input[name="codigo"]', codigo);
  await Promise.all([page.waitForNavigation(), page.click('form button[type="submit"]')]);
}

async function nuevaPagina(ctx) {
  const page = await ctx.newPage();
  page.on("console", (m) => m.type() === "error" && errores.push(`${page.url()}: ${m.text()}`));
  page.on("pageerror", (e) => errores.push(`${page.url()}: ${e.message}`));
  return page;
}

// Usa la invitación como la persona: elige la contraseña y, si es del personal, prepara el código del móvil. Guarda las credenciales.
async function darDeAlta(ctx, sujeto, enlace) {
  const page = await nuevaPagina(ctx);
  const credencial = { contrasena: `Prueba ${sujeto} ${Date.now()}`, clave: null };
  await page.goto(enlace);
  await page.fill('input[name="contrasena"]', credencial.contrasena);
  await page.fill('input[name="confirmacion"]', credencial.contrasena);
  await Promise.all([page.waitForNavigation(), page.click('form button[type="submit"]')]);
  if (page.url().includes("/Acceso/ActivarSegundoFactor")) {
    credencial.clave = (await page.textContent("#clave-autenticador")).replace(/\s/g, "");
    await escribirCodigo(page, credencial.clave);
    // Los códigos de recuperación se enseñan una vez; aquí no hacen falta.
    await Promise.all([page.waitForNavigation(), page.click('a:has-text("continuar")')]);
  }
  if (page.url().includes("/Acceso/")) throw new Error(`El alta de ${sujeto} no terminó: ${page.url()}`);
  guardarCredencial(sujeto, credencial);
  return page;
}

// Abre una página y entra con la cuenta (con un solo perfil no pide elegirlo). Si aún no tiene credenciales guardadas y hay enlace de
// invitación (ENLACE, de sembrar.sh), la da de alta; si no, entra con contraseña y, si es del personal, con el código del móvil.
async function entrar(ctx, sujeto = process.env.SUJETO, enlace = process.env.ENLACE) {
  if (!sujeto) throw new Error("Falta SUJETO: siembra una cuenta con sembrar.sh.");
  const credencial = leerCredenciales()[sujeto];
  if (!credencial) {
    if (!enlace) throw new Error(`No hay credenciales de ${sujeto} en ${ficheroCredenciales} ni enlace de invitación (ENLACE).`);
    return darDeAlta(ctx, sujeto, enlace);
  }
  const page = await nuevaPagina(ctx);
  await page.goto(`${base}/Acceso/Entrar`);
  await page.fill('input[name="usuario"]', sujeto);
  await page.fill('input[name="contrasena"]', credencial.contrasena);
  await Promise.all([page.waitForNavigation(), page.click('form[action*="Entrar"] button[type="submit"]')]);
  if (page.url().includes("/Acceso/SegundoFactor")) await escribirCodigo(page, credencial.clave);
  if (page.url().includes("/Acceso/")) throw new Error(`${sujeto} no pudo entrar: ${page.url()}`);
  return page;
}

const captura = (page, nombre) => page.screenshot({ path: path.join(salida, `${nombre}.png`), fullPage: true });

// Píxeles de desplazamiento lateral: 0 si la página cabe en el ancho.
const desborde = (page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

module.exports = { base, salida, abrirChrome, aspectos, contexto, errores, entrar, darDeAlta, captura, desborde };
