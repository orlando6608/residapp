# Política de privacidad de ResidApp (página pública `/Home/Privacy`)

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de publicarse o de usarse con datos reales.** Fecha del borrador: 2026-11-03. Segunda lectura experta, no un dictamen.

Este archivo tiene dos partes bien separadas:

- **Parte A — Texto público.** Lo que leerá la gente. Está escrito para pasarlo a la vista `src/ResidApp.Web/Views/Home/Privacy.cshtml`. Lleva marcadores **[PENDIENTE: …]** donde falta un dato que no se debe inventar. Ningún marcador puede llegar a producción.
- **Parte B — Notas para quien valide.** Base jurídica, riesgos, desajustes entre lo que hace la app y lo que dicen los documentos, y decisiones abiertas con quién decide cada una. **No se publica.**

Etiquetas de la parte B: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL); **[Suposición]** mía; **[Decidir]** lo fija una persona concreta; **[A verificar]** no he podido confirmarlo en fuente oficial.

---

# PARTE A — TEXTO PÚBLICO

> Nota para quien lo pase a la vista: en móvil, las tablas de las secciones 8 y 10 deben mostrarse como tarjetas (una por fila), según `docs/decisiones-arquitectura/directrices-pwa-movil.md`, apartado 2. Cada encabezado `##` debe tener un ancla (`id`) para poder enlazar desde el correo («apartado Privacidad»).

# Política de privacidad

Última actualización: **[PENDIENTE: fecha de publicación]**

## En pocas palabras

- ResidApp es una aplicación que usan las residencias de personas mayores para organizar los cuidados y mantener informadas a las familias.
- **El responsable de tus datos es el centro (la residencia) que te dio acceso o que cuida de ti o de tu familiar.** ResidApp trabaja por cuenta del centro y solo según sus instrucciones.
- Se tratan datos de salud de las personas residentes. Son datos especialmente protegidos y solo los ve el personal que los necesita para su trabajo.
- Si tienes cuenta, guardamos los datos mínimos para que puedas entrar y para dejar constancia de quién hace qué.
- No hay publicidad, ni analítica, ni cookies de terceros. Las cookies que usamos son las necesarias para que la aplicación funcione (apartado 10).
- Para ejercer tus derechos (acceder a tus datos, corregirlos, pedir que se borren, etc.), **escribe a tu centro**. Los datos de contacto de tu centro están en el apartado 12.
- Si no estás conforme, puedes reclamar ante la autoridad de protección de datos (apartado 13).

---

## 1. Quién es quién

**El centro (la residencia) es el responsable del tratamiento.** Esto significa que el centro decide para qué se usan tus datos y cómo, y responde ante ti y ante la autoridad. Cada centro tiene su propia información de privacidad, que te entrega en el momento de darte de alta.

**ResidApp es la encargada del tratamiento.** Esto significa que ResidApp guarda y procesa los datos por cuenta del centro, con su autorización y siguiendo sus instrucciones. ResidApp no usa tus datos para sus propios fines.

**Quién presta el servicio ResidApp:**

- Nombre o razón social: **[PENDIENTE: identidad del prestador]**
- NIF: **[PENDIENTE]**
- Domicilio: **[PENDIENTE]**
- Correo de contacto de privacidad: **[PENDIENTE]**
- Delegado de protección de datos de ResidApp, si lo hay: **[PENDIENTE: nombre y contacto, o «no se ha designado»]**

**Tu centro:** nombre, domicilio y contacto de su delegado de protección de datos, en el apartado 12.

## 2. Qué datos tratamos y para qué

Esta página describe lo que ResidApp hace hoy. Si una función no está en esta página, es que no existe.

### 2.1 Personas residentes

Los datos los introduce el personal del centro. Son:

- **Identificación:** nombre, fecha de nacimiento, sexo documentado, unidad y habitación o plaza, y estado (en el centro, de baja, suspendido).
- **Datos de salud y de cuidados:** valoración inicial (llamada «basal»: cómo está la persona al ingresar, con escalas de autonomía y cognición), registro diario de los cuidados (cómo ha comido, cómo duerme, cambios que se observan), incidencias, valoraciones de Enfermería y Medicina, indicaciones y su seguimiento, y los informes de derivación a otros servicios sanitarios.
- **Comunicaciones a la familia:** los textos que el centro decide publicar para la familia.
- **Contactos:** qué familiares tiene la persona, cuál es su contacto urgente y quién la representa.
- **Foto de identificación:** solo si hay consentimiento (apartado 7).
- **Traslados, bajas y suspensiones:** cuándo y por qué motivo.

**Para qué:** prestar la atención y los cuidados, coordinar al equipo, informar a la familia y dejar constancia de lo que se hace (continuidad asistencial y seguridad).

ResidApp **no** guarda la medicación de las personas residentes ni su administración.

### 2.2 Familiares y representantes

Los datos los introduce el personal de Administración del centro, a partir de lo que tú le das. Son:

- **Nombre, teléfono y relación** con la persona residente. El **correo electrónico es opcional**.
- **Tu autorización de acceso** al Portal Familiar: si está activa, hasta cuándo es válida y si eres la persona de referencia o su representante.
- **Tu cuenta:** identificador de acceso y contraseña (la contraseña se guarda transformada, de forma que nadie puede leerla).
- **Tus citas:** las solicitudes de cita y las citas que reservas con el centro.
- Si tu centro lo tiene activado: la marca interna de que el centro **ha confirmado tu dirección de correo contigo** (se la leyó a quien corresponde, y se anota quién y cuándo).

**Para qué:** que puedas ver lo que el centro publica para la familia, gestionar citas y recibir los avisos que hayas aceptado. El centro también usa tu teléfono para localizarte en caso necesario.

El Portal Familiar **no** da acceso al historial clínico interno.

### 2.3 Personal del centro

Los datos los introduce la Administración del centro. Son:

- **Cuenta:** nombre, identificador de acceso, perfil (Auxiliar, Enfermería, Medicina, Dirección Clínica, Administración), centro y unidades a las que tienes acceso, cargo y equipo o turno, si el centro lo registra.
- **Credenciales:** la contraseña (transformada), la clave del segundo factor, los códigos de recuperación (transformados) y, si las usas, las llaves de acceso («passkeys»).
- **Registro de entradas:** cuándo entras, sales, o fallas la contraseña o el código, con la **dirección IP** desde la que lo haces.
- **Registro de actividad (auditoría):** cuenta, perfil, centro, fecha y hora de las acciones que cambian datos o que consultan datos sensibles. En algunas consultas también se guarda el motivo que escribes.
- **Tu firma** en los documentos que firmas dentro de la aplicación (por ejemplo, un informe de derivación).
- Si usas el dictado por voz: tu voz (apartado 6).

**Para qué:** que solo entren las personas autorizadas, que cada una vea solo lo que le corresponde y poder demostrar quién consultó o cambió qué dato de salud. **Este registro sirve para la seguridad y para detectar accesos indebidos. ResidApp no incluye listas ni clasificaciones de personal por rendimiento.**

### 2.4 Quien visita esta web sin cuenta

Esta página y la hoja de ayuda se pueden ver sin iniciar sesión. Al visitarlas, el servidor recibe tu dirección IP y los datos técnicos que envía tu navegador, necesarios para mostrarte la página. **[PENDIENTE: confirmar qué registros técnicos del servidor se guardan y durante cuánto tiempo.]**

Al intentar iniciar sesión, la IP se usa también para limitar los intentos repetidos (para frenar a quien pruebe contraseñas al azar).

## 3. Con qué base legal

La base legal exacta la fija **cada centro** y te la explica en su información de privacidad. Con carácter orientativo:

- **Atención y cuidados de las personas residentes** (incluidos los datos de salud): el tratamiento es necesario para prestar asistencia sanitaria y social, y cumplir las obligaciones legales de documentación clínica. **[PENDIENTE: la residencia y su DPO confirman las bases y la condición del art. 9 RGPD que citan.]**
- **Familiares:** el servicio que el centro presta a la familia, el interés legítimo del centro en coordinar visitas y citas y, si el centro es público, el ejercicio de sus funciones. **[PENDIENTE: el DPO confirma cuál aplica.]**
- **Personal:** la relación laboral o de servicios y el interés legítimo del centro en la seguridad de los datos que custodia. **[PENDIENTE: el DPO confirma.]**
- **Foto de la persona residente:** consentimiento de la persona o de su representante (apartado 7).

## 4. Quién puede ver los datos

- **Dentro del centro:** solo el personal con permiso para ello, y solo los residentes y las unidades de su ámbito. Cada perfil ve cosas distintas: por ejemplo, Auxiliar no ve lo mismo que Medicina, y el Portal Familiar no ve el historial clínico.
- **Las familias:** solo lo que el centro publica para ellas y solo sobre la persona residente con la que tienen autorización.
- **ResidApp (el personal de operaciones):** el perfil de «Plataforma» crea los centros y su primer administrador y **no ve residentes ni contenido clínico**. Quienes mantienen la base de datos de ResidApp pueden tener acceso técnico por razón de su función, sometido a deber de confidencialidad. **[PENDIENTE: ResidApp confirma quién, con qué controles y registro de esos accesos.]**
- **Otras entidades:** las empresas del apartado 9, solo en la medida necesaria para prestar el servicio. Los datos no se venden ni se ceden para publicidad.
- **Autoridades:** si una ley o una resolución lo exige (por ejemplo, la inspección o un juzgado), el centro les facilitará lo que corresponda.

## 5. Avisos por correo

El centro **puede** tener activados avisos genéricos por correo para las familias que le hayan dado su dirección. Hoy hay dos:

- «Tienes una propuesta de cita pendiente»: cuando el centro propone una cita.
- «Tienes un cambio en una cita»: cuando el centro cambia o cancela una cita que reservaste (se envía a quien la reservó).

Estos avisos **no dicen** el nombre de la persona residente, ni el centro, ni la fecha, ni el motivo, ni nada de salud. Solo te piden que entres en ResidApp. El remitente es «ResidApp» y no se puede responder al correo: si necesitas algo, llama al centro.

- El aviso solo se envía a una dirección que el centro **ha confirmado contigo** y mientras tengas acceso al Portal Familiar.
- Es **voluntario**. Para dejar de recibirlos, pide al centro que borre tu dirección. Verás las novedades igualmente al entrar en ResidApp.
- ResidApp **no** mide si abres el correo ni si pulsas el enlace.
- **Por tu seguridad:** ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje. Si recibes un aviso que no esperabas, ignóralo y cuéntaselo al centro.
- Para enviarlos usamos el servicio de correo de Microsoft (apartado 9). Guardamos un registro de cada envío **sin el contenido ni la dirección**: solo qué aviso, a qué familiar (por su identificador interno), cuándo y si salió.

## 6. Dictado por voz (solo si el centro lo activa)

En la pantalla «Registrar cambio», una auxiliar puede **contar hablando** lo que ha observado en una persona residente, en lugar de marcar casillas.

Esta función **solo está disponible si ResidApp y el centro la han activado**. **[PENDIENTE: confirmar su estado antes de publicar; hoy está apagada y solo se ha probado con audios y datos inventados.]**

Cómo funciona:

1. La auxiliar graba un audio corto.
2. El audio se envía a un proveedor externo, **Mistral AI**, que lo pasa a texto. Un segundo servicio de Mistral propone, con ese texto, qué casillas y notas rellenar.
3. La pantalla muestra una **sugerencia**. La auxiliar la revisa una a una y es ella quien confirma. **Nada se guarda sin su confirmación y no se toma ninguna decisión automática sobre la persona.**

Qué se envía y qué no:

- Se envían **el audio y su texto**. Si en el audio se dice un nombre, llega al proveedor, por eso la pantalla pide no decir nombres de personas.
- **No** se envían el nombre ni el identificador de la persona residente, ni el centro, ni la cuenta de quien dicta, ni historial alguno.
- **ResidApp no guarda ni el audio ni el texto.** Solo anota en la auditoría que se usó el dictado: quién, en qué unidad, sobre qué residente y cuándo.
- La voz de quien dicta es también un dato personal suyo. Se usa solo para pasarla a texto, no para identificarla.
- Mistral AI es una empresa francesa. Según su información pública, trata los datos en la Unión Europea. **[PENDIENTE: confirmar por escrito con Mistral la región de tratamiento, que no conserva el audio ni el texto y que no los usa para entrenar sus modelos, antes de publicar esta frase.]**

Si no hay micrófono, si falla el servicio o si prefieres no usarlo, el formulario normal funciona igual.

## 7. Foto de la persona residente

La foto sirve para identificar bien a la persona (por ejemplo, en las listas del personal). Es **opcional** y solo se guarda **con el consentimiento por escrito** de la persona residente o de su representante. Lo que se anota en ResidApp es quién firmó el consentimiento y la fecha; el documento firmado lo guarda el centro.

- La ven el personal de Auxiliar, Enfermería, Medicina y Administración. **Nunca se muestra a las familias.**
- Se guarda una sola foto por persona: al cambiarla se sustituye la anterior, y al quitarla se borra.
- Se borra al dar de baja a la persona.
- Si la persona no quiere foto, se anota «No quiere foto» y no se guarda ninguna.
- Pasados 12 meses se avisa al personal de que conviene renovarla. Es un aviso, no un borrado.

## 8. Cuánto tiempo se guardan los datos

El plazo lo fija el centro (como responsable) y la ley. ResidApp aplica lo que el centro le indica. Mientras el centro no decida otra cosa, esto es lo que hay hoy:

| Dato | Cuánto tiempo | Estado |
|---|---|---|
| Historia de cuidados y datos de salud de la persona residente | 15 años desde la baja de la persona (plazo por defecto de ResidApp; el centro puede fijar otro dentro de lo que la ley permita; ResidApp no borra nada sola al vencer) | **[PENDIENTE: una persona experta confirma el plazo y la aplicabilidad del art. 12 a una residencia]** |
| Cuentas del personal y de las familias | Personal: se suspenden y no se borran mientras figuren en documentos clínicos firmados. Familias: a petición se borran nombre, teléfono y correo y se cierra el acceso | **[PENDIENTE: plazo tras la baja de la cuenta]** |
| Registro de entradas (con la IP) | **[PENDIENTE]** | Sin decidir |
| Registro de actividad (auditoría) | **[PENDIENTE]** | Sin decidir |
| Sesiones abiertas y cerradas | **[PENDIENTE]** | Sin decidir |
| Registro de envíos de correo (sin contenido ni dirección) | Máximo 12 meses; se borra solo | **[PENDIENTE: confirmación de la persona experta]** |
| Registros de entrega del servicio de correo de Microsoft | **[PENDIENTE: previsto 30 días en un espacio propio en la UE]** | Sin configurar |
| Foto | Hasta que se cambia, se quita o se da de baja a la persona | Según lo descrito en el apartado 7 |
| Audio y texto del dictado | ResidApp no los guarda | **[PENDIENTE: lo que conserva Mistral]** |
| Copias de seguridad | **[PENDIENTE]** | Sin decidir |

## 9. Empresas que intervienen y transferencias fuera de la UE

