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

// Abre una página y entra por /DevAuth/Login con la cuenta sembrada (con un solo perfil no pide elegirlo).
async function entrar(ctx, sujeto = process.env.SUJETO) {
  if (!sujeto) throw new Error("Falta SUJETO: siembra una cuenta con sembrar.sh.");
  const page = await ctx.newPage();
  page.on("console", (m) => m.type() === "error" && errores.push(`${page.url()}: ${m.text()}`));
  page.on("pageerror", (e) => errores.push(`${page.url()}: ${e.message}`));
  await page.goto(`${base}/DevAuth/Login`);
  await page.fill('input[name="externalSubject"]', sujeto);
  await Promise.all([page.waitForNavigation(), page.click('form[action*="Login"] button[type="submit"]')]);
  return page;
}

const captura = (page, nombre) => page.screenshot({ path: path.join(salida, `${nombre}.png`), fullPage: true });

// Píxeles de desplazamiento lateral: 0 si la página cabe en el ancho.
const desborde = (page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

module.exports = { base, salida, abrirChrome, aspectos, contexto, errores, entrar, captura, desborde };
