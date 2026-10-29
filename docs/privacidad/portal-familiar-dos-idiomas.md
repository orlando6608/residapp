# Portal Familiar y pantallas de entrada en castellano y catalán: lectura de privacidad

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-10-29. Segunda lectura experta del agente `dpo-residapp`, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado.

**Conclusión:** admisible con condiciones. Ofrecer el portal en dos idiomas mejora la transparencia (RGPD, art. 12: información comprensible para quien la recibe). Las dos
cosas que más pesan son: (1) la sugerencia al personal con «N de M» **no es anónima** cuando M es pequeño (un residente suele tener una o dos personas con acceso), y
(2) la cookie de idioma solo está claramente exenta si la fija una **elección del usuario en pantalla**, no una decisión automática del sitio.

## (a) La cookie de idioma y la exención de consentimiento (LSSI, art. 22.2)

- **Probablemente sí, con una condición [Suposición + fuente secundaria].** La actualización de la guía de cookies de la AEPD (julio de 2023, plazo de adaptación hasta el
  2024-01-11) trata como técnicas, y exentas, las cookies de personalización **que el usuario elige** (el ejemplo clásico es pulsar el idioma). Si es el sitio quien decide a
  partir de información previa del usuario, hay que informar y ofrecer aceptar o rechazar. Fuente consultada hoy: nota de prensa de la AEPD y comentarios de despachos
  (Cuatrecasas, ECIJA); **no pude abrir el texto de la guía ni el art. 22.2 consolidado en fuente oficial: a verificar con abogado.**
- **Dónde está el matiz:** el parámetro `?idioma=ca` viene de pulsar «Acceso clientes» en la landing catalana. Es una elección real, pero **implícita** y hecha en otro sitio.
  Para quedar del lado seguro: guardar la cookie solo cuando la persona pulse el selector de idioma **en la pantalla de la aplicación**; el parámetro del enlace vale para esa
  visita (sin cookie) y la pantalla muestra el selector con el idioma activo bien visible. **[Decidir]** Orlando; si prefiere guardar la cookie desde el parámetro, es un caso para el abogado.
- Cautelas de la cookie: valor `es` o `ca` fijado por el servidor (nunca el texto recibido), sin `Domain` (solo `app.residapp.es`, como el resto: `Program.cs`, líneas 158 a 160
  y `docs/privacidad/landings-y-passkeys-por-subdominio.md`, apartado 1), `Secure`, `SameSite=Lax`, caducidad corta y proporcionada (por ejemplo 6 a 12 meses, **a verificar**
  qué tope admite la AEPD para cookies exentas), sin identificador, sin mezclar con la cuenta. Aunque no lleve dato personal, **la LSSI se aplica a cualquier almacenamiento
  en el dispositivo**: debe constar en la declaración de cookies, en los dos idiomas.
- **Cuenta identificada:** no hace falta cookie; el idioma se lee de la cuenta.
- **Orden de prioridad — dos correcciones [Suposición]:** (i) antes de entrar **no se sabe el centro**, así que el «idioma del centro» no puede aplicarse a las pantallas de
  entrada (la dirección es la misma para todos); antes de identificarse solo valen pantalla, parámetro, cookie y `es`. (ii) Una elección **guardada en la cuenta** debería ganar al
  parámetro de un enlace: la persona ya dijo lo que quiere. **[Decidir]**.
- **El parámetro nunca debe escribir en la cuenta.** Si lo hiciera, quien solo pulsó un enlace contaría como «ha elegido catalán» y la sugerencia al personal sería inexacta
  (RGPD, art. 5.1.d). Solo se guarda en la cuenta con una acción explícita en «Mi cuenta» (o una casilla clara de «recordarlo en mi cuenta»).

## (b) El campo «idioma preferido» ¿dato personal? ¿categoría especial?

- **Dato personal: sí [Hecho en el diseño]** — va ligado a una cuenta identificada de un familiar (RGPD, art. 4.1). Entra en el registro, en la información y en los derechos
  (ADR 0012, línea 43 a 45).
- **Categoría especial (art. 9): no por sí mismo [Suposición].** El art. 9 pide que el dato revele origen étnico o racial, convicciones, salud, etc. Una preferencia lingüística de
  contacto no lo dice, y en Cataluña el catalán es lengua propia y cooficial. **Pero** la jurisprudencia europea interpreta con amplitud los datos que *revelan* por inferencia
  (sentencia del TJUE C-184/20 — citada de memoria, **a verificar**), y aquí el dato se une a un contexto sanitario (el familiar de un residente). Por eso se trata con cautela
  como si pudiera inferirse algo, aunque no sea categoría especial.
