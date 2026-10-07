# Pendientes de CJ

Documentos que CJ tiene que completar: valores clínicos y decisiones de producto que ingeniería no puede
inventar. Cuando CJ los completa, ingeniería revisa las respuestas y las incorpora a la aplicación.

## Cómo responderlos (CJ)

1. Abre la web de ResidApp, pulsa «Documentos de CJ» en el pie de página (o entra en `/pendientes-cj/index.html`) y abre el documento.
2. Responde en la propia página: pulsa una opción o escribe tu respuesta, y añade un comentario si quieres.
   Lo que marcas se guarda en ese navegador mientras trabajas.
3. Pulsa «Guardar respuestas» (barra de abajo). Se descarga `<documento>.respuestas.json`.
4. Envíaselo a Orlando por correo o WhatsApp. Si lo cambias y lo vuelves a guardar, envía el nuevo.

Para seguir en otro ordenador: abre el documento y pulsa «Abrir respuestas guardadas» con tu fichero.

Los `.html` se publican con la aplicación en `/pendientes-cj/` (`ResidApp.Web.csproj` los incluye desde esta carpeta, sin copiarlos),
así que llegan a la web con el siguiente despliegue.

## Cómo guardar las respuestas recibidas (ingeniería)

Guarda el `<documento>.respuestas.json` que envíe CJ en esta carpeta (si ya existía, se sustituye) y haz commit. Es lo que se lee a continuación.

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
`input type="radio"` dentro de `label.choice`. Para agrupar el avance por temas, cada tema es una `section.tema` con `id`. Añade también su entrada a `index.html` (lista estática de la web) y a la tabla de abajo.


El documento `guia-de-pruebas-cj.html` es una variante: cada pregunta es una **prueba** con pasos y «qué debería ocurrir», y las opciones son A funciona · B con problema · C no funciona · D no probada. Usa el mismo estilo y script; solo cambian los textos del script («Sin probar»/«Probada» y «completadas», y «Pendiente»/«Respondida» en las preguntas con `class="abierta"`). Está generado a partir de una lista de pruebas; si cambia una pantalla descrita en un paso, edita el HTML y sube `data-version`.

## Documentos

| Documento | Respuestas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [guia-de-pruebas-cj.html](guia-de-pruebas-cj.html) | `guia-de-pruebas-cj.respuestas.json` | Guía de pruebas en el entorno de desarrollo: 49 pruebas paso a paso (A funciona, B con problema, C no funciona, D no probada) más 3 preguntas finales; 18 imprescindibles | Pendiente |
| [rangos-referencia-constantes.html](rangos-referencia-constantes.html) | `rangos-referencia-constantes.respuestas.json` | Mínimo y máximo de las 7 constantes con aviso visual, y 5 preguntas abiertas | Respondido (2026-10-06); en incorporación |
| [decisiones-direccion-basal-derivacion.html](decisiones-direccion-basal-derivacion.html) | `decisiones-direccion-basal-derivacion.respuestas.json` | Finalidades de la lectura clínica de Dirección, aportación a un borrador de basal ajeno y el campo «Comunicaciones» en el informe de derivación (7 respuestas) | Respondido (2026-10-06); en incorporación (las aportaciones esperan aclaración) |
| [traslado-y-baja-residente.html](traslado-y-baja-residente.html) | `traslado-y-baja-residente.respuestas.json` | Traslado entre unidades o centros (episodios abiertos, borrador de basal, quién lo hace) y baja y reactivación del residente (6 respuestas) | Pendiente |
| [administracion-ambito-familiares-cargos.html](administracion-ambito-familiares-cargos.html) | `administracion-ambito-familiares-cargos.respuestas.json` | Unidades que ninguna Administración tiene, desvincular y compartir familiares, y organigrama y cargos (7 respuestas) | Pendiente |
| [continuidad-supervision-comunicacion.html](continuidad-supervision-comunicacion.html) | `continuidad-supervision-comunicacion.respuestas.json` | Confirmar recepción de seguimientos, derivaciones de Dirección y seguimientos vencidos (ya construidos); plazos de los hitos del proceso y quién aprueba la comunicación familiar (9 preguntas y 7 plazos) | Pendiente |
