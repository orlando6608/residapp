# Lecciones técnicas y del entorno

> Conocimiento que dura: trampas del entorno, cómo se ha verificado cada bloque y lecciones técnicas que conviene no redescubrir. Se **consulta** (búsqueda por texto) cuando una tarea toca el tema; no hace falta leerlo entero al arrancar.
> Movido tal cual desde `continuar-construccion.md` el 2026-11-18, al partir el relevo. Cuando aprendas algo que valga para más de un bloque, añádelo aquí y no en el relevo.

## Lecciones del entorno (sesión del 2026-10-23)

- Tras cada despliegue, Azure dev da 500 o 502 durante 30-45 s (reinicio del contenedor) y luego responde; sondea con `curl` antes de dar un fallo por real.
- En este equipo no hay Python: para ediciones por lotes de ficheros de texto, usa Node (`node script.js`) o la herramienta Edit. Ojo con los finales de línea: el
  repositorio trabaja en CRLF y un script que lea y escriba debe conservarlos.
- Dapper no admite `uint` como parámetro: pasa los contadores de 32 bits como `long`.
- Una passkey solo se prueba de verdad con un navegador: el recorrido de Chrome sin interfaz con autenticador virtual (CDP `WebAuthn.addVirtualAuthenticator`) está
  descrito en la entrada de I4; las pruebas HTTP usan `tests/FunctionalTests/SoftwareAuthenticator.cs`.
- **Bash y los heredocs largos:** en este equipo, un `cat <<'EOF'` largo con tildes, comillas o apóstrofos falla con «unexpected EOF» y no crea nada (curl con
  tildes en `-d` también rompe el cuerpo por la codificación). Crea los ficheros con la herramienta Write; para llamadas a APIs con acentos, usa un script de Node.
  `sleep` largo en el shell lo bloquea el entorno: para esperar, usa `run_in_background` (se avisa al terminar).
- ffmpeg está instalado (WinGet, en el `PATH`) y Chrome también: `chrome.exe --headless=new --screenshot` sirve para ver una página local; con `#ancla` en la URL
  la captura sale en blanco (haz una captura alta y recórtala con `ffmpeg -vf crop`).
- La clave de Mistral se lee del almacén con `dotnet user-secrets --project tools/PruebaDictado list`; para sondear un modelo, mira las cabeceras
  `x-ratelimit-limit-req-minute` (0 = sin cuota en ese modelo).
- Las mutaciones que sobreviven suelen indicar una defensa redundante (como la comprobación de cuenta del estado de la passkey, cubierta por la de sesión): decide si
  se quita el código o se añade la prueba que la aísla.

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
6. **Prueba manual con la app** del flujo completo, en un navegador real con la skill `probar-en-navegador` (entra como una persona:
   invitación, contraseña y código del móvil; ya no hay acceso de desarrollo para curl):
   - La app se levanta con `dotnet run --launch-profile http` desde `src/ResidApp.Web`, en
     `http://localhost:5203`.
   - Con una cuenta recién sembrada (`sembrar.sh`) o con el juego de Orlando de los centros de prueba (`almendros-orlando-*`), cuyas
     credenciales guarda `comun.js` en `~/.residapp/credenciales-localhost_5203.json`.
7. **Documentación:**
   - `Manual.cshtml`;
   - el pendiente del vertical;
   - este fichero;
   - el checklist y el README si cambia el estado.
8. **Commit en `main` sin push.** El push solo cuando el usuario lo pida. Después, comprueba que el
   pipeline termina en verde, con `build-and-test` y `deploy`.

## Lecciones técnicas que conviene no redescubrir

