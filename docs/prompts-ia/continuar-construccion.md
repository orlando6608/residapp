# Retomar la construcción en una sesión nueva

Estado a 2026-10-22. Actualízalo al cerrar cada bloque de trabajo, para que la siguiente sesión (con
Claude, ChatGPT o una persona) arranque sin reconstruir el contexto.

## Prompt para empezar

> Retomamos la construcción de ResidApp. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`), `docs/tareas/alta-prioridad/pendientes-enfermeria.md` y
> `docs/tareas/alta-prioridad/pendientes-medicina.md`, `docs/tareas/alta-prioridad/pendientes-direccion.md` y
> `docs/tareas/alta-prioridad/pendientes-administracion.md`.
> Mira si hay respuestas de CJ en `docs/pendientes-cj/` (ficheros `*.respuestas.json`, ver su `README.md`; las recibe Orlando por correo o WhatsApp y las guarda ahí). Después
> comprueba que la suite pasa en verde contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Dónde estamos
- **ADR 0007, incremento I4: passkeys (2026-10-23, PR #54 mergeado en `main` como `26d4f83`; script `0066`, el siguiente libre es `0067`; suite 410 unitarios, 502 de
  integración y 294 funcionales en verde; CI y CI/CD de `main` en verde, con el `0066` aplicado en Azure SQL).** Decisiones de Orlando (2026-10-23): la passkey es **entrada completa** (sin contraseña ni TOTP; contraseña y TOTP
  siguen para el personal como alternativa); se da de alta desde «Mi cuenta» con contraseña y TOTP; **Plataforma entra solo con passkey** en cuanto tiene una;
  `ServerDomain` explícito por entorno y, en Pro, dominio base dedicado a ResidApp (ADR 0007, puerta abierta 5).
  - **En lenguaje llano:** una passkey («llave de acceso») es una credencial guardada en el móvil o portátil de la persona que se desbloquea con
    la huella, la cara o el PIN y sustituye a contraseña y código; solo vale en la web de ResidApp. Una *cookie* es un pequeño fichero que el navegador
    guarda un rato; el *dominio* es el nombre de la web; una *attestation* es la prueba que el fabricante del dispositivo da sobre sí mismo (no se pide).
  - **Qué hay:** `PasskeyController` (`OpcionesEntrada`, `Entrar`, anónimas) y, en `CuentaController`, `PasskeyOpciones`, `PasskeyAlta` y `QuitarPasskey`;
    `wwwroot/js/passkeys.js`; sección «Passkeys» de «Mi cuenta» y botón «Entrar con passkey» en el login. `SqlCredentialStore` implementa `IUserPasskeyStore`
    sobre `dbo.passkeys`. Sin `SignInManager`: `PasskeyHandler<CredentialAccount>` registrado a mano y el estado de cada ceremonia en `PasskeyStateCookie`
    (5 min, un solo uso, el alta ligada a la sesión). `PasskeyOrigin`: HTTPS y `Acceso:PasskeyDominio` o subdominios; `http://localhost` solo en Development.
  - **Las passkeys están desactivadas si falta `Acceso:PasskeyDominio`** (el botón no sale y los endpoints dan 404). Lo lleva `appsettings.Development.json`
    (`localhost`). **En Azure dev está puesto `Acceso__PasskeyDominio=app-residapp-dev.azurewebsites.net`** (ajuste de la Web App). En Pro habrá que ponerlo
    con el dominio base dedicado a ResidApp antes de que nadie registre una passkey (ADR 0007, puerta abierta 5).
  - **Comprobado en Azure dev tras el despliegue** (Chrome sin interfaz con autenticador virtual, clave de desarrollo leída del ajuste de la Web App): la
    Administración de Los Almendros dio de alta `prueba-passkey-20261023-5d67` (ficticia, Enfermería) con su invitación; la persona eligió contraseña y TOTP, registró
    una passkey desde «Mi cuenta», salió y entró con «Entrar con passkey». Sin errores de consola. Las dos primeras peticiones tras el despliegue dieron 500
    (arranque en frío) y luego 200. **Queda en Azure:** esa cuenta (activa, con una contraseña que nadie más conoce y una passkey virtual) y sus filas de
    auditoría y de `eventos_acceso`; no se tocó ninguna cuenta `almendros-*` ni Residente Integrado Uno.
  - **Plataforma:** con passkey, el acceso con contraseña se cierra con un mensaje; sin ninguna, su primer acceso con contraseña y código la lleva a «Mi cuenta»,
    **pero no se bloquea el resto de la aplicación** hasta que la cree (limitación conocida). No puede quitar su última passkey. La recuperación por invitación
    borra las passkeys.
  - **Decisión mía a revisar:** una cuenta **bloqueada por intentos** tampoco entra con passkey (como `SignInManager` de Identity); quien adivine contraseñas puede
    bloquear a una persona también para la passkey durante 15 minutos.
  - **Pruebas:** `AccesoTests.Passkeys.cs` (16, funcionales, con `SoftwareAuthenticator`, un autenticador ES256 con atestación `none` hecho en C#) y 2 nuevas en
    `SqlCredentialStoreTests`. Mutaciones comprobadas: no gastar el estado, alta no ligada a la sesión, entrada sin comprobar el bloqueo y Plataforma entrando con
    contraseña hacen fallar alguna. La comprobación de que la attestation es de la cuenta (`UserEntity`) no la cubre ninguna prueba por separado: es redundante con
    la sesión.
  - **Probado en local con Chrome sin interfaz** (autenticador virtual por CDP, claro, oscuro y 375 px, sin desplazamiento ni errores de consola): invitación,
    contraseña, TOTP, alta de la passkey, salir y «Entrar con passkey». **Queda en la BD local:** cuentas `prueba-navegador-enfermeria-*` del día.
  - **Sin hacer:** familiares, renombrar passkeys, verificar la atestación del fabricante y probar con dispositivos reales (iOS, Android, llave).
  - **Para el DPO:** claves públicas de passkeys custodiadas en `dbo.passkeys`; segundo factor por población.
- **ADR 0007, incremento I2b: vincular cuentas que ya existen en otro centro (2026-10-23, PR #53 mergeado en `main` como `da5f677`; script `0065`, el siguiente
  libre es `0066`; CI del PR y CI/CD de `main` en verde, con el `0065` aplicado en Azure SQL).** Decisiones de Orlando (2026-10-23): la Administración del centro nuevo **confirma** tras el canje; las cuentas de varios centros las
  **gestiona Plataforma**; solo **personal** (los familiares quedan fuera).
  - **Flujo:** `Administracion/Vinculaciones` (botón en «Usuarios»): emite una hoja con el perfil y las unidades **sin decir a quién** (`HojaVinculacion`, QR,
    72 h, un solo uso, solo el resumen SHA-256); la persona la canjea en `Cuenta/Vincular` (código escrito en «Mi cuenta» o enlace/QR; solo cuentas activas con
    TOTP y que no sean la de quien emitió) y acepta con `Cuenta/AceptarVinculacion`, lo que **no concede nada**; la Administración la ve en «Pendientes de
    confirmar» (nombre y usuario de quien canjeó) y **confirma** (crea el ámbito y las unidades con el mismo INSERT de «Conceder perfil») o **rechaza**.
    Una hoja sin canjear se anula. Auditoría `ACCOUNT_LINK_ISSUE/CANCEL/CONFIRM/REJECT`; acceso `VINCULACION_ACEPTADA`.
  - **Cuenta en varios centros:** `RenameAsync` se niega (`ACCOUNT_OTHER_CENTERS`, como ya hacían suspender y las invitaciones); **Plataforma** tiene
    `Plataforma/Cuentas` («Cuentas de varios centros»): invitación de recuperación (`IssueForMultiCenterAsync`), suspender y reactivar
    (`ChangeMultiCenterAccountStatusAsync`), con una fila de auditoría por cada centro de la cuenta. Retirar el perfil de un centro no suspende la cuenta.
  - **Modelo:** `dbo.vinculaciones_cuenta` y `dbo.vinculaciones_cuenta_unidades` (sin RLS: la persona las lee desde otro centro; excepción en
    `CoberturaRlsTests`; triggers 50541 y 50542; se anula solo lo no canjeado).
    `SqlAccountLinkRepository`, `AccountLinksApplicationService`; los códigos `ACCOUNT_LINK_INVALID` y `ACCOUNT_LINK_SECOND_FACTOR` están en `ApplicationResultRunner`.
  - **Pruebas nuevas:** `VinculacionesCuentaTests` (9, integración) y `AccesoTests.Vinculaciones.cs` (6, funcionales). Mutaciones comprobadas: canjear sin
    exigir el segundo factor, resolver sin filtrar por centro y cambiar el nombre sin la guarda multicentro hacen fallar alguna. Suite 410 unitarios, 500 de
    integración y 278 funcionales en verde.
  - **Probado en local con Chrome sin interfaz** (claro, oscuro y 375 px, sin desplazamiento lateral): hoja con QR, código malo, oferta, aceptar, pendiente,
    confirmar, selector con los dos centros y Plataforma. **Queda en la BD local:** centros «Residencia de prueba (navegador …)» y cuentas
    `prueba-navegador-*` y `prueba-plataforma-*`.
  - **Comprobado en Azure dev tras el despliegue** (Chrome sin interfaz, clave de desarrollo leída del ajuste de la Web App): con `dev-integrado-administracion`,
    `/Administracion/Vinculaciones` carga, crea una hoja (código y QR) y la anula; con `dev-plataforma`, `/Plataforma/Cuentas` carga sin desplazamiento lateral.
    **No se probó allí** el canje ni la confirmación (la persona necesita el segundo factor activo; lo cubren las pruebas locales). **Queda en Azure:** una
    vinculación anulada del «Centro de desarrollo — escenario integrado». Las dos primeras peticiones a `/Acceso` justo tras el despliegue dieron 500 y luego 200
    (arranque en frío; no se miró el registro de la Web App). **CodeQL:** 4 alertas `missing-function-level-access-control` abiertas en `main` sobre las acciones
    de `AdministracionController.Vinculaciones.cs`, del mismo tipo que las ~20 ya abiertas en ese controlador (la autorización es la política por defecto más el servicio).
  - **Lección (entorno):** la primera pasada de pruebas **justo después de compilar** da con frecuencia cientos de timeouts de SQL (CPU saturada: 74 y 84
    fallos, 2 a 5 min) y, sin recompilar, la repetición da todo en verde en 20-30 s. La BD local tiene ya **123.000 cuentas** de pruebas (SQL Express, 1 GB de
    memoria): una consulta global sobre `ambitos_perfil` (el listado de Plataforma) tardó 26 s mientras corría la suite, pero 0,2 s en calma. El listado ya se
    hace en dos consultas (identificadores, y luego solo esas cuentas).
  - **Para el DPO:** las reglas de las cuentas de varios centros (quién las suspende, quién las recupera) y que el canje exija el segundo factor.
- **ADR 0007, incremento I3: «Mi cuenta» (2026-10-22, PR #52 mergeado en `main` como `40196f3`; script `0064`, el siguiente libre es `0065`; suite 410
  unitarios, 491 de integración y 272 funcionales en verde, proyecto a proyecto; CI del PR y CI/CD de `main` en verde, con el `0064` aplicado en Azure SQL).**
  - **Comprobado en Azure dev tras el despliegue** (Chrome sin interfaz, clave de desarrollo leída del ajuste de la Web App): `almendros-enfermeria`
    abre `/Cuenta`, que lista su sesión («Esta sesión») y sus últimos accesos. **Solo lectura:** no se probó allí cambiar la contraseña ni cerrar sesiones
    (las cuentas `almendros-*` son compartidas y podrían cerrar la sesión de otra persona); eso lo cubren las pruebas locales.
  - **Qué hay:** `/Cuenta` (`CuentaController`, `[Authorize]`, enlace «Mi cuenta» en el menú de usuario del `_Layout`, para todos los perfiles,
    también Familiar): resumen de la cuenta; cambiar la contraseña (pide la actual, cierra las demás sesiones con motivo `CIERRE_PROPIO`);
    generar 10 códigos de recuperación nuevos (pide contraseña y TOTP vigente; se enseñan en `Acceso/CodigosRecuperacion.cshtml`, que ahora lleva
    `Titulo`); sesiones abiertas propias (inicio, actividad, IP de la entrada) con «Cerrar» y «Cerrar todas las demás»; últimos 10 eventos de acceso.
  - **Decisiones mías (Orlando aprobó el plan):** el TOTP **no** se da de alta, quita ni cambia desde aquí (solo desde una invitación); los «datos
    propios» de FAM-16 quedan fuera; una contraseña actual incorrecta cuenta para el bloqueo de la cuenta y para `AccessFailureThrottle`; no se
    guarda navegador (dato nuevo, lo decide el DPO).
  - **Modelo:** `0064` amplía `CK_eventos_acceso_tipo` (`CONTRASENA_CAMBIADA`, `CODIGOS_REGENERADOS`, `SESION_CERRADA`) y `CK_sesiones_cierre`
    (`CIERRE_PROPIO`). `ISessionStore` gana `ListOpenAsync`, `CloseOwnedAsync` (filtra por cuenta) y `CloseOthersAsync`; `IAccessEventLog`, `ListRecentAsync`.
    `ListOpenAsync` puede devolver sesiones paradas por inactividad: el controlador las filtra con `SessionPolicy.IdleTimeout`.
  - **Pruebas:** `AccesoTests.Cuenta.cs` (8, funcionales) y una nueva en `SqlSessionStoreTests`. Mutaciones comprobadas: no cerrar las demás sesiones
    al cambiar la contraseña, `CloseOwnedAsync` sin filtrar por cuenta y regenerar códigos sin comprobar la contraseña hacen fallar alguna.
  - **Probado en local con Chrome sin interfaz** (claro, oscuro y 375 px, sin desplazamiento lateral ni errores de consola): contraseña incorrecta,
    cambio correcto (la otra sesión pasa al login), cierre de una sesión concreta y código TOTP falso al pedir códigos. Un recorrido de móvil
    mostró que una tabla de sesiones con columna de acción quedaba recortada: ahora es una lista. **Queda en la BD local:** la cuenta
    `prueba-navegador-enfermeria-20261022200457` con las credenciales copiadas de otra de pruebas (contraseña actual
    «otra contraseña de prueba larga»).
  - **Documentado:** ADR 0007, Manual (sección «Mi cuenta»), `acceso-y-sesion.html` y `pendientes-administracion.md`.
- **Límite de intentos de `/Acceso` corregido (2026-10-22, sin script; el siguiente libre sigue siendo `0064`).** Cierra el hallazgo de I2.
  - **Fase 1:** `Acceso:IntentosPorMinuto` pasa de 10 a 120 (techo contra inundaciones; un acceso con TOTP gasta 2 peticiones y una invitación, 4).
  - **Fase 2:** `AccessFailureThrottle` (singleton en memoria, `Security/`) cuenta los fallos por IP en 15 minutos y `AccesoController` responde
    429 (texto plano) al llegar a `Acceso:FallosPorIp` = 20; se comprueba al empezar `Entrar`, `Invitacion` (con token), `SegundoFactor`,
    `CodigoRecuperacion` y `ActivarSegundoFactor`, y se suma en `FailAsync`, en «sin ámbito» y en invitación no válida. Los éxitos no cuentan.
    Limitaciones: se reinicia con cada despliegue y con varias instancias se multiplica; el POST de `Invitacion` no lo comprueba.
  - **Pruebas:** `AccesoLimitesTests` (4, funcionales, entorno Staging con los valores reales). Mutaciones comprobadas: techo en 10 y sin
    `RecordFailure` en `FailAsync` hacen fallar alguna. `appsettings.Development.json` lleva `FallosPorIp` alto (las pruebas comparten la IP «sin-ip»).
    Si se cambian los valores en Azure: `Acceso__IntentosPorMinuto` y `Acceso__FallosPorIp`.
  - **En `main` como `f237ad2`, con el CI/CD en verde y comprobado en Azure dev:** 21 contraseñas incorrectas seguidas desde la misma IP
    dieron 200 y la siguiente, 429 con el texto explicativo; `GET /Acceso/Entrar` sigue en 200. (Se esperaba el corte tras 20 y llegó tras 21:
    sin explicar, sin importancia funcional.) Justo después del despliegue la app dio 500 en todas las rutas durante unos 45 s (el reinicio
    del contenedor; el registro no muestra ninguna excepción) y luego respondió con normalidad. **Queda en Azure:** 21 filas
    `CONTRASENA_FALLIDA` de la IP de Orlando en `eventos_acceso` (usuario inexistente) y esa IP bloqueada 15 minutos.
- **ADR 0007, incremento I2: invitaciones de un solo uso y recuperación por Administración (2026-10-22, PR #51 mergeado en `main` como
  `d2195ec`; script `0063`, el siguiente libre es `0064`; suite 410 unitarios, 490 de integración y 260 funcionales en verde, proyecto a
  proyecto; CI del PR y CI/CD de `main` en verde, con el `0063` aplicado en Azure SQL).**
  - **Comprobado en Azure dev tras el despliegue** (Chrome sin interfaz, clave de desarrollo leída del ajuste de la Web App): la
    Administración de Los Almendros da de alta «prueba-invitacion-20261022-f20c» (ficticia, Enfermería) y le crea la invitación de acceso
    (la hoja da un enlace `https://app-residapp-dev…`); la persona la usa (contraseña, TOTP, 10 códigos) y entra; el mismo enlace da 410;
    una invitación de recuperación cambia la clave del móvil y cierra la sesión que tenía abierta; la contraseña incorrecta da el mensaje
    neutro; anular una invitación pendiente y suspender la cuenta funcionan. **Queda en Azure:** esa cuenta, suspendida, con una contraseña
    que nadie conoce, y sus filas en `invitaciones_acceso`, `eventos_acceso` y la auditoría. No se tocó Residente Integrado Uno ni se creó
    ninguna invitación para las cuentas `almendros-*` existentes (esas siguen entrando por el acceso de desarrollo).
  - **Hallazgo (corregido después, ver el primer punto de «Dónde estamos»):** el límite de intentos de `/Acceso` es de 10 por minuto **por dirección IP y para todos los usuarios a la vez**
    (`Acceso:IntentosPorMinuto`, la misma política en `Entrar`, `SegundoFactor`, `CodigoRecuperacion`, `ActivarSegundoFactor` e
    `Invitacion`). Usar una invitación gasta 4 y entrar con TOTP, 2: en la prueba de Azure dio un 429 al undécimo intento. Con el personal
    de una residencia detrás de una sola IP pública (cambio de turno, varias tabletas), 5 personas entrando en el mismo minuto bastarían
    para bloquear a las demás. Antes de datos reales conviene subir el límite o repartirlo por IP y usuario.
  - **CodeQL del PR #51 (4 alertas nuevas, abiertas a propósito por decisión de Orlando):** 130 (cookie `residapp_invitacion` sin `Secure`
    explícito: la política de cookies lo pone fuera de Development, como en la 129) y 131 a 133 («acción sin comprobación de autorización»
    en `Invitacion`, `AnularInvitacion` y `Usuario`: todas exigen sesión por la política global y el servicio comprueba el ámbito de
    Administración, igual que las 9 de Importar/Exportar). Si se quiere apagarlas, la opción es `[Authorize]` en `AdministracionController` y
    `PlataformaController` (no cambia el comportamiento) o descartarlas en GitHub como falsos positivos.
  - **Decisiones de Orlando:** la invitación vale 72 h y una nueva anula la anterior; el TOTP del personal **solo** se da de alta desde una
    invitación (cierra el hueco de I1); el acceso de desarrollo se mantiene en Azure dev hasta los datos reales; vincular cuentas de otro
    centro queda para I2b. **Supuestos míos:** emite Administración para cuentas con perfiles solo en su centro (personal o familiar) y
    nunca la propia; Plataforma, para cuentas de Administración; una recuperación no cambia nada hasta que se usa; las cuentas `almendros-*`
    reciben su invitación desde la app (ningún secreto en los seeds).
  - **Modelo:** `dbo.invitaciones_acceso` (solo el resumen SHA-256 del token, `tipo` ALTA/RECUPERACION según si la cuenta ya tiene
    contraseña, a lo sumo una viva por cuenta con índice único filtrado, triggers 50539/50540, emisor ADMINISTRACION/PLATAFORMA/DESARROLLO);
    `eventos_acceso` admite `INVITACION_USADA`, `RECUPERACION_USADA` e `INVITACION_NO_VALIDA`; `sesiones`, el motivo `RECUPERACION`.
    `SqlAccessInvitationRepository` reutiliza `EnsureAdministratorAsync`, `AuditAsync`, `EnsureOperatorAsync` y `EnsureAdministrationScopeAsync`.
    **Lección:** `FORCESEEK` no se admite en la tabla destino de un `UPDATE … WITH (FORCESEEK)`: va en `UPDATE alias … FROM tabla alias WITH (FORCESEEK)`.
  - **Web:** parcial `_EstadoInvitacion` en la ficha del usuario y en la autorización del familiar (`AdministracionController.Invitaciones.cs`,
    con `[FromServices]`); `PlataformaController.Invitacion` en el centro; hoja `Views/Shared/HojaInvitacion.cshtml` (QR con qrcodejs,
    «Imprimir», `no-store`); `AccesoController.Invitacion` (GET con token → cookie `residapp_invitacion` y redirección sin token, `Referrer-Policy:
    no-referrer`; POST con la contraseña; 410 con un único mensaje si no vale); `PendingAccess.CanEnroll`; `DevAuth/Credenciales` crea una
    invitación para la cuenta de la sesión (ya no pone contraseñas ni quita el segundo factor).
  - **Pruebas nuevas:** `InvitacionesAccesoTests` (8, integración) y `AccesoTests.Invitaciones.cs` (5, funcionales; `AccesoTests` pasa a
    `partial`). Mutaciones comprobadas: consumir sin `usada_en IS NULL`, entrar sin exigir la invitación al personal sin TOTP y recuperar sin
    cerrar sesiones hacen fallar alguna prueba.
  - **Probado en local con Chrome sin interfaz** (claro, oscuro y 375 px, sin desplazamiento lateral; en consola solo los 410 esperados de la
    invitación usada): ficha, hoja (y vista de impresión), alta con contraseña, TOTP y 10 códigos, recuperación que cierra la sesión abierta en
    otro navegador, invitación usada, `almendros-elena` desde su autorización (de recuperación, porque ya tenía contraseña) y Plataforma.
    **Queda en la BD local:** las cuentas `prueba-navegador-*` e `invitada-*` del día y `almendros-elena` con una contraseña de prueba.
  - **Visto de paso, sin tocar:** al imprimir, la barra de navegación sale en todas las páginas (el `_Layout` no la oculta con
    `d-print-none`); la pregunta 3.2 de `portal-familiar-confirmaciones-cj.html` sigue describiendo Entra (código por correo).
- **ADR 0007, incremento I1: ASP.NET Core Identity, y los dos pendientes de I0 (2026-10-22, PR #50 mergeado en `main` como `d88e63d`; script
  `0062`, el siguiente libre es `0063`; suite 410 unitarios, 482 de integración y 255 funcionales en verde, proyecto a proyecto; CI del PR y
  CI/CD de `main` en verde, con el `0062` aplicado en Azure SQL).**
  - **Comprobado en Azure dev tras el despliegue** (Chrome sin interfaz, clave de desarrollo leída del ajuste de la Web App): sin sesión,
    `/Administracion` lleva a `https://…/Acceso/Entrar`; `almendros-elena` con contraseña mala da el mensaje neutro y con la buena llega a su
    residente; `almendros-enfermeria` pasa por el alta del TOTP (QR, 10 códigos), después entra con contraseña y código; al terminar se le
    quitó el segundo factor desde `DevAuth/Credenciales`. **Quedan en Azure:** esas dos cuentas con una contraseña aleatoria que nadie
    conoce (no afecta al acceso de desarrollo, que sigue igual) y sus filas en `eventos_acceso`.
  - **Decisiones de Orlando:** contraseña inicial solo desde el acceso de desarrollo hasta I2 (`DevAuth/Credenciales`); TOTP obligatorio
    para el personal y Plataforma (alta forzada en el primer acceso), familiares solo con contraseña hasta la 3.2 de CJ; DevAuth sigue con
    clave en Azure dev hasta I2; registro de seguridad en tabla propia más `ILogger`.
  - **Modelo:** el usuario de Identity es la cuenta (`Id = cuentas.id`, `UserName = sujeto_externo`). Solo `UserManager`, sin
    `SignInManager`: la sesión sigue siendo la de I0. `Microsoft.AspNetCore.App` como `FrameworkReference` en Infrastructure (sin paquete
    nuevo). `SqlCredentialStore` (Dapper) sobre `dbo.credenciales`: los intentos fallidos se suman y ponen a cero en SQL (dos a la vez no se
    pisan), los códigos de recuperación se guardan como resúmenes SHA-256 y `SubjectLookupNormalizer` busca el sujeto tal cual.
    `dbo.eventos_acceso` + `SqlAccessEventLog` (inmutable, errores 50537/50538, sin lo tecleado si el usuario no existe).
  - **Web:** `AccesoController` (`Entrar`, `SegundoFactor`, `CodigoRecuperacion`, `ActivarSegundoFactor`, `Salir`, `Solicitar`);
    `SessionSignIn` (abrir y cerrar sesión, compartido con DevAuth); `PendingAccessCookie` (`residapp_acceso_pendiente`, 10 min, solo
    `/Acceso`, con el sello de seguridad); `AccesoOptions` (5 fallos / 15 min, 12 caracteres, `Acceso:IntentosPorMinuto`); `LoginPath`
    siempre `/Acceso/Entrar`. QR con qrcodejs 1.0.0 (MIT) en `wwwroot/lib/qrcodejs`, revisado (sin red ni `eval`). `DevAuth/Logout` pasa a
    `Acceso/Salir` (con DevAuth deshabilitado daba 404).
  - **Pendientes de I0 cerrados:** (1) `SessionCookieEvents.RedirectToLogin`: un POST sin sesión vuelve al `Referer` de la misma app (o al
    Inicio) con `sesionCaducada=true`, y el login avisa de que no se guardó; ya no hay `ReturnUrl` a una acción solo POST. (2) La maqueta
    `DevAuth/Register` se quita; «Cómo pedir el acceso» (`Acceso/Solicitar`) lo explica sin formulario.
  - **Pruebas nuevas:** `AccesoTests` (10) y `SqlCredentialStoreTests` (6); mutaciones comprobadas: sin `AccessFailedAsync`, sin exigir el
    TOTP al personal y sin comprobar el sello del paso pendiente falla alguna. `CoberturaRlsTests` lleva `credenciales` y `eventos_acceso`.
  - **Probado en local con Chrome sin interfaz** (claro, oscuro y 375 px, sin errores ni desplazamiento lateral): credenciales de prueba,
    entrar con contraseña, alta del TOTP con el QR y un código calculado, código malo, 10 códigos, entrar con TOTP y con un código de
    recuperación, `Acceso/Solicitar`, aviso de sesión caducada y `almendros-elena` (familiar) solo con contraseña hasta su residente.
    **Queda en la BD local:** `almendros-elena` con contraseña (la de prueba del recorrido) y las cuentas `prueba-navegador-*` de ese día.
  - **Documentado:** ADR 0007 (I1 hecho y lo que deja para después: clave TOTP en claro, alta del TOTP solo con contraseña hasta I2,
    reutilización del código en su ventana, conservación de la IP), Manual, `acceso-y-sesion.html` y `flujo-de-trabajo.html`.
  - **Una pasada de integración tardó 3 min 51 s** (todo en verde, con 7.702 filas de auditoría); la siguiente, 39 s. Sin causa clara.
