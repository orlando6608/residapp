// Genera los PDF de la Guía por perfil con Chrome (Playwright) a partir de la web en marcha.
//
//   node generar.mjs [--base http://localhost:5203] [--salida salida] [--solo plataforma,todo]
//
// Un PDF por perfil, otro de «Para todos» y otro con la guía completa (/Guia/todo). Cada página lleva «Página X de Y» y cada
// índice de tareas lleva el número de página de cada ficha. Para eso se renderiza varias veces: se mide en qué página cae cada ficha
// y se escribe el número en el índice, hasta que deja de cambiar. Al final comprueba que ninguna ficha empieza en una página y acaba
// en otra (sale con código 1 si alguna se parte). Los PDF salen de la versión de la Guía que sirve la web: no se suben al repositorio.
import fs from "node:fs";
import path from "node:path";
import { chromium } from "playwright-core";
import * as pdfjs from "pdfjs-dist/legacy/build/pdf.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, todos) => (a.startsWith("--") ? [...acc, [a.slice(2), todos[i + 1]]] : acc), []));
const BASE = (args.base ?? "http://localhost:5203").replace(/\/$/, "");
const SALIDA = path.resolve(args.salida ?? "salida");
const SOLO = args.solo ? args.solo.split(",") : null;
const MAX_PASADAS = 4;

const PERFILES = ["plataforma", "administracion", "auxiliar", "enfermeria", "medicina", "direccion", "familia"];
const DOCUMENTOS = [...PERFILES.map((s) => ({ nombre: `guia-${s}`, ruta: `/Guia/${s}`, completa: false })), { nombre: "guia-para-todos", ruta: "/Guia/para-todos", completa: false }, { nombre: "guia-completa", ruta: "/Guia/todo", completa: true }];

const PIE = `<div style="width:100%;font-size:9px;font-family:Arial,sans-serif;color:#444;text-align:right;padding-right:12mm;">Página <span class="pageNumber"></span> de <span class="totalPages"></span></div>`;
const ESTILO_NUMEROS = ".guia-pagina { margin-left: 0.5rem; color: #444; white-space: nowrap; font-size: 0.85em; }";
const ETIQUETA_FICHA = /SIEMPRE|Siempre|REQUIERE|Requiere permiso/;

/** El texto de cada página del PDF, sin saltos ni espacios repetidos. */
async function textoPorPagina(buffer) {
  const doc = await pdfjs.getDocument({ data: new Uint8Array(buffer), useSystemFonts: true, verbosity: 0 }).promise;
  const paginas = [];
  for (let i = 1; i <= doc.numPages; i++) {
    const contenido = await (await doc.getPage(i)).getTextContent();
    paginas.push(contenido.items.map((x) => x.str).join(" ").replace(/\s+/g, " "));
  }
  return paginas;
}

/** Página (1…n) y página final de cada ficha. El título es la aparición «CÓDIGO ¿Cómo…» que lleva la etiqueta Siempre o Requiere permiso
 *  antes de que empiece otra ficha o lista: así no se confunde con el índice de tareas ni con la lista de preguntas. */
function localizarFichas(paginas) {
  const fichas = new Map();
  const titulo = /\b([A-Z]{3}-\d{2}) ¿Cómo/g;
  paginas.forEach((texto, i) => {
    let m;
    while ((m = titulo.exec(texto))) {
      const resto = texto.slice(m.index + m[0].length, m.index + m[0].length + 500);
      const hastaOtra = resto.search(/\b[A-Z]{3}-\d{2} ¿Cómo/);
      const tramo = hastaOtra < 0 ? resto : resto.slice(0, hastaOtra);
      if (!ETIQUETA_FICHA.test(tramo)) continue;
      const codigo = m[1];
      // la última aparición válida gana (en la guía completa la lista de preguntas va después de las fichas, y no lleva etiqueta)
      let fin = i;
      for (let j = i; j < paginas.length; j++) {
        const desde = j === i ? m.index : 0;
        if (paginas[j].indexOf("Dónde se explica con más detalle", desde) >= 0) { fin = j; break; }
        if (j === paginas.length - 1) fin = -1;
      }
      fichas.set(codigo, { inicio: i + 1, fin: fin < 0 ? -1 : fin + 1 });
    }
  });
  return fichas;
}

/** Página de cada capítulo de la guía completa, por los marcadores invisibles que se ponen al medir. */
function localizarCapitulos(paginas) {
  const capitulos = new Map();
  paginas.forEach((texto, i) => {
    for (const m of texto.matchAll(/§§cap:([a-z-]+)§§/g)) if (!capitulos.has(m[1])) capitulos.set(m[1], i + 1);
  });
  return capitulos;
}

