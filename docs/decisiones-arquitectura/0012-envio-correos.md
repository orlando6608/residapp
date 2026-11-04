# ADR 0012 — Envío de correos a las familias: avisos sin contenido, recordatorio de citas y código de acceso

Estado: **aceptada** el 2026-10-30 (decisión de Orlando; propuesta el 2026-10-25). Sigue pendiente la revisión del DPO real (la persona que vela por la protección de datos)
antes de usar datos reales. **Decidido por Orlando** (`docs/pendientes-cj/orlando/dominio-y-correo.respuestas.json`): proveedor Azure Communication Services Email con datos en la UE;
dominio de avisos `avisos.residapp.es`, verificado con SPF, DKIM y DMARC (`p=none`); remitente fijo **«ResidApp» `no-responder@avisos.residapp.es`**, con el texto «No respondas
a este correo» y **sin el nombre del centro**; Microsoft 365 solo antes de hablar con un centro real. **Hecho en Azure** (`ecs-residapp-dev`, `acs-residapp-dev`); **en la aplicación
no está construido nada**. La cadena de conexión está en la Web App de Azure dev como `Correo__CadenaConexion` (puesta por Orlando el 2026-10-30, B9; la app aún no la lee). **Sin `Reply-To`** por ahora (decisión de Orlando, 2026-10-30): el correo dice «No respondas a este correo» y una respuesta no llega a nadie; se revisa al contratar Microsoft 365 y crear un buzón.

## Contexto

CJ contestó el 2026-10-25 tres preguntas que necesitan mandar correos a las familias (`docs/pendientes-cj/confirmaciones-tercera-tanda-cj.respuestas.json` y
`portal-familiar-confirmaciones-cj.respuestas.json`):

- **Aviso de propuesta de cita** (tercera tanda 6.1, opción C): cuando el centro propone una cita, a la familia le llega un correo genérico. Para los comunicados, no.
- **Recordatorio de una cita confirmada** (6.2, opción B): un correo el día antes, también genérico («Tienes una cita mañana en ResidApp»), sin decir con quién ni para qué.
- **Aviso de cambio o cancelación de una cita por el centro** (Orlando, 2026-10-31; **no** pasó por CJ: pendiente de su confirmación): cuando Administración reprograma o cancela una cita confirmada, a quien la reservó le llega un correo genérico («Tienes un cambio en una cita en ResidApp»). Un solo aviso para las dos cosas, sin decir cuál; sin correo al reservar; sin aviso cuando el centro cierra una solicitud o retira una propuesta, ni cuando cambia la familia o la baja del residente cancela las citas. Lectura de privacidad (borrador por validar) en `docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md`.
- **Código de acceso del familiar** (portal 3.2, opción A): el familiar entra con su contraseña y un código que le llega por correo (hoy entra solo con la contraseña), con
  límite de intentos fallidos, caducidad de la sesión y un aviso cuando se entra desde un sitio nuevo.

CJ razonó que «el correo ya existe». **No es así:** la aplicación guarda el correo del familiar como dato opcional (`FamilyMember.MaxEmailLength`, ficha del familiar en
Administración), pero **no envía ningún correo** y **no tiene tareas programadas** (nada se ejecuta solo a una hora; todo ocurre cuando alguien abre una pantalla). La
pregunta 6.1 ya avisaba de que haría falta un proveedor de correo. Este ADR propone cómo añadir ambas cosas.

Lo que ya está fuera de alcance y no cambia: contenido sanitario por correo, SMS o WhatsApp, y notificaciones en el móvil durante el piloto (`docs/tareas/futuro/backlog-diferido.md`;
CJ lo reafirmó en la 6.4).

## Requisitos

1. **Texto genérico.** Ningún correo dice el nombre del residente, la unidad, el equipo, el motivo ni nada de salud. Solo que hay algo pendiente en ResidApp, que hay un cambio en una cita, o el código. El aviso de cambio no distingue entre reprogramación y cancelación, no dice día, hora, equipo, modalidad ni motivo, y el motivo del cambio que escribe Administración no se lee nunca.
   El HTML puede llevar el estilo de la app y el icono de ResidApp incrustado en el mensaje (`cid:`); no lleva el logotipo ni el nombre del centro, ni imágenes ni recursos
   enlazados desde un servidor. El texto visible del HTML es el del texto plano más la palabra «ResidApp» de la cabecera (`docs/privacidad/aviso-cita-icono-en-correo.md`).
   Las plantillas están en el repositorio, versionadas como el código, y son las únicas que se pueden mandar.
