# Código de acceso por correo para la familia (ADR 0012, pieza 3): lectura de privacidad antes de construirlo

**BORRADOR del agente `dpo-residapp`. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-26. Es una segunda lectura experta, no un dictamen.

Etiquetas: **[Hecho]** comprobado en el repositorio o en el texto de una norma; **[Suposición]** mía; **[Decidir]** lo fija una persona. Los números de línea son de esta fecha.

**Decisiones de Orlando posteriores a esta lectura (2026-11-26):** están en el [ADR 0012](../decisiones-arquitectura/0012-envio-correos.md), punto 5 de los requisitos, y mandan sobre lo que aquí figure como pendiente de Orlando o de CJ (a CJ se le informa, no se le pregunta). Las puertas del DPO real o abogado siguen abiertas.

Términos, una frase cada uno: **segundo factor** es una segunda prueba además de la contraseña; **código de un solo uso** es un número que sirve una vez y caduca; **suplantación en tiempo real** (*phishing*) es una web falsa que pide la contraseña y el código y los usa al momento; **resumen con clave** (HMAC) es una huella del código que solo puede calcular quien tiene un secreto del servidor; **seguridad por filas** es la regla de la base de datos que solo deja ver las filas del centro de quien está conectado.

## 1. Conclusión

**Admisible con condiciones.** Pedir contraseña y código por correo es mejor que lo que hay hoy (solo contraseña) y es proporcionado para el piloto, con lo que el portal enseña ahora. **No es un factor fuerte** ni resiste una web falsa, y así debe constar en la evaluación de impacto como riesgo residual aceptado por la residencia.

Se puede construir **ya, con datos inventados y el interruptor apagado**. No se puede encender con familias reales sin las condiciones del apartado 9.

Los cuatro puntos que cambian el diseño provisional:

1. **La contraseña correcta no puede poner a cero los fallos.** Hoy, en una cuenta de familia, los pone a cero (`AccesoController.cs:101`). Si se deja así, quien sepa la contraseña prueba códigos sin límite efectivo (hallazgo H1).
2. **La pantalla de Administración y la política dicen que el correo «solo sirve para avisos» y mandan borrarlo si la familia no quiere avisos.** Con el código, borrarlo deja a la persona sin portal (hallazgo H2).
3. **El cambio de dirección** necesita aviso a la dirección anterior, cierre de sesiones y códigos ligados a la dirección (hallazgo H3).
4. **Nunca una vuelta automática a «solo contraseña»** por no tener correo, por un fallo del envío o por un ajuste mal puesto (hallazgo H4). Y el correo **no puede usarse nunca para recuperar la contraseña**: si se hiciera, el buzón bastaría para entrar y habría un solo factor.

## 2. ¿Es admisible y proporcionado?

**Norma.**

- RGPD art. 32.1 (texto consolidado 2016-05-04, no oficial): medidas «para garantizar un nivel de seguridad adecuado al riesgo». No impone un factor concreto. El art. 32.2 obliga a mirar el riesgo de «comunicación o acceso no autorizados».
- El portal enseña información sobre la salud de personas vulnerables (comunicados, actualizaciones tras una derivación, citas externas, a veces con especialidad). Es categoría especial (art. 9.1). La lista de la AEPD de tratamientos que exigen evaluación de impacto incluye los criterios 4 (categorías especiales) y 9 (sujetos vulnerables): con dos o más, «en la mayoría de los casos» hay que hacerla. El código es una medida **dentro** de la evaluación del Portal Familiar, no una evaluación aparte [Suposición].
- ADR 0007, línea 27: «el correo o el SMS no se consideran equivalentes a un factor resistente al phishing».
- Como referencia técnica, no es ley española: NIST SP 800-63B-4, apartado 3.1.3, dice «Email SHALL NOT be used for out-of-band authentication» (el correo no debe usarse como segundo factor fuera de banda), y aclara que los códigos para comprobar una dirección no quedan afectados. El propio código cita esa guía para la contraseña (`AccesoOptions.cs:17`). Consultado el 2026-11-26 en `https://pages.nist.gov/800-63-4/sp800-63b.html`, a través de una herramienta de lectura web; contrastar antes de citarlo fuera.
- **Si la residencia es pública, o concertada dentro de un servicio público**, entra el Esquema Nacional de Seguridad (RD 311/2022, BOE actualizado el 2026-10-30; LOPDGDD, disposición adicional primera.2). Su medida `op.acc.5` (usuarios externos) pide en nivel medio y alto «[R2 o R3 o R4] + R5»: contraseña más código de un solo uso, o certificado, y además registrar accesos e «informar al usuario del último acceso efectuado con su identidad». El texto del real decreto no dice por qué canal llega el código; si las guías CCN-STIC admiten el correo está **a verificar**. Aplicarían también `op.acc.5.2` (la persona reconoce sus obligaciones) y `op.acc.5.9` (se le informa al entrar).

