# Retomar la construcción en una sesión nueva

Estado a 2026-09-29. Actualízalo al cerrar cada bloque de trabajo, para que la siguiente sesión (con
Claude, ChatGPT o una persona) arranque sin reconstruir el contexto.

## Prompt para empezar

> Retomamos la construcción de ResidApp. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`), `docs/tareas/alta-prioridad/pendientes-enfermeria.md` y
> `docs/tareas/alta-prioridad/pendientes-medicina.md`.
> Mira si CJ ha completado algo en `docs/pendientes-cj/`. Después comprueba que la suite pasa en verde
> contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Dónde estamos

- Hasta el script `0016` (bloque 2 de la historia 6, derivación a Urgencias en Enfermería y Medicina) está
  desplegado en Azure: el pipeline de `ca855f2` (run 36607704572) terminó en verde el 2026-09-29, con PDFs
  reales generados en los tests del CI (Linux). El último push (`1ed5382`, solo documentación, run
  36609356298) también terminó en verde.
- **Prueba en Azure de la derivación (2026-09-29):** `dev-integrado-enfermeria`, sobre «Residente
  Integrado Uno (ficticio)», con textos «Prueba técnica, se puede ignorar». Se hizo el recorrido completo:
  evento, valoración, protocolo, vista previa, firma, descarga del PDF, llamada y cierre con una
  comunicación Relevante, que queda pendiente de aprobación en Comunicaciones. El PDF salió correcto: la
  firma dice 19:54, en hora de España (17:54 UTC), y la fuente incrustada y las tildes se ven bien en Linux.
  La hora de su intento de llamada quedó como 17:53 en vez de 19:53: se calculó con `TZ=Europe/Madrid date`
  en Git Bash, que no aplica la zona. Es un registro de prueba inmutable y no afecta a nada más. Para
  calcular horas locales en pruebas con curl, usa el `date` de Git Bash sin `TZ`, porque la máquina ya
  está en hora de Madrid.
- La corrección de la cultura (`es-ES` fija en `Program.cs` con los patrones cortos, `LocalizationTests`)
  está desplegada desde `6f61791` (run 36582061750), y en Azure las fechas salen como `29/09/2026 15:53`
  (antes `09/29/2026`). El pipeline de `21b293f` falló en los tests por la diferencia de formato entre
  Linux y Windows (ver la lección «Cultura en Azure»).
- **Zona horaria (hecho):** la app lee y muestra las horas en la hora local del servidor. La Web App
  `app-residapp-dev` (Linux) tiene `WEBSITE_TIME_ZONE=Europe/Madrid`. Se comprobó el 2026-09-29 con el
  evento «Prueba de hora, se puede ignorar» (Residente Integrado Uno, cerrado con «No comunicar»): se
  registró a las 13:53 UTC y la app mostró las 15:53.
- El primer pipeline de `0013` (`5325ef4`) falló en los tests por interbloqueos en una BD recién creada, y
  no llegó a desplegar. Se corrigió en `f970d83` (`FORCESEEK`, ver las lecciones). En `b060542` se ordenaron
  además las opciones de área en `SqlChangeInboxDirectory.FindAsync`, porque un test fallaba de vez en
  cuando.
- Verificación de `0014`: 6 vueltas tipo CI con BD nueva y 13 vueltas contra la base local en verde. Hubo
  **una** vuelta local fallida en `SeguimientoMedico_Vencido_SigueEnLaBandeja_YSeResuelveConIndicacionOCierre`,
  que tardó 85 s frente a los 5 habituales, sin mensaje guardado. No quedó ningún `xml_deadlock_report`, y el
  log de SQL Server avisa a esa hora de que su memoria se había paginado («performance degradation»). Lo
  más probable es un tiempo de espera por presión de memoria en la máquina, pero no está confirmado. Si
  vuelve a fallar, guarda la salida completa (`--logger "console;verbosity=normal"`).
- Verificación de `0015`: 3 vueltas tipo CI con BD nueva, BD temporal con seed y 5 vueltas locales en
  verde. Dos vueltas locales dieron unos 50 fallos por tiempo de espera agotado (unos 30 s, incluso en tests
  que no tocan el protocolo):
  - las dos eran la primera ejecución de `dotnet test`, que además compilaba;
  - no había bloqueos, interbloqueos ni crecimientos lentos de ficheros (traza por defecto);
  - el log de SQL Server avisaba de memoria paginada, y la máquina tenía 1,9 GB libres de 15,7 GB;
  - repetidas con `--no-build`, pasaron enteras.

  Causa probable: presión de memoria en esta máquina, la misma que en el fallo de `0014`. Consejo:
  `dotnet build src/ResidApp.sln` primero y después `dotnet test src/ResidApp.sln --no-build`.
- Verificación de `0016`:
  - suite local en verde, 3 vueltas tipo CI con BD nueva en verde, y BD temporal con seed (17 scripts, sin
    errores) ya borrada;
  - PDF generado en un contenedor Linux (Docker, `mcr.microsoft.com/dotnet/sdk:10.0`) y revisado: fuente
    incrustada, tildes, «Ñ» y «O₂»;
  - prueba manual con curl en los dos perfiles:
    - derivar sin motivo (rechazado);
    - vista previa (sin el 112 ni las comunicaciones);
    - firmar con una huella antigua tras registrar otra evolución: enseña la vista previa nueva;
    - firmar, repetir la firma, descargar el PDF desde los dos perfiles y cerrar sin llamada, que se
      rechaza;
    - forzar «No comunicar» (rechazado);
    - registrar la llamada y cerrar con una comunicación Relevante.
- El estado de un run se consulta sin autenticación en
  `https://api.github.com/repos/orlando6608/residapp/actions/runs?branch=main`, y el de cada job y paso en
  `.../actions/runs/<id>/jobs`. Los logs piden autenticación y `gh` no está instalado.
