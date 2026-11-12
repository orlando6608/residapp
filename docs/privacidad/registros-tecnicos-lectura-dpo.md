# Registros técnicos (hallazgo A4): lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-12. Segunda lectura experta del agente `dpo-residapp`, no un dictamen ni un sustituto de un DPO (arts. 37 a 39 RGPD). No modifica código, ADR ni otros documentos. Continúa `entorno-produccion-lectura-dpo.md`, apartado 1.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial; **[Suposición]**; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, el DPO real o un abogado; **[A verificar]** no confirmado.

Un **registro técnico** es la anotación automática de lo que hace el servidor (qué petición llegó, qué error dio). No es la auditoría funcional (`eventos_auditoria`), que dice quién hizo qué a qué residente.

## Conclusión

**Admisible con condiciones.** Registrar fallos inesperados y accesos denegados es una medida de seguridad que el RGPD pide (art. 32.1: «medidas técnicas y organizativas apropiadas» y «capacidad de garantizar la confidencialidad, integridad, disponibilidad»; RGPD consolidado a 2016-05-04). Tu propuesta es buena en el fondo, pero **corregiría cinco cosas** antes de construir:

1. **Los registros HTTP de la Web App no encajan con tu punto 1.** [Hecho] Llevan la ruta completa, la IP y el agente de usuario, y no se pueden filtrar antes de guardarse. La ruta incluye el id del residente (86 usos de `asp-route-id` según mi lectura previa), el `linkId` del Portal Familiar y **el token de la invitación** (`/Acceso/Invitacion/{id}`, `AccesoController.cs:117-141`; un solo uso, 72 horas: `AccessInvitation.cs:16-21`). Es justo lo que tu punto 1 prohíbe en el código. Propuesta: en la primera fase **no enviar los registros HTTP** a Log Analytics; las alertas de 5xx, 403 y conexiones fallidas pueden basarse en **métricas** de la plataforma, que no llevan IP ni ruta [A verificar que existen las métricas de 403 y de conexiones fallidas en Web App y SQL]. Se activan después, con 30 días, **si** el token sale de la ruta o el DPO acepta el riesgo residual **[Decidir]**.
2. **No existe el enlace auditoría a registro técnico que supones.** [Hecho] `eventos_auditoria` no guarda identificador de petición ni sesión (`0002_renombrado_espanol_sqlserver.sql:705-716`). Si el log no lleva nada de la cuenta, un pico de 403 no se puede atribuir. **[Opinión]** Añadir al aviso de acceso denegado el **identificador de sesión** (un GUID aleatorio, `sesiones.id`, ya presente en `eventos_acceso`: `0062`) y el **centro**. El centro no es un dato personal y hace falta para saber a qué responsable avisar (art. 33.2). La sesión es un seudónimo: solo la base lo une a una cuenta. Sigue sin cuenta ni residente en el texto.
3. **Limpiar caracteres de control (B10) no basta.** [Hecho] `InvalidIdentifierFilter.cs:20` registra la ruta, que puede llevar otros ids o un token. Registrar la **plantilla de ruta** y el `ParamName`, como ya dice `entorno-produccion-lectura-dpo.md:61`; la limpieza de caracteres de control, además.
4. **En el runner, registra el código reconocido, no el mensaje.** [Hecho] `AccessDeniedPattern` no está anclado (`ApplicationResult.cs:46,175`): `IsMatch` acepta un mensaje con texto alrededor del código. Registra solo `Match.Value`, o el mensaje solo si encaja entero con `^[A-Z][A-Z0-9_]{2,80}$` (los códigos de dominio y los THROW de los triggers lo cumplen; un mensaje de clave duplicada no). Ojo: `Normalize` devuelve antes algunos AccessDenied con otro texto (`FAMILY_ANONYMIZATION_NOT_PRINCIPAL`, `ApplicationResult.cs:105-106`); decide si se registran. Tu regla de SQL (solo `Number`, `State`, tipo y método) es correcta. Conflict e InvalidInput sin registrar: de acuerdo.
5. **IP de Application Insights: no se «anonimiza el último octeto».** [Hecho] Por defecto se usa para ubicar el país, se descarta y se escribe `0.0.0.0` en `client_IP`; solo se guarda con `DisableIpMasking` (Microsoft Learn, «Geolocation and IP address handling», actualizada 2026-09-29, leída hoy). **No activar `DisableIpMasking`.**

