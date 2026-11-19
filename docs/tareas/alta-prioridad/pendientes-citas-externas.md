# Citas externas de los residentes: traspaso para construirlas

**Estado a 2026-11-19: PR 5 (la familia solo ve) FUSIONADO (#337, `fd338ca`; script `0104`; ver el apartado 10 y «Estado actual» de `docs/prompts-ia/continuar-construccion.md`); siguiente, PR 6 (la familia actúa y correos): PLAN decidido por Orlando en el apartado 11, sin construir. Antes: a 2026-11-18, PR 4 (nota «al terminar» y siguiente cita) FUSIONADO (#330). Antes: PR 3 (calendario del personal y vista del Auxiliar) FUSIONADO (#328, `fe47aa6`); siguiente, PR 4 (nota «al terminar» y siguiente cita). Antes: PR 2 (pantallas del personal) FUSIONADO (#327, `7df5842`). A 2026-11-17 (noche): PR 1 FUSIONADO (#325, `0e8a274`: script `0101`, dominio, servicio, repositorio, baja y pruebas; sin pantallas). Lo hecho y lo que cambió está en la primera entrada «RELEVO» de `docs/prompts-ia/historial/2026-11-02_a_2026-11-18.md`. El siguiente es el PR 5: **plan en el apartado 10** (el apartado 9 era el del PR 2, ya hecho; los apartados 5 a 8 siguen como estaban). Para el PR 4 queda el relevo de `docs/prompts-ia/historial/2026-11-02_a_2026-11-18.md`.** Diseño aprobado por Orlando y lectura del DPO hecha. Este documento es el punto de entrada para una sesión nueva. Léelo entero antes de tocar nada; después, `docs/producto/citas-externas-diseno.md` (las decisiones) y `docs/privacidad/citas-externas-lectura-dpo.md` (las condiciones de privacidad).

## 1. Qué hay que construir

Un registro de las citas del residente **fuera del centro** (pruebas, consulta, tratamiento en hospital de día o fisioterapia, otra), con calendario para el personal y para la familia, avisos cuando falta transporte o acompañante y la acción de la familia «Me encargo yo» / «No puedo» / «Ya no puedo». **Módulo base** (decisión de Orlando, 2026-11-17), con interruptores por centro, apagados por defecto, para la parte de la familia y para los correos (RGPD art. 25.2), y un interruptor por residente («la familia no ve las citas externas de esta persona»).

## 2. Qué leer, en este orden

1. `docs/producto/citas-externas-diseno.md`: 17 valores por defecto, las nueve decisiones de Orlando (apartado 2 bis) y el plan en seis PR.
2. `docs/privacidad/citas-externas-lectura-dpo.md`: condiciones de diseño. Es un **borrador** que valida una persona antes de datos reales. Ojo: Orlando decidió **apartarse** de dos recomendaciones del DPO (sin especialidades ocultas por defecto y correo de 7 días en toda cita visible); el riesgo se acepta por escrito en la EIPD del centro. El DPO ya está consultado: no hace falta volver a lanzarlo para el PR 1, salvo que cambie el esquema respecto al diseño.
3. Las respuestas de CJ: viven en el servidor. Si no tienes `respuestas-cj-local/citas-externas-modulo-cj.respuestas.json`, pídele a Orlando que te avise y ejecuta `tools/respuestas-cj.ps1` (no leas ni imprimas la clave). **Lee los comentarios de la 3.3 y la 4.2** antes de construir. No copies comentarios de CJ a ningún fichero del repositorio (es público).
4. `docs/pendientes-cj/citas-externas-modulo-cj.html` (leer el texto): la ficha 2.1, la tabla de perfiles 2.2, los avisos 2.3 y los bocetos de calendario.

## 3. Decisiones ya tomadas (resumen; manda el diseño)

- Administración registra y cambia **siempre**; Enfermería solo con el permiso nuevo `EXTERNAL_APPOINTMENT_MANAGE` (apagado por defecto, lo concede Administración por perfil); Medicina solo ve; el Auxiliar ve solo hoy y mañana, sin especialidad, lugar, nota ni nombre del familiar (prueba por reflexión sobre el modelo de la vista); Dirección Clínica y Plataforma, no.
- Fecha del centro (hora de Madrid) y hora opcional (vacía = «por confirmar»); **no UTC**.
- Estados: programada, confirmada por el hospital, realizada, no se acudió, cancelada. **«Reprogramada» no es un estado**: es un cambio de fecha en el historial.
- Tipo: prueba, consulta, tratamiento, otra. Especialidad: lista cerrada, sin texto libre. Lugar: una línea de 80 caracteres, solo el nombre del centro sanitario. Qué preparar: casillas cerradas más 100 caracteres. Motivo de cancelar: lista cerrada más 100 caracteres.
- Visibilidad para la familia: tres niveles por cita, elegidos a mano: **visible** (por defecto), **sin especialidad** (se ve el lugar) u **oculta** (sin avisos ni «Me encargo yo»).
- Suspensión del residente: las citas siguen vivas con la marca «residente suspendido». Baja: las futuras se cancelan solas con causa `BAJA_RESIDENTE` y texto fijo (patrón de `0078`).
- Aviso de «falta transporte o acompañante» (7 y 2 días): solo a Administración, en su inicio, sin especialidad ni lugar, calculado al abrir (sin tabla ni tarea programada).
- Nota «al terminar»: la escribe Enfermería, la leen Enfermería y Medicina, **no Administración**; nunca la ve la familia; lista cerrada más 150 caracteres, con versiones y autoría.
- Correos a la familia: plantillas nuevas (no reutilizar `RECORDATORIO_CITA`), un mensaje por persona, comprobación antes de enviar. A 7 días, la víspera y, a los 2 días, solo si sigue sin cubrir.

## 4. Plan en seis PR

1. **Esquema y reglas, sin pantallas** (el que toca empezar).
2. **Pantallas del personal** (registrar, cambiar, cancelar, dar por realizada, lista «Próximas», aviso en el inicio de Administración).
3. **Calendario del personal** mensual y semanal y vista del Auxiliar.
4. **Nota «al terminar»** y siguiente cita.
5. **Familia, solo ver** (interruptores por centro y por residente, nivel de visibilidad por cita, calendario).
6. **Familia, actuar, y correos** («Me encargo yo», «No puedo», «Ya no puedo», plantillas).

## 5. PR 1: qué hacer exactamente (HECHO y fusionado el 2026-11-17, PR #325; se conserva como referencia de lo construido)

**Rama** desde `origin/main` (haz `git fetch` antes). **Script `0101`** (el siguiente libre; `0100` es la marca de entorno). Plantillas a copiar: `0094_ficha_antecedentes_alergias.sql` (tablas de solo inserción, seguridad por filas, permiso nuevo con `CK_pp_code` y `TR_pp_profile_catalog`), `0054_citas_reserva_directa.sql` y `0078_baja_cancela_citas.sql` (citas con historial y causa de baja) y `0091_permisos_aplicacion.sql` (permisos mínimos del usuario de la aplicación).

- **`citas_externas`** (modificable): `id`, `centro_id`, `residente_id` (clave foránea compuesta a `residentes(centro_id, id)`), `fecha DATE`, `hora TIME(0) NULL`, `tipo_codigo`, `especialidad_codigo NULL` (lista cerrada con CHECK), `lugar NVARCHAR(80) NULL` (sin saltos de línea), `transporte_modo`/`transporte_estado`, `acompanante_modo`/`acompanante_estado`, las casillas de «qué preparar» (columnas bit) y `preparar_texto NVARCHAR(100) NULL`, `estado_codigo`, `visibilidad_familia_codigo` (VISIBLE por defecto, SIN_ESPECIALIDAD, OCULTA), `version INT` (bloqueo optimista), autoría y fechas. Coherencia por CHECK: «no necesita» implica confirmado; «sin determinar» implica pendiente.
- **`citas_externas_cambios`** (solo añadir, con `INSTEAD OF UPDATE, DELETE`): acción, cuenta, perfil, fecha, valores anterior y nuevo de fecha y estado, motivo de lista cerrada más línea de 100, y `causa_codigo` (`BAJA_RESIDENTE`) que solo admite un trigger cuando hay baja en la misma transacción (como `TR_citas_cambios_causa`, `0078`).
- **Seguridad por filas** por centro (`ALTER SECURITY POLICY seg.pol_centro ADD FILTER/BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id)`), `GRANT`/`DENY` al rol `residapp_aplicacion` en el propio script, **`CK_pp_code` y `TR_pp_profile_catalog`** con `EXTERNAL_APPOINTMENT_MANAGE` solo para `ENFERMERIA`. Copia la lista completa vigente de `0094` (es el último script que redefine `CK_pp_code` y `TR_pp_profile_catalog`; `0096` y `0097` no los tocan) y **añade** el permiso: el CHECK y el trigger se reescriben enteros.
- **Código:**
  - `src/ResidApp.Domain/Accounts/ProfilePermissions.cs`: constante, `For(Enfermeria)` y su etiqueta (busca `HealthBackgroundEdit` en `src/` para ver todos los sitios: `DireccionModels.cs`, `HealthBackground*`, `ProfilePermissions`, las guías `enfermeria.json` y `medicina.json`).
  - `database/seed/centros_prueba.sql`: conceder el permiso a las Enfermerías de los centros de prueba (como se hizo con `HEALTH_BACKGROUND_EDIT`, líneas ~329 a 344).
  - Dominio nuevo `src/ResidApp.Domain/Appointments/` (enums con `[Code("...")]`, reglas de la ficha y de «cubierta»), puerto en `Application/Ports/`, servicio en `Application/UseCases/` y repositorio Dapper en `Infrastructure/Persistence/` siguiendo `HealthBackground*` y `SqlAppointment*`.
  - La baja: `SqlResidentStatusRepository.DischargeAsync` y `DischargeOutcome` (`IResidentStatus.cs`) cancelan también las citas externas futuras; acciones de auditoría en `AdministrativeAudit.cs` y `AuditoriaModels.cs`.
- **Auditoría** solo con identificadores, nunca el contenido de un campo.
- **Filtro por unidad** de Enfermería y Medicina: lo hace la aplicación (la base solo separa por centro). Mira cómo lo resuelven `SqlChangeInboxDirectory` y las lecturas de `HealthBackground*` antes de inventar nada.

**Pruebas del PR 1** (modelo: `FichaAntecedentes*Tests.cs`, `BajaCancelaCitasTests`):

- Reglas de dominio (unitarias): coherencia de modos y estados, longitudes, saltos de línea, cubierta o no.
- Integración: aislamiento entre centros (incluida la cookie de ámbito manipulada: el centro sale del ámbito verificado), entre unidades y de Plataforma (no ve citas externas); permiso (Enfermería sin él, con él y revocado); concurrencia con la versión; la baja cancela solo las futuras de ese residente y con causa y texto fijos; el historial no se puede cambiar ni borrar; la causa solo se admite con baja en la misma transacción.
- `CoberturaRlsTests` y `PermisosAplicacionTests` obligan a cubrir cada tabla nueva (compruébalo en local antes de dar nada por hecho).
- Sin pantallas en este PR, así que sin pruebas funcionales por HTTP y **sin cambios en la documentación de usuario** (una línea en el mensaje final: «no afecta a la documentación de usuario»).

**Al cerrar el PR 1:** actualiza `docs/privacidad/antes-de-datos-reales.md` (un apartado nuevo con las condiciones de la lectura del DPO y lo que valida una persona experta) y `docs/privacidad/expurgo-y-supresion.md` (las tres tablas nuevas y su categoría; el catálogo de expurgo es un diseño **propuesto, no construido**: no hay una prueba que falle por una tabla nueva, así que anótalas a mano). Pasa a `qa-revisor-residapp` al terminar el PR 2 (bloque funcional), no antes.

## 6. Lo que falta decidir o confirmar

- **Con CJ**, en un documento breve (aún sin hacer; se prepara cuando se acerque la parte de la familia): que Administración no lea la nota «al terminar» y que el Auxiliar vea solo «Familiar». No bloquea el PR 1.
- **Para Orlando y el DPO:** la titularidad del centro piloto y si tiene plazas concertadas (AEPD o APDCAT). Cambia a quién se reclama y la cláusula, no el diseño.
- **A verificar** (lectura del DPO): que `sw.js` no guarde el calendario en el dispositivo; la terminología de `es_tutor_legal` frente a la Ley 8/2021; la regla 11 del Reglamento (UE) 2017/745; la Llei 12/2007.
- **Antes de usar la parte de la familia con familias reales:** validación de una persona experta (la guía de cumplimiento ya está contestada por CJ, pero la persona experta aún no existe) y la EIPD del centro con el riesgo de la especialidad visible.

## 7. Cómo trabajar (lecciones de esta sesión)

- Las decisiones de Orlando se le preguntan **de una en una** (AskUserQuestion), con explicación llana. Trátalo de tú, en español, y explica cada término técnico.
- **No pongas comillas invertidas dentro de un `node -e "..."` ni de un `echo "..."` en Bash:** el shell las ejecuta y se come el texto. Para textos con código, usa Write o Edit.
- El repositorio trabaja en CRLF; los avisos de «LF will be replaced by CRLF» al hacer commit son normales.
- Un PR de Orlando puede fusionarse antes de que subas los últimos commits: antes de abrir el siguiente, comprueba con `git show origin/main:<fichero>` que lo que crees fusionado lo está.
- Los comentarios de CJ no se copian a ficheros del repositorio (es público); se remite a las respuestas del servidor.
- Referencias de base para comparar: unitarias 654, integración 815 y funcionales 511 en verde en local a 2026-11-17 (una pasada de funcionales puede fallar por lentitud de la SQL local; repite antes de concluir nada). La base local `ResidApp` va por detrás de varios scripts: se pone al día con `database/aplicar-scripts.sh` y `APLICAR_SEED=1`; las pruebas usan `ResidAppPruebas`.

## 8. Prompt para empezar la sesión nueva

> Retomamos las citas externas de ResidApp. Lee `CLAUDE.md`, `docs/tareas/alta-prioridad/pendientes-citas-externas.md` (entero), `docs/producto/citas-externas-diseno.md` y `docs/privacidad/citas-externas-lectura-dpo.md`. Haz `git fetch` y comprueba el estado de `origin/main` y los PR abiertos. Después, propón el plan del PR 1 (script `0101`: esquema, seguridad por filas, permiso `EXTERNAL_APPOINTMENT_MANAGE`, baja de residente y pruebas de aislamiento) antes de escribir código, y pregúntame de una en una cualquier decisión que no esté ya en el diseño.

## 9. PR 2: pantallas del personal (HECHO y fusionado, #327; se conserva como referencia; propuesta de ingeniería; ajústala al implementar)

**Qué hay ya (PR 1, en `main`):** `ExternalAppointmentsApplicationService` (registrar, cambiar, cambiar de estado, cancelar, buscar, listar por rango de fechas; devuelve `CanManage`), `SqlExternalAppointmentRepository`, el dominio `Domain/Appointments/ExternalAppointments.cs` (enums con `[Display]` en español, `Covered`, `TimeToConfirm`) y los errores traducidos en `ApplicationResult.cs` (`ExternalAppointmentFailures`). Ninguna pantalla lo usa todavía.

**Qué hacer:**
1. **Permiso concedible:** añadir `ExternalAppointmentManage` a `ProfilePermissions.For(Enfermeria)` (y restaurar `"EXTERNAL_APPOINTMENT_MANAGE"` en `ProfessionalAccountTests`) **y a la vez** crear su ficha en `src/ResidApp.Web/Guia/enfermeria.json` con rutas que existan: `GuiaDerivaTests` falla si no (cada permiso de `For` necesita una ficha; cada ruta, una acción de controlador). La etiqueta del permiso ya está en `DireccionModels.PermissionLabel`.
2. **Controlador y vistas Razor** (Administración, Enfermería con permiso, Medicina solo ver): lista «Próximas» con filtro por fechas, ficha para registrar y cambiar, cancelar (motivo de la lista cerrada más línea de 100) y dar por realizada o «no se acudió». Muestra la marca «residente suspendido» (`ResidentSuspended`) y «Por confirmar» si no hay hora. Los selectores de enums deben salir **en español** (usa `[Display]`; regla de memoria «UI en español»). Sigue la guía de diseño (`docs/bocetos-pantallas/guia-diseno-sistema-visual.md`: Bootstrap 5, WCAG 2.2 AA, norma de iconos 2.E reutilizando los ya creados).
3. **Ayudas visibles junto a los campos** (condición del DPO, apartado 3): lugar «Solo el nombre del hospital o centro. No escribas el servicio, la unidad ni el motivo.» (`maxlength` 80, un `<input>` de una línea); «qué preparar» «Solo lo que hay que llevar o hacer para salir. Las indicaciones clínicas van por Medicina, no aquí.» (100); motivo «No escribas diagnósticos ni nombres.» (100). Nunca `textarea`.
4. **Aviso en el inicio de Administración** (solo Administración): citas que empiezan dentro de 7 días con transporte o acompañante sin cubrir (a 2 días o menos, más destacado), con residente, fecha y «falta transporte / acompañante», **sin especialidad ni lugar**; calculado al abrir, sin tabla ni tarea programada. Necesita una consulta nueva en el repositorio (aún no existe).
5. **La baja:** que `PreviewDischargeAsync` (resumen previo que ve Administración) cuente también las citas externas que se cancelarán, y que el mensaje de `AdministracionController.Estado.cs` use `DischargeOutcome.ExternalAppointmentsCancelled`.
6. **Módulo base:** las acciones **no** llevan `[RequiereModulo]` (decisión de Orlando: módulo base). Los interruptores por centro y por residente son del PR 5.
7. **Pruebas:** funcionales por HTTP de cada pantalla y permiso (modelo: `ResidentsFlowTests` y las de la ficha de antecedentes; un permiso se concede por el endpoint de permisos como en `HEALTH_BACKGROUND_EDIT`); una de traslado de unidad en integración (el residente cambia de unidad y la cita cambia de visibilidad: **sin probar en el PR 1**).
8. **Cierre del PR 2:** al cambiar texto visible, rutas y botones, consulta a `doc-usuario-residapp` antes de darlo por cerrado (Manual, Guía por perfil y flujo de trabajo; el sello «Revisado para ResidApp vX.Y»); comprueba la parte con JavaScript con la skill `probar-en-navegador`; **lanza `qa-revisor-residapp`** (es un bloque funcional). Actualiza el relevo.

**Para ver pantallas en local:** la base `ResidApp` (la de la aplicación) aún no tiene `0101`; ejecuta `database/aplicar-scripts.sh` con `APLICAR_SEED=1` (`SQL_SERVER=ACER-ORLANDO`, `SQL_DATABASE=ResidApp`). `ResidAppPruebas` (la de las pruebas) ya lo tiene.

**Referencias de pruebas a 2026-11-17 (tras el PR 1, en local):** unitarias 677, integración 847, funcionales 516.

**Pendientes que no bloquean el PR 2:** documento breve para CJ (que Administración no lea la nota «al terminar»; que el Auxiliar vea solo «Familiar»; y que revise la lista de especialidades); titularidad del centro piloto (AEPD o APDCAT); siete ramas locales viejas ya fusionadas, sin borrar, y `main` local por detrás de `origin/main`.

**Prompt para empezar el PR 2:**

> Retomamos las citas externas de ResidApp, PR 2 (pantallas del personal). Lee `CLAUDE.md`, `docs/tareas/alta-prioridad/pendientes-citas-externas.md` (entero, sobre todo el apartado 9) y `docs/producto/citas-externas-diseno.md`. Haz `git fetch` y comprueba `origin/main` y los PR abiertos (el PR 1 fue el #325). Propón el plan del PR 2 antes de escribir código y pregúntame de una en una cualquier decisión que no esté ya en el diseño.

## 10. PR 5: familia, solo ver (PLAN decidido el 2026-11-18; HECHO y fusionado el 2026-11-19, #337; se conserva como referencia de lo construido)

> **Lo construido difiere del plan en una cosa:** en vez de una tabla de estado y otra de historial por interruptor, hay **una tabla de solo añadir por interruptor** (`citas_externas_familia_centro` y `citas_externas_familia_residente`, patrón de `0098`): el estado vigente es la fila de mayor `numero`, que hace de versión. Los nombres `citas_externas_configuracion_centro` y `citas_externas_ajuste_residente` de 10.2 ya no existen. Los hallazgos de QA que quedan por decidir están en «Estado actual» de `continuar-construccion.md`.

Consultados `dpo-residapp` y `doc-usuario-residapp` (sus conclusiones están integradas abajo). Orlando decidió una a una las siete cuestiones del apartado 10.1. No hay botones de acción ni correos: eso es el PR 6.

### 10.1 Decisiones de Orlando (2026-11-18)

1. **Encender el interruptor del centro:** confirmación expresa con recuento. Administración marca que la residencia ya informó a residentes y familias, que tiene el procedimiento de voluntad del residente y que actualizó la EIPD; la pantalla muestra cuántas citas abiertas se verían, por nivel, y enlaza a la lista para revisarlas. Se audita quién, cuándo y con qué versión de la fila. Lo enciende **Administración** (el DPO pregunta si debería ser Dirección; no se ha cambiado).
2. **Interruptor por residente:** lo cambian **Administración y Enfermería** (esta, solo de las unidades del residente y **sin** el permiso `EXTERNAL_APPOINTMENT_MANAGE`: es una herramienta de privacidad). Quitar la restricción pide confirmación y muestra cuántas citas se abrirían. Ambas direcciones se auditan con valor anterior y nuevo, sin texto libre de motivo. Medicina no (queda «solo ve»; se pregunta a CJ en el documento breve).
3. **Nivel «sin especialidad»:** la familia ve el **tipo general** («Consulta / Prueba / Tratamiento fuera del centro»; «Cita fuera del centro» si es «otra»), no la especialidad. Si la persona experta lo pide, bajar a «Cita fuera del centro» es un cambio de una línea. En `0101` solo existen prueba, consulta, tratamiento y otra: no hay «tipo detallado».
4. **Qué ve de lo que ya no va a ser:** una cita visible que se cancela sigue saliendo como «Cancelada», sin motivo, hasta su fecha original. Realizadas y «no se acudió» nunca se ven. La lista llega a **90 días**. Una cancelación por baja no aparece (el residente de baja no sale en `AccessibleLinks`).
5. **Interruptor del centro:** pantalla propia dentro de Citas externas de Administración («Ajustes para la familia»); no va en «Datos del centro» ni en «Citas» (que es de las citas con la familia).
6. **Nombre del enlace para la familia:** «Citas fuera del centro».
7. **Vistas:** lista por días (la principal, y la única en móvil) y mes con una marca neutra por día (sin tipo, especialidad ni número). Sin semana por ahora.

### 10.2 Qué hacer

**Script `0104`** (siguiente libre; mira antes `ls database/scripts`):
- `citas_externas_configuracion_centro`: `centro_id` PK, `familia_ve_citas BIT NOT NULL DEFAULT 0`, `version`, quién y cuándo. Historial de solo añadir (`INSTEAD OF UPDATE, DELETE`).
- `citas_externas_ajuste_residente`: clave compuesta a `residentes(centro_id, id)`, `familia_no_ve BIT`, `version`, quién y cuándo, con su historial de solo añadir. Sin fila = la familia ve.
- Seguridad por filas por centro, `GRANT`/`DENY` al rol `residapp_aplicacion` y matriz de `0091`; `CoberturaRlsTests` y `PermisosAplicacionTests` obligan a cubrir cada tabla. **Catálogo de expurgo** (a mano, ver `expurgo-y-supresion.md`): la del residente con la categoría del residente.
- Las acciones de auditoría nuevas (activar o desactivar el centro, restringir o liberar un residente) llevan solo identificadores y valor anterior y nuevo como código cerrado; añádelas a `AdministrativeAudit.cs` y `AuditoriaModels.cs`.

**Regla única de visibilidad** (se evalúa en cada lectura, sin caché en memoria): la familia ve una cita solo si el centro está encendido **y** el residente no está restringido **y** el nivel no es `OCULTA`. Sin fila de configuración = apagado. Si la consulta falla, no sale nada.

**Lectura de la familia:** puerto y repositorio nuevos (patrón de `FamilyAppointments` y `SqlFamilyAppointments`) con `SqlFamilyPortalReader.AccessibleLinks` (vínculo, autorización activa y vigente, residente `ACTIVE`; suspendido o de baja no muestra nada). Modelo propio `ExternalAppointmentForFamily`, **redactado en el servidor según el nivel** (en el SQL con `CASE` o en el mapeo; nunca «oculto en pantalla»):
- VISIBLE: día, hora (o «por confirmar»), tipo, especialidad, lugar, qué preparar, transporte y acompañante (modo y estado), si está cubierta.
- SIN_ESPECIALIDAD: lo mismo con el tipo general y sin especialidad.
- Nunca: nota «al terminar», motivo de cancelar, nombre de quien se encarga, historial, quién la registró, ni citas realizadas o «no se acudió».
- Prueba por reflexión sobre las propiedades del modelo (modelo: `ExternalAppointmentAuxiliarViewTests`).

**Pantallas** del Portal Familiar bajo `/Familia/Residente/{linkId}/…` (el nombre de la acción se decide al construir; la Guía exige que exista en el controlador):
- Lista por días, mes y detalle. El enlace sale en la pantalla del residente siempre que el centro esté encendido y el residente no restringido, **haya o no citas** (que el enlace aparezca o no dice algo). Con la lista vacía: «No hay citas que mostrar», no «No hay citas». Con el centro apagado o el residente restringido, la URL directa da el **mismo 404** que una cita inexistente, de otro residente o `OCULTA`.
- `Cache-Control: no-store` en estas pantallas (lectura del DPO, apartado 5.1) y ningún dato de la cita en títulos, `aria-label` ni URL. `sw.js` ya no guarda navegaciones.
- En el detalle: «Esta información la ve quien está autorizado a acompañar a [nombre del residente] y solo en la medida en que él o ella lo permite. Si crees que no debería verla, avisa al centro.» (DPO 10.3; esa frase solo es cierta si el centro tiene el procedimiento de voluntad del residente: depende de lo que valide la persona experta). **No** va la promesa «el centro se pondrá en contacto contigo».
- Junto al nivel «sin especialidad» del formulario del personal, un aviso: «Lugar y Qué preparar siguen viéndolos la familia: no escribas ahí la especialidad».
- Quitar el texto de `CitasExternas/Formulario.cshtml` (línea ~169) «La familia aún no ve las citas externas…».

**Pruebas:**
- Aislamiento: centro A no ve B (también con la cookie de ámbito manipulada), un `linkId` ajeno da 404 como uno inexistente, una familia vinculada en dos centros ve cada uno por separado, Plataforma, Auxiliar y Enfermería no entran por esa ruta; vínculo desvinculado, autorización revocada o caducada, residente suspendido y de baja.
- Cada nivel y cada interruptor; una cita `OCULTA` con un lugar reconocible no aparece en ningún HTML; en SIN_ESPECIALIDAD, ninguna etiqueta de especialidad (recorre todos los códigos) sale en el HTML; canceladas y el límite de 90 días; que el recuento de la confirmación no cuenta las `OCULTA`.
- Los interruptores: permiso por perfil y unidad, versión (bloqueo optimista), historial inmutable.
- Una prueba funcional que falle si en el Manual, la Guía o las vistas queda «aún no ve las citas externas» o «Todavía no ve las citas externas».

**Documentación de usuario (actualización larga; no obliga a re-sellar, todo sigue en 0.4.0):**
- `Manual.cshtml`: línea 26 (resumen por perfil); sección «Citas externas» (617, 631, 639; quitar la frase de 679 «La familia aún no ve…»); párrafo nuevo «Citas fuera del centro» en «Portal Familiar» junto a «Citas» (~1960), y reescribir el «Próximamente» de ~1993 para que solo conserve «que se ofrezca a llevar o acompañar» (es el PR 6).
- Guía: ficha nueva en `familia.json` (con su ruta real; retocar `proposito`, `siempre`, `nunca` y FAM-08), ficha nueva en `administracion.json` para «Ajustes para la familia» y un paso para el interruptor del residente (ADM-20 o ADM-47), retoques de ADM-47 (línea ~1518, ya no es «Todavía no ve») y ADM-48, y `enfermeria.json:763` (puede apagarlo por residente). Medicina y Auxiliar, sin cambios. `GuiaDerivaTests` exige rutas, botones, campos y títulos del Manual literales: escribe los textos desde las vistas.
- Flujo de trabajo: opcional, una línea en «La familia, informada». Acceso y sesión y las guías de CJ: sin cambios (no hay que subir ningún `data-version`; si el interruptor viniera encendido en los centros de ejemplo cambiaría `pruebas-6` paso 558: déjalo **apagado** en `centros_prueba.sql`).
- Con `probar-en-navegador` (JavaScript, sesión): que cada nivel se vea o no se vea, que el enlace desaparezca con cualquiera de los dos interruptores, que no haya botones de acción, el mes en móvil y escritorio, y el corte con residente suspendido o de baja. Cierra con `qa-revisor-residapp`.

**Al cerrar el PR 5:** `docs/privacidad/antes-de-datos-reales.md` y la EIPD del centro (los riesgos del apartado 10.3); `expurgo-y-supresion.md`.

### 10.3 Riesgos que deben constar para la EIPD y la persona experta (DPO, 2026-11-18)

- **El interruptor del residente es todo o nada**: no distingue entre familiares. «A mi hija no se lo cuentes» solo se resuelve ocultando una cita concreta o revocando a ese familiar. Abierta la decisión de un nivel por vínculo (Orlando, CJ y DPO real).
- Al encender el interruptor del centro, las citas ya registradas (todas nacieron `VISIBLE`) pasan a verse; por eso la confirmación con recuento.
- Que el enlace aparezca o desaparezca dice algo; si se restringe a un residente, un familiar atento lo deduce. Aceptado.
- La frecuencia de las marcas del mes y el patrón semanal pueden delatar un tratamiento aunque no salga la especialidad; `lugar` y `qué preparar` son texto libre y pueden repetirla (decisión 7 del diseño). El límite de 90 días lo reduce.
- Lo que un familiar ya vio antes de una restricción no se puede deshacer.
- La decisión 5 (la familia ve siempre la especialidad, apartándose del DPO) consta por escrito en la EIPD con responsable y fecha.
- Las lecturas de la familia no se auditan (decisión general).
- **Para el PR 6, anotado desde ya:** con el interruptor del residente apagado desaparecen las tareas de «Me encargo yo» y no salen correos, comprobándolo otra vez al enviar; y `AccessibleLinks` no mira que la cuenta esté activa (`SqlChangeNoticeStore.cs`), cosa que importa al enviar correos.

### 10.4 Prompt para empezar el PR 5

> Retomamos las citas externas de ResidApp, PR 5 (la familia solo ve). Lee `CLAUDE.md`, `docs/tareas/alta-prioridad/pendientes-citas-externas.md` (entero, sobre todo el apartado 10) y `docs/producto/citas-externas-diseno.md`. Haz `git fetch` y comprueba `origin/main` y los PR abiertos. El plan y las siete decisiones de Orlando ya están escritas en el apartado 10: no vuelvas a preguntarlas. Empieza por el script `0104` y sus pruebas de aislamiento y permisos antes de las pantallas.

## 11. PR 6: la familia actúa, y correos (PLAN decidido el 2026-11-19; sin construir)

Consultados `dpo-residapp` y `doc-usuario-residapp` (el 2026-11-19; sus conclusiones están integradas abajo: no repetir las consultas salvo que cambie el esquema). Orlando decidió una a una las ocho cuestiones del apartado 11.1. La rama es `feat-citas-externas-pr6-familia-actua`; ya lleva la corrección del relevo del PR 5.

### 11.1 Decisiones de Orlando (2026-11-19)

1. **Destinatarios del correo de 7 días y de la víspera:** a quien tenga un encargo vigente en esa cita; si nadie lo tiene, a todos los autorizados con correo confirmado y cuenta activa. El de 2 días («sigue sin cubrir») va solo a quien no tiene encargo ni dijo «No puedo». Las reglas se reevalúan **al enviar**; si entre medias alguien se encarga, el correo queda `OMITIDO`.
2. **Correo de 7 días en toda cita visible** (decisión 8 del diseño, sin acotar a citas con algo que cubrir). El DPO baja su condición 5.2(f) a recomendación, porque desde el PR 5 la familia ya ve la cita en el portal. Va con la baja por persona (decisión 4) y el riesgo aceptado por escrito en la EIPD, con responsable y fecha.
3. **«No puedo»:** se guarda como respuesta por familiar y cita (revocable; deja de verse al terminar la cita) y excluye a esa persona del correo «sin cubrir». El personal ve **solo un recuento** («N familiares han dicho que no pueden»), sin nombres, sin contadores históricos y sin informes por familiar. Riesgo anotado: con 2 familiares, el recuento permite deducir quién fue.
4. **Información previa y baja del correo (art. 13 y 21.4 RGPD):** tres piezas: la línea fija junto a los botones del portal; una frase fija en el pie de las plantillas («si no quieres recibir estos avisos, díselo a la Administración del centro»); y ampliar el texto del campo del correo, la cláusula del alta y la casilla «Dirección confirmada con la familia» (finalidad ampliada, art. 13.3). La baja es **un marcador por familiar que registra Administración**; el autoservicio en el portal queda para después. Ningún envío real hasta que la persona experta valide la base jurídica (6.1.f o 6.1.e) y el texto.
5. **«Cuenta activa» al enviar:** `cuentas.estado = 'ACTIVE'` más `AccessibleLinks` **más** el perfil `FAMILIAR` vigente en ese centro y la activación completada (una invitación sin aceptar no cuenta). `residapp_tareas` no puede leer `ambitos_perfil` (0093): hace falta un procedimiento `EXECUTE AS OWNER` que devuelva solo sí/no y la dirección. **A verificar:** cómo se detecta «activación completada» en el esquema actual.
6. **«Cubierta»:** un encargo cuenta como cubierto para el correo de la familia (no sale el de 2 días), pero el personal lo ve como «ofrecido por un familiar, sin confirmar»; el estado confirmado lo pone el personal. La ayuda del portal dice que no sustituye al teléfono.
7. **Quién ve el nombre de quien se encarga:** Administración y Enfermería con `EXTERNAL_APPOINTMENT_MANAGE` en las unidades del residente (los que pueden cambiar la cita). Medicina ve «un familiar»; el Auxiliar, «Familiar» (decisión 9 del diseño); las demás familias, «lo hará un familiar».
8. **Aviso cuando un familiar suelta una tarea:** no hay aviso nuevo; la cita vuelve a salir en «Citas externas con algo sin cubrir» del inicio de Administración (se calcula al abrir, sin tabla). Limitación aceptada: solo si la cita empieza dentro de 7 días.

### 11.2 Qué hacer

**Script `0105`** (siguiente libre). Tablas de solo añadir (patrón de `0098` y `0104`: `numero` como versión, `INSTEAD OF UPDATE, DELETE`, seguridad por filas por centro, `GRANT INSERT` y `DENY UPDATE, DELETE` a `residapp_aplicacion`, matriz de `0091` y catálogo de expurgo a mano):
- `citas_externas_encargos`: `id`, `centro_id`, `cita_id` (FK compuesta), `ambito` (`TRANSPORTE` o `ACOMPANANTE`), `numero` con `UNIQUE (cita_id, ambito, numero)` (esa unicidad es la asignación atómica: se inserta `max+1` bajo bloqueo y el perdedor recibe la violación), `accion` (`ME_ENCARGO`, `SOLTAR`, `LIBERADO_POR_PERSONAL`), `vinculo_id` (FK `(centro_id, id)` a `residentes_familiares`), `actor_perfil`, `actor_cuenta_id` (NULL si el actor es `FAMILIAR`: no se guarda la cuenta global), `causa_codigo` cerrada, `cambiado_en`, y una clave de idempotencia (patrón `operacion_id` de `0102`) para la doble pulsación. Sin nombre ni texto libre. «Quien lo tiene ahora» = última fila `ME_ENCARGO` si el vínculo sigue pasando `AccessibleLinks` más la decisión 5 y el modo de la cita sigue siendo de familia; la suelta automática se calcula al leer, no se escribe (así la anonimización `0087` no toca esta tabla; el nombre se resuelve con `JOIN` y sale «Persona anonimizada»).
- `citas_externas_no_puedo`: `centro_id`, `cita_id`, `vinculo_id`, `numero`, `respuesta` (`NO_PUEDO` o `REVOCADA`), `cambiado_en`. Nunca con nombre ni agregada por familiar.
- Marcador de baja del correo por familiar (decisión 4), registrado por Administración.
- Interruptor de **correos por centro** (solo añadir, apagado por defecto, tres confirmaciones como el de la familia): no se puede encender si el de la familia está apagado.
- **Correo:** índice único nuevo en `envios_correo` que incluya `familiar_id` (**hallazgo grave**: `UX_envios_correo_referencia` es `(plantilla_codigo, referencia_id, referencia_clave)`, y con «un mensaje por persona» solo se reservaría el primer familiar; `SqlChangeNoticeStore.RegisterAsync` ignora la violación como «ya reservado»; `0081` no se edita). Plantillas nuevas `AVISO_CITA_EXTERNA` y `AVISO_CITA_EXTERNA_SIN_CUBRIR` (máx. 32 caracteres), clave `7D|aaaa-mm-dd`, `1D|…`, `2D|…` (cabe en los 40 de `referencia_clave`), franja 10:00 a 18:59, reserva por procedimiento `EXECUTE AS OWNER` sin `INSERT` para `residapp_tareas` (`0092`), motivos nuevos (`YA_CUBIERTA`, `FAMILIA_NO_VE`, `FUERA_DE_PLAZO`). Reescribir `dbo.cerrar_envios_correo_sin_resolver` (`0089`) para las plantillas nuevas, incluida la regla de «sin reclamar más de 24 horas» que `0092:132` aplica solo a `RECORDATORIO_CITA`. `residapp_tareas` no lee lugar, especialidad ni «qué preparar». Interruptor por entorno y por plantilla (patrón de `CorreoOptions`). Texto sin datos (sin residente, centro, hora, especialidad ni detalle), prueba de comparación exacta; no reutilizar `RECORDATORIO_CITA`; sin `Cc` ni `Cco`; catalán marcado como borrador.
- Acciones de auditoría nuevas, solo identificadores y perfil `FAMILIAR` (tomar, no poder, soltar, encender o apagar los correos del centro, baja de un familiar), en `AdministrativeAudit.cs` y `AuditoriaModels.cs`.

**Regla única:** el botón, la tarea y el correo reutilizan la regla de visibilidad del PR 5 (`SqlFamilyExternalAppointments`); no se hace otra copia. Se aplica al reservar y otra vez al enviar. Una restricción puesta después de reservar el correo lo corta. Con el interruptor del residente apagado o la cita `OCULTA`, no hay botones ni correo. «Me encargo yo» solo existe mientras `transporte_modo = FAMILIA` o `acompanante_modo = FAMILIAR` y el estado esté pendiente; si el personal cambia el modo, la fecha o cancela, el encargo deja de contar y el portal lo dice con un texto fijo. Los tramos de 7 y 2 días que ya no se pueden cumplir (cita registrada o movida tarde) se omiten con `FUERA_DE_PLAZO`; la víspera sí sale. La familia no modifica `citas_externas`.

**Pantallas:** detalle de la cita en el Portal Familiar con los tres botones y el texto junto a «Me encargo yo» («Si pulsas «Me encargo yo», el personal del centro verá tu nombre. Otros familiares solo verán que lo hará un familiar. El centro no te llamará solo por esto: las citas no son un canal urgente.»; **no** va «el centro se pondrá en contacto contigo»); interruptor de correos y marcador de baja en «Ajustes para la familia» y la ficha del familiar (`Administracion/Familiar.cshtml`, `ayuda-correo`); nombre o «ofrecido por un familiar, sin confirmar» y recuento de «No puedo» en la ficha de la cita del personal.

### 11.3 Documentación de usuario (actualización larga; **aplicada en este PR con el visto bueno de Orlando, 2026-11-19**: Manual, Guía de Familia con FAM-10 y FAM-11, Administración con ADM-50 y retoques de ADM-28, ADM-47 y ADM-49, Enfermería, Medicina, Flujo, `AjustesFamilia.cshtml` y la lista `Stale` del test; quedan sin tocar `Privacy.cshtml:106`, que valida una persona con el DPO, y `docs/pendientes-cj/pruebas.html:165`, que es de CJ; AUX-09 no cambia porque el Auxiliar no ve nada de los ofrecimientos; sin subir sellos ni `data-version`)

Unas 12 frases de «solo ve» quedan falsas en 7 documentos: `Manual.cshtml` (L.1985 «solo para verlas», L.1986, el «Próximamente» de L.2015 que se quita, L.687, L.677 que pasa a tres interruptores, L.627, L.632 y L.640 «Cubierta», L.690-692, L.584/586/605 «correos para todo el entorno», aviso de Administración, L.26); `familia.json` (L.3, 17, 25, 317, 326, 332); `administracion.json` (L.1518, 1598, 872/883, 1526); `enfermeria.json` (L.785, 868); `flujo-de-trabajo.html` L.443; `AjustesFamilia.cshtml:33-34`; `CitaFuera.cshtml:3`; `Administracion/Familiar.cshtml:75-89`. Fichas nuevas (los códigos son correlativos): **FAM-10 y FAM-11** (Me encargo yo / No puedo; Ya no puedo; y los correos que recibe la familia, quizá junto a FAM-07), **ADM-50** (interruptor de correos) y retoques de ADM-01, ADM-28, ADM-47, ENF-23, ENF-25, MED-17 y AUX-09. Ampliar la lista `Stale` de `CitasExternasFamiliaDocumentacionTests` («solo para verlas», «solo para ver», «Por ahora no se puede desde el portal», «no se pueden cambiar ni confirmar desde el portal», «Sin botones de acción»); `PortalFamiliarFlowTests.cs:914` y `:924` afirman `DoesNotContain("Me encargo yo")` y deben cambiar a propósito. **Fuera del registro:** `Views/Home/Privacy.cshtml:106` (política pública: «ni se enciende centro por centro», «hoy hay tres» avisos) se contradirá: lo valida una persona con el DPO. `docs/pendientes-cj/pruebas.html:165` («Qué NO se prueba») conviene ampliarlo. No se sube ningún sello ni `data-version`; el seed de los centros de ejemplo deja **apagados** el interruptor de correos y el de la familia (si no, cambian pasos de `pruebas-6` y `pruebas-7`).

### 11.4 Riesgos para la EIPD y `antes-de-datos-reales.md` (14.2)

Volumen de correo a todos los autorizados (decisión 2, aceptado); interruptor del residente todo o nada (heredado); «No puedo» como dato del familiar y prohibición de perfilar; recuento deducible con pocos familiares; encargo desfasado tras reprogramar o cancelar (no hay aviso de cambio de citas externas, riesgo aceptado: quien se comprometió se entera solo si entra al portal); correo a una cuenta que ya no debería recibirlo; falsa seguridad del «Me encargo yo»; pérdida de correos por la clave de envío (resuelta con el índice nuevo); el correo no se audita (la huella es la fila de `envios_correo`). Lo que valida una persona experta antes del primer correo real: base jurídica del dato del familiar y su cláusula, información previa y baja, retención (propuesta: 12 meses desde la fecha de la cita, como `envios_correo`, y borrado antes con el expurgo del residente), contrato y configuración de Azure Communication Services (B10 y DMARC en `quarantine`), catalán, «borrar o bloquear» (art. 32 LOPDGDD) y la confirmación escrita de CJ de que FAM-10 incluye estos avisos.

### 11.5 Pruebas y cierre

Empezar por `0105` y sus pruebas de aislamiento, permisos y concurrencia real (dos pulsaciones a la vez; doble pulsación del mismo familiar), antes de las pantallas. Cada regla de envío con su escenario: residente suspendido, de baja o restringido; cita `OCULTA`; perfil `FAMILIAR` revocado con la cuenta activa; invitación sin aceptar; familiar dado de baja del correo; encargo que cambia entre reservar y enviar; plantilla, clave y franja; dos familiares de la misma cita reciben un mensaje cada uno. Ampliar `AislamientoPortalFamiliarTests` con los métodos nuevos del puerto. Ejecuta **cada proyecto de pruebas por separado**. Cierra con `qa-revisor-residapp`, `probar-en-navegador` (botones, «lo hará un familiar», recuento, interruptor de correos; los correos reales solo con el interruptor de entorno y la lista de direcciones permitidas), docs y relevo. El hito 0.5.0 (#302) se cierra al acabar el PR 6.

### 11.6 Lo construido y lo que cambió respecto al plan (2026-11-19)

PR 6 construido en la rama `feat-citas-externas-pr6-familia-actua` (script `0105`). Diferencias y decisiones de ingeniería que Orlando puede cambiar:

- **El cálculo de los avisos es un procedimiento (`dbo.calcular_avisos_citas_externas`), no una función en línea:** como función recalculaba los encargos y el acceso de todo el sistema por cada fila y tardaba minutos; por pasos y con tablas temporales tarda unos segundos en una base con 3.000 citas. La regla de acceso es `fn_familiares_con_acceso(vínculo, día)` (por vínculo, para que el plan busque por clave).
- **Interruptor de correos del centro con UNA confirmación** («la residencia ha informado a las familias»), no tres como el de las familias; exige que las familias estén abiertas. **Decidido por Orlando (2026-11-19): una confirmación.**
- **El aviso de 2 días solo sale si la familia aún tiene algo que cubrir** (modo `FAMILIA` o `FAMILIAR` pendiente y sin encargo): no si lo que falta es del centro, de una ambulancia o está sin determinar. **Decidido por Orlando (2026-11-19): así.**
- **Un encargo desfasado «resucita»** si la cita vuelve a su fecha y modo (se calcula al leer, no se escribe); la persona ve un texto fijo cuando su encargo deja de contar. **Decidido por Orlando (2026-11-19): que resucite, sin suelta automática.**
- **Un encargo solo cuenta mientras la parte está pendiente** (si el personal la confirma, deja de figurar como «ofrecido»), y a 7 días y la víspera solo silencia a los demás si quien tiene el encargo puede recibir el correo (acceso a la fecha de la cita, dirección confirmada y sin baja).
- **Sin interruptor «por plantilla»** además del de entorno (`Correo:AvisoCitaExterna`) y el del centro. Menor.
- **Hallazgos de QA sin prueba: cerrados (2026-11-19).** Ahora tienen prueba: residente suspendido o de baja al actuar y al comprobar el aviso; citas a 90 y 91 días; `CorreoOptions.AvisoCitaExterna` apagado por defecto; la franja 10:00 a 18:59 al minuto y con el cambio de hora (verano e invierno); citas `CONFIRMADA_HOSPITAL`; «Me encargo yo» frente a «Ya no puedo» y frente a la confirmación del personal, a la vez; baja de una persona con dos residentes; auditoría de la baja y el alta del aviso; persona anonimizada; invitación `ALTA` sin usar, anulada y usada; y criterios de aceptación en las historias de usuario (historia 7 de `portal-familiar.md` y 11 de `administracion.md`). Lo que las pruebas dejaron claro: al dar de baja al residente el aviso reservado se omite como `CITA_NO_VIGENTE` (la baja cancela sus citas externas), no como `SIN_ACCESO`; y una persona anonimizada no aparece nunca entre los ofrecimientos (pierde el acceso, así que su encargo no cuenta).