- **Cautelas:**
  1. Valor cerrado `es`/`ca`, `NULL` = no ha elegido; ningún texto libre ni otras lenguas sin nueva revisión (ADR 0012, línea 41).
  2. **No inferir** nunca (nombre, apellidos, navegador, IP, lengua del centro): ya descartado en ADR 0012, líneas 45 y 46. Mantenerlo: la aplicación **no debe leer** `Accept-Language`.
  3. Solo lo cambia el propio familiar (y Administración, a petición suya, como rectificación); cada cambio sin historial de valores anteriores, sin auditoría de contenido.
  4. Finalidad cerrada y dicha al familiar: mostrar el portal, enviar los correos y avisar al personal. Nada más (no estadísticas, no informes).
  5. El personal **no ve el idioma de ninguna persona**, solo la sugerencia del apartado (c). Plataforma, Dirección e informes: no.
  6. Si la persona pierde el acceso al residente o se borra la cuenta, el dato se va con ella. El «idioma del centro» no es dato personal.
  7. El registro de envíos de correo ya anota el idioma de cada plantilla por familiar (ADR 0012, línea 30): es una copia del dato; fijar para ese registro **la misma conservación**.
- **Base jurídica [Suposición, Decidir con DPO]:** el portal en el idioma elegido es parte del servicio que la residencia ofrece a la familia (art. 6.1.b o 6.1.f; **a validar**).
  No necesita consentimiento, pero la información sí.

## (c) La sugerencia agregada al personal ¿reidentifica?

- **Sí, cuando M es pequeño [Suposición razonable].** El personal que redacta conoce o puede ver quiénes son las personas con acceso a ese residente. Con M = 1, «1 de 1 prefiere
  catalán» **es** nombrarla; con M = 2 y N = 2, nombra a las dos; con N = 0 no revela nada. El agregado por residente no es anonimización (RGPD, considerando 26): es el mismo dato con
  menos detalle. Una regla de mínimo tipo «mostrar solo si M ≥ 3» **no sirve**: casi nunca se cumpliría y entonces la función no existe.
- **Compatibilidad del fin (art. 6.4):** que el personal sepa en qué idioma escribir a esa familia es coherente con el motivo por el que la familia eligió idioma. Por tanto no
  es una reutilización incompatible **siempre que se le haya dicho** (información al familiar) y sea lo que haga el personal (escribir mejor), no un control sobre la persona.
- **Condiciones propuestas [Decidir]:**
  1. **Quitar los números.** Mostrar solo un aviso sin cifras («Alguna persona con acceso a este residente ha elegido catalán. Puede redactar también en catalán») y solo con
     elección explícita; nunca por defecto del centro ni por inferencia. Con M = 1 sigue siendo identificable, pero el fin es legítimo y el personal ya conoce a la familia.
  2. Sin nombres, sin lista ni filtro por idioma, sin exportación, sin búsqueda «familias que prefieren catalán».
  3. Solo al personal con permiso de redactar comunicados **para ese residente** (el alcance de siempre), no a Dirección en bloque.
  4. Sin registrar en auditoría quién vio el aviso ni cuántas veces.
  5. **Alternativa más prudente:** ofrecer siempre la opción «redactar también en catalán» en los centros con catalán, sin depender de ningún familiar. Sin dato nuevo, sin riesgo de
     reidentificar. Si Orlando quiere la sugerencia, que el DPO real lo apruebe expresamente.
- Recordar: el texto del comunicado es libre y lo escribe el personal; su contenido sanitario es asunto de la política de comunicados, no de este campo.

## (d) El parámetro `?idioma=ca` en la URL

- **Open redirect: no hay riesgo si el parámetro solo se compara con `es`/`ca` [Hecho + Suposición].** No debe usarse como destino ni reflejarse en la página. La aplicación ya trata
  los destinos de retorno con `Url.IsLocalUrl` (`AccesoController.cs`, líneas 113, 354 y 431) y `LocalRedirect` (línea 428); no mezclar el idioma con `returnUrl`. Cualquier valor distinto
  se ignora, sin error ni eco.
- **Registros: riesgo bajo.** `?idioma=ca` no es dato personal; indica solo que la persona llegó desde la landing catalana. La tabla `dbo.eventos_acceso` guarda la IP pero **no la
  URL** (`database/scripts/0062_credenciales_y_eventos_acceso.sql`, líneas 32 a 38 [Hecho]). **A verificar:** si el registro HTTP de la Web App de Azure o una herramienta de
  telemetría guardan la dirección completa con IP; si sí, entra en la conservación de esos registros.
- **Referer: bajo.** La aplicación recibe como origen `https://residapp.cat/` (sin ruta). Hacia fuera, `Referrer-Policy: no-referrer` solo se pone en la invitación
  (`AccesoController.cs`, línea 135 [Hecho]); no hay política global (`docs/seguridad/hallazgos.md`, línea 355). Ponerla antes de producción, y no cargar recursos de terceros en las
  pantallas de entrada.
