# Pendientes de CJ

Documentos que CJ tiene que completar: valores clínicos y decisiones de producto que ingeniería no puede
inventar. Cuando CJ los completa, ingeniería revisa las respuestas y las incorpora a la aplicación.

## Las secciones (dos para CJ y una para Orlando)

La web (`/pendientes-cj/index.html`) tiene dos secciones para CJ y una de trabajo para Orlando, cada una con su página:

| Sección | Página | Qué hay |
| --- | --- | --- |
| **Preguntas** | `preguntas.html` | Documentos con decisiones y valores que CJ tiene que responder, o ya respondidos pero todavía no implementados del todo |
| **Pruebas** | `pruebas.html` | Guías paso a paso para probar la aplicación (hoy, las 8 guías `pruebas-N-….html`), con el glosario y lo que todavía no se prueba |
| **Para Orlando** | `orlando.html` | Documentos de trabajo para Orlando, **no para CJ** (explicaciones en lenguaje llano y decisiones de dominio, correo, landing y privacidad), en `orlando/`. Se publican con la misma clave y la misma regla de la carpeta. El documento `orlando/decisiones-medicacion-y-plazos.html` no tiene formulario: se decide en la sesión (una pregunta cada vez) para que las respuestas no acaben en el repositorio público. Sus formularios siguen con **el script de antes** (descargan el `.respuestas.json`, que se guarda en `orlando/`, lo cargan solos del repositorio una vez publicado y avisan si es de otra versión); solo cambian la clave del navegador, la marca «Hecho» de las casillas y el aviso al guardar. Los `.md` que se dejaran en esa carpeta viven en el repositorio pero no se publican |

**Regla de cierre (sin archivo):** cuando CJ ha contestado **y** lo que pedía ya está implementado, la pregunta se **quita** del documento (y, si no queda ninguna, el documento entero). Ya no hay sección Archivo ni carpeta `archivados/`: se retiraron el 2026-11-11 porque el repositorio es público y llevaban las respuestas de CJ. Lo que quede sin hacer se pregunta en otro documento. Lo anterior queda en el historial de git. Las respuestas de CJ no se tocan: siguen en el servidor.

**Citas a `archivados/…` en otros documentos.** Los ADR, `docs/tareas/`, `docs/privacidad/` y `tools/PruebaDictado/README.md` siguen citando rutas `docs/pendientes-cj/archivados/<documento>.html` o `.respuestas.json`: son citas históricas (qué se preguntó a CJ y cuándo) y esos ficheros ya no existen. Para ver uno, tal como estaba al retirarlos: `git show 571c6cb:docs/pendientes-cj/archivados/<documento>.html` (o `.respuestas.json`), o el zip privado de Orlando. Los valores que decidió CJ ya están recogidos en `docs/flujos-clinicos/` y en el código (por ejemplo, los rangos de las constantes en `src/ResidApp.Domain/Enfermeria/VitalSignReferenceRanges.cs`); no se reescriben esas citas para no tocar texto ya cerrado. Desde el 2026-11-13, cada fichero que las cita lleva bajo su título una pista que remite aquí, y los enlaces Markdown que ya no llevaban a ningún sitio se convirtieron en texto.

## La clave

Toda la carpeta `/pendientes-cj` (índice, secciones, documentos y respuestas) está detrás de una clave: sin ella, las páginas
redirigen a `/AccesoCj` y los `.json` y la API de respuestas dan 401. Se pide una vez por sesión del navegador (cookie de sesión). La clave es
`PendientesCj:Clave` en `src/ResidApp.Web/appsettings.json` (hoy `CJ123`); se puede sobrescribir con la variable de entorno
`PendientesCj__Clave` en la Web App. Sin clave configurada, no entra nadie. «Salir» está al final del índice (`/AccesoCj/Salir`).

**Límite:** es un freno para la fase de desarrollo, no autenticación real. El repositorio de GitHub es público, así que la clave también lo es. Las respuestas de CJ **no** están en el repositorio (viven en el servidor), pero quien tenga la clave puede leerlas y escribirlas. Antes de usar la aplicación con
datos reales hará falta autenticación de verdad (ver el ADR 0007 de proveedor de identidad). Este sistema es solo de desarrollo: el entorno de producción no llevará `/pendientes-cj` (ADR 0014).

