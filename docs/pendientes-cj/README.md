# Pendientes de CJ

Documentos que CJ tiene que completar: valores clínicos y decisiones de producto que ingeniería no puede
inventar. Cuando CJ los completa, ingeniería revisa las respuestas y las incorpora a la aplicación.

## Las secciones (tres para CJ y una para Orlando)

La web (`/pendientes-cj/index.html`) tiene tres secciones para CJ y una de trabajo para Orlando, cada una con su página:

| Sección | Página | Qué hay |
| --- | --- | --- |
| **Preguntas** | `preguntas.html` | Documentos con decisiones y valores que CJ tiene que responder, o ya respondidos pero todavía no implementados del todo |
| **Pruebas** | `pruebas.html` | Guías paso a paso para probar la aplicación (hoy, las 8 guías `pruebas-N-….html`), con el glosario y lo que todavía no se prueba |
| **Archivo** | `archivo.html` | Documentos **contestados y ya implementados**, en `archivados/`, que se abren con sus respuestas cargadas |
| **Para Orlando** | `orlando.html` | Documentos de trabajo para Orlando, **no para CJ** (explicaciones en lenguaje llano y decisiones de dominio, correo, landing y privacidad), en `orlando/`. Se publican con la misma clave y la misma regla de la carpeta; sus formularios guardan el `.respuestas.json` en `orlando/`. Sus formularios usan **el mismo script que los de CJ** (guardan el fichero, lo cargan solos del repositorio una vez publicado y avisan si es de otra versión); solo cambian la clave del navegador, la marca «Hecho» de las casillas y el aviso al guardar. Los `.md` que se dejaran en esa carpeta viven en el repositorio pero no se publican |

**Regla de archivo:** un documento se archiva cuando CJ lo ha contestado **y** lo que pedía ya está implementado. Lo que quede sin hacer
se pregunta en otro documento de Preguntas, y el archivado lo cita (como hicieron los dos de la segunda tanda con `confirmaciones-segunda-tanda-cj.html`, ya archivado). No se borra nada.

## La clave

Toda la carpeta `/pendientes-cj` (índice, secciones, documentos y respuestas) está detrás de una clave: sin ella, las páginas
redirigen a `/AccesoCj` y los `.respuestas.json` dan 401. Se pide una vez por sesión del navegador (cookie de sesión). La clave es
`PendientesCj:Clave` en `src/ResidApp.Web/appsettings.json` (hoy `CJ123`); se puede sobrescribir con la variable de entorno
`PendientesCj__Clave` en la Web App. Sin clave configurada, no entra nadie. «Salir» está al final del índice (`/AccesoCj/Salir`).

**Límite:** es un freno para la fase de desarrollo, no autenticación real. El repositorio de GitHub es público, así que la clave y los
`.respuestas.json` (decisiones de producto y valores clínicos, sin datos de residentes) también lo son. Antes de usar la aplicación con
datos reales hará falta autenticación de verdad (ver el ADR 0007 de proveedor de identidad).

**No es la clave de la aplicación.** Desde el 2026-10-24 en la aplicación se entra como en un centro real, también en Azure dev: cada
cuenta de prueba de CJ (`database/seed/centros_prueba.sql`) se da de alta con la invitación que le entrega Orlando, y tiene su propia
contraseña y su código del móvil o passkey (ADR 0007). `pruebas.html` («Tus cuentas») se lo explica a CJ. Al cambiar las cuentas y los
residentes de las guías no se subió su `data-version`: los pasos y lo que se comprueba son los mismos, así que sus respuestas siguen valiendo.

## Cómo responderlos (CJ)

1. Abre la web de ResidApp, pulsa «Documentos de CJ» en el pie de página (o entra en `/pendientes-cj/index.html`), escribe la clave y abre
   una sección y el documento.
2. Responde en la propia página: pulsa una opción o escribe tu respuesta, y añade un comentario si quieres.
   Lo que marcas se guarda en ese navegador mientras trabajas.
