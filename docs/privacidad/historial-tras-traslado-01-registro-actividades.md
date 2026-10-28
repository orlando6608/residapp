# Historial tras un traslado: entrada del registro de actividades de tratamiento (art. 30 RGPD)

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha del borrador: 2026-10-28.

Etiquetas: **[Hecho]** verificado en el repositorio (con archivo y línea), **[Suposición]** mía, **[Decidir]** lo fija la residencia, el DPO real o CJ.

**Qué es el registro de actividades de tratamiento (RAT):** la lista que cada organización debe tener de para qué usa datos personales, de quién, quién los ve y cuánto tiempo los guarda. El responsable lleva uno (art. 30.1) y el encargado lleva otro, más corto, con lo que trata por cuenta del responsable (art. 30.2).

## Reparto de papeles

- **[Hecho]** La residencia es la responsable y ResidApp la encargada (ADR 0007, líneas 3 a 5). Por tanto, la **parte A** (más abajo) es entrada del responsable: la firma y la custodia la residencia, y ResidApp solo le da los datos técnicos para rellenarla. La **parte B** es entrada del encargado: la lleva ResidApp.
- **[Hecho]** No interviene Mistral ni ninguna IA en esta función: los archivos que la implementan (`SqlTransferHistoryAccess.cs`, `SqlChangeInboxDirectory.cs`, `TransferHistoryAccess.cs`) no llaman a ningún proveedor externo. **[Hecho]** El único subencargado que toca estos datos es el de infraestructura (Azure SQL y Web App, según ADR 0007).
- **[Suposición]** Es una **actividad nueva dentro del tratamiento «historia asistencial en ResidApp»** del responsable, no un tratamiento aparte. Si la residencia ya tiene un RAT por unidad o por aplicación, se añade como una fila o como un anexo de ese.

## Parte A — entrada del responsable (la residencia) (art. 30.1)

| Campo | Contenido propuesto |
|---|---|
| Responsable | [RESIDENCIA], [dirección], [contacto]. DPO: [DPO] |
| Encargado | ResidApp ([datos de ResidApp]) |
| Nombre de la actividad | Acceso de la unidad de destino a los episodios cerrados de la unidad anterior de un residente trasladado |
| Finalidad 1 | **Continuidad asistencial**: que el equipo que atiende ahora al residente pueda consultar, si lo necesita, lo ocurrido en los episodios que ya se cerraron en su unidad anterior. |
| Finalidad 2 | **Control de accesos y responsabilidad proactiva** (poder demostrar quién consultó qué y por qué): registro del motivo y de cada lectura, y su revisión por Dirección Clínica. |
| Personas afectadas | (a) **Residentes** trasladados de unidad. (b) **Profesionales** de Enfermería y Medicina que piden y leen (su cuenta, perfil y hora quedan registrados). (c) **Terceros nombrados** en el texto libre de los episodios o en el motivo (familiares, otros residentes, otros profesionales), de forma incidental. |
| Datos | **Lo que se consulta (ya existía):** el historial, la línea temporal y el detalle de episodios cerrados: observaciones, clasificación, escalado a Medicina y su motivo, textos de comunicación a la familia (`SqlChangeInboxDirectory.Timeline.cs`, líneas 36 a 46). Son **datos de salud** (art. 9). **Lo que esta función crea:** por cada petición, cuenta, ámbito de perfil, residente, **motivo en texto libre (hasta 300 caracteres)**, hora de creación, de caducidad y de fin (`0077_historial_tras_traslado.sql`, líneas 13 a 28); y filas de auditoría con acción `TRANSFER_HISTORY_READ`, unidad actual del residente, tipo de recurso y el mismo motivo (`SqlTransferHistoryAccess.cs`, líneas 43 a 49; `SqlChangeInboxDirectory.cs`, líneas 117 a 140). |
| Base jurídica (art. 6) | **[Suposición]** Art. 6.1.c (obligación legal: documentación clínica y seguridad del tratamiento, art. 32 RGPD) para la lectura y para el registro de accesos. Para el registro de accesos, alternativamente 6.1.f (interés legítimo en la seguridad): **[Decidir]** DPO. |
| Condición del art. 9.2 | **[Suposición]** 9.2.h (asistencia sanitaria o social y gestión de sistemas de asistencia, bajo secreto profesional, art. 9.3), con la disposición adicional 17.ª de la LOPDGDD, la Ley 41/2002 y la Llei 21/2000 (documentación clínica). **[Decidir]** abogado: la cita exacta de los artículos de la ley catalana que habilitan el acceso del equipo que atiende al paciente. |
| No es una cesión | **[Suposición]** Es el mismo responsable, el mismo centro y el mismo residente: un acceso interno, no una comunicación a un tercero. |
| Destinatarios | Dentro de la residencia: Enfermería y Medicina de la unidad de destino (lectura) y Dirección Clínica de su ámbito (ve el motivo en la trazabilidad, `SqlChangeInboxDirectory.Traceability.cs`, línea 22). Fuera: ninguno, salvo la autoridad de control, jueces o la inspección cuando lo exija la ley. |
| Transferencias fuera del EEE | **[Suposición]** Ninguna por esta función (Azure en región de la UE, preferida según ADR 0007). **[Decidir]** confirmar la región real del piloto. |
| Plazo de conservación | **[Decidir]** Sin plazo fijado hoy: el motivo hereda el de la auditoría, que no está decidido (ver `historial-tras-traslado-05-conservacion-motivo-y-purga.md`). Los episodios consultados siguen el plazo de la documentación clínica: **a verificar** (art. 12 de la Llei 21/2000; no lo cito de memoria). |
| Medidas de seguridad (art. 32) | Ver lista siguiente. |
| Autoridad de control | **A verificar según el centro piloto.** AEPD en general; APDCAT si la residencia es del sector público catalán o presta un servicio público por gestión directa o indirecta (su web, consultada el 2026-10-28, incluye a esas entidades privadas y a quienes tratan datos por encargo suyo, pero no menciona expresamente los centros concertados de servicios sociales). Si es pública o concertada, además aplica el Esquema Nacional de Seguridad (RD 311/2022). |

