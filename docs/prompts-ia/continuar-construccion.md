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

- Todo el trabajo está en `main` y subido al remoto; compruébalo con `git status` al empezar. Hasta el
  script `0014` (historia 5 de Medicina, seguimiento médico) está desplegado en Azure: el pipeline de
  `d273a5a` (run 36561451773) terminó con éxito el 2026-09-29, con `build-and-test` y `deploy` en verde.
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
- El estado de un run se consulta sin autenticación en
  `https://api.github.com/repos/orlando6608/residapp/actions/runs?branch=main`, y el de cada job y paso en
  `.../actions/runs/<id>/jobs`. Los logs piden autenticación y `gh` no está instalado.
- CJ no ha completado nada nuevo: `docs/pendientes-cj/rangos-referencia-constantes.html` sigue con 17
  valores «por definir».
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1
  (parcial), 2, 3, 4, 5, 7, 8, 9 y 10, más los rangos de referencia de constantes (fase 1 y su pantalla).
  Medicina está en curso: historias 1, 2, 3, 4 y 5 (escalados, valoración médica, indicaciones, cierre
  médico y seguimiento médico con continuidad entre turnos).
- La base local `ResidApp` tiene los scripts `0001` a `0014` registrados en `dbo.scripts_aplicados`.
  Hay una copia previa a `0014` en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0014-20260929.bak`.
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
    resuelto con una indicación (con indicación pendiente, sin leer).
- Suite: 97 unitarios, 160 de integración y 1 funcional, todos en verde.
- Hay dos scripts con el número `0005` (`0005_auxiliar_opciones_rapidas.sql` y
  `0005_enfermeria_borrador_basal.sql`). Es inofensivo, porque el runner los registra por nombre completo y
  son independientes entre sí. **No los renombres:** el runner los volvería a ejecutar y el despliegue en
  Azure fallaría. Antes de crear un script, comprueba cuál es el último número.

## Siguiente tarea: decidir el orden

Hay que proponer al usuario el orden antes de empezar. La conducta médica ya tiene tres de sus cuatro
salidas (indicaciones, cierre y seguimiento médico); falta el protocolo urgente.

- **Protocolo urgente y derivación a Urgencias** (historia 6 de Enfermería y 6 de Medicina, `DER-01` a
  `DER-06`, [`derivacion-urgencias.md`](../flujos-clinicos/derivacion-urgencias.md)). Es la salida que
  falta en las dos decisiones. Antes de empezar hay que decidir con el usuario:
  - cómo se genera y firma el informe en PDF;
  - la «actualización relevante» obligatoria para la familia con el registro del intento de llamada, que
    toca el Portal Familiar, todavía sin construir.
- **Historial** (historia 11 de Enfermería, 9 de Medicina): puede mostrar ya las versiones de las
  valoraciones y los seguimientos terminados.
- **Evento propio de Medicina** (historia 7 de Medicina).

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
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`.