**Qué protege y qué no.**

| Situación | ¿Protege? |
|---|---|
| Alguien adivina o conoce la contraseña, pero no entra en el buzón | Sí |
| Alguien encuentra la hoja de invitación sin usar | Sí para entrar (el código va al correo de la ficha). Aún puede cambiar la contraseña y dejar fuera a la persona, que recibe el correo del código y se entera |
| La persona usa la misma contraseña en su correo y en ResidApp | **No**: cae todo a la vez. Hay que decírselo |
| Buzón compartido (pareja, hermanos, expareja con acceso) | No frente a quien comparte el buzón |
| Web falsa que pide contraseña y código | No |
| Móvil ajeno o desbloqueado con el correo abierto | No frente a quien además sepa la contraseña |
| Quien administra la ficha cambia la dirección | Solo si además sabe la contraseña. Administración ya puede tomar una cuenta con una invitación de recuperación: el código no le da un poder nuevo |

**Base jurídica del correo para este fin [Decidir DPO].** La misma que la cuenta del familiar: art. 6.1.b si es parte del contrato, 6.1.f en otro caso, 6.1.e si el centro es público; en los tres, como medida de seguridad del art. 32. **No es consentimiento.** El correo en sí no es dato de salud.

**Mejora posterior recomendada:** ofrecer a las familias, de forma voluntaria, la llave de acceso (*passkey*, huella o cara del propio móvil) o la aplicación de códigos, que ya están construidas para el personal. Son más fuertes, no necesitan correo y sirven de vía para quien no lo tiene. El propio ADR 0012 (recomendación de ingeniería) aconsejaba decidirlo junto con el código.

## 3. Hallazgos, por orden de gravedad

### H1. Límite de intentos: tal como está el código, no aguantaría un código de 6 cifras

- **[Hecho]** Una cuenta de familia pone a cero los fallos y abre sesión al acertar la contraseña (`AccesoController.cs:99-103`), y también al usar una invitación (`:203-206`). El personal solo pone a cero al completar el segundo factor (`:418-423`).
- **Riesgo.** Si se mantiene el reinicio de la línea 101, se prueban cuatro códigos, se repite la contraseña y se empieza de nuevo.
- **Aun corregido, el bloqueo actual no basta solo** (cálculo mío): 5 fallos cada 15 minutos son hasta 480 intentos al día; contra un código de 6 cifras, del orden de un 16 % de acierto acumulado en un año de insistencia. Con un tope diario cae muy por debajo.
- **Acción.** (i) Reiniciar fallos solo al validar el código. (ii) Cada código admite como mucho 5 intentos y luego se anula. (iii) Pedir otro código **no** reinicia ningún contador (NIST 800-63B-4, 3.1.3.2: «Generating a new authentication secret SHALL NOT reset the failed authentication count»). (iv) Tope acumulado diario de fallos de código que exige intervención de Administración (en la línea de `op.acc.5.8`). (v) Límite de envíos. Propuesta de ingeniería **[Decidir]**: 60 segundos entre envíos, 5 por hora y 10 por día y cuenta; 15 códigos fallidos en 24 horas bloquean hasta una invitación de recuperación.
- **Efecto no buscado que hay que cerrar:** quien sepa la contraseña puede agotar los envíos y dejar fuera a la persona, también después de una recuperación. **Usar una invitación de recuperación reinicia los contadores de código y envíos** (cambia la contraseña, así que el atacante queda fuera).
- **El resumen del código debe llevar clave.** Un SHA-256 simple de 6 cifras se averigua al instante probando el millón de combinaciones. Usar resumen con clave (secreto fuera de la base de datos) sobre el identificador del intento, la cuenta, **la dirección actual** y el código.
- **Cada código se liga al intento de entrada** (un identificador en la cookie del paso pendiente, `PendingAccessCookie.cs:9`) y solo hay uno vivo por cuenta.
- **El paso pendiente debe saber de qué clase es.** Un paso pendiente del personal no puede completarse con el código por correo (sería rebajar su segundo factor), ni uno de familia dar de alta una aplicación de códigos (`AccesoController.cs:212-328` comparte hoy el mismo paso pendiente).

### H2. El correo cambia de finalidad y los textos actuales lo contradicen