- **Caché del navegador y de la PWA:** una página que cambia según el idioma no debe servirse a otra persona en el mismo dispositivo. La caché del service worker ya se limita
  (`docs/decisiones-arquitectura/directrices-pwa-movil.md`, líneas 42 y 43); la página `Offline` debe seguir sin leer cookies ni idioma. Para las respuestas con idioma por cookie,
  `Vary: Cookie` o `Cache-Control: no-store` **[Suposición]**.
- **Suplantación (phishing):** el parámetro es inofensivo en sí, pero la recomendación anterior era que los correos del ADR 0012 enlacen a `app.residapp.es` **sin parámetros**
  (`landings-y-passkeys-por-subdominio.md`, apartado 3). Los correos en catalán no necesitan `?idioma=ca` si la cuenta ya tiene idioma. **[Decidir]**: mantener la regla y que el
  correo diga en el mismo idioma que el enlace «lleva al portal en tu idioma».

## (e) Líneas para el registro, la información y `antes-de-datos-reales.md`

**Registro de actividades de tratamiento** (art. 30; la residencia como responsable y ResidApp en el suyo como encargada):
- «Gestión del acceso y la comunicación con familiares: **idioma preferido de la familia** (`es`/`ca`)». Finalidad: mostrar el portal y los correos en el idioma elegido y orientar al
  personal. Categoría: datos identificativos de familiares (no especiales). Destinatarios: personal con permiso de redactar comunicados (solo como aviso sin nombre), proveedor de
  correo (por la plantilla). Conservación: la de la cuenta del familiar. Seguridad: valor cerrado, sin inferencia.
- «Idioma por defecto del centro» (ajuste del centro, sin datos personales) y «cookie técnica de idioma» (en la declaración de cookies).

**Información al familiar** (art. 13; en castellano y catalán, texto que adopta la residencia):
- Qué dato se guarda (solo si lo elige), para qué (portal, correos y que el personal pueda saber si escribirle también en catalán), que es voluntario, que puede cambiarlo o quitarlo
  en «Mi cuenta» o pidiéndolo a la residencia, y que no se deduce de su nombre ni de su navegador.
- Que la aplicación guarda en su dispositivo una cookie de idioma si elige uno en pantalla, y para qué.
- Que los dos idiomas son el mismo contenido y qué versión prevalece si discrepan **[Decidir]**.

**`docs/privacidad/antes-de-datos-reales.md`** (añadir un apartado, sin tocarlo yo):
- Declaración de cookies y política de privacidad del Portal Familiar validadas en es y ca.
- Cláusula informativa al familiar con el campo de idioma, validada por el DPO real.
- Decisión del DPO sobre la sugerencia al personal (con o sin cifras) y sobre la base jurídica.
- Texto de las plantillas de correo en catalán revisado por una persona que sepa catalán (ADR 0012, línea 39).

## Choques con los ADR

- **ADR 0012, líneas 40 a 43 [Hecho]:** dice que la aplicación está «solo en español» y que el correo en catalán avisa de ello, y condiciona el selector a un «análisis pendiente».
  Con esta decisión queda desactualizado. Lo reescribe Orlando o una sesión de código, no este agente.
- **ADR 0007:** no choca. El segundo factor y la invitación no cambian; las pantallas de entrada compartidas por personal y familias no deben mostrar nada que distinga a las dos poblaciones.

## A verificar o decidir

1. **A verificar (abogado):** art. 22.2 LSSI vigente y la guía de cookies de la AEPD en fuente oficial; tope de caducidad aceptable para una cookie de idioma exenta.
2. **A verificar:** C-184/20 (TJUE) y su alcance sobre datos por inferencia.
3. **A verificar:** si el registro HTTP de Azure o la telemetría guardan la URL completa con IP y cuánto tiempo.
4. **A verificar (abogado):** si los derechos lingüísticos de las personas usuarias de servicios sociales en Cataluña obligan a la residencia a atender en catalán (ADR 0012, línea 81).
5. **[Decidir]** Cookie solo por elección en pantalla o también desde el parámetro; prioridad definitiva (cuenta frente a parámetro).
6. **[Decidir]** Sugerencia al personal: con o sin cifras, o la alternativa de ofrecer siempre «también en catalán».
7. **[Decidir]** Quién fija el idioma por defecto del centro (la residencia, como responsable).
8. **[Decidir]** Versión que prevalece, y quién revisa el catalán de la información legal.
9. **A verificar:** autoridad de control (AEPD o APDCAT) según el tipo de centro piloto.