Dos precisiones de hecho: [Hecho] la app ya tiene más avisos que «dos» (`AccesoController.cs:87,108,140,389`, `MistralClient.cs:109,124`, los workers de correo), y todos registran solo tipos o códigos; están bien. Y en los *diagnostic settings* de SQL, la categoría `Errors` puede traer el texto del error con valores [A verificar]: usa solo la auditoría de autenticación y métricas.

## (a) Qué datos personales acaban en los registros, y base jurídica

| Dato | Dónde acaba | ¿Personal? | Comentario |
|---|---|---|---|
| IP del cliente | Registros HTTP; auditoría SQL; `eventos_acceso`. **No** en App Insights (0.0.0.0) | Sí: «identificador en línea» (RGPD, considerando 30) | Familiares y personal |
| Ruta con ids (residente, `linkId`, token) | Registros HTTP; `Url` en App Insights si no se filtra | Sí; el par IP + id de residente revela quién se interesó por quién. Es **inferencia** sobre salud o relación asistencial (art. 9) [Opinión] | Token: credencial, no solo dato |
| Agente de usuario | Registros HTTP; App Insights | Sí, junto con la IP: huella del dispositivo | |
| Sesión y centro (si se añaden) | Aviso de acceso denegado | Sesión: seudónimo; centro: no | Ver punto 2 |
| Código de error, tipo de excepción, hora, método | Aplicación | No | |

**Base jurídica [Opinión; la fija el responsable]:** la residencia necesita una del art. 6 para el tratamiento. Candidatas: art. 6.1.f (interés legítimo en la seguridad de la información: considerando 49, «en la medida estrictamente necesaria y proporcionada») o 6.1.c (cumplir el art. 32). **Si la residencia es pública, el art. 6.1.f no vale «en el ejercicio de sus funciones»** (art. 6.1, último párrafo); entonces 6.1.c o misión pública, y el ENS (RD 311/2022, BOE act. 2026-10-30) pide el registro de actividad (`op.exp.8`) [A verificar según tipo de centro]. **Finalidad única:** detectar, investigar y notificar incidentes (arts. 32 y 33). Nada de uso por ResidApp para mejorar el producto ni medir el trabajo del personal: si ResidApp lo hiciera, decidiría fines y dejaría de ser encargada (art. 28.10, a verificar). Evitar cuenta en el log también reduce el choque con el control laboral (LOPDGDD art. 87.3: criterios con participación de los representantes) [A verificar si aplica].

## (b) Retención

[Hecho, Microsoft Learn «Manage data retention in a Log Analytics workspace», leída hoy] 30 días por defecto; las tablas de Application Insights, 90 días sin coste; mínimo 4 días; ampliable hasta 2 años; una retención de «30 días» puede conservar 31 salvo `immediatePurgeDataOn30Days`. **Ninguna norma que haya leído fija una cifra** (el ENS solo exige *indicar* el plazo: `op.exp.8.r3.1`; el art. 5.1.e RGPD, el «no más del tiempo necesario»). Propuesta **[Opinión; Decidir el DPO]** en dos clases, como en `entorno-produccion-lectura-dpo.md:66`:

- **Clase 1, sin IP ni ruta con valores (aplicación, excepciones, solicitudes ya normalizadas): 90 días.** Un incidente se descubre a menudo semanas después; con 30 se pierde el rastro. El riesgo de 90 es bajo porque el diseño evita datos personales.
- **Clase 2, con IP o ruta cruda (registros HTTP si se activan, auditoría SQL de autenticación): 30 días.** La IP de inicios de sesión ya vive más tiempo en `eventos_acceso` (plazo sin decidir), así que el rastro importante no se pierde. **[Decidir]** bajar la auditoría SQL de dev (hoy 90 días) a 30 o aceptar 90.
- Una conservación **ampliada de un incidente concreto** es una copia aparte con acceso restringido, decidida por el responsable; no sube el plazo general. Sin archivo de largo plazo.