- **[Hecho]** `Views/Administracion/Familiar.cshtml:77-87`: «Este correo solo sirve para mandar a esa persona avisos de ResidApp (…) Es voluntario (…) Si alguien le pide que no le manden más avisos, borre la dirección». Igual en `Manual.cshtml:486` y en `politica-privacidad-portal.md`, línea 131. La recomendación venía de `direccion-confirmada-familia.md` (riesgo 3, línea 174), escrita cuando el correo no tenía otro uso.
- **Norma.** Art. 5.1.b (finalidades determinadas y explícitas), art. 13.3 (informar **antes** de un fin nuevo), art. 21 (oposición). Que oponerse a los avisos cueste el acceso al portal no es aceptable cuando se puede separar [Suposición].
- **Acción antes de datos reales.** (i) Una **baja de avisos por familiar** para todos los avisos (propuesta, cambio, recordatorio), separada de borrar la dirección; hoy solo existe para las citas externas (`0105`, `citas_externas_correo_familiar`). (ii) Reescribir los textos para que dependan del interruptor (apartado 8). (iii) El correo **no** pasa a ser obligatorio en la ficha de cualquier familiar: solo para quien vaya a entrar en el portal (minimización, art. 5.1.c).
- Suprimir la dirección sigue siendo posible (art. 17): la pantalla avisa de que, sin ella, la persona no podrá entrar, y **no lo impide**.

### H3. Cambio de dirección por Administración

- **[Hecho]** Hoy un cambio quita la marca de confirmación (`0084:29-51`) y queda en la auditoría como `FAMILY_EMAIL_CHANGE`, sin direcciones (`SqlResidentFamilyRepository.cs:210-213`). No avisa a nadie ni cierra sesiones.
- **Riesgo.** Quien sepa la contraseña y convenza a Administración de cambiar el correo, entra. Una errata deja fuera a la persona y manda códigos a un desconocido (que no puede entrar sin la contraseña: falla hacia el lado seguro).

| Qué | ¿Hace falta? |
|---|---|
| Avisar a la **dirección anterior** | **Sí**, antes de datos reales, si el familiar tiene acceso al portal y había dirección (al cambiarla o borrarla). Texto genérico (apartado 7). Se envía después de guardar; su fallo no deshace el cambio. Mandarlo siempre evita guardar un dato nuevo de «verificada» |
| **Cerrar las sesiones** abiertas de la cuenta | **Sí**. Motivo cerrado nuevo en `sesiones` (script nuevo) |
| **Anular códigos pendientes** | **Sí**. Basta con que la dirección forme parte del resumen con clave: el código enviado a la anterior deja de valer |
| Quitar la **marca de `0084`** | Ya lo hace la base de datos. Sin cambios |
| **Volver a verificar** | Es automático: el siguiente código va a la dirección nueva |
| **Aviso en pantalla** a Administración | **Sí**: «Esta persona entra en el portal con un código que llega a este correo. Compruebe que es ella quien pide el cambio» |
| **Comprobar la identidad** de quien pide el cambio | **Sí**, procedimiento escrito de la residencia (en persona, o llamando al teléfono que ya consta) |
| Exigir **nueva invitación** tras el cambio | No lo propongo para el piloto [Decidir DPO]: es más fuerte y más gravoso |

El aviso a la dirección anterior es un correo nuevo que la lista de FAM-10 no nombra (`portal-familiar.md:22`): **necesita el sí de CJ**.

### H4. Fallar cerrado, interruptor y disponibilidad

- El boceto exige el segundo factor «obligatorio antes de datos reales» (`portal-familiar.md:32-35`). El interruptor apagado solo vale para pruebas.
- **Acción.** (i) En producción, la aplicación no arranca, o no deja entrar a las familias, si el código está apagado o el correo no está configurado. (ii) Código encendido y `Correo__Activo` apagado significa **no se entra**, nunca «solo contraseña». (iii) Dejar escrito que apagar el correo por un incidente de los avisos deja a las familias sin portal; conviene que el código no dependa de los interruptores de cada aviso. (iv) Al encender, cerrar las sesiones de familia abiertas.
- **Caída del proveedor:** nadie de las familias entra. Es aceptable porque el portal no es canal urgente (`portal-familiar.md:76`). Apagar el segundo factor con datos reales es una decisión **escrita de la residencia**, temporal y anotada, nunca automática.
- **«Aceptado» no es «entregado»** (`AzureEmailSender.cs:34-35`): la pantalla no puede asegurar que llegó. Texto: «Te hemos enviado un código. Si no llega en unos minutos, mira el correo no deseado, pide otro o llama al centro».
- El adaptador solo captura un tipo de error (`AzureEmailSender.cs:37-44`): el servicio nuevo debe atrapar los demás y los tiempos de espera, sin registrar la dirección ni el código.
- Límites de envío de Azure Communication Services con una entrada por código: **a verificar**.

### H5. Lectura del correo antes de la sesión