Estas empresas son **subencargadas**: trabajan para ResidApp, que trabaja para el centro. Tratan los datos solo para dar el servicio y están obligadas por contrato.

| Empresa | Para qué | Cuándo interviene |
|---|---|---|
| **Microsoft Azure** | Alojar la aplicación (servidor web) y la base de datos donde se guardan todos los datos | Siempre. Región: **[PENDIENTE: región de Azure del servicio; se prefiere la Unión Europea]** |
| **Microsoft Azure Communication Services** | Enviar los avisos por correo del apartado 5 | Solo si el centro tiene activados los avisos |
| **Mistral AI** | Pasar a texto el dictado y proponer la sugerencia del apartado 6 | Solo si el centro tiene activado el dictado |

**Transferencias fuera de la Unión Europea.** ResidApp no tiene previsto enviar datos fuera de la UE. **[PENDIENTE: confirmar región real de Azure y de Mistral.]** Microsoft indica, para su servicio de correo, que los datos pueden transitar o procesarse en otras zonas geográficas. En nuestros avisos solo viajaría una dirección de correo y la hora de envío. **[PENDIENTE: garantía aplicable a esas transferencias, por ejemplo cláusulas contractuales tipo o certificación del proveedor.]**

**Fuentes de letra de Google en dos páginas de ayuda.** Las páginas «Flujo de trabajo» y «Acceso y sesión» (enlazadas en el pie de la aplicación del personal) cargan los tipos de letra desde Google. Al abrirlas, tu navegador se conecta con Google y le llega tu dirección IP. **[PENDIENTE: ResidApp va a servir esas letras desde su propio servidor; retirar este párrafo cuando esté hecho.]** El resto de la aplicación no carga nada de terceros.

## 10. Cookies y almacenamiento en tu dispositivo

Una **cookie** es un pequeño fichero que el navegador guarda en tu dispositivo. Además, la aplicación guarda dos cosas más en tu navegador: una preferencia y una copia de los ficheros de la propia aplicación.

**ResidApp no usa cookies de publicidad, ni de análisis, ni de terceros. Todas las cookies de esta tabla son técnicas: sin ellas la aplicación no puede funcionar o no puede ser segura. Por eso no te pedimos que las aceptes.** La única preferencia que no es imprescindible es el tema claro u oscuro, que se guarda solo cuando tú lo eliges.

Dónde se pone cada cookie: solo en el sitio de la aplicación, sin compartirse con otros sitios. Todas llevan la marca «Secure» (solo viajan por conexión cifrada), salvo en el entorno local de desarrollo.

### Cookies de la aplicación

| Nombre | Para qué | Cuándo se crea | Cuánto dura |
|---|---|---|---|
| `residapp_sesion` | Mantiene tu sesión abierta. Va cifrada y firmada. | Al iniciar sesión | Hasta que cierres el navegador o pulses «Salir». Además, el servidor deja de aceptarla a los **30 minutos sin actividad** o a las **12 horas**, lo que ocurra antes |
| `residapp_active_scope` | Recuerda el centro y el perfil con el que estás trabajando. No da acceso por sí sola: el servidor comprueba siempre tu permiso | Al elegir centro y perfil (personal y familias) | Hasta que cierres el navegador, cambies de perfil o pulses «Salir» |
| `residapp_clinical_access` | Solo para Dirección Clínica: recuerda la declaración que haces antes de consultar el detalle clínico de una persona residente | Al hacer esa declaración | Hasta **1 hora**; se borra al cambiar de perfil o al salir |
| `residapp_transfer_history` | Solo para Enfermería y Medicina: recuerda que has pedido ver el historial anterior de una persona residente trasladada de unidad | Al pedirlo | Hasta **1 hora**; se borra al cambiar de perfil o al salir |
| `residapp_acceso_pendiente` | Recuerda que ya has puesto bien la contraseña y falta el segundo paso (código). Va cifrada | Entre la contraseña y el código | **10 minutos** |
| `residapp_invitacion` | Recuerda la invitación que estás usando para crear tu acceso. Va cifrada | Al abrir el enlace de invitación | **30 minutos** |
| `residapp_passkey` | Guarda el paso intermedio al entrar o registrar una llave de acceso. Va cifrada y se usa una sola vez | Al entrar con llave de acceso o al registrarla | **5 minutos** |
| `.AspNetCore.Antiforgery.…` **[PENDIENTE: confirmar el nombre exacto en el navegador]** | Impide que otra web envíe formularios en tu nombre (protección «antifalsificación») | Al abrir una pantalla con formulario | Hasta que cierres el navegador |
| `.AspNetCore.Mvc.CookieTempDataProvider` **[PENDIENTE: confirmar el nombre exacto en el navegador]** | Lleva un mensaje breve de una pantalla a la siguiente (por ejemplo, «Datos guardados») | Tras algunas acciones | Se borra al mostrarse el mensaje |

### Almacenamiento en el navegador que no son cookies

| Nombre | Para qué | Cuánto dura |
|---|---|---|
| `residapp-tema` (almacenamiento local) | Recuerda si prefieres tema claro, oscuro o automático. Solo se guarda cuando tú lo eliges en el menú «Tema». No lleva datos personales | Hasta que lo cambies o borres los datos del sitio |
| `residapp-shell-v9` (copia del navegador) | Una copia de los ficheros de la propia aplicación (estilos, iconos y la página «sin conexión») para que la página de aviso cargue sin cobertura. **No guarda datos personales ni clínicos** y la aplicación no funciona sin conexión | Hasta que se publique una versión nueva, que la sustituye |

La aplicación no usa «almacenamiento de sesión» ni bases de datos locales del navegador. Tampoco guarda en tu dispositivo ninguna copia de datos de residentes.

**Cómo borrarlas.** Puedes borrar las cookies y los datos del sitio desde los ajustes de tu navegador. Si lo haces, tendrás que volver a iniciar sesión.

**Otras cosas del dispositivo.** Si usas una llave de acceso (passkey), la clave secreta queda en tu móvil u ordenador y **nunca llega a ResidApp**; ResidApp solo guarda la clave que sirve para comprobarla. Si usas el dictado, el navegador te pide permiso para el micrófono; ese permiso lo gestiona tu navegador.

**Documentos de trabajo de ResidApp.** Hay un acceso reservado al equipo del proyecto que crea una cookie técnica más (`residapp_cj_acceso`). No afecta a residentes, familias ni personal. **[PENDIENTE: retirar esta línea si ese acceso desaparece antes de producción.]**

## 11. Seguridad

Medidas principales, explicadas sin tecnicismos:

- **Solo entra quien tiene cuenta.** Todas las pantallas piden iniciar sesión, salvo esta página, el manual, la ayuda y la pantalla de entrada. Las cuentas las crea el centro, no se pueden crear por cuenta propia.
- **Permiso para cada cosa.** El servidor comprueba en cada petición quién eres, con qué perfil actúas y a qué residentes y unidades llega tu permiso. **Lo que no está permitido expresamente, se deniega.**
- **Cada centro, separado.** Los datos de un centro no se mezclan con los de otro: las reglas están en el servidor y en la propia base de datos.
- **Contraseña larga y segundo paso.** La contraseña debe tener al menos 12 caracteres. El personal usa además un **segundo factor**: un código temporal que genera una aplicación en su móvil, que se pide en cada entrada. El personal puede entrar también con una **llave de acceso («passkey»)**, que es una credencial que se desbloquea en tu dispositivo con la huella, la cara o el PIN y que solo funciona en la web de ResidApp (disponible cuando el servicio la tiene activada). Las familias entran con usuario y contraseña.
- **Frenos a los intentos repetidos.** Tras 5 fallos seguidos (contraseña o código), la cuenta se bloquea 15 minutos. También se limitan los intentos por dirección IP.
- **Entrega de accesos.** Las invitaciones para crear un acceso son de un solo uso, caducan a las 72 horas y el centro puede anularlas.
- **Sesiones con tiempo límite** y que se pueden cerrar desde «Mi cuenta». Cerrar sesión la invalida también en el servidor.
- **Registro de actividad.** Quedan anotadas, sin posibilidad de modificarlas ni borrarlas, las acciones sobre datos de salud y las consultas sensibles. Ese registro permite detectar accesos indebidos.
- **Cifrado.** Las conexiones van cifradas (HTTPS) y la base de datos de Azure cifra los datos almacenados.
- **Pensado para no sobrar datos.** Los avisos por correo no llevan datos de salud, y el dictado no envía el nombre de la persona.