- **`az sql server firewall-rule delete` no admite `--yes`, y un `|| true` lo escondió (2026-11-03, A3).** El primer despliegue con el paso de cerrar la regla del runner salió «correcto» aunque la regla `ci-runner-<run>` seguía en el servidor: el paso tenía `--yes` y `|| true`, y el error («unrecognized arguments: --yes») solo se veía en el registro. Se detectó porque tras el despliegue se listaron las reglas en vez de fiarse del ✓. Reglas: (1) no poner `|| true` a un comando de limpieza; comprobar antes si existe (`az sql server firewall-rule show` devuelve error si no, código 3) y dejar que falle a la vista; (2) **probar los pasos de un workflow contra el servicio real**, no con un `az` simulado: un simulacro no conoce la sintaxis y dio por bueno un comando que no existía; el arnés de la corrección (regla vieja, sobrante y faltante creadas a propósito en Azure con la puerta general abierta, y limpieza garantizada con `trap`) sí lo habría detectado.
- **Ficheros subidos (foto del residente, el primer formulario `multipart/form-data`):**
  - el servidor no tiene librería de imágenes: el navegador reduce y recodifica la foto (`site.js`, `createImageBitmap` con `imageOrientation: "from-image"`, y
    `DataTransfer` para cambiar el fichero del `<input>`);
  - el servidor solo acepta un JPEG y lo limpia recorriendo sus bloques (`ResidentPhoto.Normalize`): sin el reductor, una foto del móvil llevaría la posición GPS;
  - `RequestSizeLimit` alto (20 MB) y comprobación de 1 MB antes de leer el fichero, para dar un mensaje en vez de un 413;
  - la imagen se sirve con `nosniff`, `Cross-Origin-Resource-Policy: same-origin` y `Cache-Control: private`, y su dirección lleva `?v=` con la fecha de la foto;
  - una acción que responde a un `<img>` devuelve 404, nunca una redirección: `ResidentId.From(Guid.Empty)` lanzaría y `InvalidIdentifierFilter` respondería con un
    302 a una página HTML. Por eso se comprueba `Guid.Empty` antes.
- **Autorizar algo que no es del basal sobre un residente:** `ResidentPhotoApplicationService` llama directamente a
  `IAuthorizationEvidenceProvider.LoadEvidenceAsync(..., new AuthorizationTarget.Read(id))` (la misma regla de ámbito que las listas) y añade su propia lista de
  perfiles. `RequestAuthorizationContextResolver` rechaza `Read` salvo para Dirección y el basal.
- **Una foto en la cabecera no va con `loading="lazy"`:** las capturas (y el primer pintado) la enseñaban vacía. Solo las miniaturas de las listas cargan en diferido.
- **Formularios por `fetch` (importar y exportar, `intercambio.js`):**
  - **Redirección:** `fetch` sigue el 302 él solo y consume el TempData de la página de destino antes de que el JavaScript navegue a ella. El mensaje de
    éxito va en la dirección (`?plantillaGuardada=`).
  - **«change» de un campo de texto:** salta al pulsar el botón siguiente (al perder el foco). Si el manejador borra el bloque donde está el botón, el clic no
    llega: solo los desplegables borran la vista previa.
  - **Pruebas en navegador real:** los dos fallos solo salieron al probar con un navegador real; las pruebas por HTTP no los ven. Cómo arrancar la app,
    sembrar una cuenta de cualquier perfil y recorrer pantallas en Chrome sin interfaz: skill `.claude/skills/probar-en-navegador/` (con el recorrido de
    Importar y Exportar como ejemplo).

- **`border-start border-4 border-<color>` en una `.card` dibuja un marco**, no una franja: `.border-4` fija el ancho de los
  cuatro lados y `.border-<color>` los colorea todos. `residapp-theme.css` devuelve los otros tres lados al borde neutro;
  no lo quites.
- **`<legend>` como columna de una `fieldset.row`** solo funciona con el `float: left` que le da Bootstrap: con `float-none` vuelve a
  ser la leyenda del recuadro, sale de la rejilla y ocupa toda la fila.
- **`input.btn-check` es `position: absolute`** sin coordenadas: se queda al principio de su contenedor y, al recibir el foco, el
  navegador desplaza la página hasta allí y no hasta el chip. Por eso cada pareja va en `.chip-par`.
- **Puppeteer `page.type` en un campo dentro de un `<details>` cerrado** no escribe nada (no se puede enfocar) y no da error.
- **El CSS aislado (`_Layout.cshtml.css`) no llega a los enlaces con tag helper** (`<a asp-action>`): no reciben el atributo
  `b-xxxx`. Las reglas para esos enlaces van en `residapp-theme.css`.
- **Modo oscuro:** los colores salen de los tokens `--color-*` de `residapp-theme.css`, asignados a las variables de Bootstrap.
  En oscuro, `text-danger`/`text-success` y los `btn-outline-*` de serie se quedan en 3,7:1 y el tema los pasa a
  `--bs-*-text-emphasis`; para un color nuevo, usa esos tonos. `text-warning` no sirve como texto ni en claro (1,6:1): usa
  `text-warning-emphasis`.
