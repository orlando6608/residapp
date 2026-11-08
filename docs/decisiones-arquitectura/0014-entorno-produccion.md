# ADR 0014 — Entorno de producción de ResidApp: recursos, despliegue, seguridad, copias de seguridad, rendimiento y repositorio privado

Estado: **propuesta** (2026-11-08, solo documentación; no se ha creado nada en Azure ni en GitHub). **Decidido por Orlando el 2026-11-08:** producción en una **suscripción de Azure nueva
y propia de ResidApp**; **privatizar el repositorio antes** (adelanta el disparador del PR #185) y **transferir la propiedad del repositorio a una organización de GitHub** (nombre y
titular por decidir). **Aplazado por Orlando (2026-11-08): este plan queda en pausa** hasta que (1) se decida pasar el repositorio a privado y, más adelante, (2) exista el **nombre de la
organización y la sociedad titular**, que será la dueña del repositorio y de la cuenta y la suscripción de Azure como activos intangibles de la sociedad. Hasta entonces el ADR es la
referencia de diseño y no se ejecuta ningún paso. Todo lo demás está sin decidir salvo lo marcado como **[Hecho]**. Lectura de protección de datos previa, que este ADR recoge: `docs/privacidad/entorno-produccion-lectura-dpo.md` (borrador
del agente `dpo-residapp`; lo valida una persona antes de usarlo con datos reales). Sin ADR previo de entornos, de copias de seguridad ni de escalado: lo más cercano es
`docs/producto/roadmap.md` (línea 48: «SLA, RPO, RTO y copias de seguridad: pendiente de definir antes de producción»).

Etiquetas: **[Hecho]** verificado en el repositorio; **[Propuesta]** lo que recomienda este ADR; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]**
no se ha podido confirmar en una fuente oficial (no hay acceso a Azure desde la redacción: ningún precio, límite ni disponibilidad de región de este documento es una medida).

## Contexto

Hoy solo existe **desarrollo**: la Web App `app-residapp-dev` (servida también como `dev.residapp.es`), el servidor `sql-residapp-dev` con la base `sqldb-residapp-dev` (Basic, 5 DTU),
todo en `rg-residapp-dev`, región Spain Central, con datos ficticios. **[Hecho]** El prototipo de producción que se decidió es `app.residapp.es` (ADR 0007, punto 5), que
no existe: su DNS se crea solo cuando exista el recurso.

Lo que hay que resolver antes del primer centro real, con lo que ya se sabe:

- **No hay ninguna definición de producción**: ni scripts ni plantillas de creación de recursos (el repositorio no tiene nada de infraestructura como código), ni entorno de despliegue propio.
  `ci-cd.yml` y `sql-dev-nivel.yml` (de los cuatro workflows de `.github/workflows/`) tienen los nombres de dev escritos a mano; el único trabajo de despliegue se llama **`production`** en GitHub pero despliega **dev**, y aplica
  los datos ficticios (`APLICAR_SEED=1`) en cada despliegue (`ci-cd.yml`).
- **Hallazgos de seguridad abiertos que condicionan el diseño**: A4 (no hay registros técnicos), B6 (copias de 7 días, sin retención larga ni bloqueo de borrado), B8 (claves de
  Data Protection por defecto, válidas con una sola instancia), B9 (suscripción compartida con otros proyectos), B7 (repositorio público), M4 pasos 4 a 6 (identidad gestionada).
- **Lo que se rompe con más de una instancia de la Web App** (`Program.cs`): las claves de Data Protection viven en el almacenamiento de App Service, así que un *slot* o una
  segunda instancia dejaría sin sesión a todos; los límites de intentos (`AccessFailureThrottle`, `RateLimiter`, líneas 175 a 193) están en memoria y se multiplicarían; el
  firewall de SQL depende de las IP de salida de la Web App (31 reglas `webapp-salida-*`), que pueden cambiar al escalar; y el despliegue con `clean: true` deja unos segundos
  la web a medias (el propio `ci-cd.yml` ya dice que con usuarios reales hay que repensarlo).
- **No hay estado local**: ninguna foto, logo ni PDF se escribe en disco (van a SQL), ni hay caché distribuida, SignalR ni sesión de servidor. Cada petición valida la sesión contra
  SQL, así que la **latencia entre la web y la base** pesa más que la CPU.
- **Los trabajos en segundo plano** (avisos de cambio de cita, purga de correos, cierre de avisos sin resolver) son servicios dentro de la propia Web App y piden «Siempre activo»
  (ADR 0012). Son seguros con varias instancias, pero cada una consulta la base.
- **Producción heredaría hoy los documentos de CJ**: `ResidApp.Web.csproj` (líneas 10 a 13) empaqueta `docs/pendientes-cj/**`, incluidos los `.respuestas.json` (uno lleva cabeceras de
  un correo personal de Orlando), y `Program.cs` (líneas 435 a 465) los sirve en `/pendientes-cj` tras una clave, `CJ123`, que está en `appsettings.json` (B3).

