# Zonas horarias por centro: estudio de implantación

**Estado: F0 construida (2026-11-20, PR de F0); F1 a F4 sin construir.** Decisión de F1 (Orlando, 2026-11-20, pregunta A): al corregir la zona de un centro con franjas o citas futuras, Plataforma ve cuántas se ven afectadas y confirma; las franjas se regeneran a la hora nueva y las citas ya reservadas (en UTC) no se tocan. Responde a la petición de Orlando: que cada centro tenga su zona horaria, elegida al crearlo, entre las que define Plataforma. Sustituye a la nota «Una sola zona para toda la aplicación» de `docs/prompts-ia/avisos-abiertos.md`. No cambia ningún valor clínico: la zona solo decide **cómo se ven las horas y qué es «hoy»** en cada centro.

## 1. Decisiones de Orlando (2026-11-20, de una en una)

1. **Catálogo de zonas que define Plataforma: tres, una por cada hora real distinta.** «Península y Baleares (Madrid)» y «Canarias», más una tercera zona **fuera de España** cuyo estudio de cumplimiento se mantiene aparte, **fuera del repositorio**. Madrid y Barcelona son la misma zona para el sistema (`Europe/Madrid`), así que **no hay una entrada «Barcelona» aparte**.
2. **Una zona fuera de España no es solo un cambio técnico: es un asunto de cumplimiento.** Su estudio (normativa aplicable, dónde se alojan los datos, transferencias) **se mantiene fuera del repositorio** y la zona no se activa sin él: ver el apartado 7.
3. **La zona se elige al crear el centro y después no la cambia ninguna Administración.** Solo Plataforma puede corregirla, con un motivo de una lista cerrada y con constancia en la auditoría.
4. **Plataforma usa siempre la hora de la península (Madrid)**, también su centro reservado. Los días de vigencia de los módulos y contratos (`TodayInMadrid`) siguen siendo días de Madrid: es un concepto contractual de la plataforma, no del centro.

## 2. Qué pasa hoy (explicado sin jerga)

- **Todo se guarda en hora universal (UTC)**: la base de datos no usa hora local en ningún sitio (ningún `GETDATE()` ni `SYSDATETIME()`). Esto es lo que hace posible el cambio: **no hay que convertir ningún dato guardado**.
- **Lo que se muestra y lo que cuenta como «hoy» se calcula con la hora del servidor.** El servidor está fijado en Madrid (`WEBSITE_TIME_ZONE=Europe/Madrid`; el despliegue lo comprueba y la aplicación no arranca en producción si no es así: `ServerTimeZone.Verify`). Toda la aplicación da por hecho que **hay una sola zona**.
- **Dónde se usa esa hora** (recuento a 2026-11-20 sobre `main`):
  - **Vistas y modelos:** `ToLocalTime()` en unos 70 ficheros (fechas de auditoría, historiales, derivaciones, familias, citas) y `TimeZoneInfo.Local` en unos 15.
  - **«Hoy» en los casos de uso:** `DateTime.Today` / `DateTime.Now` en más de 20 sitios de Application y Web (turnos, planificación, portal familiar, citas, Dirección, intercambio).
  - **Base de datos:** dos procedimientos con la zona de Madrid escrita a mano, `0092_recordatorio_cita.sql` y `0105_citas_externas_familia_actua.sql` (`AT TIME ZONE 'Romance Standard Time'`). Las franjas de citas se guardan como **hora local sin zona** (`citas_franjas.hora_inicio`).
  - **Procesos de fondo:** siete en `src/ResidApp.Web/Correo/` (recordatorios, avisos de cambios y de citas externas, avisos sin resolver…). Los que mandan avisos a familias deciden «¿es buena hora para enviar?» con la hora de la aplicación.
  - **Informes:** el PDF del informe de derivación y los nombres de fichero usan la hora local.
  - **Formularios con fecha y hora** (`datetime-local`): se interpretan con la zona del servidor.
  - **Ya pasan una zona explícita:** el horario de los comunicados, los indicadores de Dirección y las franjas de citas (`FamilyCommunicationSchedule`, `SupervisionIndicatorRules`, `AppointmentSlots`). Son la base para generalizar.
- **Las pruebas no cubren ninguna zona distinta de Madrid:** el CI corre en UTC y muchas usan la hora local de la máquina. Un `DateTime.Today` mal usado hoy no falla.

## 3. Diseño propuesto

### 3.1 Datos (un script nuevo numerado; nunca se edita uno aplicado)

