# Aviso de cita por correo: estilo de la app e icono de ResidApp incrustado

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-10-31. Segunda lectura experta, no un dictamen.

Complementa `docs/privacidad/aviso-cita-por-correo.md` (el «borrador existente») y el ADR 0012 (`docs/decisiones-arquitectura/0012-envio-correos.md`). **No modifico ninguno de los dos**: en el apartado 2 está la redacción que los sustituiría, para que la aplique una persona o la sesión principal.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL), **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado.

## Conclusión

**Admisible, con condiciones.** Un HTML con cabecera azul, tarjeta, recuadro para el enlace y el icono de ResidApp **incrustado** en el mensaje no añade ningún dato, no hace ninguna petición a ningún servidor y no revela nada que el remitente y el asunto «ResidApp» no den ya. El texto plano y el asunto no cambian, y la plantilla sigue recibiendo solo idioma y URL.

El riesgo más importante **no es el icono, es que el mensaje deje de ser comprobable**: hoy la prueba «exactamente una URL, sin imágenes» es sencilla y estricta; con un diseño más rico, cualquier cambio futuro (un píxel «de cortesía», una letra de Internet, un fondo con `url(...)`, un texto oculto para la vista previa, un enlace distinto del texto que se ve) cabría sin que nadie lo notase. Por eso las condiciones del apartado 4 pasan de «lista de lo prohibido» a **lista cerrada de lo permitido**, con pruebas automáticas.

Dos matices que conviene tener presentes:

1. **Más aspecto oficial = phishing más convincente.** Este correo tiene el patrón típico de fraude («propuesta de cita» + enlace a un inicio de sesión). Un icono y una cabecera de marca hacen un correo auténtico más reconocible, pero también lo hacen más fácil de imitar (copiar el icono cuesta lo mismo que copiar el texto). El icono **no debe contarse como medida de seguridad**: lo que protege es la URL visible, la frase «ResidApp nunca te pedirá tu contraseña…» y DMARC reforzado antes de producción (borrador existente, apartado 2.6).
2. **«Incrustado» no es «sin efectos»**: añade un adjunto a cada mensaje (nombre del archivo, tipo, bytes, metadatos del PNG). Todo eso debe ser neutro y verificable (apartado 4, bloque B).

## 1. Qué hay hoy [Hecho]

El estado que leo en el árbol de trabajo (cambios en curso, aún sin confirmar en git cuando empecé; **la sesión principal debe contrastarlo con lo que finalmente se confirme**):

- `src/ResidApp.Application/Email/EmailLayout.cs`: marco común. Cabecera `#0A58CA` con `<img src="cid:residapp-icono" width="48" height="48" alt="">` y la palabra «ResidApp» (líneas 44 a 48); tarjeta blanca (líneas 42 y 43); párrafo destacado para el que lleva el enlace (línea 52); clases neutras (`fondo`, `tarjeta`, `texto`, `destacado`, `pie`); modo oscuro en un bloque `<style>` con `@media (prefers-color-scheme: dark)` y sin `url(...)` (líneas 22 a 30). El icono sale de un recurso incrustado en el ensamblado (`icono-correo.png`, líneas 15, 18 y 61 a 68), no de un servidor.
- `src/ResidApp.Application/Email/ProposalNoticeTemplate.cs`: sigue recibiendo solo `language` y `baseUrl`; el texto de los párrafos y el pie no cambian; el enlace sigue como `<a href="URL">URL</a>` (texto visible igual al destino); el icono se devuelve en `RenderedEmail.InlineImages` (líneas 6, 7 y 55 del cambio).
- `src/ResidApp.Infrastructure/Correo/AzureEmailSender.cs`, líneas 24 a 32: `UserEngagementTrackingDisabled = true` se mantiene y cada imagen se añade como `EmailAttachment` con `ContentId`. Un solo destinatario, sin `Reply-To`.
- Mi lectura del PNG a 96 px (`src/ResidApp.Application/Email/Images/icono-correo.png`): una marca abstracta en azul y verde (una forma tipo «S» de dos piezas) sobre fondo blanco, **sin texto, sin cruz, sin corazón, sin casa, sin figuras de personas**. Es el mismo icono que ya usan los PDF (`src/ResidApp.Infrastructure/Pdf/Images/logo.png`) y la versión PNG de `src/ResidApp.Web/wwwroot/images/logo.svg` (que tampoco tiene texto).
- Pruebas actuales (`tests/UnitTests/AvisoPropuestaCitaPlantillaTests.cs`): ya comprueban una sola URL (líneas 49 a 63), la única imagen con `cid:` y `alt=""` (líneas 65 a 82), el enlace visible (líneas 84 a 94) y una lista de palabras prohibidas (líneas 96 a 108). **Hay una prueba que ya no comprueba nada** (apartado 5, hallazgo 1).