Ningún sistema es infalible. Si hay una brecha de seguridad que afecte a tus datos, el centro (responsable) lo gestionará con ResidApp conforme a la ley, y te avisará cuando proceda.

## 12. Tus derechos y cómo ejercerlos

Tienes derecho a:

- **Acceso:** saber qué datos tuyos se tratan y recibir una copia.
- **Rectificación:** corregir datos incorrectos o incompletos.
- **Supresión:** pedir que se borren tus datos. Si lo pides, el centro borra tu nombre, teléfono y correo en ResidApp y cierra tu acceso. Lo que la ley manda conservar (la documentación clínica y los registros de seguridad) se conserva mientras dure la obligación. **[PENDIENTE: una persona experta decide si, además, hay que guardar aparte los datos suprimidos (bloqueo, art. 32 LOPDGDD) y durante cuánto tiempo]**
- **Oposición:** oponerte a un tratamiento basado en el interés legítimo o en una misión pública.
- **Limitación:** pedir que se congele el uso de tus datos mientras se resuelve una duda o reclamación.
- **Portabilidad:** recibir los datos que nos diste en un formato de uso común, cuando la base del tratamiento sea tu consentimiento o un contrato.
- **Retirar tu consentimiento** cuando el tratamiento se base en él (por ejemplo, la foto).

**Cómo ejercerlos: dirígete a tu centro**, que es el responsable. Puedes hacerlo directamente o por medio de un representante.

- Centro: **[PENDIENTE: nombre del centro, domicilio y correo o ventanilla para ejercer derechos — lo rellena cada centro]**
- Delegado de protección de datos del centro: **[PENDIENTE: nombre y contacto, o «no designado»]**

Si lo prefieres, puedes escribir a ResidApp (apartado 1). ResidApp trasladará tu petición al centro y le ayudará a contestar. **[PENDIENTE: ResidApp confirma este compromiso en el contrato de encargado con cada centro.]**

El centro debe contestar en el plazo de **un mes**, ampliable en dos meses más si la petición es compleja, avisándote del motivo.

Para las **personas residentes con capacidad disminuida**, los derechos los puede ejercer su representante legal. **[PENDIENTE: cada centro indica cómo se acredita la representación y cómo se atiende a las familias de personas fallecidas.]**

## 13. Reclamación ante la autoridad de protección de datos

Si crees que tus derechos no se han respetado, puedes reclamar ante la autoridad de protección de datos que corresponda a tu centro:

- Autoridad: **[PENDIENTE: Agencia Española de Protección de Datos (aepd.es) o Autoritat Catalana de Protecció de Dades (apdcat.gencat.cat), según el tipo de centro; lo confirma cada centro]**

Recomendamos hablar antes con el centro, pero no es obligatorio.

## 14. Cambios en esta política

Si cambia lo que ResidApp hace con los datos, actualizaremos esta página y la fecha de arriba. Si el cambio es importante, el centro te informará de otra forma.

Esta página está solo en castellano.

---

# PARTE B — NOTAS PARA QUIEN VALIDE (no se publica)

## B1. Conclusión

**Admisible con condiciones, y no publicable tal cual.** El texto de la parte A describe lo que el repositorio hace hoy. Lo que impide publicarla es que contiene decisiones que no son de ResidApp ni mías: identidad del prestador, contacto de privacidad, autoridad de control, plazos de conservación, región de Azure, garantías de transferencia y estado del dictado. Están marcadas **[PENDIENTE]**.

Los cinco riesgos más importantes, de más a menos grave:

1. **Una página común no sustituye la información del responsable (art. 13 RGPD).** La responsable es cada residencia. Esta página puede ser su capa de remisión, pero cada centro debe entregar la suya en el momento de la recogida (alta del residente, del familiar y del personal) y dar a la página los datos de su centro. Ver decisión D1.
2. **El código y el Manual dicen «5 años desde la baja» y la ley parece decir otra cosa** (B4, desajuste 1).
3. **Los datos de cuentas, familiares, sesiones, accesos y auditoría no se pueden borrar por diseño** (B4, desajuste 2). La sección 12 no promete borrado por esto; hace falta decidir cómo se atiende una supresión.
4. **Dos páginas públicas cargan Google Fonts** (B4, desajuste 3), contra la decisión de Orlando del 2026-10-29 de que todo sea local.
5. **El dictado con Mistral no tiene contrato adaptado a datos de salud ni evaluación de impacto** (`docs/seguridad/dictado-por-voz-dossier-dpo.md`, líneas 63 a 71 y 119). La sección 6 lo describe como opcional y condicionado; no debe publicarse como activo.

Recordatorio para quien lo valide: yo no soy DPO designado ni abogado.

## B2. Estado real de cada función descrita [Hecho]

La página debe decir solo lo que existe. Estado a 2026-11-03:

| Función | Estado en el repo | Cómo lo trata la parte A |
|---|---|---|
| Cuentas, sesión, contraseña, TOTP, códigos de recuperación, invitaciones, «Mi cuenta» | Hecho (ADR 0007, I0 a I3) | Descrito |
| Passkeys | Hecho, pero **solo si `Acceso:PasskeyDominio` está configurado** (ADR 0007, I4) | «cuando el servicio la tiene activada» |
| Segundo factor de familiares | **No**: solo contraseña (ADR 0007, tabla I1, «familiares solo con contraseña hasta que CJ responda la 3.2»). CJ eligió código por correo (ADR 0012, líneas 17 y 18) y **no está construido** | Dice «Las familias entran con usuario y contraseña» |
| Pulsera NFC con PIN (I5), NFC en camas | **No construido** (ADR 0007, línea 76; ADR 0009, línea 4) | No aparece |
| Aviso de propuesta de cita por correo | Construido, **apagado por defecto** (ADR 0012, línea 58) | «El centro puede tener activados…» |
| Aviso de cambio o cancelación | Construido, **apagado por defecto** (ADR 0012, línea 63) | Ídem |
| Recordatorio del día antes, código de acceso por correo | **No construidos** (ADR 0012, líneas 75 y 76) | No aparecen |
| Casilla «Dirección confirmada» | Construida el 2026-11-02, script `0084` (ADR 0012, línea 75) | Descrita |
| Registro de envíos y purga a 12 meses | Construido (`0081`, `0085`); la purga exige la conexión `ResidAppTareas` (`antes-de-datos-reales.md`, línea 105) | «Máximo 12 meses», con marcador |
| Dictado por voz | Construido, **sin clave de Mistral en Azure** (dossier, línea 144), **sin contrato ni EIPD** | Sección 6, con marcador de estado |
| Foto con consentimiento | Construida (`0059`, `0071`) | Sección 7 |
| Idioma catalán / cookie de idioma | **No construido** (ADR 0013, línea 3; no existe ninguna cookie de idioma en `src/`) | La página dice «solo en castellano»; no hay cookie de idioma en la tabla |
| Landings `.es` / `.cat` | No construidas (`antes-de-datos-reales.md`, línea 73) | Fuera de alcance de esta política (otro responsable: ResidApp; ver `landings-y-passkeys-por-subdominio.md`, línea 27) |