3. Pulsa «Guardar respuestas» (barra de abajo). Se descarga `<documento>.respuestas.json`.
4. Envíaselo a Orlando por correo o WhatsApp. Si lo cambias y lo vuelves a guardar, envía el nuevo.

Para seguir en otro ordenador: abre el documento y pulsa «Abrir respuestas guardadas» con tu fichero. Si el `.respuestas.json` del documento
ya está en el repositorio (y publicado), el documento lo carga solo al abrirse.

Los `.html` y los `.respuestas.json` de esta carpeta y de `archivados/` se publican con la aplicación en `/pendientes-cj/`
(`ResidApp.Web.csproj` los incluye desde aquí, sin copiarlos), así que llegan a la web con el siguiente despliegue.

## Cómo guardar las respuestas recibidas (ingeniería)

Guarda el `<documento>.respuestas.json` que envíe CJ junto a su `.html` (si ya existía, se sustituye) y haz commit. Es lo que se lee a continuación.
Al publicarse, el documento se abre con esas respuestas cargadas («del repositorio»).

## Cómo archivar un documento (ingeniería)

Cuando el documento está contestado y lo que pedía está implementado:

1. `git mv` del `.html` y del `.respuestas.json` a `archivados/`.
2. En el `.html`, bajo el «eyebrow» de la cabecera, la línea «Archivado el DD/MM/AAAA: lo que respondiste ya está implementado…» (como en los ya archivados; las tablas de este README llevan la fecha como AAAA-MM-DD) y «Archivado» en lugar de «Pendiente de CJ». Arriba de la cabecera, el bloque `<nav class="back">` con «← Documentos para CJ» (`../index.html`) y «Volver a ResidApp» (`/`), con su CSS y su regla de impresión; cópialo de uno ya archivado.
3. Quitarlo de `preguntas.html` y añadirlo a `archivo.html` (con una frase de qué se hizo y dónde sigue lo pendiente), y moverlo en la tabla de abajo. Si `preguntas.html` se queda sin documentos, la ficha «Preguntas» de `index.html` lleva la clase `sin-pendientes` y el texto «Nada pendiente por ahora»; mientras haya documentos de Preguntas, la ficha es la normal.
4. Actualizar las rutas que lo citan en `docs/` (`pendientes-*.md`, `docs/prompts-ia/continuar-construccion.md`).
5. Tras el despliegue, comprobar que la ruta antigua (`/pendientes-cj/<documento>.html`) da 404. No hay que borrar nada a mano en Azure: el despliegue usa `clean: true` (`.github/workflows/ci-cd.yml`) y quita del servidor los ficheros que ya no están en el repositorio.

## Cómo leer las respuestas (ingeniería)

Cada `<documento>.respuestas.json` se entiende sin abrir el HTML:

```json
{
  "documento": "traslado-y-baja-residente",
  "cuestionario": "2026-10-01",
  "guardado": "2026-10-05T08:12:00.000Z",
  "respondidas": 4,
  "total": 6,
  "respuestas": {
    "1.1": { "pregunta": "¿Qué traslados necesita el centro?", "opcion": "A",
             "textoOpcion": "Solo entre unidades del mismo centro.", "texto": "", "respondida": true }
  }
}
```

- `opcion` y `textoOpcion` solo aparecen en las preguntas con opciones; `texto` es la respuesta libre o el comentario.
- Las tablas (rangos, plazos) guardan una entrada por fila con sus campos (`minimo`, `maximo`, `sinRango`, `notas`;
  `plazo`, `plazoPrioritario`, `notas`).
- `cuestionario` es la versión de las preguntas (`data-version` del `<body>`). Si se cambian preguntas ya
  respondidas, sube la versión: el documento avisa a CJ de que sus respuestas son de otra versión.
- Una respuesta no se da por buena hasta revisarla: si algo no queda claro, se pregunta a CJ.

## Cómo añadir un documento nuevo

