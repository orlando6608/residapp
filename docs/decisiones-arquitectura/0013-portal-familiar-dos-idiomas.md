# ADR 0013 — Portal Familiar y pantallas de entrada en castellano y catalán

Estado: **propuesta** el 2026-10-29, con las decisiones de Orlando de ese día. Nada de lo que describe está construido. Pendiente de: la respuesta de CJ
(`docs/pendientes-cj/portal-familiar-dos-idiomas-cj.html`) sobre si hace falta y cuándo, y de la revisión del DPO real (la persona que vela por la protección de datos).
La lectura de privacidad del agente `dpo-residapp` está en `docs/privacidad/portal-familiar-dos-idiomas.md`.

## Contexto

La aplicación está solo en castellano (`Program.cs` fija `es-ES` y no hay ficheros de traducción). Orlando decidió el 2026-10-29 que `residapp.cat` tendrá una landing en
catalán (`docs/pendientes-cj/orlando/analisis-landing-page.html`) y que los correos a las familias se redactarán en los dos idiomas (ADR 0012, requisito 7). Para que la
familia no pase del correo o la landing en catalán a una pantalla en castellano, se propone que **la parte que usan las familias** también esté en los dos idiomas. La parte
profesional (todo lo que ve el personal) sigue solo en castellano.

Alcance medido el 2026-10-29 (estimación, sin cerrar): 12 vistas del Portal Familiar (unas 940 líneas) más su diseño de página, unos 25 mensajes de los controladores,
las 10 vistas de entrada y «Mi cuenta» (667 líneas, compartidas con el personal), las etiquetas de tipos de comunicado y equipos, y la página de Privacidad: **unos 300 a 400
textos**.

## Decisión propuesta

1. **Qué se ofrece en los dos idiomas:** el Portal Familiar, las pantallas de entrada (invitación, contraseña, segundo factor, códigos de recuperación, «Mi cuenta») y los
   correos. El personal ve toda su aplicación en castellano, también cuando está en pantallas de entrada compartidas (tras entrar, vuelve al castellano).
2. **El idioma de cada familiar se guarda en su cuenta.** Un campo nuevo, de **valor cerrado** `es` o `ca`, vacío cuando no ha elegido (entonces se usa el idioma por defecto del
   centro). Lo cambia el propio familiar en «Mi cuenta» cuando quiera (y Administración, a petición suya, como rectificación). Sirve para: mostrar el portal, enviar los correos y
   la sugerencia al personal (punto 5). Nada más.
3. **El centro tiene un idioma por defecto** (`es` o `ca`), un ajuste nuevo del centro que fija Administración o Plataforma. No es un dato personal.
4. **Cómo se elige el idioma de las pantallas de entrada**, por orden (antes de entrar no se sabe a qué centro pertenece la persona):
   1. el selector `Castellano | Català` que la persona pulsa en pantalla, guardado en una cookie solo de idioma (`es` o `ca`, fijada por el servidor, sin dominio, `Secure`,
      `SameSite=Lax`, caducidad corta y proporcionada);
   2. el parámetro del enlace `?idioma=ca` o `?idioma=es`, para esa visita; el botón «Acceso clientes» de `residapp.cat` apunta a `https://app.residapp.es/?idioma=ca`;
   3. castellano.

   Una vez identificado el familiar, **gana el idioma guardado en su cuenta**; si no lo ha elegido, el del centro. El parámetro **nunca** escribe en la cuenta ni se mezcla con la
   dirección de retorno (`returnUrl`); cualquier valor que no sea `es` o `ca` se ignora sin error. **[Decidir]** Orlando: el agente de protección de datos recomienda guardar la
   cookie solo cuando la persona pulse el selector en la aplicación (así queda clara la exención de consentimiento); Orlando había propuesto que el catalán fuera el valor por
   defecto de quien llega desde `residapp.cat`, lo que con esta regla vale para esa visita.
5. **Sugerencia al personal que redacta un comunicado (no obligatoria).** Si algún familiar con acceso a ese residente ha **elegido** catalán, se muestra un aviso en
   castellano: «Alguna persona con acceso a este residente ha elegido catalán. Puede redactar también en catalán.» **Sin cifras, sin nombres, sin lista ni filtro por idioma,
   sin registro de quién lo vio.** Solo al personal con permiso de redactar comunicados para ese residente. Nunca se sugiere por el idioma por defecto del centro. El comunicado
   lo sigue escribiendo el personal en el idioma que quiera: **la aplicación no lo traduce** (traducir con IA sería un caso de uso nuevo, no aprobado por el ADR 0011).
   **[Decidir]** DPO real: el agente advierte de que, con una o dos personas con acceso, el aviso sigue señalando a quien lo eligió, y propone como alternativa más prudente
   ofrecer siempre «redactar también en catalán» en los centros con catalán, sin dato nuevo. Esta sugerencia no se construye hasta que el DPO real la apruebe.

## Requisitos de privacidad (resumen; el detalle está en `docs/privacidad/portal-familiar-dos-idiomas.md`)

- El campo es dato personal (no categoría especial por sí mismo, pero se une a un contexto sanitario): valor cerrado, nunca inferido (ni por nombre, apellidos, IP, centro ni
  `Accept-Language`), rectificable, sin historial de valores anteriores y que el personal no ve nunca por persona.
- La cookie de idioma debe constar en la declaración de cookies, en los dos idiomas.
- El registro de envíos de correo (ADR 0012, requisito 3) guarda el idioma de la plantilla: misma conservación que el resto de ese registro.
- La información al familiar (castellano y catalán) dice qué idioma se guarda, para qué, que es voluntario y cómo cambiarlo.
- Respuestas que cambian según el idioma: `Vary: Cookie` o sin caché en los dos casos; la página sin conexión (`Offline`) no lee cookies.
- Política `Referrer-Policy` global y ningún recurso de terceros en las pantallas de entrada.

## Fuera de alcance

- Traducir la parte profesional (hoy y en el piloto).
- Más idiomas que `es` y `ca` (cada uno exige revisión del DPO por cada plantilla y texto legal).
- Traducción automática de los comunicados.

## Orden de construcción propuesto

1. Mecanismo (un solo punto que decide el idioma de cada petición; el personal siempre en castellano), ficheros de traducción y la entrada.
2. Portal Familiar.
3. Campo de la cuenta, selector en «Mi cuenta» y el idioma por defecto del centro (script nuevo; siguiente número libre al empezar).
4. Los correos por idioma, junto con el ADR 0012.
5. La sugerencia al personal, solo si el DPO real la aprueba.

Una prueba barata y útil: que los dos ficheros de idioma tengan siempre las mismas claves.

## Consecuencias

- Cada texto nuevo del Portal Familiar y de la entrada se escribe en dos idiomas, y una persona que sepa catalán lo revisa (pregunta 3.1 del documento de CJ).
- La landing de `residapp.cat` puede decir que el portal para familias está disponible en catalán y castellano y que la plataforma profesional está en castellano.
- Lo supuesto por ingeniería y lo que valida una persona (DPO real o abogado): base jurídica del campo, la sugerencia, la política de cookies y la información al familiar en
  catalán, y si los derechos lingüísticos en Cataluña obligan a la residencia a atender en catalán.