- **[Hecho]** La seguridad por filas filtra por **centro**, no por persona (`0030:29-45`): con el contexto de un ámbito familiar se ven las filas de todos los familiares del centro. El plan (abrir la conexión con el contexto del ámbito ya verificado) es correcto y no se salta la regla, pero **el aislamiento entre familias lo pone la consulta**.
- **Acción.** Una sola consulta, filtrada por `cuenta_id` **y** `centro_id`, que devuelva solo el correo (ni nombre ni teléfono). La base permite una fila por centro y cuenta (`0052:21`), y hay cuentas de prueba con perfil familiar sin ninguna fila (`database/seed/centros_prueba.sql`, cuentas de varios centros): **cero filas, o direcciones distintas, significa no se entra**.
- El código **no exige** la marca de `0084`: se verifica solo al introducirlo. La marca sigue siendo requisito de los avisos.

### H6. «Avisar de accesos nuevos»: lo cubre en parte

- **Cubre** el caso más probable: alguien con la contraseña intenta entrar, la familia recibe un código que no ha pedido y el intruso no pasa. Avisa antes del acceso, sin cookie de dispositivo ni rastreo por IP. Es la opción que menos datos usa y la apoyo.
- **No cubre:** buzón en manos de otro (verá y borrará el correo), dirección cambiada, sesión robada. Y al llegar en cada entrada, se aprende a ignorarlo.
- **Completar sin datos nuevos:** (i) enseñar tras entrar «Última entrada: fecha y hora», sacada de `eventos_acceso` (lo exige además `op.acc.5.r5.2` si aplica el Esquema); (ii) el aviso a la dirección anterior (H3).
- El ADR 0012 (líneas 17 y 18) recoge «un aviso cuando se entra desde un sitio nuevo». Esto lo reinterpreta: **lo confirma CJ**.
- La frase «cambia tu contraseña» es viable: «Mi cuenta» lo permite y cierra las demás sesiones (ADR 0007, I3).

### H7. Conservación sin decidir (apartado 5)

### H8. Riesgo residual del factor (apartado 2)

Debe figurar en la evaluación de impacto con quién lo acepta y cuándo se revisa: si el portal enseñara más detalle clínico o documentos, el correo dejaría de bastar [Suposición].

## 4. Para qué sirve (y para qué no) la verificación por código

Introducir el código prueba que **quien sabe la contraseña puede leer ese buzón en ese momento**. No prueba que el buzón sea suyo, ni quién es.

- **Cubre** la exigencia de verificación técnica para un código de acceso (ADR 0012, línea 84; `direccion-confirmada-familia.md`, D9), porque se comprueba en cada entrada.
- **No marcar sola la casilla de `0084`** ni escribir en sus columnas: significan otra cosa (Administración leyó la dirección a la persona), la pone solo Administración (D2 y D3) y encendería avisos que quizá nadie pidió.
- **No usarla para recuperar la contraseña**, ni para identificar a quien llama, ni para mandar contenido por correo.
- **Posible más adelante [Decidir CJ y Orlando con el DPO]:** que los avisos genéricos admitan «confirmada **o** verificada por código», solo cuando exista la baja de avisos separada (H2) y en una columna propia que se borre al cambiar la dirección. No en esta pieza: **ahora no se guarda ningún dato «dirección verificada»**.

## 5. Datos mínimos y plazos

### Tabla nueva (sin seguridad por filas, como `credenciales`)

| Guardar | No guardar |
|---|---|
| Identificador del intento, `cuenta_id` | La dirección ni una huella suya aparte |
| Resumen con clave del código, **que se borra al usarse, anularse o caducar** | El código en claro, en ningún entorno (tampoco en pantalla, registros o pruebas en Azure) |
| Creado, caduca, consumido o anulado (motivo de lista cerrada) | IP, navegador o dispositivo (la IP ya está en `eventos_acceso`) |
| Intentos fallidos de ese código | Texto del correo o de un error |
| Estado y motivo cerrado del envío, identificador de operación del proveedor | Centro, residente o nombre |
| Versión de la plantilla e idioma (requisito 3 del ADR 0012, líneas 35 y 36; falta en el diseño provisional) | |

El familiar se deduce de la cuenta (no hace falta `familiar_id`): decirlo en el comentario de la tabla, porque el requisito 3 habla de «a qué familiar».

- **Permisos:** un script nuevo añade la tabla a la matriz (`0091`): insertar y actualizar; **borrar denegado**. La purga, por un procedimiento `EXECUTE AS OWNER` que solo ejecute `residapp_tareas`, como `0085` (`antes-de-datos-reales.md`, línea 165). Sin la conexión `ResidAppTareas` no se purga: es condición antes de datos reales.
- **Plazo propuesto: 30 días desde que se creó la fila** [Decidir DPO]. Basta para los límites de envío, para explicar «no me llegó» y para revisar un incidente. **No es un plazo legal.** Es menor que los 12 meses de `envios_correo` a propósito: cada entrada genera una fila y el rastro de accesos ya está en `eventos_acceso`.
- Añadirla al catálogo de expurgo (`expurgo-y-supresion.md`).