Copia uno existente y cambia el `<title>`, `data-doc` (el nombre del fichero, sin `.html`), `data-version` y el
contenido de `<div class="page">`. El estilo y el script del final son iguales en todos y no hace falta tocarlos. Cada
pregunta es un elemento con `data-q` (clave única en el JSON); sus campos llevan `data-field`, y las opciones son
`input type="radio"` dentro de `label.choice`. Para agrupar el avance por temas, cada tema es una `section.tema` con `id`. Añade también su entrada a `preguntas.html` (o a `pruebas.html`: listas estáticas de la web) y a la tabla de abajo. Si es un documento de Preguntas y la ficha de `index.html` estaba como «sin pendientes», vuelve a dejarla normal (quita la clase `sin-pendientes` y repón su texto).

Las guías de pruebas (`pruebas-1-auxiliar-enfermeria.html` a `pruebas-8-aislamiento.html`) son una variante: cada pregunta es una **prueba** con pasos y «qué debería ocurrir», y las opciones son A funciona · B con problema · C no funciona · D no probada. Usan el mismo estilo y script; solo cambian los textos del script («Sin probar»/«Probada» y «completadas», y «Pendiente»/«Respondida» en las preguntas con `class="abierta"`). Se dividió la guía única de 82 pruebas (2026-10-13) agrupando por **dependencias**, no solo por tema: las pruebas que usan lo que deja otra van en la misma guía y en el orden en que se hacen, numeradas 1, 2, 3… en cada guía. Cada guía es autónoma: lleva arriba el `<nav class="back">` («← Pruebas», «Documentos para CJ», «Volver a ResidApp»), su «Antes de empezar» con solo las cuentas y residentes que usa, lo que necesita de otra guía y el orden, y en el pie el enlace a la siguiente; lo común a todas (glosario y lo que todavía no se prueba) está en `pruebas.html`. Si cambia una pantalla descrita en un paso, edita la guía, sube su `data-version` y revisa las referencias «guía N, prueba M» de las demás. Si una prueba nueva cambia la numeración de una guía, revisa también esas referencias.

Cada guía tiene además una **página de flujo** (`pruebas-N-…-flujo.html`): un diagrama de carriles con qué perfil hace cada prueba y qué información pasa de un perfil a otro, el mismo recorrido en texto y «Dónde mirar» para los puntos que confunden. No es un cuestionario (no lleva `data-doc` ni script de respuestas) y se enlaza desde la cabecera y el «Antes de empezar» de su guía y desde `pruebas.html`. **Si cambias una guía que tiene flujo, revisa su flujo** (números, perfiles y nombres de botones). Las de las guías 1 y 2 se escribieron a mano; las de la 3 a la 8 (2026-10-14) siguen el mismo estilo y se generaron con un script de un solo uso que no está en el repositorio: para cambiarlas, edita su HTML.

## Documentos

### Preguntas

| Documento | Respuestas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [modulos-base-y-opcionales-cj.html](modulos-base-y-opcionales-cj.html) | `modulos-base-y-opcionales-cj.respuestas.json` | Qué funciones son la base de ResidApp y cuáles podrían ser módulos opcionales cobrados aparte (modelo de suscripción), qué no debe faltar nunca por seguridad del residente y qué debe tener activado el centro piloto (16 respuestas) | Pendiente (creado el 2026-10-29; sirve de base para el interruptor de módulos por centro y para la sección de precios de la landing) |
| [portal-familiar-dos-idiomas-cj.html](portal-familiar-dos-idiomas-cj.html) | `portal-familiar-dos-idiomas-cj.respuestas.json` | Si hace falta el Portal Familiar, las pantallas de entrada y los correos a las familias en catalán además de castellano, cuándo, idioma por defecto del centro, qué hacer con los comunicados que escribe el personal y quién revisa las traducciones (7 respuestas) | Pendiente (creado el 2026-10-29; decisiones de Orlando en el ADR 0012, requisito 7) |
| [portal-familiar-confirmaciones-cj.html](portal-familiar-confirmaciones-cj.html) | `portal-familiar-confirmaciones-cj.respuestas.json` | Lo que ingeniería tuvo que suponer al construir el Portal Familiar: qué ve la familia, derivaciones a Urgencias, privacidad, cómo debe identificarse un familiar, qué pasa al corregir o retirar un comunicado ya publicado y cómo funcionan las citas con reserva directa y con solicitud previa (28 respuestas) | Contestado el 2026-10-25; hecho el 2026-10-25 salvo la 3.2 (código por correo), que espera al ADR 0012. Sigue aquí hasta que se haga la 3.2 |
| [confirmaciones-tercera-tanda-cj.html](confirmaciones-tercera-tanda-cj.html) | `confirmaciones-tercera-tanda-cj.respuestas.json` | El resumen periódico para la familia y la finalidad de calidad asistencial de Dirección, y confirmaciones de Dirección (registro, nombres, comunicados, frecuencia), del inicio de Administración, de los episodios y traslados y de los avisos a la familia; 21 preguntas en 6 temas | Contestado el 2026-10-25; hecho salvo la 6.1 (aviso de propuesta de cita por correo) y la 6.2 (recordatorio el día antes), que esperan al ADR 0012. Sigue aquí hasta que se hagan |