**Fuente oficial sobre el mecanismo** (consultada el 2026-10-31): Microsoft Learn, «Inline attachments» (`https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-attachment-inline`, actualizada el 2026-09-23): el icono se referencia por un identificador de contenido (CID) y viaja dentro del mensaje, sin alojarlo en un servidor externo; y `EmailAttachment.ContentId` (`https://learn.microsoft.com/en-us/dotnet/api/azure.communication.email.emailattachment.contentid?view=azure-dotnet`), disponible en `Azure.Communication.Email` 1.1.0, la versión que usa el repositorio. Esas páginas **no dicen** cuánto tiempo guarda ACS el contenido del mensaje (sigue abierta la pregunta 4 del apartado 2.6 del borrador existente) ni si cambia algo por llevar adjuntos.

## 2. Valoración (1): admisibilidad y razones

- **Datos personales: ninguno nuevo.** El icono es una constante del programa, igual para todos; no lleva nombre, centro, residente ni fecha. La plantilla no gana parámetros (condición 7 del borrador existente intacta).
- **Sin petición remota.** Una imagen `cid:` la lee el programa de correo dentro del propio mensaje; **el remitente no se entera de si se abrió**. Por eso no crea el seguimiento que sí crea un píxel o una imagen enlazada, y no entra en la preocupación de la LSSI art. 22 sobre almacenar o recuperar datos en el equipo de la persona **[Suposición; a verificar con abogado, no cito más de memoria]**. Esto es cierto **solo mientras** el HTML no tenga ningún otro recurso externo (apartado 4, A2).
- **Minimización (art. 5.1.c RGPD).** Proporcionado: el icono no añade información; y el beneficio (que la familia reconozca el aviso como de la aplicación) es legítimo. No exige cambiar la base jurídica, la información a la familia ni el registro de actividades.
- **Accesibilidad.** Con `alt=""` el icono se declara decorativo (la palabra «ResidApp» va a su lado en texto), que es lo correcto; si el programa de correo bloquea imágenes, la cabecera sigue diciendo «ResidApp». Contraste del blanco sobre `#0A58CA`: unos 6,4 a 1 (cálculo mío con la fórmula de WCAG; **verificar con una herramienta**), por encima del 4,5 a 1 de WCAG 2.2 AA.
- **Condición nueva sobre el texto que ve la persona:** el texto visible del HTML debe ser el del texto plano más la palabra «ResidApp» de la cabecera, y nada más. Un HTML que diga algo distinto del texto plano es una señal clásica de correo fraudulento y, además, haría inútil el contraste exacto con el texto aprobado.

## 3. Redacción propuesta para sustituir frases del borrador y del ADR

Para el borrador existente (`aviso-cita-por-correo.md`):

**(a) Línea 44, final del tercer punto de «Mitigaciones».** Hoy: «… sin nombre ni saludo personalizado; sin imágenes ni logotipo.» Propuesta:

> … sin nombre ni saludo personalizado; sin logotipo ni nombre del centro; sin imágenes remotas ni ningún recurso que se cargue desde un servidor. La única imagen es el icono de ResidApp, incrustado en el propio mensaje (`aviso-cita-icono-en-correo.md`); no lleva texto y no sugiere nada más que la marca de la aplicación.

**(b) Condición 7 (línea 184).** Añadir al final: «El icono no es un parámetro: es una constante incrustada en el ensamblado. La comparación exacta con el fichero versionado se hace con el asunto y el texto plano; para el HTML, el texto visible (sin etiquetas) tiene que ser el del texto plano más la palabra «ResidApp» de la cabecera.»

