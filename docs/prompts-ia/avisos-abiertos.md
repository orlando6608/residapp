# Avisos abiertos (fuera de alcance, sin corregir)

> Registro vivo de lo que se detectó durante otros bloques y no se corrigió por quedar fuera de su alcance. Movido tal cual desde `continuar-construccion.md` el 2026-11-18. Al corregir un aviso, bórralo de aquí; al detectar uno nuevo, añádelo aquí con su fecha y no en el relevo.

Se detectaron durante otros bloques. No se han corregido porque quedaban fuera de su alcance; propónselos al
usuario cuando encajen:

- **Inicio y permisos:** desde el bloque 4 de Administración, «Alta de residente» y «Rangos de referencia» solo salen con
  el permiso, y «Firmar borrador de basal» con el de basal inicial o el de reevaluar (2026-10-01). Si cambian las reglas
  de perfiles o permisos de una pantalla, revisa también los `@if` de `Views/Home/Index.cshtml`.- **Datos de prueba en Azure:** Residente Integrado Dos tiene abierto el escalado «Prueba manual escalados
  abiertos: tos productiva.», en valoración médica, que se dejó como ejemplo de la lista de escalados. El
  usuario confirmó el 2026-09-30 que toda la BD de Azure es de desarrollo, así que se pueden crear datos de
  prueba allí.

- **Rediseño visual, lo que queda (2026-10-04, tras el cierre):**
  - párrafos introductorios largos en muchas pantallas (Planificación, Estructura, Usuarios…): el usuario decidió no acortarlos mientras la app
    siga en desarrollo;
  - «Vencido» es ámbar en Dirección y rojo en el resto (coherente dentro de cada perfil): va con la pregunta a CJ de «Basal pendiente».
- **Pregunta para CJ (2026-10-03):** «Basal pendiente» sale en rojo, como fija la guía «por exigencia estricta del PRD», y compite con las urgencias
  reales. El documento de recomendaciones propone ámbar. Es una decisión clínica de saliencia: no se ha cambiado.
- **Preguntas para CJ (2026-09-30):** `docs/pendientes-cj/archivados/decisiones-direccion-basal-derivacion.html` recoge las finalidades de la lectura clínica de Dirección, la
  aportación a un borrador de basal ajeno y el campo «Comunicaciones» del informe de derivación. Revisa si
  CJ ha respondido antes de proponer el bloque 2 de Dirección o la aportación.