- **Tabla catálogo `zonas_horarias`:** `codigo` (el nombre estándar, `Europe/Madrid`, `Atlantic/Canary` y la de fuera de España), `nombre_windows` (SQL Server solo entiende los nombres de Windows para `AT TIME ZONE`), `etiqueta` (el nombre que ve la persona), `activa`. Añadir una zona nueva en el futuro es **un `INSERT` en un script**, sin tocar restricciones (a diferencia de una lista fija en un `CHECK`).
- **`centros.zona_horaria`**, obligatoria, con clave foránea al catálogo y valor por defecto `Europe/Madrid`: **todos los centros existentes quedan exactamente como están**.
- **El centro reservado de Plataforma queda fijado a `Europe/Madrid`** por una restricción de la base de datos, no solo por la pantalla.
- **Nombres de Windows a verificar en Azure SQL** (consultando `sys.time_zone_info`): Madrid `Romance Standard Time` (ya en uso), Canarias `GMT Standard Time` (comparte nombre con Londres y Lisboa) y el de la zona fuera de España. Si falta alguno, **se verifica antes de construir**, no después.
- **El cambio de zona** (solo Plataforma) deja una fila en la auditoría con el motivo de una lista cerrada y sin texto libre. Ver la pregunta abierta A del apartado 8.

### 3.2 Un único sitio donde se pregunta «¿qué hora es en este centro?»

- **Como se construyó en F0:** una clase estática `CenterTime` en `ResidApp.Shared` con la zona **de la petición en curso** (`AsyncLocal`, como la cultura): `Zone`, `Local(instante)`, `FromLocal(hora de un formulario)`, `Today`, `TodayAt(instante)` y `Now`; `CenterTime.Use(zona)` la fija y la restaura. Sin fijar, es Madrid. Se eligió esto en lugar de un servicio inyectado (`ICenterTime`) porque la hora se pregunta desde vistas, modelos estáticos, casos de uso y repositorios, y un servicio habría obligado a cambiar decenas de constructores sin ganar nada. **En F1**, un middleware fijará la zona con la del centro del ámbito activo verificado, y los procesos de fondo (F2) la fijarán centro a centro.
- **Se acaba con `TimeZoneInfo.Local`, `ToLocalTime()` y `DateTime.Today`** salvo en los dos sitios que son de Plataforma/Madrid. Es la parte más grande del trabajo, pero es mecánica. **Hecho en F0:** 105 ficheros de `src` y 21 de pruebas, sin cambiar ninguna hora para los centros actuales.
- **Vistas:** un ayudante de formato único (por ejemplo, `Hora.Local(at)`) en lugar de `ToLocalTime()` repartido por 70 ficheros, de modo que la zona nunca se escriba en una vista.
- **Etiqueta de la zona:** cuando la zona del centro no es la de la península, las pantallas con horas la rotulan una vez (por ejemplo, «hora de Canarias»), para que nadie las lea como hora de Madrid. Aplica a cuentas que trabajan en varios centros y a familias que ven el centro desde otro país.

### 3.3 Procesos de fondo y base de datos

- Los procesos que mandan avisos por centro (recordatorios, avisos de cambios, de citas externas y sin resolver) **calculan la franja de envío y «mañana» con la zona de cada centro**, no con la del servidor. Los procesos de la plataforma (purga del registro de correo, avisos a CJ y a Orlando) siguen con la hora de Madrid o con UTC. **A confirmar al construir, uno por uno.**
- Los procedimientos de `0092` y `0105` pasan a leer la zona del centro (`JOIN` a `centros` y al catálogo) en lugar de escribir `'Romance Standard Time'`. Como no se editan scripts aplicados, **la corrección va en un script nuevo** que reemplaza los procedimientos.

### 3.4 Servidor

- Cuando el último `TimeZoneInfo.Local` desaparezca, la zona del servidor **deja de importar**. La comprobación de arranque (`ServerTimeZone.Verify`) pasa a comprobar que **las tres zonas del catálogo existen en el servidor** (en Linux hace falta la base de datos de zonas, `tzdata`: a verificar en la imagen de la Web App) y deja de exigir Madrid. El paso de CI «Fijar la zona horaria de la Web App» puede quedarse mientras no se cierre la fase 0.

## 4. Qué puede salir mal