- **Capturas con sesión para revisar una pantalla:** la skill `probar-en-navegador` (Playwright con el Chrome instalado) inicia
  sesión como una persona (`comun.js`, `entrar`), emula `prefers-color-scheme` y, con `isMobile` y `hasTouch`, también `any-pointer: coarse`.
  En Git Bash, un argumento que empiece por `/` se convierte en una ruta de Windows: exporta `MSYS_NO_PATHCONV=1`.

- **Enums en español en pantalla:** un enum con `[Code("X_Y")]` que el formulario envía como texto se pinta con
  `EnumDisplay.Label(valor)` (necesita `[Display(Name=…)]` en español) y `value="@valor.ToCode()"`; no sirve
  `GetEnumSelectList` porque envía el número. Nunca imprimas `@x.AlgoCode` ni `ToString()` de un enum: sale el nombre
  del miembro o el código en inglés. Revisado el 2026-10-02: Dirección Clínica (tipo de recurso y propósito) y el
  motivo del basal ya cumplen.
- **(Histórico: hasta el cambio a QuestPDF del 2026-11-15; ya no hay que serializar y las pruebas leen el PDF con PDFsharp 6.2.4 solo en `tests/`.) PDFsharp/MigraDoc no admite generar documentos a la vez:** comparten estado de fuentes y un título puede salir con una
  palabra en la fuente equivocada. Cualquier renderizador nuevo debe serializar el renderizado como
  `ReferralReportPdfRenderer`. Para revisar un PDF sin abrirlo, compara el contenido de su página
  (`PdfReader.Open(...).Pages[0].Contents`, `UnfilteredValue`): el texto va con la fuente incrustada y no se busca como
  texto plano.
- **Identidad de marca en PDF e impresión** (obligatoria, ver sección 4 de `guia-diseno-sistema-visual.md`): todo PDF
  nuevo de servidor llama a `PdfBranding.ComposeHeader(container)` en el encabezado de cada página. Las impresiones del navegador ya llevan el encabezado
  del `_Layout`, y ninguna vista debe ocultarlo al imprimir. Se repite en cada página porque, al imprimir, el contenedor
  es una tabla CSS y el encabezado su `table-header-group`. Sin `break-inside: avoid`, Chrome no lo repite (comprobado
  imprimiendo Indicadores en dos páginas).
- **Imágenes en el PDF (desde 2026-11-15 con QuestPDF, que tampoco importa SVG; el recurso se pasa como bytes, no con `base64:`):** El icono del encabezado es `wwwroot\images\logo.svg` rasterizado a
  PNG de 256 px (`Infrastructure\Pdf\Images\logo.png`, generado con Chrome headless `--screenshot` y
  `--default-background-color=00000000`), incrustado como recurso y pasado a MigraDoc con el prefijo `base64:`, sin
  ficheros temporales. Si cambia el SVG, hay que regenerar el PNG. Para ver un PDF como imagen sin poppler, usa
  `Windows.Data.Pdf` desde Windows PowerShell 5.1; Chrome headless no pinta su visor de PDF.
- **Probar una impresión con sesión en local:** entra con `comun.js` (`entrar`; con un ámbito único se elige solo) e imprime con
  `page.pdf()` de Playwright, o con Chrome por DevTools (`Network.setCookie` con la cookie de la sesión + `Page.printToPDF`; Node 20
  necesita `--experimental-websocket`). En local, la app devuelve 0 bytes en los estáticos
  si el navegador pide compresión (`Accept-Encoding: br/gzip`): para la prueba, manda `Accept-Encoding: identity`.
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
- **Nuevos orígenes del evento:** la vista de «propio» se decide con `Origin != ClinicalEventOrigin.CambioAuxiliar`
  (`_InformacionReunida`, `DetalleCambio`, `ReferralReportBuilder`). Un origen nuevo necesita su
  etiqueta, su rama en `CK_ea_origen` y `CK_ea_inicio`, y revisar `ScopedEventsFrom`. Todo código que
  inserte en `eventos_asistenciales` debe llamar también a `EventContextSnapshot.CaptureAsync` en la misma
  transacción (HIS-03, `0019`); si no, el evento sale en el Historial «Sin datos de contexto».