**No es la clave de la aplicación.** Desde el 2026-10-24 en la aplicación se entra como en un centro real, también en Azure dev: cada
cuenta de prueba de CJ (`database/seed/centros_prueba.sql`) la dio de alta Orlando el 2026-11-03, con una contraseña única y una clave del
código del móvil únicas para todo el personal (excepción solo de Azure dev, con datos ficticios, que nunca se aplica a un centro real, y sin escribirlas en el repositorio; ADR 0007). `pruebas.html` («Tus cuentas») se lo explica a CJ. Al cambiar las cuentas y los
residentes de las guías no se subió su `data-version`: los pasos y lo que se comprueba son los mismos, así que sus respuestas siguen valiendo.

## Cómo responderlos (CJ)

1. Abre la web de ResidApp, pulsa «Documentos de CJ» en el pie de página (o entra en `/pendientes-cj/index.html`), escribe la clave y abre
   una sección y el documento.
2. Responde en la propia página: pulsa una opción o escribe tu respuesta, y añade un comentario si quieres (sin datos personales reales).
3. **No hay que guardar ni enviar nada.** Unos segundos después de cada cambio, la página guarda tus respuestas en el servidor y lo dice abajo («Guardado en el servidor» con la hora). Si no hay conexión, avisa y lo reintenta; lo marcado se conserva también en ese navegador.
4. Para seguir en otro ordenador, abre el documento: recupera lo último que se guardó.

Ingeniería recibe un aviso por correo cuando CJ deja de responder un rato (ver «Aviso a Orlando»). Debajo de una respuesta puede aparecer una **nota de ingeniería**; si no hay, es que no hace falta más comentario. Cuando se despliegan notas nuevas, CJ recibe un correo (ver «Aviso a CJ»).

Los `.html` y los `.notas.json` de esta carpeta se publican con la aplicación en `/pendientes-cj/` (`ResidApp.Web.csproj` los incluye desde aquí, sin copiarlos), así que llegan a la web con el siguiente despliegue.

## Dónde están las respuestas (ingeniería)

- **En el servidor, no en el repositorio.** Un fichero JSON por documento, `<documento>.respuestas.json`, que cada guardado **sustituye** (sin historial; se conserva solo `<documento>.respuestas.anterior.json`, la versión de antes). Carpeta: `PendientesCj:CarpetaRespuestas`; por defecto `/home/data/pendientes-cj` en Linux (App Service conserva `/home` entre despliegues) y la carpeta de datos locales del usuario en Windows. Código: `RespuestasCjStore`, `RespuestasCjController` (API `/pendientes-cj/api/respuestas/<documento>`, detrás de la misma clave).
- **Bajarlas para analizarlas:** `.\tools\respuestas-cj.ps1` (PowerShell; pide la clave de CJ o la lee de `RESIDAPP_CLAVE_CJ`) deja un `<documento>.respuestas.json` por documento respondido en `respuestas-cj-local\` (carpeta **ignorada por git**). Es lo que se lee en la sesión de análisis, y sirve de **copia de seguridad**: no hay historial, así que conviene ejecutarlo antes de cada análisis y antes de un despliegue que toque esto. `-Url` apunta a otro servidor (por defecto `https://dev.residapp.es`).
- **Subir respuestas antiguas:** `.\tools\respuestas-cj.ps1 -Accion Subir` manda los `.respuestas.json` de `respuestas-cj-local\` al servidor; no pisa lo que ya haya (salvo `-Forzar`). Hizo falta una vez, al retirar los `.respuestas.json` del repositorio.
- **Nunca commitees las respuestas de CJ** ni las pegues en documentos o PR: el repositorio es público.

## Aviso a Orlando

Cuando CJ guarda algo, la app lo anota; cuando lleva `PendientesCj:SilencioAvisoMinutos` (10 por defecto) sin cambiar nada, manda **un solo correo** con los documentos cambiados y «N de M completadas» (nunca las respuestas). Usa el correo de la aplicación (ACS, ADR 0012) y la tarea `AvisoRespuestasCjWorker` (la Web App debe estar «Siempre activo»). Se configura en la Web App (no en el repositorio, que es público): `PendientesCj__AvisarA` (la dirección de Orlando; sin ella no avisa), y `Correo__Activo`, `Correo__CadenaConexion`, `Correo__UrlBase` y `Correo__DireccionesPermitidas__0` (debe incluir esa dirección; es una lista) como el resto de avisos. Un fallo del envío se registra en el log y se reintenta tras otro silencio.

## Aviso a CJ

Cuando se despliega **un documento nuevo con preguntas** (un `.html` de la raíz con `data-doc`; no cuentan el índice, las secciones, las páginas de flujo ni `orlando/`) o una nota nueva o cambiada (`<documento>.notas.json`), la app manda **un solo correo a CJ** (`AvisoNotasCjWorker`, `AvisoNotasCj`) con el título y el enlace de cada documento afectado; nunca el texto de las notas ni sus respuestas. Compara la huella del texto de cada nota con la última avisada (`avisos-notas.json`, en la carpeta de las respuestas: solo huellas, ni la dirección ni el texto); retirar una nota no avisa, aunque el documento conserve otras, y un fichero de notas que no se puede leer en ese momento no hace olvidar lo ya avisado. Los documentos nuevos se detectan con una marca por documento en ese mismo estado (`<documento>/#documento`): si el correo avisa de un documento nuevo, el asunto lo dice («Hay 1 documento nuevo con preguntas para ti») y, si a la vez hay notas, es un solo correo («Novedades en tus documentos»). Un estado anterior a esta marca da por conocidos, sin correo, los documentos que ya había (la primera vuelta tras desplegarlo aprende); **por eso este cambio debe desplegarse antes que el documento nuevo que se quiera avisar**. Retirar un documento al implementarlo no avisa; volver a publicarlo después sí. Si el envío falla, reintenta pasados 10 minutos.