- **ADR 0007, incremento I0: capa común de sesión (cierra C1 y A2) y centro de prueba «Residencia Los Almendros» (2026-10-21; script `0061`, el
  siguiente libre es `0062`; suite 410 unitarios, 476 de integración y 245 funcionales en verde, proyecto a proyecto; commits `2325f47`, `455a0ec`
  y `b5522c0` en `main`, desplegados en Azure dev con el CI en verde y comprobados allí).**
  - **Pendiente detectado (los dos, cerrados en I1 el 2026-10-22):**
    - si la sesión caduca con un formulario a medias, al guardar se pasa por el login y el `ReturnUrl` apunta a una acción solo POST:
      probablemente acabe en un error 405 (no comprobado). Arreglo posible: devolver al GET de la pantalla o al Inicio.
    - la maqueta «Solicitar acceso» (`DevAuth/Register`) pide correo y contraseña para crearse una cuenta, lo contrario del ADR 0007 (las
      cuentas las da Administración o Plataforma); sustituirla por «pide el acceso a la Administración de tu centro», como mucho en I2.
  - **Dependabot (2026-10-21):** las 6 alertas abiertas (Vite y `launch-editor`, 1 alta) eran todas de `docs/legado-cloudflare/package.json`.
    Se quitaron del legado `package.json`, `pnpm-lock.yaml` y `pnpm-workspace.yaml` (nadie lo ejecuta; su README lo explica y dice cómo
    recuperarlos de git). El resto del legado se conserva: lo citan 31 veces el `CLAUDE.md`, ADRs, migraciones, el dominio y los bocetos como
    fuente de las reglas de negocio. Tras el push, comprobar que GitHub cierra las alertas solo.
  - **Decisión:** el ADR 0007 pasa a **ASP.NET Core Identity propio** (no Entra External ID: sin TOTP, passkeys con Front Door fuera del EU Data
    Boundary, sin invitaciones). Orlando hace de Seguridad; las puertas del DPO esperan a la residencia piloto. Incrementos I1–I5 en el ADR.
  - **Sesión:** cookie de ASP.NET Core `residapp_sesion` (sujeto, id de sesión y nombre visible; cifrada por Data Protection) validada en cada petición
    contra `dbo.sesiones` por `Web/Security/SessionCookieEvents.cs` con `SessionPolicy` (Application/Sessions): inactividad 30 min y caducidad absoluta
    12 h (`Sesion:*` en `appsettings.json`), que fallan cerradas; la renovación no amplía la absoluta. `CookieSessionIdentityProvider` sustituye a
    `DevSessionIdentityProvider` (borrado); el puerto no cambia. La hora sale de `TimeProvider` (las pruebas la adelantan).
  - **Política global** `FallbackPolicy = RequireAuthenticatedUser`: solo son anónimos `Home`, `DevAuth`, `AccesoCj` y los estáticos
    (`MapStaticAssets().Add(AllowAnonymous)`, sin lo cual el CSS y la PWA pedirían sesión). Una ruta inexistente sin sesión ya no da 404, sino el login;
    salvo `/pendientes-cj/*`, que tiene un `MapFallback` con 404.
  - **Acceso de desarrollo** (`DevAuthOptions`): solo si `DevAuth:Habilitado` (true en `appsettings.Development.json`); fuera de Development pide
    `DevAuth:Clave` (16+ caracteres, ajuste de la Web App) y sin ella la app no arranca; mensaje neutro con clave (B1); límite de intentos por IP
    (`DevAuth:IntentosPorMinuto`, alto en Development por las pruebas); cambiar de cuenta termina las declaraciones clínicas y cierra la sesión anterior.
  - **A2:** `UseForwardedHeaders` (redes privadas del balanceador de App Service), HSTS fuera de Development, `UseCookiePolicy` con `Secure` siempre fuera
    de Development; el CI pone `httpsOnly=true` (paso «Exigir HTTPS en la Web App»).
  - **Seed `database/seed/dev_seed_los_almendros.sql`:** el centro del Flujo de trabajo con las cuentas `almendros-*` (una por perfil y Elena), Planta 1 y
    Planta 2, turnos, equipos, rangos, citas y Rosa en Planta 1 · 104 · B con Elena autorizada. Probado dos veces sobre una BD nueva con todos los seeds.
  - **Pruebas nuevas:** `SesionTests` (barrido de todas las acciones sin sesión, cookie antigua y sesión inventada, cierre, suspensión, cambio de
    cuenta, inactividad, caducidad sin ampliar) y `SesionFueraDeDesarrolloTests` (entorno Staging: clave, mensaje neutro, cookies `Secure`, 429, 404
    deshabilitado, no arranca sin clave), `SqlSessionStoreTests` y `SessionPolicyTests`. `CoberturaRlsTests` lleva `sesiones` como excepción de diseño.
  - **Probado en local con Chrome:** cada cuenta `almendros-*` entra y llega a su inicio; Rosa sale en las listas del Auxiliar y de Enfermería
    como Planta 1 · 104 · B con el basal pendiente; Elena la ve en el portal; sin sesión, `/Administracion` lleva al login y lo público carga.
  - **Ajustes de la Web App (puestos el 2026-10-21, antes del push):** `DevAuth__Habilitado=true` y `DevAuth__Clave` (23 caracteres) en
    `app-residapp-dev` (`az webapp config appsettings set -g rg-residapp-dev -n app-residapp-dev --settings ...`). Sin ellos nadie entra en Azure dev;
    con `Habilitado` y sin clave, la app no arranca. El CI solo añade `WEBSITE_TIME_ZONE`, así que los despliegues no los borran. La clave la tiene
    Orlando y se pasa a CJ en privado: nunca en el repositorio ni en los documentos.
  - **Comprobado en Azure dev tras `2325f47` (CI en verde):** `httpsOnly=true` y `http://` da 301 a `https://`; el login pide la clave; una clave
    incorrecta da el mensaje neutro; la cookie fabricada `residapp_dev_subject` lleva al login (C1 comprobado); las seis cuentas `almendros-*` y
    `dev-integrado-enfermeria` entran con la clave, con la cookie `secure; samesite=lax; httponly`.
  - **Fallo encontrado y corregido después (`455a0ec`, desplegado y comprobado: llega HSTS y el login redirige a `https://`; C1 y A2 cerrados en
    Azure dev):** no llegaba HSTS y el login redirigía a `http://`. El balanceador de App Service
    Linux llega desde `::ffff:169.254.130.1` (enlace local), que no estaba en `KnownIPNetworks`, así que la app ignoraba `X-Forwarded-Proto` (y el
    límite de intentos contaba a todos como una IP). `Program.cs` añade `169.254.0.0/16` y su equivalente `::ffff:`; prueba
    `SeFiaDelBalanceadorDeAppService_YDeNadieMas` (balanceador sí, IP pública no). En Azure, `https://…/` trae
    `Strict-Transport-Security: max-age=2592000` y `/Administracion` sin sesión redirige a `https://…/DevAuth/Login`.
  - **Página `wwwroot/acceso-y-sesion.html`** (pública, estilo del Flujo de trabajo, enlazada en el pie y en el Manual): cómo se pide el acceso y
    quién lo da, qué hace Plataforma o soporte caso por caso, las comprobaciones de cada pantalla, entrar, cuánto dura la sesión, las cuentas de
    Los Almendros, qué protege, lo que falta (I1–I5) y dónde vive cada pieza. Revísala con cada incremento del ADR 0007.
  - **Lecciones de Azure (Linux):** para ver por qué no se aplican las cabeceras reenviadas, el registro de depuración de
    `ForwardedHeadersMiddleware` dice «Unknown proxy: …». En App Service Linux los puntos del nombre de un ajuste se cambian por `_`, así que
    `Logging__LogLevel__Microsoft.AspNetCore.HttpOverrides` no llega; sirve `Logging__Console__LogLevel__Default=Debug` (temporal, con
    `az webapp log config --docker-container-logging filesystem` y `az webapp log download`). Ese `log config` añade el ajuste
    `WEBSITE_HTTPLOGGING_RETENTION_DAYS`: quítalo al terminar, con el de depuración, y deja el registro del contenedor en `off`.
  - **Lección:** un seed con tildes aplicado a mano con `sqlcmd` sin `-f 65001` guarda los nombres mal (`aplicar-scripts.sh` ya lo pasa). En la BD
    local se corrigieron así los de Los Almendros; el de Rosa, con una corrección de identidad desde la app.
- **Importar y exportar datos por ficheros (2026-10-21, estudio de mercado R1, solo la parte administrativa; ADR 0010; script `0060`, el siguiente libre es
  `0061`; suite 405 unitarios, 473 de integración y 222 funcionales en verde; commit `b5e6536` en `main`, desplegado en Azure dev con el CI en verde).**
  - **Cómo se llegó:** el usuario pidió analizar funciones con IA (Mistral) y, tras descartar las que chocaban con las reglas, eligió atacar el doble registro
    con un módulo de importación. Decidió hacerlo **sin IA**: el emparejamiento de columnas es determinista (plantilla del centro, sinónimos, formato de los
    datos y, si no, a mano). La IA se reserva para el dictado del ADR 0009, que es el **siguiente bloque** y necesita su propio plan.
  - **Lo hecho (detalle en `pendientes-administracion.md`):**
    - `/Administracion/Importar` (estructura y residentes desde CSV, XLS o XLSX, con vista previa fila a fila, reimportación sin duplicados y plantillas por
      centro);
    - `/Administracion/Exportar` (estructura y censo en CSV, auditado antes de entregarlo, también con las columnas de otra aplicación);
    - auditoría `IMPORT_RUN`, `EXPORT_RUN` y `EXCHANGE_TEMPLATE_SAVE`;
    - dependencia nueva: ExcelDataReader 3.9.0 (MIT);
    - el fichero no se guarda en el servidor: `intercambio.js` lo reenvía en cada paso.
  - **Supuestos para CJ:** `docs/pendientes-cj/importacion-exportacion-confirmaciones-cj.html` (8 preguntas, no bloquea): fecha de alta del importado, criterio
    de duplicado, nombre y apellidos, sexo que falta, familiares, censo exportado, quién importa y exporta, y si se estudia exportar los registros
    asistenciales.
  - **Probado en local con Chrome sin interfaz** (Playwright; ver «Lecciones técnicas»):
    - recorrido completo con ficheros ficticios: estructura de 5 filas (4 nuevas, 1 con error), residentes con un valor de sexo sin reconocer (re-análisis y
      foco de vuelta al desplegable), plantilla aplicada sola al mismo fichero, plantilla de exportación y las tres descargas;
    - en claro, oscuro y a 375 px, sin desplazamiento lateral ni errores en la consola;
    - encontró y se corrigieron dos fallos: el nombre de la plantilla borraba la vista previa al pulsar «Importar», y el mensaje «Plantilla guardada» se
      perdía;
    - quedan en la base local los centros «Residencia de prueba (intercambio …)» con sus cuentas `prueba-intercambio-adm*`.
  - **Sin hacer:**
    - no se ha probado con exportaciones reales de ResiPlus, Gerosalus u otro programa;
    - no se ha probado en Azure dev (el despliegue del 2026-10-21 aplicó el `0060`): conviene hacerlo con un fichero ficticio, sin tocar «Residente
      Integrado Uno»;
    - **Aviso (fuera de este bloque):** `dotnet test src/ResidApp.sln` lanza a la vez los proyectos de integración y funcionales contra la misma BD
      local. Ese día falló de forma intermitente: 3 de 6 pasadas, con decenas de tiempos de espera de SQL (ejecución y también inicio de sesión) en
      pruebas de todo tipo.
      - Con el código de `HEAD`, sin este bloque, falló igual (219 de integración), así que no lo causa este bloque.
      - Uno a uno (`dotnet test` de cada proyecto) todo sale en verde en 32 s y 13 s.
      - Si vuelve a pasar, ejecuta los proyectos por separado. Arreglarlo (por ejemplo, que no corran en paralelo) es otra tarea.
- **Foto y habitación en la lista de residentes (2026-10-20, estudio de mercado R2 en parte; script `0059`, el siguiente libre es `0060`; suite 361 unitarios,
  460 de integración y 219 funcionales en verde; sin commit ni despliegue todavía):** al revisar el código se vio que la habitación y la plaza ya salían en Auxiliar,
  Enfermería y Medicina desde el 2026-10-02. El estudio no las vio porque ningún residente de Azure tiene habitación (11 de 11 «SinHabitacion»), así que se corrigió
  su debilidad 3. Lo hecho:
  - `ResidentLocationLabel` dice «Sin habitación asignada» en vez de callarlo, y Administración muestra la ubicación en su lista (columna «Ubicación») y en la ficha
    («Ubicación actual»);
  - **foto de identificación**: tabla `fotos_residente` (una por residente, se sobrescribe y se borra, seguridad por filas), `ResidentPhoto.Normalize` (solo
    JPEG ≤ 1 MB, quita EXIF/XMP/comentarios y lo que va tras el fin de la imagen), `ResidentPhotoApplicationService`, `SqlResidentPhotoRepository` (auditoría
    `RESIDENT_PHOTO_SET`/`REMOVE`) y `FotoResidenteController` (`Ver` responde 404 sin distinguir el motivo, `Guardar` y `Quitar`), con los parciales
    `_FotoResidente` (miniatura o iniciales) y `_FotoResidenteEditar` (fichas de Administración y Enfermería) y el encuadre en `site.js` (recorte cuadrado de 480 px, JPEG 0,85; desde el 2026-10-21, antes reducía la foto entera);
  - la ven Auxiliar (solo asignados), Enfermería, Medicina y Administración, nunca la familia ni Dirección; la ponen Administración y Enfermería con la casilla de
    consentimiento. Todo son **suposiciones** en `docs/pendientes-cj/foto-residente-confirmaciones-cj.html` (8 preguntas), junto con la cama obligatoria en el alta,
    que **no** se ha cambiado (decisión del usuario: preguntar a CJ, con la propuesta «obligatoria si la unidad tiene camas, con "Pendiente de asignar"»);
  - Manual actualizado (Administración, Estructura, Auditoría, Mis residentes, Residentes de Enfermería y Medicina).
  - Comprobado en local con Chrome sin interfaz: una foto de 900×1200 sale del navegador como JPEG de 360×480 y 6 KB; listas y fichas de los cuatro perfiles en
    escritorio y a 375 px (y en oscuro), sin desplazamiento lateral; el Portal Familiar no la enseña y el familiar recibe 404 al pedirla.
  - Queda en la base local la foto de «Residente Integrado Dos (ficticio)».
  **Desplegado y probado en Azure dev (2026-10-20, commit `f1bca37`; CI/CD en verde, el `0059` aplicado; tras el despliegue la app dio un 500 un momento mientras
  arrancaba).** Datos de prueba creados por la interfaz:
  - en la unidad del escenario integrado, «Habitación 101 (prueba)» con «Cama A» y «Cama B», y «Habitación 102 (prueba)» con «Cama A»;
  - «Residente Integrado Dos (ficticio)» trasladado a 101 · Cama A, con foto puesta por `dev-integrado-administracion`;
  - «Prueba manual 2026-10-11 Cierre Uno (ficticio)» trasladado a 102 · Cama A, con foto puesta por `dev-integrado-enfermeria`;
  - 101 · Cama B queda libre. Residente Integrado Uno y los de CJ no se han tocado.

  El resto de R2 (buscador en la lista de la Auxiliar y orden por pendiente de cierre) sigue pendiente.

  **Después (2026-10-20, a petición del usuario):**
  - la foto es un cuadrado con esquinas redondeadas (`rounded-3`): primero en la cabecera de las fichas y la vista previa (2026-10-20) y, desde el 2026-10-21,
    también en las listas (las guías 1 y 5 pasan a la versión 2026-10-21);
  - guías de pruebas: guía 1, prueba 13 nueva (foto y cama como Auxiliar, Enfermería y Medicina, foto puesta por Enfermería en «Residente Portal Familiar» y Portal
    Familiar sin foto); guía 5, prueba 16 nueva (poner, cambiar y quitar la foto, ubicación en la lista, auditoría). Se añadieron al final para no renumerar;
  - ambas guías van con `data-version` 2026-10-20 (las respuestas de la guía 1 son de la versión anterior: el documento avisa); 85 pruebas en total;
  - en sus páginas de flujo, la caja 13 de la guía 1 y una quinta fila de Administración con la 16 en la guía 5 (la franja de Enfermería baja 95 px);
  - «Flujo de trabajo» menciona la foto en el alta de Rosa, el paso 1 de «Llega un residente» y lo que la familia no ve nunca.
  - Una pasada de la suite funcional falló con 36 tiempos de espera de SQL Server, sin bloqueos ni crecimiento de la auditoría (77.578 filas); repetida, 219 en verde.
  - **Encuadre de la foto (2026-10-21, sin script ni cambios en el servidor):** al elegirla, `_FotoResidenteEditar` muestra un lienzo de 480×480 donde se arrastra
    (puntero, con `setPointerCapture` y `touch-action: none` para que el dedo no desplace la página), se mueve con cuatro botones de flecha (alternativa al
    arrastre, WCAG 2.5.7) o con las flechas del teclado, y se acerca de 1× a 3× con «Acercar»; la foto cubre siempre el cuadrado (el desplazamiento se limita).
    `site.js` envía ese recorte: un JPEG cuadrado de 480 px con calidad 0,85, ya no la foto entera reducida (las fotos guardadas antes siguen centradas con
    `object-fit-cover`). **Lección:** el fichero se rehace con `toDataURL` (síncrono) tras cada ajuste y no con `toBlob`, para que al pulsar «Guardar foto»
    justo después de mover ya esté el último encuadre. Comprobado en local con Chrome sin interfaz (script de un solo uso): arrastre con ratón y con el dedo a
    375 px, botones, teclado, zoom sin bordes vacíos, la foto servida es el encuadre elegido, y sin JavaScript el recuadro no aparece y se guarda el JPEG tal
    cual. Manual, guías 1 (prueba 13) y 5 (prueba 16) y el contexto de `foto-residente-confirmaciones-cj.html` actualizados.