- **Cambiar una valoración ya cerrada:** desde `0020`, `TR_ve_guard` y `TR_vm_guard` solo lo permiten si
  antes se insertó en `valoraciones_*_correcciones` una fila con el mismo contenido, autor y hora
  (`actualizado_en`), usando el mismo parámetro de fecha en las dos sentencias. La comparación usa
  `INTERSECT`, que trata los `NULL` como iguales. Si se añade una columna de contenido a una valoración, hay
  que añadirla también a su tabla de correcciones y al `INTERSECT` del trigger.
- **Sin Python en esta máquina:** para ediciones en lote, usa Edit o `sed`, no scripts de Python.
- **Nuevos estados del evento:** añádelos también a `ClinicalEventStatusDisplay` (`EnfermeriaModels.cs`).
  Si no, la insignia muestra el nombre interno en inglés. Busca además los filtros por estado en los casos
  de uso y en las consultas (`grep` de los `ClinicalEventStatus.` vecinos). Con `EN_SEGUIMIENTO_MEDICO`,
  `ListMedicalIndications` habría ocultado las indicaciones de un evento que pasa de «con indicación
  pendiente» a seguimiento médico.
- **Comandos locales:** la solución está en `src/ResidApp.sln`, así que se ejecuta
  `dotnet test src/ResidApp.sln`. La app se levanta con `dotnet run --launch-profile http` desde
  `src/ResidApp.Web`. Con `--no-launch-profile` no carga los user-secrets y falla por falta de la cadena de
  conexión.
- **Quién puede leer o hacer algo:** antes de proponer una decisión de autorización, consulta la matriz de
  permisos del prototipo
  (`legado-cloudflare-final:docs/legado-cloudflare/docs/product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md`). En el
  bloque 1 del Historial se decidió un permiso nuevo para la línea temporal sin mirarla, y hubo que cambiar
  la decisión en el bloque 2, porque la matriz la da por ámbito.
- **`NULL` sin tipo en una consulta de Dapper:** un `NULL AS Columna` suelto llega como `int`, y Dapper no
  encuentra el constructor del record si el parámetro es `string?`. El caso de uso solo devuelve «No se ha
  podido completar la operación». Escribe `CAST(NULL AS NVARCHAR(…))`. En un test, `Assert.True(r.Ok,
  r.Error?.Message)` ayuda a llegar antes al fallo.
- **Suite lenta o con tiempos de espera:** en el bloque 1 del Historial, dos vueltas de la solución completa
  fallaron: la primera con 51 fallos (tiempos de espera de 30 s, y en `DatabaseTriggerTests` fallos
  inmediatos en cascada) y otra con 1. En esas vueltas las pruebas de integración tardaban de 40 s a 2 min en
  vez de 6 s. Cómo se descartó el código:
  - sin `xml_deadlock_report` en `system_health`;
  - la máquina tenía 1,3 GB libres de 15,7;
  - las pruebas de integración solas pasaron enteras en 10 s;
  - una vuelta completa muestreando `sys.dm_exec_requests` cada 2 s no encontró ningún bloqueo de más de
    0,5 s, y tardó 6 s;
  - las 3 vueltas siguientes pasaron en 5-10 s.

  Si vuelve a pasar, repite este diagnóstico antes de tocar código.

  El 2026-10-01 (bloque 1 de Administración) se repitió a lo grande: 188 y 52 fallos, todos «tiempo de espera durante
  la fase previa al inicio de sesión», con 1 GB libre y SQL Server con 267 MB en memoria. Había 13 nodos de MSBuild
  (unos 1,5 GB) que las compilaciones dejan vivos para reutilizarlos. `dotnet build-server shutdown` los cerró, dejó 2,1 GB
  libres, y las 6 vueltas siguientes (3 locales y 3 tipo CI) salieron limpias en 7-13 s. Antes de una tanda de vueltas,
  compila primero y lanza ese comando.
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
  BD nueva en cada vuelta. Los logs del pipeline piden autenticación y `gh` no está instalado. Una vuelta, en
  Git Bash (compila antes con `dotnet build src/ResidApp.sln -c Release`):

  ```bash
  db=ResidAppCi1
  sqlcmd -S ACER-ORLANDO -E -C -b -Q "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END; CREATE DATABASE [$db]"
  SQL_SERVER=ACER-ORLANDO SQL_DATABASE=$db SQLCMD_EXTRA=-C bash database/aplicar-scripts.sh > /dev/null
  RESIDAPP_TEST_CONNECTION_STRING="Server=ACER-ORLANDO;Database=$db;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True" \
    dotnet test src/ResidApp.sln --no-build -c Release
  sqlcmd -S ACER-ORLANDO -E -C -Q "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]"
  ```

  No la solapes con otra carga sobre SQL Server (aplicar el seed, la app con curl, otra compilación): el
  2026-09-30 una vuelta que coincidió con la aplicación del seed a la base local tardó 1 min 13 s y dio 3
  fallos en tests no relacionados; las tres siguientes, sin nada en paralelo, salieron limpias.