Si se activa alguna de las no construidas, hay que actualizar la página antes (por ejemplo, cookie de idioma, segundo factor de familias, recordatorio).

## B3. Cookies y almacenamiento: inventario verificado

### B3.1 Qué hay [Hecho]

Búsqueda en `src/` de `Response.Cookies`, `AddCookie`, `localStorage`, `sessionStorage`, `document.cookie`, `indexedDB` y `caches`. Resultado:

| Elemento | Dónde se define | Detalle |
|---|---|---|
| `residapp_sesion` | `SessionAuthentication.cs`, línea 12; `Program.cs`, líneas 157 a 167 | HttpOnly, SameSite=Lax, `ExpireTimeSpan` 30 min con renovación. Se abre con `SignInAsync` sin propiedades (`SessionSignIn.cs`, líneas 25 y 26), así que **no es persistente**: cookie de sesión del navegador. La caducidad absoluta de 12 h la aplica SQL (`appsettings.json`, líneas 18 a 21) |
| `residapp_active_scope` | `ActiveProfileScopeCookie.cs`, líneas 17 a 28 | HttpOnly, Lax, `IsEssential`, sin caducidad (de sesión). **Valor en Base64 de un JSON sin cifrar**: ids del ámbito y del centro, **nombre del centro** y perfil. No concede acceso (SQL revalida, líneas 9 a 14 del mismo archivo) |
| `residapp_clinical_access` | `ClinicalAccessDeclarationCookie.cs`, líneas 11 a 19 | Solo el id de la declaración. Duración de la declaración: 60 min (`appsettings.json`, línea 16) |
| `residapp_transfer_history` | `TransferHistoryCookie.cs`, líneas 10 a 18 | Solo el id de la petición. La petición vale 1 h (`historial-tras-traslado-01`, línea 38) |
| `residapp_acceso_pendiente` | `PendingAccessCookie.cs`, líneas 18, 19 y 27 | Cifrada por Data Protection, 10 min, `Path=/Acceso`, Strict |
| `residapp_invitacion` | `InvitationCookie.cs`, líneas 13, 14 y 20 | Cifrada, 30 min, `Path=/Acceso`, Strict. Lleva el resumen del token, no el token |
| `residapp_passkey` | `PasskeyStateCookie.cs`, líneas 24, 25 y 32 | Cifrada, 5 min, un solo uso, Strict |
| `residapp_cj_acceso` | `PendientesCjAccess.cs`, líneas 14 y 52 a 58 | De sesión, HttpOnly, Lax. Solo se crea tras escribir la clave de `/pendientes-cj` |
| Antiforgery (`.AspNetCore.Antiforgery.*`) | **No configurado** (no hay `AddAntiforgery`); nombre por defecto del framework. Los POST llevan `[ValidateAntiForgeryToken]` (`docs/seguridad/hallazgos.md`, línea 450) | **[A verificar]** el nombre exacto con las herramientas del navegador sobre Azure |
| TempData (`.AspNetCore.Mvc.CookieTempDataProvider`) | **No configurado**; proveedor por defecto de MVC. Se usa en `AccesoController.cs` (líneas 49, 200, 410, 454) y en los controladores de Administración | **[A verificar]** el nombre exacto. Los mensajes que he visto no llevan nombres ni datos de salud; no he revisado todos |
| Política de cookies | `Program.cs`, líneas 191 a 194 y 420 | `Secure` siempre fuera de Development (hallazgo A2). No hay mecanismo de consentimiento (ni `ConsentCookie`) |
| `localStorage["residapp-tema"]` | `wwwroot/js/tema.js`, líneas 5, 11 y 46; también lo lee `wwwroot/acceso-y-sesion.html`, línea 20, y `flujo-de-trabajo.html`, línea 19 | Valores `claro`, `oscuro` o `auto`. Solo se escribe al pulsar una opción de «Tema» |
| Cache Storage `residapp-shell-v9` | `wwwroot/sw.js`, líneas 9 a 22 y 33 a 39 | Estáticos y `/Home/Offline`. Las cachés de versiones anteriores se borran en `activate`. No cachea POST. `Offline` no lee cookies ni muestra identidad (`directrices-pwa-movil.md`, líneas 42 a 44) |
| `sessionStorage`, `indexedDB`, `document.cookie` | **No hay** ninguno en `src/` (salvo las librerías de `wwwroot/lib`) | — |
| Cookie de idioma | **No existe** (ADR 0013 no construido) | Fuera de la tabla |

No hay analítica, publicidad, píxeles ni scripts de terceros en las vistas Razor ni en `wwwroot/js`; Bootstrap y jQuery se sirven de `wwwroot/lib`.

### B3.2 ¿Hace falta banner de consentimiento? [Suposición]

**No, para las cookies.** Son todas técnicas o de seguridad: autenticación, estado de ceremonias de acceso, protección CSRF y mensajes entre pantallas. El art. 22.2 de la LSSI exige consentimiento salvo que el almacenamiento sea para transmitir una comunicación o «estrictamente necesario» para un servicio que la persona ha pedido expresamente (BOE-A-2002-13758, texto consolidado con última actualización 23/01/2025; leído hoy a través de un resumen automático de la herramienta de lectura, no del texto literal).

**La única duda es el tema claro u oscuro (`residapp-tema`).** No es imprescindible. La actualización de la guía de cookies de la AEPD (11/07/2023, nota de prensa en aepd.es) considera técnicas y exentas las cookies de personalización cuando **el propio usuario decide** (idioma, moneda). La preferencia de tema encaja: solo se escribe al pulsar «Tema». **No he podido abrir el texto de la guía ni el art. 22.2 literal en fuente oficial: a verificar con abogado.** La información en la política (sección 10) cumple en todo caso el deber de transparencia.

**No es una cookie, pero afecta a la política:** `fonts.googleapis.com` y `fonts.gstatic.com` en dos páginas estáticas públicas (ver B4, desajuste 3).

### B3.3 Condiciones para que siga siendo cierto [Hecho + Suposición]

1. Toda cookie nueva exige actualizar la tabla (hoy no hay un inventario automático).
2. La cookie de idioma del ADR 0013 solo está exenta si la fija una elección en pantalla (`portal-familiar-dos-idiomas.md`, apartado (a)). Hay que añadirla a la tabla cuando exista y verificar el tope de caducidad con el abogado.
3. `Secure` no se aplica en Development; el entorno de Azure dev debería comprobarse con las herramientas del navegador porque las dos cookies sin nombre fijo (antiforgery y TempData) y la fecha de caducidad de cada una se afirman aquí por código, no por observación.
4. Servir las letras de Google en local antes de producción.

## B4. Desajustes entre lo que hace la app y lo que dicen los documentos