- **Flujo de trabajo ilustrado (2026-10-16, sin script; suite funcional 215 en verde):** `src/ResidApp.Web/wwwroot/flujo-de-trabajo.html`, página estática y **pública** como el Manual (no va tras la clave de CJ), enlazada desde el pie de `_Layout.cshtml` como «Flujo de trabajo», entre el Manual y los Documentos de CJ. Cuenta la vida de una residente ficticia (Rosa, en la «Residencia Los Almendros») con todos los perfiles: quién interviene (siete tarjetas), un mapa general con cada caja enlazada a su sección, una línea temporal de 23 pasos y 13 diagramas SVG en línea (puesta en marcha de un centro, lugares y personas, alta y basal, autorización del familiar, cierre de turno, episodio con horas, salidas de Enfermería y Medicina, estados del episodio según `TR_ea_transition_guard`, urgencia, comunicado, citas y estancia), la tabla de los siete hitos con los plazos de `ProcessMilestoneRules.Defaults` y «Lo que todavía no está» (tomado de «Próximamente» del Manual). Mismo estilo que las páginas de flujo de las guías (tokens, IBM Plex, un color por perfil), más el encabezado de marca (§4 de la guía visual), la impresión siempre en claro y el tema elegido en la app (`residapp-tema` de `localStorage`). Se generó con un script de un solo uso fuera del repositorio: **para cambiarla, edita el HTML**. Comprobada con capturas de Chrome sin interfaz (claro, oscuro, tema «Oscuro» de la app, 375 px sin desplazamiento lateral e impresión), sin anclas, identificadores ni marcadores rotos, y con cada etiqueta entre «» presente en una vista. Test `FlujoDeTrabajoPageTests` (enlace del pie y página servida sin clave). **Siguiente:** que CJ diga si le sirve.
- **Páginas de flujo de las guías 3 a 8 (2026-10-14, sin script; suite funcional 213 en verde):** `docs/pendientes-cj/pruebas-3-…-flujo.html` a `pruebas-8-…-flujo.html`, con el mismo estilo que las de la 1 y la 2 (carriles por perfil en SVG, «Paso a paso» con los cambios de cuenta y «Dónde mirar»), más una franja por perfil para Dirección (granate), Administración (verde) y Plataforma (gris), una línea blanca entre franjas y, en la guía 8, flechas rojas discontinuas para lo que la aplicación tiene que bloquear. La guía 7 lleva dos diagramas (reserva directa y solicitud previa). Se generaron con un script de un solo uso fuera del repositorio: **para cambiarlas, edita el HTML**. Enlazadas desde la cabecera y el «Antes de empezar» de cada guía (sin subir `data-version`: no cambia ninguna pregunta) y desde `pruebas.html`; añadidas a `PendientesCjPagesTests.Pages`; README actualizado. Comprobadas con capturas (claro, oscuro y 375 px) y sin enlaces rotos. El contenido sale solo de los pasos de cada guía. **Siguiente:** que CJ diga si le sirven.
- **Páginas de flujo de las guías de pruebas 1 y 2 y la prueba 2.5 aclarada (2026-10-14, sin script; suite funcional 201 en verde):** `docs/pendientes-cj/pruebas-1-auxiliar-enfermeria-flujo.html` y `pruebas-2-medicina-indicaciones-flujo.html`, HTML estático como las guías (mismo estilo y tokens, claro y oscuro, sin script de respuestas): diagrama de carriles por perfil en SVG en línea (cada prueba numerada en el carril de quien la hace, Imprescindibles con borde grueso, flechas de lo que pasa a otro perfil con dónde lo ve), el mismo recorrido en texto («Paso a paso», con los cambios de cuenta) y «Dónde mirar». En el móvil el diagrama se desliza dentro de su caja; la página no. Enlazadas desde la cabecera y el «Antes de empezar» de cada guía y desde `pruebas.html`; añadidas a `PendientesCjPagesTests.Pages`; regla en el README: si cambia una guía con flujo, revisar su flujo. Comprobadas con capturas de Chrome headless (claro, oscuro y 375 px dentro de un iframe, porque headless no baja de unos 500 px de ventana). **Guía 2, prueba 5:** «Qué debería ocurrir» dice ahora dónde mirar (etiqueta del evento junto al nombre del residente; cada indicación con la suya); `data-version` a `2026-10-14`, así que CJ verá el aviso de respuestas de otra versión en sus 6 respuestas. **Siguiente:** enseñar las dos páginas a CJ y, si le sirven, hacer las de las guías 3 a 8 con el mismo patrón.
- **Segunda vuelta de CJ (2026-10-14, tras el PR #49 desplegado): guía 1 repetida y guía 2 a medias (6 de 13), sin script nuevo.** Guía 1: 6 y 12 pasan a A (arreglos confirmados); la 2 ya no lleva comentario (la lentitud de Inicio de Enfermería no se repitió o no la anotó: sigue sin medir). **Prueba 4 (B):** el aviso directo vacío mostraba «This field is required.» en inglés (jQuery Validate, por el `required` del HTML); `_ValidationScriptsPartial` ahora fija en español `required` y `maxlength` para las 41 vistas que lo cargan, con aserción en el funcional del Auxiliar (falla sin el cambio). Quedan sin traducir los demás mensajes de jQuery Validate (`number`, `email`…), que hoy ningún campo dispara. **Prueba 10 (A, con comentario):** «Fecha prevista» solo admite el día (`DateOnly`, `type=date`), y la guía decía «mañana a las 10:00»: corregida la guía; **pregunta para CJ: ¿quiere hora en la fecha prevista del seguimiento?** (el flujo ENF-08 solo dice «fecha prevista»; añadirla sería un cambio de dominio y de esquema). Guía 2: **prueba 4 (B)** «poca información de a qué valoración médica se refiere» en la pantalla de conducta médica (`Medicina/Conducta` solo muestra el nombre del residente; sin tocar, falta confirmar qué quiere ver: ¿fecha y autor del último guardado, o un resumen del texto, como en `Medicina/Cerrar`?). **Prueba 5 (B):** esperaba ver «Con indicación pendiente» y ve «Pendiente de lectura»; el evento sí muestra «Con indicación pendiente» junto al nombre (comprobado y fijado en el funcional `Indicaciones_MedicinaNoCierraSinCumplirlas…`) y «Pendiente de lectura» es el estado de cada indicación; probablemente la guía debe decir dónde mirar. Sin editar la guía 2 para no cambiar su versión mientras CJ la contesta. Siguiente: que CJ termine la guía 2 (7 a 13) y responda las dos preguntas; entonces decidir 2.4 y 2.5.
- **Primeras respuestas de CJ a las pruebas (guía 1, 2026-10-14, `pruebas-1-auxiliar-enfermeria.respuestas.json`; script `0058`, el siguiente libre es `0059`; suite 456 de integración y 197 funcionales en verde):** 12 pruebas: 7 en A y 5 con incidencia. **Prueba 4 (C, error real):** el cambio prioritario con el motivo «Alteración de conciencia o del estado general» daba «No se ha podido completar la operación» porque su código (`ALTERACION_CONCIENCIA_ESTADO_GENERAL`, 36 caracteres) no cabía en `motivo_prioritario_codigo NVARCHAR(32)` (0004) y SQL Server truncaba el INSERT; los otros cinco motivos miden de 23 a 26. Corregido con `0058` (columna a 64, quitando y recreando `CK_ccr_change` y `CK_ccr_priority`, que la usan) y un test que recorre todos los motivos del catálogo (`RegisterChangeAsync_Prioritario_AceptaCadaMotivoDelCatalogo`; falla sin el script). **Prueba 6 (C):** consecuencia de la 4 (el prioritario nunca se grabó); falta que CJ la repita. **Prueba 12 (B):** «Preparar comunicación» tras una derivación era un selector de una sola opción ya marcado, y pulsarlo no hacía nada; ahora la pantalla dice «Sí: Preparar comunicación» (campo oculto con el mismo valor) y se comprueba en el funcional de derivación; Manual y guía actualizados. **Prueba 10 (C):** no parece un fallo del código sino del camino: «Iniciar seguimiento» está en la decisión asistencial, que solo se ofrece tras guardar una valoración, y CJ solo había registrado el evento. La guía lo detalla ahora (versión `2026-10-14`); sin confirmar con CJ que ese fuera el motivo, ni probado por HTTP (no hay test web de `Enfermeria/IniciarSeguimiento`, solo de servicio). **Prueba 2 (B, sin resolver):** tras cambiar a `dev-integrado-enfermeria`, Inicio de Enfermería tardó mucho; `EnfermeriaController.Index` hace nueve consultas seguidas, cada una con la lista completa solo para contar, y en Azure dev los eventos de prueba no se borran; es una hipótesis sin medir (también puede ser arranque en frío tras el cambio de identidad). **Lección:** un test de repositorio con un solo valor de un catálogo no cubre los demás; si el código de un enum se guarda en una columna de ancho fijo, recorre todo el catálogo. **Siguiente:** que CJ repita 4, 6, 10 y 12 tras el despliegue; medir Inicio de Enfermería; esperar las guías 2 a 8.
- **Guía de pruebas para CJ dividida en 8 guías (2026-10-13, rama `guias-pruebas-cj-por-bloques`):** la guía única (`guia-de-pruebas-cj.html`, 82 pruebas, sin respuestas de CJ) se retiró y se repartió en
  `docs/pendientes-cj/pruebas-1-auxiliar-enfermeria.html` … `pruebas-8-aislamiento.html`, agrupadas por dependencias (las que usan lo que deja otra van juntas y en orden) y numeradas de nuevo en cada guía; cada una
  lleva arriba los enlaces a Pruebas, al índice y a la app, y su propio `.respuestas.json`. `pruebas.html` las lista en orden, con el glosario y lo que no se prueba. Al revisarla se corrigió lo desfasado:
  el Portal Familiar dado por inexistente (glosario, «Qué NO se prueba», bandeja de comunicaciones), «Solicitud previa» como no disponible, «28 de 76» imprescindibles, la cuenta `dev-integrado-familiar` y su residente
  que faltaban, «Ha comido poco» (no existe esa opción), «Cerrado por Enfermería» (el detalle dice «Cerrado por ti»), la consulta de Dirección con «Propósito» / «Supervisión clínica» (ahora finalidad y justificación),
  y se añadieron el apartado «Comunicaciones» del informe de derivación y una prueba de la lectura clínica auditada de Dirección (DIR-06, 07, 12, 14 y 15), que existían desde el 2026-10-07 sin prueba: **83 pruebas, 30 imprescindibles**.
  **Siguiente:** pasárselas a CJ; por cada `pruebas-N-….respuestas.json` que devuelva, guardarlo junto a su guía y convertir cada B o C en una tarea.
- **Análisis de seguridad (2026-10-13, PR #45 en `main`):** el informe, con modelo de amenazas, hallazgos priorizados y lo no comprobado, está **solo en la máquina de Orlando** (`docs/seguridad/hallazgos.md`, excluido en `.git/info/exclude`, sin versionar por ahora). En `main` está `.github/workflows/codeql.yml`: CodeQL para C#, compila solo el proyecto web (`build-mode: manual`), consultas `security-extended`, en push y PR a `main` y una vez a la semana. En los PR solo informa de las líneas que cambia el PR. El análisis completo de `main` ya se hizo y sus alertas están clasificadas en el informe. Se activaron las alertas de Dependabot, pero no las actualizaciones automáticas: las 7 que hay son de `next` en el prototipo `docs/legado-cloudflare` (no desplegado) y no aplican a la app. Decisión de Orlando: el repo sigue público por ahora y se privatizará antes de cargar datos reales (los requisitos están en el informe). **Siguiente:** las correcciones que el informe marca como bloqueantes antes de datos reales, en su orden.
- **Tercera tanda de preguntas a CJ (2026-10-13, `docs/pendientes-cj/confirmaciones-tercera-tanda-cj.html`, 19 preguntas en 6 temas, sin responder):** revisados los pendientes, lo que de verdad espera a CJ es (1) el **resumen periódico para la familia** (qué es: texto nuevo o recopilación; frecuencia por centro o por residente, que las historias 3 del portal y 5 de Administración contradicen; quién redacta y aprueba; qué pasa si no hay novedades; si sustituye a los comunicados sueltos) y (2) **qué se ve con la finalidad de calidad asistencial** de Dirección (el permiso específico ya lo decidió CJ el 2026-10-07, falta el alcance); ambas **bloquean**. El resto son confirmaciones de decisiones mías ya construidas: registro de las lecturas de Dirección (por consulta o por evento), consultas de Dirección dentro de la trazabilidad, nombre de quien firma, texto de comunicados sin declaración, cifras de la tabla de frecuencia; las cuatro suposiciones del inicio de Administración; quién cierra un episodio con indicaciones que nadie cumple; qué ve la unidad de destino de los episodios cerrados tras un traslado; y si hay avisos genéricos por correo y recordatorios de cita. **Ya no esperan a CJ (corrección de la lista anterior):** DIR-13 (hecho), DIR-11 (hecho, plazos configurables) y el permiso de la cuarta finalidad (decidido el 2026-10-07, solo falta construirlo cuando se responda 2.1). Siguen esperando al proveedor de identidad el segundo factor y «Mi cuenta» del portal, y ADM-29 y ADM-30.
- **Mejora 1 del inicio de Administración: filtro «Requiere atención» en la lista de residentes (2026-10-12, PR #41 en `main`; sin script):** `Administracion/Residentes?atencion=SinHabitacion|AutorizacionPendiente|AutorizacionCaducada|AutorizacionPorCaducar` y un desplegable nuevo en el filtro; las cuatro líneas correspondientes del inicio enlazan a él. `ListAttentionAsync` comparte con el inicio los CTE `scoped`/`latest` (`SqlAdministrationOverview.ScopedAuthorizationsCtes`) para que cuenten igual. **Mejora 2:** `FORCESEEK` en las lecturas de `citas` y `citas_cambios` dentro de transacciones (huecos ocupados, número de cambio, cita propia por clave, lista por centro y residente, historial con lista vacía protegida, cita futura por equipo, doble pulsación por operación); quedan sin él las lecturas fuera de transacción (agenda y bandeja de Administración). **Mejora 3:** tabla «Frecuencia y estado por unidad» en `Direccion/Comunicados` (DIR-13; `UnitCommunicationFrequency`, solo cifras; sin estado «con error» porque no existe). Guía de pruebas de CJ: 5.10 (82 pruebas, 30 imprescindibles). **Probado en Azure dev (2026-10-12, tras el PR #41; CI/CD verde):** el inicio de Administración enlaza a `Residentes?atencion=SinHabitacion` (11 de 11 residentes) y `AutorizacionPendiente` (2 de 11); `AutorizacionCaducada` y `AutorizacionPorCaducar` no encuentran a nadie; `dev-integrado-direccion` ve la tabla de frecuencia con una sola unidad («Unidad de desarrollo — escenario integrado»: 10 comunicados, 2,3 por semana, 0 programados, 9 publicados, 1 con correctora, 1 retirado, 9 en la última semana); la citas de la familia, la agenda y la bandeja responden 200 con el `FORCESEEK` nuevo. Tras el despliegue la app dio 502 durante un minuto mientras arrancaba.
- **Administración: inicio con lo que requiere atención (2026-10-12, ADM-01 y historia 10; sin script, PR #39 en `main`; suite 346, 449 y 171 en verde):** `Administracion/Index` muestra el bloque «Requiere atención» con cifras y enlaces (solicitudes de cita por gestionar, propuestas esperando a la familia, autorizaciones pendientes, caducadas o que caducan en 30 días, residentes sin habitación) y, debajo, las citas de hoy y de los próximos 7 días y los comunicados de la semana. Solo cifras. `SqlAdministrationOverview` (una consulta de solo lectura, sobre `ScopedResidentsSelect`; la autorización vigente es el último cambio de cada vínculo) + `AdministrationOverviewApplicationService` (límites del día en la zona del centro) + `AdministrationCommunicationsSummary`. Suposiciones mías: ubicación pendiente = sin habitación, «pronto» = 30 días, vínculo sin cambios = pendiente, citas del centro entero. No incluido: conflictos de turnos (no se guardan), ADM-29/30 (proveedor de identidad) y filtros de la lista de Residentes. **DIR-13 ya estaba hecho** (`Direccion/Comunicados`): la línea de `pendientes-direccion.md` estaba desactualizada. **Interbloqueo corregido de paso (lección de 0055/0056, otra vez):** una vuelta tipo CI con BD nueva dio un 1205 en `SqlFamilyAppointmentRequests.ReadAsync` (lectura de la solicitud dentro de `AcceptAsync`): el `OR` sobre `@requestId` hacía que el plan recorriese `citas_solicitudes` entera y dos aceptaciones de familias distintas se esperaban entre sí; igual con `ReadChangesAsync` (historial de `citas_solicitudes_cambios`). Ahora ambas llevan `WITH (FORCESEEK)` (la solicitud, por clave; la lista, por centro y residente; el historial, por solicitud, y con lista vacía no se consulta). Tras el arreglo: 6 vueltas de integración y 3 de funcionales con BD nueva, sin ningún 1205. Quedan sin `FORCESEEK` varias lecturas de `citas` y `citas_cambios` (reserva directa) que no han dado ningún 1205, anotadas por si aparece uno. **Probado en Azure dev (2026-10-12, tras el PR #39; CI/CD verde):** con `dev-integrado-administracion` el inicio muestra «2 autorizaciones de familiares pendientes de activar» y «11 residentes sin habitación asignada» (los 11 de su ámbito, ninguno con habitación), «Citas confirmadas: 0 hoy y 0 en los próximos 7 días» (las de la prueba de las solicitudes se cancelaron), «0 programados y 8 publicados en los últimos 7 días» y ninguna línea de solicitudes (las 6 de la bandeja están aceptadas, canceladas o cerradas); `dev-integrado-enfermeria` recibe «No se puede acceder a esta operación». Justo tras el despliegue la app dio 500 unos segundos mientras arrancaba. Guía de pruebas de CJ: 6.32 (81 pruebas, 30 imprescindibles).
- **Portal Familiar, bloque 5: citas con solicitud previa (2026-10-12, PR #37 en `main`, script `0057`; el siguiente libre es `0058`; probado en Azure dev; suite 342, 444 y 170 en verde):** historia 5 del portal y 8 de Administración (`CIT-S01…CIT-S04`, `CIT-C02`, `CIT-C03`). Tablas `citas_solicitudes` (estados PENDIENTE, PROPUESTA, ACEPTADA, CANCELADA, CERRADA; a lo sumo una abierta por residente y equipo, `UX_citas_solicitudes_abierta`; idempotencia `UX_citas_solicitudes_operacion`) y `citas_solicitudes_cambios` (inmutable), con triggers de identidad y de transiciones, seguridad por filas y las búsquedas con `FORCESEEK`. **La solicitud no es una cita:** solo al aceptar la familia se inserta una fila normal en `citas` (CONFIRMADA, `operacion_id` = id de la solicitud) y la solicitud la enlaza; desde ahí es una cita como las demás. **Una propuesta reserva su hueco** mientras viva: `AppointmentSql.ReadTakenAsync` suma las propuestas vivas (con `exceptRequestId` para la que se acepta), proponer y aceptar se ordenan con `LockTeamAsync`, y una propuesta cuya hora ya empezó caduca (calculado, sin job). Dominio `AppointmentRequests.cs` (estados, motivos, `AppointmentAvailability`, `AppointmentRequestInput.Validate`, `AppointmentRequestRules`); `AppointmentServiceSettings.Validate` admite `SolicitudPrevia` y `RequestsEnabled`. Aplicación: `AppointmentRequestsApplicationService` (Administración) y `FamilyAppointmentRequestsApplicationService` (familia); infraestructura `SqlAppointmentRequestRepository`, `SqlFamilyAppointmentRequests` y `AppointmentRequestSql`; `SqlFamilyAppointments.FindLinkAsync` pasa a `internal` y `AppointmentSql.RecordChangeAsync` admite `auditAction` nulo (la aceptación audita sobre la solicitud, no sobre la cita). Web: `Administracion/SolicitudesCitas`, `ProponerCita`, `CerrarSolicitudCita` (`AdministracionController.SolicitudesCitas.cs`) y, en el portal, `Solicitudes/Nueva`, `Solicitudes/{id}` y sus acciones Aceptar, OtroHorario y Cancelar (`PortalFamiliarController.Solicitudes.cs`); la pantalla de citas ofrece «Solicitar una cita» solo en ese modo. Auditoría en «Citas»: `APPOINTMENT_REQUEST_CREATE|PROPOSE|OTHER_TIME|ACCEPT|CANCEL|CLOSE`. **Desviación del plan:** sin pantalla intermedia de revisión; el formulario envía directamente (la solicitud no compromete a nada y el `operacionId` oculto cubre la doble pulsación). **Suposiciones sin confirmar con CJ:** tema 6 (6.1 a 6.9) de `portal-familiar-confirmaciones-cj.html`, ahora de 28 respuestas. **Pruebas:** `AppointmentRequestsTests` (unitarias), `CitasSolicitudesTests` (11, de integración: idempotencia, límites, la propuesta reserva el hueco, aceptar crea la cita y repetirlo no la duplica, caducada o hueco tomado, cerrar sin enseñar el motivo, aislamiento por cuenta, vínculo, centro y autorización, cambio de modo, dos propuestas o dos aceptaciones a la vez, triggers) y el funcional `Solicitudes_LaFamiliaSolicita_…`; mutaciones comprobadas (sin filtro de cuenta, la propuesta sin reservar hueco y aceptar sin mirar la caducidad hacen fallar pruebas). **Interbloqueos:** 6 vueltas de integración y 3 de funcionales tipo CI (BD nueva, Release, `SqlErrorLog`) sin ningún 1205. **Limitaciones:** sin avisos ni recordatorios a la familia (Administración la avisa por teléfono); una cita nacida de una solicitud no se reprograma desde el portal (se cancela y se pide otra); la bandeja es solo de Administración y de todo el centro. Guía de pruebas de CJ: 6.28 a 6.31 (80 pruebas, 30 imprescindibles). **Probado en Azure dev (2026-10-12, tras el PR #37; el despliegue aplicó `0057`):** con `dev-integrado-administracion` se pasó el centro a solicitud previa y con `dev-integrado-familiar` se envió una solicitud (sin motivo se rechaza; repetir la misma `operacionId` devuelve la misma solicitud; una segunda abierta del mismo equipo se rechaza), Administración propuso el martes 13/10 a las 16:00 (el hueco deja de ofrecerse), la familia vio la propuesta sin el profesional, pidió otro horario (el hueco volvió a ofrecerse), se propuso otro y dos aceptaciones simultáneas crearon una sola cita Confirmada que salió en la agenda; además un cierre por Administración (sin motivo no se cierra; la familia no ve el motivo), una cancelación por la familia con confirmación, y una propuesta viva que se aceptó después de pasar el centro a reserva directa (nace la cita y no se admiten solicitudes nuevas). **Cuidado al repetirlo:** el formulario de «Citas con la familia» reenvía solo lo que lleva marcado, y un POST sin las modalidades las vacía (pasó en esta prueba y se restauraron las seis). Al terminar se cancelaron las dos citas de prueba y el centro volvió a reserva directa; quedan en Azure las solicitudes ficticias (aceptadas, cancelada y cerrada) y cuatro citas canceladas.
- **Portal Familiar, bloques 3 y 4: citas con reserva directa (2026-10-12, PR #30 en `main`, script `0054`; el siguiente libre es `0057` (`0055` son los índices contra interbloqueos y `0056` el `FORCESEEK` en triggers); probado en Azure dev; suite 327, 433 y 164 en verde):** `citas_configuracion_centro` (modo, antelación, horizonte, versión), `citas_modalidades_habilitadas`, `citas_franjas` y `citas_bloqueos` (se retiran, no se borran), `citas` y `citas_cambios` (inmutable), todas con seguridad por filas. **El hueco no se guarda:** `AppointmentSlots.Generate` (dominio) lo calcula con las franjas (hora local del servidor, cambios de hora incluidos), la antelación, el horizonte, los bloqueos y las citas; la exclusión real es el índice único filtrado `UX_citas_hueco` (centro, equipo, inicio, no canceladas) y la idempotencia, `UX_citas_operacion` (cuenta, operación). Reservar y reprogramar se ordenan con un bloqueo de aplicación por centro y equipo (`AppointmentSql.LockTeamAsync`, `sp_getapplock`) y se comprueban en la misma transacción que escribe. **Administración:** `AppointmentServiceApplicationService` + `SqlAppointmentServiceRepository` (configuración con versión, franjas, bloqueos, agenda, reprogramar y cancelar con motivo, finalizar), pantallas `Administracion/Citas`, `AgendaCitas`, `CancelarCita` y `ReprogramarCita`. **Familia:** `FamilyAppointmentsApplicationService` + `SqlFamilyAppointments`, que reutiliza `SqlFamilyPortalReader.AccessibleLinks` (ahora `internal`) como único criterio de acceso; pantallas `/Familia/Residente/{linkId}/Citas` (lista), `Nueva` (equipo y modalidad → huecos), `Revisar` (confirmar con `operacionId`), `{citaId}` y `{citaId}/Cancelar`. Si el hueco se ocupa, se ofrecen los demás (CIT-D04). Los errores de negocio llegan a la web como `Conflict` genérico (el código detallado no se expone): los mensajes de la familia lo explican en conjunto. **Seed** `dev_seed_familiar_citas.sql` (reserva directa, las tres modalidades y franjas de lunes a viernes para el centro del escenario integrado; va después de `dev_seed_familiar.sql` por el orden alfabético). **Suposiciones sin confirmar con CJ:** tema 5 (5.1 a 5.8) del documento `portal-familiar-confirmaciones-cj.html`, ahora de 19 respuestas; además la 4.1 recoge que la historia 6 de Administración pide un permiso específico para retirar, que hoy no existe. **Pruebas:** `AppointmentsTests` (9), `CitasTests` (12, con mutaciones comprobadas: sin filtro de cuenta, sin límite por equipo, sin antelación para la familia y el propio hueco que sigue ocupado al reprogramar) y un flujo por HTTP en `PortalFamiliarFlowTests`. Guía de CJ: 6.24 a 6.27 (76 pruebas, 28 imprescindibles, versión `2026-10-14`). **Pendiente:** la solicitud previa (historia 5 del portal y 8 de Administración); avisos y recordatorios a la familia; la agenda no la ven Dirección, Enfermería ni Medicina; el tiempo de las franjas depende de la zona horaria del servidor (`TimeZoneInfo.Local`, como el horario de los comunicados). **Probado en Azure dev (2026-10-12, tras el PR #31 en `main`; el despliegue aplicó `0054`, `0055` y `dev_seed_familiar_citas.sql`):** con `dev-integrado-administracion`, `/Administracion/Citas` muestra reserva directa, 24 h, 30 días, las seis modalidades marcadas y las franjas de lunes a viernes. Con `dev-integrado-familiar` (Residente Portal Familiar), el primer hueco es el martes 13/10 a las 16:00 (+02:00, hora de Madrid, con 24 h de antelación); se reservó Equipo médico presencial, repetir la misma `operacionId` devolvió la misma cita, reprogramar a las 16:30 dejó el historial y liberó el hueco, Administración la vio en la agenda con quién la reservó y la canceló con un motivo interno, y la familia vio «Cancelada … por el centro» sin el motivo y el hueco volvió a estar libre. Además se reservó una cita de Enfermería telefónica y la familia la canceló sola. Enfermería recibe «No se puede acceder a esta operación» en `/Administracion/Citas`, sin datos. Quedan en Azure dos citas canceladas de prueba (ficticias). **Bucle de redirecciones de `/Familia` (bloque 1 del portal; corregido el 2026-10-12 en `arreglo-bucle-familia`):** un perfil que no era familiar y abría `/Familia` entraba en un bucle con `/ProfileScope/Select?returnUrl=%2FFamilia` (ERR_TOO_MANY_REDIRECTS), porque Select, con un solo ámbito, lo volvía a seleccionar y a devolver aquí. Ahora, con un ámbito activo que no es familiar, `PortalFamiliarController.ScopeRedirect` responde 403 con «Esta sección es del Portal Familiar» y los botones «Cambiar de ámbito» e «Ir al inicio»; sin ámbito activo sigue yendo a elegir uno. Test `PortalFamiliar_ConUnPerfilProfesional_NoEntraEnBucleDeRedirecciones_ConLaAppEntera` (falla sin el arreglo).
- **Portal Familiar, bloque 2: corregir o retirar lo ya publicado (2026-10-12, PR #28 en `main`, script `0053`; suite 318, 421 y 163 en verde; probado en Azure dev):** historia 3 salvo el resumen periódico. **Modelo:** `comunicaciones_familiares_correctoras` (cada fila es una publicación nueva vinculada al comunicado que corrige, con su tipo, su texto y su `publicada_en`) y `comunicaciones_familiares_retiradas` (a lo sumo una por comunicado, con un motivo interno); las dos inmutables, con seguridad por filas y triggers (el residente es el del comunicado; no se corrige un retirado). Que el comunicado esté publicado (depende de la hora) y no retirado lo comprueba la aplicación bajo `UPDLOCK`. **Personal:** `IFamilyCommunicationAmender` implementado en `SqlFamilyCommunicationPublication` (comparte con `PublishNowAsync` el bloqueo y la lectura en `LockAndReadAsync`); `FamilyCommunicationPublicationApplicationService.PublishCorrectiveAsync` y `WithdrawAsync`; acciones `PublicarCorreccion` y `RetirarComunicado` en `AdministracionController` y `DireccionController` con las vistas compartidas `Views/Shared/PublicarCorreccion.cshtml` y `RetirarComunicado.cshtml`; el parcial `_ComunicadosPublicacion` muestra correctoras y retirada y los botones solo si `CanAmend`. Auditoría `FAMILY_COMMUNICATION_CORRECTIVE` y `FAMILY_COMMUNICATION_WITHDRAW` (la columna `accion_codigo` admite 32 caracteres: con `..._CORRECTIVE_PUBLISH` truncaba). **Familia:** `FamilyPortalCommunication` lleva `Kind` (original, corrección, retirada) y `Corrects`; el historial mezcla originales y correctoras por fecha de publicación, la original avisa «Tiene una corrección», la retirada es una tarjeta sin texto («Esta novedad ya no está disponible») que solo sale en «Todas», y las correctoras de un comunicado retirado no salen. **Suposiciones sin confirmar con CJ** (tema 4, preguntas 4.1 a 4.5, del documento `portal-familiar-confirmaciones-cj.html`, ahora de 11 respuestas): lo hacen Administración y Dirección Clínica; la correctora tiene su propia ventana (30 días o 6 meses desde que se publica) aunque el original ya no se vea; retirar oculta también las correctoras; el motivo no lo ve la familia; una retirada no se deshace. **Pruebas:** `CorrectorasYRetiradasTests` (7, con mutaciones comprobadas: correctoras de un retirado visibles, retirada sin vaciar el texto, corregir sin publicar, sin filtro de residente en las correctoras y ventana sin límite), `FamilyPortalVisibilityTests` y `FamilyCommunicationWithdrawalTests`, y un flujo por HTTP en `PortalFamiliarFlowTests`. Guía de CJ: 6.22 y 6.23 (72 pruebas, 26 imprescindibles, versión `2026-10-13`). **Probado en Azure dev (2026-10-12, tras el despliegue, que aplicó `0053`):** con `dev-integrado-administracion` se publicó una corrección relevante sobre el comunicado de «Residente Portal Familiar (ficticio)» (sin texto no se publica); con `dev-integrado-direccion` se publicó antes de hora y se retiró un segundo comunicado (sin motivo no se retira; retirarlo otra vez da error); la familia ve la corrección con «Corrige la novedad publicada el…» y el original con «Tiene una corrección», y del retirado solo «Esta novedad ya no está disponible», sin texto ni motivo; la familia no abre los formularios del personal. **Queda en Azure:** esos dos comunicados (uno con una corrección, otro retirado) y tres eventos más abiertos y sin valorar del residente ficticio (de intentos del recorrido que ejecutaron el registro sin completar el cierre). El primer CI de `main` falló una vez en la siembra de `CorreccionComunicadosTests` (conflicto de revisión al cerrar un evento) y pasó al relanzarlo: segunda intermitencia sin causa demostrada tras la de B1. **Pendiente:** la lista de comunicaciones de Enfermería y Medicina sigue mostrando el texto original sin avisar de que se corrigió o retiró; el flujo de Dirección no tiene prueba funcional propia (comparte vistas y servicio con Administración, que sí).
- **Portal Familiar B1 mergeado y probado en Azure dev (2026-10-11, PR #25 y #26 en `main`; el despliegue aplicó `0052` y el seed `dev_seed_familiar.sql`):** el primer CI del PR #25 falló en `Indicaciones_MedicinaNoCierraSinCumplirlas_YEnfermeriaCierraElEpisodio_ConLaAppEntera` (la indicación no se guardó, `EN_VALORACION_MEDICA` en vez de `CON_INDICACION_PENDIENTE`) y pasó al relanzarlo; no se reproduce en local ni había otro fallo en las 80 ejecuciones anteriores. Es una intermitencia sin causa demostrada: el test no comprueba la respuesta del POST previo (¿conflicto de revisión en la valoración médica?); si se repite, ampliarlo para que lo haga. Recorrido por HTTP con `dev-integrado-enfermeria`, `-direccion` y `-familiar`: antes de publicar la familia ve «No hay novedades…»; Enfermería registra, valora y cierra preparando un comunicado relevante, Dirección lo publica antes de hora y la familia lo ve en el inicio y en el detalle («Equipo asistencial del centro»), sin nada del personal (ni `dev-integrado`, ni «Documentos de CJ», ni «Enfermería»); el filtro «Ordinarias» queda vacío y «Relevantes» lo muestra; un vínculo o comunicado inventado, o el `Guid` vacío, da 403; sin sesión redirige a elegir ámbito; las pantallas del personal no enseñan datos a la familia; `Administracion/Usuario` sin `cuentaId` redirige a Inicio. **No probado aún en Azure:** dar el acceso desde Administración y suspender/revocar la autorización (lo cubren los tests). **Queda en Azure:** «Residente Portal Familiar (ficticio)» con un evento cerrado y un comunicado relevante publicado, y dos eventos más abiertos y sin valorar (de intentos que ejecutaron el POST sin seguir la redirección).
- **Identificadores vacíos ya no dan 500 (2026-10-11, PR #26, en `main`):** una dirección o un formulario sin identificador (por ejemplo `Administracion/Usuario` sin `cuentaId`) llegaba a la acción como `Guid.Empty`, y `OpaqueId` lanzaba un `ArgumentException` sin tratar (500, con cualquier perfil). Ahora lanza `InvalidIdentifierException` (sigue siendo un `ArgumentException`) y `InvalidIdentifierFilter` (filtro de excepciones global, registrado en `Program.cs`) devuelve a Inicio con un aviso en el registro. Tests `IdentificadoresVaciosTests` (una dirección, un formulario y un barrido de todas las rutas GET sin identificadores; sin el filtro los tres fallan). **Ojo:** el filtro también atrapa un `Guid.Empty` que viniera de nuestro propio código; el aviso del registro (`Petición con identificador vacío en …`) es lo que lo delataría.
- **Portal Familiar, bloque 1 (2026-10-11, PR #25, en `main`, script `0052`; suite 316, 414 y 159 en verde, con las intermitencias de timeouts de SQL ya conocidas):** primera entrega sin esperar al proveedor de identidad (ADR 0007 sigue en propuesta), con el login de desarrollo. **Cuenta del familiar:** `familiares.cuenta_id` (solo pasa de NULL a un valor; una cuenta por familiar y centro) y un único ámbito `FAMILIAR` por centro **sin unidades**; Administración la crea en `Administracion/AutorizacionFamiliar` («Acceso al Portal Familiar», `GrantFamilyPortalAccessAsync`, auditoría `FAMILY_PORTAL_ACCESS_GRANT`) y «Usuarios» no la lista. **Lectura:** `PortalFamiliarController` (`/Familia`, layout propio `_LayoutFamilia`, sin nada del personal), `FamilyPortalApplicationService` (perfil Familiar o `AccessDenied`) y `SqlFamilyPortalReader`, cuyo único criterio de acceso es `AccessibleLinks` (cuenta de la sesión → familiar → vínculo no desvinculado → última autorización Activa y vigente; la RLS por centro no distingue familiares ni residentes, así que el aislamiento está ahí y en sus tests). Un comunicado se ve si está publicado (`FamilyPortalVisibility`: ordinario 30 días, relevante 6 meses, desde la publicación) con el texto vigente de la última corrección; del evento no se lee nada. Cada lectura (residente o comunicado) se audita en la misma transacción como `FAMILY_PORTAL_READ`, fuera de la auditoría de Administración. **Estados:** sin autorización activa, sin novedades, acceso denegado (403) y fallo técnico (503) se distinguen. **Pruebas:** `PortalFamiliarTests` (11, con mutaciones comprobadas: sin filtro de autorización, de vigencia, de desvinculado o de cuenta falla alguna), `FamilyPortalVisibilityTests` y `PortalFamiliarFlowTests` (flujo por HTTP y un barrido: ninguna ruta GET de los controladores del personal devuelve a la familia el residente ajeno ni el texto de un evento; con una sesión de Enfermería el mismo barrido falla). **Seed** `dev_seed_familiar.sql` (cuenta `dev-integrado-familiar`, «Residente Portal Familiar (ficticio)», autorización Activa; sin comunicados: se generan por el flujo real). **Suposiciones sin confirmar** en `docs/pendientes-cj/portal-familiar-confirmaciones-cj.html` (6 preguntas; la ficha «Preguntas» del índice vuelve a la normal); guía de pruebas de CJ a 70 pruebas (25 imprescindibles, 6.18 a 6.21, versión `2026-10-12`). **Siguiente (bloques, ver el roadmap):** B2 correctoras y retiradas (hoy `TR_cfc_valid` impide corregir lo publicado), B3/B4 citas, B5 resumen periódico por residente (la historia pide frecuencia por residente y el horario es por centro: contradicción a resolver con CJ), B6 proveedor real. (El 500 por identificadores vacíos que se vio de paso está corregido: ver la nota siguiente.)
- **Mergeado y probado en Azure dev (2026-10-11, PR #22 en `main`; el despliegue aplicó `0050` y `0051`):** no había copia suelta del documento archivado en la raíz de `site/wwwroot/pendientes-cj/` (el despliegue limpia `wwwroot`). Recorrido por HTTP con `dev-integrado-administracion`, `-enfermeria`, `-medicina` y `-direccion` (no hizo falta crear cuentas), todo como esperaba: alta con motivo de «sin contacto urgente»; búsqueda y vínculo de un familiar de otro residente del centro; desvincular al único contacto (queda «sin contacto» con ese motivo); Enfermería ve el motivo; Medicina no cierra con una indicación sin cumplir (botón deshabilitado y envío forzado rechazado), Enfermería la lee, la realiza, la encuentra en «Para cerrar» y cierra, y Medicina ve «Cerrado por otro profesional de Enfermería»; comunicado preparado por Medicina, que lo corrige, y Dirección lo publica antes de hora (Medicina y Administración lo ven publicado y ya no se corrige); horario de comunicados cambiado a 20:30 y 120 min (un comunicado nuevo salió a las 20:30) y **restablecido a 19:00 y 60 min**; derivación a Urgencias de un residente sin contacto cerrada sin intento de llamada. **Queda en Azure:** «Prueba manual 2026-10-11 Cierre Uno (ficticio)» (3 eventos cerrados, 2 comunicados, una familiar desvinculada) y «Prueba manual 2026-10-11 Sin Contacto Dos (ficticio)» (1 derivación cerrada, la familiar de Uno vinculada como «Sobrina»), más el historial de dos cambios del horario. Detalle menor: el texto de la tarjeta «Cerrar el evento» de `_ProtocoloUrgente.cshtml` aún dice que exige un intento de llamada aunque el residente no tenga contacto urgente.
- **Respuestas de CJ a las confirmaciones de la segunda tanda (2026-10-10 / 2026-10-11, rama `respuestas-cj-confirmaciones`, scripts `0050` y `0051`; el siguiente libre es `0052`; sin push; suite 311, 403 y 155 en verde, con las intermitencias de timeouts de SQL ya conocidas al compilar justo antes):** el documento `confirmaciones-segunda-tanda-cj` (12/12) está **archivado** en `docs/pendientes-cj/archivados/`; al desplegar hay que borrar en Azure su copia de la raíz si existiera. Hecho, en commits aparte: (C, 4.2) **Medicina no cierra con indicaciones sin cumplir** (`MEDICAL_INDICATIONS_PENDING` en `ClinicalEventCloser`, salvo desde `PROTOCOLO_URGENTE_MEDICO`); cumplidas, cierra **Enfermería** desde `CON_INDICACION_PENDIENTE` (cierra la valoración médica en borrador sin cambiar su autoría) **o Medicina**; el perfil que cierra se lee de la auditoría (`ClosedByProfileSql`) en detalle, historial y línea temporal; tarjeta «Para cerrar» de Enfermería (`ListEventsReadyToClose`, `Enfermeria/ParaCerrar`), botón «Cerrar episodio» y redirección al cumplir la última indicación; Medicina ve las no realizadas de un evento cerrado por Enfermería. Sustituye a la decisión del 2026-09-28. (A, 1.1) **vincular un familiar buscando en todo el centro** (`LinkableFamilySearch`, mínimo 3, 30 resultados, `ActiveLinks`). (B, 2.1, script `0050`) **residente sin contacto urgente con motivo** (`SIN_CONTACTO`, `motivo`; alta, ficha y desvincular al único; `ResidentEmergencyContacts`; derivación sin intento de llamada y hito `LlamadaFamilia` no medido). (D–G, 5.1–5.4, script `0051`) **horario de comunicados por centro** (`comunicados_horario_centro`, `Administracion/HorarioComunicados`, hora fijada al guardar en `comunicaciones_familiares.publicacion_programada_en`), **corregir hasta publicar**, **Dirección publica antes** (`Direccion/Comunicados`; el servicio pasó a llamarse `FamilyCommunicationPublicationApplicationService`) y **Medicina** con `Medicina/Comunicaciones`/`CorregirComunicacion`. Sin cambio por decisión de CJ: 2.2, 3.1, 3.2, 4.1, 4.3. Detalle y suposiciones en `pendientes-administracion.md` y `pendientes-medicina.md`. Guía de pruebas de CJ ampliada a 66 pruebas (22 imprescindibles, versión `2026-10-11`). **Pendiente:** salvaguarda para cerrar un episodio con indicaciones que nadie cumple (unidad sin Enfermería, baja distinta del fallecimiento); Portal Familiar. **Al desplegar:** el pipeline aplica `0050` y `0051` solo; tras el push, comprobar en Azure dev.
- **Respuestas de CJ del 2026-10-07 incorporadas (aclaraciones y traslado/baja; rama `respuestas-cj-aclaraciones-traslado-baja`, sin push):** los dos documentos están **archivados** (`docs/pendientes-cj/archivados/`). Suite: 273, 133 y 358 en verde (una ejecución de las tres a la vez falló una prueba de Dirección que sola y en la repetición pasa: la intermitencia ya anotada).
  - **Constantes (sin script):** temperatura sugerida 36–36,9 (un decimal: avisa desde 37,0), glucemia 70–120; la SatO₂ con oxigenoterapia avisa con el mismo rango indicando el flujo (`VitalSignAlert.OxygenFlowLpm`); retirados el aviso visual de la Auxiliar y la regla «varias veces seguidas».
  - **Script `0039`:** `REFERENCE_RANGES_MANAGE` solo para `DIRECCION_CLINICA` (CJ 3.2: «no Medicina»); revoca los permisos de ámbitos Medicina.
  - **Basal, sin código:** CJ eliminó las «aportaciones» a un borrador ajeno; se corrige con una reevaluación que firma quien la crea; se mantiene el permiso `BASELINE_REEVALUATE`. `BASELINE_DRAFT_CONTRIBUTE` queda sin uso.
  - **Script `0040` — traslado:** `ResidentTransferApplicationService`/`SqlResidentTransferRepository`, tabla `traslados_residente`, pantalla `Trasladar` en la ficha administrativa. Mueve a la unidad de destino los **eventos no cerrados** (`TR_ea_transition_guard` lo permite solo con un traslado de la misma transacción) y cancela el borrador de basal (`cancelado_por_traslado_id`). **Pendiente/decisión:** los eventos cerrados no se mueven (la unidad de destino no ve su historial); el traslado por Enfermería con permiso no se construyó.
  - **Script `0041` — baja, reactivación y suspensión:** `ResidentStatusApplicationService`, tablas `bajas_residente` y `suspensiones_residente`, `Bajas`/`DarDeBaja`/`Reactivar`/`Suspender`/`Reanudar`. La baja usa `residentes.estado = INACTIVE`; conserva 5 años (sin purga). La suspensión bloquea la escritura clínica con disparadores `TR_*_suspension_guard` (`RESIDENT_SUSPENDED`). Límite: solo la ficha administrativa marca al suspendido. El siguiente script libre es `0042`.
  - **Sin construir de estas respuestas:** (3.1) el **permiso específico** de Dirección/Coordinación para la finalidad de calidad asistencial y esa cuarta finalidad (ver `pendientes-direccion.md`); el rango por paciente (constantes basales, futuro). **Al desplegar:** borrar en Azure las copias antiguas de la raíz `site/wwwroot/pendientes-cj/aclaraciones-respuestas-cj.html` y `traslado-y-baja-residente.html` si siguieran (el despliegue ya limpia `wwwroot`).
- **Segunda tanda de respuestas de CJ (2026-10-07), hecha casi entera (suite 306, 142 y 386 en verde al 2026-10-08):** CJ devolvió `administracion-ambito-familiares-cargos.respuestas.json` (7/7) y `continuidad-supervision-comunicacion.respuestas.json` (16/16), **archivados el 2026-10-08** en `docs/pendientes-cj/archivados/` (lo que se supuso se pregunta en `docs/pendientes-cj/archivados/confirmaciones-segunda-tanda-cj.html`, ya contestado y archivado el 2026-10-11; al desplegar hay que borrar en Azure las copias antiguas de la raíz de `administracion-ambito-familiares-cargos.html` y `continuidad-supervision-comunicacion.html`, ver el README de esa carpeta). Hecho: (1) **script `0042`**: la baja por fallecimiento cierra sola los eventos abiertos con una anotación de sistema (`cierre_sistema_codigo`); (2) **script `0043`**: el alta registra familiares de contacto prioritario, referentes y tutores legales (`es_referente`, `es_tutor_legal`); (3) **script `0044`**: puede haber **varios contactos urgentes** por residente (`accion_codigo` en `residentes_contacto_urgente`; conjunto vigente = recorrer el historial) y **al menos uno** (obligatorio en el alta por formulario; en la ficha no se puede quitar el último). Detalle y suposiciones en `pendientes-administracion.md`. Después se hicieron el resto de scripts `0045` a `0049` (ver las entradas del 2026-10-08); el siguiente libre es `0050`.
  **Pendiente de esta tanda (decidido por CJ, sin construir):** que la familia lea los comunicados (Portal Familiar); organigrama y calendario de turnos (aplazados, guardados en `pendientes-administracion.md`).
- **Comunicados a la familia: aprobación, publicación y corrección (2026-10-08, rama `comunicados-familia`, scripts `0048` y `0049`; el siguiente libre es `0050`; suite 306, 142 y 386 en verde, también con el usuario limitado en un SQL Server de Docker y `comprobar_aislamiento.sql` en 62 OK):** `FamilyCommunicationSchedule` (dominio: 1 h de margen y 19:00 locales), tabla `comunicaciones_familiares_publicacion_anticipada`, `AdministrationFamilyCommunicationApplicationService`, `SqlFamilyCommunicationPublication`, `Administracion/Comunicados`/`PublicarComunicado` y la pantalla `Enfermeria/Comunicaciones` con hora y estado (la tarjeta del inicio cuenta solo las no publicadas). **Corrección (script `0049`):** `CorrectFamilyCommunication`/`SqlFamilyCommunicationCorrection`, tabla `comunicaciones_familiares_correcciones` (inmutable; el contenido vigente es la última corrección, vía `FamilyCommunicationSql`), `Enfermeria/CorregirComunicacion`; solo Enfermería del ámbito, durante la hora de margen y sin publicación anticipada. Detalle, suposiciones y lo no construido en `pendientes-administracion.md`. **Al desplegar:** el pipeline aplica `0048` y `0049` solo.
- **Hecho el 2026-10-08 (misma rama; scripts `0045` a `0047`):** (g) **Administración principal** (CJ 1.1/1.2 de Administración; script `0047`, el siguiente libre es `0048`; ver `pendientes-administracion.md`); (f) **DIR-11 completo** (ver `pendientes-direccion.md`; script `0046`); (e) **2.2 de Administración** (datos de familiares compartidos): CJ eligió dejarlo como está, así que no hay cambio de código, solo un test que lo fija; (d) **script `0045`**: desvincular familiares con motivo (CJ 2.1 de Administración; ver `pendientes-administracion.md`), el siguiente script libre es `0047`; (c) **derivaciones de Dirección con episodios cerrados y periodo** (CJ 2.1/2.2; ver `pendientes-direccion.md`); (a) **recepción de transferencias**: se quitó la restricción por miembros del equipo (CJ 1.1; bandeja común por unidad y servicio): `TransferReceptionSql` y `CanConfirmReception` eliminados; (b) **«vencidos» → «pendientes»** (CJ 3.1/3.2): en las pantallas y el Manual el seguimiento con la fecha prevista pasada se llama *pendiente* (los identificadores internos siguen llamándose `…Overdue`/`SeguimientoVencido`; las indicaciones médicas siguen diciendo «Vencida»), y el seguimiento **sigue abierto hasta que se cierra el episodio**: `SqlSupervisionDirectory` ya no lo termina al escalar a Medicina ni al activar el protocolo urgente (`EndedAt = cerrado_en`; derivar a Urgencias y el fallecimiento cierran el episodio). Test `Indicadores_SeguimientosConLaFechaVencidaEnElPeriodo_…` ampliado con un seguimiento escalado. **Sin tocar:** el listado de Pendientes de Dirección sigue usando el estado actual del episodio (`HasFollowUp` = en seguimiento), así que un episodio escalado no sale como «Seguimiento pendiente» en esa lista aunque el indicador sí lo cuente; hay que decidirlo con CJ.
  - **Prueba en Azure (2026-10-08, tras el merge del PR #16):** el despliegue aplicó `0039` a `0047` y los seeds. Con `dev-integrado-administracion`, `/Administracion/AmbitoCentro` la muestra como principal; con `dev-plataforma`, `/Plataforma/Centro` lista las Administraciones de los seis centros y se marcó como principal la del «Centro de desarrollo (datos ficticios)» (repetirlo avisa «Ese perfil ya estaba así»). `/PlazosProceso` y `/Direccion/Calidad` cargan con `dev-integrado-direccion`. Queda esa marca en Azure.
- **Dirección, bloque 2 completo con DIR-12 (2026-10-07, rama `despliegue-limpio`, sin script):** tipo de recurso «Informes de derivación firmados» (`REFERRAL_REPORTS`) en `/Baseline/Direction`: lista los informes del residente (con el nombre de quien firmó) y cada PDF se abre con
  `GET /Baseline/InformeDerivacion?residenteId=&eventoId=` (`DownloadDirectionReferralReport`, `SqlReferralReportRepository.DownloadAsDirectionAsync`), que exige la declaración de acceso vigente y **audita cada descarga** en la misma transacción (finalidad y justificación de la declaración). `Derivaciones` enlaza a la consulta.
  Suite: 279, 127 y 346 en verde; comprobado por HTTP con un PDF real. **Del bloque 2 solo falta la 4ª finalidad** (calidad asistencial; espera a CJ). **Siguiente:** el bloque 3 de Dirección (DIR-13 comunicación familiar, DIR-11 calidad de proceso) depende de Portal Familiar y de CJ; mientras tanto, lo desbloqueado está en Administración (desvincular familiar) y en infraestructura (cierre de G1, CI antes del 19/10).
- **Dirección, bloque 2: DIR-14 hecho (2026-10-07, rama `despliegue-limpio`, sin script):** tipo de recurso «Trazabilidad clínica» (`CLINICAL_TRACEABILITY`) en `/Baseline/Direction`: la auditoría de las acciones clínicas del residente (lista cerrada
  `ClinicalTraceability.ActionCodes`), con nombre de la cuenta, perfil, unidad, acción, recurso y hora; `SqlChangeInboxDirectory.Traceability.cs`, `Ports/ClinicalTraceability.cs`, `Models/TrazabilidadModels.cs` (etiquetas) y `_TrazabilidadClinica.cshtml`.
  Suite: 279, 127 y 345 en verde; comprobado por HTTP. **Queda del bloque 2 solo el informe de derivación firmado** (DIR-12, acción condicionada); después el bloque 3 de Dirección. Si añades una acción de auditoría clínica nueva, ponla en `ActionCodes` y en `ClinicalTraceabilityDisplay`.
- **Dirección, bloque 2: DIR-15 hecho (2026-10-07, rama `despliegue-limpio`, sin script):** tipo de recurso «Correcciones y rectificaciones» (`ASSESSMENT_AMENDMENTS`) en `/Baseline/Direction`, con auditoría de una fila por lectura. `ResidentAmendmentHistory.From` (pura, en
  `Ports/ResidentAmendmentHistory.cs`) agrupa la línea temporal del ámbito de Dirección por evento y tipo de valoración (original, correcciones con motivo, rectificaciones) y añade los basales firmados como versiones vinculadas (`ReadHistoryAsync`). Autor = nombre visible de la cuenta + perfil (decisión de Orlando, 2026-10-07; `includeAuthorNames` solo en DIR-15). Suite: 279, 125 y 344 en verde; comprobado por HTTP en local. **Siguiente:** DIR-14 (trazabilidad clínica) y el informe de derivación firmado; después el bloque 3 de Dirección.
- **Dirección, bloque 2: DIR-06 y DIR-07 hechos (2026-10-07, rama `despliegue-limpio`, sin script):** en `/Baseline/Direction`, tipos de recurso nuevos «Línea temporal» (`RESIDENT_TIMELINE`) y «Historial de eventos cerrados» (`CLOSED_EVENTS_HISTORY`),
  con acciones nuevas en la política y la misma declaración de acceso. Se audita el residente (una fila por lectura). `ReadAsClinicalDirectionAsync` devuelve cabeceras vacías para ellos y solo después `ReadDirectionBaseline` pide
  `ListDirectionTimelineAsync` / `ListDirectionClosedEventsAsync` (`SqlChangeInboxDirectory`, con `DirectionScopedEventsFrom`). `DirectionBaselineRead` gana `Timeline` y `ClosedEvents`; `ReadDirectionBaseline` recibe ahora `IChangeInboxDirectory`.
  Parciales `_LineaTemporalLista` y `_EventosCerradosLista` compartidos con Enfermería y Medicina. Detalle y decisiones en `pendientes-direccion.md`. Suite: 276, 125 y 343 en verde; comprobado por HTTP en local con «Residente Prueba DIR-06 (ficticio)» (cuenta
  `test-…` de las pruebas). **Siguiente:** (DIR-15 se hizo después, ver el punto siguiente) DIR-14 (trazabilidad) y el informe de derivación firmado.
- **Dirección, bloque 2: DIR-05 hecho (2026-10-07, rama `despliegue-limpio`, commit `905b4ae`, sin push, sin script):** `/Baseline/Direction` con «Basal vigente» muestra el contenido de la versión vigente (nueve áreas y
  Barthel), no solo cabeceras. `ReadDirectionBaseline` devuelve ahora `DirectionBaselineRead(Headers, Content)`: primero la lectura auditada (`ReadAsClinicalDirectionAsync`, sin cambios) y solo después
  `ReadVersionAsync` de la versión auditada; «Historial de basal» sigue sin contenido. Parcial nuevo `Views/Shared/_ContenidoVersionBasal.cshtml` (extraído de `VersionBasal.cshtml`, que ahora lo usa).
  `tipo_recurso` no tiene `CHECK` de valores: **las pantallas siguientes (DIR-06/07/14/15, informe firmado) no necesitan script por eso**, solo ampliar `ClinicalResourceType`. Suite: 276, 125 y 342 en verde.
  Test `Direccion_LeeElContenidoDelBasalVigenteAuditado_…` (mutación comprobada). Comprobado por HTTP en local con «Residente Prueba Contenido DIR-05 (ficticio)» (basal firmado, unidad integrada).
  **Lección:** si dejas la app local en marcha (`dotnet run`), `ResidApp.Web.exe` bloquea la compilación de los tests funcionales (la suite parece pasar pero no sale la línea de funcionales): páralo antes.
  (DIR-06 y DIR-07 se hicieron después: ver el punto anterior.)
- **El despliegue a Azure limpia `wwwroot` (2026-10-07, rama `despliegue-limpio`):** el paso «Desplegar en Azure Web App» lleva `clean: true`. Antes, OneDeploy añadía ficheros pero no borraba los de
  despliegues anteriores, y los documentos de CJ movidos a `archivados/` siguieron sirviéndose desde su ruta antigua (se borraron a mano por Kudu). `wwwroot` no guarda estado de la app (claves de Data Protection
  en `/home/ASP.NET`, logs en `/home/LogFiles`, datos en SQL). **Límite:** deja un corte de segundos con `wwwroot` a medias; vale para desarrollo (B1, sin slots, datos ficticios). Con usuarios reales,
  repensarlo: `WEBSITE_RUN_FROM_PACKAGE=1` o slots de staging (plan Standard). Kudu tiene el acceso básico desactivado: se entra con un token de Entra ID (`az account get-access-token --resource https://management.azure.com`).
- **Documentos de CJ con clave y tres secciones (2026-10-07, misma rama, sin push):** `/pendientes-cj/` (índice, secciones, documentos y `.respuestas.json`) ya no es público: pide una clave
  (`PendientesCj:Clave` en `appsettings.json`, hoy `CJ123`; sobrescribible con `PendientesCj__Clave`; sin clave configurada no entra nadie). Middleware en `Program.cs` antes de los estáticos
  (páginas → 302 a `/AccesoCj?returnUrl=…`, `.json` → 401), `PendientesCjAccess` (cookie de sesión `residapp_cj_acceso` = SHA-256 de la clave, recalculada en cada petición), `AccesoCjController`
  y su vista; `GET /AccesoCj/Salir`. **Límite:** freno de desarrollo, no autenticación real; el repo de GitHub es público, así que la clave y las respuestas también lo son.
  El índice tiene tres fichas —**Preguntas** (`preguntas.html`), **Pruebas** (`pruebas.html`) y **Archivo** (`archivo.html`)—, listas estáticas que se mantienen a mano. **Regla de archivo:** un documento pasa a
  `archivados/` cuando está contestado **e implementado**; hoy `rangos-referencia-constantes` y `decisiones-direccion-basal-derivacion` (movidos con `git mv`, con una nota «Archivado el 07/10/2026»).
  Lo que queda sin hacer de ellos está en `aclaraciones-respuestas-cj` (Preguntas). Causa del problema original (documentos que no se veían contestados): el csproj solo publicaba `*.html`, así que el
  `.respuestas.json` hermano daba 404; ahora se publican los `.html` y `.respuestas.json` de toda la carpeta, subcarpetas incluidas (los documentos se abren con sus respuestas cargadas, «del repositorio»).
  Las respuestas de cada documento nuevo que llegue se guardan junto a su `.html` (README de `pendientes-cj`: flujo para guardar y para archivar). Tests: `PendientesCjPagesTests` (49, con mutación comprobada).
- **Respuestas de CJ incorporadas (2026-10-07, rama `respuestas-cj-rangos-derivacion-direccion`, sin push):** CJ devolvió `rangos-referencia-constantes.respuestas.json` (12/12) y
  `decisiones-direccion-basal-derivacion.respuestas.json` (7/7), ya versionados. Tres bloques construidos y uno en espera, en commits aparte:
  - **Rangos de constantes (sin script):** botón «Cargar valores sugeridos» (`VitalSignReferenceRanges.Suggested`, `RangosReferenciaController.Index(sugeridos)`: rellena el
    formulario sin guardar; quien tiene `REFERENCE_RANGES_MANAGE` lo revisa y guarda, con historial). Valores de CJ iguales en todos los centros: T 36–37,9; PA 90–139/60–89; FC 60–100;
    FR 12–20; SatO₂ 95–100; glucemia mínimo 70 sin máximo. `Evaluate` no avisa de SatO₂ con oxigenoterapia (**suposición mía**); el campo del flujo de O₂ solo aparece con ella
    (`_ConstantesFormulario`). Aviso visual de temperatura de la Auxiliar (`AuxiliarTemperatureAlerts`: >37 mantener seguimiento, >38 avisar a Enfermería; parcial
    `_AvisoTemperaturaAuxiliar` en la confirmación y en el detalle de Enfermería; no cambia la clasificación). **No construido, sin definir por CJ:** «>37 en varias ocasiones
    seguidas». Rangos por residente (fase 2): CJ dice que no entra en el mínimo producto. El permiso lo sigue concediendo Administración persona a persona.
  - **«Comunicaciones» en el informe de derivación (script `0037`):** columna opcional `informes_derivacion.comunicaciones` (hasta 4000), sección «Comunicaciones» no automática del
    informe, tercer textarea en `_Derivacion.cshtml`. **Suposición mía:** campo propio que escribe el profesional, NO copia el de la valoración ni los contactos del protocolo (DER-04 sigue
    vigente para lo automático). Informes firmados antes: sin él.
  - **Declaración de acceso clínico de Dirección (script `0038`):** finalidades `CONTINUIDAD_ASISTENCIAL`, `INCIDENCIA_RECLAMACION`, `TRAZABILIDAD_DOCUMENTAL` (la cuarta, calidad asistencial, NO:
    Dirección y Coordinación son el mismo perfil `DIRECCION_CLINICA`), justificación siempre (≤300), una declaración por residente y ámbito de 1 hora (`AccesoClinico:DuracionMinutos`) que termina al
    cambiar de residente, ámbito o perfil, al cerrar sesión o al caducar. Tabla `declaraciones_acceso_clinico` (se crea y se termina, con RLS), `eventos_auditoria.justificacion`,
    cookie `residapp_clinical_access` (solo el id), `ClinicalAccessDeclarations`. La declaración se crea dentro de la primera lectura auditada y cada apertura sigue auditando antes de
    entregar. **Solo aplicado a `/Baseline/Direction`** (cabeceras de versión); falta construir sobre el mecanismo DIR-05/06/07/14/15 y el informe firmado (ver `pendientes-direccion.md`, bloque 2).
    Hueco corregido: `CK_audit_direction_read` aceptaba una finalidad nula (un `CHECK` acepta UNKNOWN).
  - **En espera, a la respuesta de CJ:** aportaciones a un borrador de basal ajeno (contradicción: su comentario pide que no firme quien aportó, la opción elegida hace firmar al autor; hoy
    `CK_bv_signer`/`LoadAuthorizedDraftAsync`/`BaselineActivation` exigen que firme el autor; `BASELINE_DRAFT_CONTRIBUTE` no se ofrece; `revision_borrador` nunca se incrementa), la 4ª finalidad,
    las temperaturas seguidas. Todo preguntado en `docs/pendientes-cj/aclaraciones-respuestas-cj.html` (11 preguntas, nuevo, añadido al índice y al README).
  - **Verificado:** suite completa en una base SQL Server desechable de Docker con los 38 scripts desde cero y la app como `residapp_app`: 276, 83 y 341 en verde, dos vueltas, y `declaraciones_acceso_clinico`
    `OK` en `comprobar_aislamiento.sql` (el contenedor ya está eliminado). **Base local `ACER-ORLANDO\ResidApp`:** tenía una fila de auditoría ficticia que impedía recrear `CK_audit_direction_read`; el 2026-10-07 Orlando autorizó recrearla
    desde cero (collation `Modern_Spanish_CI_AS`, `aplicar-scripts.sh` con `APLICAR_SEED=1`: 39 filas en `scripts_aplicados`) y quedó con la restricción y la suite entera en verde (276, 125 y 341).
  - **Intermitencia vista de nuevo:** dos ejecuciones completas de la solución dieron timeouts masivos de SQL de 35–55 s en pruebas ajenas, y las tres siguientes salieron limpias; sin causa explicada (como la anomalía
    ya anotada en el bloque de Administración).
- **Guía de pruebas para CJ (2026-10-06, rama `docs-guia-pruebas-cj`, commit `f7c25a5`; actualizada el 2026-10-08 a 60 pruebas, 20 imprescindibles, versión `2026-10-08`, con las de traslado, baja, suspensión, contactos y familiares, Administración principal, comunicados, calidad de proceso, plazos y Plataforma, y corregidas las que describían lo anterior; el mismo día se revisó la redacción de la guía y de las confirmaciones: glosario, orden de las pruebas, preguntas separadas del resultado esperado y ejemplos breves, sin cambiar preguntas ni opciones):** `docs/pendientes-cj/guia-de-pruebas-cj.html`, publicado en `/pendientes-cj/guia-de-pruebas-cj.html` con el siguiente
  despliegue. 49 pruebas paso a paso en 7 temas (Auxiliar, Enfermería, Medicina, basal y Barthel, Dirección, Administración y familias, aislamiento entre centros) más 3
  preguntas finales; 18 marcadas «Imprescindible». Cada prueba lleva sus pasos, «qué debería ocurrir» y cuatro opciones (A funciona · B con problema · C no funciona ·
  D no probada) con comentario; el fichero de respuestas es `guia-de-pruebas-cj.respuestas.json`, como en los demás documentos (mismo estilo y script; solo cambian
  los textos «Sin probar»/«Probada»). **Datos preparados en Azure dev** (solo por la app): «Residente Prueba CJ Uno (ficticio)» (basal v1 firmado, asignado al
  Auxiliar), «Residente Prueba CJ Dos (ficticio)» (sin basal, asignado al Auxiliar) y el residente de mi recorrido «Prueba Activación RLS Azure (ficticio)» (historial y dos
  informes de derivación) para mirar; «Residente Integrado Uno» queda intacto, sin basal, para el recorrido guiado del Manual. Los pasos se comprobaron por HTTP contra
  Azure dev con la app limitada, y eso corrigió varias suposiciones: Medicina no ve el borrador de basal de Enfermería (si intenta crear otro, recibe «Revisa los datos de
  la operación.», un mensaje poco claro, que la guía deja anotar a CJ); la auditoría de Administración no muestra las consultas auditadas del basal de Dirección; abrir
  a mano un residente de otro centro devuelve a la lista propia. **Siguiente:** que Orlando la publique y se la pase a CJ con las cuentas; cuando CJ devuelva
  `guia-de-pruebas-cj.respuestas.json`, guardarlo en `docs/pendientes-cj/` y convertir cada B o C en una tarea.
- **G1 completo y activo en Azure dev (2026-10-06, PR #7, en `main`; la app responde 200):** la política `seg.pol_centro` cubre las 55 tablas de datos y ahora **se aplica de verdad**:
  la app de Azure dev se conecta como `residapp_app` (usuario contenido con contraseña, sin `db_owner`; lo creó Orlando con
  `database/seguridad/crear_usuario_aplicacion_azure.sql` y comprobó `IS_MEMBER('db_owner') = 0` y `COUNT(*)` de `residentes` = 0 sin ámbito). El despliegue aplica
  scripts y seeds con los secretos de GitHub `AZURE_SQL_ADMIN_USER` y `AZURE_SQL_ADMIN_PASSWORD` (si faltan, con el usuario de la cadena de la app). **Decisión:**
  las credenciales de administrador van en secretos de GitHub y NO en la Web App (las cadenas de la Web App llegan al entorno del proceso de la app: si se
  comprometiera, el atacante tendría el administrador). **Dónde está la cadena:** en la Web App `app-residapp-dev` hay DOS sitios con el mismo valor, la variable de
  entorno `ConnectionStrings__ResidApp` y la cadena de conexión `ResidApp` (tipo `SQLAzure`, que el despliegue lee para sacar servidor y base). Ambas llevan
  `residapp_app` (se compararon por hash: idénticas). Vuelta atrás: poner otra vez el administrador en esa variable.
  **Recorrido por HTTP contra Azure dev con la app limitada** (residente ficticio «Prueba Activación RLS Azure», con 6 eventos cerrados y un basal firmado v1): registrar
  evento y valoración; escalar a Medicina, valoración médica, indicación y su confirmación y realización por Enfermería; cierre con comunicación a la familia; protocolo
  urgente con derivación firmada, intento de llamada y cierre (iniciado por Enfermería y por Medicina); seguimiento de Enfermería (actuación, reprogramación,
  transferencia al Equipo B) y seguimiento médico; las 6 pantallas de Dirección y el detalle de un episodio abierto; Administración y Auxiliar (asignar un residente y
  registrar «sin cambios»; el Auxiliar lo ve solo mientras está asignado); basal completo (borrador, 9 áreas, Barthel, firma y borrador de reevaluación, cancelado).
  Sin ningún fallo atribuible a la seguridad por filas; los únicos rechazos fueron reglas de producto (tras una derivación a Urgencias hay que preparar la
  comunicación a la familia y registrar antes un intento de llamada). **No recorrido:** el alta de centros por Plataforma (cubierta por un test funcional con el usuario
  limitado), quién aprueba las comunicaciones a familias («pendiente de aprobación») y el perfil Familiar. **Pendiente:** que CJ recorra sus flujos en dev; dejar una sola
  de las dos cadenas; el salto de `PLATAFORMA` en las tablas de provisión; y, opcional, autenticación sin contraseña con Microsoft Entra (identidad administrada).
- **G1, RLS por centro, tanda 4b: las tablas de la autorización (2026-10-06, PR #6, en `main` y aplicada en Azure dev; el despliegue devuelve 200):** `0036_rls_autorizacion.sql` amplía
  `seg.pol_centro` a las últimas 4 tablas: `permisos_perfil`, `ambitos_perfil_residente`, `episodios_residente_centro` e `intervalos_ubicacion_residente`. La
  autorización de cada petición las lee, pero siempre con el ámbito activo y su centro: la política no cambia ningún resultado y, sin ámbito activo, no se
  ve ninguna fila y la autorización deniega. Los 73 funcionales existentes ya pasaban con las 4 bajo política; se añadió
  `Administracion_ConcedeYRevocaUnPermisoYAsignaUnResidente_ConLaAppEntera` (concede y revoca un permiso, asigna y retira un residente de un Auxiliar, y el
  Auxiliar lo ve solo mientras está asignado). En Docker, con la app como `residapp_app`: 264, 74 y 330 en verde y las 55 tablas cubiertas `OK` en
  `comprobar_aislamiento.sql`. **Con esta tanda quedan cubiertas todas las tablas de datos** salvo las de provisión (`unidades`, `ambitos_perfil_unidad`,
  `eventos_auditoria`) y las de diseño; `CoberturaRlsTests` ya solo lista esas. Siguiente script: `0037`. **Lo que queda de G1** es activarla de verdad en Azure
  (usuario limitado, segunda cadena de administrador) y el salto de `PLATAFORMA`.
- **G1, RLS por centro, tanda 4a: cierres, idempotencia y familias (2026-10-06, PR #5, en `main` y aplicada en Azure dev; el despliegue devuelve 200):** `0035_rls_cierres_y_familias.sql` amplía
  `seg.pol_centro` a 8 tablas: los tres `cierres_cotidianos_*`, `operaciones_idempotencia`, `residentes_contacto_urgente`, `residentes_familiares`,
  `residentes_identidad_correcciones` y `familiares_autorizaciones_cambios`. Ninguna interviene en la autorización de cada petición (esas cuatro van en la
  tanda 4b). Medido ejecutando solo los funcionales en Docker: solo `operaciones_idempotencia` recibía filas por la app real; las otras 7 no. Se añadieron
  `Administracion_CorrigeIdentidadYGestionaFamiliares_ConLaAppEntera` (corregir identidad, añadir familiar, abrir y activar su autorización, designarlo
  contacto urgente) y `Auxiliar_RegistraSinCambiosYUnCambioConOpciones_ConLaAppEntera` (cierre sin cambios y cambio con área de texto y opción rápida).
  En Docker, con la app como `residapp_app`: 264, 73 y 330 en verde y las 51 tablas cubiertas `OK` en `comprobar_aislamiento.sql`. **Las 4 últimas** (las de la autorización, lo que sigue) van en la tanda 4b.
- **G1, RLS por centro, tanda 3: estructura y planificación (2026-10-06, PR #4, en `main` y aplicada en Azure dev; el despliegue devuelve 200):** `0034_rls_estructura.sql` amplía
  `seg.pol_centro` a 10 tablas: `edificios`, `plantas`, `habitaciones`, `plazas`, `equipos`, `equipos_miembros`, `turnos_catalogo`, `planificacion_turnos`,
  `rangos_referencia_constantes` y su historial (las unidades no entran: las escribe `PLATAFORMA`). Con la política activa, los funcionales existentes
  ya creaban filas por la app real en 7 de las 10 (se midió ejecutando solo los funcionales en Docker antes de nada). Para las otras 3 se amplió el test
  de turnos y equipos (añade un miembro de Enfermería a un equipo) y se añadió `RangosReferencia_UnaMedicaConPermisoLosGuarda_ConLaAppEntera` (el
  permiso `REFERENCE_RANGES_MANAGE` solo lo admiten `MEDICINA` y `DIRECCION_CLINICA`: un trigger lo impone). En Docker, con la app como `residapp_app`:
  264, 71 y 330 en verde, dos vueltas, y las 43 tablas cubiertas `OK` en `comprobar_aislamiento.sql`. **Con esta tanda, todo lo estructural queda probado
  por la app entera.**
- **G1, RLS por centro, tanda 2: flujo clínico (2026-10-06, PR #3 mergeado: `0033` en `main` y aplicado a Azure de desarrollo; el paso de aislamiento del CI pasó también en Actions):** `0033_rls_flujo_clinico.sql` amplía
  `seg.pol_centro` a 20 tablas: `eventos_clinicos`, `eventos_contexto`, `escalados_medicina`, las valoraciones de Enfermería y de Medicina con sus versiones y
  correcciones, `valoraciones_rectificaciones`, los dos seguimientos y sus acciones, `indicaciones_medicas`, `protocolos_urgentes` y sus registros,
  `informes_derivacion`, `intentos_llamada_familia` y `comunicaciones_familiares`. Varias consultas usan `WITH (FORCESEEK)` sobre estas tablas: se
  vigiló que no chocaran con el predicado (no chocan). **Comprobación nueva sobre datos reales:** `database/seguridad/comprobar_aislamiento.sql`
  toma, para cada tabla cubierta, el centro con más filas, entra con un ámbito de ese centro como usuario limitado (`EXECUTE AS`) y exige ver
  exactamente sus filas y ninguna ajena; falla con `exit 1` si no (probado quitando un filtro). Es un paso del CI **después de las pruebas**
  («Comprobar el aislamiento por centro con los datos de las pruebas»), así que no depende del orden de los tests. Prueba funcional nueva
  `Enfermeria_RegistraUnEventoYGuardaSuValoracion_ConLaAppEntera` (registrar evento, empezar y guardar la valoración por la app real). En Docker, con
  la app como `residapp_app`: 264, 70 y 330 en verde y las 33 tablas `OK`. Siguen sin prueba por la app entera: escalar, cerrar con comunicación,
  seguimientos, protocolo urgente, derivación y Medicina (solo por repositorio y por la comprobación de aislamiento).
- **G1, RLS por centro, tanda 1: basal (2026-10-06, PR #2 mergeado: `0032` en `main` y aplicado a Azure de desarrollo):** `0032_rls_basal.sql` amplía `seg.pol_centro` a
  las diez tablas del basal (`basales_borrador` y sus áreas y Barthel, `basales_version` y sus áreas y Barthel, `basales_vigentes_residente`,
  `basales_sustituciones`). Aplicado en la BD local y en un SQL Server de Docker (ya eliminado). Pruebas nuevas: `RlsCentroTests.Las_tablas_de_basal_solo_se_ven_en_su_centro`
  (siembra dos centros con un basal firmado y otro reevaluado: las 10 tablas con filas propias y ninguna ajena; falla si se quita un filtro) y
  `CoberturaRlsTests.Toda_tabla_cubierta_tiene_filtro_y_bloqueo...` (toda tabla cubierta lleva FILTER y BLOCK al insertar y al actualizar), y
  `ResidentsFlowTests.EnfermeriaBasal_CreaYCancelaUnBorrador_ConLaAppEntera` (crea y cancela un borrador por la app real). **Hallazgo:** antes de
  esa prueba funcional, los 68 funcionales no creaban ni una fila de basal, así que «la app con usuario limitado» no ejercitaba nada de esta tanda;
  la prueba de política (integración, con `EXECUTE AS`) y la funcional se complementan. Suite completa con la app como `residapp_app` en Docker,
  dos vueltas en verde (264, 69, 330). Los flujos de firma y reevaluación por la app entera siguen sin prueba funcional (solo por repositorio).
- **G1, RLS por centro, primera tanda (2026-10-06, PR #1 mergeado: `0030` y `0031` están en `main` y se aplicaron a Azure de desarrollo):** script
  `0030_rls_centro.sql` (esquema `seg`,
  `seg.fn_centro_del_ambito`, `SECURITY POLICY seg.pol_centro` sobre `residentes`, `eventos_asistenciales` y `familiares`).
  `ITenantContext` (Application) + `RequestTenantContext` (Web) + `SqlConnectionFactory(cadena, tenant = null)`: cada conexión fija
  `sujeto_externo` y `ambito_perfil_id` en `SESSION_CONTEXT` (`read_only`), con o sin ámbito. `Program.cs` registra la fábrica como scoped.
  `RlsCentroTests`: 10 pruebas; suite completa en verde (264 unitarias, 324 de integración, 68 funcionales).
  - **El predicado se aplica a todos, también a `dbo`/sysadmin; deja pasar a `db_owner`.** Por eso la política queda latente en cualquier
    entorno cuya aplicación use un usuario `db_owner`. En Azure dev ya no es así (la app usa `residapp_app`, ver el primer punto de «Dónde estamos»);
    en un entorno nuevo hay que crear el usuario limitado antes de dar la RLS por activa.
  - **Lección:** una conexión con `EXECUTE AS USER` que vuelve al pool sin `REVERT` falla al restablecerse («session is in the kill state»):
    las pruebas abren esas conexiones con `Pooling=false`. La instancia local es solo Windows (no admite logins SQL), así que las pruebas
    suplantan con `EXECUTE AS USER ... WITHOUT LOGIN` en lugar de usar una cadena aparte.
  - **La app entera con un usuario limitado (2026-10-06):** `database/seguridad/crear_usuario_aplicacion.sql` crea el login `residapp_app`
    (`db_datareader` + `db_datawriter`, sin `db_owner`). `WebAppFactory` acepta `RESIDAPP_TEST_APP_CONNECTION_STRING`: la app se conecta con
    ese usuario y el sembrado y las comprobaciones siguen con `TestConnectionString`. Verificado en un SQL Server de Docker (la misma
    imagen que el CI, puerto 14333, ya eliminado): los 68 funcionales pasan con la app como `residapp_app` (se vieron sesiones de ese
    login durante la ejecución), la suite completa pasa (264, 68, 325) y, sin contexto, `residapp_app` ve 0 de 6 residentes. El CI tiene
    un paso nuevo («Crear usuario limitado de la aplicación») y ejecuta toda la suite así; en GitHub Actions pasó (PR #1 y push a `main`), y el despliegue aplicó `0030` y `0031` a Azure de desarrollo (la
    app responde 200). **No se hizo antes el ensayo con `ROLLBACK` de `0031` contra Azure**; salió bien igualmente.
    La instancia local `ACER-ORLANDO` es solo Windows: para repetirlo en local, levanta un contenedor (`docker run ... -p 14333:1433`),
    aplica los scripts con `aplicar-scripts.sh`, crea el login y exporta las dos variables.
  - **Cobertura de la política (`CoberturaRlsTests`, 2026-10-06):** toda tabla de `dbo` está bajo `seg.pol_centro` o figura en la lista
    `Excepciones` del test con su motivo; una tabla nueva sin decidir, o una excepción ya cubierta, hacen fallar el test. Hoy: 43 cubiertas
    (3 de `0030`, 10 del basal de `0032`, 20 del flujo clínico de `0033` y 10 de estructura de `0034`), 5 por diseño (`centros`, `cuentas`, `ambitos_perfil`, `scripts_aplicados`, `sysdiagrams`), 3 de provisión
    (`unidades`, `ambitos_perfil_unidad`, `eventos_auditoria`) y 12 pendientes, **todas con `centro_id`** (las cifras anteriores a la tanda 2, 53 y 43, iban una por encima: eran 52 y 42). Al cubrir una tabla, quítala de `Excepciones`.
  - **`0031_centro_id_en_tablas_hijas.sql` (2026-10-06, en `main` y en Azure de desarrollo):** diez hijas sin `centro_id` (`valoraciones_enfermeria_versiones` y
    `_correcciones`, `valoraciones_medicas_versiones` y `_correcciones`, `seguimiento_acciones`, `seguimiento_medico_acciones`,
    `protocolo_urgente_registros`, `intentos_llamada_familia`, `cierres_cotidianos_cambio_area_opciones`, `operaciones_idempotencia`) llevan
    ahora `centro_id NOT NULL` con clave foránea compuesta con su padre (índices únicos nuevos `UX_*_centro*` en los padres; en
    `operaciones_idempotencia`, FK simple a `centros`). Una sola transacción; para el relleno desactiva y reactiva los triggers de
    inmutabilidad. En `operaciones_idempotencia` el centro sale del recurso guardado; lo que no se resuelve queda en el centro reservado
    «Plataforma» (en la BD local: 0 de 86.324). Todos los `INSERT` de la app y de los tests pasan ya `centro_id`: **una escritura nueva en
    esas tablas sin `centro_id` falla** (NOT NULL). Verificado: ensayo con `ROLLBACK` sobre la BD local, aplicación real, 32 scripts desde
    cero en Docker y la suite completa con la app como `residapp_app` (264, 68, 328).
  - **Lección (`EXECUTE AS` en tests):** con `MultipleActiveResultSets=true` falla de forma intermitente («a simultaneous batch has called
    it») cuando corren a la vez varios proyectos de test; las conexiones suplantadas de `RlsCentroTests` van sin pool y sin MARS. La
    fábrica de producción usa `sp_set_session_context`, que no tiene ese límite.
  - **Pendiente de G1:** ver el primer punto de «Dónde estamos» (la activación en Azure ya está hecha; quedan la prueba con CJ, una sola cadena de conexión y el salto de `PLATAFORMA`).
- **Propuesta de multi-centro y grupos empresariales (2026-10-06, solo documentación):** ADR 0008 en estado propuesto. El esquema ya es
  multi-centro por filas (`centro_id` en todo, claves compuestas); no hay `ITenantProvider` y `SqlConnectionFactory` tiene una sola
  cadena. Decide: sin claims de centro, sin borrado lógico genérico, grupo como `grupos` + `centros.grupo_id NULL` (sin `grupo_id` en
  tablas clínicas), RLS por centro por fases con salto de `PLATAFORMA` solo en las tablas de provisión (nunca en las clínicas, por el
  ADR 0006) y separación futura a otra base por grupo, con catálogo aparte. Incrementos G1–G3 en el ADR; el siguiente número de script
  libre es `0035`. No se ha tocado `src/` ni `database/`. Decidido por el usuario el mismo día: un administrador de grupo **no** ve datos
  clínicos de varias residencias, solo estructura y agregados.
- **Propuesta de proveedor de identidad (2026-10-04, solo documentación):** ADR 0007 en estado propuesto. Microsoft Entra External
  ID con tenant en la UE; Auth0 UE como respaldo si se exige TOTP o se rechaza el código por correo como segundo factor. El alta crea
  el usuario por Graph y guarda `<tenant>|<oid>` como `sujeto_externo`. Las puertas de aceptación y los incrementos I1–I5 están en el
  ADR. No se ha creado ningún tenant ni se ha tocado `src/`; Portal Familiar y ADM-30 siguen esperando a que se acepte.
- **Rediseño visual, cierre (2026-10-04, sin script):** Indicadores con una tarjeta por unidad (o por mes) en el móvil y una barra gris
  (`.barra-proporcion`, `aria-hidden`, sin color semafórico) en cada cifra «n de m»; al imprimir salen las cinco tablas y ni tarjetas ni
  barras a cualquier ancho (comprobado con `emulateMediaType("print")`). Fechas de Planificación, Serie y PlanificarTurno con solo la
  primera letra en mayúscula (`text-capitalize` ponía «4 De Noviembre De 2026»). `.chip-par` también en `_ComunicacionFamiliarFormulario`
  (`.chip-par > .btn` crece para seguir a todo el ancho en `d-grid`). Serie de planificación revisada a 390 px: no se desborda.
- **Rediseño visual, tercera tanda (2026-10-04, sin script):** Confirmar cambio ya no vacía el formulario en sus dos rutas de error (sin
  aviso directo con el navegador saltado, o datos manipulados): vuelve a pintar la confirmación o el formulario con lo enviado y el mismo
  `OperacionId` (probado con Chrome sin interfaz, sin escribir nada). Índice del Manual plegado en el móvil (`collapse d-lg-block`).
  `.estado-vacio` en 28 listas y bandejas a página completa. Tarjetas o `list-group` en el móvil para Usuarios, Usuario, ficha del
  residente (familiares y ubicación), Estructura, Edificios, Habitaciones, Turnos, Equipos, Planificación, Auditoría, Plataforma y el
  historial de Rangos; el formulario de Rangos pasa de tabla a rejilla (los campos no se pueden duplicar). En Registrar cambio, cada chip va
  en `.chip-par` y la página reserva `scroll-padding-bottom`: tabulando, ningún elemento con foco queda bajo la barra fija (antes, 8).
  `manifest.json`: `background_color` con el fondo nuevo.
- **Rediseño visual, segunda tanda (2026-10-04, sin script):** fichas del residente (Enfermería y Medicina) con acciones arriba y dos
  columnas en escritorio; acción principal arriba en DetalleCambio y Escalado; parcial `_ResumenBasal` (Barthel con barra neutra);
  tarjetas en móvil para las bandejas de Enfermería, el Historial, Dirección (inicio «Por unidad», Pendientes, Derivaciones) y los residentes de
  Administración; Barthel con fieldset y total en vivo en una barra fija (solo la suma, sin bandas); Indicadores con «Cómo se cuenta» al final;
  sin «(s)» en ninguna vista; `role="status"`/`role="alert"` en los 48 mensajes de resultado; índice lateral fijo en el Manual;
  `manifest.json` con `orientation: any`; `sw.js` (`residapp-shell-v2`) en red primero y, sin red, el shell ignorando `?v=`: la página sin
  conexión sale con el tema (comprobado parando el servidor). Para probar Barthel se creó y se canceló un borrador en la BD local
  («Prueba Enfermería nueva (ficticia)»).
- **Indicador de envío (2026-10-20):** `wwwroot/js/site.js` pone un spinner en el botón de todo `submit` válido, anuncia «Enviando…»
  (`role="status"`) y descarta el segundo envío; `data-sin-carga` lo excluye. No usa `disabled` (el `Accion=Firmar` del botón debe viajar).
  `sw.js` sube a `residapp-shell-v3`. Pendiente (fase 2, solo si CJ lo pide): indicador para navegación GET lenta (Indicadores, Calidad)
  excluyendo los enlaces de descarga de PDF. Sin pruebas JS en el repo: verificado a mano.
- **Service worker y Portal Familiar (2026-10-20):** `_LayoutFamilia` registra ahora `/sw.js` como `_Layout`. `/Home/Offline` usaba `_Layout`
  y el service worker la guarda al instalarse con las cookies del momento (perfil, centro e «Identidad» de quien estuviera dentro); ahora usa
  `_LayoutOffline`, sin cookies (`PaginaSinConexionTests`). Lección: todo lo que precachea el service worker se renderiza sin sesión.
  `residapp-shell-v3` (aún sin desplegar) fuerza la reinstalación que recoge la página neutra.
- **Dependencias vulnerables (2026-10-20):** `ResidApp.Infrastructure.csproj` fija `System.Security.Cryptography.Pkcs` 10.0.13 (SqlClient y PDFsharp
  piden 9.0.13, con GHSA-846f-f764-76mx y GHSA-2fmv-rv2c-59x7; subir esos paquetes no lo corrige). GitHub no ve las dependencias NuGet
  transitivas: se comprueban con `dotnet list package --vulnerable --include-transitive`. Las 7 alertas de Dependabot eran de `next` en
  `docs/legado-cloudflare` (congelado, sin compilar ni desplegar) y se descartaron en GitHub como «no se usa»; si salen otras, igual.
- **Rediseño visual, primera tanda (2026-10-03, sin script):** revisión crítica de `docs/bocetos-pantallas/residapp-recomendaciones-diseno.md`
  (documento del usuario, sin versionar: se adoptó una parte, se adaptó otra y se descartó el resto con motivo; ver los commits). Cambios:
  - **Guía vinculante actualizada** (`guia-diseno-sistema-visual.md`): neutros y enlaces que cumplen AA (enlaces `#0A58CA`, secundario `#5B6676`
    sobre `#F5F7FA`), tokens del modo oscuro, color según el valor (un 0 nunca en rojo), etiquetas suaves, franja lateral, 48 px en táctil
    (`any-pointer: coarse`) y prohibido `bg-white`/`text-dark`/`btn-dark` en las vistas.
  - **Tema y modo oscuro:** `residapp-theme.css` asigna los tokens a las variables de Bootstrap 5.3; `wwwroot/js/tema.js` (en `<head>`) pone
    `data-bs-theme` según el dispositivo o la elección Claro/Oscuro/Automático (en `localStorage`, por dispositivo) y fuerza el claro al imprimir.
    Texto base de 16 px también en móvil (la plantilla bajaba a 14 px).
  - **Cabecera:** «Mi panel», selector de tema y menú de usuario (identidad, ámbito, Cambiar ámbito, Cambiar de identidad, Salir).
  - **Paneles** de Enfermería, Medicina, Dirección y Administración con cifra grande, plural real y color según el valor; **listas de
    residentes** con la tarjeta entera pulsable (`stretched-link`).
  - **Registro del Auxiliar:** tres salidas juntas y con el mismo peso; Registrar cambio con chips (`btn-check`), «Añadir nota» (`<details>`)
    y barra fija; **«Volver a editar» ya no pierde los datos** (`AuxiliarController.RegistrarCambio` con `editar=true` devuelve el formulario
    relleno, mismo `OperacionId`).
  - Verificado con capturas de Chrome sin interfaz en claro y oscuro, escritorio y 390 px (ver la lección «Capturas con sesión»), y la prueba
    de «Volver a editar» de punta a punta. Suite en verde (264, 315 y 68). Medido: Registrar cambio pasa de 2.197 a 2.063 px en escritorio,
    pero **de 2.208 a 2.785 px a 390 px** por los chips de 48 px; la barra fija mantiene «Continuar» a mano.
- **Documentos de CJ publicados en la web (2026-10-03):** `docs/pendientes-cj/*.html` (incluido el nuevo `index.html`, lista estática a mano) se sirven en `/pendientes-cj/` con la propia app: `ResidApp.Web.csproj` los incluye como `Content` enlazado (fuente única, sin copia en `wwwroot`) y `Program.cs` los expone con `UseStaticFiles` + `PhysicalFileProvider` sobre `AppContext.BaseDirectory/pendientes-cj`. Enlace «Documentos de CJ» en el pie del `_Layout`. Públicos (sin datos de residentes; las respuestas no pasan por el servidor). **Cambio de canal:** CJ ya no sube el JSON a GitHub; lo descarga con «Guardar respuestas» y se lo envía a Orlando por correo o WhatsApp (textos de los 5 HTML y `README.md` ajustados; se quitó `UPLOAD` del script). Test `PendientesCjPagesTests`. Al añadir un documento nuevo, hay que añadirlo también a `index.html`. Aviso sin corregir: `ProfessionalAccountScreensTests.Plataforma_CreaUnCentro_ElInicioSoloLaOfreceAEsePerfil_YOtroPerfilNoEntra` falla también sin estos cambios.
- **Documentos de CJ con respuestas guardadas (2026-10-02):** los `.html` de `docs/pendientes-cj/` ya no se rellenan editando «por definir»: CJ
  los abre en el navegador, responde con opciones, texto y tablas (borrador en `localStorage`), pulsa «Guardar respuestas» y sube a la misma
  carpeta `<documento>.respuestas.json`, que es lo que lee ingeniería (formato en el `README.md` de la carpeta; lleva el texto de cada pregunta
  y de la opción elegida). Los 3 documentos que había se convirtieron con las mismas preguntas y opciones, y hay 2 nuevos:
  `archivados/administracion-ambito-familiares-cargos.html` (unidades sin Administración, desvincular y compartir familiares, organigrama) y
  `archivados/continuidad-supervision-comunicacion.html` (confirmar lo construido sin documento: recepción por miembros, derivaciones de Dirección,
  seguimientos vencidos; plazos de hitos de DIR-11 y quién aprueba la comunicación familiar). Estilo y script iguales en todos (inline, sin
  dependencias salvo las fuentes); probados con Chrome sin interfaz: responder, recargar, guardar, abrir el fichero (incluido uno de otro
  documento o roto), carga automática del JSON hermano por http y aviso de versión distinta. Los `pendientes-*.md` enlazan cada hueco a su tema.
- **Derivaciones en solo lectura para Dirección, DIR-12 (2026-10-02, sin script):** `/Direccion/Derivaciones` (tarjeta nueva del inicio). `ISupervisionDirectory.ListReferralsAsync`
  (`SqlSupervisionDirectory`, reutiliza `ScopedEventsFrom/Where`) devuelve `SupervisionReferral`: episodios **abiertos** con protocolo urgente, con el protocolo
  (perfil y hora), el informe firmado si lo hay (perfil y hora) y el número y la hora de la última llamada a la familia; `DireccionApplicationService.ListReferralsAsync`
  exige el ámbito de Dirección. Sin texto clínico, contacto, resultado de llamadas ni contenido del informe (test por reflexión). **Suposiciones mías, sin confirmar:**
  solo abiertos, sin permiso ni auditoría. **Sigue bloqueado por CJ:** leer el informe firmado (permiso clínico y finalidades) y DIR-13 (comunicación familiar,
  que espera a Portal Familiar). Test `Derivaciones_MuestranElEstadoDelProcesoDeLosProtocolosAbiertosDelAmbito_SinContenido`. Suite en verde (264, 315 y 61).
  Curl en local: Dirección ve «1 en curso, 1 firmada, 1 con llamadas» (cuadra con SQL); Enfermería, Medicina, Auxiliar y Administración reciben «No se puede acceder».
- **Seguimientos con la fecha vencida por periodo en los indicadores de Dirección (2026-10-02, sin script):** `SupervisionIndicatorCounts` gana
  `FollowUpsOpen` y `FollowUpsOverdue` (con valor por defecto 0), `SupervisionIndicatorFacts` gana `FollowUps`, y `SqlSupervisionDirectory.ListIndicatorFactsAsync`
  los lee (seguimientos de Enfermería y de Medicina con sus reprogramaciones, una fila por reprogramación agrupada en C#). La regla pura es
  `SupervisionIndicatorRules.WasOpen/WasOverdue` (días en la zona del centro; el plan vigente es el de las reprogramaciones registradas un día anterior; el día
  de terminar cuenta como abierto). Columna nueva en «Continuidad entre turnos» y en la evolución mensual, con su explicación en «Cómo se cuenta».
  **Deducción mía, sin confirmar** (límites en `pendientes-direccion.md`: un seguimiento termina por cierre, escalado o protocolo; otras vías no se guardan).
  Tests: 12 unitarios nuevos y 1 de integración (falla si se pierde el cierre). Suite en verde (264, 314 y 61). Control en local: «2 de 5» seguimientos de
  la unidad integrada en el periodo 01/08–02/10, y 5 con una consulta `sqlcmd` independiente.
- **Vincular un familiar que ya existe a otro residente (2026-10-02, sin script):** `VincularFamiliar` (GET/POST, vista nueva, enlace en la ficha
  administrativa). Los candidatos son los familiares ya vinculados a algún residente del ámbito y no a este (`LinkableFamilySelect`, reutiliza
  `ScopedResidentsSelect`); `SqlResidentFamilyRepository.LinkExistingAsync` repite el filtro dentro de la transacción (idempotente por el `OperacionId`
  del formulario, que es el id del vínculo), `FamilyMember.ValidateRelationship`, auditoría `FAMILY_MEMBER_LINK` (en la lista cerrada y en
  `AuditActionDisplay`). `ResidentFamilyMember.OtherResidentLinks` y el aviso de la pantalla de edición («vinculado también a N residentes: el cambio vale
  para todos»). **Suposiciones mías, sin confirmar** (detalle en `pendientes-administracion.md`): ámbito de los candidatos, relación por residente,
  autorización por vínculo. Tests: `Vincular_UnFamiliarDeOtroResidenteDelAmbito_…` y `Vincular_NoOfrece…` (el segundo falla sin el filtro de ámbito) y los
  unitarios de la relación. Suite en verde (252, 313 y 61). Curl en local: «Familiar concurrencia prueba» vinculado a «Residente Integrado Dos
  (ficticio)» como «Sobrina»; el reenvío no duplica (2 vínculos); la edición avisa. **No hecho:** desvincular. **ADM-30 «Mi cuenta» no se ha
  construido:** pide credenciales, segundo factor y sesión, que dependen del proveedor de identidad sin decidir; el cambio de perfil ya existe
  (Cambiar ámbito).
- **Token de concurrencia en la edición de familiares (2026-10-02, sin script):** la pantalla `EditarFamiliar` lleva `Form.Version`, la huella SHA-256 de
  los datos que enseñó (`FamilyMemberData.Version`, que une nombre, relación, teléfono y correo con un separador). `SqlResidentFamilyRepository.UpdateAsync`
  la compara con los datos actuales, ya con el vínculo bloqueado (`UPDLOCK`), y si no coinciden lanza `FAMILY_MEMBER_CONFLICT` (en `ConflictPattern`). Se
  comprueba **después** del acceso (familiar ajeno sigue siendo acceso denegado) y **antes** de «sin cambios». El parámetro es obligatorio en
  `UpdateFamilyMemberCommand.VersionEsperada` y en el puerto: un envío sin versión es conflicto. En el conflicto el controlador (`EditForm`) vuelve a
  pintar el formulario con los datos actuales y un aviso, tras `ModelState.Clear()` para que los campos no conserven lo escrito. Límite conocido: A→B→A
  deja la misma huella y no se detecta. Tests: `Editar_ConUnaVersionObsoleta_EsConflicto_…` (falla sin la comprobación), `Version_EsEstableYCambiaConCualquierDato`
  y el de edición adaptado. Suite en verde (246, 310 y 61). Curl en local con dos pestañas sobre «Familiar concurrencia prueba»: la primera guarda, la segunda
  recibe el aviso con el teléfono de la primera, y reintentar guarda. Queda ese familiar de prueba en la base local.
- **Recepción de transferencias limitada a los miembros del equipo (2026-10-02) — REVERTIDA el 2026-10-08:** CJ (1.1 de `continuidad-supervision-comunicacion`) pidió una bandeja común por unidad y servicio, así que se quitó `TransferReceptionSql`, `FollowUpActionSummary.CanConfirmReception` y el aviso de las pantallas: la recepción la confirma cualquiera de Enfermería/Medicina de la unidad. Lo que sigue es el historial de aquella decisión.
  (`equipo_entrante_id`, script `0028`) solo la confirma un miembro vigente de ese equipo (`equipos_miembros`, `0027`). **Suposición mía, no
  confirmada con el usuario ni con CJ:** si el equipo no tiene miembros vigentes, o la transferencia es anterior a `0028` (sin equipo), la
  confirma cualquiera del ámbito como hasta ahora, para que ninguna transferencia quede sin salida. Regla única en `TransferReceptionSql.CanReceive`
  (fragmento SQL), usada por `SqlNursingAssessmentRepository` y `SqlMedicalAssessmentRepository` (el `INSERT` de la recepción) y por
  `SqlChangeInboxDirectory` (`FollowUpActionSummary.CanConfirmReception`). Las pantallas de seguimiento de Enfermería y Medicina esconden el botón y dicen
  «Solo los miembros del equipo «X» pueden confirmar la recepción.»; un envío forzado recibe el conflicto genérico de siempre
  (`FOLLOW_UP_TRANSFER_NOT_PENDING`, sin código nuevo). Tests: `Recepcion_ConMiembrosEnElEquipo_SoloLaConfirmanSusMiembros` (Enfermería),
  `SeguimientoMedico_Recepcion_ConMiembrosEnElEquipo_…` y `Recepcion_SiElEquipoNoTieneMiembrosVigentes_…`; fallan sin la regla. Suite en verde (245, 309 y 61).
  Pantalla comprobada en local insertando por SQL dos transferencias en el seguimiento abierto de la unidad integrada («Equipo I2 prueba», del que
  `dev-integrado-enfermeria` no es miembro, muestra el aviso; «Equipo I1 prueba», del que sí, el botón). Quedan esas dos filas de prueba.
- **Habitación y plaza en Enfermería, Medicina y Auxiliar (2026-10-02, sin script ni push):** `ScopeResidentSummary` y `AssignedResidentSummary` llevan
  `RoomName` y `PlaceName` (opcionales, al final) y `LocationLabel` («Unidad · Habitación · Plaza», vía `ResidentLocationLabel.Format`, omitiendo lo que
  no consta). `SqlEnfermeriaResidentDirectory` (que sirve también a Medicina y al desplegable de Dirección, que no lo muestra) y
  `SqlAssignedResidentDirectory` hacen `LEFT JOIN` a `habitaciones` y `plazas` desde la ubicación vigente. Vistas: `Residentes` y `Residente` de
  Enfermería y Medicina, y `Index` y `Registro` de Auxiliar. Manual al día (sale de «Próximamente»). Tests: uno de integración por directorio y
  `ResidentLocationLabelTests`. Suite en verde (245, 306 y 61). Curl en local con «Habitacion 8 prueba · Cama Y prueba» en la unidad del escenario
  integrado: lista y ficha de Enfermería y Medicina lo muestran; Auxiliar no se probó con curl (hay que asignarle el residente) y lo cubre el test de
  integración. Quedan datos de prueba en la base local (habitaciones 7 y 8, residentes «Residente ubicacion … (ficticio)»).
- **Login de desarrollo sin callejón sin salida (2026-10-02, commit `ff61368`, sin push):** `DevAuth/Login` rechaza un usuario sin cuenta activa o sin ningún
  ámbito activo (`IProfileScopeDirectoryProvider.ListActiveAsync` vacío) y no escribe la cookie; antes `dev-kk` dejaba la sesión atrapada en
  `ProfileScope/Select`, sin enlaces, y «Inicio» redirigía de nuevo allí. Un login rechazado vuelve a pintar el formulario con la identidad previa
  (y su botón «Salir»). `Select.cshtml`: con 0 ámbitos ofrece «Cambiar de identidad» y «Salir»; si falla la carga de ámbitos (p. ej. BD caída)
  muestra el error y «Reintentar» en vez de «no tiene ningún ámbito»; el texto «varios ámbitos» solo sale con más de uno. Manual actualizado.
  4 tests funcionales nuevos (fallan sin el cambio); suite en verde (240, 304 y 61).
- **Consulta auditada de basal de Dirección y firma suelta (2026-10-02, sin push):** `/Baseline/Direction` toma el ámbito y el centro
  del ámbito activo y el residente se elige en un desplegable (`DireccionApplicationService.ListResidentsAsync`, que reutiliza
  `SqlEnfermeriaResidentDirectory` admitiendo ahora `DIRECCION_CLINICA`). Se retiró la pantalla suelta `/Baseline/Sign` y su tarjeta del
  Inicio (decisión del usuario): el GET redirige a Residentes del perfil, y el POST queda solo para `EnfermeriaBasal/Confirmar`, al que
  vuelve con el mensaje si la firma falla. Antes, los identificadores de los dos formularios se prerrellenaban con `Guid.Empty` y daban
  un 500 (commit `77048c5`). Un residente sin basal firmado ya no se deniega: el resultado dice «Este residente todavía no tiene
  ningún basal firmado.» (ver la lección de abajo). Verificado con la suite (240, 304 y 57) y en local con `dev-integrado-direccion`
  («Residente Integrado Uno» muestra su versión y se audita; «Prueba selector unidad», sin basal, da el mensaje y no deja auditoría).
- **Manual de usuario revisado (2026-10-02):** «Próximamente» al día con los `pendientes-*.md` (fuera lo ya hecho: turnos recurrentes y
  auditoría; añadidos cambio de habitación o plaza, habitación y plaza en las vistas clínicas, aportación a un borrador ajeno, organigrama,
  rangos por residente y la tarjeta «Comunicaciones» de Medicina). También se actualizaron la introducción por perfil, las cuentas de prueba,
  el título de Estructura, la lista de la auditoría y la consulta del basal de Dirección. Al cerrar cada bloque, revisa «Próximamente».
- **Administración, edificios, plantas, habitaciones y plazas (historia 2; script `0029`; sin cambios en el seed):** hecho el 2026-10-02 en `main`
  **sin push** (Azure en `0028`). Dos fases, dos commits (`449f02b` y la fase 2) más `f88a5fa` (corrección de los tests, ver abajo). Detalle, decisiones y
  suposiciones en `pendientes-administracion.md`.
  - **Qué hace:** `/Administracion/Edificios` (edificios y plantas del centro: crear, renombrar, inactivar, reactivar), `UbicacionUnidad` (edificio y planta de
    cada unidad), `/Administracion/Habitaciones?unidadId=` (habitaciones y plazas de la unidad, con su ocupación) y, en el alta de residente
    (`Residents/Create`, Administración y Enfermería con permiso), una lista opcional «Ubicación en la unidad» con habitaciones y plazas libres. La ficha
    administrativa enseña habitación y plaza. Decisiones del usuario: el bloque cubre estos cuatro niveles (el organigrama queda fuera: ningún documento lo
    define) y habitación y plaza son opcionales en el alta. **Pendiente:** cambiar de habitación o plaza (traslado) y la baja, que esperan a CJ; las vistas
    de Enfermería, Medicina y Auxiliar no enseñan aún la habitación ni la plaza.
  - **Detalle técnico que conviene no redescubrir:**
    - un solo script `0029` con las cuatro tablas, las claves foráneas de `unidades` e `intervalos_ubicacion_residente` y `UX_rli_place_active`; antes de
      crearlas falla si esas columnas ya tenían ids (no los tenían: el alta mandaba null). `TR_units_guard` se recreó sin la parte de edificio/planta;
    - `SqlResidentRepository.CreateWithInitialLocationAsync` ya **no** guarda el edificio y la planta que mande el cliente: usa los de la unidad
      (`ResolveLocationAsync`), y valida habitación y plaza dentro de la transacción; `PLACE_OCCUPIED` también sale del choque con el índice único;
    - `ICenterLayout*` (edificios, plantas, habitaciones, plazas) es puerto aparte de `ICenterStructure*` (unidades); `SqlCenterLayoutRepository` es `partial`
      (`.Rooms.cs`); `AdministracionEstructuraApplicationService` tiene 6 dependencias;
    - la vista `NombreEstructura` sirve para renombrar edificio, planta, habitación y plaza (el `Kind` decide la acción y a dónde volver);
    - **mensajes del alta:** el servicio devuelve mensajes genéricos (el código de dominio no llega a la web); el controlador distingue plaza ocupada
      (conflicto) y habitación o plaza no disponible (entrada inválida) solo cuando se eligió una ubicación;
    - **los fallos intermitentes de la suite eran interbloqueos de SQL Server (1205), causados por los tests:** varios ayudantes contaban filas de
      `eventos_auditoria` por `recurso_id` y `accion_codigo` sin índice que los respalde, y bajo ejecución paralela chocaban con las inserciones de otras
      pruebas (la lectura salía como víctima). Se capturó con `--logger "console;verbosity=detailed"`. Corregido en `f88a5fa` con `WITH (NOLOCK)` en esas
      lecturas de los tests; después, 20 ejecuciones completas seguidas sin un fallo (antes, uno cada tres o cuatro). **Los tests nuevos que consulten esa
      tabla deben hacer lo mismo.** El código de producción no cambió: si en algún momento la auditoría por recurso se consulta desde la aplicación,
      conviene un índice por `(recurso_id, accion_codigo)` o reintentar ante el error 1205;
    - heredocs largos y `sed` con continuaciones de línea en Git Bash pierden la indentación o fallan: escribe los ficheros con la herramienta de escritura
      o edición.
  - **Verificación:**
    - suite en verde antes (240, 291 y 53) y después (240, 300 y 54): más de 20 ejecuciones completas limpias tras corregir los tests, y
      3 vueltas tipo CI con BD nueva por fase, limpias. **Una anomalía distinta, sin explicar:** al parar la app local tras el recorrido con curl, dos ejecuciones
      seguidas de la suite dieron timeouts de SQL masivos (37 y 109 tests; no había sesiones bloqueadas ni transacciones abiertas, con 1,9 GB libres) y las tres
      siguientes salieron limpias. Copia previa `ResidApp-antes-0029-20261001.bak`; BD temporal con los 29 scripts y los seeds dos
      veces, sin errores (ya borrada);
    - curl en local con `dev-integrado-administracion`: edificio y planta (nombre vacío, repetido), colocar la unidad (mal formada, repetir), inactivar con
      hijos activos, reactivar la planta con el edificio inactivo; habitación «Habitacion 12 local» y plazas «Cama A/B» (repetida), alta en «Cama A» (la lista
      deja de ofrecerla), segunda alta en esa plaza rechazada («ya está ocupada»), ficha con «Habitacion 12 local · Cama A», no se puede inactivar la plaza ni la
      habitación con residente, auditoría `ROOM_CREATE`/`PLACE_CREATE`; Enfermería, Medicina, Auxiliar y Dirección reciben «No se puede acceder»;
      Enfermería sin permiso de alta ve el aviso de siempre (con permiso, lo cubre el test funcional existente del alta).
  - **Datos de prueba en la base local:** «Edificio Principal local» con «Planta baja» (la unidad del escenario integrado quedó sin edificio), «Habitacion 12 local»
    con «Cama A» (ocupada por «Residente A local (ficticio)») y «Cama B».
- **Administración, turnos recurrentes con excepciones (historia 4; ADM-16; sin script ni cambios en el seed):** hecho el 2026-10-01 en `main`,
  pusheado y desplegado en Azure (push de `db4f013`, run 36929187490 en verde con `build-and-test` y `deploy`; sin script, Azure sigue en `0028`).
  - **Qué hace:** «Planificar un turno» admite hasta **367 fechas** por envío (una serie: las filas con el mismo `lote_id`) y un campo
    **«Fechas a saltar»** (festivos, `AAAA-MM-DD` o `DD/MM/AAAA`). `Ver serie` (`/Administracion/SeriePlanificacion?loteId=`) enseña sus fechas, retira una
    fecha suelta o **retira la serie desde un día** (`POST RetirarSerie`; solo hoy en adelante), con un solo evento de auditoría
    `SCHEDULE_SERIES_RETIRE`. Decisión del usuario: series con horizonte de un año, no una regla sin fecha final; excepciones al crear y después.
    Detalle en `pendientes-administracion.md`.
  - **Pendiente de este bloque:** nada propio; el resto de Administración es la estructura (edificios, plantas, habitaciones, plazas y organigrama).
  - **Detalle técnico que conviene no redescubrir:**
    - no hay script: `lote_id` ya era la serie y retirar es un `UPDATE` que los triggers permiten;
    - `SchedulePlan.MaxDates` = 367 (`MaxHorizonDays + 1`) y la ventana de la consulta tiene `MaxListDays` = 62;
    - un test funcional existente exige que a Enfermería no le salga el rótulo «Planificar un turno» en la página de planificación: no lo uses
      en textos que se vean sin permiso;
    - `ScheduleEntry` lleva `BatchId` (segundo parámetro); `curl` envía «ñ» en Latin-1 (el aviso de fecha inválida sale «ma%F1ana»: es el
      efecto ya conocido, no un fallo).
  - **Verificación:**
    - suite en verde antes (232, 280 y 48) y después 3 veces (232, 284 y 52), más 3 vueltas tipo CI con BD nueva (sin script nuevo: no hacen
      falta copia ni BD temporal);
    - curl en local con `dev-integrado-administracion`: una fecha a saltar inválida da el error en español; lunes a viernes del 2 al 27 de
      noviembre saltando el 2 y el 25 guarda 18 fechas (no salen esas dos); la serie las enseña; retirar una suelta deja 17 y 1 retirada; retirar
      desde el 20 retira 5 (12 activas, 6 retiradas); repetirlo avisa; la auditoría tiene `SCHEDULE_SERIES_RETIRE`; Enfermería, Medicina, Auxiliar y
      Dirección reciben «No se puede acceder» en la serie.
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion`: «Equipo A az» con «Manana az» de lunes a viernes del 2 al 27 de noviembre
    saltando el 2 y el 25 guarda 18 fechas (no salen esas dos); una fecha a saltar inválida da el error; retirar una suelta deja 17 y 1 retirada;
    retirar desde el 20 retira 5 (12 activas, 6 retiradas); repetirlo avisa; la auditoría tiene `SCHEDULE_SERIES_RETIRE`; Enfermería, Medicina,
    Auxiliar y Dirección reciben «No se puede acceder». Queda esa serie de prueba en Azure.
  - **Datos de prueba en la base local:** una serie de «Equipo I1 prueba» con «Manana prueba» en noviembre de 2026. Nada se borra.
- **Equipos en los seguimientos de Enfermería y Medicina (punto 2 del pendiente de Administración; script `0028`; sin cambios en el seed):** hecho
  el 2026-10-01 en `main`, pusheado y desplegado en Azure (push de `17a4926`, run 36925459313 en verde con `build-and-test` y `deploy`, que aplicó `0028`). Antes, en la misma sesión, `AdministracionApplicationService` se dividió en tres
  (`30509f1`, ya pusheado): `AdministracionApplicationService` (residentes y cuentas), `AdministracionEstructuraApplicationService` (unidades y
  auditoría) y `AdministracionTurnosApplicationService` (turnos, equipos y planificación), con `AdministrationAccessResolver` compartido. En los
  tests: `Build`, `BuildEstructura` y `BuildTurnos`.
  - **Qué hace:** la transferencia de un seguimiento (Enfermería y Medicina) ya no escribe el «equipo o turno entrante» a mano: elige uno de los
    equipos **activos de la unidad del evento** (`<select>` en `Seguimiento.cshtml` de cada vertical); sin equipos activos, aviso y no se puede
    transferir (decisión del usuario: «No, siempre un equipo»). El turno planificado no interviene.
  - **Cómo se guarda:** `equipo_entrante_id` (nullable, FK a `equipos`) más `equipo_entrante` con el nombre copiado al transferir, así el
    historial, la bandeja y Dirección siguen mostrando el nombre aunque el equipo se renombre o inactive. Las transferencias anteriores conservan
    su texto y `equipo_entrante_id` NULL. La inserción SQL (`SqlNursingAssessmentRepository`/`SqlMedicalAssessmentRepository`) copia el nombre
    desde `dbo.equipos` y exige que el equipo sea del centro y la unidad del evento y esté `ACTIVE`; si no, `FOLLOW_UP_TEAM_INVALID`
    (entrada inválida; la transacción se deshace y la revisión no avanza). El dominio (`FollowUpAction.Transfer`) recibe un id de equipo no vacío.
  - **Lectura:** `ITransferTeamDirectory`/`SqlTransferTeamDirectory` (reutiliza `SqlChangeInboxDirectory.ScopedEventsFrom`, así el evento tiene que
    ser visible para el ámbito) y el caso de uso `ListTransferTeams` (exige el perfil del ámbito); `ListTransferTeamsAsync` en
    `EnfermeriaApplicationService` y `MedicinaApplicationService`. `SeguimientoViewModel` (compartido) lleva `Teams`.
  - **Pendiente de este bloque:** que la transferencia tenga en cuenta el turno planificado, (la recepción limitada a miembros del equipo se hizo después, ver la primera
    entrada). No hay test funcional de la pantalla de seguimiento (ninguno existía; se
    verificó con curl).
  - **Verificación:**
    - suite en verde antes (232, 276 y 48) y después 3 veces (232, 280 y 48), más CI con BD nueva (una vuelta dio 2 fallos de integración y
      2 min 27 s con 2,8 GB libres; no pude ver cuáles: las cinco vueltas siguientes salieron limpias, así que lo anoto como carga de la
      máquina, sin explicar) y una BD temporal con los 28 scripts y los seeds dos veces (sin errores, ya borrada);
    - curl en local: Enfermería (evento nuevo → valoración → seguimiento) con el selector de «Equipo I1/I2 prueba»; sin equipo y con un id
      inventado, rechazo en español; con equipo, «Transferencia registrada» y «A: Equipo I1 prueba»; recepción confirmada; inactivando los dos
      equipos desde Administración sale «La unidad del residente no tiene equipos activos» y al reactivarlos vuelve el selector; Medicina (escalado
      → valoración → seguimiento) igual, con «Equipo I2 prueba»;
    - tests nuevos: `Transferencia_…` ×2 y `ListTransferTeams_…` (Enfermería) y `SeguimientoMedico_Transferencia_…` (Medicina); los que usaban
      `EquipoEntrante: "…"` crean ahora un equipo real con `TransferTeamData`.
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-enfermeria`, `-medicina` y `-administracion`, sobre «Residente Integrado Dos» (Uno no se toca) y los
    equipos «Equipo A az»/«Equipo B az» de la prueba anterior: Enfermería (evento → valoración → seguimiento) ofrece el selector; sin equipo y con un id
    inventado se rechaza en español; transferencia a «Equipo A az» y recepción confirmada. Medicina (escalado → valoración → seguimiento) igual, con
    «Equipo B az». Inactivando los dos equipos desde Administración, Medicina ve «La unidad del residente no tiene equipos activos»; reactivados,
    vuelve el selector. Quedan en Azure dos eventos ficticios («Prueba azure de transferencia…») con sus transferencias.
  - **Datos de prueba en la base local:** dos eventos de Enfermería/Medicina de prueba («Prueba de transferencia…») en el residente
    `a1000000-…-000000000002`, con sus transferencias. Copia previa: `ResidApp-antes-0028-20261001.bak`.
- **Administración, turnos y equipos, primer bloque (historia 4; ADM-14/15/17; script `0027`; sin cambios en el seed):** hecho el
  2026-10-01 en `main`, pusheado y desplegado en Azure (push de `4b3fef4`, run 36913920922 en verde con `build-and-test` y `deploy`, que aplicó `0027`). Dos commits: fase 1 (`838f6f8`, catálogo de turnos, equipos y miembros) y fase 2
  (planificación puntual con conflictos). El script `0027` lleva las cuatro tablas, la de planificación incluida.
  - **Qué hace:** `/Administracion/Turnos`, `/Equipos` (con `MiembrosEquipo`), `/Planificacion` (dos semanas, por unidad) y `PlanificarTurno`
    (un equipo en un turno para un rango de fechas y días de la semana, de 1 a 62 fechas). El servidor avisa de dos solapamientos (el mismo
    equipo en turnos que se solapan; una persona en dos equipos con turnos que se solapan, con el cruce de medianoche) y Administración decide:
    solo se guarda con una justificación, que queda en las fechas afectadas. Las horas de un turno no cambian nunca. Detalle, decisiones y
    suposiciones en `pendientes-administracion.md`.
  - **Pendiente de este bloque:** nada (las recurrencias y los seguimientos con equipos están hechos; ver las entradas anteriores).
  - **Detalle técnico que conviene no redescubrir:**
    - la confirmación de un solapamiento lleva una huella SHA-256 (`ConflictosVistos`) de los que se enseñaron; si cambian, no se confirma;
    - el reenvío de un lote ya guardado enseña «Todas esas fechas ya tienen planificado ese equipo…» (la vista previa corre antes que la
      escritura); el token de lote (`lote_id`) protege el servicio, no esa pantalla;
    - `AdministracionController` es ahora `partial` (`.Turnos.cs`, `.Planificacion.cs`); el parámetro `service` del constructor primario vale en todas
      las partes. **El servicio ya se dividió** (refactor posterior): `AdministracionApplicationService` (residentes y cuentas),
      `AdministracionEstructuraApplicationService` (unidades y auditoría) y `AdministracionTurnosApplicationService` (turnos, equipos y
      planificación); los tres reciben `AdministrationAccessResolver` (el ámbito activo de Administración, que antes era privado). El
      controlador recibe los tres (`service`, `estructura`, `turnos`). En los tests: `Build`, `BuildEstructura` y `BuildTurnos`;
    - **heredocs largos en Git Bash:** varios comandos con un heredoc largo y comillas simples fallaron con «unexpected EOF» sin ejecutar nada.
      Escribe los ficheros con la herramienta de escritura y aplica los `sed` aparte.
  - **Verificación:**
    - suite en verde antes (207, 259 y 46) y después 3 veces en cada fase (224, 267 y 47 tras la fase 1; 232, 276 y 48 tras la 2), más 3 vueltas
      tipo CI con BD nueva y una BD temporal con los 27 scripts y los seeds aplicados dos veces (ya borrada);
    - curl en local con `dev-integrado-administracion` sobre la unidad del escenario integrado: tres turnos (uno nocturno, que sale «termina al
      día siguiente»); dos equipos con `dev-integrado-enfermeria` y `-medicina` como miembros (los elegibles son los de Auxiliar, Enfermería y Medicina
      de la unidad, sin Administración ni Dirección); planificar Mañana en 3 fechas; el mismo equipo y turno otra vez da el aviso de ya planificado; Tarde del
      otro equipo el primer día avisa de que Medicina está en los dos y confirmar sin justificación se rechaza; con ella se guarda y sale
      «Solapamiento justificado»; la noche de un día y la mañana del siguiente no avisan; retirar una fecha funciona y repetirlo avisa; la auditoría
      tiene `SCHEDULE_CREATE` ×6, `SCHEDULE_RETIRE`, `TEAM_CREATE` y `TEAM_MEMBER_ADD`;
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en Turnos, Equipos y
      Planificación (en `PlanificarTurno` la vi vacía y se corrigió para que también lo diga);
    - dos ejecuciones filtradas de los tests nuevos dieron errores de compilación y de orden de mis propios tests, ya corregidos; ningún fallo de
      entorno en esta tanda.
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion`, tras el despliegue de `0027`: tres turnos «Manana/Tarde/Noche az»
    (el nocturno sale «termina al día siguiente»); «Equipo A az» (enfermería y medicina) y «Equipo B az» (medicina y auxiliar) en la unidad del
    escenario integrado; Mañana de A del 5 al 7 de octubre; Tarde de B el 5 avisa de que `dev-integrado-medicina` está en los dos equipos, confirmar
    sin justificación se rechaza y con ella se guarda («Solapamiento justificado»); Noche de B el 6 no avisa; retirar una fecha funciona y repetirlo
    avisa. La auditoría tiene 5 `SCHEDULE_CREATE`, 1 `SCHEDULE_RETIRE`, 2 `TEAM_CREATE`, 4 `TEAM_MEMBER_ADD` y 3 `SHIFT_CREATE`. Enfermería,
    Medicina, Auxiliar y Dirección reciben «No se puede acceder» en Turnos, Equipos, Planificación y PlanificarTurno. Los datos de prueba quedan.
  - **Datos de prueba en la base local:** turnos «Manana/Tarde/Noche prueba», equipos «Equipo A/B prueba» (en la unidad de la prueba de estructura) y
    «Equipo I1/I2 prueba» (en la del escenario integrado) con su planificación, en octubre de 2026. No se limpian (nada se borra).
  - Copia previa: `ResidApp-antes-0027-20261001.bak`.
- **Administración, auditoría administrativa (historia 9, ADM-28 / AUD-01 a AUD-03; sin script):** hecho el 2026-10-01
  y desplegado en Azure (push de `4c8b389`, run 36905131323 en verde con `build-and-test` y `deploy`; sin script, Azure sigue en `0026`; la pantalla no se ha probado allí).
  - **Qué hace:** `/Administracion/Auditoria` (tarjeta «Auditoría» del inicio) lista, del más reciente al más antiguo, las acciones
    administrativas del centro del ámbito (cuentas, perfiles y permisos, estructura, residentes y familias) con cuándo, qué, quién y con
    qué perfil, y la cuenta afectada, la unidad y el residente si constan. Filtros por periodo (30 días por defecto, máximo 366), tipo de
    acción y cuenta afectada; nunca por quien actuó ni con contadores (AUD-02). Como mucho 500 filas, con aviso. Los eventos con unidad
    solo salen si la unidad está concedida al ámbito. Detalle, decisiones y suposiciones en `pendientes-administracion.md`.
  - **Lista cerrada** (`Domain/Audit/AdministrativeAudit.cs`): una acción nueva no aparece hasta añadirla allí **y** a
    `AuditActionDisplay` (un test comprueba que todas tengan etiqueta). Quedan fuera las clínicas, `CLINICAL_DETAIL_READ` y
    `REFERENCE_RANGES_UPDATE`.
  - **Verificación:**
    - suite en verde antes (196, 251 y 43) y después 3 veces (207, 259 y 46), más 3 vueltas tipo CI con BD nueva (no hay script, así
      que no hizo falta BD temporal);
    - curl en local con `dev-integrado-administracion`: en el periodo por defecto salen 20 tipos de acción administrativa (altas de
      residente, autorizaciones, contacto urgente, permisos, unidades, cuentas…) y ninguno clínico ni con código interno, aunque la
      base tiene unos 97 mil eventos de auditoría; el filtro por acción, el periodo sin eventos, el periodo imposible y los filtros mal
      formados responden bien; `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta
      operación»;
    - `AdministracionAuditoriaTests` (incluido el test por reflexión de AUD-02) comprueban que los tipos de la lectura no tienen texto libre, contadores ni
      filtro por actor.
- **Perfil de plataforma: alta de un centro nuevo (script `0026`; seed `dev_seed_plataforma.sql`; ADR
  `docs/decisiones-arquitectura/0006-perfil-plataforma.md`):** hecho el 2026-10-01 y desplegado en Azure (push de `4029380`, run 36902277867 en verde con `build-and-test` y `deploy`, que aplicó `0026` y el seed: `dev-plataforma` existe en Azure; no se ha probado la pantalla allí).
  - **Qué hace:** séptimo perfil, `PLATAFORMA`, con `/Plataforma`: lista de centros y «Nuevo centro», que crea en una transacción el
    centro, su primera unidad, la cuenta de su primer administrador y su ámbito de Administración con la unidad concedida. El
    operador pertenece a un centro reservado «Plataforma» (`5F3A1C00-0000-4000-8000-000000000001`), porque `ambitos_perfil.centro_id`
    es `NOT NULL`. No ve datos clínicos y los demás servicios lo deniegan. Cuenta de desarrollo: `dev-plataforma`. Detalle, decisiones
    y suposiciones en `pendientes-administracion.md`.
  - **Raíz de confianza:** la primera cuenta de plataforma de un entorno real se inserta por SQL una sola vez (plantilla: el seed). Si
    se hace el push, **hay que crear esa cuenta en Azure** para poder usar la pantalla; el seed de desarrollo ya crea `dev-plataforma`
    allí si el pipeline aplica los seeds (Azure es de desarrollo).
  - **Aviso:** un perfil nuevo toca todos los `switch` por perfil. Se revisaron `SystemProfileDisplay`, `ProfilePermissions` y
    `ProfessionalAccount`, y `ElPerfilDePlataforma_NoEntraEnNingunOtroServicio` comprueba que Administración, Enfermería, Medicina y
    Dirección lo deniegan (Auxiliar no se probó porque su servicio no se construye desde el test). Si se añade un servicio nuevo, añádelo.
  - **Verificación:**
    - suite en verde antes (184, 239 y 42) y después 3 veces (196, 251 y 43), más 3 vueltas tipo CI con BD nueva y una BD temporal
      con los 26 scripts y todos los seeds, aplicados dos veces (una sola cuenta `dev-plataforma`; ya borrada);
    - dos ejecuciones filtradas de los tests nuevos fallaron con «tiempo de espera durante la fase previa al inicio de sesión» (la
      máquina tenía 2,2 GB libres); tras `dotnet build-server shutdown` pasaron los 12 en 1 s. Es el patrón de «Suite lenta»;
    - curl en local con `dev-plataforma`: el Inicio ofrece «Centros»; el formulario vacío da los errores en español; se crea
      «Residencia de prueba plataforma (ficticia)» (`PRUEBA-PLAT-1`, unidad `PLANTA-1`, administrador `dev-prueba-plat-admin`); el
      reenvío no la duplica; el mismo identificador de acceso con otro centro da «Ya existe» y no crea nada; la lista no incluye el
      centro reservado. Con `dev-prueba-plat-admin`: su Inicio es el de Administración, Estructura muestra su unidad, el alta de
      residente la ofrece ya elegida y da de alta «Residente prueba centro nuevo (ficticio)», que sale en su lista;
    - `/Plataforma` da «No se puede acceder a esta operación» a `dev-integrado-enfermeria`, `-medicina`, `-direccion`, `-auxiliar` y
      `-administracion`, y al administrador del centro nuevo.
  - **Datos de prueba en la base local:** el centro «Residencia de prueba plataforma (ficticia)» con su administrador y un residente.
    Hay además centros `FUNC-CENTER-…` de los tests funcionales, que no se limpian.
  - Copia previa: `ResidApp-antes-0026-20261001.bak`.
- **Administración, bloque 5: estructura del centro, unidades (ADM-05; historia 2, primer bloque; script `0025`; sin cambios
  en el seed):** hecho el 2026-10-01 y desplegado en Azure (push de `0a48f3f`, mismo run 36902277867, que aplicó `0025`).
  - **Qué hace:** `/Administracion/Estructura` lista las unidades concedidas al ámbito de Administración (activas e
    inactivas, con código, estado y residentes ubicados), con «Nueva unidad», «Cambiar nombre» e «Inactivar»/«Reactivar». Una
    unidad nueva se concede al ámbito de quien la crea; una con residentes ubicados no se inactiva; código y nombre no se
    repiten en el centro; nada se borra (`TR_units_guard` y `TR_units_no_delete`). Detalle, decisiones y suposiciones en
    `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (174, 233 y 41) y después 3 veces (184, 239 y 42), más 3 vueltas tipo CI con BD nueva y una BD
      temporal con los 25 scripts y el seed (sin errores, ya borrada);
    - una primera ejecución filtrada de los tests nuevos dio un fallo en `Inactivar_NoValeConResidentesUbicados_…` (41 s); no se
      guardó su mensaje y no se repitió en 7 ejecuciones posteriores (la misma tanda filtrada una vez, la suite completa 3 veces
      y las 3 vueltas tipo CI). No está explicado: puede ser el patrón de «Suite lenta» (se había compilado justo antes), pero no
      está confirmado. Si vuelve, guarda la salida completa;
    - curl en local con `dev-integrado-administracion`: lista inicial, formulario vacío (errores en español), alta de
      «Prueba estructura (ficticia)» (código `PRUEBA-EST-1`; el reenvío no la duplica, el código o el nombre repetidos dan «Ya existe»,
      el código con espacio da el aviso), renombrar (y renombrar igual, rechazado), inactivar (sale del alta de residentes; repetir
      avisa), reactivar (vuelve al alta), inactivar la unidad del escenario con 8 residentes (rechazado), y la auditoría tiene
      `UNIT_CREATE`, `UNIT_RENAME`, `UNIT_DEACTIVATE` y `UNIT_ACTIVATE`. `dev-integrado-enfermeria` recibe «No se puede acceder a
      esta operación» y no ve «Nueva unidad». El POST de Enfermería se probó con el token de la pantalla de entrada, así que su
      rechazo lo cubre el test de integración, no ese curl;
    - la unidad de prueba «Prueba estructura renombrada (ficticia)» queda activa en la base local.
  - Copia previa: `ResidApp-antes-0025-20261001.bak`.
  - Al empezar la sesión se comprobó que `origin/main` y `HEAD` eran el mismo commit (`bf90bda`) y que CJ no había contestado nada.
- **Pantalla de alta de residente según el permiso (sin script):** hecho el 2026-10-01, en `origin/main` (commit `bf90bda`; no se ha revisado su run en Azure). Antes,
  `GET /Residents/Create` abría el formulario a cualquier ámbito y solo se denegaba al guardar. Ahora, con la misma
  regla que `ResidentBaselinePolicy` (Administración siempre; Enfermería con `RESIDENT_IDENTITY_CREATE`), sin permiso sale
  un aviso en lugar del formulario; guardar sigue autorizándose en el servidor. Test
  `AltaDeResidente_ElFormularioSoloSaleSiElAmbitoPuedeDarDeAlta` (falla sin el cambio); suite en verde 3 veces (174, 233
  y 41); curl en local: `dev-multi` (todos sus ámbitos), `dev-integrado-enfermeria` y `dev-prueba-enfermeria-nueva` ven el
  aviso y `dev-integrado-administracion` el formulario; concedido el permiso a `dev-prueba-enfermeria-nueva` vio el
  formulario y dio de alta «Prueba alta pantalla permiso (ficticia)»; revocado otra vez, vuelve el aviso.
- **«Firmar borrador de basal» en el Inicio por permiso (sin script):** hecho el 2026-10-01, en `origin/main` (commit `23e9616`; no se ha revisado su run en Azure). La
  tarjeta sale a Enfermería y Medicina solo con `BASELINE_INITIAL_COMPLETE` o `BASELINE_REEVALUATE` (firmar exige uno u
  otro según el motivo del borrador). Test `Inicio_FirmarBorradorDeBasal_SoloConUnPermisoDeBasal` (falla sin el cambio);
  suite en verde 3 veces (174, 233 y 37); curl en local: `dev-multi` (Enfermería o Medicina, sin permisos) no la ve y
  `dev-integrado-enfermeria`, `dev-integrado-medicina` y `dev-prueba-enfermeria-nueva` sí.
- **Administración, bloque 4: permisos configurables (historia 4; script `0024`; sin cambios en el seed) e Inicio por
  permiso:** hecho el 2026-10-01 y desplegado en Azure (push de `d6629b3`, run 36880340272 en verde). En Azure, con
  `dev-integrado-administracion` sobre `prueba-azure-usuarios-b3` (Enfermería): sus tres permisos salen «no concedidos»;
  conceder «Dar de alta residentes» la hace aparecer en su Inicio; repetirlo avisa del conflicto; `CLINICAL_DETAIL_READ`
  se rechaza; revocarlo la quita y deja el permiso en «Revocado». Quedó sin permisos.
  - **Qué hace:** sección «Permisos» en la pantalla de cada perfil, con el catálogo de ese perfil (Enfermería: alta,
    basal inicial y reevaluación; Medicina: basal inicial, reevaluación y rangos; Dirección: lectura clínica detallada y
    rangos) y «Conceder»/«Revocar». El Inicio solo enseña «Alta de residente» y «Rangos de referencia» con el permiso.
    Detalle, decisiones y suposiciones en `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (173, 231 y 34) y después 3 veces (174, 233 y 35), más 3 vueltas tipo CI y una BD temporal con
      scripts y seed;
    - curl en local con `dev-integrado-administracion` sobre `dev-prueba-enfermeria-nueva`:
      - la sección muestra sus tres permisos «no concedidos»;
      - concederle «Dar de alta residentes» hace aparecer la tarjeta en su Inicio y da de alta «Prueba alta Enfermeria
        (ficticio)»; repetir la concesión avisa del conflicto; revocarlo quita la tarjeta y deniega el alta;
      - `CLINICAL_DETAIL_READ` y `REFERENCE_RANGES_MANAGE` se rechazan («no es de este perfil»);
      - sin «Basal inicial» no crea el borrador del basal y con él sí;
      - la ficha propia sigue sin acciones;
      - `dev-integrado-direccion` ve «Rangos de referencia» y `dev-integrado-medicina` (sin el permiso) no.
  - **Antes de empezar,** verificado en Azure el push de `c913af8` (runs 36875998442 y 36875998509 en verde). Como
    `prueba-azure-usuarios-b3` se registró el evento «Prueba manual firma con nombre azure: disnea.» sobre «Prueba familiares
    azure (ficticio)», se valoró, se activó el protocolo y se firmó la derivación. El PDF dice «Firmado por Prueba usuarios
    azure (ficticia) (Enfermería, cuenta prueba-azure-usuarios-b3)», con todos los títulos en negrita. Queda abierto, sin
    llamada.
- **Dos correcciones tras el bloque 3 (sin script):** hechas el 2026-10-01 y desplegadas en Azure (push de `c913af8`).
  - **Firma del PDF de derivación:** lleva el nombre visible de la cuenta (`ActiveProfileScope.AccountDisplayName`), su
    perfil y su identificador; sin nombre, como antes. Test `FirmaDerivacionNombreTests`; PDF real revisado.
  - **Ámbito de Dirección:** `SqlSupervisionDirectory.FindScopeAsync` dice «limitado a determinados residentes» con
    cualquier fila de `ambitos_perfil_residente`, aunque esté revocada, como la evidencia de autorización (ADR 0004).
    Test `Ambito_ConSuUnicaAsignacionRevocada_SigueRestringido`.
  - Suite en verde 3 veces (173, 230 y 34) y 3 vueltas tipo CI con BD nueva.
- **PDF de derivación generados a la vez (sin script):** corregido el 2026-10-01 y desplegado en Azure (push de
  `c913af8`). En el PDF de un test, «residente» salía sin negrita en «Identificación del residente y del centro». Un PDF generado solo salía bien; de 16
  generados en paralelo, 2 tenían cambios de fuente de más en el contenido de la página. `ReferralReportPdfRenderer`
  serializa ahora el renderizado con un `Lock` estático; 200 en paralelo salieron idénticos. Test
  `ReferralReportPdfRendererTests` (64 en paralelo comparados con uno solo): falla 3 de 3 sin el candado y pasa con él.
  Suite en verde 3 veces (173, 231 y 34) y 3 vueltas tipo CI (las primeras 3 se hundieron con 1,8 GB libres; tras
  `dotnet build-server shutdown`, limpias).
- **Administración, bloque 3: usuarios profesionales, perfiles, unidades y residentes de Auxiliar (historia 4 sin turnos
  ni permisos; ADM-12 y ADM-13; script `0023`; sin cambios en el seed):** hecho el 2026-10-01 y desplegado en Azure (push
  de `31ab320`, run 36869186733 en verde con `build-and-test` y `deploy`, que aplicó `0023`).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion`: alta de `prueba-azure-usuarios-b3` («Prueba usuarios
    azure (ficticia)», Enfermería), que entra y ve los 5 residentes; suspenderla deja su sesión sin acceso y reactivarla la
    devuelve; Auxiliar concedido con «Prueba familiares azure (ficticio)» asignado, que es el único que ve; la última unidad
    no se revoca; revocar el Auxiliar deja su sesión sin acceso. `dev-integrado-enfermeria` y `-direccion` no abren
    Usuarios, y la ficha propia es de solo lectura. La cuenta queda activa con Enfermería y el Auxiliar revocado.
  - **Qué hace:** lista de usuarios del centro, alta (identificador de acceso, nombre, perfil y unidades), ficha con nombre,
    suspender/reactivar y conceder perfil, y pantalla de cada perfil con sus unidades, sus residentes (Auxiliar) y revocar.
    Los permisos configurables llegaron en el bloque 4. Detalle, decisiones y suposiciones en
    `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (161, 219 y 32) y después 3 veces (173, 228 y 34), más 3 vueltas tipo CI con BD nueva y una BD
      temporal con todos los scripts y el seed (sin errores, ya borrada);
    - curl en local con `dev-integrado-administracion`: alta de `dev-prueba-enfermeria-nueva` (el reenvío no duplica; el
      mismo identificador en mayúsculas da «Ya existe»), que entra como Enfermería y ve los 5 residentes de la unidad;
      suspenderla deja su sesión abierta en «No se puede acceder» y sin ámbitos al volver a entrar; suspender otra vez avisa;
      reactivar la devuelve; conceder Auxiliar (ya no ofrece Enfermería), asignar «Prueba familiares (ficticio)» y verlo solo
      a él desde Auxiliar; revocar la última unidad por POST, rechazado; revocar el perfil Auxiliar deja la sesión Auxiliar
      abierta sin acceso; cambiar el nombre y repetirlo, rechazado;
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en
      la lista y la ficha; la ficha de `dev-integrado-administracion` es de solo lectura; la auditoría tiene cada acción.
  - Una vuelta de los tests de Administración y otra del test funcional nuevo fallaron por tiempos de espera (también
    `sqlcmd` dio *login timeout*), con 1,5 GB libres; tras `dotnet build-server shutdown` pasaron (lección «Suite lenta»).
- **Contacto urgente en la derivación y en la ficha de Enfermería y Medicina (sin script):** hecho el 2026-10-01 y
  desplegado en Azure (push de `fc360fa`, run 36858712803 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure (2026-10-01)** sobre «Prueba familiares azure (ficticio)»:
    - las fichas de `dev-integrado-enfermeria` y `-medicina` muestran a Tomás (622 555 666, `tel:622555666`, sin correo);
    - evento «Prueba manual contacto urgente azure: disnea.» registrado, valorado, con protocolo y derivado (la vista previa
      no incluye el contacto); el protocolo muestra a Tomás y precarga «Tomás Pérez (ficticio) (Hijo)», y la llamada
      «Contactado» se registró con ese texto. Queda abierto, sin cerrar;
    - la hora de esa llamada quedó en 12:03 en vez de ~14:03 porque el script la calculó con `TZ=Europe/Madrid date` en Git
      Bash (la lección de más abajo: usa `date` sin `TZ`); es un dato de prueba, no un fallo de la app;
    - `dev-integrado-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en las fichas y el protocolo, y
      el episodio de Dirección no muestra el contacto.
  - **Qué hace:** tras firmar la derivación, el protocolo muestra el contacto urgente (teléfono con `tel:`) y precarga «A
    quién se llama»; la ficha de Enfermería y de Medicina tiene la tarjeta «Contacto urgente». Detalle en
    `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (161, 216 y 29) y después 3 veces (161, 219 y 32), más 3 vueltas tipo CI con BD nueva;
    - curl en local sobre «Prueba familiares (ficticio)»: las fichas de Enfermería y Medicina dicen que no hay contacto; tras
      designar a Tomás como Administración, lo muestran (sin el correo); evento «Prueba manual contacto urgente: disnea.»
      registrado, valorado, con protocolo (sin bloque de contacto antes de derivar) y derivado (la vista previa no lo
      incluye); el protocolo muestra el contacto y precarga «Tomás Pérez (ficticio) (Hijo)», y la llamada se registra con
      ese texto;
    - Auxiliar y Dirección no ven el contacto en ninguna pantalla (las de Enfermería y Medicina les dan acceso denegado; el
      episodio de Dirección no lo muestra).
  - Dos vueltas de la suite fallaron por tiempos de espera de ejecución con 0,8–1,2 GB libres (sin bloqueos en SQL Server);
    las mismas pruebas pasaron en cuanto hubo memoria (lección «Suite lenta»).
- **Administración, bloque 2: familiares, autorizaciones y contacto urgente (historia 3; ADM-08 a ADM-11 del wireframe,
  FAM-01; script `0022`; sin cambios en el seed):** hecho el 2026-10-01 y desplegado en Azure (push de `380552b`, run
  36855312637 en verde con `build-and-test` y `deploy`, que aplicó `0022`).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion` sobre «Prueba familiares azure (ficticio)», dado de
    alta para eso:
    - Lucía (el reenvío del alta no la duplica) y Tomás añadidos, sin autorización;
    - autorización de Lucía: activar antes de abrir rechazado, abrir, activar hasta el 31/12/2026, suspender sin motivo
      rechazado, suspender, reactivar y suspender con número antiguo (conflicto); queda **Activa**;
    - contacto urgente: Lucía, repetido (rechazado), Tomás, número antiguo (conflicto); queda **Tomás**, con su teléfono
      editado a 622 555 666.

    `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en las
    cuatro pantallas, sin ver datos del familiar, y su POST de suspensión no cambia nada.
  - **Qué hace:** sección «Familiares» y «Contacto urgente» en la ficha administrativa; pantallas Añadir/Editar familiar,
    Autorización (Pendiente → Activa → Suspendida / Revocada / Caducada, con historial) y Contacto urgente. Nada da acceso
    todavía: el Portal Familiar no existe. Detalle, decisiones y suposiciones en `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes de empezar (133, 208 y 27) y después 3 veces seguidas (161, 216 y 29), más 3 vueltas tipo CI con
      BD nueva (23 scripts);
    - `0022` probado a mano en una BD desechable con todos los scripts y el seed (20 casos: transiciones, motivo, numeración,
      inmutabilidad, contacto de otro residente, borrados);
    - curl en local con `dev-integrado-administracion` sobre «Prueba familiares (ficticio)»: añadir (el reenvío no duplica, el
      teléfono «llamar» se rechaza), activar antes de abrir rechazado, abrir, activar con fecha, suspender sin motivo
      rechazado, suspender, activar con fecha pasada rechazado, reactivar, revocar con número antiguo (conflicto), revocar y
      sin más cambios; contacto designado, repetido (rechazado), cambiado, con número antiguo (conflicto), quitado y quitado
      otra vez (rechazado); edición del teléfono y edición sin cambios (rechazada);
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en
      las cuatro pantallas, sin ver ningún dato del familiar, y sus POST no cambian nada.
  - La primera vuelta de la suite volvió a dar fallos por tiempo de espera (0,8 GB libres); con `dotnet build-server
    shutdown` salió limpia (lección «Suite lenta»).
  - Copia previa: `ResidApp-antes-0022-20261001.bak`.
- **Correcciones (sin script), 2026-10-01:** desplegadas en Azure (push de `2816ce3`, run 36839380770 en verde con
  `build-and-test` y `deploy`). Prueba en Azure: el alta con `dev-integrado-administracion` muestra la unidad vacía y los
  errores en español (unidad vacía o «no-es-un-guid»), sin crear ningún residente; los indicadores del 15/08 al 01/10 con
  `dev-integrado-direccion` siguen dando 4 registrados y 3 cerrados (no hay hechos cerca de medianoche).
  - **Indicadores de Dirección, días en UTC:** las fechas se guardan en UTC, pero el periodo se comparaba con días locales y
    se agrupaba por la fecha UTC, así que un hecho entre las 00:00 y las 02:00 de España contaba en el día (y a fin de mes,
    en el mes) anterior. Ahora `SupervisionIndicatorRules.UtcBounds` convierte los límites del periodo a UTC y `Aggregate`
    agrupa por el día de la zona horaria local del servidor (`TimeZoneInfo.Local`, Europe/Madrid en Azure). Tests nuevos en
    `SupervisionIndicatorRulesTests` (cambio de día en verano y en invierno y el día de 25 horas); el de cambio de día falla
    con el código anterior.
  - **Alta de residente:** `UnidadId` empezaba con el identificador de ceros y sus mensajes salían en inglés. Ahora es
    anulable y empieza vacío; los campos visibles llevan mensajes en español, y `Program.cs` traduce los mensajes del enlace
    del modelo (p. ej., «no-es-un-guid» no es un valor válido para Unidad), que valen para todos los formularios. Test
    funcional `Create_UnidadEmpiezaVacia_YSusErroresSalenEnEspañol` (hoy
    `Create_LaUnidadSeEligeEntreLasDelAmbito_YSusErroresSalenEnEspañol`).
- **Selector de unidad en el alta (sin script), 2026-10-01:** desplegado en Azure (push de `6a433fe`, run 36841516152 en
  verde con `build-and-test` y `deploy`). Prueba en Azure: `dev-integrado-administracion`, `-enfermeria` y `-auxiliar` ven la
  unidad integrada ya elegida; el alta de «Prueba selector unidad azure (ficticio)» como Administración llega a la
  confirmación y sale en la lista, y Auxiliar recibe «No se puede acceder a esta operación».
  **Qué hace:** la unidad ya no se escribe a
  mano: se elige en un `<select>` con las unidades concedidas y activas del ámbito activo
  (`IProfileScopeDirectoryProvider.ListUnitsAsync`, con las mismas condiciones con las que `SqlAuthorizationEvidenceProvider`
  autoriza el alta; caso de uso `ListActiveScopeUnits`). Con una sola unidad viene elegida; con varias empieza en «Elige una
  unidad»; sin ninguna, un aviso sustituye al formulario. El alta sigue autorizando la unidad recibida, así que el selector
  solo orienta. Tests: `ScopeUnitsTests` (integración: revocadas, inactivas, sin conceder, ámbito ajeno y otro centro) y el
  funcional del alta con una y dos unidades. Probado en local con curl: el alta de `dev-integrado-administracion` con la unidad
  del selector llega a la confirmación y Auxiliar sigue recibiendo «No se puede acceder a esta operación».
- **Administración, bloque 1: residentes, ficha administrativa y corrección de identidad (ADM-01 a ADM-03, RES-01,
  RES-03, RES-04; script `0021`; cambia el seed):** hecho el 2026-10-01 y desplegado en Azure (push de `c650ba2`, run
  36833525743 en verde con `build-and-test` y `deploy`, que aplicó `0021` y el seed).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion` (ámbito único, se selecciona solo):
    - inicio, lista (2 residentes integrados) y «DÓS» encuentra solo a Residente Integrado Dos;
    - ficha de Residente Integrado Dos con su intervalo de ubicación y sin correcciones;
    - alta de «prueba correccion identidad azure (ficticio)» (`4dec3da0-c937-4a35-bf4f-0cf1540380f1`, «No consta») y
      corrección a Mujer con motivo; sin motivo y sin cambios se rechaza, y el reenvío da conflicto;
    - la primera corrección guardó «Correcci%F3n» porque curl en Git Bash envió las tildes en Latin-1 (fallo del envío de
      prueba, no de la app). Una segunda corrección enviada en UTF-8 desde fichero la dejó en «Prueba Corrección Identidad
      Azure (ficticio)»; la ficha muestra las dos. Para probar con tildes por curl, pasa el valor con `--data-urlencode
      "campo@fichero"`.

    `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en
    la lista, la ficha y el formulario.
  - **Qué hace:** `/Administracion` (inicio), `/Administracion/Residentes` (lista con edad, sexo, unidad y fecha de alta,
    con búsqueda y filtro), `/Administracion/Residente` (ficha con historial de ubicación y de correcciones) y
    `/Administracion/CorregirIdentidad` (con motivo obligatorio). Sin basal ni contenido clínico.
  - **Sin traslado ni baja:** `D1-P04` está diferida en los flujos; se ha preguntado a CJ en
    `docs/pendientes-cj/traslado-y-baja-residente.html`. El detalle y las decisiones están en `pendientes-administracion.md`
    (fichero nuevo).
  - **Verificación:**
    - suite local en verde antes de empezar (125, 201 y 21) y después 3 veces seguidas (131, 206 y 26);
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con todos los scripts (22) y el seed aplicado dos veces: la cuenta nueva queda una sola vez;
    - `0021` probado a mano en una BD desechable (8 casos del trigger y de la tabla);
    - curl con `dev-integrado-administracion`:
      - inicio (2 residentes), lista, «DÓS» encuentra a Residente Integrado Dos y un filtro mal formado se ignora;
      - ficha de Residente Integrado Dos con su historial de ubicación;
      - alta de «prueba correccion identidad (ficticio)» y su corrección a «Prueba Corrección Identidad (ficticio)», Mujer,
        con motivo; el reenvío da conflicto, sin motivo se rechaza y sin cambios también;
      - la ficha muestra la corrección y la auditoría tiene `RESIDENT_CREATE` y `RESIDENT_IDENTITY_CORRECT`;
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación».
  - La primera vuelta completa dio 188 fallos y otra 52, todos tiempos de espera al iniciar sesión en SQL Server, con
    1 GB libre. Ver la lección «Suite lenta»: se resolvió liberando los nodos de MSBuild.
  - Copia previa: `ResidApp-antes-0021-20261001.bak`.
- **Dirección Clínica, bloque 4: indicadores agregados, evolución e informe imprimible (DIR-08, DIR-09, DIR-10, DIR-16; sin
  script):** hecho el 2026-10-01 y desplegado en Azure (push de `60b3c86`, run 36827738565 en verde con `build-and-test` y
  `deploy`).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-direccion`:
    - el periodo por defecto da 4 registrados (3 de Enfermería y 1 de Medicina), 3 cerrados, 1 escalado, 1 protocolo, 1 derivación
      y 1 indicación sin resolver, que cuadran con los datos de prueba de Azure anotados más abajo;
    - el periodo 15/08–01/10 da los tres meses, con «1 de octubre de 2026» (ICU de Linux);
    - las fechas mal formadas se ignoran, y `desde > hasta` y más de 366 días dan su mensaje;
    - la página no muestra residentes.

    `dev-integrado-enfermeria`, `-medicina` y `-auxiliar` reciben «No se puede acceder a esta operación».
  - **Qué hace:** `/Direccion/Indicadores?desde=&hasta=` muestra, por unidad y en total, los indicadores como «n de m». Son cuatro
    grupos (actividad, escalados y urgencias, indicaciones médicas y continuidad entre turnos), con la evolución mes a mes debajo.
  - **Informe:** «Imprimir / guardar como PDF» deja solo el informe de actividad agregado. Las decisiones y suposiciones están en
    `pendientes-direccion.md`.
  - **Verificación:**
    - suite local en verde antes de empezar (121, 199 y 17) y después 3 veces seguidas (125, 201 y 21);
    - 3 vueltas tipo CI con BD nueva en verde;
    - `IndicatorPeriodFilterTests` y `LocalizationTests` en un contenedor Linux (nombres de los meses con ICU);
    - curl con `dev-integrado-direccion`:
      - el periodo por defecto cuadra con una consulta `sqlcmd` de control: 15 registrados (1, 12 y 2 por origen; 5 prioritarios),
        12 cerrados, 5 escalados, 4 protocolos, 2 derivaciones, 4 indicaciones y una transferencia recibida por perfil;
      - el periodo 15/08–01/10 da tres meses recortados;
      - las fechas mal formadas se ignoran, y `desde > hasta` y más de 366 días dan su mensaje;
      - la página no muestra residentes;
    - `dev-integrado-enfermeria`, `-medicina` y `-auxiliar` reciben «No se puede acceder a esta operación»;
    - impresión revisada con Edge sin interfaz (`msedge --headless --print-to-pdf` sobre la página guardada con `<base href>`): la
      primera versión cortaba las tablas anchas (`table-responsive`), y se corrigió con estilos de impresión en la propia vista.
- **Dirección Clínica, bloque 1: supervisión operativa en solo lectura (DIR-01 a DIR-04, DIR-17; sin script):** hecho el
  2026-09-30 y desplegado en Azure (push de `04c68ce`, run 36762590727 en verde con `build-and-test` y `deploy`). `/Direccion` (contadores por unidad con denominador),
  `/Direccion/Pendientes` (filtro por tipo y unidad), `/Direccion/Episodio` (hitos sin texto) y `/Direccion/Ambito`. Sin
  contenido clínico: los tipos de supervisión no tienen campos de texto. El detalle y las suposiciones están en
  `pendientes-direccion.md` (fichero nuevo).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-direccion`: inicio con 1 episodio abierto (el escalado de
    ejemplo de Residente Integrado Dos, «en valoración médica»), la fila de la unidad con sus denominadores, pendientes
    con el filtro «Escalado a Medicina» («1 de 1») y valores mal formados ignorados, y el episodio con sus 4 hitos
    (registro, valoración de Enfermería, escalado y valoración médica) sin el texto de la observación. Un episodio
    inexistente vuelve a Pendientes y «Mi ámbito» muestra el centro, la unidad y sus dos permisos.
    `dev-integrado-enfermeria` y `-medicina` reciben «No se puede acceder a esta operación».
  - **Verificación:**
    - suite local en verde 3 veces seguidas (121, 199 y 17) y 3 vueltas tipo CI con BD nueva en verde;
    - curl con `dev-integrado-direccion`: inicio (Residente Integrado Uno y Dos, 3 abiertos), pendientes con filtro y valores
      mal formados ignorados, los 3 episodios sin ningún texto de observación, episodio inexistente vuelve a Pendientes y
      «Mi ámbito»; `dev-integrado-enfermeria`, `-medicina` y `-auxiliar` reciben «No se puede acceder a esta operación».
- **Filtro de la bandeja de cambios ordinarios (ENF-02; sin script):** hecho el 2026-09-30 y desplegado en Azure (push de `8c22c13`, run 36753447680 en verde con `build-and-test` y `deploy`).
  Filtro por nombre, unidad y estado en `/Enfermeria/Ordinarios`, que completa la historia 1
  de Enfermería. El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 194 y 17) en la 3.ª vuelta seguida; las dos primeras (la primera tras compilar)
      dieron tiempos de espera de conexión, el patrón de «Suite lenta» (SQL Server responde en 0,1 s desde
      `sqlcmd` y las pruebas aisladas pasan);
    - vueltas tipo CI con BD nueva: la primera (52 fallos por tiempos de espera) y las dos siguientes limpias;
    - curl con `dev-integrado-enfermeria`: sin filtro, nombre («DOS»), estado, valores mal formados ignorados,
      «Ningún evento coincide con la búsqueda.» y «Limpiar»; `dev-integrado-medicina` recibe «No se puede acceder
      a esta operación».

- **Eventos abiertos en la ficha del residente (ENF-18, MED-20; sin script):** hecho el 2026-09-30 y desplegado
  en Azure (push de `685b683`, run 36743950450 en verde con `build-and-test` y `deploy`). En Azure, Residente
  Integrado Dos muestra en las dos fichas el escalado de prueba «En valoración médica» (su «Ver detalle» abre en
  Medicina) y Residente Integrado Uno, «Este residente no tiene eventos abiertos.». Las fichas de Enfermería y
  Medicina muestran los eventos abiertos que ve cada perfil, con su estado y «Ver detalle» (parcial
  `Shared/_EventosAbiertos`). El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 194 y 14) y 3 vueltas tipo CI con BD nueva;
    - curl en local:
      - Enfermería ve en Residente Integrado Uno el cambio prioritario de Auxiliar («Dolor / malestar») y en
        Residente Integrado Dos el evento propio de Medicina en valoración médica y el suyo pendiente;
      - Medicina ve en Residente Integrado Dos solo su evento propio, y en Residente Integrado Uno «Este
        residente no tiene eventos abiertos.»;
      - cada «Ver detalle» abre `DetalleCambio` o `Escalado`.
- **Escalados abiertos en Enfermería (sin script):** hecho el 2026-09-30 y desplegado en Azure (push de `3f241d6`,
  run 36733282186 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure (2026-09-30):** sin escalados, la tarjeta marca 0 y la lista dice «No hay escalados
    abiertos.». Con `dev-integrado-medicina` la lista da «No se puede acceder a esta operación». Después,
    `dev-integrado-enfermeria` registró, valoró y escaló «Prueba manual escalados abiertos: tos productiva.» sobre
    Residente Integrado Dos. La tarjeta pasó a 1 y la lista lo mostró con «Escalado por ti», el motivo y la fecha.
    Al empezar Medicina su valoración, pasó a «En valoración médica». Se deja abierto en Azure como ejemplo.
  - **Qué hace:** tarjeta «Escalados a Medicina» en el inicio de Enfermería y lista `/Enfermeria/Escalados` con
    los eventos que la Enfermería de sus unidades escaló y siguen abiertos en Medicina, marcando «Escalado por
    ti». El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 193 y 14) y 3 vueltas tipo CI con BD nueva;
    - curl en local sobre Residente Integrado Dos, con el evento «Prueba manual escalados abiertos: tos
      productiva.», que queda cerrado en la base local:
      - `dev-integrado-enfermeria` lo registra, lo valora y lo escala. La tarjeta pasa a 1 y la lista lo
        muestra «Escalado a Medicina», con «Escalado por ti», el motivo, la unidad y la fecha;
      - `dev-integrado-medicina` recibe «No se puede acceder a esta operación» en `/Enfermeria/Escalados`;
      - al empezar la valoración médica la lista muestra «En valoración médica»;
      - al cerrarlo Medicina, la tarjeta vuelve a 0, la lista dice «No hay escalados abiertos.» y el evento
        aparece en el Historial.
- **Buscador de las listas de residentes (ENF-17, MED-19; sin script):** hecho el 2026-09-30 y desplegado en
  Azure (push de `e6720f2`, run 36722073738 en verde con `build-and-test` y `deploy`). En Azure, con las cuentas
  integradas de Enfermería y Medicina: «uno», «DÓS» (sin acentos ni mayúsculas, con ICU en Linux), estado basal
  y una búsqueda sin coincidencias. Buscar por nombre (sin mayúsculas ni acentos) y filtrar por estado basal y
  unidad, al pulsar «Buscar», en Enfermería y Medicina (parcial `Shared/_FiltroResidentes`). El detalle está en
  `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 192 y 14) y 3 vueltas tipo CI con BD nueva;
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina`: nombre en mayúsculas y con acentos
      («fictÍCIO Dos»), estado basal, unidad por URL, «N de M residentes», «Ningún residente coincide»,
      «Limpiar», valores mal formados ignorados, y sin ámbito vuelve a la búsqueda tras elegirlo. Los dos
      residentes integrados comparten unidad, así que el desplegable de unidad no aparece en local: se
      prueba en `ResidentListFilterTests`.
- **Historia 8 de Medicina: gestionar el basal (sin script; cambia el seed):** hecho el 2026-09-30 y
  desplegado en Azure (push de `6131d66`, run 36717481881 en verde con `build-and-test` y `deploy`, incluido
  «Aplicar esquema y seed en Azure SQL»).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-medicina`: la ficha de los dos residentes integrados
    ofrece «Crear borrador de basal», y el módulo del basal abre con las migas de Medicina. No se creó
    ningún borrador, para no tocar el recorrido guiado de CJ, que parte de un Residente Integrado Uno sin
    basal.
  - **Qué hace:** con permiso de basal, la ficha de Medicina ofrece «Crear borrador de basal» o «Iniciar
    reevaluación», que abren el módulo común `EnfermeriaBasal`. Sus migas y redirecciones siguen al perfil
    activo. `dev-integrado-medicina` recibe los dos permisos en el seed del escenario integrado. El detalle
    y las decisiones están en `pendientes-medicina.md` (historia 8).
  - **Verificación:**
    - suite local en verde (121, 192 y 11);
    - vueltas tipo CI con BD nueva: la primera dio 3 fallos en tests no relacionados y tardó 1 min 13 s (se
      solapó con la aplicación del seed a la base local); las tres siguientes, limpias en unos 10 s;
    - el seed, aplicado dos veces a una BD temporal, deja cada permiso una sola vez;
    - curl con `dev-integrado-medicina` sobre Residente Integrado Dos: botón en la ficha, migas de Medicina,
      borrador de alta, nueve áreas, Barthel, confirmación y firma (versión 1, firmada por Medicina, que queda
      en la base local);
    - con los permisos revocados a mano, el botón desaparece y crear el borrador da «No se puede acceder a
      esta operación». Al reaplicar el seed se vuelven a conceder;
    - Enfermería conserva sus migas, y un residente ajeno vuelve a la lista del perfil activo.
- **Contenido de una versión del basal (historia 11 de Enfermería, 9 de Medicina; sin script):** hecho el
  2026-09-30 y desplegado en Azure (push de `7669c20`, run 36715209771 en verde con `build-and-test` y
  `deploy`). En Azure no hay basal firmado: se comprobó que una versión inexistente vuelve al Historial, en
  Enfermería y Medicina, y que los eventos cerrados sin basal no muestran «Ver ese basal».
  - **Qué hace:** «Ver versión» en el Historial y «Ver ese basal» en el detalle de un evento cerrado abren
    `VersionBasal` (Enfermería y Medicina, vista común `Shared/VersionBasal`): cabecera, fuente y fecha de la
    información, las nueve áreas y el Barthel por ítems. Los ítems del Barthel tienen nombre en español, que
    usa también su formulario. El detalle está en `pendientes-enfermeria.md` (historia 11, bloque 4).
  - **Verificación:**
    - suite local en verde (121, 190 y 11) y 3 vueltas tipo CI con BD nueva;
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina` sobre Residente Integrado Uno: la versión 1
      con sus nueve áreas y los diez ítems; «Ver ese basal» en los 9 eventos cerrados de Enfermería y en los
      de Medicina; una versión inexistente vuelve al Historial y un residente ajeno a Residentes;
    - el formulario del Barthel con los nombres en español, sobre un borrador de prueba que después se
      canceló.
- **Nombre de campo en el resumen del basal (sin script):** hecho el 2026-09-30 y desplegado en Azure (push
  de `85b43a4`, run 36712405469 en verde con `build-and-test` y `deploy`). En Azure ningún residente del
  escenario integrado tiene basal firmado, así que el resumen con áreas solo se probó en local.
  - **Qué cambia:** `BaselineAreaDisplay.Summarize` da una línea «Campo: valores» por cada campo con dato,
    con el nombre en español de las 35 propiedades de las nueve respuestas de área (`[Display]` en
    `Domain/Baseline/Answers`). Las listas se unen con comas. El informe de derivación escribe cada área como
    «Comunicación — Comprensión: …; Expresión: …».
  - **Test:** `BaselineAreaDisplayTests` comprueba que todo campo de respuesta tiene nombre.
  - **Verificación:**
    - suite local y 3 vueltas tipo CI en verde (una cuarta dio 5 fallos por lentitud y se repitió limpia);
    - curl de `Auxiliar/Basal`.
- **Resumen y etiquetas del basal (sin script):** hecho el 2026-09-30 y desplegado en Azure (push de
  `b8e378d`, run 36710274311 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure:** con `dev-integrado-enfermeria`, los desplegables de motivo y fuente del basal salen en
    español. `Auxiliar/Basal` responde sin nombres de tipo, pero en Azure Residente Integrado Uno no tiene
    basal firmado, así que no hay áreas que ver: el resumen con áreas solo se probó en local.
  - **Qué cambia:**
    - `BaselineAreaDisplay.Summarize` despliega las respuestas de varias opciones, que antes escribían el
      nombre del tipo de lista;
    - los 151 valores de los catálogos del basal, más `BaselineReason`, llevan `[Display(Name)]` en
      español, transcripción literal de su código;
    - lo usan los `<select>` y las casillas del formulario del basal, la fuente de información, los
      resúmenes y el informe de derivación, que deja su propio resumen y usa el común.
  - **Test:** `BaselineAreaDisplayTests` (funcional) comprueba que todo valor de catálogo tiene etiqueta.
  - **Verificación:**
    - suite local en verde y 3 vueltas tipo CI;
    - curl de `Auxiliar/Basal`, del formulario del basal (7 áreas) y de `Confirmar` con un área de varias
      opciones, sobre un borrador de prueba que después se canceló.

    La primera vuelta local falló por tiempos de espera: SQL Server tenía 216 MB en memoria y tardaba 5 s en
    una consulta trivial (ver «Suite lenta»). Las siguientes pasaron.
- **Parte médica en el detalle de Enfermería (sin script):** hecho el 2026-09-30 y desplegado en Azure
  (push de `ae45d44`, run 36706361068 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-enfermeria`:
    - el evento propio de Medicina de Residente Integrado Uno muestra la valoración médica, las indicaciones
      y «Ir a Indicaciones»;
    - sus dos eventos sin escalar no muestran nada nuevo.

    En Azure no hay ningún escalado para esos residentes (Residente Integrado Dos no tiene eventos), así que
    las tres tarjetas de un escalado solo se probaron en local.
  - **Qué hace:** el detalle de un evento escalado, o de uno propio de Medicina con indicaciones, muestra en
    solo lectura la valoración médica, las indicaciones y el seguimiento médico, con «Ir a Indicaciones» si
    queda alguna pendiente. El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 189 y 7);
    - 3 vueltas tipo CI con BD nueva;
    - curl con `dev-integrado-enfermeria` sobre los 10 eventos de Residente Integrado Uno: tarjetas en los 3
      escalados con valoración médica y en el evento propio de Medicina, y ninguna en los 6 sin escalar.
- **Historial, bloque 3: corrección y rectificación (COR-01/COR-02, script `0020`):** hecho el 2026-09-30 y
  desplegado en Azure (push de `8554dd2`, run 36699847694 en verde con `build-and-test` y `deploy`, incluido
  «Aplicar esquema y seed en Azure SQL»).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-enfermeria`, sobre Residente Integrado Uno:
    - los dos eventos cerrados antiguos ofrecen «Añadir rectificación»;
    - una rectificación añadida se ve en el detalle y en la línea temporal, y su reenvío da conflicto;
    - «Corregir valoración» fuera de la ventana vuelve al detalle.

    Con `dev-integrado-medicina`: su evento propio ofrece «Añadir rectificación», y el evento no escalado no
    se abre. En Azure no se probó una corrección dentro de la ventana, para no crear un evento solo para
    eso; sí se probó en local.
  - **Política provisional del usuario:** el prototipo no concreta los «objetos habilitados» y `roadmap.md`
    deja la política al centro y al responsable de protección de datos.
    - Solo se corrigen las valoraciones de Enfermería y médica, y solo su autor.
    - Dentro de 6 h (`Correccion:VentanaHoras`), versión corregida con motivo.
    - Después, rectificación añadida.

    El detalle y las suposiciones aprobadas están en `pendientes-enfermeria.md` (historia 11, bloque 3).
  - **Verificación:**
    - suite local en verde 3 veces (121 unitarios, 189 de integración y 7 funcionales);
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con seed (21 scripts, sin errores) ya borrada;
    - curl con `dev-integrado-enfermeria` sobre Residente Integrado Uno:
      - registro, valoración y cierre de un evento, y corrección dentro de la ventana, con la hora límite, el
        aviso y el motivo;
      - el reenvío del formulario da conflicto;
      - la línea temporal muestra la versión original y la corrección;
      - rectificación de un evento antiguo (sin motivo se rechaza).
    - curl con `dev-integrado-medicina`: rectificación de la valoración médica; ve también la de Enfermería.
      `dev-integrado-auxiliar` no entra en ninguna de las cuatro pantallas.
  - Copia previa: `ResidApp-antes-0020-20260930.bak`.
- **Historial, bloque 2: línea temporal (HIS-02, sin script):** hecho el 2026-09-30 y desplegado en Azure
  (push de `429eae7`, run 36685732287 en verde con `build-and-test` y `deploy`). «Ver línea temporal» desde
  el Historial y desde el detalle de cada evento, en Enfermería y Medicina: cada evento visible con sus hitos
  y su texto, más las versiones del basal y los cambios de ubicación.
  - **Prueba en Azure (2026-09-30)**, sobre Residente Integrado Uno:
    - Enfermería ve 15 hitos, con el PDF de derivación descargable, y Medicina 5;
    - los días salen en español en Linux («martes, 29 de septiembre de 2026») y las horas en hora de España;
    - los enlaces van y vuelven entre la línea temporal y el detalle.
  - **Decisión del usuario:** por ámbito, como la matriz de permisos del prototipo. Sustituye a la del permiso
    nuevo. El detalle está en `pendientes-enfermeria.md` (historia 11, bloque 2).
  - **Verificación:**
    - suite local en verde 3 veces (113 unitarios, 184 de integración y 7 funcionales);
    - 3 vueltas tipo CI con BD nueva en verde;
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina`:
      - Residente Integrado Uno: 61 hitos para Enfermería y 38 para Medicina, con los enlaces al detalle en los
        dos sentidos;
      - Residente Integrado Dos: el informe de derivación y las llamadas, con la descarga del PDF;
      - `dev-integrado-auxiliar` no entra.
  - Antes, en el mismo día: push de `c1a0b11` (solo documentación, run 36683643083 en verde) y prueba del
    bloque 1 en Azure con los dos perfiles: Enfermería ve 3 eventos cerrados de Residente Integrado Uno y
    Medicina 1, con el contexto de su fecha (ese residente no tiene basal firmado en Azure).
- **Historial, bloque 1 (historia 11 de Enfermería, 9 de Medicina; script `0019`):** hecho el 2026-09-30 y
  desplegado en Azure: push de `5deb007`, run 36679533522 en verde con `build-and-test` y `deploy` (incluido
  «Aplicar esquema y seed en Azure SQL», con el relleno de la instantánea). El detalle y las decisiones están
  en `pendientes-enfermeria.md` (historia 11, bloque 1).
  - «Ver historial» desde la ficha, en Enfermería y Medicina: eventos cerrados con el basal y la ubicación de
    su fecha (HIS-01, HIS-03) y versiones firmadas del basal (ENF-24).
  - **Verificación:**
    - suite local (113 unitarios, 183 de integración y 7 funcionales) en verde en 6 de 8 vueltas; las 4
      últimas, seguidas y en 5-10 s. Ver la lección «Suite lenta o con tiempos de espera» por las 2 que
      fallaron;
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con seed (20 scripts, sin errores) ya borrada;
    - relleno en local: 4986 eventos, todos con su instantánea (10 con versión de basal);
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina` sobre Residente Integrado Uno (8 y 4
      eventos cerrados, basal versión 1) y Dos (sin basal); detalle cerrado con el basal de su fecha y
      detalle abierto con el vigente; `dev-integrado-auxiliar` no entra.
  - Copia previa: `ResidApp-antes-0019-20260930.bak`.
- **Avisos tras el evento propio de Medicina (script `0018`):** resueltos el 2026-09-29 y desplegados en
  Azure. Push de `5bb39c6`: el run 36630575234 terminó en verde, con `build-and-test` y `deploy` (incluido
  «Aplicar esquema y seed en Azure SQL»), así que `0018` está aplicado en Azure SQL.
  - La ficha del residente de Enfermería ya no dice que la bandeja y la valoración «llegan en un grupo
    posterior».
  - `0018_evento_clinico_perfil_origen` impone en la BD que el perfil de `eventos_clinicos` coincida con el
    origen de `eventos_asistenciales`:
    - columna calculada persistida `evento_clinico_perfil`;
    - clave foránea compuesta `FK_ea_evento_clinico_perfil` sobre `UX_ec_perfil`;
    - vale NULL en `CAMBIO_AUXILIAR`, así que ahí no se comprueba;
    - la prueba el test `OrigenDistintoDelPerfilQueRegistra_LoRechazaLaClaveForanea`, que falla si se quita la
      clave.
  - El inicio (`Home/Index`) muestra solo las fichas del perfil activo, leído de la cookie, y su título es
    «Inicio». **Decisión del usuario:** filtrar solo por perfil, sin consultar permisos, porque el servidor ya
    deniega. Familiar ve «Tu perfil todavía no tiene pantallas disponibles». Nueva frase en el Manual
    (`#entrar-ambito`).
  - **Verificación:**
    - suite local en verde 3 veces (113 unitarios, 178 de integración y 7 funcionales);
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con seed (19 scripts, sin errores) ya borrada;
    - curl del inicio con `dev-integrado-*`, `dev-admin`, `dev-multi` (Familiar) y sin identidad;
    - registro de un evento de Enfermería y otro de Medicina, con «Prueba manual 0018 …: se puede ignorar»,
      sobre el mismo residente de la base local.
  - Copia previa: `ResidApp-antes-0018-20260929.bak`.
- **Evento propio de Medicina (historia 7 de Medicina, script `0017`):** hecho el 2026-09-29 y desplegado en
  Azure; hasta `0017` está aplicado en Azure SQL. El detalle y las decisiones están en
  `pendientes-medicina.md`. Verificación:
  - suite local en verde 3 veces (113 unitarios, 176 de integración y 7 funcionales);
  - 3 vueltas tipo CI con BD nueva en verde;
  - BD temporal con seed (18 scripts, sin errores) ya borrada;
  - prueba manual con curl:
    - `dev-integrado-medicina`: residentes, ficha, registro con tildes, POST repetido con el mismo
      `OperacionId` (mismo evento, una sola fila), valoración, bandeja, detalle e indicación;
    - `dev-integrado-enfermeria`: ve la indicación y el detalle («Es un evento propio de Medicina…»), y el
      evento no está en sus bandejas;
    - cierre por Medicina («Cerrado por Medicina» para Enfermería).
  - Evento de prueba en la base local: «Prueba manual evento propio Medicina: soplo sistólico no conocido.»,
    sobre Residente Integrado Uno, cerrado sin comunicar y con una indicación pendiente de lectura.
  - **Desplegado en Azure:** push de `eccc69c`; el run 36621253595 terminó en verde, con `build-and-test` y
    `deploy`.
  - **Prueba en Azure (2026-09-29),** sobre «Residente Integrado Uno (ficticio)», con los textos «Prueba
    técnica, se puede ignorar»:
    - con `dev-integrado-medicina`: registro (sale como «observado por Medicina el 29/09/2026 21:50», en hora
      de España), valoración e indicación;
    - con `dev-integrado-enfermeria`: ve la indicación, abre el detalle («Es un evento propio de
      Medicina…») y el evento no está en sus bandejas;
    - cierre por Medicina sin comunicar: Enfermería ve «Cerrado por Medicina» y la indicación sigue en su
      bandeja.
  - Copia previa a `0017`:
    `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0017-20260929.bak`.
- El script `0016` (bloque 2 de la historia 6, derivación a Urgencias en Enfermería y Medicina) se
  desplegó en Azure antes que `0017`: el pipeline de `ca855f2` (run 36607704572) terminó en verde el 2026-09-29, con PDFs
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
- CJ no ha completado nada nuevo (comprobado también en GitHub el 2026-10-01): `decisiones-direccion-basal-derivacion.html`
  sigue con sus 7 respuestas «por definir», y
  `docs/pendientes-cj/archivados/rangos-referencia-constantes.html` sigue con 19 huecos «por definir» (14 celdas de la
  tabla y 5 respuestas; antes se contaban mal como 17). El 2026-10-01 se añadió
  `docs/pendientes-cj/traslado-y-baja-residente.html` (6 respuestas).
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1 a 10
  y 11 (salvo la lectura de Dirección Clínica, que es de su vertical), más los rangos de
  referencia de constantes (fase 1 y su pantalla). Medicina está en curso: historias 1 a 8 (escalados,
  valoración médica, indicaciones, cierre médico, seguimiento médico con continuidad entre turnos, protocolo
  urgente, derivación a Urgencias, evento propio y basal con permiso) y 9 (con la misma salvedad que la 11).
- La base local `ResidApp` tiene los scripts `0001` a `0027` registrados en `dbo.scripts_aplicados`
  (en Azure, hasta `0027`). Hay copias previas a `0016` … `0027` en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\` (`ResidApp-antes-0016-20260929.bak`,
  `ResidApp-antes-0017-20260929.bak`, `ResidApp-antes-0018-20260929.bak`, `ResidApp-antes-0019-20260930.bak`,
  `ResidApp-antes-0020-20260930.bak`, `ResidApp-antes-0021-20261001.bak`, `ResidApp-antes-0022-20261001.bak`,
  `ResidApp-antes-0023-20261001.bak`, `ResidApp-antes-0024-20261001.bak`, `ResidApp-antes-0025-20261001.bak`, `ResidApp-antes-0026-20261001.bak` y `ResidApp-antes-0027-20261001.bak`). También
  tiene la cuenta `dev-integrado-administracion`, el residente de prueba «Prueba Corrección Identidad (ficticio)», con una
  corrección, «Prueba selector unidad (ficticio)» y «Prueba familiares (ficticio)», con dos familiares (Lucía, autorización
  revocada tras recorrer todos los estados; Tomás, teléfono editado) y el contacto urgente designado, cambiado, quitado y
  vuelto a designar (Tomás); tiene el evento «Prueba manual contacto urgente: disnea.», en protocolo urgente, derivado, con
  una llamada «Contactado» a Tomás, sin cerrar. Y la cuenta `dev-prueba-enfermeria-nueva` («Prueba Enfermería renombrada
  (ficticia)»), dada de alta desde Usuarios: Enfermería vigente en la unidad del escenario (con «Basal inicial»; «Dar de
  alta residentes» concedido y revocado) y un Auxiliar revocado que tuvo asignado a «Prueba familiares (ficticio)». Esa
  cuenta dio de alta a «Prueba alta Enfermeria (ficticio)», que tiene un borrador de basal inicial suyo sin completar.
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
  - «Prueba manual escalados abiertos: tos productiva.» (Residente Integrado Dos): escalado por Enfermería,
    valorado y cerrado por Medicina sin comunicación. Residente Integrado Dos tiene además un basal versión 1
    firmado por Medicina (historia 8).
- Suite: 232 unitarios, 276 de integración y 48 funcionales, todos en verde.
- Hay dos scripts con el número `0005` (`0005_auxiliar_opciones_rapidas.sql` y
  `0005_enfermeria_borrador_basal.sql`). Es inofensivo, porque el runner los registra por nombre completo y
  son independientes entre sí. **No los renombres:** el runner los volvería a ejecutar y el despliegue en
  Azure fallaría. Antes de crear un script, comprueba cuál es el último número.

## Siguiente tarea

0. **I0 a I4 e I2b del ADR 0007 están hechos** (I4 mergeado y comprobado en Azure dev). Orlando elige entre **I5 (pulsera NFC)**, el **dictado por voz del ADR 0009** y la
   retirada del acceso de desarrollo; antes de datos reales, retirar el acceso de desarrollo y llevar al DPO las reglas de las cuentas de varios centros
   (el límite de intentos por IP ya está corregido). Sueltos: probar Importar y Exportar (ADR 0010) en Azure dev, y las dos preguntas a CJ de la guía 2 (hora en la
   fecha prevista, prueba 2.4 de «Conducta médica»).
   Para entrar en Azure dev hace falta la clave de desarrollo: está solo en el ajuste `DevAuth__Clave` de la Web App
   (`az webapp config appsettings list -g rg-residapp-dev -n app-residapp-dev --query "[?name=='DevAuth__Clave'].value" -o tsv`); no la copies a
   ningún fichero del repositorio.
1. **Revisar las respuestas de CJ** que lleguen:
   - `docs/pendientes-cj/aclaraciones-respuestas-cj.html` (preparado el 2026-10-07, 11 respuestas): según lo que responda, las aportaciones a un borrador de basal ajeno (tema 1), la
     finalidad de calidad asistencial y qué es Coordinación Clínica (tema 3), las temperaturas seguidas (tema 2) o un cambio en «Comunicaciones» del informe (tema 4).
   - Siguen sin respuesta: `docs/pendientes-cj/traslado-y-baja-residente.html` (6 respuestas): traslado y baja del residente en Administración.
   - **Dirección, bloque 2:** solo falta la 4ª finalidad (calidad asistencial), pendiente de la respuesta de CJ (ver `pendientes-direccion.md`).
2. **Administración, bloques siguientes** (ver `pendientes-administracion.md`): el organigrama y los cargos (ADM-07; ningún documento los define: pregunta a CJ); después publicaciones,
   citas, auditoría administrativa y panel.
3. **Dirección, bloque 3** (derivaciones y comunicación familiar en solo lectura): necesita la publicación familiar
   (Administración y Familia). La revisión de calidad de proceso (DIR-11) necesita que CJ defina los hitos y plazos.
4. **Familia / Portal Familiar**, que depende de Administración y del proveedor de identidad.

## Avisos abiertos (fuera de alcance, sin corregir)

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
    `ubuntu-26.04` y «Instalar sqlcmd» usa el repositorio de Microsoft de Ubuntu 26.04 (`.../config/ubuntu/26.04/prod.list`).
    El CI de `main` pasó entero, incluido el despliegue. Si falla «Instalar sqlcmd», mira primero ese repositorio.
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

## Reglas de trabajo propias de este repositorio

- **Push:** siempre con confirmación explícita del usuario. El push a `main` aplica los scripts nuevos
  y el seed en Azure SQL.
- **Rama y PR (decidido el 2026-10-06):** todo cambio con un script de migración en `database/scripts/` va en una rama y se mergea con PR,
  para que el CI lo pruebe antes de `main` (los scripts son inmutables una vez aplicados en Azure). Los cambios sin migración
  (documentación, tests, ajustes de código) pueden ir directos a `main`, salvo que sean grandes o arriesgados. El despliegue ya depende de que
  pasen los tests, así que un cambio roto no llega a Azure en ningún caso; el PR evita dejar un commit en rojo en `main`.
- **Esquema:** siempre un script nuevo numerado; nunca se edita uno ya aplicado en Azure.
- **Manual:** actualiza `Views/Home/Manual.cshtml` cuando cambie una pantalla que describa o se construya
  un módulo marcado como «Próximamente».
- **Flujo de trabajo ilustrado:** revisa `wwwroot/flujo-de-trabajo.html` cuando cambie el circuito, un perfil o una pantalla
  que aparezca en él, o se construya algo de su recuadro «Lo que todavía no está».
- **Interfaz:** siempre en español, incluidos los enums que aparecen en un `<select>`. Sigue
  `docs/bocetos-pantallas/guia-diseno-sistema-visual.md` (Bootstrap 5, WCAG AA, `btn-lg`, nada de texto
  blanco sobre amarillo, ningún color fijo en las vistas) y revisa cada pantalla en claro y en oscuro.
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
- **Capturas con sesión para revisar una pantalla:** `puppeteer-core` (en el directorio temporal, con el Chrome instalado) inicia
  sesión rellenando `/DevAuth/Login`, emula `prefers-color-scheme` y, con `isMobile` y `hasTouch`, también `any-pointer: coarse`.
  En Git Bash, un argumento que empiece por `/` se convierte en una ruta de Windows: exporta `MSYS_NO_PATHCONV=1`.

- **Enums en español en pantalla:** un enum con `[Code("X_Y")]` que el formulario envía como texto se pinta con
  `EnumDisplay.Label(valor)` (necesita `[Display(Name=…)]` en español) y `value="@valor.ToCode()"`; no sirve
  `GetEnumSelectList` porque envía el número. Nunca imprimas `@x.AlgoCode` ni `ToString()` de un enum: sale el nombre
  del miembro o el código en inglés. Revisado el 2026-10-02: Dirección Clínica (tipo de recurso y propósito) y el
  motivo del basal ya cumplen.
- **PDFsharp/MigraDoc no admite generar documentos a la vez:** comparten estado de fuentes y un título puede salir con una
  palabra en la fuente equivocada. Cualquier renderizador nuevo debe serializar el renderizado como
  `ReferralReportPdfRenderer`. Para revisar un PDF sin abrirlo, compara el contenido de su página
  (`PdfReader.Open(...).Pages[0].Contents`, `UnfilteredValue`): el texto va con la fuente incrustada y no se busca como
  texto plano.
- **Identidad de marca en PDF e impresión** (obligatoria, ver sección 4 de `guia-diseno-sistema-visual.md`): todo PDF
  nuevo de servidor llama a `PdfBranding.AddHeader(section)`. Las impresiones del navegador ya llevan el encabezado
  del `_Layout`, y ninguna vista debe ocultarlo al imprimir. Se repite en cada página porque, al imprimir, el contenedor
  es una tabla CSS y el encabezado su `table-header-group`. Sin `break-inside: avoid`, Chrome no lo repite (comprobado
  imprimiendo Indicadores en dos páginas).
- **Imágenes en el PDF:** PDFsharp no importa SVG. El icono del encabezado es `wwwroot\images\logo.svg` rasterizado a
  PNG de 256 px (`Infrastructure\Pdf\Images\logo.png`, generado con Chrome headless `--screenshot` y
  `--default-background-color=00000000`), incrustado como recurso y pasado a MigraDoc con el prefijo `base64:`, sin
  ficheros temporales. Si cambia el SVG, hay que regenerar el PNG. Para ver un PDF como imagen sin poppler, usa
  `Windows.Data.Pdf` desde Windows PowerShell 5.1; Chrome headless no pinta su visor de PDF.
- **Probar una impresión con sesión en local:** inicia sesión con curl (`/DevAuth/Login` con el token antiforgery;
  `/ProfileScope/Select` elige solo el ámbito único) e imprime con Chrome por DevTools (`Network.setCookie` +
  `Page.printToPDF`; Node 20 necesita `--experimental-websocket`). En local, la app devuelve 0 bytes en los estáticos
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
  (`docs/legado-cloudflare/docs/product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md`). En el
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