**(c) Condición 9 (línea 186).** Hoy: «… sin imágenes, sin píxel, sin enlace de seguimiento. Prueba: el HTML y el texto plano contienen exactamente una URL, y es la configurada.» Propuesta:

> 9. **Enlace y recursos.** El enlace sale de la configuración del entorno, nunca de `Host` ni de cabeceras; sin parámetros, sin identificadores ni tokens, sin enlace de seguimiento. **El HTML no carga nada de ningún servidor**: ni imágenes enlazadas, ni píxel, ni hoja de estilo, letra de Internet o fondo con `url(...)`. La única imagen es el icono de ResidApp, incrustado en el mensaje (`src` que empieza por `cid:`). El enlace va siempre en texto visible: el texto del enlace es la propia URL. Pruebas: (i) el conjunto de direcciones `http` o `https` del HTML y del texto plano es exactamente {la URL configurada}; (ii) el HTML tiene un único `<a>`, y su `href` y su texto visible son esa URL; (iii) el único `<img>` tiene `src` que empieza por `cid:`; (iv) las condiciones del bloque A de `aviso-cita-icono-en-correo.md`, apartado 4.

**(d) Fila «Seguridad» del registro de actividades (línea 216).** Sustituir «sin seguimiento de aperturas» por «sin seguimiento de aperturas y sin ningún recurso remoto en el mensaje (el icono de ResidApp va incrustado)».

**(e) Apartado 2.6, recomendación B10 (línea 156).** Sin cambio. Se puede añadir una frase: «La imagen incrustada no genera evento de apertura; el seguimiento sigue apagado en el dominio y en cada envío (`UserEngagementTrackingDisabled`).»

Para el ADR 0012:

**Requisito 1 (líneas 28 a 30).** Añadir tras «Solo que hay algo pendiente en ResidApp o el código.»:

> El HTML puede llevar el icono de ResidApp incrustado en el mensaje (`cid:`) y el estilo de la aplicación; no lleva el logotipo ni el nombre del centro, ni imágenes ni recursos enlazados desde un servidor. El texto visible del HTML es el del texto plano más la palabra «ResidApp» de la cabecera.

**Requisito 7 (líneas 40 a 44).** Añadir una frase: «El estilo y el icono son comunes a los dos idiomas, se versionan con la plantilla y no son un parámetro de ella.»

**Estado de la construcción (líneas 54 a 57).** Añadir «icono incrustado (`cid:`), sin recursos remotos» junto a «seguimiento de aperturas apagado».

Para el código (lo aplica la sesión principal, no yo): el comentario de `ProposalNoticeTemplate` ya dice «sin más imagen que el icono»; la versión de la plantilla (`ProposalNoticeTemplate.Version`, línea 17) sigue en «1». **Recomiendo subirla a «2»** porque el mensaje enviado es distinto (HTML nuevo), y la tabla de envíos guarda la versión «como prueba de qué se mandó» (ADR 0012, requisito 3). No bloquea; es coherencia. **[Decidir Orlando]**

## 4. Condiciones comprobables en código (valoración 4)

Criterio general: **lista cerrada de lo permitido, no lista de lo prohibido.** Una lista de palabras prohibidas (la que ya existe en las pruebas) protege contra un descuido, no contra un recurso nuevo que nadie pensó prohibir.

### Bloque A. HTML