1. **Conservación «5 años desde la baja».** `ResidentDischarge.cs`, líneas 15 y 16 («CJ: … en Cataluña, 5 años»), `Manual.cshtml`, línea 433 («Los datos no se borran: se conservan 5 años desde la baja»), `0041_baja_y_suspension_residente.sql`, línea 4 y `hallazgos.md`, línea 428 dan por hecho 5 años. El texto consolidado de la Llei 21/2000, art. 12 (BOE-A-2001-2353; última actualización de la página 28/11/2025; artículo modificado por última vez por la Ley 16/2010, BOE-A-2010-10215), según el resumen automático que obtuve, distingue **al menos 15 años desde el alta de cada proceso asistencial** para ciertos documentos (consentimientos informados, informes de alta, informes de pruebas, etc.; apartado 4), y **5 años desde el alta** para el resto, que «pueden destruirse» (apartado 6), con excepciones por criterio de la comisión técnica (apartado 7). Además, el cómputo es **desde el alta de cada proceso asistencial**, no desde la baja del residente. **[A verificar]** el texto literal y si aplica a la documentación de una residencia (clínica o sociosanitaria) y no solo a un centro sanitario. Consecuencias: (a) la parte A **no afirma ningún plazo** de datos de salud; (b) la regla de 5 años del producto puede quedarse corta para los documentos de 15; (c) «los datos nunca se borran» sin vía de purga choca con el art. 5.1.e RGPD (limitación del plazo de conservación). **Acción:** el DPO real o un abogado fija el plazo por tipo de documento; después se construye la purga (hoy solo existe la de `envios_correo`, `0085`).
2. **Supresión y rectificación técnicamente limitadas.** Hay disparadores que impiden borrar: `cuentas` (`0023`, línea 35), `familiares` (`0022`, línea 45), `residentes_familiares` (`0022`, línea 77), sesiones (`0061`, líneas 9 y 45), accesos (`0062`, línea 53) y auditoría inmutable (`0002`, línea 727). La sección 12 de la parte A no promete borrado y habla de «bloqueo» (el art. 32 de la LOPDGDD obliga a bloquear al rectificar o suprimir; texto leído hoy en BOE-A-2018-16673, actualización 27/12/2025). Pero ni el bloqueo ni la supresión de un familiar o de una cuenta tienen hoy procedimiento. **[Decidir]** Orlando y el DPO: cómo se atiende (por ejemplo, anonimizar el nombre y el teléfono de un familiar desvinculado, conservando el vínculo). Una petición de supresión de un familiar con correo es el caso más probable. El antecedente es la vía de purga que se hizo para el motivo del historial y para `envios_correo`.
3. **Google Fonts en `acceso-y-sesion.html` (líneas 27 a 29) y `flujo-de-trabajo.html` (líneas 26 a 28).** Son públicas (`Program.cs`, línea 452) y se enlazan desde el pie del personal (`_Layout.cshtml`, líneas 188 y 189). Cada visita envía la IP a Google. Orlando decidió el 2026-10-29 que todo sea local (`antes-de-datos-reales.md`, línea 78), pero allí se hablaba de landings y pantallas de entrada. **Acción recomendada:** servir las letras desde `wwwroot` y retirar el párrafo de la sección 9. Mientras no se haga, el párrafo es obligatorio para no ocultarlo.
4. **«Política adoptada por cada residencia como plantilla»** (`antes-de-datos-reales.md`, línea 86) frente a «página común con remisión al centro» (encargo). Ver D1.
5. **Pie de los correos.** Dice «En el apartado «Privacidad» del portal tienes la información completa» y «el responsable es el centro que la registró» (`ProposalNoticeTemplate.cs`, línea 24). La parte A cubre ese compromiso (sección 5 y 12) pero, mientras sea una página común, la persona que recibe el correo **no sabe de qué centro** (el correo no nombra al centro, decisión de Orlando). Hoy debe preguntarlo a quien le diera el alta. Por eso D1 propone un bloque «Datos de tu centro».
6. **El pie del correo sale sin el titular de ResidApp** (decisión de Orlando 2026-11-02, `antes-de-datos-reales.md`, línea 106). Es requisito previo a datos reales y condiciona el apartado 1 de la parte A.
7. **«Registro técnico del servidor».** `hallazgos.md`, línea 309, dice que los logs de la Web App están desactivados (a fecha del hallazgo). La parte A deja la sección 2.4 con marcador. Cuando se activen (A4), decidir qué se registra (IP, ruta) y su plazo.
8. **Consistencia con otros borradores.** La parte A usa las mismas decisiones que `aviso-cita-por-correo.md`, `aviso-cita-cambio-cancelacion-por-correo.md` y `direccion-confirmada-familia.md`, con una excepción: aquellas hablan de «dirección confirmada» con base 6.1.b o 6.1.f sin decidir; la parte A deja la base abierta.
9. **Dos documentos de privacidad quedaron obsoletos por esto, no los modifico:** `antes-de-datos-reales.md` (línea 86, política «adoptada por cada residencia»; falta una entrada «Política de privacidad del portal» en su lista), y `aviso-cita-por-correo.md` (línea 166: «Hoy `Views/Home/Privacy.cshtml` está sin redactar»).

## B5. Base jurídica y art. 9 (orientación para el DPO) [Suposición]

La responsable es la residencia, así que **la base la elige y documenta ella**. Lo que propongo para que lo contraste su DPO:

| Tratamiento | Base art. 6 | Condición art. 9 | Notas |
|---|---|---|---|
| Datos de residentes (identidad, basal, cuidados, incidencias, derivaciones) | 6.1.b o 6.1.c (contrato/obligación legal de documentación clínica); 6.1.e si el centro es público | 9.2.h (asistencia sanitaria o social, bajo secreto profesional, art. 9.3) con DA 17.ª LOPDGDD, Ley 41/2002 y Llei 21/2000 | Mismo planteamiento que `historial-tras-traslado-01`, filas «Base jurídica» y «Condición del art. 9.2». **[A verificar]** la cita exacta de la ley catalana |
| Comunicados a familias | 6.1.b o 6.1.f | 9.2.h si llevan datos de salud | El contenido lo decide el personal; política de comunicados aparte |
| Cuentas y datos de familiares | 6.1.b (representantes, partes), 6.1.f (resto), 6.1.e (público) | — | Igual que `aviso-cita-por-correo.md`, apartado 2.1 |
| Correo de la familia (avisos) | 6.1.b / 6.1.f / 6.1.e; **sin consentimiento**, porque el aviso es operativo y no comercial | — | LSSI: **[A verificar con abogado]** que un aviso de servicio no es comunicación comercial |
| Cuentas del personal, auditoría, registro de accesos con IP | 6.1.f (seguridad) o 6.1.c (art. 32 RGPD) | — | Mismo debate que `antes-de-datos-reales.md`, línea 63. Derechos digitales laborales de la LOPDGDD: **artículos a verificar**, no los cito de memoria |
| Foto | 6.1.a (consentimiento) | — (no es dato biométrico mientras no se use para reconocimiento) | Firma de la persona o su representante; ¿capacidad? Cómo se acredita queda para el centro |
| Dictado (voz y texto a Mistral) | Depende de la EIPD | 9.2.h | Cadena centro → ResidApp → Mistral sin firmar (dossier, apartado 4, línea 83). **No activar con datos reales sin contrato ni EIPD** |

**Evaluación de impacto (EIPD):** hace falta antes de producción (voz y salud, acceso con NFC y pulsera, Portal Familiar), según las puertas abiertas del proyecto. Esta política no la sustituye.

**Autoridad de control [A verificar]:** la AEPD con carácter general. La APDCAT se ocupa también de entidades privadas que prestan servicios públicos «mediante cualquier forma de gestión directa o indirecta», pero solo en lo vinculado a esa prestación (art. 3.f de la Ley 32/2010, BOE-A-2010-16136, leído a través de un resumen automático), y de quienes ejercen funciones públicas en materias de la Generalitat con tratamiento en Cataluña (art. 3.h). Una búsqueda mostró una resolución de la APDCAT sobre una fundación de servicios sociales bajo el art. 3.h; **no la he abierto**. Si el centro piloto es público, concertado o de gestión indirecta de un servicio público (lo habitual en residencias catalanas), probablemente corresponde la APDCAT. **Lo decide el DPO o un abogado, con el tipo de centro a la vista.** Si es público o concertado, además se aplica el Esquema Nacional de Seguridad (RD 311/2022).