2. **Proveedor con garantías.** Contrato de encargado del tratamiento (artículo 28 del RGPD: el proveedor trata los datos solo por cuenta nuestra) y datos guardados en la UE.
   El correo del familiar es un dato personal suyo.
3. **Registro de envíos sin contenido.** Qué plantilla, a qué familiar (su identificador, no la dirección) y cuándo; con eso se evita mandar dos veces el mismo aviso y se puede
   comprobar si un aviso salió. Se anota también la versión de la plantilla y su idioma (prueba de qué texto se mandó). No se guarda el texto ni el código.
4. **Remitente verificado.** Un dominio propio con SPF y DKIM (las firmas que dicen a los servidores de correo que el remitente es quien dice ser), para que los avisos no
   acaben en la carpeta de correo no deseado y para que nadie pueda hacerse pasar por ResidApp con facilidad.
5. **Familias sin correo.** Hoy el correo es opcional. Sin correo no hay aviso ni recordatorio (el aviso dentro del portal, 6.3, cubre a todas). Para el código de acceso (3.2)
   el correo pasa a ser **obligatorio** en el familiar que tiene acceso al portal: decidir qué pasa con los que ya tienen cuenta sin correo.
6. **Si el proveedor falla,** la acción que lo provoca no falla: la propuesta de cita, el cambio o la cancelación se guardan igual y el envío se reintenta; el código de acceso sí debe avisar de que no se
   pudo mandar.
7. **Plantillas por idioma (decidido por Orlando el 2026-10-29).** Cada plantilla existe en castellano y en catalán, como ficheros separados y versionados en el
   repositorio, con el mismo contenido genérico (requisito 1). Asunto, remitente y pie legal van también por idioma. Las dos versiones comparten identificador y versión, y
   se revisan y aprueban juntas. La traducción catalana la revisa ingeniería con un traductor externo (CJ, 2026-11-02, 2.2 B; antes se pensaba en una persona que sepa catalán; el DPO valida el contenido, no la lengua). El correo del código de acceso
   lleva en los dos idiomas el mismo aviso contra fraude («ResidApp nunca te pedirá este código por teléfono ni por mensaje»). Mientras el portal no esté en catalán (ADR 0013, aceptado), el
   correo en catalán dice que el enlace lleva a una pantalla en castellano. Añadir un tercer idioma exige una revisión del DPO por cada plantilla.
   El estilo y el icono son comunes a los dos idiomas (`EmailLayout`), se versionan con la plantilla y no son un parámetro de ella.
   **Cómo se elige el idioma de cada familiar (decidido por Orlando el 2026-10-29):** por defecto, el idioma del centro; el propio familiar puede cambiarlo en el portal
   cuando quiera. (El portal y la entrada en los dos idiomas, y dónde se guarda esta elección, están en el ADR 0013, aceptado.) Guardar la elección de
   cada familiar añade un dato personal nuevo (valor cerrado «es» o «ca», nunca texto libre), que entra en el registro de actividades, en la información al familiar y en
   el procedimiento de rectificación. Queda **descartado** deducir el idioma por el
   nombre o los apellidos, o por el navegador sin decírselo: es inexacto y puede ser discriminatorio. Si falta una de las dos versiones, el sistema usa la otra (el código
   de acceso no puede quedarse sin enviar); es una suposición de ingeniería a confirmar.

## Estado de la construcción

- **Pieza 1, aviso de propuesta de cita: construida el 2026-10-30, apagada por defecto** (script `0081`, tabla `envios_correo`; lectura de privacidad y 16 condiciones en `docs/privacidad/aviso-cita-por-correo.md`).
  `IEmailSender` (Application) con el adaptador `AzureEmailSender` (Azure Communication Services Email 1.1.0), `ProposalNoticeService`, `ProposalNoticeTemplate` (textos en castellano y catalán) y `SqlProposalNoticeStore`.
  Se llama después del `Commit` de proponer; vuelve a comprobar la vigencia y el acceso con la misma consulta que el portal; un solo destinatario (el familiar de la solicitud); sin `Reply-To`;
  seguimiento de aperturas apagado; HTML con el estilo de la app (`EmailLayout`: banda azul, tarjeta, enlace destacado y modo oscuro) y el icono de ResidApp incrustado (`cid:`), sin recursos remotos
  (2026-10-31, pendiente de validación humana según `docs/privacidad/aviso-cita-icono-en-correo.md`); sin reintentos (no hay tareas programadas hasta el recordatorio).
