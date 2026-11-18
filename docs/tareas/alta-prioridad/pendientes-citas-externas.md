# Citas externas de los residentes: traspaso para construirlas

**Estado a 2026-11-18: PR 4 (nota «al terminar» y siguiente cita) FUSIONADO (#330); siguiente, PR 5 (familia, solo ver): el plan y las siete decisiones de Orlando están en el apartado 10 (consultados el DPO y doc-usuario); sin construir. Antes: PR 3 (calendario del personal y vista del Auxiliar) FUSIONADO (#328, `fe47aa6`); siguiente, PR 4 (nota «al terminar» y siguiente cita). Antes: PR 2 (pantallas del personal) FUSIONADO (#327, `7df5842`). A 2026-11-17 (noche): PR 1 FUSIONADO (#325, `0e8a274`: script `0101`, dominio, servicio, repositorio, baja y pruebas; sin pantallas). Lo hecho y lo que cambió está en la primera entrada «RELEVO» de `docs/prompts-ia/historial/2026-11-02_a_2026-11-18.md`. El siguiente es el PR 5: **plan en el apartado 10** (el apartado 9 era el del PR 2, ya hecho; los apartados 5 a 8 siguen como estaban). Para el PR 4 queda el relevo de `docs/prompts-ia/historial/2026-11-02_a_2026-11-18.md`.** Diseño aprobado por Orlando y lectura del DPO hecha. Este documento es el punto de entrada para una sesión nueva. Léelo entero antes de tocar nada; después, `docs/producto/citas-externas-diseno.md` (las decisiones) y `docs/privacidad/citas-externas-lectura-dpo.md` (las condiciones de privacidad).

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

## 10. PR 5: familia, solo ver (PLAN decidido el 2026-11-18; sin construir)

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