- Se configura en la Web App con `PendientesCj__AvisarACj` (la dirección de CJ; sin ella no avisa) y esa dirección debe estar también en `Correo__DireccionesPermitidas__N`. **No pongas su dirección sin que CJ haya aceptado por escrito** (un mensaje basta), y retírala cuando acabe su colaboración. Mejor una dirección personal o de proyecto que la del hospital (lectura del DPO: `docs/privacidad/aviso-cj-notas-lectura-dpo.md`).
- El asunto y el cuerpo no nombran a CJ ni a Orlando (se ven en pantallas de bloqueo); solo el pie dice a quién decírselo para dejar de recibirlo (Orlando). El correo no pide la clave: dice que ResidApp nunca la pide por correo.
- Si el paquete no lleva los documentos de CJ (como en producción, ADR 0014), el aviso no hace nada. Ojo: hoy `Program.cs` crea un `PhysicalFileProvider` sobre esa carpeta, que falla si no existe; al excluir `/pendientes-cj` de producción hay que protegerlo (parte del incremento del ADR 0014).
- Con el estado vacío (carpeta nueva) avisa de todas las notas que haya: un correo de más, nunca uno de menos.

## Notas de ingeniería (retroalimentación)

Cada documento tiene un `<documento>.notas.json` (`{"documento": "<documento>", "notas": {"<data-q>": "texto"}}`) que se despliega con la aplicación. La página pinta cada nota, de solo lectura y como texto plano, bajo su pregunta; sin nota, no se muestra nada. Se escriben en la sesión de análisis de las respuestas (Claude propone, Orlando revisa). Las notas **son públicas** (van en el repositorio): no citan datos personales ni copian los comentarios de CJ; analizan y preguntan. Un documento sin fichero de notas da un 404 en la consola del navegador: crea siempre el suyo (con `"notas": {}`). Al desplegar notas nuevas, CJ recibe el aviso por correo (ver «Aviso a CJ»). Tener `.html` y `.notas.json` en la raíz es lo que hace válido a un documento para la API de respuestas (los índices y las páginas de flujo no lo son).

## Cómo cerrar lo implementado (ingeniería)

1. Baja las respuestas (`tools\respuestas-cj.ps1`; la ejecuta Claude cuando Orlando le avisa de que ha recibido el correo, con la clave del fichero local `~\.residapp\clave-cj.txt`, que el script lee y Claude nunca ve), léelas y, donde haga falta, escribe la nota en el `.notas.json`.
2. Cuando lo que pedía una pregunta ya está implementado: **quita la pregunta** del HTML y su nota. Cambia lo que CJ contesta, así que sube el `data-version` del documento (**con el visto bueno de Orlando**). Las respuestas viejas siguen en el servidor.
3. Si el documento se queda sin preguntas: quita el `.html` y su `.notas.json`, su entrada de `preguntas.html` o `pruebas.html` y de la tabla de abajo. Si `preguntas.html` se queda sin documentos, la ficha «Preguntas» de `index.html` lleva la clase `sin-pendientes` y el texto «Nada pendiente por ahora»; mientras haya documentos de Preguntas, la ficha es la normal.
4. Actualiza las rutas que lo citan en `docs/` (`pendientes-*.md`, `docs/prompts-ia/continuar-construccion.md`).
5. Tras el despliegue, comprueba que la ruta antigua (`/pendientes-cj/<documento>.html`) da 404. No hay que borrar nada a mano en Azure: el despliegue usa `clean: true` (`.github/workflows/ci-cd.yml`) y quita del servidor los ficheros que ya no están en el repositorio (no toca la carpeta de respuestas, que está fuera del paquete).