- **CI (anotaciones de GitHub Actions):**
  - `actions/checkout@v4` y `actions/setup-dotnet@v4` usan Node.js 20, que está obsoleto (hoy se fuerzan a
    Node 24);
  - `ubuntu-latest` pasa a Ubuntu 26 el 19 de octubre de 2026. Hecho el 2026-10-07 (PR #14): los dos jobs están fijados a
    `ubuntu-26.04`. El CI de `main` pasó entero, incluido el despliegue.
  - **sqlcmd sin apt (2026-11-11).** «Instalar sqlcmd» (apt, con el repositorio de Microsoft de Ubuntu 26.04) cayó en los tres intentos de ese día
    porque el runner no resolvía los servidores de Ubuntu (`Temporary failure resolving 'azure.archive.ubuntu.com'`; cada intento tardaba 9 minutos
    en rendirse), con la misma imagen de runner que había pasado una hora antes. Ahora el paso «Preparar sqlcmd»
    (`.github/scripts/preparar-sqlcmd.sh`, en los dos jobs) usa el `sqlcmd` de la imagen de SQL Server en un contenedor auxiliar, sin apt. Si falla,
    mira ese script y si `mcr.microsoft.com` responde. El envoltorio se probó en local contra un SQL Server de prueba (`-P`, `SQLCMDPASSWORD`,
    `-i` relativo, UTF-8 con y sin BOM, `-v`, códigos de salida); el job de despliegue solo se puede ejercitar en `main`.
- **Hora y zona horaria: pendientes (2026-10-12).** Hoy funciona porque `app-residapp-dev` tiene `WEBSITE_TIME_ZONE=Europe/Madrid` (ver
  «Zona horaria (hecho)» más arriba), pero hay límites que no estaban anotados. **Primer punto resuelto el 2026-10-12** (rama `zona-horaria-en-despliegue`): el despliegue (`ci-cd.yml`, paso «Fijar la zona horaria de la Web App») pone `WEBSITE_TIME_ZONE=Europe/Madrid` si no lo está (no escribe, y no reinicia la app, si ya lo está; la lectura se comprobó contra `app-residapp-dev`, la escritura no se ha ejercitado porque el valor ya era el correcto), y `ServerTimeZone.Verify` (en `Program.cs`) comprueba al arrancar que el servidor da en invierno y en verano el mismo desfase que Europe/Madrid: en producción no arranca y en desarrollo y pruebas avisa en el registro (`ServerTimeZoneTests`, 4 pruebas; fallan con la comprobación anulada). El resto sigue sin cambiar:
  - **La zona solo está puesta a mano en Azure.** `WEBSITE_TIME_ZONE` no está en `.github/workflows/ci-cd.yml` ni en ningún otro fichero del
    repositorio. Un entorno nuevo, otra región o una Web App recreada arrancaría en UTC sin que nada avise, y las horas saldrían 1 o 2 horas
    desplazadas.
  - **Toda la app depende de `TimeZoneInfo.Local`.** `DateTime.Today` aparece en al menos 20 sitios de Application y Web (turnos, planificación,
    portal familiar, citas, Dirección) y `.ToLocalTime()` en 57 ficheros de vistas y código. Solo el horario de los comunicados, los indicadores
    de Dirección y las citas pasan una zona explícita (`FamilyCommunicationSchedule`, `SupervisionIndicatorRules`, `AppointmentSlots`).
  - **Los tests no cubren la zona de producción.** Las pruebas unitarias de esos tres usan Europe/Madrid explícito, pero `HorarioComunicadosTests` y
    el resto usan `Local`; el runner de CI (`ubuntu-26.04`) probablemente está en UTC (no comprobado). Un `DateTime.Today` o un `.ToLocalTime()`
    mal usado no fallaría en CI.
  - **Una sola zona para toda la aplicación.** No hay zona por centro. Vale mientras todos los centros estén en Madrid; Canarias
    (Atlantic/Canary) tendría otra hora. No consta que CJ lo haya confirmado.
  - **Las franjas de citas se guardan como hora local sin zona** (`citas_franjas.hora_inicio`, `TIME(0)`) y las citas en UTC: si cambiara la zona del
    servidor, las franjas se moverían y las citas ya reservadas no.
  - Lo que está bien: la base de datos no usa hora local (ningún `GETDATE()` ni `SYSDATETIME()`); todo se guarda en UTC.
  - **Opciones:** (mínima, recomendada) mantener `TimeZoneInfo.Local`, fijar `WEBSITE_TIME_ZONE` en el despliegue (`az webapp config appsettings set`
    en el CI/CD) y comprobar al arrancar que la zona es Madrid, avisando en el registro o fallando; (completa) un ajuste `ResidApp:TimeZone` con un
    servicio de hora que sustituya `DateTime.Today` y `.ToLocalTime()`; (por centro) solo si CJ confirma centros fuera de la península.
- **Tests de integración intermitentes en CI: interbloqueos de SQL Server (resuelto el 2026-10-12, script `0055`; el siguiente libre es `0056`).**
  Habían fallado, siempre con otro test y en verde al relanzar: `Indicaciones_MedicinaNoCierra…` (PR #25), `CorreccionComunicadosTests.SeedAsync`
  (`main` tras #28) y `ProtocoloUrgenteMedico_DesdeIndicacionPendiente…` (PR #30). La aplicación convierte cualquier excepción en «No disponible»
  (`ApplicationResultRunner.Normalize`) y el motivo no queda en el log de CI, así que se buscó en la BD local: la sesión `system_health` de SQL Server
  guarda los grafos de interbloqueo (`sys.fn_xe_file_target_read_file` sobre `system_health*.xel`, evento `xml_deadlock_report`) y tenía 19 de
  `eventos_auditoria` y `basales_version_areas` entre el 01/10 y el 12/10. Siempre lo mismo: una lectura que recorre la tabla entera con bloqueos de
  página compartidos (`READ COMMITTED`) contra un `INSERT` de auditoría en transacción, que toma bloqueos de intención en otra página.
  - **Producción:** `SqlChangeInboxDirectory.ClosedByProfileSql` busca en `eventos_auditoria` por `recurso_id` y `accion_codigo` en una subconsulta por
    cada evento de la bandeja y del detalle, y la tabla solo tenía el índice `(centro, unidad, residente, fecha)`: cada evento recorría las 140.000
    filas de la BD local. El script `0055` añade `IX_audit_recurso_accion (recurso_id, accion_codigo) INCLUDE (perfil_activo)` (la consulta pasa a
    3 lecturas lógicas) y `IX_bva_residente (residente_id)` en `basales_version_areas`.
  - **Tests:** 40 lecturas de `eventos_auditoria` de los tests (entre ellas las de B2, Portal Familiar y citas, escritas sin tener en cuenta la lección «Los tests
    nuevos que consulten esa tabla deben hacer lo mismo», más abajo) no llevaban `WITH (NOLOCK)`; ahora sí. Sin ese cambio, con solo el índice, quedaban
    2 interbloqueos en 4 pasadas por recuentos por `cuenta_id` y `accion_codigo` (sin índice).
  - **Resultado en local:** suite de integración de 2 a 4 minutos (con tiempos de espera y fallos) a unos 20 s; 6 pasadas de integración (433) y 2 de
    funcionales (164) seguidas en verde, sin ningún interbloqueo nuevo en `system_health`. En CI, no demostrado hasta que pase varias veces.
  - **Segunda tanda, solo visible en una BD recién creada como la del CI (2026-10-12, script `0056`; el siguiente libre es `0057`).** Tras `0055` el CI de `main` volvió a fallar
    (PR #35: `Enfermeria_CorrigeUnComunicadoDentroDelMargen…` en funcionales y `CitasTests.SinAutorizacionActiva…` en integración). El paso «Excepciones de SQL durante las
    pruebas» de `ci-cd.yml` (PR #31) lo mostró por primera vez: **error 1205 real**, con víctima en `SqlResidentFamilyRepository.ChangeAuthorizationAsync`. Se reprodujo en local con la receta
    «Cómo reproducir el CI en local» (BD nueva sin seed, Release, `RESIDAPP_TEST_SQL_ERRORS_LOG`; la primera vuelta ya daba 4 fallos y 36 apariciones del 1205) y el grafo de
    `system_health` dio dónde: **dentro de triggers y de una escritura**, siempre por recorrer la clave primaria en vez de buscar por clave (con tablas vacías el plan no usa el índice;
    lección «El índice no basta en una BD recién creada»):
    - `TR_fac_transition` (0022): el `LEFT JOIN` a la autorización anterior, bloqueos de clave sobre `PK_fac`. `TR_ea_suspension_guard` (0041): la suspensión abierta del residente,
      sobre `PK_spr` y `PK_ea`. Los otros cuatro triggers de suspensión (`TR_cc/ec/bd/cf_suspension_guard`) hacen la misma búsqueda. `0056` los vuelve a crear con `WITH (FORCESEEK)`
      en esa búsqueda y nada más (misma lógica, mismos códigos de error).
    - `SqlResidentTransferRepository`: el traslado bloquea con `UPDLOCK, HOLDLOCK` los eventos abiertos del residente (a propósito: que no aparezca otro durante el traslado) y su `UPDATE`
      posterior recorría `PK_ea` y se topaba con el evento que otra transacción acababa de insertar (`IX_ea_residente` en `RangeS-U` contra `RangeI-N`). El `SELECT` y el `UPDATE`
      llevan ahora `FORCESEEK`.
    - Verificación: antes, la primera vuelta tipo CI fallaba; con `0056` sola, 1 de 2 vueltas seguía dando un 1205 (el del traslado); con ambos cambios, **6 vueltas seguidas con BD nueva,
      433 en verde y ningún 1205**. `SqlErrorLog` está ahora también en `FunctionalTests` (enlace al mismo fichero) porque un test funcional también falló en CI.
    - Para investigar el siguiente: en el log del CI, el paso de diagnóstico imprime las `SqlException` por número y los grafos de `system_health`; en local, `database/diagnostico/interbloqueos_recientes.sql`
      sacado con `objectname`/`indexname` de cada recurso da la tabla y el índice. Toda sentencia de escritura o trigger que busque por clave dentro de una transacción de escritura debe llevar `FORCESEEK`.
  - **Sigue abierto, sin cambiar:** la aplicación no reintenta ante el error 1205 (`ApplicationResultRunner` lo devuelve como «No disponible»); si
    algún día reaparece un interbloqueo en producción, la opción es reintentar la transacción víctima en el repositorio. **Decidido el 2026-10-12 no hacerlo todavía:** un reintento general en `ApplicationResultRunner.RunAsync` no es seguro, porque varios casos de uso encadenan llamadas a repositorios que son transacciones distintas (por ejemplo `ReadDirectionBaseline` audita y después lee, "auditoría o nada"), y repetir todo si el interbloqueo cae en el segundo paso duplicaría la fila de auditoría. Solo valdría un reintento dentro del repositorio, sobre una transacción única, y no hay ningún interbloqueo observado en producción que lo justifique; si aparece uno, el paso de diagnóstico de CI (`database/diagnostico/interbloqueos_recientes.sql`) dirá qué sentencias chocan y se reintenta solo ahí.