Si es corta, se pierde la capacidad de reconstruir un incidente tardío (art. 33.5). Si es larga, se acumulan IP y rutas sin mecanismo de supresión.

## (c) IP y muestreo

- IP en Application Insights: ya enmascarada; no tocar. Truncar la IP en los registros HTTP: **no lo recomiendo** [Opinión]; sin la IP completa, el registro apenas sirve para investigar; se compensa con 30 días, lectura mínima y token fuera de la ruta. [A verificar] si Azure permite transformarla en el origen.
- **Muestreo: desactivarlo (100 %) para excepciones, trazas de aviso/error y solicitudes** [A verificar el valor por defecto del SDK elegido]. El muestreo falsea los recuentos que sirven para estimar «el número aproximado de interesados» (art. 33.3.a) y puede esconder justo el evento raro. Con el volumen de un piloto el coste es bajo.
- **Tope diario:** [A verificar] al alcanzarlo se deja de recoger y se pierde el incidente. Ponerlo alto y con alerta al 80 %, no como muro silencioso.

## (d) Quién lee, y qué anotar

- **Lectura:** rol **Log Analytics Reader** (o Monitoring Reader) sobre el almacén, a **dos personas con nombre** (titular y suplente), sin Owner ni Contributor en el día a día. Marcar como **tablas protegidas** las que lleven IP (Microsoft Learn, ip-collection: bloquean los roles de lectura hasta dar acceso explícito). Quien pueda cambiar retención o borrar datos queda anotado: eso es lo que da valor de prueba (ENS `op.exp.8.r4.1`).
- **Registro de actividades (art. 30):** una línea en el de la residencia («seguridad y trazabilidad técnica»: finalidad, categorías del cuadro anterior, plazos de las dos clases, destinatario Microsoft como encargado, medidas) y una en el de ResidApp como encargada (art. 30.2). **EIPD:** anexo breve al análisis general, no una EIPD propia: la lista de la AEPD (punto 3) cubre la monitorización «sistemática y exhaustiva» y esto no lo es mientras no lleve cuenta ni ruta cruda; se rehace si se añade cuenta, contenido o analítica de uso [Opinión]. **Información:** al personal (art. 13) y la política del Portal, hoy con `[PENDIENTE]` justo en esto (`politica-privacidad-portal.md:98`).
- **Subencargados:** Azure Monitor, Log Analytics y Application Insights son servicios de Microsoft, ya encargado de la Web App y la base. [Suposición] No es un subencargado nuevo, pero el art. 28.2 obliga a informar de cambios previstos: actualiza la **lista de servicios y regiones** del anexo, avisa al responsable y [A verificar] que las condiciones de Microsoft cubren cada servicio y que Spain Central lo ofrece. Los grupos de acción de alertas son recursos «globales» [A verificar dónde se procesan]: que las alertas lleven solo recuentos.

## (e) ¿Sirven como evidencia para las 72 horas?

Los registros **no abren el plazo**: corre desde que el responsable «tuvo constancia» (art. 33.1; AEPD, guía de brechas, apartado B: incluye fines de semana). El encargado avisa al responsable «sin dilación indebida» (art. 33.2). Los registros sirven para detectar pronto, saber si hubo datos personales, estimar afectados (33.3.a) y documentar (33.5). Mínimo para que sirvan:

1. Hora en UTC con reloj gestionado por la plataforma (ENS `op.exp.8.r2.1`) [Suposición en PaaS].
2. Sin muestreo y sin tope que los corte (apartado c).
3. **Una persona los mira**: alertas con receptor y suplente; sin eso no hay detección (ENS `op.exp.8.r1.1`). Hoy hay una sola persona de operación [Decidir cobertura].
4. Integridad razonable: Log Analytics **no es inmutable**. Candado de borrado en el grupo de recursos, cambios de retención y purgas visibles en el registro de actividad de Azure, y copia del incidente a un almacén con inmutabilidad [A verificar].
5. Correlación con `eventos_auditoria` por **sesión y centro** (punto 2); sin ella, se cruza solo por hora.
6. Un protocolo de brechas con la plantilla del art. 33.3 y un simulacro antes de producción.