### `eventos_acceso`

- No necesita columnas nuevas. Código erróneo: `SEGUNDO_FACTOR_FALLIDO`; entrada: `ENTRADA`. Nunca el código ni la dirección.
- **[Hecho]** Guarda la IP, no tiene plazo y la base impide borrar (`0062:32-55`; ADR 0007, línea 106; `expurgo-y-supresion.md`, apartado 2.8).
- **Propuesta para que el DPO decida (no sale de ninguna norma que haya leído):** quitar la IP a los 6 meses y borrar la fila a los 12. Antes de datos reales hace falta el plazo por escrito y la fecha de la vía de borrado. Si aplica el Esquema Nacional de Seguridad, sus guías pueden pedir otro: **a verificar**. Decidir a la vez `sesiones`.

### Fuera de la base

Cada entrada genera un correo: Microsoft ve la dirección y la hora, y por tanto **cuándo entra cada persona**. Los registros de entrega (30 días previstos, en un espacio propio en la UE) siguen sin configurar (`politica-privacidad-portal.md`, línea 186). La retención del contenido y de rebotes sigue pendiente de respuesta (`aviso-cita-por-correo.md`, apartado 2.6).

## 6. Quien no tiene correo o no quiere darlo

- **No propongo una excepción de «solo contraseña».** Si bastara con no tener correo, quitarlo sería la forma de rebajar la seguridad.
- **Ahora:** no usa el portal y el centro le da **lo mismo por otra vía** (en persona o por teléfono: novedades, citas, citas externas), sin coste ni peor trato. El portal es un canal añadido. Los derechos de protección de datos se ejercen por cualquier medio: LOPDGDD art. 12.2, «El ejercicio del derecho no podrá ser denegado por el solo motivo de optar el afectado por otro medio». Vale también para quien representa a un residente. La residencia lo escribe en su procedimiento.
- **Después:** llave de acceso o aplicación de códigos como alternativa (apartado 2).
- **Si la residencia exigiera una excepción** [Decidir DPO]: por persona, con motivo de lista cerrada, quién la autoriza, fecha de revisión y auditoría; nunca por tener el campo vacío. No recomiendo construirla.
- **Correo de un tercero** (una hija que «ayuda» a su madre): cada familiar entra con su propia cuenta; si da el correo de otra persona, esa persona participa en cada entrada y debe saberlo. No usar direcciones del centro o del personal.
- **Accesibilidad.** WCAG 2.2, criterio 3.3.8 (nivel AA, el del proyecto): se debe poder **pegar** el código y no obligar a transcribirlo. Un solo campo, `autocomplete="one-time-code"`, teclado numérico, admitir espacios, botón «Enviar otro código», sin cuenta atrás que meta prisa, mensajes leídos por el lector de pantalla. Fuente: `https://www.w3.org/WAI/WCAG22/Understanding/accessible-authentication-minimum.html` (2026-11-26, leída por herramienta web). Llei 12/2007, art. 10.f (BOE, actualizado el 2026-11-18, no oficial): derecho a «las ayudas y los apoyos necesarios para comprender la información»; su aplicación a familiares y las normas de accesibilidad web que obliguen al centro, **a verificar**.

## 7. Plantillas (borrador)

Condiciones: un destinatario, sin seguimiento, sin nombre de residente, centro ni persona. **El código no va en el asunto** (se ve en la pantalla bloqueada). **Sin enlace** en este correo: quien lo recibe ya está en la pantalla, y así se aprende que un correo de código con enlace es sospechoso (si no hay enlace, la frase del requisito 7 del ADR 0012 sobre «el enlace» se adapta). El catalán lo revisa ingeniería con traductor externo.

**Código, castellano.** Asunto: «Tu código para entrar en ResidApp»
> Tu código es: [CÓDIGO]
> Sirve una sola vez y caduca en 10 minutos. Escríbelo en la pantalla donde has puesto tu contraseña.
> ResidApp nunca te pedirá este código por teléfono ni por mensaje. No se lo des a nadie.
> ¿No estás intentando entrar? Puede que alguien sepa tu contraseña. Cámbiala (entra en ResidApp y abre «Mi cuenta») y avisa a tu centro.
> No respondas a este correo.