- **Estado de un push en GitHub:** busca su run con `.../actions/runs?head_sha=<sha completo>`; la lista con
  `per_page=1` a veces devuelve un run antiguo. Después, `.../actions/runs/<id>/jobs` da la conclusión de
  `build-and-test` y `deploy`.
- **`sqlcmd` y los índices filtrados:** un `UPDATE` manual sobre una tabla con índices filtrados (como
  `permisos_perfil`) falla con «SET options have incorrect settings: 'QUOTED_IDENTIFIER'» si no se lanza con
  `sqlcmd -I`. Los scripts ya lo llevan (`aplicar-scripts.sh` usa `-I`).
- **Filtros por GET:** un valor mal formado en la URL (por ejemplo `basal=xyz` en un enum) deja un error de
  enlace de modelo en inglés en `ModelState`, que sale en el `asp-validation-summary`. En una acción de lista
  que solo filtra, `ModelState.Clear()` al empezar lo ignora (ver `Residentes`).
- **Buscar sin acentos:** `CultureInfo.InvariantCulture.CompareInfo.IndexOf(texto, busqueda,
  CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)` funciona igual en Windows y en Linux con ICU
  (probado en Azure: «DÓS» encuentra «Dos»). No actives `InvariantGlobalization`, o dejaría de ignorarlos.
- **Recorrido con curl de un escalado:** Enfermería `RegistrarEvento` (`ResidenteId`, `OperacionId`,
  `Observacion`, `Clasificacion`) → `EmpezarValoracion` (`eventoId`, `revision`) → `Valoracion` (`Form.*`, con
  `Form.Revision` de la página) → `Escalar` (`Form.EventoId`, `Form.Revision`, `Form.Motivo`). Medicina
  `EmpezarValoracion` → `Valoracion` (`Form.HallazgosExploracion`, `Form.Valoracion`) → `Cerrar`
  (`Form.OperacionId`, `Form.Comunicacion=NoComunicar`). Lee cada revisión de la página anterior: sube en
  cada paso.
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
- **PDF (QuestPDF desde 2026-11-15; lo que sigue describe PDFsharp-MigraDoc 6.2.4, MIT, y solo sigue valiendo la fuente incrustada y la hora local):**
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
- **`ListScopeResidents` es una puerta clínica, no solo un listado:** `FindScopeResident` lo reutiliza, y de él dependen la
  ficha, la línea temporal, el contacto de urgencia y el basal de Enfermería y Medicina (`EnfermeriaBasalController.ResolveAsync`
  pasa el perfil activo). No lo abras a otro perfil para obtener una lista de residentes: haz un caso de uso propio que exija ese
  perfil y llame al directorio (como `DireccionApplicationService.ListResidentsAsync`). Lo comprueba
  `Residentes_OtrosPerfilesNoEntran_YDireccionSigueSinLaFichaDeEnfermeriaNiMedicina`.
- **Desplegable que conserve la elección tras un POST:** usa `asp-items` con `SelectListItem` (como `Residents/Create`). Con
  `<option>` escritos a mano dentro de un `<select asp-for>`, la opción enviada no salía marcada como `selected`.
- **Consulta auditada de basal sin basal firmado:** `SqlBaselineRepository.ReadAsClinicalDirectionAsync` evalúa primero la
  autorización (`@Authorized`) y solo entonces audita cada versión. Sin autorización lanza `CLINICAL_DETAIL_READ_NOT_AUTHORIZED`,
  como el prototipo; autorizado y sin basal devuelve una lista vacía, sin auditoría, y la pantalla dice que no hay basal (desvío
  deliberado del prototipo, que denegaba también este caso). Para ver versiones hace falta un residente con basal, como «Residente
  Integrado Uno» de la semilla.
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`. Guarda cada página con formulario en un
  fichero y lee de él el token, la revisión y el `OperacionId`. En la prueba en Azure del evento propio, una
  captura en variable salió vacía y la indicación se envió sin revisión: la app la rechazó sin guardar nada,
  pero hubo que repetirla.