## (f) Lista de comprobación antes de activarlo

- [ ] **Antes de activar en dev:** `RunAsync` registra solo código reconocido (`Match.Value`), tipo, `Number`/`State` y método; prueba que una `SqlException` con clave duplicada **no** deja el valor; B10 por plantilla de ruta.
- [ ] Sin SDK de JavaScript en el navegador (cookies propias: LSSI art. 22) [A verificar con el DPO].
- [ ] Filtro que sustituye `Url` por la plantilla, quita la cadena de consulta y excluye el texto de comandos SQL. Consulta de control en dev: ninguna fila con patrón GUID ni `Invitacion/` en `AppRequests`, `AppTraces` ni `AppExceptions`.
- [ ] Ningún logger en nivel Information que imprima rutas (`Microsoft.AspNetCore.*` en Warning o más; revisa las variables de la Web App y el registro de `HttpClient`).
- [ ] `DisableIpMasking` apagado. Muestreo al 100 %. Tope con alerta.
- [ ] Almacén propio de producción, en la UE, no compartido con dev; retenciones de las dos clases fijadas.
- [ ] Roles: lectura a dos personas; tablas con IP protegidas; candado de borrado.
- [ ] Alertas por métricas, con recuentos; receptor y suplente nombrados.
- [ ] Registro de actividades, anexo de EIPD, información al personal y a la política, y anexo de subencargados al día.
- [ ] **Datos reales solo después** de la decisión escrita del DPO sobre las dos clases de plazo y de los puntos de abajo.

## Decisiones de Orlando (2026-11-12, para Azure dev)

Tomadas de una en una tras esta lectura. No sustituyen la decisión escrita de una persona experta antes de usar datos reales.

- **Qué se guarda:** solo la salida de la aplicación (`AppServiceConsoleLogs` y `AppServicePlatformLogs`), sin los registros de peticiones HTTP.
- **Dónde:** una cuenta de almacenamiento nueva en Spain Central, separada de la de la auditoría de SQL.
- **Retención:** 90 días para estos registros, que no llevan IP ni ruta con identificadores.
- **Sesión y centro:** sí. El aviso de acceso denegado lleva el identificador de la sesión (aleatorio, validado contra la base) y el centro **que declara la cookie del ámbito activo**. Esa cookie no está firmada: es lo que dice la petición, no lo verificado, y se llama «centro declarado» en el registro. Sin cuenta, residente ni nombre en el texto.
- **Alertas:** por métricas de la plataforma (errores 5xx, respuestas 403 y conexiones fallidas a SQL), con un titular y un suplente designados. Las direcciones no se anotan en el repositorio, que es público.

## A verificar o decidir

**[Decidir] Orlando / DPO / abogado:** fase 1 sin registros HTTP (sí o no); token de invitación fuera de la ruta (no lo pido, lo señalo); plazos de las dos clases y alineación de la auditoría SQL; si el log lleva sesión y centro; base jurídica según sea la residencia pública, concertada o privada; titular y suplente de las alertas.

**[A verificar]:** métricas de 403 y de conexiones fallidas; categorías de diagnóstico de SQL con texto; muestreo por defecto del SDK; comportamiento del tope diario; transformar la IP en origen; Azure Monitor en Spain Central y su cobertura contractual; región de los grupos de acción; art. 28.10 y 87.3 LOPDGDD (no leídos aquí enteros); inmutabilidad de la copia de un incidente.

**Fuentes** (carpeta `normativa-local`, texto consolidado, no oficial): RGPD CELEX 02016R0679-20160504 y considerandos 30 y 49; ENS RD 311/2022 (BOE act. 2026-10-30); LOPDGDD (BOE act. 2026-10-08); AEPD, guía de brechas y lista de EIPD (PDF). Web, leídas hoy con WebFetch: Microsoft Learn `ip-collection` y `data-retention-configure`.