| N.º | Condición | Cómo se comprueba |
|---|---|---|
| A1 | **Una sola imagen**, y su `src` es exactamente `cid:` + la constante (`residapp-icono`). Sin `srcset`, sin `<picture>`, sin `background=`. | Analizar el HTML (un analizador de verdad, no solo expresiones regulares) y contar `<img>`. Ya existe una prueba parecida (línea 77); pasarla a analizador. |
| A2 | **Ningún recurso remoto.** Prohibidas estas etiquetas: `link`, `script`, `iframe`, `object`, `embed`, `video`, `audio`, `source`, `form`, `input`, `base`, `meta http-equiv`. Prohibidos en cualquier atributo y en `<style>`: `url(`, `@import`, `@font-face`, `//` al inicio de una dirección, `data:`, `javascript:`. | Prueba sobre el HTML renderizado en `es` y en `ca`. La existente solo mira `url(`, `<link` y `@import` (líneas 62, 80 y 81): ampliarla. |
| A3 | **Direcciones.** El conjunto de todas las direcciones con esquema (`http`, `https`, `mailto`, `tel` y cualquier otra) del HTML y del texto plano es exactamente {la URL configurada}; el `cid:` no cuenta como dirección. Un único `<a>`; su `href` y su texto visible (descodificado) son esa URL; sin `title`, sin `target`. | Sustituye a la prueba de «exactamente una URL» (borrador existente, condición 9). La existente (líneas 49 a 63) ya cubre buena parte. |
| A4 | **El texto visible no se parte ni se oculta.** Nada con `display:none`, `visibility:hidden`, `font-size:0`, `opacity:0`, `max-height:0` ni `mso-hide`; sin comentarios HTML. Así no hay «texto de vista previa» oculto, que sería un segundo texto que nadie revisó. | Prueba sobre el HTML: ninguna de esas cadenas aparece. |
| A5 | **El texto visible del HTML es el del texto plano más «ResidApp».** Quitar etiquetas, descodificar, normalizar espacios y comparar párrafo a párrafo. | Prueba nueva. Es la que impide que HTML y texto plano diverjan. |
| A6 | **Atributos por lista cerrada.** `alt` solo puede ser `""`. `title`, `aria-label` y `data-*` no existen. `id` no existe. `class` solo puede valer una de {`fondo`, `tarjeta`, `texto`, `destacado`, `pie`}. `lang` solo `es` o `ca`. | Prueba que recorre todos los atributos de todas las etiquetas y falla ante cualquier valor fuera de lista. **Esta es la forma buena de pedir «ningún `alt`, `class` o `id` con residencia, centro, salud…»**: si solo se admiten los valores de la lista, no hace falta adivinar palabras. |
| A7 | **Red de seguridad por palabras** (además de A6, no en su lugar), sobre **todo el mensaje**: asunto, texto plano, HTML completo (atributos, `<title>`, bloque `<style>`), nombre de archivo, CID y tipo del adjunto. Insensible a mayúsculas, sin acentos. Patrón orientativo: `resid(?!app)`, `centr`, `salud`, `salut`, `sanit`, `m[eé]dic`, `metge`, `enfermer`, `geri`, `cl[ií]nic`, `asist`, `cuid`, `mayor`, `anci`, `hospital`, `unitat`, `unidad`. **Excepciones documentadas y exactas**: la frase «llama al centro» / «truca al centre» y «el centro que la registró» / «el centre que la va registrar» del texto aprobado (por eso la prueba existente (líneas 104 y 105) no incluye «centro»). | Prueba que elimina primero esas frases exactas del texto visible y luego aplica el patrón; **fuera del texto visible (atributos, nombres, CSS) no hay excepción alguna**, ni para «centro». Ojo: «ResidApp» contiene «resid», de ahí el `(?!app)`. |
| A8 | **Aspecto sin depender del color.** Cabecera con color de fondo escrito en la propia etiqueta (como ahora, línea 44) y texto blanco; el enlace sigue siendo texto subrayado o con borde, no solo color. Contraste mínimo 4,5 a 1 en claro y oscuro. | Revisión humana con herramienta de contraste; prueba automática solo de que el color de fondo está en línea. |

### Bloque B. Adjunto (el icono)