**Código, catalán.** Asunto: «El teu codi per entrar a ResidApp»
> El teu codi és: [CÓDIGO]
> Només serveix una vegada i caduca al cap de 10 minuts. Escriu-lo a la pantalla on has posat la contrasenya.
> ResidApp mai no et demanarà aquest codi per telèfon ni per missatge. No el donis a ningú.
> No estàs intentant entrar? Pot ser que algú sàpiga la teva contrasenya. Canvia-la (entra a ResidApp i obre «Mi cuenta») i avisa el teu centre.
> La pantalla de ResidApp és en castellà.
> No responguis aquest correu.

**Cambio de dirección (a la anterior), castellano.** Asunto: «Cambio en tu cuenta de ResidApp»
> El centro ha cambiado el correo al que ResidApp envía tus códigos de acceso.
> Si lo has pedido tú, no tienes que hacer nada. Si no, o si no sabes de qué va este correo, llama a tu centro.
> No respondas a este correo.

El pie con el titular de ResidApp sigue siendo requisito previo a datos reales (ADR 0012, línea 84).

## 8. Registro de actividades e información a la familia

### Registro de actividades (art. 30): añadir a la actividad del Portal Familiar

| Campo | Parte A, residencia (responsable) |
|---|---|
| Finalidad | Comprobar la identidad de quien entra en el portal y proteger la cuenta |
| Base jurídica | [DECIDIR: 6.1.b, 6.1.f o 6.1.e, con art. 32] |
| Personas afectadas | Familiares y representantes con acceso al portal |
| Datos | Correo (fin nuevo, además de los avisos); resumen del código; fechas de envío, uso y caducidad; resultado del envío; intentos; entradas y fallos con IP |
| Destinatarios | ResidApp (encargada); Microsoft Azure Communication Services (subencargado) |
| Transferencias | Las ya anotadas para el correo: «puede transitar o procesarse en otras geografías»; garantía **a verificar** |
| Plazos | Tabla de códigos: [30 días, DECIDIR]; `eventos_acceso`: [DECIDIR]; registros de entrega: [30 días, sin configurar] |
| Seguridad | Dos pasos; código de 6 cifras, un uso, 10 minutos; límites de intentos y envíos; sin dirección ni código en registros; aviso y cierre de sesiones al cambiar la dirección |

**Parte B, ResidApp (encargada, art. 30.2):** envío de códigos y avisos de seguridad por cuenta de cada residencia; subencargado Microsoft; medidas anteriores. **Contrato de encargado (art. 28):** incluir este uso en el objeto y las instrucciones; informar del cambio en el uso del subencargado.

### Información a la familia (antes de recoger o reutilizar la dirección)

Todo con el interruptor encendido; apagado, se mantienen los textos actuales.

**Primera capa (hoja de invitación y junto al guion del alta):**
> Para entrar en el Portal Familiar necesitas tu contraseña y un código que te enviamos por correo cada vez. Por eso [RESIDENCIA] necesita tu correo. Lo usa para ese código, para avisarte si se cambia la dirección y, si no te has dado de baja, para avisos sobre citas. Puedes dejar de recibir los avisos sin perder el acceso. Si no tienes correo o no quieres darlo, no podrás usar el portal: te atenderemos igual en persona o por teléfono ([TELÉFONO]). Usa en ResidApp una contraseña distinta de la de tu correo. Tus derechos y más información: «Privacidad», [DPO], [AUTORIDAD].

**Ayuda del campo en Administración (sustituye a «solo sirve para…»):**
> Se usa para el código con el que esta persona entra en el portal y, si no se ha dado de baja, para avisos genéricos. Sin correo no puede entrar. Antes de cambiarlo, compruebe que es ella quien lo pide. Si no quiere avisos, dela de baja de los avisos; no borre la dirección.

(La última frase solo cuando exista esa baja.)

**Otros textos que cambian:** `Privacy.cshtml` y `politica-privacidad-portal.md` (datos de familiares, «el correo electrónico es opcional», avisos y cómo dejarlos, seguridad, cookie `residapp_acceso_pendiente`, plazos); `Manual.cshtml`; guías `Guia/familia.json` (línea 40) y `Guia/administracion.json` (línea 950: «Los familiares entran solo con contraseña»); «Acceso y sesión». Consultar a `doc-usuario-residapp`.

## 9. Qué construir ya y qué falta

### Ya, con datos de prueba e interruptor apagado (por entorno, apagado por defecto)

Tabla, permisos y purga; servicio y plantillas; pantalla del código; límites; paso pendiente tipado; exigir correo al dar acceso y al emitir la invitación (con el interruptor encendido); cierre de sesiones y aviso a la dirección anterior; «Última entrada»; textos condicionados al interruptor; baja de avisos por familiar. En Azure dev, encender solo cuando las cuentas de prueba tengan una dirección de la lista permitida (los familiares sembrados no tienen correo, `centros_prueba.sql:272-274`): de lo contrario quedan fuera.