- CJ no ha completado nada nuevo: `docs/pendientes-cj/rangos-referencia-constantes.html` sigue con 17
  valores «por definir».
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1
  (parcial), 2, 3, 4, 5, 6, 7, 8, 9 y 10, más los rangos de referencia de constantes (fase 1 y su
  pantalla). Medicina está en curso: historias 1 a 6 (escalados, valoración médica, indicaciones, cierre
  médico, seguimiento médico con continuidad entre turnos, protocolo urgente y derivación a Urgencias).
- La base local `ResidApp` tiene los scripts `0001` a `0016` registrados en `dbo.scripts_aplicados`.
  Hay una copia previa a `0016` en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0016-20260929.bak`.
  La base local tiene además eventos de prueba del escenario integrado:
  - «Prueba manual historia 3: tos.»: cerrado, con comunicación pendiente de aprobación.
  - «Prueba manual historia 4: tos.»: cerrado tras un seguimiento completo.
  - «Prueba manual historia 5: disnea.»: cerrado por Medicina, con una comunicación pendiente y una
    indicación que Enfermería realizó después del cierre. Su motivo de escalado quedó como
    «Desaturaci%F3n…» por la lección de curl de más abajo.
  - «Prueba manual Medicina: fiebre.»: tenía una indicación que Enfermería leyó y realizó. Después pasó a
    seguimiento médico (vencido, con revisión, reprogramación, transferencia con recepción y conservación) y
    se cerró desde el seguimiento.
  - «Prueba manual cierre Enfermería tras 0013: mareo.»: cerrado por Enfermería.
  - «Prueba manual seguimiento médico: disuria.»: escalado, seguimiento médico desde la valoración y
    resuelto con una indicación sin leer. Después, protocolo urgente de Medicina con un contacto, y cerrado
    desde el protocolo.
  - «Prueba manual protocolo urgente: desaturación.»: prioritario de Enfermería, protocolo urgente con
    actuación, contacto con el 112 (hora de 10 minutos antes) y evolución, y cerrado desde el protocolo.
  - «Prueba manual derivacion: desaturación brusca.»: protocolo de Enfermería con un contacto al 112 y dos
    evoluciones, derivado (informe firmado), con una llamada «No contesta» y cerrado con una comunicación
    relevante.
  - «Prueba manual derivacion medica: dolor torácico.»: escalado, protocolo de Medicina, derivado por
    Medicina, con una llamada «Contactado» y cerrado con una comunicación relevante.
- Suite: 113 unitarios, 170 de integración y 6 funcionales, todos en verde.
- Hay dos scripts con el número `0005` (`0005_auxiliar_opciones_rapidas.sql` y
  `0005_enfermeria_borrador_basal.sql`). Es inofensivo, porque el runner los registra por nombre completo y
  son independientes entre sí. **No los renombres:** el runner los volvería a ejecutar y el despliegue en
  Azure fallaría. Antes de crear un script, comprueba cuál es el último número.

## Siguiente tarea: decidir el orden

Hay que proponer al usuario el orden antes de empezar. Las salidas de la decisión de Enfermería, de la
conducta de Medicina y del protocolo urgente están todas construidas.

- **Historial** (historia 11 de Enfermería, 9 de Medicina): puede mostrar ya las versiones de las
  valoraciones, los seguimientos terminados y el informe de derivación firmado (el flujo pide que sea
  accesible desde el Historial).
- **Evento propio de Medicina** (historia 7 de Medicina).

## Avisos abiertos (fuera de alcance, sin corregir)

Se detectaron durante otros bloques. No se han corregido porque quedaban fuera de su alcance; propónselos al
usuario cuando encajen:

- **`BaselineAreaDisplay.Summarize`** (`Web/Models/AuxiliarModels.cs`) escribe `ToString()` de cada
  propiedad. En las áreas con varias opciones (comunicación, continencia, conducta, sueño, ayudas
  habituales), las pantallas del basal (`Auxiliar/Basal`, `EnfermeriaBasal/Confirmar`) mostrarían el nombre
  del tipo de lista (`System.Collections.Generic.List…`) en vez de los valores. El informe de derivación usa
  su propio resumen (`ReferralReportBuilder.AreaValues`), que despliega las listas. Además, los valores de
  los catálogos del basal no tienen etiquetas en español con tildes: se muestran con el nombre del enum.
- **Pregunta para CJ:** si el campo «Comunicaciones» de la valoración de Enfermería debe entrar en el
  informe de derivación. Hoy se excluye por prudencia (DER-04 saca los contactos del informe externo).
  Está anotada en `pendientes-enfermeria.md`; si hace falta, se le pide con un documento en
  `docs/pendientes-cj/`.
- **CI (anotaciones de GitHub Actions):**
  - `actions/checkout@v4` y `actions/setup-dotnet@v4` usan Node.js 20, que está obsoleto (hoy se fuerzan a
    Node 24);
  - `ubuntu-latest` pasará a Ubuntu 26 a partir del 19 de octubre de 2026. El workflow instala `sqlcmd` desde
    el repositorio de paquetes de Microsoft para Ubuntu 22.04 (`.../config/ubuntu/22.04/prod.list`): revísalo
    si el pipeline empieza a fallar en «Instalar sqlcmd».

## Reglas de trabajo propias de este repositorio

- **Push:** siempre con confirmación explícita del usuario. El push a `main` aplica los scripts nuevos
  y el seed en Azure SQL.
- **Esquema:** siempre un script nuevo numerado; nunca se edita uno ya aplicado en Azure.
- **Manual:** actualiza `Views/Home/Manual.cshtml` cuando cambie una pantalla que describa o se construya
  un módulo marcado como «Próximamente».
- **Interfaz:** siempre en español, incluidos los enums que aparecen en un `<select>`. Sigue
  `docs/bocetos-pantallas/guia-diseno-sistema-visual.md` (Bootstrap 5, WCAG AA, `btn-lg`, nada de texto
  blanco sobre amarillo).
- **Valores clínicos:** nunca los inventes. Si hace falta uno, se pide a CJ con un documento en
  `docs/pendientes-cj/`.
- **Contradicciones:** si el código contradice `docs/flujos-clinicos/`, para y avisa.
- **Idioma:** responde al usuario siempre en español, también en los mensajes cortos de estado.

## Cómo se ha verificado cada bloque

Repite estos pasos antes de dar un bloque por cerrado:

1. **Plan:** plan aprobado por el usuario antes de tocar código. Las decisiones de producto se le preguntan
   a él, o a CJ con un documento en `docs/pendientes-cj/`.
2. **Copia de la BD local** antes de aplicar un script nuevo:
   `BACKUP DATABASE [ResidApp] TO DISK = N'...\Backup\ResidApp-antes-00XX-AAAAMMDD.bak'`.
3. **Aplicar el script en local:**
   `SQL_SERVER=ACER-ORLANDO SQL_DATABASE=ResidApp SQLCMD_EXTRA="-C" bash database/aplicar-scripts.sh`.
4. **Suite en verde varias veces** contra la BD local y, además, en el entorno tipo CI (ver «Cómo
   reproducir el CI en local» más abajo).
5. **BD temporal nueva** con todos los scripts y el seed (`APLICAR_SEED=1`), que se borra al terminar.
6. **Prueba manual con la app y curl** del flujo completo, con las cuentas `dev-integrado-*`:
   - La app se levanta con `dotnet run --launch-profile http` desde `src/ResidApp.Web`, en
     `http://localhost:5203`.
   - Para entrar, haz un GET de `/DevAuth/Login` para sacar el `__RequestVerificationToken` y un POST
     con `externalSubject=<cuenta>`. Después, `GET /ProfileScope/Select -L`.
   - En cada POST, toma el token de una página que tenga formulario y envíalo con `--data-urlencode`.
