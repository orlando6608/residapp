# Pendientes de CJ

Documentos que CJ tiene que completar: valores clínicos y decisiones de producto que ingeniería no puede
inventar. Cuando CJ los completa, ingeniería revisa las respuestas y las incorpora a la aplicación.

## Las tres secciones

La web (`/pendientes-cj/index.html`) tiene tres secciones, cada una con su página:

| Sección | Página | Qué hay |
| --- | --- | --- |
| **Preguntas** | `preguntas.html` | Documentos con decisiones y valores que CJ tiene que responder, o ya respondidos pero todavía no implementados del todo |
| **Pruebas** | `pruebas.html` | Guías paso a paso para probar la aplicación (hoy, `guia-de-pruebas-cj.html`) |
| **Archivo** | `archivo.html` | Documentos **contestados y ya implementados**, en `archivados/`, que se abren con sus respuestas cargadas |

**Regla de archivo:** un documento se archiva cuando CJ lo ha contestado **y** lo que pedía ya está implementado. Lo que quede sin hacer
se pregunta en otro documento de Preguntas (por ejemplo, `administracion-ambito-familiares-cargos.html`), y el archivado lo cita. No se borra nada.

## La clave

Toda la carpeta `/pendientes-cj` (índice, secciones, documentos y respuestas) está detrás de una clave: sin ella, las páginas
redirigen a `/AccesoCj` y los `.respuestas.json` dan 401. Se pide una vez por sesión del navegador (cookie de sesión). La clave es
`PendientesCj:Clave` en `src/ResidApp.Web/appsettings.json` (hoy `CJ123`); se puede sobrescribir con la variable de entorno
`PendientesCj__Clave` en la Web App. Sin clave configurada, no entra nadie. «Salir» está al final del índice (`/AccesoCj/Salir`).

**Límite:** es un freno para la fase de desarrollo, no autenticación real. El repositorio de GitHub es público, así que la clave y los
`.respuestas.json` (decisiones de producto y valores clínicos, sin datos de residentes) también lo son. Antes de usar la aplicación con
datos reales hará falta autenticación de verdad (ver el ADR 0007 de proveedor de identidad).

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
2. En el `.html`, bajo el «eyebrow» de la cabecera, la línea «Archivado el AAAA-MM-DD: lo que respondiste ya está implementado…» (como en los dos ya archivados) y «Archivado» en lugar de «Pendiente de CJ». Arriba de la cabecera, el bloque `<nav class="back">` con «← Documentos para CJ» (`../index.html`) y «Volver a ResidApp» (`/`), con su CSS y su regla de impresión; cópialo de uno ya archivado.
3. Quitarlo de `preguntas.html` y añadirlo a `archivo.html` (con una frase de qué se hizo y dónde sigue lo pendiente), y moverlo en la tabla de abajo.
4. Actualizar las rutas que lo citan en `docs/` (`pendientes-*.md`, `docs/prompts-ia/continuar-construccion.md`).
5. Tras el despliegue, borrar en Azure la copia antigua de la raíz (`site/wwwroot/pendientes-cj/<documento>.html`): el despliegue añade ficheros pero no borra los de antes, y esa copia seguiría abierta (con clave) y sin respuestas. Kudu tiene desactivado el acceso básico: se borra con la API `vfs` y un token de Entra ID (`az account get-access-token --resource https://management.azure.com`, `DELETE` con `If-Match: *`).

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
`input type="radio"` dentro de `label.choice`. Para agrupar el avance por temas, cada tema es una `section.tema` con `id`. Añade también su entrada a `preguntas.html` (o a `pruebas.html`: listas estáticas de la web) y a la tabla de abajo.

El documento `guia-de-pruebas-cj.html` es una variante: cada pregunta es una **prueba** con pasos y «qué debería ocurrir», y las opciones son A funciona · B con problema · C no funciona · D no probada. Usa el mismo estilo y script; solo cambian los textos del script («Sin probar»/«Probada» y «completadas», y «Pendiente»/«Respondida» en las preguntas con `class="abierta"`). Está generado a partir de una lista de pruebas; si cambia una pantalla descrita en un paso, edita el HTML y sube `data-version`.

## Documentos

### Preguntas

| Documento | Respuestas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [administracion-ambito-familiares-cargos.html](administracion-ambito-familiares-cargos.html) | `administracion-ambito-familiares-cargos.respuestas.json` | Unidades que ninguna Administración tiene, desvincular y compartir familiares, y organigrama y cargos (7 respuestas) | Pendiente |
| [continuidad-supervision-comunicacion.html](continuidad-supervision-comunicacion.html) | `continuidad-supervision-comunicacion.respuestas.json` | Confirmar recepción de seguimientos, derivaciones de Dirección y seguimientos vencidos (ya construidos); plazos de los hitos del proceso y quién aprueba la comunicación familiar (9 preguntas y 7 plazos) | Pendiente |

### Pruebas

| Documento | Respuestas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [guia-de-pruebas-cj.html](guia-de-pruebas-cj.html) | `guia-de-pruebas-cj.respuestas.json` | Guía de pruebas en el entorno de desarrollo: 49 pruebas paso a paso (A funciona, B con problema, C no funciona, D no probada) más 3 preguntas finales; 18 imprescindibles | Pendiente |

### Archivo (contestados e implementados)

| Documento | Respuestas | Qué se pidió | Estado |
| --- | --- | --- | --- |
| [archivados/rangos-referencia-constantes.html](archivados/rangos-referencia-constantes.html) | `archivados/rangos-referencia-constantes.respuestas.json` | Mínimo y máximo de las 7 constantes con aviso visual, y 5 preguntas abiertas | Archivado 2026-10-07: hechos los valores sugeridos, el flujo de O₂ y el aviso de temperatura de la Auxiliar; lo pendiente, en «Aclaraciones» |
| [archivados/decisiones-direccion-basal-derivacion.html](archivados/decisiones-direccion-basal-derivacion.html) | `archivados/decisiones-direccion-basal-derivacion.respuestas.json` | Finalidades de la lectura clínica de Dirección, aportación a un borrador de basal ajeno y el campo «Comunicaciones» en el informe de derivación (7 respuestas) | Archivado 2026-10-07: hechos las finalidades y la justificación de Dirección y «Comunicaciones»; las aportaciones al basal, en «Aclaraciones» |
| [archivados/aclaraciones-respuestas-cj.html](archivados/aclaraciones-respuestas-cj.html) | `archivados/aclaraciones-respuestas-cj.respuestas.json` | Aclaraciones a las respuestas de rangos y de Dirección, basal y derivación (11 respuestas) | Archivado 2026-10-07: hechos los valores de temperatura y glucemia, la saturación con oxígeno, el aviso de la Auxiliar retirado, los rangos solo para Dirección / Coordinación y las aportaciones al basal descartadas |
| [archivados/traslado-y-baja-residente.html](archivados/traslado-y-baja-residente.html) | `archivados/traslado-y-baja-residente.respuestas.json` | Traslado entre unidades, baja y reactivación del residente (6 respuestas) | Archivado 2026-10-07: hechos el traslado por Administración, la baja, la lista de bajas, la reactivación y la suspensión por ingreso hospitalario; queda el traslado por Enfermería con permiso, para cuando un centro lo pida |
