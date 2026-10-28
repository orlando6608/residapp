# Historial tras un traslado: nota de EIPD (evaluación de impacto)

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha del borrador: 2026-10-28.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija la residencia, el DPO real o CJ.

**Qué es una EIPD:** un análisis por escrito, hecho antes de empezar a tratar datos, de los riesgos para las personas y de las medidas para reducirlos (art. 35 RGPD). Lo hace el responsable (la residencia) con ayuda del encargado (ResidApp) y del DPO.

## Conclusión

**Esta función no necesita una EIPD propia; basta incorporarla como un anexo a la EIPD general** que ya consta como puerta abierta antes de producción (ADR 0007, 0009 y 0011). Razones:

1. **[Suposición]** La EIPD general es obligatoria de todos modos: datos de salud de personas mayores, a menudo con capacidad disminuida, tratados de forma sistemática, más voz y NFC. **[Decidir] DPO:** contrastarlo con la lista de tratamientos que exigen EIPD de la AEPD (o de la APDCAT, según el centro) y con el art. 35.4.
2. **[Hecho]** La función no crea una categoría nueva de datos clínicos ni los lleva fuera del centro: abre, con condiciones, datos que el mismo responsable ya tiene sobre el mismo residente (`SqlChangeInboxDirectory.cs`, líneas 79 a 112).
3. **[Hecho]** Lo que sí crea es un dato nuevo, **el motivo en texto libre**, y un registro permanente de accesos con él (`0077`, línea 19). Eso es lo que la EIPD debe valorar.

**Cuándo habría que hacer una EIPD específica o rehacer este anexo:** si el motivo se analiza automáticamente o se usa para evaluar al personal; si se amplía a otro centro u otro perfil; si se quita el límite de 1 hora o la comprobación en cada lectura; si el acceso pasa a incluir el informe de derivación o la escritura.

## Riesgos concretos y medidas

Valoración de riesgo residual: **[Suposición]** mía, para que el DPO la corrija.

| # | Riesgo | Lo que ya hay [Hecho] | Hueco | Medida propuesta | Residual |
|---|---|---|---|---|---|
| 1 | **Acceso por curiosidad** o sin necesidad asistencial (p. ej. un residente conocido, un familiar de un compañero) | Motivo obligatorio; auditoría antes de entregar; 1 hora; solo cerrados; solo la unidad de destino; solo Enfermería y Medicina (`TransferHistoryAccess.cs`, 38 a 49; `SqlChangeInboxDirectory.cs`, 101 a 111) | (a) El motivo es cualquier texto de 1 a 300 caracteres (`TransferHistoryRequest.cs`, 20 y 21): «ver» sirve. (b) Dirección solo ve las lecturas **residente por residente** y tras abrir su propia declaración de acceso (`SqlChangeInboxDirectory.Traceability.cs`, 13 a 38; `ReadDirectionBaseline.cs`, 57 a 62 y 105 a 107): no hay una lista de todas las peticiones. (c) Una petición rechazada (residente sin episodios aplicables) no deja rastro (`SqlTransferHistoryAccess.cs`, 57 a 61). (d) La pantalla dice «las revisa Dirección» (`_HistorialUnidadAnterior.cshtml`, 28): hoy es posible, no automático | Procedimiento de revisión (fichero 04); lista de motivos cerrada más texto opcional; vista de Dirección con todas las peticiones del periodo; registrar los intentos rechazados; cambiar «las revisa» por «puede revisarlas» hasta que exista la vista | Medio, bajando a bajo con las medidas |
| 2 | **Motivo con datos de salud o nombres**, escritos libremente | Advertencia junto al campo (`_HistorialUnidadAnterior.cshtml`, 28) | Solo es un aviso: no se impide. El motivo queda **copiado en varias filas inmutables** (petición, auditoría de la petición y una fila por cada apertura) (`SqlTransferHistoryAccess.cs`, 43 a 49; `SqlChangeInboxDirectory.cs`, 121 a 140) | Lista cerrada de motivos (como ya hace Dirección con sus finalidades, `0038`, líneas 26 a 28); texto libre opcional y corto; procedimiento de retirada del texto (fichero 05); formar al personal | Medio |
| 3 | **Inmutabilidad frente a supresión, rectificación o plazo vencido** (arts. 5.1.e, 16 y 17) | Tabla y auditoría no se pueden borrar ni editar (`0077`, 33 a 50; `0002`, 727 y 728) | No existe vía de purga ni plazo | Opciones y recomendación en el fichero 05 | Alto hasta que se decida |
| 4 | **Acceso a datos de otra unidad** (minimización, art. 5.1.c) | Solo cerrados anteriores al traslado; Medicina solo escalados y propios; solo lectura; sin PDF (`SqlChangeInboxDirectory.cs`, 101 a 111) | Sin límite de antigüedad. El detalle incluye texto libre y textos de comunicación a la familia (`SqlChangeInboxDirectory.Timeline.cs`, 41 y 42). La lectura del historial y la línea temporal audita al **residente**, no cada episodio entregado (`recurso_id` = residente; `SqlChangeInboxDirectory.cs`, 127) | Preguntar a CJ por una ventana de antigüedad; valorar auditar también la lista de episodios entregados | Medio |
| 5 | **Datos del personal en el registro** (cuenta, perfil, hora, motivo): vigilancia laboral | Solo ve el motivo Dirección Clínica de su ámbito (`...Traceability.cs`, 22 a 32) | Hay que informar al personal y, probablemente, a su representación | Comunicación al personal (fichero 03); **[Decidir]** asesoría laboral | Bajo |
| 6 | **Dirección no ve peticiones de unidades fuera de su ámbito** | La trazabilidad filtra por la unidad actual del residente en el ámbito de Dirección (`...Traceability.cs`, 29 a 32) | Una unidad sin Dirección asignada queda sin revisión | Comprobar al implantar que cada unidad tiene una Dirección Clínica con ámbito | Bajo |
| 7 | **Cookie** | Solo el identificador, `HttpOnly`, de sesión (`TransferHistoryCookie.cs`, 13 a 18) | — | **[Suposición]** cookie estrictamente necesaria; exenta de consentimiento (LSSI, art. 22.2). **A verificar** en la política de cookies | Bajo |
| 8 | **Fallo de la auditoría** | Si no se escribe la fila, no se entrega nada (`...Timeline.cs`, 51 a 57; `SqlChangeInboxDirectory.cs`, 231 a 235) | — | Mantener esa regla; probarla en la EIPD | Bajo |

## Para el DPO real

- ¿Basta el anexo o prefiere EIPD separada?
- ¿Se aceptan el riesgo residual medio de los puntos 1, 2 y 4 con las medidas, o exige alguna antes del piloto con datos reales?
- ¿Consulta previa a la autoridad (art. 36)? **[Suposición]** No, si las medidas se aplican: no se detecta un riesgo alto sin mitigar. Lo decide el DPO.

## A verificar o decidir

1. **[Decidir] DPO:** contraste con la lista de la AEPD/APDCAT (autoridad según el centro piloto: **a verificar**).
2. **[Decidir] Orlando/CJ:** los cambios de código sugeridos (lista de motivos, vista de revisión, registro de intentos rechazados, ventana de antigüedad) son decisiones de producto: los valoráis vosotros.
3. **[Decidir] Residencia:** asesoría laboral sobre la información al personal.