- **Pieza 1 bis, aviso de cambio o cancelación de una cita: construida el 2026-10-31, apagada por defecto** (script `0082`, misma tabla `envios_correo` con la plantilla `AVISO_CAMBIO_CITA`; lectura de privacidad en `docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md`).
  `ChangeNoticeService`, `ChangeNoticeTemplate` (textos en castellano y catalán, versión "1"), `SqlChangeNoticeStore` y `ChangeNoticeWorker`, **la primera tarea programada de la aplicación** (un `BackgroundService` de ASP.NET Core que se ejecuta cada minuto).
  Va en dos tiempos: al reprogramar o cancelar Administración, y solo allí (`AppointmentServiceApplicationService`), se **reserva** una fila PENDIENTE después del `Commit` (clave única: plantilla, cita, número del cambio); la tarea, **unos 5 minutos después**, la reclama (`intentos` pasa a 1, así que dos copias de la aplicación no la mandan dos veces), vuelve a comprobar que el cambio sigue siendo el último de la cita, que lo hizo Administración sin causa de sistema (la baja), que la hora que importaba no ha pasado y que quien la reservó sigue con acceso y con correo, y manda **un solo correo a quien reservó la cita**. Si la cita vuelve a cambiar en esos minutos, solo avisa el último cambio. Sin reintentos. El motivo del cambio no se lee en ningún momento.
  Decidido por Orlando el 2026-10-31: un solo aviso, con la línea «Aquí no ponemos los detalles…», **sin casilla** «No mandar aviso; voy a llamar yo», envío **diferido** con tarea programada dentro de la aplicación (no una Azure Function; si se pasa a producción con varias instancias, el servicio puede llamarlo una Function sin reescribirlo) y **sin aviso** al cerrar una solicitud o retirar una propuesta.
  Necesita «Siempre activo» en la Web App (una aplicación dormida no ejecuta la tarea): **no se ha encendido todavía**; con el aviso apagado no hace falta.
  **Identidad de la tarea (decidido por Orlando el 2026-10-31):** la tarea no tiene persona conectada y, con el usuario limitado de la aplicación, la seguridad por filas no le dejaría ver nada.
  Tiene un **usuario propio** de base de datos, miembro del rol `residapp_tareas` (script `0083`; ver ADR 0008), con su propia cadena `ConnectionStrings:ResidAppTareas` que solo usa la tarea
  (la reserva del aviso, dentro de la petición de Administración, usa la conexión normal). Antes de cada vuelta la tarea comprueba que su conexión es de ese rol y, si no, lo registra como error
  y no procesa nada. Revisión del DPO y condiciones: `docs/privacidad/tarea-correo-identidad-rls.md`.
- **Cierre de los avisos que quedan PENDIENTE (script `0089`, 2026-11-04; Orlando decidió el motivo, los plazos y lo no reclamado; lectura de `dpo-residapp`).** Una fila queda PENDIENTE para
  siempre si la aplicación se cae entre reservar o reclamar y anotar el resultado, o si el correo sale y falla después la escritura del resultado. El procedimiento
  `dbo.cerrar_envios_correo_sin_resolver` (solo `EXECUTE` para `residapp_tareas`; la tarea `UnresolvedNoticeWorker` lo llama cada 10 minutos con la conexión `ResidAppTareas`) las marca
  `FALLIDO` con el motivo nuevo `RESULTADO_DESCONOCIDO`: **no se sabe si el correo salió, y quien lea ese estado debe entenderlo como «sin confirmar», nunca como «no enviado»** (se mantiene «Avísala
  también por teléfono»). Plazo y motivo van dentro del procedimiento: más de 15 minutos para el aviso de propuesta; más de 30 minutos para el de cambio **y solo si ya estaba reclamado**
  (`intentos >= 1`); un aviso de cambio sin reclamar no se toca (sigue esperando de verdad; si la tarea vuelve, lo revalida). Nunca escribe ENVIADO, no reenvía ni reabre (política
  «como mucho una vez»: un correo duplicado o tardío es peor que uno que falta) y no borra; `resuelto_en` es el momento del cierre. La resolución tardía de la tarea sobre una fila ya cerrada no la
  cambia. Sin auditoría en `eventos_auditoria` (no actúa ninguna persona y exigiría `INSERT` al rol): solo se registra el recuento, como advertencia si es mayor que cero. Sin la conexión
  `ResidAppTareas` no cierra nada y lo avisa una vez.
