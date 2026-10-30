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
- **Código de acceso del familiar** (portal 3.2, opción A): el familiar entra con su contraseña y un código que le llega por correo (hoy entra solo con la contraseña), con
  límite de intentos fallidos, caducidad de la sesión y un aviso cuando se entra desde un sitio nuevo.

CJ razonó que «el correo ya existe». **No es así:** la aplicación guarda el correo del familiar como dato opcional (`FamilyMember.MaxEmailLength`, ficha del familiar en
Administración), pero **no envía ningún correo** y **no tiene tareas programadas** (nada se ejecuta solo a una hora; todo ocurre cuando alguien abre una pantalla). La
pregunta 6.1 ya avisaba de que haría falta un proveedor de correo. Este ADR propone cómo añadir ambas cosas.

Lo que ya está fuera de alcance y no cambia: contenido sanitario por correo, SMS o WhatsApp, y notificaciones en el móvil durante el piloto (`docs/tareas/futuro/backlog-diferido.md`;
CJ lo reafirmó en la 6.4).

## Requisitos

1. **Texto genérico.** Ningún correo dice el nombre del residente, la unidad, el equipo, el motivo ni nada de salud. Solo que hay algo pendiente en ResidApp o el código.
   Las plantillas están en el repositorio, versionadas como el código, y son las únicas que se pueden mandar.
2. **Proveedor con garantías.** Contrato de encargado del tratamiento (artículo 28 del RGPD: el proveedor trata los datos solo por cuenta nuestra) y datos guardados en la UE.
   El correo del familiar es un dato personal suyo.
3. **Registro de envíos sin contenido.** Qué plantilla, a qué familiar (su identificador, no la dirección) y cuándo; con eso se evita mandar dos veces el mismo aviso y se puede
   comprobar si un aviso salió. Se anota también la versión de la plantilla y su idioma (prueba de qué texto se mandó). No se guarda el texto ni el código.
4. **Remitente verificado.** Un dominio propio con SPF y DKIM (las firmas que dicen a los servidores de correo que el remitente es quien dice ser), para que los avisos no
   acaben en la carpeta de correo no deseado y para que nadie pueda hacerse pasar por ResidApp con facilidad.
5. **Familias sin correo.** Hoy el correo es opcional. Sin correo no hay aviso ni recordatorio (el aviso dentro del portal, 6.3, cubre a todas). Para el código de acceso (3.2)
   el correo pasa a ser **obligatorio** en el familiar que tiene acceso al portal: decidir qué pasa con los que ya tienen cuenta sin correo.
6. **Si el proveedor falla,** la acción que lo provoca no falla: la propuesta de cita se guarda igual y el envío se reintenta; el código de acceso sí debe avisar de que no se
   pudo mandar.
7. **Plantillas por idioma (decidido por Orlando el 2026-10-29).** Cada plantilla existe en castellano y en catalán, como ficheros separados y versionados en el
   repositorio, con el mismo contenido genérico (requisito 1). Asunto, remitente y pie legal van también por idioma. Las dos versiones comparten identificador y versión, y
   se revisan y aprueban juntas. La traducción catalana la revisa una persona que sepa catalán (el DPO valida el contenido, no la lengua). El correo del código de acceso
   lleva en los dos idiomas el mismo aviso contra fraude («ResidApp nunca te pedirá este código por teléfono ni por mensaje»). Mientras el portal no esté en catalán (ADR 0013, aceptado), el
   correo en catalán dice que el enlace lleva a una pantalla en castellano. Añadir un tercer idioma exige una revisión del DPO por cada plantilla.
   **Cómo se elige el idioma de cada familiar (decidido por Orlando el 2026-10-29):** por defecto, el idioma del centro; el propio familiar puede cambiarlo en el portal
   cuando quiera. (El portal y la entrada en los dos idiomas, y dónde se guarda esta elección, están en el ADR 0013, aceptado.) Guardar la elección de
   cada familiar añade un dato personal nuevo (valor cerrado «es» o «ca», nunca texto libre), que entra en el registro de actividades, en la información al familiar y en
   el procedimiento de rectificación. Queda **descartado** deducir el idioma por el
   nombre o los apellidos, o por el navegador sin decírselo: es inexacto y puede ser discriminatorio. Si falta una de las dos versiones, el sistema usa la otra (el código
   de acceso no puede quedarse sin enviar); es una suposición de ingeniería a confirmar.

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
- **Una tarea dentro de la aplicación** para el recordatorio, con «Siempre activo» encendido, y una tabla de envíos con una clave única por (plantilla, cita o cuenta, día) para
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