7. **Documentación:**
   - `Manual.cshtml`;
   - el pendiente del vertical;
   - este fichero;
   - el checklist y el README si cambia el estado.
8. **Commit en `main` sin push.** El push solo cuando el usuario lo pida. Después, comprueba que el
   pipeline termina en verde, con `build-and-test` y `deploy`.

## Lecciones técnicas que conviene no redescubrir

- **Decimales en formularios:** la cultura del servidor es es-ES. Un decimal enlazado desde
  `type="text"` convierte «37.8» en 378. Usa `type="number" step="0.1"`, que ASP.NET Core enlaza con
  cultura invariante gracias al campo oculto `__Invariant`. En un campo oculto que reenvía un decimal,
  escribe el valor en formato invariante y añade también `__Invariant`.
- **`[Range]` con decimales:** necesita `ParseLimitsInInvariantCulture = true`; si no, da un error 500
  en es-ES.
- **`OUTPUT` en SQL:** no se puede usar `UPDATE ... OUTPUT` sin `INTO` en tablas con triggers. Comprueba
  las filas afectadas con la revisión en el `WHERE`.
- **Códigos de error:** los que lanza la BD o el dominio se traducen a acceso denegado, entrada inválida
  o conflicto mediante patrones en `src/ResidApp.Application/Errors/ApplicationResult.cs`. Un código nuevo
  necesita su patrón allí.