| N.º | Condición | Cómo se comprueba |
|---|---|---|
| B1 | **Un solo adjunto**, siempre el mismo: `ContentId` constante (no un identificador por mensaje), nombre `icono-residapp.png`, tipo `image/png`. Ninguna otra cosa en `Attachments`. | Prueba del adaptador con el envío falso: cuenta y valores exactos. Hoy no hay ninguna prueba del adaptador con el SDK. |
| B2 | **El icono es siempre el mismo archivo**: su huella (SHA-256) está escrita en la prueba y coincide. Cambiar el icono obliga a cambiar la huella a propósito. | Prueba nueva. Sustituye al «entre 1 y 30 000 bytes» actual (línea 76), que no detecta un cambio de archivo. |
| B3 | **El PNG no lleva metadatos.** Solo los bloques `IHDR`, `PLTE` (si hay), `IDAT` y `IEND` (y como mucho `sRGB`, `gAMA`, `pHYs`). Ningún `tEXt`, `iTXt`, `zTXt`, `eXIf`, `tIME` (pueden llevar ruta de archivo, programa, usuario o fecha de quien lo exportó). | Prueba que recorre los bloques. El repositorio ya tiene un patrón parecido para el logo del centro (`tests/UnitTests/CenterLogoTests.cs`). **No he podido comprobarlo yo con las herramientas que tengo**: a verificar. |
| B4 | **El PNG no lleva texto dibujado** y no es una cruz, un corazón, una casa ni una figura de personas. Hoy cumple (apartado 1). | Revisión humana al aprobar la imagen; se repite si cambia la huella (B2). |
| B5 | **El icono es un recurso incrustado**, no se lee de una ruta ni de una dirección ni de la configuración, y generar el mensaje no abre ninguna conexión. | Revisión de código (`EmailLayout.LoadIcon`) y prueba de que `Render` funciona sin red. |
| B6 | **Cualquier fallo del icono no cambia el resultado de proponer.** Si el recurso faltase, `Render` lanzaría una excepción (`EmailLayout.cs`, líneas 61 a 64); como está dentro del `try` de `ProposalNoticeService.NotifyAsync` (líneas 26 a 51), la propuesta no se ve afectada. Pero la reserva queda `PENDIENTE` (línea 28 ya la hizo): debe tratarse como `FALLIDO` (borrador existente, apartado 2.4, «Las filas `PENDIENTE` olvidadas se tratan como `FALLIDO`»). | Prueba: un `Render` que lanza, `NotifyAsync` no lanza. |
| B7 | **Seguimiento apagado.** `UserEngagementTrackingDisabled = true` se mantiene. | Prueba del adaptador, o revisión de código. |

### Bloque C. Plantilla y versión

| N.º | Condición |
|---|---|
| C1 | `Render` sigue sin ganar parámetros: idioma y URL, nada más (condición 7 del borrador existente). Prueba: firma por reflexión. |
| C2 | El asunto y el texto plano de la versión 1 no cambian: las pruebas actuales (líneas 15 a 47) los comparan literalmente y **deben seguir pasando sin tocarlas**. |
| C3 | Versión de plantilla: ver apartado 3, recomendación de subir a «2». |

### Bloque D. Comprobación manual en el primer envío real

No automatizable, y necesaria porque cada programa de correo hace lo suyo con el contenido incrustado:

1. En Gmail web, Outlook (web y de escritorio), Apple Mail (móvil) y la aplicación de correo de Android: ¿se ve el icono?, ¿se muestra como archivo adjunto con clip?, ¿con qué nombre?, ¿con las imágenes externas bloqueadas sigue viéndose?
2. «Ver original» o «ver código fuente»: confirmar `Content-ID`, ninguna URL distinta de la configurada y, de nuevo, que no hay `Reply-To` y que `To` lleva un solo destinatario (borrador existente, apartado 7 de «A verificar»).
3. Con el panel de red del navegador abierto, abrir el correo en el cliente web: **ninguna petición a `residapp.es` ni a otro dominio al abrir el mensaje** (solo se admite la petición del clic en el enlace).
4. Vista previa de la bandeja y de la pantalla de bloqueo: que no aparezca más que el asunto y el principio del texto.

## 5. Hallazgos sobre lo que hay hoy

1. **Una prueba existente se ha vuelto vacía.** `tests/UnitTests/AvisoPropuestaCitaPlantillaTests.cs`, línea 117: `Regex.Matches(es.Html, "<p>").Count == Regex.Matches(ca.Html, "<p>").Count`. Con el diseño nuevo los párrafos son `<p class=…>` (`EmailLayout.cs`, líneas 53 y 56), así que la expresión cuenta cero en los dos idiomas y la igualdad es trivial. Acción: contar `<p ` con atributos, o comparar la estructura con un analizador. **Importante**: ahora mismo no se comprueba que el catalán y el castellano tengan la misma estructura en el HTML.
2. **La prueba de «ninguna palabra prohibida» (líneas 96 a 108) no mira atributos con rigor** (solo busca palabras sueltas; no cubre `centro`, `centre` ni los nombres de archivo y CID). Se sustituye por A6 y A7.
3. **Falta toda prueba del adaptador** (`AzureEmailSender`): no se comprueba hoy ni el adjunto, ni el `ContentId`, ni que no hay `Reply-To`, ni un solo destinatario. Con el icono pasa a ser más importante (B1 y B7).
4. **El nombre del archivo y del CID viajan en el mensaje.** Hoy son neutros (`icono-residapp.png`, `residapp-icono`; `EmailLayout.cs`, líneas 15 y 18). Quedan fijados por B1.
5. **Las clases CSS están en castellano y son neutras** (`fondo`, `tarjeta`, `texto`, `destacado`, `pie`): bien, y A6 impide que alguien las cambie por otra cosa por descuido.