**Medidas de seguridad [Hecho], todas en el repositorio:**
- Solo Enfermería y Medicina pueden pedir, y el ámbito activo debe ser de ese perfil (`TransferHistoryAccess.cs`, líneas 38 a 49).
- Solo episodios **cerrados**, de una unidad desde la que se trasladó al residente **después** del cierre, con el residente hoy en una unidad del ámbito de quien pide; Medicina solo ve escalados y propios (`SqlChangeInboxDirectory.cs`, líneas 101 a 111).
- La petición vale 1 hora (valor configurable: `ReadDirectionBaseline.cs`, línea 10) y cada lectura la vuelve a comprobar en la base de datos (cuenta, ámbito, residente, hora, no terminada): una cookie manipulada no concede nada (`TransferHistoryCookie.cs`, líneas 3 a 6).
- Auditoría **antes** de entregar: si no se puede escribir, no se entrega nada (`SqlChangeInboxDirectory.Timeline.cs`, líneas 51 a 57).
- Tabla de peticiones inmutable (solo crear y terminar; error 50570) (`0077`, líneas 33 a 50) y auditoría inmutable (`0002_renombrado_espanol_sqlserver.sql`, líneas 727 y 728).
- Aislamiento por centro con seguridad a nivel de fila (`0077`, líneas 53 a 56). Solo lectura: sin corregir ni PDF de derivación (`pendientes-enfermeria.md`, líneas 280 y 281; **no comprobado en el código de las vistas**).
- Cookie de sesión, solo con el identificador, `HttpOnly` (`TransferHistoryCookie.cs`, líneas 13 a 18).

## Parte B — entrada del encargado (ResidApp) (art. 30.2)

| Campo | Contenido propuesto |
|---|---|
| Encargado | ResidApp ([datos de ResidApp]) y, si se designa, su DPO o persona de contacto |
| Responsable por cuenta del que actúa | [RESIDENCIA], [contacto], DPO [DPO] |
| Categoría de tratamiento | Alojar y servir la aplicación que (1) ofrece al personal autorizado la consulta de episodios cerrados de la unidad anterior, (2) guarda la petición con su motivo y (3) escribe la auditoría de cada lectura. Solo según instrucciones documentadas de la residencia (art. 28). |
| Subencargados | Azure (SQL y Web App). Mistral **no** interviene en esta función. **[Decidir]** lista oficial de subencargados autorizada en el contrato de encargado. |
| Transferencias fuera del EEE | Ninguna prevista; **a verificar** con la región real de despliegue. |
| Medidas de seguridad generales | Las de la parte A, más: aislamiento por centro, cuentas personales con identificación según el ADR 0007 (el segundo factor por población sigue pendiente del DPO), copias y registros según `docs/seguridad/hallazgos.md`. |
| Instrucciones que ResidApp espera de la residencia | Plazo de conservación del motivo; quién revisa y con qué frecuencia; procedimiento de supresión (ver ficheros 04 y 05). |

## A verificar o decidir

1. **[Decidir] Residencia + DPO:** base jurídica del registro de accesos (6.1.c o 6.1.f) y redacción exacta de la condición del art. 9.2.h con la normativa catalana.
2. **[Decidir] Residencia:** plazo de conservación de la auditoría y del motivo.
3. **A verificar:** autoridad de control (AEPD o APDCAT) y aplicación del ENS según el tipo de centro piloto.
4. **[Decidir] CJ:** si los episodios cerrados accesibles deben tener un límite de antigüedad (hoy no lo hay: la consulta no filtra por fecha, `SqlChangeInboxDirectory.cs`, líneas 101 a 111). Afecta a la minimización (art. 5.1.c).
5. **A verificar:** firma del contrato de encargado (art. 28) con la residencia y lista de subencargados.