Los documentos de la sección «Para Orlando» (`orlando/`) se quitan igual cuando están hechos (se quitan también de `orlando.html` y se corrigen los enlaces de otros documentos de `orlando/`).

## Cómo leer las respuestas (ingeniería)

Cada `<documento>.respuestas.json` (el del servidor o el bajado a `respuestas-cj-local\`) se entiende sin abrir el HTML:

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
contenido de `<div class="page">`. El estilo y el script del final son iguales en todos y no hace falta tocarlos (guardan solos en el servidor y pintan las notas). Crea también su `<documento>.notas.json` con `{"documento": "<documento>", "notas": {}}`. Cada
pregunta es un elemento con `data-q` (clave única en el JSON); sus campos llevan `data-field`, y las opciones son
`input type="radio"` dentro de `label.choice`. Para agrupar el avance por temas, cada tema es una `section.tema` con `id`. Añade también su entrada a `preguntas.html` (o a `pruebas.html`: listas estáticas de la web) y a la tabla de abajo. Si es un documento de Preguntas y la ficha de `index.html` estaba como «sin pendientes», vuelve a dejarla normal (quita la clase `sin-pendientes` y repón su texto).

Las guías de pruebas (`pruebas-1-auxiliar-enfermeria.html` a `pruebas-8-aislamiento.html`) son una variante: cada pregunta es una **prueba** con pasos y «qué debería ocurrir», y las opciones son A funciona · B con problema · C no funciona · D no probada. Usan el mismo estilo y script; solo cambian los textos del script («Sin probar»/«Probada» y «completadas», y «Pendiente»/«Respondida» en las preguntas con `class="abierta"`). Se dividió la guía única de 82 pruebas (2026-10-13) agrupando por **dependencias**, no solo por tema: las pruebas que usan lo que deja otra van en la misma guía y en el orden en que se hacen, numeradas 1, 2, 3… en cada guía. Cada guía es autónoma: lleva arriba el `<nav class="back">` («← Pruebas», «Documentos para CJ», «Volver a ResidApp»), su «Antes de empezar» con solo las cuentas y residentes que usa, lo que necesita de otra guía y el orden, y en el pie el enlace a la siguiente; lo común a todas (glosario y lo que todavía no se prueba) está en `pruebas.html`. Si cambia una pantalla descrita en un paso, edita la guía, sube su `data-version` y revisa las referencias «guía N, prueba M» de las demás. Si una prueba nueva cambia la numeración de una guía, revisa también esas referencias. **El sello «Revisado para ResidApp vX.Y.Z»** de la línea `meta` de cada guía y de cada flujo significa que alguien los repasó de punta a punta contra esa versión de la app (`src\Directory.Build.props`; el registro `.claude\documentacion-usuario.json` guarda la misma cifra en `revisadoPara`). No dice que cambiaran los pasos: ponerlo o actualizarlo **no** sube el `data-version`, que solo cambia cuando cambia lo que CJ responde.

Cada guía tiene además una **página de flujo** (`pruebas-N-…-flujo.html`): un diagrama de carriles con qué perfil hace cada prueba y qué información pasa de un perfil a otro, el mismo recorrido en texto y «Dónde mirar» para los puntos que confunden. No es un cuestionario (no lleva `data-doc` ni script de respuestas) y se enlaza desde la cabecera y el «Antes de empezar» de su guía y desde `pruebas.html`. **Si cambias una guía que tiene flujo, revisa su flujo** (números, perfiles y nombres de botones). Las de las guías 1 y 2 se escribieron a mano; las de la 3 a la 8 (2026-10-14) siguen el mismo estilo y se generaron con un script de un solo uso que no está en el repositorio: para cambiarlas, edita su HTML.

## Documentos

### Preguntas

| Documento | Notas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [guia-cumplimiento-datos-cj.html](guia-cumplimiento-datos-cj.html) | `guia-cumplimiento-datos-cj.notas.json` | Versión para leer de la guía de cumplimiento de protección de datos (la completa está en `docs/privacidad/guia-cumplimiento-propietarios.md`, que manda; si cambia una, revisa la otra) y 10 preguntas: confirmar lo decidido por Orlando el 2026-11-13 (búsqueda de la persona experta, sociedad antes de firmar, aviso de brecha en 24 h, plantilla de información), la aceptación de CJ como suplente de contacto de privacidad y de la confidencialidad, su disponibilidad y lo que sabe del centro piloto | Pendiente. Vive aquí (no en `orlando/`) para que sus respuestas se guarden en el servidor; `orlando.html` la enlaza. Al desplegarla, CJ recibe el aviso de documento nuevo |
| [iconos-pantallas-cj.html](iconos-pantallas-cj.html) | `iconos-pantallas-cj.notas.json` | Si los iconos de menú, inicios, títulos, registro del Auxiliar, estado basal y Portal Familiar ayudan y se entienden; si el icono de «Sin cambios» puede sesgar el registro; dónde dejar los iconos de las nueve áreas del basal; si los del portal son adecuados | Pendiente |
| [direccion-comunicados-flujo-cj.html](direccion-comunicados-flujo-cj.html) | `direccion-comunicados-flujo-cj.notas.json` | Aviso del cambio de redacción del flujo de Dirección (puede adelantar, corregir y retirar comunicados a la familia, decisión de CJ del 10/10/2026) y dos confirmaciones: si sigue de acuerdo y si el texto de una corrección lo escribe la propia Dirección | Pendiente |
| [modulos-base-y-opcionales-cj.html](modulos-base-y-opcionales-cj.html) | `modulos-base-y-opcionales-cj.notas.json` | Qué funciones son la base de ResidApp y cuáles podrían ser módulos opcionales cobrados aparte (modelo de suscripción), qué no debe faltar nunca por seguridad del residente y qué debe tener activado el centro piloto (16 respuestas) | Contestado el 2026-10-30 (todo es base salvo exportar la estructura/censo y el dictado por voz, que serían opcionales; en el centro piloto, todo activado); sigue aquí hasta construir el interruptor de módulos por centro y la sección de precios de la landing |
| [portal-familiar-dos-idiomas-cj.html](portal-familiar-dos-idiomas-cj.html) | `portal-familiar-dos-idiomas-cj.notas.json` | Si hace falta el Portal Familiar, las pantallas de entrada y los correos a las familias en catalán además de castellano, cuándo, idioma por defecto del centro, qué hacer con los comunicados que escribe el personal y quién revisa las traducciones (7 respuestas) | Contestado el 2026-10-30 (sí, muchas familias; antes del primer centro con familias reales; castellano por defecto; revisa CJ); ADR 0013 aceptado el mismo día. Sigue aquí hasta construir el portal en catalán |
| [portal-familiar-confirmaciones-cj.html](portal-familiar-confirmaciones-cj.html) | `portal-familiar-confirmaciones-cj.notas.json` | Lo que ingeniería tuvo que suponer al construir el Portal Familiar: qué ve la familia, derivaciones a Urgencias, privacidad, cómo debe identificarse un familiar, qué pasa al corregir o retirar un comunicado ya publicado y cómo funcionan las citas con reserva directa y con solicitud previa (28 respuestas) | Contestado el 2026-10-25; hecho el 2026-10-25 salvo la 3.2 (código por correo), que espera al ADR 0012. Sigue aquí hasta que se haga la 3.2 |
| [confirmaciones-tercera-tanda-cj.html](confirmaciones-tercera-tanda-cj.html) | `confirmaciones-tercera-tanda-cj.notas.json` | El resumen periódico para la familia y la finalidad de calidad asistencial de Dirección, y confirmaciones de Dirección (registro, nombres, comunicados, frecuencia), del inicio de Administración, de los episodios y traslados y de los avisos a la familia; 21 preguntas en 6 temas | Contestado el 2026-10-25; hecho salvo la 6.1 (aviso de propuesta de cita por correo) y la 6.2 (recordatorio el día antes), que esperan al ADR 0012. Sigue aquí hasta que se hagan |

### Pruebas

| Documento | Notas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [pruebas-1-auxiliar-enfermeria.html](pruebas-1-auxiliar-enfermeria.html) | `pruebas-1-auxiliar-enfermeria.notas.json` | Guía 1: Auxiliar y Enfermería (13 pruebas, 4 imprescindibles) | Pendiente |
| [pruebas-2-medicina-indicaciones.html](pruebas-2-medicina-indicaciones.html) | `pruebas-2-medicina-indicaciones.notas.json` | Guía 2: escalado a Medicina, indicaciones y cierre del episodio (13 pruebas, 6 imprescindibles) | Pendiente |
| [pruebas-3-basal-barthel.html](pruebas-3-basal-barthel.html) | `pruebas-3-basal-barthel.notas.json` | Guía 3: basal y Barthel (7 pruebas, 3 imprescindibles) | Pendiente |
| [pruebas-4-direccion.html](pruebas-4-direccion.html) | `pruebas-4-direccion.notas.json` | Guía 4: Dirección Clínica, con la consulta auditada del expediente (10 pruebas, 3 imprescindibles) | Pendiente |
| [pruebas-5-administracion.html](pruebas-5-administracion.html) | `pruebas-5-administracion.notas.json` | Guía 5: Administración del centro (19 pruebas, 3 imprescindibles) | Pendiente |
| [pruebas-6-comunicados-portal.html](pruebas-6-comunicados-portal.html) | `pruebas-6-comunicados-portal.notas.json` | Guía 6: comunicados a la familia y Portal Familiar (14 pruebas, 5 imprescindibles) | Pendiente |
| [pruebas-7-citas.html](pruebas-7-citas.html) | `pruebas-7-citas.notas.json` | Guía 7: citas con la familia e inicio de Administración (9 pruebas, 4 imprescindibles) | Pendiente |
| [pruebas-8-aislamiento.html](pruebas-8-aislamiento.html) | `pruebas-8-aislamiento.notas.json` | Guía 8: aislamiento entre centros (5 pruebas, 2 imprescindibles) y 3 preguntas finales | Pendiente |

### Para Orlando (no son de CJ)

| Documento | Respuestas | Qué hay | Estado |
| --- | --- | --- | --- |
| [orlando/dominio-y-correo.html](orlando/dominio-y-correo.html) | `orlando/dominio-y-correo.respuestas.json` | Guía paso a paso de dominio, certificado y correo (ACS Email) con un formulario de 7 decisiones y 6 casillas de lo hecho | Contestado el 2026-10-30 (13/13) y hecho salvo B10 (registros de actividad de Azure) y DMARC en cuarentena; Microsoft 365 y `app.residapp.es`, para antes del primer centro real (el seguimiento de B10 y DMARC está en `aviso-correo-decisiones`) |
| [orlando/aviso-correo-decisiones.html](orlando/aviso-correo-decisiones.html) | `orlando/aviso-correo-decisiones.respuestas.json` | Lo que falta por decidir del aviso de cita por correo: 6 decisiones de Orlando (titular de ResidApp en el pie, conservación del registro, registros de Azure B10, DMARC, prueba real, persona experta) y una tabla de 9 puntos para llevar al DPO o abogado | Contestado el 2026-11-02 (6/6: sin línea de titular en el pie mientras sean datos de prueba; registro de envíos 12 meses como tope; Azure B10 sin seguimiento y con registros a 30 días en la UE; DMARC a cuarentena antes de producción; prueba real ya hecha; la persona experta se busca antes del primer centro real). La purga a los 12 meses ya está hecha (script `0085`). Sigue aquí hasta hacer lo que queda para producción (titular, B10, DMARC) |
| [orlando/hoja-de-acceso-cj.html](orlando/hoja-de-acceso-cj.html) | (sin formulario) | Plantilla **en blanco** de la hoja que se entrega a CJ con sus cuentas de prueba: instalar Microsoft Authenticator, añadir la clave, entrar, qué guías hacer y en qué orden. Orlando teclea en la propia página la dirección, la contraseña, la clave del móvil y su contacto, y la guarda como PDF con «Imprimir»; los datos no se guardan ni se envían. Enlazada desde `orlando.html` | Creada el 2026-11-03. **Nunca guardes en el repositorio la hoja ya rellena** (lleva la contraseña y la clave) |