## 6. Valoración (3): ¿añade el icono alguna pista sobre un servicio asistencial?

**No, con las condiciones anteriores.** Razonamiento:

- **El nombre ya está en tres sitios** (remitente, asunto, texto). Repetir «ResidApp» en la cabecera hace más visible la misma información para quien mire la pantalla de pasada (bandeja compartida, pantalla abierta), pero no añade información. La parte «resid» del nombre ya evoca «residencia» a quien lo piense, igual que antes (borrador existente, 2.2).
- **La forma del icono** (marca abstracta, sin símbolos sanitarios ni de cuidado ni de personas mayores) **no dice nada que el nombre no diga**. Esta conclusión depende de que siga siendo así: si en el futuro el icono cambiase por una versión con un símbolo (cruz, corazón, casa, figura), habría que volver a leerlo (B2 y B4 lo impiden por descuido).
- **Dónde se ve y dónde no.** La vista previa de la pantalla de bloqueo suele enseñar remitente, asunto y primeras palabras de texto, no imágenes. **No se pide ni se usa BIMI** (el icono que algunos servicios enseñan junto al remitente en la bandeja): sería otro mecanismo, con un registro aparte y una condición de DMARC estricto, y no está en esta propuesta. **[Suposición, comprobar en el apartado 4D]**
- **Lo que sí cambia, y no es una pista de salud:** (i) el mensaje parece más «oficial», con el matiz de fraude del principio; (ii) en algunos programas de correo la bandeja puede enseñar un clip de «adjunto» y el nombre del archivo, de ahí que el nombre sea neutro (B1). Ninguna de las dos cosas revela nada de la persona.
- **Una dirección mal escrita** recibe el mismo contenido que antes más un icono: el riesgo para un tercero no cambia (borrador existente, 2.5).

## 7. Valoración (5): aplazar el logotipo y el nombre del centro

**Confirmo el criterio: aplazar.** El icono de ResidApp **identifica la aplicación**, que es igual para todos los centros y ya figura como remitente; el logotipo o el nombre del centro **identifican a una residencia concreta**, y eso cambia la naturaleza del mensaje. Razones:

1. **Revela una relación con un centro.** Un tercero que reciba el aviso por una dirección mal escrita, o quien comparta bandeja con la familiar (pareja, hijos), pasa de saber «esta persona usa una aplicación» a saber «esta persona tiene un familiar en tal residencia». Eso permite deducir una situación de dependencia o de salud del familiar: no es un dato de salud en el mensaje, pero sí una **inferencia sobre la salud** de otra persona, mayor que la que permite «ResidApp». Pasa del riesgo bajo que acepta el borrador existente (apartado 1) a un riesgo que habría que reevaluar entero.
2. **Rompe la condición 7** del borrador existente (la plantilla recibe solo idioma y URL): tendría que recibir el centro, o el logotipo, y con ello nace el riesgo de **enviar el logotipo de un centro a familias de otro**. En una plataforma con varios centros, eso es una brecha entre centros, mucho más grave que una dirección mal escrita.
3. **Ya hay una decisión escrita en sentido contrario**: remitente «ResidApp» «y **sin el nombre del centro**» (ADR 0012, línea 5; el pie tampoco nombra al centro, borrador existente, línea 90).
4. **El logotipo del centro es contenido subido por el centro** (`src/ResidApp.Domain/Platform/CenterLogo.cs`, script 0068: PNG o JPEG, 512 KB y 1024 px como máximo, sin metadatos). El repositorio ya lo normaliza para los PDF, pero sacarlo en correos añade un camino más por donde un archivo externo llega a una bandeja ajena.

**Qué haría falta si Orlando lo quisiera más adelante** (todo ello **antes** de construirlo):