- **Patrones ya asentados** que se reutilizan en vez de reinventarlos:
  - Concurrencia optimista por revisión o versión, con un mensaje de conflicto neutro que conserva lo
    escrito.
  - Tablas de solo inserción con triggers `INSTEAD OF UPDATE, DELETE` que lanzan `THROW`.
  - Comprobación de ámbito deny-by-default en cada caso de uso.
  - Auditoría en `eventos_auditoria`.
  - Cierre de evento común a Enfermería y Medicina en `ClinicalEventCloser`, con la regla de cada perfil en
    `ClinicalEventCloseRule`.
- **Nuevos estados del evento:** añádelos también a `ClinicalEventStatusDisplay` (`EnfermeriaModels.cs`).
  Si no, la insignia muestra el nombre interno en inglés. Busca además los filtros por estado en los casos
  de uso y en las consultas (`grep` de los `ClinicalEventStatus.` vecinos). Con `EN_SEGUIMIENTO_MEDICO`,
  `ListMedicalIndications` habría ocultado las indicaciones de un evento que pasa de «con indicación
  pendiente» a seguimiento médico.
- **Comandos locales:** la solución está en `src/ResidApp.sln`, así que se ejecuta
  `dotnet test src/ResidApp.sln`. La app se levanta con `dotnet run --launch-profile http` desde
  `src/ResidApp.Web`. Con `--no-launch-profile` no carga los user-secrets y falla por falta de la cadena de
  conexión.
- **Interbloqueos entre tests:** los tres proyectos de test se ejecutan en paralelo contra la misma BD. Un
  `UPDATE` o una lectura por una columna sin índice recorre la tabla entera y puede provocar interbloqueos
  intermitentes. Pasó con `valoraciones_enfermeria.evento_id` y se resolvió con `IX_ve_evento` en `0011`.
  Toda columna por la que se filtre un `UPDATE` necesita un índice. Si un test falla solo a veces, ejecuta
  `dotnet test src/ResidApp.sln` varias veces y busca `xml_deadlock_report` en `system_health`: está en
  `sys.fn_xe_file_target_read_file('system_health*.xel', ...)`, con `sqlcmd -I`.
