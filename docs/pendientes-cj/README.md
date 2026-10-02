# Pendientes de CJ

Documentos que CJ tiene que completar: valores clínicos y decisiones de producto que ingeniería no puede
inventar. Cuando CJ los completa, ingeniería revisa las respuestas y las incorpora a la aplicación.

## Cómo responderlos (CJ)

1. Abre el `.html` en GitHub, pulsa «Download raw file» y abre el fichero descargado con el navegador.
2. Responde en la propia página: pulsa una opción o escribe tu respuesta, y añade un comentario si quieres.
   Lo que marcas se guarda en ese navegador mientras trabajas.
3. Pulsa «Guardar respuestas» (barra de abajo). Se descarga `<documento>.respuestas.json`.
4. Súbelo a esta carpeta: en GitHub, «Add file → Upload files» (o el enlace del propio documento), arrastra el
   fichero y pulsa «Commit changes». Si ya existía, se sustituye.

Para seguir en otro ordenador: abre el documento y pulsa «Abrir respuestas guardadas» con tu fichero. Si la página
se sirve por http desde el repositorio (no desde disco), carga sola el `.respuestas.json` de la carpeta.

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
`input type="radio"` dentro de `label.choice`. Para agrupar el avance por temas, cada tema es una `section.tema` con `id`.

## Documentos

| Documento | Respuestas | Qué hay que completar | Estado |
| --- | --- | --- | --- |
| [rangos-referencia-constantes.html](rangos-referencia-constantes.html) | `rangos-referencia-constantes.respuestas.json` | Mínimo y máximo de las 7 constantes con aviso visual, y 5 preguntas abiertas | Pendiente |
| [decisiones-direccion-basal-derivacion.html](decisiones-direccion-basal-derivacion.html) | `decisiones-direccion-basal-derivacion.respuestas.json` | Finalidades de la lectura clínica de Dirección, aportación a un borrador de basal ajeno y el campo «Comunicaciones» en el informe de derivación (7 respuestas) | Pendiente |
| [traslado-y-baja-residente.html](traslado-y-baja-residente.html) | `traslado-y-baja-residente.respuestas.json` | Traslado entre unidades o centros (episodios abiertos, borrador de basal, quién lo hace) y baja y reactivación del residente (6 respuestas) | Pendiente |
| [administracion-ambito-familiares-cargos.html](administracion-ambito-familiares-cargos.html) | `administracion-ambito-familiares-cargos.respuestas.json` | Unidades que ninguna Administración tiene, desvincular y compartir familiares, y organigrama y cargos (7 respuestas) | Pendiente |
| [continuidad-supervision-comunicacion.html](continuidad-supervision-comunicacion.html) | `continuidad-supervision-comunicacion.respuestas.json` | Confirmar recepción de seguimientos, derivaciones de Dirección y seguimientos vencidos (ya construidos); plazos de los hitos del proceso y quién aprueba la comunicación familiar (9 preguntas y 7 plazos) | Pendiente |
