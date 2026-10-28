# Historial tras un traslado: conservación del motivo y vía de purga

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha del borrador: 2026-10-28.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija la residencia, el DPO real o CJ.

**Qué es una purga:** borrar o vaciar datos de forma controlada cuando ha vencido su plazo de conservación o cuando una persona ejerce su derecho de supresión (art. 17 RGPD). **Qué es seudonimizar:** sustituir el dato que identifica o delata (aquí, el texto del motivo) por una marca neutra, conservando el resto del registro.

## Conclusión

Hoy **no hay plazo ni vía de purga**, y el motivo (texto libre) está copiado en varias filas que la base de datos no deja tocar. **Recomiendo no resolver esto con borrados de filas enteras**, sino combinar tres medidas: (1) **reducir lo que se escribe** (lista cerrada de motivos con texto opcional corto), (2) **retirar solo el texto del motivo** con un procedimiento de mantenimiento autorizado, auditado y limitado a esa columna, y (3) fijar un plazo para el **registro de acceso** (quién, cuándo, qué residente) que decide la residencia. Nada de esto se puede aplicar sin que antes se decida el plazo.

## Dónde está el motivo hoy [Hecho]

| Lugar | Cuántas copias | Qué impide tocarlo |
|---|---|---|
| `dbo.declaraciones_historial_traslado.motivo` (`0077`, línea 19, `NOT NULL` y con comprobación de que no esté vacío, línea 25) | 1 por petición | Disparador `TR_dht_guard`: solo permite terminar la petición; todo lo demás da el error 50570 (`0077`, líneas 33 a 50) |
| `dbo.eventos_auditoria.justificacion` de la fila de la petición, tipo de recurso `RESIDENT_TRANSFER_HISTORY` | 1 por petición | `TR_audit_immutable`: ningún cambio ni borrado, error 50200 (`0002_renombrado_espanol_sqlserver.sql`, líneas 727 y 728; columna añadida en `0038`, línea 14) (`SqlTransferHistoryAccess.cs`, líneas 43 a 49) |
| `dbo.eventos_auditoria.justificacion` de **cada lectura** (Historial, Línea temporal, Detalle de cada episodio) | 1 por apertura, con el mismo motivo | Igual (`SqlChangeInboxDirectory.cs`, líneas 117 a 140) |
| Copias de seguridad de Azure SQL | Según la retención de la copia | No se purgan con la tabla; salen cuando caduca la copia (`docs/seguridad/hallazgos.md`, línea 428 habla de definir la retención de las copias) |

- **[Hecho]** Esa misma columna `justificacion` la comparten las lecturas de Dirección (`0038`) y el cierre excepcional (`0075`), así que lo que se decida vale para las tres.
- **[Hecho]** Solo la trazabilidad de Dirección lee el motivo (`SqlChangeInboxDirectory.Traceability.cs`, línea 22). Otros listados no lo exponen.
- **[Hecho]** No existe ningún plazo de conservación para `eventos_auditoria`. Solo consta como pendiente el de `eventos_acceso` (ADR 0007, línea 104) y el de las filas de dictado (`docs/seguridad/dictado-por-voz-dossier-dpo.md`, línea 130). El `pendientes-enfermeria.md` (línea 284) lo reconoce: «hereda la de la auditoría, aún sin decidir».
- **[Hecho]** Una purga en vigor exigiría tocar a la vez la tabla de peticiones y las filas de auditoría, porque el motivo vive en las dos.

## Plazo legal de conservación

- **[Suposición]** Los episodios consultados son documentación clínica y siguen el plazo de la normativa de documentación clínica de Cataluña. **A verificar.** Orientación de fuentes secundarias consultadas el 2026-10-28 (resultados de búsqueda sobre el art. 12 de la Llei 21/2000, tras la reforma de la Llei 16/2010, que cuenta los plazos desde el alta de cada proceso asistencial; el cuadro de la Comissió Tècnica de Documentació Clínica en canalsalut.gencat.cat): hay un plazo largo para los documentos más relevantes y uno corto para el resto. **No he podido leer el texto oficial** en BOE ni DOGC: no fijo cifras. Además, una búsqueda indicó que la Llei 2/2024 modificó también la Llei 21/2000 y no he comprobado si tocó este artículo. **[Decidir] Abogado o DPO:** confirmar en el texto consolidado vigente (portal jurídico de la Generalitat o DOGC).
- **Pregunta clave para el DPO real o abogado:** ¿forma parte del **registro de accesos y su motivo** de la historia clínica (y sigue su plazo), o es un registro de seguridad con plazo propio? La respuesta cambia todo lo demás: si es parte de la historia, retirar el texto del motivo es más delicado.
- **[Suposición]** El art. 17.3 RGPD permite no suprimir cuando hay obligación legal o para defender reclamaciones: **a verificar** su alcance aquí. Hasta entonces, una petición de supresión sobre un motivo se resuelve con la retirada del texto (opción C), no con el borrado del registro de acceso.