- **El índice no basta en una BD recién creada, como la de CI.** El plan se compila con las tablas vacías y
  recorre la clave primaria. Pasó en el CI de `0013`: dos guardados de valoración se bloqueaban en `PK_ve` a
  pesar de `IX_ve_evento`. Por eso las sentencias que leen o actualizan la valoración por evento dentro de
  una transacción de escritura llevan `WITH (FORCESEEK)`, y un `UPDATE` con esa pista se escribe
  `UPDATE v ... FROM tabla v WITH (FORCESEEK)`.
- **Cómo reproducir el CI en local:** crea una BD nueva, aplica solo los scripts, sin seed, y ejecuta
  `dotnet test` en Release con `RESIDAPP_TEST_CONNECTION_STRING` apuntando a ella. Hazlo varias veces, con una
  BD nueva en cada vuelta. Los logs del pipeline piden autenticación y `gh` no está instalado.
- **Acentos con curl en Git Bash:** `--data-urlencode` recibe los argumentos en Latin-1, así que «ó» llega
  como `%F3` y ASP.NET lo guarda tal cual. Un navegador envía UTF-8 y se guarda bien. En pruebas con curl,
  escribe los acentos ya codificados en UTF-8 (`--data "Form.Motivo=Desaturaci%C3%B3n"`).
- **Razor:** dentro de un bloque `@if`, el texto que sigue a una etiqueta vacía (`<br />Texto`) se interpreta
  como C#. Envuélvelo en un `<span>`.
- **Vistas comunes a dos perfiles:** en una vista parcial, `asp-action` sin `asp-controller` apunta al
  controlador en curso. Así, `Shared/_ProtocoloUrgente` sirve a Enfermería y a Medicina sin pasarle el
  controlador.
- **`CHECK` antes que el trigger:** en un test de BD que fuerza un estado prohibido, un `CHECK` de la fila
  (como `CK_ea_inicio_medico`) salta antes que `TR_ea_transition_guard` y cambia el mensaje esperado.
- **Cultura en Azure:** el contenedor Linux de la Web App corre con la cultura invariante (fechas
  `MM/dd/yyyy`), y Windows con la del usuario. Por eso la cultura `es-ES` se fija en `Program.cs`, y en local
  no se nota si falta. Además, `es-ES` no formatea igual en los dos sistemas:
  - con ICU (Linux) es `d/M/yyyy H:mm`;
  - con Windows es `dd/MM/yyyy HH:mm`.

  Por eso los patrones cortos se fijan a mano. El primer intento (`21b293f`, run 36579577575) no los fijaba,
  y su test falló en el CI por esa diferencia. Cualquier aserción sobre texto formateado debe probarse
  también en Linux:
  - `docker run --rm -v <repo>:/repo:ro mcr.microsoft.com/dotnet/sdk:10.0`, copiando el repo dentro del
    contenedor y quitando `bin` y `obj`;
  - `LocalizationTests` no necesita BD.
- **PDF (PDFsharp-MigraDoc 6.2.4, MIT):**
  - en Linux (Azure y CI), PDFsharp no lee fuentes del sistema y lanza una excepción sin un `IFontResolver`
    propio;
  - la fuente Liberation Sans (OFL, con su licencia en `Infrastructure/Pdf/Fonts/OFL.txt`) va incrustada
    como recurso y la resuelve `ReferralReportPdfRenderer`, así que el PDF sale igual en Windows y Linux;
  - las horas del PDF se escriben con formato fijo en la hora local del servidor.
- **Idempotencia antes que las reglas de unicidad:** si un caso de uso rechaza «ya existe» antes de llegar
  al repositorio, repetir la misma operación da error en vez de devolver su resultado. La regla de «uno por
  evento» va en la BD, después de consultar `operaciones_idempotencia` (como en `ReferralWriter.SignAsync`).
- **Campos ocultos tras un POST:** `asp-for` pinta el valor enviado (ModelState) y no el del modelo. Para
  una revisión o una huella que el servidor actualiza al volver a mostrar la vista, escribe `value="@..."`
  explícito (ver `Shared/_Derivacion`).
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`.
