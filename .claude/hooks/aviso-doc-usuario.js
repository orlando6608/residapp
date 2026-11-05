#!/usr/bin/env node
// Hook PostToolUse (Edit|Write): avisa a Claude, una vez por sesión, de que ha tocado código que
// determina lo que cuenta la documentación de usuario. No bloquea nada: solo añade contexto.
// La lista de carpetas y documentos vive en .claude/documentacion-usuario.json (no se repite aquí).
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');

function leerEntrada() {
  try { return JSON.parse(fs.readFileSync(0, 'utf8')); } catch { return null; }
}

function avisar(texto) {
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: 'PostToolUse', additionalContext: texto },
  }));
}

// Una sola vez por sesión y por tipo de aviso. Si no se puede escribir la marca, se avisa igualmente.
function yaAvisado(sesion, tipo) {
  const marca = path.join(os.tmpdir(), `residapp-aviso-doc-${tipo}-${String(sesion).replace(/[^\w-]/g, '_')}.flag`);
  try {
    if (fs.existsSync(marca)) return true;
    fs.writeFileSync(marca, '');
  } catch { /* sin marca: puede repetirse el aviso, que es mejor que callar */ }
  return false;
}

const norm = (p) => String(p).replace(/\\/g, '/').toLowerCase();

const entrada = leerEntrada();
const fichero = entrada && entrada.tool_input && entrada.tool_input.file_path;
if (!fichero) process.exit(0);

const raiz = process.env.CLAUDE_PROJECT_DIR || (entrada && entrada.cwd) || process.cwd();
const rel = path.relative(raiz, fichero);
if (rel.startsWith('..') || path.isAbsolute(rel)) process.exit(0); // fuera del proyecto
const relN = norm(rel);

let registro;
try {
  registro = JSON.parse(fs.readFileSync(path.join(raiz, '.claude', 'documentacion-usuario.json'), 'utf8'));
  if (!Array.isArray(registro.disparadores) || !Array.isArray(registro.documentos)) throw new Error('faltan «disparadores» o «documentos»');
} catch (e) {
  // Registro ausente o mal formado: solo molesta si se está editando código de la web, para no avisar de la nada.
  if (relN.startsWith('src/') && !yaAvisado(entrada.session_id, 'registro')) {
    avisar('No se pudo leer .claude/documentacion-usuario.json (' + e.message + '). Sin ese registro el aviso de revisión de la documentación de usuario está desactivado hasta que se arregle.');
  }
  process.exit(0);
}

// Un documento no es código a vigilar, aunque viva bajo una carpeta disparadora (p. ej. Manual.cshtml bajo Views/).
const esDocumento = registro.documentos.some((d) => d && d.ruta && !String(d.ruta).includes('*') && norm(d.ruta) === relN);
if (esDocumento) process.exit(0);

const esDisparador = registro.disparadores.some((d) => relN.startsWith(norm(d)));
if (!esDisparador) process.exit(0);
if (yaAvisado(entrada.session_id, 'cambio')) process.exit(0);

const docs = registro.documentos.map((d) => d.que || d.ruta).join('; ');
avisar(
  'Aviso del repositorio: se ha editado ' + rel.replace(/\\/g, '/') + ', código que determina lo que describe la documentación de usuario (' + docs + '). ' +
  'Según CLAUDE.md, antes de dar el cambio por cerrado corresponde consultar al agente doc-usuario-residapp: si su informe dice «breve» se aplican los cambios en el momento; ' +
  'si dice «larga» se propone a la persona antes de editar. Si el cambio no altera texto visible, ruta ni comportamiento, basta una línea en el mensaje final. ' +
  'Este aviso aparece una sola vez por sesión.'
);