- **Ajustes** (variables de entorno de la Web App, sección `Correo`): `Correo__Activo` (por defecto apagado), `Correo__AvisoCambioCita` (interruptor propio del aviso de cambio, por defecto apagado; además de `Correo__Activo`), `Correo__CadenaConexion`, `Correo__UrlBase` (por ejemplo `https://dev.residapp.es`; el único enlace del correo),
  `Correo__Remitente` (por defecto `no-responder@avisos.residapp.es`), `Correo__Idioma` (`es` o `ca`; por defecto `es`) y, fuera de producción, `Correo__DireccionesPermitidas__0`, `__1`… Sin ninguna lista, solo se enciende con `Correo__TodosLosDestinos=true`.
- **Falta:** para el aviso de cambio, **antes de encender el aviso en Azure:** que Orlando ejecute `database/seguridad/crear_usuario_tarea_azure.sql` (después de que el CI aplique el `0083`, que en un entorno con usuarios reales se aplica en ventana de mantenimiento) y ponga la contraseña en el ajuste `ResidAppTareas` de la Web App, probar en dev con datos de prueba que la tarea resuelve un aviso y no ve el motivo, y activar la auditoría de Azure SQL para ese usuario (hecho en dev el 2026-11-02; en producción hay que repetirlo), la información a la familia ampliada, los reintentos (hoy un fallo queda FALLIDO) y la validación humana del borrador del DPO; además, la validación humana de la casilla «Dirección confirmada con la familia» (**construida el 2026-11-02, script `0084`**; CJ eligió la opción A, 2.1: Administración confirma de palabra y, **sin la marca, no se envía ningún aviso**, ni el de propuesta ni el de cambio; **la marca autoriza solo avisos genéricos**: un correo con contenido o un código de acceso exigirían verificación técnica; lectura del DPO en `docs/privacidad/direccion-confirmada-familia.md`); la línea del titular de ResidApp en el pie (hoy sale sin ella, decisión de Orlando del 2026-11-02; requisito previo a datos reales); la confirmación por la persona experta del plazo del registro (12 meses como tope, decidido por Orlando el 2026-11-02; **el borrado está construido, script `0085`**: el procedimiento `dbo.purgar_envios_correo` borra lo que lleva más de 12 meses desde su resolución, o desde su creación si nunca se resolvió, y el rol `residapp_tareas` solo puede ejecutarlo, no borrar de la tabla; una tarea diaria de la aplicación, `EmailLogPurgeWorker`, lo llama con la conexión `ResidAppTareas` y registra solo cuántas filas borró; sin esa conexión no purga y avisa una vez en el registro de la Web App);
  **Casilla «Dirección confirmada con la familia» (construida el 2026-11-02, script `0084`):** dos columnas en `familiares` (`correo_confirmado_en` y `correo_confirmado_por_cuenta_id`; es del familiar, no del vínculo), sin canal ni dirección leída; el disparador `TR_fam_correo_confirmado` quita la marca ante cualquier cambio o borrado de la dirección salvo que el mismo `UPDATE` ponga una confirmación posterior; los dos avisos la vuelven a comprobar al enviar y, sin ella, anotan `OMITIDO / DIRECCION_NO_CONFIRMADA` (que **no** se manda después al confirmar, decisión de Orlando); la tarea (`residapp_tareas`) solo lee `correo_confirmado_en`; auditoría `FAMILY_EMAIL_CONFIRM`, `FAMILY_EMAIL_UNCONFIRM` y `FAMILY_EMAIL_CHANGE`; la casilla va solo en la ficha del familiar (añadir y editar), no en el alta del residente (decisión de Orlando), y la ficha enseña la fecha, no la cuenta (suposición de ingeniería, por el riesgo 2 del DPO). **Decidido el 2026-11-02:** CJ modificó FAM-10 por escrito (1.1 A; reescrita en `docs/bocetos-pantallas/wireframes-funcionales/portal-familiar.md`) y aceptó el asunto «Propuesta de cita» solo para la propuesta (1.2 A; el aviso de cambio conserva el suyo); el catalán lo revisa ingeniería con un traductor externo (2.2 B), lo que corrige el requisito 7, que decía «una persona que sepa catalán»; atiende a una familia con dudas Administración (3.1 A); la familia podrá apagar los avisos desde el portal más adelante (3.2 A; apagar el aviso no debe hacer perder una propuesta, y la pantalla dirá que el correo no sustituye al teléfono). Orlando: B10 sin seguimiento y con registros de entrega a 30 días en la UE (1.3 A, paso suyo en Azure aún sin hacer), DMARC a cuarentena antes de producción tras unos días de informes (1.4 A), prueba real del 31/10 suficiente (1.5 A) y persona experta pendiente hasta el primer centro real (1.6 C). Quedan por confirmar con CJ el aviso de cambio y la ampliación de FAM-10 a él: su matiz es que la redacción propuesta no lo nombraba;
  el recordatorio (pieza 2) y el código de acceso (pieza 3).