### Antes de la primera familia real

1. DPO real o abogado: suficiencia del correo, base jurídica, plazos, ausencia de excepción.
2. Evaluación de impacto del Portal Familiar actualizada, con el riesgo residual aceptado por la residencia.
3. Información y textos nuevos **antes** de pedir direcciones; baja de avisos separada.
4. Interruptor encendido y comprobación de arranque que falle cerrado.
5. Procedimientos de la residencia: identidad ante un cambio de dirección, quien no tiene correo, «no me llega», código no pedido (incidente, art. 33.5), caída del correo.
6. Plazos escritos y purga funcionando (`ResidAppTareas`).
7. Contrato de encargado, subencargado Microsoft, registros de entrega y entrega real probada.
8. Tipo de centro: público o concertado (Llei 32/2010, art. 3, letras a a f: autoridad APDCAT para los tratamientos de ese ámbito, y posible Esquema Nacional de Seguridad) o privado (AEPD). **Falta el dato.** El centro piloto necesita además su DPO formal.

### Antes de producción

DMARC en cuarentena o rechazo; vigilancia del porcentaje de envíos fallidos; revisión de límites del proveedor; decisión sobre llave de acceso o aplicación de códigos para familias.

## 10. Condiciones comprobables con pruebas

| Id | Condición | Prueba |
|---|---|---|
| C1 | Con el código encendido, una cuenta solo familiar no obtiene sesión sin código válido | Contraseña y también invitación (alta y recuperación): sin sesión ni cookie de sesión antes del código |
| C2 | Apagado, todo igual que hoy; personal sin cambios | Suite actual; personal nunca recibe código por correo |
| C3 | Falla cerrado | Sin correo, sin fila, varias direcciones, correo inactivo, destino no permitido, rechazo, excepción cualquiera, tiempo agotado: no hay sesión. En producción con interruptor apagado: no entra ninguna familia |
| C4 | 6 cifras aleatorias criptográficas, 10 minutos, un uso | Reutilizado, caducado y dos envíos simultáneos del mismo código: una sola entrada |
| C5 | Código ligado a intento, cuenta y dirección; uno vivo por cuenta | El de otro navegador, otra cuenta, el anterior o tras cambiar la dirección no vale |
| C6 | La contraseña correcta y pedir otro código no reinician fallos | 4 fallos, contraseña de nuevo, 1 fallo: bloqueo |
| C7 | Límite por código, bloqueo, límite por IP, tope diario y de envíos | Una prueba por límite; la invitación de recuperación reinicia los de código y envío |
| C8 | Pasos pendientes separados | Paso del personal en la ruta del correo y paso de familia en las rutas de la aplicación de códigos: rechazados |
| C9 | En el momento de validar se revisan cuenta activa, ámbitos solo familiares, sello y bloqueo | Suspensión, revocación o cambio de contraseña entre envío y código |
| C10 | Solo se lee el correo propio | Dos familiares del mismo centro y usuario de base limitado: cada código va a su dirección |
| C11 | Un destinatario, sin copia, sin seguimiento, sin adjuntos salvo el icono | Mensaje construido |
| C12 | Plantillas es y ca exactas, sin datos personales; el código no está en el asunto; sin enlace | Comparación con los ficheros versionados |
| C13 | Ni código ni dirección en tabla, `eventos_acceso`, auditoría, registros ni pantalla | Esquema y captura de registros; resumen distinto con otra clave |
| C14 | Respuestas antes de la contraseña idénticas a las de hoy | Cuenta inexistente, sin correo y con correo: mismo mensaje con contraseña mala |
| C15 | La pantalla del código no enseña la dirección (ni enmascarada, salvo decisión contraria) y no se guarda en caché | HTML y cabecera `no-store` |
| C16 | Dar acceso e invitar exige correo; borrar el correo se permite con aviso | Una prueba por camino |
| C17 | Cambiar o borrar la dirección: aviso a la anterior, cierre de sesiones, código anulado, marca quitada, auditoría | Incluido fallo del aviso: el cambio se guarda |
| C18 | La baja de avisos no afecta al código | Dado de baja: recibe código y no avisos |
| C19 | La verificación por código no escribe las columnas de `0084` | Tras entrar, siguen nulas |
| C20 | Purga y permisos | Borrado directo denegado; el procedimiento borra solo lo vencido; prueba de cobertura de `0091` |
| C21 | Campo del código accesible | Un campo, pegar y autocompletar; revisión en claro y oscuro |

## 11. A verificar o decidir

**Orlando**
1. Cifras de los límites de H1.
2. Enseñar o no la dirección enmascarada (propongo no).
3. Construir la baja general de avisos en esta pieza (propongo sí).
4. Comprobación de arranque en producción; apagar la pieza ante caída del correo solo por escrito de la residencia.
5. Correo repetido en dos familiares: permitirlo e informar (propongo) o impedirlo.