- **El repositorio es hoy personal y público** (`orlando6608/residapp`, id de repositorio `1366636161`, id de propietario `1379872`). Las credenciales de Azure del despliegue
  usan identificadores inmutables en su sujeto (`repo:orlando6608@1379872/residapp@1366636161:…`): **no dependen de la visibilidad, pero sí del propietario**, así que privatizar no las
  rompe y **transferir el repositorio sí** (el id del repositorio se conserva; el del propietario pasa a ser el de la organización).

## Decisión propuesta

### 0. Dos puertas **[Propuesta, de la lectura del DPO]**

El ADR separa dos momentos, porque lo que se puede hacer con un entorno **vacío** no es lo que hace falta para **cargar datos reales**:

- **Puerta 1 — crear el entorno vacío** (sin datos reales): suscripción propia, región, política de regiones permitidas y bloqueo contra borrado; identidades de administración
  separadas y cuenta de emergencia; almacén de registros propio con telemetría filtrada; trabajo de despliegue de producción separado y sin datos ficticios, con entorno y credenciales
  propios; `/pendientes-cj` fuera del paquete; **IA y correo apagados**; `app.residapp.es` con su registro `TXT` de verificación y el DNS creado después del recurso.
- **Puerta 2 — cargar datos reales**: todo lo de `docs/privacidad/antes-de-datos-reales.md` más la lista del apartado 11 de la lectura del DPO (persona experta contratada, política de
  copias aprobada con su cifra, primer simulacro de restauración, registro externo de supresiones, plazos de acceso y auditoría decididos, protocolo de brechas con plazo de aviso,
  contrato de encargado con la lista de servicios y ubicaciones, EIPD, segunda persona para lo irreversible, y el repositorio ya transferido a la organización y privado: decidido).

### 1. Aislamiento y recursos

- **Suscripción de Azure nueva y propia de ResidApp** (decidido el 2026-11-08; cierra B9), **a nombre de la sociedad titular** (facturación, propiedad y propietarios de la suscripción), no de
  una persona, como activo intangible de la sociedad que también será dueña del repositorio. Por eso no se crea hasta que exista la sociedad (ver Estado). **[Decidir]** si cuelga del mismo inquilino de Entra que dev y otros proyectos: los
  administradores globales de ese inquilino alcanzarían producción, de modo que B9 cambiaría de sitio y no desaparecería del todo.
- **`rg-residapp-prod`** en la región elegida. **[A verificar]** la disponibilidad de zonas de disponibilidad y de región emparejada antes de crear nada: una búsqueda indica que
  Spain Central no tiene región par, lo que haría que la copia geográfica por defecto no valga allí (apartado 5).
- Bloqueo `CanNotDelete` en el grupo y en el servidor SQL (B6); etiquetas de propietario y entorno; **política de Azure de regiones permitidas (solo UE/EEE)** en la suscripción,
  que impide también una restauración geográfica fuera de la UE; presupuesto y alertas de gasto propios (en dev son `PresupuestoResidApp` y `AlertaResidApp20`).
- Recursos (nombres propuestos): plan `asp-residapp-prod` y Web App `app-residapp-prod`; servidor `sql-residapp-prod` y base `sqldb-residapp-prod`; `kv-residapp-prod` (Key Vault);
  `law-residapp-prod` y `appi-residapp-prod` (registros y medición); almacenamiento de auditoría `stresidappauditprod`; `acs-residapp-prod` y `ecs-residapp-prod` (correo).
  Ninguno se comparte con dev ni con otros proyectos.
- **Infraestructura como código [Propuesta]**: plantillas Bicep en una carpeta `infra/` con `az deployment … what-if` en cada PR, para poder recrear el entorno y revisar cada cambio
  sin depender de clics en el portal. Hoy dev se montó a mano y su auditoría figura como «sin guion ni IaC».

### 2. Dominio, certificado y correo

- `app.residapp.es`: CNAME en CDmon hacia la Web App y registro `TXT` `asuid.app` (obligatorio, no opcional); certificado **administrado por nombre, sin comodín**; HSTS **sin**
  `includeSubDomains` ni `preload`; sin `Domain` en las cookies; **DNS creado solo cuando exista el recurso y borrado antes de borrar el recurso** (CNAME colgante, ADR 0007, punto 5).
  `AllowedHosts` ha de ser `app.residapp.es` (hoy `*`, `appsettings.json`).
- `Acceso__PasskeyDominio=app.residapp.es`. Las passkeys de dev **no** se migran: cada persona recibe su invitación y registra las suyas en producción. **[Hecho]** ADR 0007.
- **Correo de producción propio**: recurso de ACS y dominio de correo de producción, con la clave en Key Vault y los registros de entrega a 30 días en el almacén de producción, en la
  UE. **Dev y producción no comparten remitente**: dev no debe poder enviar «como ResidApp» a familias reales (`Correo__Activo` ya está apagado por defecto). **[Decidir]** si producción
  usa `avisos.residapp.es` y dev otro subdominio o ningún envío; **[A verificar]** si un mismo dominio puede estar verificado a la vez en dos recursos de ACS. DMARC a `quarantine`
  tras unos días de informes, solo informes agregados (no los de fallo con copia del mensaje). **Producción arranca con correo e IA apagados** hasta cerrar sus puertas
  (ADR 0012 y ADR 0011).

