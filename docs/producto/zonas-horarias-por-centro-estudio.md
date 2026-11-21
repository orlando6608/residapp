# Zonas horarias por centro: estudio de implantación

**Estado: F0 (PR #370), F1 (PR #372), F2 (PR #373) y F3 construidas (2026-11-21); F4 sin construir.** Decisión de F1 (Orlando, 2026-11-20, pregunta A): al corregir la zona de un centro con franjas o citas futuras, Plataforma ve cuántas se ven afectadas y confirma; las franjas se regeneran a la hora nueva y las citas ya reservadas (en UTC) no se tocan. Responde a la petición de Orlando: que cada centro tenga su zona horaria, elegida al crearlo, entre las que define Plataforma. Sustituye a la nota «Una sola zona para toda la aplicación» de `docs/prompts-ia/avisos-abiertos.md`. No cambia ningún valor clínico: la zona solo decide **cómo se ven las horas y qué es «hoy»** en cada centro.

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

### 3.5 Cómo se construyó F1 (2026-11-21)

- **Script `0109`.** Tabla catálogo `zonas_horarias` (Madrid activa; **Canarias dada de alta pero inactiva** hasta que F2 y F3 estén hechas: si no, un centro de Canarias vería sus pantallas a su hora y recibiría los avisos con la de la península) y tabla de solo añadir `zonas_centro`, como `modulos_centro`: **sin fila, un centro usa Madrid** (los centros existentes no se tocan ni se rellenan); al corregir un centro sin fila se anota antes su fila de alta con Madrid. La zona se guarda como fila 1 al crear el centro (motivo `ALTA`) y las correcciones llevan un motivo de lista cerrada (`ERROR_DE_ALTA`, `CAMBIO_DE_UBICACION`) y los recuentos confirmados. Seguridad por filas, permisos e inmutabilidad igual que `modulos_centro`; el centro reservado de Plataforma no puede tener fila (trigger).
- **Recuento de lo afectado.** `citas` y `citas_franjas` solo las ve el centro propio (seguridad por filas), así que Plataforma lo cuenta con el procedimiento `recuento_citas_centro` (dos números; solo lo ejecuta el usuario de la aplicación, tras comprobar que quien pide es Plataforma).
- **La zona de cada petición.** Un middleware (`CenterTimeMiddleware`) fija `CenterTime` con la zona del centro del ámbito activo, leída de SQL contra la cuenta de la sesión (la cookie no basta) y con una caché de 30 segundos; un cambio de zona la vacía en el servidor que lo hace. Un fallo de lectura se registra y usa Madrid.
- **Pantallas.** Selector «Zona horaria del centro» al crear el centro y sección «Zona horaria» en la pantalla del centro de Plataforma (corregir con motivo, ver lo afectado, casilla de confirmación e historia). La Administración no puede.
- **Pendiente de F2 (lo señaló el DPO; resuelto en 3.6):** los procesos de fondo sin ámbito (rol `residapp_tareas`) no pueden leer la zona por la seguridad por filas; y revisar todas las comparaciones de fecha dentro de SQL contra el día UTC.
- **Preguntas abiertas que deja F1:** (a) tras un `CAMBIO_DE_UBICACION`, las horas anteriores al traslado se leerían en la zona nueva (con `numero` y `cambiado_en` se podría usar la zona vigente en cada momento); (b) un informe firmado no debe cambiar de hora al reimprimirse tras una corrección de zona (el PDF de derivación imprime la hora de la firma en la zona del centro): comprobarlo con ingeniería y CJ.

### 3.6 Cómo se construyó F2 (2026-11-21)

- **Script `0110`.** Una vista `zonas_vigentes_centros` (la zona vigente de cada centro; sin fila, Madrid; ni la aplicación ni la tarea de correo la leen) y los tres procedimientos de avisos reescritos con `ALTER`: `reservar_recordatorios_cita_en`, `reservar_avisos_citas_externas_en` y `comprobar_aviso_cita_externa_en`. Cada uno calcula «hoy», «mañana» y la franja de envío (10:00–18:59) **con la zona de cada centro**: la reserva hace una vuelta por cada zona que esté ahora dentro de su franja y solo con los centros de esa zona; la comprobación del aviso usa la zona del centro del aviso. Con todos los centros en Madrid el resultado es el de antes.
- **La tarea de correo (`residapp_tareas`) no lee `zonas_centro`** (seguridad por filas) y no hace falta que la lea: pregunta la zona a un procedimiento, `zona_horaria_del_centro`, que solo devuelve el nombre de la zona de un centro y solo ella puede ejecutar.
- **En el código.** Los servicios `ReminderService` y `ExternalNoticeService` ya no filtran «reservar» con una hora única (lo decide la base) y, antes de reclamar cada aviso, miran la franja **con la zona de su centro**; si no se puede leer la zona, el aviso no se reclama y se reintenta en la vuelta siguiente (mejor esperar que enviar fuera de la franja del centro). La comprobación de «mañana» del recordatorio (`SqlReminderStore`) usa también la zona del centro.
- **Un defecto antiguo, corregido de paso.** El `CHECK` de `0071` (`fotos_residente`) comparaba la fecha de consentimiento —un día del centro— con el día UTC, y fallaba en la primera hora tras la medianoche de Madrid; ahora admite un día de diferencia. No hay más comparaciones de ese tipo en los scripts.
- **Lectura del DPO (2026-11-21):** admisible; sin datos personales nuevos. Condiciones aplicadas: el cambio del `CHECK` va en una transacción y la aplicación no puede leer la vista. **Para cuando se active una zona nueva:** (a) al corregir la zona de un centro con recordatorios ya reservados, uno puede repetirse o saltarse ese día (la clave es la fecha local): convendría una frase en el aviso de confirmación de Plataforma; (b) el cálculo del inicio del día supone que el cambio de hora no ocurre a medianoche (cierto en España y Canarias): cualquier zona que cambie la hora a las 00:00 necesita revisarse en su propio estudio.

### 3.7 Cómo se construyó F3 (2026-11-21)

- **Script `0111`:** activa Canarias (`activa = 1`); antes comprueba que `0110` está aplicado y que el servidor conoce «GMT Standard Time». Los centros que ya existen siguen en Madrid. Plataforma ya ofrece «Canarias» al crear un centro y al corregir la zona de uno.
- **«Hoy» de un centro es el de su zona.** Las citas externas (registro, listados, plazos), la baja de un residente (que cancela las citas externas abiertas de hoy en adelante) y el recuento previo a la baja usaban el día de Madrid de los módulos; ahora usan `CenterTime`. El día de Madrid (`TodayInMadrid`) queda solo para lo que es de Madrid por decisión de Orlando: la vigencia de los contratos de módulos y Plataforma. Una prueba de arquitectura lo vigila.
- **Textos:** «hora de Madrid» pasa a «hora del centro» en el Manual, el Flujo de trabajo y las guías de Enfermería y Medicina (hora prevista de los seguimientos, recordatorio del día antes); los de Plataforma no cambian. El aviso amarillo de «Corregir la zona horaria» y su guía (PLA-12) añaden que los recordatorios ya reservados pueden repetirse o no salir ese día.
- **Pruebas:** el catálogo y el servidor coinciden en el desfase de cada zona en invierno y en verano (.NET y `AT TIME ZONE`); las pruebas de zonas ya no encienden y apagan Canarias; las de F2 cubren la reserva, el envío y la comprobación de un centro de Canarias frente a uno de Madrid.
- **Queda abierto (decisiones de una persona, de F1):** (a) tras un `CAMBIO_DE_UBICACION`, las horas anteriores al traslado se leen en la zona nueva; (b) un informe firmado no debe cambiar de hora al reimprimirse tras una corrección de zona. Mientras ningún centro cambie de zona después de existir, no se manifiestan; conviene decidirlas antes de usar Canarias con datos reales.
- **Pendiente menor:** la guía 1 de pruebas de CJ (`pruebas-1-auxiliar-enfermeria.html`, línea ~573) dice «hora de Madrid» en un paso de seguimientos; cambiarlo exige subir su `data-version`, así que se deja para cuando se toque esa guía (decisión de Orlando).

### 3.8 Zonas con varias horas de diferencia y cambios de hora a medianoche (2026-11-22)

Madrid y Canarias están a 0–2 horas de UTC: un error de «día UTC frente a día del centro» solo se nota una o dos horas al día. Una zona de América (por ejemplo, la costa este de EE. UU. o México, a 4–6 horas por detrás de UTC) lo notaría unas cinco horas cada tarde-noche. Y una zona que cambia de hora **a las 00:00** tiene un día que mide 23 horas, en el que las 00:00 no existen (o se repiten). Esta fase endurece el código para ambas cosas **sin añadir ninguna zona al catálogo**; las zonas nuevas entran inactivas en una fase siguiente (script `0112`) y se activan una a una, cada una con su estudio de cumplimiento.

- **«Inicio del día» en un solo sitio:** `CenterTime.StartOfDayUtc(día, zona)` devuelve el instante UTC en que empieza un día del centro; si las 00:00 no existen, el día empieza a la primera hora válida (el instante del cambio), y si se repiten, la primera vez. Lo usan `SupervisionIndicatorRules.UtcBounds` (panel de Administración, Dirección y auditoría: **antes lanzaba `ArgumentException`** en un día de cambio de hora a medianoche) y el cierre cotidiano del Auxiliar. `CenterTime.FromLocal` toma, en una hora que se repite, la primera vez (la del horario de verano); la generación de franjas de citas y la conversión de los bloqueos de la agenda usan esa misma función.
- **«Hoy» del centro en lugar del día UTC:** el «cerrado hoy» de la lista del Auxiliar comparaba el día UTC de `ocurrido_en` con el día UTC de ahora (se reiniciaba a las 19:00 locales con UTC-5); ahora compara con los límites del día del centro. La fecha de nacimiento y los avisos de hitos usan también el día del centro. Una prueba de arquitectura prohíbe `CAST(<columna>_en AS DATE)` y `SYSUTCDATETIME()` convertido a fecha en el código y en los scripts posteriores a `0110`.
- **El servidor tiene que conocer las zonas activas:** al arrancar se comprueba (`CatalogZoneVerifier`); en producción, una zona activa que el servidor no resuelve impide el arranque (en Linux hace falta `tzdata` con ese nombre). El registro del servidor deja un **error** (no un aviso) si un centro cae a la zona de Madrid porque su zona no resuelve.
- **Quedan como están, a propósito o por ser el lado seguro:** la vigencia de los contratos de módulos y Plataforma siguen en hora de Madrid (decisión de Orlando, 2026-11-16: con UTC-5 un contrato «hasta el día 30» vence a las 18:00 locales del 29); `RetainUntil` de una baja usa el día UTC y sale un día más largo, nunca más corto; el tope de año de las fichas de antecedentes (`0096`, `0097`) es algo más permisivo, nunca rechaza un dato bueno.
- **Lo que no se puede dar por hecho al activar una zona:** que las bases de datos de zonas horarias de Windows (SQL Server) y de Linux (`tzdata`, .NET en el CI y en Azure) coincidan. Con los datos de esta máquina, una zona de América del Caribe sigue en horario de verano hasta diciembre de 2026, algo que hay que contrastar con la norma oficial antes de activarla. La fase siguiente refuerza la prueba de coherencia (`.NET` frente a `AT TIME ZONE`, en los dos sentidos, todo 2026–2027) y una zona que no la pase no se activa.
- **Una hora repetida en un procedimiento de reservas** (`reservar_recordatorios_cita_en`): `AT TIME ZONE` puede errar una hora un solo día al año en una zona que repite las 00:00; el servicio vuelve a comprobar con la zona del centro y omite el aviso (`CITA_NO_VIGENTE`): falla por el lado seguro.

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