**CJ**
1. Que el aviso de cada código, más «Última entrada» y el aviso a la dirección anterior, cumple «avisar de accesos nuevos».
2. Que quien no tiene correo queda sin portal y se le atiende por otra vía.
3. Añadir a FAM-10 el aviso de cambio de dirección.
4. Si quiere ofrecer a las familias llave de acceso o aplicación de códigos.
5. Que la familia pueda no querer avisos y seguir entrando.

**DPO real o abogado**
1. Suficiencia y proporcionalidad del correo como segundo factor para este contenido.
2. Base jurídica del correo para autenticación y compatibilidad si ya se recogió para avisos (art. 6.4).
3. Plazos: tabla de códigos, `eventos_acceso`, `sesiones`, registros de entrega.
4. Alternativa para quien no tiene correo; si admite alguna excepción.
5. Esquema Nacional de Seguridad: si aplica, categoría, y si un código por correo cumple `op.acc.5.r2` según las guías CCN-STIC (**a verificar**).
6. Que estos correos de seguridad no son comunicaciones comerciales (LSSI): **a verificar**.

**Residencia:** tipo de centro, DPO, autoridad, teléfono y persona de referencia, procedimientos del apartado 9.

**A comprobar al construir:** comportamiento del contador de Identity al bloquear (supongo que se reinicia, y por eso hace falta el tope diario); que ningún otro camino abre sesión a una cuenta familiar sin código (la llave de acceso hoy es solo del personal, ADR 0007 I4).

**Observación fuera de alcance:** la clave de la aplicación de códigos sigue en claro en `credenciales` (ADR 0007, líneas 101 y 102); afecta al personal, no a esta pieza.

## Fuentes (consultadas el 2026-11-26)

Base normativa local (comprobada el 2026-11-20; textos consolidados, no oficiales): RGPD (versión 2016-05-04), LOPDGDD (BOE 2026-10-08), RD 311/2022 (BOE 2026-10-30), Llei 32/2010 (BOE 2026-11-17), Llei 12/2007 (BOE 2026-11-18), lista de la AEPD del art. 35.4. Web, leídas por herramienta (contrastar antes de citarlas fuera): NIST SP 800-63B-4 y la explicación del criterio 3.3.8 de WCAG 2.2. No he encontrado resolución de la AEPD ni guía del CCN sobre el código por correo.

## PR 2: Administración y última entrada

**Borrador del agente `dpo-residapp` (2026-11-26), por validar por una persona.** Lectura del diseño antes de construirlo: **admisible con condiciones**. Se construyó con ellas (script `0122`).

**Correcciones al diseño, aplicadas:** motivo propio `CORREO_APAGADO` en `envios_correo` (`AVISOS_DESACTIVADOS` significa otra cosa); la plantilla `AVISO_CAMBIO_CORREO` no admite filas `PENDIENTE`; «solo familia» es tener algún perfil familiar activo y ningún otro activo; con el código encendido ya no se pide borrar la dirección para dejar de recibir avisos. **La baja de avisos (C18) sigue siendo requisito para encender.**

| Id | Condición | Prueba |
|---|---|---|
| C22 | Cambio de mayúsculas o espacios, sin cuenta o sin dirección anterior, perfiles mixtos o anonimización: ni cierre ni aviso | Una por caso |
| C23 | Conflicto o reversión: sin efectos; el mismo formulario enviado dos veces: un solo aviso | Reenvío con la versión anterior |
| C24 | Tras guardar, dejan de valer las sesiones y el código anteriores; las otras cuentas, intactas | Dos familias |
| C25 | El aviso va solo a la dirección anterior, aunque haya baja, falte la marca o estén apagados otros avisos | Mensaje construido |
| C26 | Se envía tras el guardado, con tiempo límite propio; un fallo del envío o del registro no deshace nada; sin direcciones en tabla ni registros | Fallo, excepción, tiempo agotado |
| C27 | Dar acceso e invitar comprueban el correo dentro de su transacción; si falta, no crean ni anulan nada | La invitación viva sigue viva |
| C28 | Advertencia visible antes de guardar | Pantalla |
| C29 | «Última entrada» solo de la cuenta autenticada, sin la sesión actual ni IP, en la hora del centro | Dos cuentas y dos sesiones |

**Textos:** el aviso a la dirección anterior no dice que la dirección sea de quien lo recibe (tras una errata puede ser una persona ajena). **Por validar:** el pie, por el DPO real o un abogado; antes de datos reales, añadirle el titular y el contacto de ResidApp (ADR 0012). El catalán, por una persona traductora. No se revisaron normas nuevas.