## Opciones

| | Opción | A favor | En contra |
|---|---|---|---|
| A | **No purgar nunca**; se conserva el mismo plazo que la auditoría | Cero cambios; integridad máxima de la prueba | No permite atender una supresión ni cumplir el art. 5.1.e (limitación del plazo); un motivo con datos de salud queda fijado para siempre |
| B | **Borrado por procedimiento de mantenimiento autorizado** (borrar filas vencidas de peticiones y auditoría) | Cumple un plazo vencido de forma limpia | Hay que desactivar o saltar los dos disparadores de inmutabilidad; destruye la prueba de **quién accedió**, que es lo valioso; riesgo de usarlo como atajo para borrar rastros |
| C | **Seudonimizar el motivo**: cambiar solo el texto por una marca neutra («[retirado el DD/MM/AAAA]») en las filas afectadas y conservar cuenta, residente, hora y recurso | Atiende supresión, rectificación y plazo del texto sin perder la trazabilidad de acceso | Exige una excepción controlada en los dos disparadores y una tabla de constancia de cada retirada |
| D | **Cambiar el diseño para que el motivo esté en un solo sitio** (las lecturas apuntan a la petición en vez de copiarlo) y **reducir el motivo a lista cerrada más texto opcional** | Menos copias, menos texto libre, riesgo menor | Es un cambio de código y de esquema; no resuelve lo ya escrito |

## Recomendación

1. **D antes de datos reales** (los dos cambios): lista cerrada de motivos con texto opcional corto, y que las filas de lectura no repitan el texto libre. Precedente en el propio sistema: la lectura de Dirección usa finalidad cerrada más justificación (`0038`, líneas 26 a 28). **Cambio de código: lo valoráis Orlando y CJ; no lo aplico.**
2. **C como vía de purga y de supresión**, con estas condiciones:
   - La retirada solo puede cambiar la columna del motivo o justificación; ninguna otra.
   - Solo se permite dentro de un procedimiento de mantenimiento con permiso aparte (no la cuenta de la aplicación), que escribe antes una fila en una **tabla de constancia** inmutable (quién lo ordenó, quién lo ejecutó, filas afectadas, fecha, base: plazo vencido o solicitud de supresión) y comprueba que está en la misma transacción. **[Hecho]** El proyecto ya usa ese patrón de «fila de constancia en la misma transacción» para permitir una transición que el disparador normalmente impide (`0075_cierre_excepcional_direccion.sql`, líneas 62 a 64).
   - Como `motivo` es `NOT NULL` y no puede estar vacío (`0077`, líneas 19 y 25), y la lectura de Dirección exige justificación no vacía (`0038`, líneas 21 a 31), la marca debe ser un **texto fijo**, no un nulo.
   - Se rechaza si hay una **investigación abierta** sobre esa cuenta o residente (ver fichero 04).
3. **B solo para la fila entera cuando venza el plazo del registro de acceso**, y con la misma tabla de constancia. **[Decidir] Residencia + DPO:** ese plazo.
4. **Mientras no exista C:** el motivo se atiende como una incidencia, con respuesta al interesado explicando que está fijado y por qué (si la base legal lo ampara), y se anota en la EIPD como riesgo aceptado hasta tener la vía. **[Decidir] DPO.**
5. **Copias de seguridad:** definir una retención y documentarla; la retirada del texto llegará a las copias cuando caduquen.

## Derecho de acceso del residente (art. 15)

**Pregunta para el DPO real:** si un residente o representante pide saber quién accedió a su historia, ¿se le facilitan las anotaciones de acceso con el nombre del profesional y su motivo? Afecta a la intimidad del profesional y al texto libre del motivo, que puede mencionar a terceros. El texto del fichero 03 no promete esa entrega.

## A verificar o decidir

1. **A verificar:** plazo legal de la documentación clínica en Cataluña (texto oficial consolidado de la Llei 21/2000, art. 12, y si la Llei 2/2024 lo cambió).
2. **[Decidir] DPO y residencia:** plazo de la auditoría y del motivo; si el registro de accesos es parte de la historia clínica.
3. **[Decidir] Orlando y CJ:** opción D (lista cerrada y no copiar el texto) y el diseño técnico de C.
4. **[Decidir] Residencia:** quién autoriza una retirada y con qué acta; retención de las copias.