**Plazo de respuesta a derechos:** un mes, ampliable en otros dos (art. 12.3 RGPD, leído hoy en la versión del BOE del DOUE, DOUE-L-2016-80807). No he podido abrir el art. 77 (reclamación) literal: lo cito por su contenido general, **a verificar con EUR-Lex o el BOE**.

## B6. Riesgos y observaciones por sección

1. **Sección 2.2 (marca de dirección confirmada).** Cuenta y hora de quien marca son datos del personal. La política menciona la marca; la información al personal y la base están en `direccion-confirmada-familia.md`, riesgo 2 y apartado 6.
2. **Sección 2.3 (auditoría).** La frase «ResidApp no incluye listas ni clasificaciones de personal por rendimiento» es cierta para el producto (alcance.md, línea 47: fuera de alcance «Rankings nominativos de productividad»; `antes-de-datos-reales.md`, línea 61: sin ranking por cuenta ni exportación). Si el centro usa la auditoría de otro modo, es decisión suya y de su asesoría laboral. Debe informarse al personal por separado (`historial-tras-traslado-03`, parte 2).
3. **Sección 4 (acceso técnico de ResidApp).** El administrador de la base es `db_owner` y se salta la seguridad por filas (`hallazgos.md`, línea 296). Hay además 8 tablas sin seguridad por filas, entre ellas `eventos_auditoria` (`hallazgos.md`, línea 328; a fecha del hallazgo, **[A verificar]** que sigue así). El contrato de encargado debe fijar quién tiene ese acceso, confidencialidad y registro (art. 28.3.b). El marcador de la sección 4 obliga a decidirlo.
4. **Sección 5 (correo).** Microsoft como subencargado y sus datos de rebote y de entrega: ver `aviso-cita-por-correo.md`, apartado 2.6. Pendiente de Orlando el paso B10 (seguimiento apagado y registros de entrega a 30 días en un Log Analytics propio de la UE). La frase «ResidApp no mide si abres el correo» es cierta solo si B10 se aplica (seguimiento de aperturas apagado en el dominio `avisos.residapp.es`); **es un paso de Orlando aún sin hacer** (`antes-de-datos-reales.md`, línea 104). Hasta entonces, rebajarla o cumplir B10 antes de publicar. Los correos salen con `Reply-To` y `Sender` añadidos por Azure con la misma dirección que `From` (`aviso-cita-por-correo.md`, línea 187).
5. **Sección 6 (dictado).** Todo lo que atañe a la retención de Mistral es [A verificar]: el DPA indica «categorías especiales: ninguna» y no menciona datos de salud, entrenamiento ambiguo, retención «durante el contrato» salvo retención cero (dossier, líneas 57 a 71). La frase «según su información pública, trata los datos en la UE» sale del dossier, apartado 3 (línea 46). La URL fija del adaptador es `https://api.mistral.ai/` (`IaOptions.cs`, línea 42); **[A verificar]** qué región usa ese punto de acceso: el dossier dice que por defecto es la UE y que existe un punto de EE. UU. que no se usa. El audio y el texto se envían siempre en claro por HTTPS (`MistralClient.cs`, líneas 36 a 46 y 52 a 71). Que ResidApp «no guarda» audio ni texto no está probado por una prueba automática (dossier, línea 97: «por diseño»).
6. **Sección 7 (foto).** Fotografía + sexo + fecha de nacimiento + nombre en una base de residentes: es dato personal reforzado por el contexto, aunque no sea biométrico mientras no se use para reconocimiento. Solo la ven cuatro perfiles (`0059`, líneas 5 a 8). Una foto se renueva a los 12 meses (`ResidentPhoto.cs` / `ResidentPhotoConsent.cs`, línea 29); es un aviso, no un borrado. **[Decidir]** CJ y el DPO si se debe borrar al cabo de un plazo.
7. **Sección 8 (plazos).** Seis filas con [PENDIENTE]. La más importante es la de la documentación clínica (B4.1). `eventos_acceso` guarda IP y no tiene plazo (ADR 0007, línea 104).
8. **Sección 9 (Google Fonts).** Obligada por B4.3 hasta que se corrija. La política no puede decir «no se carga nada de terceros» mientras eso exista.
9. **Sección 11 (seguridad).** Descripción de medidas verificadas: contraseña de 12 o más (ADR 0007, línea 70), TOTP obligatorio al personal (línea 70), bloqueo 5 fallos y 15 minutos (línea 70), invitación de 72 h (línea 71), sesión 30 min y 12 h (`appsettings.json`, líneas 18 a 21), cookies HttpOnly y Secure (`Program.cs`, líneas 158 a 162 y 191 a 194), TDE (`hallazgos.md`, línea 83), auditoría inmutable (`hallazgos.md`, línea 313). **Dos límites que la política no menciona a propósito:** el TOTP se guarda en claro en `dbo.credenciales` (ADR 0007, línea 97) y las familias entran sin segundo factor. No son datos que haya que decir en una política de privacidad, pero el DPO debe conocerlos: son puertas abiertas del proyecto.
10. **Sección 12 (derechos).** Compromiso de que ResidApp traslade las peticiones al centro: es una obligación de asistencia del encargado (art. 28.3.e), pero hoy **no hay contrato de encargado firmado con ningún centro**. Por eso lleva marcador.
11. **Idioma.** Solo castellano, por ahora. La referencia a la normativa catalana de documentación clínica está en B4.1 y B5; la parte A no la cita en el texto público porque la cita exacta depende de que el DPO confirme cuál aplica. Puedo redactar la versión catalana cuando se pida, tras el ADR 0013.

## B7. Notas de implementación para la vista (sin tocar código)

- Sustituir el contenido de `Views/Home/Privacy.cshtml` por la parte A, **sin** los marcadores. Un test de la vista debería fallar si el texto contiene la cadena `[PENDIENTE`.
- Añadir la fecha de la última actualización como dato de la vista, no en el texto.
- Si se opta por D1.b, el bloque «Datos de tu centro» se alimenta con datos que hoy no existen en la base (contacto de privacidad, DPO, autoridad); sería un cambio de esquema y de pantalla de Administración o Plataforma, y pasa por el agente `dpo-residapp` otra vez.
- La vista usa `_Layout` o `_LayoutFamilia` según el perfil; no se necesita `[AllowAnonymous]` adicional: `HomeController` ya lo lleva (`HomeController.cs`, línea 12).
- Convertir las tablas de 8, 9 y 10 en tarjetas en pantallas estrechas.
- Después de publicarla, consultar al agente `doc-usuario-residapp` (regla de `CLAUDE.md`) para el Manual (líneas 156 y 433).

## A verificar o decidir

Cada punto con su responsable.

### Decisiones de producto y de ResidApp