### 3. Cómputo, escalabilidad y velocidad

- **Web App Linux** en un plan que permita *slots* y escalado automático (Standard o Premium v3; **[A verificar]** el escalón y su precio en la calculadora de Azure antes de elegir),
  empezando con **una instancia más un slot de preparación**. «Siempre activo» encendido (lo exigen los trabajos en segundo plano), prueba de salud, afinidad de instancia
  desactivada y HTTPS solo (`httpsOnly`, como hoy en dev). `WEBSITE_TIME_ZONE=Europe/Madrid` (sin ella la aplicación no arranca en producción: `Program.cs`, `ServerTimeZone.Verify`).
- **Primero escalado vertical; el horizontal solo después de cerrar lo que se rompe con 2 o más instancias:**
  1. **Data Protection con claves compartidas** (B8): almacenarlas en Blob Storage y cifrarlas con una clave de Key Vault, accediendo con identidad gestionada. **Obligatorio antes de usar
     slots**: sin esto, cada swap invalida las cookies de sesión. *Cambio de código pequeño.* Condición: si algún día la clave TOTP se cifra con Data Protection, perder esas claves sería
     perder datos, y entonces pasan a necesitar copia y recuperación como un dato más (ADR 0007).
  2. **Límites de intentos en memoria**: con N instancias el tope efectivo es N veces mayor. Aceptable con una; con más, moverlos a SQL o a una caché compartida (cuenta como cambio de seguridad).
  3. **Trabajos en segundo plano**: con más de una instancia, pasarlos a una Function o WebJob como ya prevé el ADR 0012 (no hay daño funcional, pero cada instancia consulta la base).
- **Velocidad**: poner web y base en la misma región (y la misma zona si se elige redundancia de zona); conexiones agrupadas (ya lo hace el cliente SQL); activar la compresión de
  respuestas y, si hace falta, caché de salida en las páginas públicas (`/Guia`, `/Home/Manual`); ninguna medida de velocidad que meta un intermediario nuevo (apartado 6). Antes del
  primer centro, **una prueba de carga** con cifras objetivo **[Decidir con CJ: centros, personas simultáneas, picos de turno]** y con SQL en el escalón de producción: lo medido en local
  (501 centros, 16 a 18 ms en la pantalla de centros de Plataforma) **no** vale para Azure ni para Basic.
- **Sin Front Door ni CDN global al inicio**: ver el apartado 6.

### 4. Base de datos de producción

- **Nivel**: Azure SQL en vCore de Uso general o en DTU S2 a S3, **sin pausa automática** (un arranque en frío no es aceptable). Basic (5 DTU) ya dio esperas en dev y no vale.
  **[A verificar]** y **[Decidir]** el escalón con la prueba de carga; subir o bajar de escalón por un procedimiento manual con aprobación (nada parecido al `sql-dev-nivel.yml` nocturno,
  que se queda solo para dev).
- **Autenticación solo Entra** (sin usuario y contraseña de SQL) cuando el despliegue lo permita; administrador de Entra = un grupo y no una persona. La Web App entra con **su
  identidad gestionada desde el primer día** (como en dev tras M4); los trabajos de correo (`ResidAppTareas`) con una **identidad asignada por el usuario** (en dev se quedaron con
  contraseña por decisión de Orlando; producción nace sin ella, y esa diferencia es deliberada). **[A verificar]** compatibilidad con el cierre de avisos y con el despliegue.
- **Copias de seguridad [Propuesta, ajustada por la lectura del DPO]:**
  - **Recuperación a un punto en el tiempo a 35 días** (el máximo; 7 por defecto) y almacenamiento de copias redundante en zona. **[A verificar]** en «Automatic backups» de Microsoft Learn.
  - **Redundancia geográfica**: si Spain Central se confirma sin región par, la copia geográfica por defecto no vale y exige una decisión expresa **[Decidir]**: una segunda región de
    la UE/EEE con replicación activa o grupo de conmutación (más coste y una ubicación más en el contrato), o aceptar que un desastre de toda la región supone horas o días de corte y dejarlo
    por escrito para el centro.
  - **Retención a largo plazo y exportaciones: de retención CORTA, con una cifra que fija el DPO.** *No* se propone guardar copias o exportaciones durante 15 años. Los 15 años del plazo
    de conservación (Llei 21/2000, art. 12; script `0086`) los cumple la **base viva**: una copia anual a 15 años conservaría durante años datos ya expurgados o suprimidos de **todos los
    centros a la vez**, no permitiría borrar los de uno solo al terminar su contrato (RGPD 28.3.g) y, si se bloquea, no se podría acortar ante una orden de supresión. Se corrige así mi
    primera idea de una exportación anual inmutable. **[Decidir con el DPO]** cifra, frecuencia, y si la política va bloqueada o con retención por asunto.
  - **Restaurar deshace expurgos, anonimizaciones y revocaciones** (cuentas suspendidas, passkeys, autorizaciones de familiares, invitaciones anuladas) y acorta la auditoría inmutable. Por eso:
    restaurar **siempre en una base nueva**, comprobar, reaplicar el **registro externo de supresiones y revocaciones** (solo identificadores, fechas y quién; fuera de la base y del
    repositorio), **cerrar todas las sesiones** y avisar al centro. Texto de la política de copias para el contrato: apartado 3.2 de la lectura del DPO.
  - **Simulacro de restauración** antes de producción con datos y después cada [N] meses **[Decidir]**, dentro de la suscripción de producción, en un servidor aislado, sin leer
    historias (recuentos y claves) y con acta sin datos. Nunca en dev ni en un portátil.