### Pruebas

| Documento | Respuestas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [pruebas-1-auxiliar-enfermeria.html](pruebas-1-auxiliar-enfermeria.html) | `pruebas-1-auxiliar-enfermeria.respuestas.json` | Guía 1: Auxiliar y Enfermería (13 pruebas, 4 imprescindibles) | Pendiente |
| [pruebas-2-medicina-indicaciones.html](pruebas-2-medicina-indicaciones.html) | `pruebas-2-medicina-indicaciones.respuestas.json` | Guía 2: escalado a Medicina, indicaciones y cierre del episodio (13 pruebas, 6 imprescindibles) | Pendiente |
| [pruebas-3-basal-barthel.html](pruebas-3-basal-barthel.html) | `pruebas-3-basal-barthel.respuestas.json` | Guía 3: basal y Barthel (7 pruebas, 3 imprescindibles) | Pendiente |
| [pruebas-4-direccion.html](pruebas-4-direccion.html) | `pruebas-4-direccion.respuestas.json` | Guía 4: Dirección Clínica, con la consulta auditada del expediente (9 pruebas, 3 imprescindibles) | Pendiente |
| [pruebas-5-administracion.html](pruebas-5-administracion.html) | `pruebas-5-administracion.respuestas.json` | Guía 5: Administración del centro (16 pruebas, 3 imprescindibles) | Pendiente |
| [pruebas-6-comunicados-portal.html](pruebas-6-comunicados-portal.html) | `pruebas-6-comunicados-portal.respuestas.json` | Guía 6: comunicados a la familia y Portal Familiar (14 pruebas, 5 imprescindibles) | Pendiente |
| [pruebas-7-citas.html](pruebas-7-citas.html) | `pruebas-7-citas.respuestas.json` | Guía 7: citas con la familia e inicio de Administración (9 pruebas, 4 imprescindibles) | Pendiente |
| [pruebas-8-aislamiento.html](pruebas-8-aislamiento.html) | `pruebas-8-aislamiento.respuestas.json` | Guía 8: aislamiento entre centros (5 pruebas, 2 imprescindibles) y 3 preguntas finales | Pendiente |

### Para Orlando (no son de CJ)

| Documento | Respuestas | Qué hay | Estado |
| --- | --- | --- | --- |
| [orlando/dominio-y-correo.html](orlando/dominio-y-correo.html) | `orlando/dominio-y-correo.respuestas.json` | Guía paso a paso de dominio, certificado y correo (ACS Email) con un formulario de 7 decisiones y 6 casillas de lo hecho | Pendiente de Orlando (actualizada el 2026-10-29) |
| [orlando/analisis-landing-page.html](orlando/analisis-landing-page.html) | — | Análisis de la landing: frente a la aplicación, dominios, precios y planes, FAQ y chat, lanzamiento y frases prudentes. Documento para leer, sin formulario | Actualizado el 2026-10-29 |
| [orlando/adr-0007-passkeys-por-entorno.html](orlando/adr-0007-passkeys-por-entorno.html) | `orlando/adr-0007-passkeys-por-entorno.respuestas.json` | Borrador de cambio del punto 5 del ADR 0007 (passkeys en el subdominio exacto de cada entorno), explicado en lenguaje llano, con opciones y un formulario de 4 decisiones | Pendiente de Orlando (creado el 2026-10-29); el ADR no se modifica hasta que responda |