| N.º | Qué | Quién decide |
|---|---|---|
| D1 | **Modelo de la página:** (a) página común sin datos de centro, con remisión «pregunta a quien te dio el alta»; (b) página común más un bloque «Datos de tu centro» (nombre, contacto, DPO, autoridad) que se rellena por centro; (c) cada centro adopta la plantilla como suya. Recomiendo (b) para el piloto y (c) para producción con varios centros. La opción (a) deja a quien recibe el correo sin saber a quién dirigirse | Orlando, con el DPO real |
| D2 | **Identidad del prestador** (persona física o sociedad, NIF, domicilio, correo): sección 1. Es previo a cualquier dato real (`antes-de-datos-reales.md`, línea 106) | Orlando |
| D3 | **Contacto de privacidad de ResidApp** y si se designa DPO propio o se contrata externo (art. 37 RGPD, a verificar si es obligatorio por tratamiento a gran escala de datos de salud) | Orlando, con asesoría |
| D4 | **Plazos de conservación** de entradas con IP, auditoría, sesiones, registros de entrega de Azure, copias de seguridad y registros del servidor (filas de la sección 8) | La residencia con su DPO real; ResidApp ejecuta |
| D5 | **Región de Azure** y de Mistral y garantías de transferencia | Orlando (técnico) y DPO (jurídico) |
| D6 | **Letras de Google en local** antes de producción | Orlando |
| D7 | **Paso B10 en Azure** (seguimiento de aperturas apagado y registros de entrega a 30 días en la UE) antes de publicar la frase de que ResidApp no mide aperturas | Orlando |
| D8 | **Supresión y bloqueo** de familiares, cuentas y registros: procedimiento y, si hace falta, cambio de código | Orlando, CJ y DPO |
| D9 | **Quién tiene acceso técnico a la base** de ResidApp y con qué controles | Orlando |

### Para el DPO real o un abogado

| N.º | Qué | Detalle |
|---|---|---|
| J1 | **Plazos legales** de conservación de la documentación clínica en una residencia de Cataluña (Llei 21/2000 art. 12; desde el alta de cada proceso, 15 y 5 años) y si la regla de «5 años desde la baja» del producto es correcta | **A verificar** en el texto literal; los datos de la parte B proceden de un resumen automático de BOE-A-2001-2353 |
| J2 | **Bases jurídicas y condición del art. 9** de cada tratamiento (tabla B5) | La fija la residencia |
| J3 | **Autoridad de control** (AEPD o APDCAT) según el tipo de centro piloto y ENS si es público o concertado | Art. 3.f y 3.h de la Ley 32/2010 |
| J4 | **LSSI, art. 22.2:** que todas las cookies son técnicas y que la preferencia de tema encaja en la excepción de personalización elegida por la persona; y que el aviso por correo no es comunicación comercial | No he podido abrir la guía de la AEPD ni el artículo literal |
| J5 | **Contrato de encargado** (art. 28) con cada centro: asistencia en derechos, subencargados (Azure, ACS, Mistral), aviso de cambios, acceso técnico, devolución o borrado al terminar | No existe ninguno firmado |
| J6 | **Mistral:** DPA que cubra datos de salud, entrenamiento desactivado, retención cero, región y versión vigente del DPA (12/03/2026 o 27/07/2026) | Dossier, líneas 70 a 81 |
| J7 | **Microsoft ACS:** Anexo de protección de datos, garantía de transferencias, retención de las direcciones de rebote | `aviso-cita-por-correo.md`, apartado 2.6 |
| J8 | **Información al personal y a su representación** sobre auditoría, marca de dirección y dictado; derechos digitales laborales (artículos a verificar) | Asesoría laboral |
| J9 | **Art. 21 RGPD** (oposición) y **art. 77** (reclamación): contraste del texto literal en EUR-Lex o BOE | Solo he visto reproducciones y un tramo del BOE |
| J10 | **Fallecidos:** cómo se atiende el acceso de familiares a datos de una persona fallecida (LOPDGDD, artículo a verificar) | Marcador de la sección 12 |
| J11 | **EIPD** previa a producción: voz y salud, acceso con NFC y pulsera, Portal Familiar | Puerta abierta del proyecto |

### Para CJ

| N.º | Qué |
|---|---|
| C1 | Que lo que dice la política de las funciones clínicas coincide con el producto: no se guarda medicación (alcance.md, línea 45; ADR 0011, líneas 76 y 77), la familia no ve el historial clínico, el dictado es solo una sugerencia |
| C2 | Si la foto debe borrarse tras un plazo y no solo avisar de renovar a los 12 meses |

### Pendiente técnico antes de publicar

- Confirmar en el navegador, sobre Azure, el nombre exacto de la cookie de antifalsificación y la de TempData, y que todas las cookies llevan `Secure`.
- Confirmar qué registros HTTP y de aplicación se activan y su retención (hallazgo A4).
- Quitar el párrafo de Google Fonts cuando el cambio esté hecho.
- Retirar la fila `residapp_cj_acceso` cuando desaparezca el acceso a `/pendientes-cj` del entorno de producción.

## Cambios al pasarlo a la vista (2026-11-03, tras la revisión de `doc-usuario-residapp`)

Se ajustó el texto de `Privacy.cshtml` (no esta parte A, que queda como la redactó el agente) para que no contradiga lo que hace la app: (1) las guías de pruebas de `/pendientes-cj` también cargan letras de Google y guardan un borrador en `localStorage` (`residapp-pendientes-cj:<guía>`); (2) las sesiones guardan también la IP; (3) la cookie de sesión puede recuperarse al reabrir el navegador; (4) el cifrado se describe como cifrado en disco de la base, más contraseñas transformadas; (5) la entrega de la información del centro al alta lleva marcador, porque el Manual no tiene ese paso. Se quitó la frase «ResidApp no mide si abres el correo» (depende de B10). **Decisiones que siguen abiertas, no resueltas por esos ajustes:** plazo «5 años desde la baja» del Manual y de `ResidentDischarge.cs` frente a la política (que lo deja abierto; no hay código que borre al cumplirse `conservar_hasta`); frases «no se borra» de la auditoría, las sesiones y `eventos_acceso` en el Manual y en «Acceso y sesión» cuando se fije un plazo; TOTP guardado en claro (`acceso-y-sesion.html:1140`).

## Fuentes consultadas el 2026-11-03

Repositorio (archivos y líneas citados). Fuentes externas, **todas leídas a través de la herramienta de lectura web, que devuelve resúmenes automáticos y no el texto literal**: por eso las cifras y los artículos de esta parte se marcan «a verificar» antes de usarlos en un documento formal.

- BOE, Ley 21/2000 (Cataluña), texto consolidado, art. 12: `https://www.boe.es/buscar/act.php?id=BOE-A-2001-2353` (última actualización de la página, 28/11/2025). Modificación: Ley 16/2010, `https://www.boe.es/buscar/doc.php?id=BOE-A-2010-10215`. Ley 2/2024 modifica el art. 8 según una búsqueda; no aparece que cambie el art. 12.
- BOE, Ley 32/2010 de la Autoridad Catalana de Protección de Datos, art. 3: `https://www.boe.es/buscar/act.php?id=BOE-A-2010-16136`.
- BOE, Ley 34/2002 (LSSI), art. 22.2: `https://www.boe.es/buscar/act.php?id=BOE-A-2002-13758` (actualización 23/01/2025).
- BOE, LO 3/2018 (LOPDGDD), arts. 12 a 18 y 32: `https://www.boe.es/buscar/act.php?id=BOE-A-2018-16673` (actualización 27/12/2025).
- BOE (DOUE), Reglamento (UE) 2016/679, art. 12.3: `https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807`.
- AEPD, nota de prensa de la actualización de la guía de cookies (11/07/2023): `https://www.aepd.es/prensa-y-comunicacion/notas-de-prensa/aepd-actualiza-guia-cookies-para-adaptarla-a-nuevas-directrices-cepd`. La guía misma no se abrió.
- Datos de Microsoft ACS y de Mistral: no consultados de nuevo; proceden de `docs/privacidad/aviso-cita-por-correo.md` (consultado el 2026-10-30) y `docs/seguridad/dictado-por-voz-dossier-dpo.md` (2026-10-23). Los términos de un proveedor cambian: releerlos antes de publicar.