- **Objetivos [Decidir con CJ y los centros]**: RPO (cuánto dato se puede perder) ≤ 15 minutos y RTO (cuánto se tarda en volver) ≤ 4 horas para un fallo dentro de la región;
  para un desastre de región, lo que se decida arriba. Cierra el «pendiente de definir» de `roadmap.md`.
- **Auditoría de SQL** activada **en el servidor** (en dev solo a nivel de base), con los grupos de **autenticación, cambios de permisos y cambios de esquema**; **no** el grupo que
  registra todas las sentencias de la cuenta de la aplicación (con consultas parametrizadas el texto puede llevar valores, y pasarían a un almacén aparte, sin seguridad por filas y con
  todos los centros mezclados). La auditoría de la cuenta de tareas que pide el ADR 0012 se limita igual.
- **Defender para SQL [Decidir]**: recomendado para producción; los avisos pueden llevar la sentencia afectada, así que van solo a Orlando y a quien cubra, sin reenviarse a canales de
  equipo. **[A verificar]** qué telemetría analiza y dónde.
- Cifrado en reposo con claves de Microsoft (TDE); claves propias en Key Vault **[Decidir]**, no necesario para el piloto y con riesgo de perder la clave.
- El primer acceso de Plataforma en un entorno vacío es el `INSERT` único del ADR 0006 más `database/operaciones/invitar_cuenta.sql` (emisor `ARRANQUE`): lo hace quien administra la base.
  Los usuarios de la base (`crear_usuario_identidad_azure.sql`, `crear_usuario_tarea_azure.sql`) se crean a mano con el administrador de Entra, no por el CI.

### 5. Seguridad y acceso

- **Secretos** (clave de ACS, clave de Mistral, clave de cifrado de Data Protection) en **Key Vault**, con referencias de Key Vault en los ajustes de la Web App y acceso por
  identidad y por roles; borrado suave y protección contra purga; sus registros de acceso, al almacén de producción. Key Vault y Blob **no son subencargados nuevos** (son servicios
  de Microsoft, igual que SQL y la Web App), pero sí **servicios nuevos que hay que listar** en el anexo del contrato y en el registro de actividades.
- **Acceso del personal de ResidApp** (hoy una sola persona, con rol de propietario, y sin «cuatro ojos»): nadie ve datos clínicos sin un motivo escrito (instrucción del centro o
  incidente) y un registro de intervenciones sin contenido; **identidades separadas** (una de uso diario sin permisos sobre producción y otra de administración, con passkey o llave
  física, y una cuenta de emergencia guardada fuera de línea con alerta si se usa); **sin permisos permanentes de propietario** (acceso temporal con caducidad; **[A verificar]** qué
  licencia de Entra exige PIM y, si es cara, asignar el rol solo mientras dura la tarea con alerta en cada cambio); alertas de cambio de roles, de administrador de SQL, de reglas de
  firewall y de uso de secretos; **segunda persona para lo irreversible** (expurgo, borrado de cuentas, restauración, levantar un bloqueo) o decisión escrita de que esas operaciones no se
  hacen en producción hasta entonces **[Decidir]**; sin datos de producción en equipos de trabajo ni en dev; **[A verificar]** Customer Lockbox.
- **Red**: Private Endpoint e integración con VNet para SQL, Key Vault y Blob son favorables (quitan las 31 reglas `webapp-salida-*`, la fragilidad al escalar y la exposición a Internet).
  Consecuencia: el `ci-cd.yml` actual abre una regla temporal con la IP del runner de GitHub; con SQL privado un runner de GitHub no llega. **[Propuesta]** empezar con acceso público de
  SQL en «redes seleccionadas» y solo la regla temporal del runner, y la web por Private Endpoint (**[A verificar]** que ambos conviven); un ejecutor dentro de la red sería otro
  componente con acceso privilegiado a la base de producción y se evalúa después.
- **Observabilidad** (cierra A4, antes de que haya tráfico): Application Insights y Log Analytics en la UE, **propios de producción**, solo medición **del servidor** (sin el SDK de
  JavaScript, que pondría cookies), con diagnóstico de la Web App, SQL, ACS y Key Vault y alertas por recuentos y tipos, nunca por datos. **Se puede** registrar hora, método, plantilla
  de ruta, estado, duración, tipo de excepción y código de dominio, recuentos y una identificación de correlación. **No se debe** registrar la URL con valores (hay GUID de residente, de
  vínculo y de cita en la ruta, y el **token de invitación viaja en la ruta**: `/Acceso/Invitacion/{token}`), la cadena de consulta, el mensaje de las excepciones de SQL, cuerpos,
  cabeceras `Cookie` ni nombres, correos o teléfonos. Un filtro sustituye la ruta por su plantilla; `InvalidIdentifierFilter` (que hoy escribe la ruta completa) y
  `ApplicationResultRunner.RunAsync` (que hoy traga las excepciones sin registrarlas; B10) se corrigen antes. No se activa `DisableIpMasking`. Retención: telemetría de funcionamiento la por defecto
  (30 días); registros de **seguridad** más largos, con la cifra que fije el DPO junto con el plazo de `eventos_acceso` y `sesiones` **[Decidir]**.