| Riesgo | Por qué | Cómo se evita |
|---|---|---|
| **Una hora sale en la zona equivocada** | Basta un `ToLocalTime()` olvidado: sale la hora del servidor, no la del centro, y nada avisa | Un solo ayudante, una **prueba de arquitectura** que prohíba `TimeZoneInfo.Local`, `ToLocalTime()` y `DateTime.Today` fuera de lo permitido, y pruebas con zona explícita en un CI que corre en UTC |
| **Cambios de hora de verano en fechas distintas** | España y un país de fuera pueden cambiar en semanas diferentes (fechas a verificar): unas semanas al año la diferencia entre ambos es una hora distinta de la de siempre | Pruebas sobre los días de cambio de cada zona (hora que no existe y hora que se repite) |
| **Una franja de cita ya creada se desplaza** | Las franjas se guardan como hora local sin zona: si la zona de un centro cambia, sus franjas futuras cambian de hora real | Corregir la zona de un centro con franjas futuras se trata como una operación con confirmación (pregunta A) |
| **Un fichero o un correo con la hora equivocada** | PDF, nombre de fichero y textos de correo se arman con la hora local | Pasan por el mismo ayudante; revisión de cada plantilla de correo |
| **«Hoy» distinto entre centro y familia** | Una familia que mira desde otro país | La etiqueta de zona (3.2) y todo el portal con la zona **del centro del residente** |
| **Pruebas que dependen del reloj de la máquina** | Hoy muchas usan la hora local | Reloj de pruebas inyectado (`TimeProvider`) con zona explícita |

## 5. Plan por fases (cada una se puede fusionar sola y deja la aplicación funcionando igual para los centros actuales)

| Fase | Contenido | Cambia algo para los centros actuales |
|---|---|---|
| **F0** | `ICenterTime` con la zona de Madrid para todos; sustituir `TimeZoneInfo.Local`, `ToLocalTime()` y `DateTime.Today` por vertical (Enfermería, Medicina, Administración, Dirección, portal familiar, informes); prueba de arquitectura | No |
| **F1** | Script: catálogo y `centros.zona_horaria`; selector al crear el centro (Plataforma); corrección de zona por Plataforma con motivo y auditoría | No (todos en Madrid) |
| **F2** | Script con `0092`/`0105` leyendo la zona del centro; procesos de fondo por centro | No |
| **F3** | **Canarias** activada (la primera zona distinta: una hora menos, con el mismo calendario de horario de verano que la península) y pruebas de punta a punta | Solo los centros que elijan Canarias |
| **F4** | **La zona fuera de España**, solo cuando esté cerrado su estudio de cumplimiento (apartado 7) | Solo el centro que la elija |

F0 y F1 se pueden hacer **antes** del estudio de cumplimiento de esa zona: son lo mismo para cualquier zona. Cada script nuevo y cada cambio con datos personales pasa antes por `dpo-residapp`; el único dato nuevo es la zona del centro, que no es un dato personal.

## 6. Pruebas que debe tener

- Para cada zona del catálogo: la hora que se muestra, el «hoy» y los límites de un día en UTC, en invierno y en verano, **y en los días del cambio de hora** (que no existe / que se repite).
- Un centro de Canarias y otro de la zona fuera de España en la misma base con la misma cita: cada uno la ve a su hora y el recordatorio sale en su franja.
- Un formulario `datetime-local` enviado desde un centro con zona distinta de la del servidor.
- Plataforma siempre en Madrid, aunque haya centros en otra zona; un centro nuevo sin zona elegida no se crea; una zona fuera del catálogo se rechaza (la clave foránea lo impide).
- La prueba de arquitectura del apartado 4.

## 7. Una zona fuera de España (F4)

Un centro fuera de España obliga a cumplir su normativa local además de la europea y plantea **dónde se alojan los datos y si su consulta desde allí es una transferencia internacional**. Eso **no se resuelve con la zona horaria** y su estudio **se mantiene aparte, fuera del repositorio** (el repositorio es público; ni el nombre, ni el titular, ni la dirección de ese centro se escriben en él).

**Consecuencias para este estudio:**

- **F0 a F3 no dependen de ese estudio** y se pueden construir antes. **F4 queda bloqueada** hasta que lo cierre una persona experta; si concluye que los datos deben alojarse fuera de la UE, el problema pasa de ser de zona horaria a ser **de arquitectura y de entorno** y pediría su propio estudio.
- **Pruebas:** con datos ficticios, sí; no se crea en Azure dev un centro con nombre real ni se prueba desde ese país.
- **La zona horaria no es un dato personal nuevo**, siempre que se guarde **por centro y nunca por persona** (la zona de un navegador revelaría dónde está una familia). El aviso de brecha da la hora en UTC más la zona del centro.

## 8. Preguntas abiertas

- **A. ¿Qué pasa con las franjas de citas al corregir la zona de un centro?** Propuesta: la pantalla de Plataforma avisa de cuántas franjas futuras y citas reservadas se verían afectadas y exige confirmación; las franjas se vuelven a generar a la hora local nueva y las citas ya reservadas (que están en UTC) **no se tocan**. A decidir antes de F1.
- **B. ¿Ven las familias la hora del centro con su etiqueta, aunque estén en otro país?** Propuesta: sí, siempre la del centro y rotulada. A confirmar con CJ en el recorrido del portal familiar.
- **C. ¿Hay que registrar la zona en el contrato de cada centro?** Es una decisión de Orlando, no técnica.