async function preparar(page, ruta, { numeros, capitulos, marcadores }) {
  const respuesta = await page.goto(BASE + ruta, { waitUntil: "networkidle" });
  if (!respuesta || respuesta.status() !== 200) throw new Error(`${ruta}: la web responde ${respuesta?.status()}. ¿Está en marcha en ${BASE}?`);
  await page.emulateMedia({ media: "print" });
  await page.addStyleTag({ content: ESTILO_NUMEROS });
  await page.evaluate(({ numeros, capitulos, marcadores }) => {
    // Páginas de las fichas en cada lista de tareas o de preguntas (todo enlace «#XXX-NN» dentro de .guia-indice).
    for (const a of document.querySelectorAll(".guia-indice a[href^='#']")) {
      const codigo = a.getAttribute("href").slice(1);
      const pagina = numeros?.[codigo];
      if (pagina) {
        const span = document.createElement("span");
        span.className = "guia-pagina";
        span.textContent = `p. ${pagina}`;
        a.after(span);
      }
    }
    // «Contenido» de la guía completa: página de cada capítulo.
    for (const a of document.querySelectorAll("#contenido ~ ul a[href^='#capitulo-']")) {
      const pagina = capitulos?.[a.getAttribute("href").slice(10)];
      if (pagina) {
        const span = document.createElement("span");
        span.className = "guia-pagina";
        span.textContent = `p. ${pagina}`;
        a.after(span);
      }
    }
    // Marcadores invisibles para medir en qué página empieza cada capítulo (no se escriben en el PDF final).
    if (marcadores) {
      for (const c of document.querySelectorAll(".guia-capitulo")) {
        const m = document.createElement("span");
        m.textContent = `§§cap:${c.id.replace("capitulo-", "")}§§`;
        m.style.cssText = "position:absolute;font-size:1px;color:transparent;";
        c.prepend(m);
      }
    }
  }, { numeros, capitulos, marcadores });
}

async function pdf(page) {
  return await page.pdf({ preferCSSPageSize: true, printBackground: false, displayHeaderFooter: true, headerTemplate: "<span></span>", footerTemplate: PIE });
}

const mapa = (m) => Object.fromEntries([...m].map(([k, v]) => [k, typeof v === "object" ? v.inicio : v]));

async function generar(browser, doc) {
  const page = await browser.newPage();
  let numeros = {};
  let capitulos = {};
  let buffer;
  let fichas;
  let estable = false;
  for (let pasada = 1; pasada <= MAX_PASADAS && !estable; pasada++) {
    await preparar(page, doc.ruta, { numeros, capitulos, marcadores: doc.completa });
    buffer = await pdf(page);
    const paginas = await textoPorPagina(buffer);
    fichas = localizarFichas(paginas);
    const nuevos = mapa(fichas);
    const nuevosCap = doc.completa ? Object.fromEntries(localizarCapitulos(paginas)) : {};
    estable = JSON.stringify(nuevos) === JSON.stringify(numeros) && JSON.stringify(nuevosCap) === JSON.stringify(capitulos);
    numeros = nuevos;
    capitulos = nuevosCap;
  }
  if (!estable) console.warn(`  ${doc.nombre}: los números de página no se estabilizaron en ${MAX_PASADAS} pasadas; revisa el índice.`);
  // Último render sin marcadores, con los números definitivos.
  if (doc.completa) {
    await preparar(page, doc.ruta, { numeros, capitulos, marcadores: false });
    buffer = await pdf(page);
    const paginas = await textoPorPagina(buffer);
    fichas = localizarFichas(paginas);
  }
  await page.close();
  fs.mkdirSync(SALIDA, { recursive: true });
  const ruta = path.join(SALIDA, `${doc.nombre}.pdf`);
  fs.writeFileSync(ruta, buffer);
  const paginasTotal = (await textoPorPagina(buffer)).length;
  const partidas = [...fichas].filter(([, f]) => f.fin !== f.inicio).map(([codigo, f]) => `${codigo} (p. ${f.inicio}${f.fin > 0 ? ` a ${f.fin}` : ", sin final"})`);
  console.log(`${doc.nombre}.pdf: ${paginasTotal} páginas, ${fichas.size} fichas${partidas.length ? `, ¡PARTIDAS: ${partidas.join(", ")}!` : ", ninguna partida"}.`);
  return { partidas, fichas: fichas.size };
}

const documentos = DOCUMENTOS.filter((d) => !SOLO || SOLO.some((s) => d.nombre === `guia-${s}` || d.nombre === s || (s === "todo" && d.completa)));
if (documentos.length === 0) { console.error(`Nada que generar con --solo ${args.solo}.`); process.exit(2); }
const browser = await chromium.launch({ channel: "chrome", headless: true });
let fallos = 0;
try {
  for (const doc of documentos) {
    const r = await generar(browser, doc);
    fallos += r.partidas.length;
  }
} finally {
  await browser.close();
}
console.log(`PDF en ${SALIDA}`);
process.exit(fallos ? 1 : 0);