- **Brechas**: ResidApp es encargado; la residencia notifica a la autoridad en 72 horas desde que **ResidApp se lo comunica**, así que el contrato fija un plazo de aviso **más corto** y un
  canal por centro con suplente, y hay un protocolo y un simulacro antes de la Puerta 2 **[Decidir]** cifras y quién cubre noches y vacaciones.
- **En producción no se publica `/pendientes-cj` ni su clave** **[Propuesta]**: el mismo commit se publica dos veces, **un paquete con los documentos de CJ (dev) y otro sin ellos
  (producción)**, mediante una propiedad de publicación en `ResidApp.Web.csproj`; la ruta no se mapea si la carpeta no existe; se quita `PendientesCj:Clave` de `appsettings.json`
  (dev la pone desde un ajuste de la Web App); y una **prueba** falla si `GET /pendientes-cj/…` no da 404 con la configuración de producción. Es la alternativa fuerte a
  un simple interruptor, porque los `.respuestas.json` no deben viajar ni inactivos. Se pierde «un único paquete para los dos entornos», pero el código es idéntico.
- **Pendiente de cerrar antes de datos reales** (de `hallazgos.md`): A4, B6, B8, B10 y, con calma, M5 (acciones de GitHub fijadas por SHA).

### 6. Lo que se rechaza al inicio: Front Door, CDN global y WAF de borde

**No se usa Front Door** salvo decisión expresa de la persona experta **[Decidir]**: ADR 0007 (líneas 33 a 35) ya recoge que está **excluido del EU Data Boundary** (**[A verificar]** que sigue
vigente); termina TLS fuera de la web y vería cabeceras, cookies de sesión, códigos TOTP y aserciones de passkey, y llevaría datos clínicos; sus registros guardan la IP; y rompería la
cadena de IP de confianza (`Program.cs`, `ForwardedHeaders` con redes fijas, líneas 201 a 207 y 421): la IP guardada en `eventos_acceso` y el límite por IP serían falsos, y sin
`X-Forwarded-Proto` desaparece HSTS (incidente del 2026-10-21). Alternativas dentro de la región: restricciones de acceso de App Service, los límites que ya tiene la aplicación y,
si hace falta WAF, uno **regional** (Application Gateway) **[A verificar]** precio y disponibilidad.

### 7. Despliegue

- **Construir una vez por commit y promover**: `deploy-dev` en cada push a `main` (como hoy); **`deploy-prod` al crearse el tag `vX.Y.Z`** de release-please (Orlando fusiona el PR de
  release solo en hitos, y eso genera el tag) o por ejecución manual, con entorno de GitHub **`production`** de verdad (revisor Orlando, espera opcional). El trabajo de producción
  **no** lleva `APLICAR_SEED`: **control técnico [Propuesta]** que `aplicar-scripts.sh` se niegue a aplicar los datos ficticios si el servidor o la base no son de desarrollo, y no
  basta el comentario de la línea 169 de `ci-cd.yml`.
- **Despliegue sin corte**: al *slot* de preparación → **pruebas de humo** (la prueba de salud, y `POST /Acceso/Entrar` con un usuario falso, que debe dar 200 «incorrectos» porque toca
  la base; lección de M4: un `GET` que da 200 puede no tocarla) → **swap**. Los ajustes pegajosos al slot son las cadenas de conexión, `PasskeyDominio` y los de correo e IA.
  Prueba de salud nueva (`/salud`, anónima, mínima, que solo comprueba la base): *cambio de código pequeño*.
- **Reversión**: swap inverso para el código. La base solo avanza: los scripts SQL **no son idempotentes** y se aplican **antes** de desplegar la web, así que cada script debe ser
  compatible con la versión anterior del código (patrón expandir y contraer) y los que toman bloqueos de esquema (tipo `0083`) llevan ventana de mantenimiento documentada. Si no hay
  vuelta atrás: restauración a un punto en el tiempo en una base nueva (apartado 4).
- **Identidad de despliegue propia de producción**, con **rol a medida limitado a `rg-residapp-prod`** (escritura en la Web App y `Microsoft.Sql/servers/firewallRules/*` más lectura
  en el servidor); resuelve M4 paso 5 en producción desde el principio sin esperar a dev. Ningún secreto de producción accesible desde ramas ni PR.

### 8. Cambios en Git y GitHub (descritos; ninguno se ejecuta con este ADR)

1. **Parametrizar** el trabajo de despliegue con variables por entorno de GitHub (`AZURE_RG`, `AZURE_WEBAPP`, `AZURE_SQL_SERVER`, `AZURE_SQL_DB`) en lugar de los nombres escritos a mano
   (M4 paso 4, hoy opcional).