| Paso | Quién | Para qué |
|---|---|---|
| Nueva decisión escrita que **sustituya** la del ADR 0012 línea 5 (nueva versión o ADR nuevo) | Orlando | Es una decisión de producto con efectos legales: hoy está tomada en el otro sentido. |
| **Instrucción de la residencia**: la responsable del tratamiento es ella, y es quien decide si su nombre o logotipo figuran en lo que se manda a las familias. ResidApp, como encargada, solo lo ejecuta (art. 28.3.a RGPD). **Opción por centro, apagada por defecto.** | Dirección de la residencia, con Orlando | Un centro puede no querer que su nombre salga en un correo que puede llegar a una bandeja ajena. |
| **Valoración del riesgo y revisión del riesgo de dirección equivocada** por el DPO real | DPO real | La inferencia del punto 1 y qué garantías pide (probablemente exigir la dirección **verificada**, la casilla `correo_confirmado_en` del borrador existente, apartado 2.1, antes de enviar con nombre o logotipo del centro). |
| **Información a la familia** (art. 13) en castellano y catalán diciendo que el aviso llevará el nombre o logotipo del centro; actualizar el registro de actividades (apartado 5 del borrador existente: datos y seguridad) | Residencia, DPO | Transparencia y coherencia con el registro. |
| **Cambios de las condiciones**: la plantilla pasa a recibir «centro» de una fuente cerrada (el centro de la solicitud, no de la sesión ni de la cabecera); prueba de aislamiento entre centros (el logotipo enviado es siempre el del centro de la solicitud; seguridad por filas); el logotipo se vuelve a normalizar y se envía como `cid:` (ningún enlace); `alt` con el nombre del centro pasa a ser el único texto fuera de lista (A6 se amplía con ese caso exacto); el asunto y el texto plano **siguen sin nombrar al centro** salvo que el DPO acepte otra cosa. | Sesión de código, con revisión del DPO | Mantener lo comprobable. |
| **Versión nueva de la plantilla** y aprobación conjunta en los dos idiomas | Orlando, DPO, quien revise el catalán | ADR 0012, requisito 7. |
| **Comprobación manual** (apartado 4D) con un centro de prueba y datos de prueba, no con familias reales | Orlando | Que no sale el logotipo de un centro en el correo de otro. |

## A verificar o decidir

1. **[Decidir Orlando]** Subir `ProposalNoticeTemplate.Version` a «2» (recomendado) o dejarla en «1».
2. **[Decidir Orlando / sesión de código]** Pasar las condiciones del apartado 4 a pruebas, empezando por A3 a A7 y B1 a B3, y arreglar la prueba vacía del apartado 5, hallazgo 1.
3. **A verificar (abogado):** que el icono incrustado no entra en el art. 22 LSSI; no cito más artículos de memoria.
4. **A verificar:** metadatos del PNG (B3) con una herramienta que lea los bloques; no he podido comprobarlo con lo que tengo.
5. **A comprobar en el primer envío real:** apartado 4D (clientes de correo, clip de adjunto, ninguna petición de red al abrir).
6. **A verificar con Microsoft (ya abierto en el borrador existente, 2.6, punto 4):** cuánto retiene ACS el contenido de los mensajes, ahora con un adjunto; el icono no es dato personal, pero el mensaje sí lo es por la dirección.
7. **[Decidir DPO]** Si acepta el criterio de que el icono **no** aporta pista de relación con un servicio asistencial (apartado 6) y si prefiere, aun así, el correo sin cabecera de marca hasta que se resuelva la cuestión de la dirección verificada.
8. **Fuera de alcance:** el recordatorio y el código de acceso (pieza 2 y 3 del ADR 0012) repiten esta valoración con su propio texto; el código de acceso, además, exige una lectura aparte por el riesgo de fraude.

Fuentes consultadas el 2026-10-31: Microsoft Learn, «Inline attachments» (`https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-attachment-inline`), «Send email with inline attachments» (`https://learn.microsoft.com/en-gb/Azure/communication-services/quickstarts/email/send-email-advanced/send-email-with-inline-attachments`) y referencia de `EmailAttachment.ContentId` en .NET (`https://learn.microsoft.com/en-us/dotnet/api/azure.communication.email.emailattachment.contentid?view=azure-dotnet`).