### Archivo (contestados e implementados)

| Documento | Respuestas | Qué se pidió | Estado |
| --- | --- | --- | --- |
| [archivados/direccion-y-traslados-confirmaciones-cj.html](archivados/direccion-y-traslados-confirmaciones-cj.html) | `archivados/direccion-y-traslados-confirmaciones-cj.respuestas.json` | Nueve detalles que ingeniería tuvo que suponer al construir lo pedido sobre Dirección y traslados (2026-10-26): el cierre con indicaciones sin cumplir, el permiso de calidad asistencial y el historial de un residente trasladado (9 respuestas) | Contestado el 2026-10-28 y archivado: Calidad sin nombres, constancia de consulta, motivo con lista cerrada y lista de peticiones |
| [archivados/portal-familiar-cuarta-tanda-cj.html](archivados/portal-familiar-cuarta-tanda-cj.html) | `archivados/portal-familiar-cuarta-tanda-cj.respuestas.json` | Tres dudas que salieron al construir las respuestas del Portal Familiar (2026-10-25): citas y solicitudes de un residente suspendido o de baja, huecos que se pueden proponer a menos de 24 horas y qué cuenta como nuevo en la primera visita (3 respuestas) | Contestado el 2026-10-28 y archivado: la baja cancela las citas futuras y cierra las solicitudes abiertas |
| [archivados/organigrama-y-cargos-cj.html](archivados/organigrama-y-cargos-cj.html) | `archivados/organigrama-y-cargos-cj.respuestas.json` | Ocho detalles para construir el organigrama (ADM-07), aplazado el 2026-10-07: quién ocupa un cargo, cargos fijos o por centro, personas por cargo, coordinación funcional, unidades y equipos, historial y quién lo ve (8 respuestas) | Contestado el 2026-10-25; hecho y archivado el 2026-10-26 |
| [archivados/importacion-exportacion-confirmaciones-cj.html](archivados/importacion-exportacion-confirmaciones-cj.html) | `archivados/importacion-exportacion-confirmaciones-cj.respuestas.json` | Lo supuesto al construir la importación y la exportación de estructura y residentes (2026-10-21, estudio de mercado R1, ADR 0010): fecha de alta del importado, criterio de «ya existe», nombre y apellidos, sexo que falta, familiares, datos del censo exportado, quién importa y exporta, y si se estudia exportar los registros asistenciales; 8 preguntas en 3 temas | Contestado el 2026-10-25; hecho y archivado el 2026-10-26 |
| [archivados/foto-residente-confirmaciones-cj.html](archivados/foto-residente-confirmaciones-cj.html) | `archivados/foto-residente-confirmaciones-cj.respuestas.json` | Lo supuesto al poner la foto y la habitación en las listas de residentes (2026-10-20, estudio de mercado R2): consentimiento, quién pone y quién ve la foto, conservación, auditoría de las vistas y si la cama debe ser obligatoria en el alta; 8 preguntas en 3 temas | Contestado el 2026-10-25; archivado el 2026-10-25 (hecho, script `0071`) |
| [archivados/dictado-por-voz-confirmaciones-cj.html](archivados/dictado-por-voz-confirmaciones-cj.html) | `archivados/dictado-por-voz-confirmaciones-cj.respuestas.json` | Dictado por voz en «Registrar cambio» (ADR 0009 y 0011): jerga de planta, en qué área va cada cosa, rango de la temperatura, qué se revisa antes de guardar, nombres al dictar y los 31 ejemplos (15 respuestas) | Archivado el 2026-10-25 (opciones nuevas, temperatura 34–42 °C, cambio con solo temperatura, prompt v3) |
| [archivados/rangos-referencia-constantes.html](archivados/rangos-referencia-constantes.html) | `archivados/rangos-referencia-constantes.respuestas.json` | Mínimo y máximo de las 7 constantes con aviso visual, y 5 preguntas abiertas | Archivado 2026-10-07: hechos los valores sugeridos, el flujo de O₂ y el aviso de temperatura de la Auxiliar; lo pendiente, en «Aclaraciones» |
| [archivados/decisiones-direccion-basal-derivacion.html](archivados/decisiones-direccion-basal-derivacion.html) | `archivados/decisiones-direccion-basal-derivacion.respuestas.json` | Finalidades de la lectura clínica de Dirección, aportación a un borrador de basal ajeno y el campo «Comunicaciones» en el informe de derivación (7 respuestas) | Archivado 2026-10-07: hechos las finalidades y la justificación de Dirección y «Comunicaciones»; las aportaciones al basal, en «Aclaraciones» |
| [archivados/aclaraciones-respuestas-cj.html](archivados/aclaraciones-respuestas-cj.html) | `archivados/aclaraciones-respuestas-cj.respuestas.json` | Aclaraciones a las respuestas de rangos y de Dirección, basal y derivación (11 respuestas) | Archivado 2026-10-07: hechos los valores de temperatura y glucemia, la saturación con oxígeno, el aviso de la Auxiliar retirado, los rangos solo para Dirección / Coordinación y las aportaciones al basal descartadas |
| [archivados/confirmaciones-segunda-tanda-cj.html](archivados/confirmaciones-segunda-tanda-cj.html) | `archivados/confirmaciones-segunda-tanda-cj.respuestas.json` | Lo que ingeniería tuvo que suponer al construir la segunda tanda: vincular familiares, contactos urgentes, Administración principal, pendientes y avisos, y comunicados a la familia (12 respuestas) | Archivado 2026-10-11: hechos vincular un familiar de todo el centro, residente sin contacto urgente (con motivo), horario de comunicados por centro, corregir hasta publicar, Dirección que publica antes, comunicados de Medicina y el cierre del episodio por Enfermería al cumplirse las indicaciones |
| [archivados/administracion-ambito-familiares-cargos.html](archivados/administracion-ambito-familiares-cargos.html) | `archivados/administracion-ambito-familiares-cargos.respuestas.json` | Unidades que ninguna Administración tiene, desvincular y compartir familiares, y organigrama y cargos (7 respuestas) | Archivado 2026-10-08: hechas la Administración principal (y desde soporte), desvincular con motivo, datos de un familiar compartido y familiares del alta; el organigrama, aplazado y guardado; lo supuesto, en «Confirmaciones» |
| [archivados/continuidad-supervision-comunicacion.html](archivados/continuidad-supervision-comunicacion.html) | `archivados/continuidad-supervision-comunicacion.respuestas.json` | Recepción de seguimientos, derivaciones de Dirección, seguimientos pendientes, plazos de los hitos del proceso y comunicación familiar (16 respuestas) | Archivado 2026-10-08: hechos la bandeja común, las derivaciones con periodo, «pendientes», los siete plazos con calidad de proceso y avisos, y los comunicados (aprobar al guardar, margen, hora fija y publicación anticipada); falta el Portal Familiar y DIR-13; lo supuesto, en «Confirmaciones» |
| [archivados/traslado-y-baja-residente.html](archivados/traslado-y-baja-residente.html) | `archivados/traslado-y-baja-residente.respuestas.json` | Traslado entre unidades, baja y reactivación del residente (6 respuestas) | Archivado 2026-10-07: hechos el traslado por Administración, la baja, la lista de bajas, la reactivación y la suspensión por ingreso hospitalario; queda el traslado por Enfermería con permiso, para cuando un centro lo pida |