2. **Entornos**: crear **`dev`** con su credencial federada **antes** de cambiar el workflow (lección de A1: si no, el despliegue falla; el sujeto de la credencial lleva el nombre del
   entorno, así que renombrar `production` exige una credencial nueva en la aplicación de Entra `residapp-github-actions`); reservar `production` para la suscripción nueva, con credencial,
   revisor y secretos propios, y quitar a la credencial vieja los permisos sobre `rg-residapp-dev` cuando proceda. `sql-dev-nivel.yml` queda solo para dev y su credencial de la rama `main`.
   La credencial de `production` de la suscripción nueva se crea **después** de existir la organización (punto 4), para no hacerla dos veces: su sujeto lleva el identificador del propietario.
3. **Workflows**: `deploy-prod` por tag y con slot; los dos paquetes de publicación (apartado 5); un job de prueba de que la ruta de CJ da 404 en producción; `release-please` sin cambios.
4. **Transferencia a una organización y repositorio privado — decidido por Orlando el 2026-11-08.** El disparador vigente (decisión del 2026-11-07, PR #185) era «el primer cliente real o la
   primera demo a un tercero, lo que ocurra antes». Orlando lo **adelanta**: el repositorio se privatiza **antes de fusionar a `main` el trabajo de despliegue de producción y antes de firmar
   con un centro** (propuesta de la lectura del DPO: este ADR y su flujo pondrían en un repositorio público los nombres y la estructura de producción), y además **la propiedad se
   transfiere a una organización de GitHub** (nombre y titular por decidir: el titular persona física o sociedad sigue abierto, ver Puertas abiertas). Una organización resuelve también el
   acceso de CJ: en un repositorio personal GitHub no admite los roles Read ni Triage (por eso CJ salió el 2026-11-03); en una organización sí.
   - **Lo que se rompe al transferir** (el sujeto de OIDC es **[Hecho]**, comprobado con `gh api …/actions/oidc/customization/sub`; el resto **[A verificar]** y sin probar):
     - **Las dos credenciales federadas de Azure** (`residapp-main-branch` y `residapp-env-production`, en la aplicación de Entra `residapp-github-actions`): el sujeto pasa a ser
       `repo:<org>@<idOrg>/residapp@1366636161:…`, así que dejan de coincidir y fallan `azure/login` en `ci-cd.yml` (despliegue) y en `sql-dev-nivel.yml` (cada noche a la 01:00 UTC). El
       comando con el que se crearon no está escrito en ninguna parte del repositorio: hay que reconstruirlo y dejarlo documentado. Se crean las nuevas **antes** de transferir (conviven con
       las viejas) y se borran las viejas después.
     - **`RELEASE_PLEASE_TOKEN`** es un token de grano fino cuyo propietario del recurso es la cuenta personal: casi seguro deja de dar acceso al repositorio de una organización (que además
       puede exigir aprobación o prohibir los tokens personales). Si falla, el paso queda en rojo y release-please deja de abrir el PR de release. Se recrea con la organización como
       propietaria o, mejor, con una **GitHub App de la organización** (lo que ya sugiere el comentario de `release-please.yml`). Caduca hacia el 2027-02-03.
     - **Ruleset «Proteger main», entorno `production` (con su revisor, solo desde `main`, sin saltos para administradores) y secretos** del repositorio y del entorno: GitHub debería
       conservarlos, pero **no está probado** y el comando con el que se creó el ruleset no está documentado: **exportar su JSON antes** de transferir. El revisor del entorno debe ser
       miembro de la organización.
     - **Referencias escritas a mano** al propietario viejo (no hay ninguna en los workflows ni en `.claude/settings.json`): comandos `gh … --repo orlando6608/residapp` y `gh api …/rulesets/…`
       y el `curl` de la comprobación del 404 en las entradas A1 y «Repositorio: se sigue público por ahora» de `docs/prompts-ia/continuar-construccion.md`; el HTML
       `docs/pendientes-cj/archivados/costes-azure-y-nivel-de-la-base.html`; `docs/pendientes-cj/README.md` y `LICENSE` (hablan de repositorio público); `docs/seguridad/hallazgos.md` B7 (fuera
       de git) y la memoria de trabajo. `CHANGELOG.md` conserva 38 enlaces al propietario viejo: GitHub los redirige mientras nadie cree otro repositorio con ese nombre, y los nuevos los
       genera release-please solos. **Esa redirección no vale para OIDC ni para el token.** Cada copia local (la de Orlando, los *worktrees* y la de CJ) necesita `git remote set-url origin`.
   - **Orden: transferir primero y privatizar después, ya dentro de la organización** (para no contratar GitHub Pro personal, que dejaría de servir al transferir):
     0. Decidir titular y nombre; crear la organización; elegir el plan de pago de la organización (probablemente Team, por usuario; **[A verificar]** el precio y que incluye ruleset y
        aprobación de despliegues en repositorios privados) y fijar un **límite de gasto de Actions** (~15 USD sugeridos; estimación de ~3.300 minutos al mes **[sin medir en privado]**);
        añadir a Orlando (revisor) y a CJ (rol Read).
     1. Preparar en público y sin coste el **análisis estático sustituto de CodeQL** (Semgrep OSS, trabajo `analisis-estatico`) y el **escaneo de secretos** (gitleaks, ya probado el 2026-11-05);
        añadir el check nuevo al ruleset «Proteger main» y **solo después** quitar `analyze` (si no, ningún PR se puede fusionar); exportar el JSON del ruleset.
     2. `gh api orgs/<org> --jq .id` y crear en Azure las **dos credenciales federadas nuevas** con ese identificador.
     3. Transferir el repositorio; `git remote set-url origin` en cada copia; reinvitar a CJ.
     4. Verificar que sobreviven el ruleset, el entorno con su revisor y los secretos; recrear lo que falte.
     5. Recrear `RELEASE_PLEASE_TOKEN` (o la GitHub App) y comprobar que se abre el PR de release.
     6. Borrar las credenciales viejas de Azure.
     7. Privatizar: `gh repo edit <org>/residapp --visibility private --accept-visibility-change-consequences`, con Orlando delante.
     8. Comprobar: `curl` sin sesión a la página del repositorio da 404; un PR de prueba con los checks nuevos; un despliegue completo con aprobación; `sql-dev-nivel` lanzado a mano; release-please.
     9. Reactivar las alertas de Dependabot; actualizar las referencias de arriba, B7, la memoria y el relevo.
   - Lo que cambia o hay que tener presente: `LICENSE` («se publican solo para su consulta») queda obsoleta; los enlaces antiguos de `CHANGELOG.md` dependen de la redirección; los commits llevan el
     correo de Orlando (se puede ocultar hacia delante con la dirección `noreply`, no hacia atrás sin reescribir historia); **lo ya clonado o leído mientras fue público no se recupera** y a la
     persona experta se le dice «todo lo que fue público se trata como conocido» (correo de Orlando en los commits, cabeceras de un correo de prueba, diseño de seguridad con sus debilidades).
     Se puede volver a público. **[A verificar]** dónde guarda GitHub los datos del plan elegido (la residencia en la UE solo consta para Enterprise Cloud) y su acuerdo de tratamiento; GitHub
     entra en el registro de actividades como proveedor de desarrollo con la regla escrita «ningún dato personal en el repositorio, PR, issues ni registros de CI» (en el despliegue de
     producción, ningún paso imprime resultados de consultas). **Pendiente de la lectura del DPO:** el efecto de la transferencia en datos personales (el titular de la organización, GitHub
     como proveedor, residencia de datos del plan elegido); la lectura de `docs/privacidad/entorno-produccion-lectura-dpo.md` es anterior a esta decisión.
5. **Antes de abrir producción**: protección de etiquetas `v*` (solo Orlando puede crear una), 2FA en toda cuenta con escritura, acciones fijadas por SHA (M5).

### 9. Separación entre dev y producción

| Regla | Cómo se hace cumplir |
|---|---|
| Nunca datos reales en dev | Procedimiento escrito; rótulo «entorno de pruebas, no introduzca datos reales» y `noindex` en `dev.` (**no consta hoy**); sin restaurar copias de producción en dev |
| Nunca datos de dev en producción | Las cuentas de CJ y de Orlando de dev no se crean en producción: invitación y passkeys propias en `app.residapp.es` |
| `APLICAR_SEED` prohibido en producción | Trabajo de despliegue de producción separado y sin la variable, más el control técnico de `aplicar-scripts.sh` (apartado 7) |
| Credenciales no mezclables | Entornos de GitHub, credenciales federadas, secretos, identidad de despliegue, Key Vault y almacén de registros **distintos** por entorno |
| `/pendientes-cj` fuera de producción | Paquete de producción sin la carpeta, clave fuera de `appsettings.json` y prueba de 404 (apartado 5) |
| Configuración propia | `PasskeyDominio`, `Correo__UrlBase`, `AllowedHosts`, zona horaria; IA y correo apagados; nada copiado de los ajustes de dev |
| Demo a un tercero | Siempre sobre dev, con datos ficticios y sin acceso a `/pendientes-cj` |

## Alternativas descartadas

- **Misma suscripción que dev y otros proyectos, otro grupo de recursos**: más rápido, pero B9 seguiría abierto y el aislamiento sería solo por permisos.
- **Exportación anual o retención a largo plazo de 15 años de la base**: ver el apartado 4; es una copia con menos controles que la base y de todos los centros.
- **Front Door con WAF delante**: ver el apartado 6.
- **Firewall de SQL por IP de salida como en dev**: más simple, pero frágil al escalar y con 31 reglas que mantener; se mantiene solo la regla temporal del ejecutor.
- **Un único paquete para dev y producción con un interruptor para CJ**: conserva «un artefacto», pero los `.respuestas.json` viajarían a producción; se prefiere el paquete sin ellos.
- **Una Function para los trabajos de correo desde el primer día**: no hace falta con una instancia; queda como salida si se escala (ADR 0012).
- **Claves de cifrado propias en Key Vault para SQL (TDE)**: no necesario para el piloto y con riesgo de perder la clave.
- **Privatizar primero y transferir después**: obliga a contratar GitHub Pro personal, que al transferir deja de servir, y repite el cambio de credenciales y de ajustes. Se transfiere primero.
- **Transferir a otra cuenta personal en vez de a una organización**: misma rotura de credenciales y del token, sin rol de lectura para CJ y sin separar la titularidad de la persona.

## Consecuencias

- Producción es un entorno **aparte en suscripción, grupo, base, identidades, secretos, registros y correo**: más coste fijo y más piezas que mantener, a cambio de aislamiento real.
- Hay **cuatro cambios de código pequeños** previos que se pueden hacer ya en dev sin riesgo: prueba de salud, Data Protection con claves compartidas, registros filtrados (A4 y B10) y
  el paquete de publicación sin los documentos de CJ con su prueba.
- El pipeline pasa de uno a dos despliegues, con entornos de GitHub renombrados: el cambio de nombre del entorno es el paso más delicado (credencial nueva antes de tocar el workflow).
- El cumplimiento del plazo de 15 años descansa en la **base viva y en no borrar**, no en las copias; el ADR deja escrito que las copias son cortas y que restaurar exige reaplicar
  supresiones y cerrar sesiones.
- Transferir y privatizar el repositorio es un paso aparte, con coste y con un orden obligatorio; este ADR **no lo ejecuta**, solo lo registra como decidido. Si se transfiere sin haber creado antes
  las credenciales federadas nuevas, el despliegue y `sql-dev-nivel.yml` quedan caídos hasta crearlas, y si no se recrea el token, release-please deja de abrir el PR de release.
- El **titular de la organización** (persona física o sociedad) condiciona el contrato con los centros, el registro de actividades y la propiedad del código: se decide antes de crearla.
- `docs/seguridad/hallazgos.md` (fuera de git a propósito) debería actualizar B6 («la retención de las copias **no** sigue al plazo clínico») y B7; `docs/privacidad/expurgo-y-supresion.md:434`
  espera «que no hay retención a largo plazo»: o se cambia esa línea o se mantiene esta propuesta, pero no las dos cosas a medias.

## Puertas abiertas (decidir)

1. Cifra de retención de las copias, de la recuperación a un punto en el tiempo (35 días propuestos) y de cualquier exportación (**DPO con Orlando**).
2. Segunda región o alternativa a la copia geográfica; redundancia de zona o geo (**Orlando y el DPO**).
3. Objetivos de RPO y RTO, y cifras de la prueba de carga (**Orlando y CJ**).
4. Plazos de los registros de seguridad y plazo máximo de aviso de brecha a cada centro; quién cubre la guardia (**DPO y Orlando**).
5. Segunda persona para operaciones irreversibles; PIM o equivalente; inquilino de Entra de la suscripción nueva (**Orlando**).
6. **Nombre de la organización de GitHub, plan de pago y límite de gasto de Actions** (**Orlando**). Decididos ya: privatizar antes, transferir a una organización y que CJ entre con rol de lectura.
7. Dominio de envío de producción y de dev; si se usa un WAF regional o ninguno (**Orlando**).
8. Titular de ResidApp: **la sociedad** propietaria del repositorio, de la organización de GitHub y de la cuenta y suscripción de Azure como activos intangibles (**Orlando**). Condiciona los
   contratos con los centros y es **previo a todo lo demás**: sin ella no se crea la organización, ni la suscripción, ni se transfiere el repositorio.
9. Defender para SQL y para la nube (**Orlando**, coste).
10. Cuándo y cada cuánto se hace el simulacro de restauración (**Orlando y el DPO**).

## Incrementos

| N.º | Incremento | Estado |
|---|---|---|
| 0 | Decidir este ADR, la lectura del DPO y el titular de ResidApp | Propuesta, sin decidir |
| 1 | Preparar en dev: prueba de salud, Data Protection con claves compartidas, registros filtrados (A4 y B10), paquete sin documentos de CJ y prueba de 404, parametrizar el workflow, entorno `dev` | Sin empezar |
| 2 | Crear la suscripción (a nombre de la sociedad) y `rg-residapp-prod` con Bicep (`what-if` en PR), identidades separadas, rol a medida y bloqueos; **Puerta 1** | Aplazado hasta que exista la sociedad |
| 3 | Ensayos: restauración de una copia, prueba de carga, simulacro de swap y de reversión, simulacro de brecha | Sin empezar |
| 4 | **Crear la organización, transferir el repositorio y privatizarlo** (apartado 8, punto 4, pasos 0 a 9). Va **antes** de fusionar el despliegue de producción y de firmar con un centro, y antes de crear la credencial de producción (fila 2) | Decidido el 2026-11-08; **aplazado hasta que se decida privatizar y exista la sociedad titular**; falta nombre y titular |
| 5 | Cargar datos reales: **Puerta 2** y lista de `antes-de-datos-reales.md` | Sin empezar |
| 6 | DNS `app.residapp.es`, certificado, correo de producción y primer centro | Sin empezar; DNS solo después del recurso |