## Opciones

### Proveedor

| Opción | A favor | En contra |
| --- | --- | --- |
| **Azure Communication Services Email** (servicio de correo de Azure) | Mismo proveedor y mismo contrato que el resto de la aplicación; se crea con `az` como la base y la Web App; permite elegir que los datos se guarden en Europa (a comprobar al crearlo). | Hay que verificar un dominio propio; precio por correo y límites de envío, a comprobar. |
| Proveedor externo de correo (SendGrid, Brevo u otro) | Muy usados, con buen panel de entregas. | Un proveedor y un contrato de encargado más que llevar al DPO; datos fuera de Azure; otra clave secreta que guardar. |
| El servidor de correo (SMTP) de cada centro | El centro manda desde su propio dominio. | Una configuración por centro, sin garantías de entrega y difícil de probar; no sirve mientras no haya centros reales. |

### Cuándo se ejecuta el recordatorio

| Opción | A favor | En contra |
| --- | --- | --- |
| **Una tarea dentro de la propia aplicación** (`BackgroundService` de ASP.NET Core) que cada pocos minutos busca las citas de mañana sin recordatorio | Sin piezas nuevas en Azure; usa el mismo código y la misma base. | Exige «Siempre activo» (Always On) en la Web App, que hoy está **desactivado** (el plan `asp-residapp-dev` es Basic B1, que sí lo permite); con varias copias de la app habría que evitar envíos dobles (el registro de envíos lo resuelve). |
| Una Azure Function o un WebJob con temporizador | Se ejecuta aunque la Web App esté parada. | Otra pieza que desplegar, configurar y vigilar. |

## Recomendación de ingeniería

- **Azure Communication Services Email**, con los datos en Europa y un dominio propio verificado. Un puerto (`IEmailSender`, una interfaz de la capa de aplicación) y un
  adaptador para Azure, como con la IA (ADR 0011), para poder cambiar de proveedor sin tocar los casos de uso.
- **Una tarea dentro de la aplicación** para el recordatorio, con «Siempre activo» encendido, y una tabla de envíos con una clave única por (plantilla, cita o cuenta, día; para el aviso de propuesta, el número del cambio PROPUESTA, no el día) para
  que nunca salga dos veces.
- **Orden de construcción:** primero el aviso de propuesta de cita (no necesita tarea programada), después el recordatorio y, por último, el código de acceso del familiar,
  que cambia cómo entra y conviene hacerlo junto con la decisión de las passkeys de las familias (ADR 0007).

## Lo que debe revisar el DPO

- Que el contrato de Azure cubre Communication Services Email como encargado del tratamiento y dónde se guardan los datos y los registros del servicio (a comprobar).
- Cómo se informa a la familia de que recibirá correos y para qué, y si hace falta verificar su dirección antes del primer envío.
- Cuánto tiempo se guarda el registro de envíos.
- El texto de cada plantilla, en castellano y en catalán, y que el aviso previo a la familia exista en los dos idiomas y se muestre en el mismo en que luego se envía.
- Si el idioma de cada familiar se guarda (dato nuevo) y cómo se rectifica; y, con un abogado, si los derechos lingüísticos de las personas usuarias de servicios sociales
  en Cataluña obligan a la residencia a atenderlas en catalán (la obligación recaería en la residencia, no en ResidApp).

## Consecuencias

- Hasta que se decida, los tres puntos de CJ quedan pendientes y bloqueados por este ADR (`docs/tareas/alta-prioridad/pendientes-administracion.md`). El aviso dentro del
  portal (tercera tanda 6.3) no depende de él y se construye antes.
- Encender «Siempre activo» cambia un ajuste de la Web App: se hará desde el despliegue (`ci-cd.yml`), como la zona horaria y el HTTPS, para que una Web App recreada no lo pierda.
- Habrá que decir a CJ que hoy no existe el correo, para que no lo dé por hecho en sus respuestas.
