# Landings (es y cat) y passkeys por subdominio: lectura de privacidad y seguridad

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-10-29. Segunda lectura experta del agente `dpo-residapp`, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado.

**Conclusión:** la propuesta es admisible y mejora la actual. Fijar las passkeys en el subdominio exacto es **mejor** que el dominio base. Lo que de verdad hay que corregir antes de publicar es de otra naturaleza: la maqueta carga recursos de terceros, el botón apunta a un sitio que no existe y las páginas legales de la landing no están escritas.

## 1. Passkeys en el subdominio exacto

**Sí, es mejor [Hecho + Suposición].** Con el dominio base como identificador de la passkey (`ServerDomain`), cualquier subdominio podría pedir una firma, y `PasskeyOrigin.IsAllowed` aceptaría el dominio y todos sus subdominios (`src/ResidApp.Web/Security/PasskeyOrigin.cs`, líneas 22 a 24). Hoy los subdominios son nuestros, pero un subdominio olvidado o secuestrado pasaría a ser un sitio con poder sobre las passkeys de producción. Con `app.residapp.es` exacto, ni la landing (`residapp.es`, que además es un sitio estático alojado quizá en un tercero) ni `dev.` ni `avisos.` pueden usarlas. Además `dev` y `app` quedan aisladas: nadie reutiliza por error una passkey de pruebas en producción.

Riesgos que quedan, por orden:

1. **Secuestro de subdominio (CNAME colgante) — riesgo real.** `dev` apunta por CNAME a `app-residapp-dev.azurewebsites.net` (`docs/pendientes-cj/orlando/dominio-y-correo.html`, línea 396). Si se borra o recrea esa Web App y el CNAME sigue, otra persona puede reclamar el nombre. **Acción:** crear siempre el TXT `asuid.<subdominio>` que Azure ofrece (el documento lo trata como opcional, línea 263: debe ser obligatorio) y, al borrar un recurso, borrar antes su registro DNS. Vale igual para los CNAME DKIM de `avisos` y para el hosting de las landings. Crear el DNS de `app.` solo cuando exista la Web App de producción.
2. **El registrador es la raíz de todo.** Quien entre en la cuenta de CDmon controla todos los subdominios. Verificación en dos pasos y bloqueo del dominio ya están en la pregunta 1.7 de la guía; añadir que no se usen certificados comodín (un certificado gestionado por nombre) y, **opcional**, registros CAA.
3. **Cookies con `Domain=.residapp.es`: hoy no hay ninguna [Hecho].** Todas se crean sin `Domain` (host-only), p. ej. `residapp_sesion` (`Program.cs`, líneas 158 a 160) y las de `src/ResidApp.Web/Security/*Cookie.cs`. Mantener esa regla en la revisión de código. Un sitio hermano comprometido aún podría "plantar" una cookie con ese `Domain` hacia `app.`; **opcional:** prefijo `__Host-` en la cookie de sesión.
4. **HSTS.** `UseHsts()` sin configurar (`Program.cs`, línea 383): 30 días; no consta `includeSubDomains` ni `preload` **[Suposición: a verificar en la versión de .NET]**. **Acción:** no activar `includeSubDomains` ni `preload` en la raíz `residapp.es` hasta que todos los subdominios (incluidos los de correo y los futuros) sirvan HTTPS. En `app.` no hace falta.
5. **Sin CSP, sin `Referrer-Policy` global ni `X-Frame-Options` general** (`docs/seguridad/hallazgos.md`, líneas 355 y 356). No depende de esta propuesta, pero antes de producción hay que cerrarlo (**[Decidir]** prioridad con Seguridad).
6. **Opcional:** que el servidor compare el origen exacto (`app.residapp.es`) y no "dominio o subdominio" (`PasskeyOrigin.cs`, líneas 5 y 6). Con un solo nivel de subdominio no es urgente.
7. **El ADR 0007 punto 5** (líneas 118 a 123) debe reescribirse: subdominio exacto por entorno, `dev.residapp.es` en desarrollo y `app.residapp.es` en producción, sin migrar passkeys entre ellos ni desde `azurewebsites.net`; quitar "dominio base… válido en todos los subdominios… que no deben alojar contenido de terceros", y eliminar "dominio de Pro por elegir". Es un cambio de ADR: lo hace Orlando o una sesión de código, no este agente.

## 2. Dos landings (es y cat)

**Obligaciones: las mismas, por duplicado.** Cada sitio necesita su identificación del prestador (LSSI, art. 10: nombre o razón social, NIF, domicilio, correo), su política de privacidad (RGPD, art. 13) y su declaración sobre almacenamiento en el dispositivo (LSSI, art. 22.2). Fuente: BOE-A-2002-13758, consultada el 2026-10-29 (el texto consolidado se modificó el 2024-12-24; **a verificar** el vigente).

- **[Hecho]** En la landing el responsable es **ResidApp/Orlando**, no ninguna residencia (`docs/pendientes-cj/orlando/analisis-landing-page.html`, línea 260). Son documentos **distintos** de la política de la aplicación, donde la responsable es la residencia (ADR 0007). Hay que añadir una línea al registro de actividades de ResidApp para los contactos comerciales. **[Decidir]** titular: persona física o sociedad (su NIF y domicilio quedan públicos en el aviso legal).
- **¿Páginas legales en catalán? Sí, recomendado [Suposición].** El art. 12 RGPD pide información comprensible para quien la recibe: una página en catalán que enlaza a textos legales solo en castellano debilita la transparencia. La obligación específica de ofrecerlas en catalán (Codi de consum de Catalunya, art. 128-1) protege al consumidor y la landing es de empresa a empresa; **a verificar con un abogado** si aplica. Decidir qué versión prevalece si discrepan.
- **Cookies:** con dos sitios sin selector de idioma dentro de la página no hace falta guardar la preferencia de idioma. Si cada landing no guarda nada en el dispositivo ni carga recursos de terceros, basta decirlo en la política, sin banner. Cualquier analítica, mapa o vídeo cambia esto (la guía de la AEPD sobre cookies de medición de audiencia limita las exentas).
- **Formulario de contacto:** mejor `mailto:` al inicio (análisis, línea 260). Con dos dominios, dos buzones (`info@residapp.es`, `suport@residapp.cat`) significan dos MX, SPF y DMARC y un solo procedimiento de derechos: ambos deben desembocar en el mismo registro. **[Decidir]** quién los atiende.
- **Desalineación entre versiones — riesgo real, pero gestionable.** Textos legales distintos (plazos, correo de contacto, titular) o una versión sin actualizar son incumplimientos objetivos. **Acción:** una sola fuente con los textos legales en dos idiomas, mismo repositorio y mismo despliegue, fecha de versión visible y lista de comprobación "cambio legal = cambio en es y cat".
- **Dominios sin correo:** publicar en la raíz de `residapp.es` y de `residapp.cat` un SPF restrictivo (`-all`) donde no se envíe correo y DMARC, para que nadie suplante a ResidApp ante familias. El DMARC `p=none` del documento (línea 328) es solo el principio; **[Decidir]** cuándo endurecer.
- **[Hecho] Maqueta con terceros** (`docs/bocetos-pantallas/maqueta_residapp_landingPage.html`): Tailwind desde `cdn.jsdelivr.net` (línea 7) y fotos de `unsplash.com` (líneas 91 y 140). Cada visita envía su IP a esos terceros (algunos fuera de la UE) sin información previa. Antes de publicar: todo local, sin CDN ni imágenes remotas. Además, "Manual" y "Flujo" apuntan a `azurewebsites.net` (líneas 317 y 318).

## 3. El botón «Acceso clientes»

- **Enlace a un sitio que no existe: riesgo real.** `app.residapp.es` aún no tiene DNS. Mientras no haya producción, **no enlazar a `dev.`** desde una landing pública: invita a directores y familias reales a un entorno sin contrato, sin EIPD y con datos ficticios, y lo hace indexable. **Acción:** ocultar el botón hasta que exista producción (o, en demostraciones, enlace manual a `dev.` solo a quien lo recibe de nosotros). `dev.` debe llevar `X-Robots-Tag: noindex` **[Suposición: no consta hoy]** y su primera página, un rótulo "entorno de pruebas, no introduzca datos reales".
- **Referer y tracking: bajo.** Entre dominios distintos el navegador envía por defecto solo el origen (`https://residapp.es/`), sin ruta. No hay `Referrer-Policy` global en la aplicación (hallazgos, línea 355): ponerla (`strict-origin-when-cross-origin` o `no-referrer`) y `rel="noopener noreferrer"` en el enlace (maqueta, línea 33, solo tiene `target="_blank"`). **No añadir** parámetros UTM ni identificadores al enlace, y no medir con herramientas de terceros el clic: saber qué centro entra en "Acceso clientes" ya es información comercial.
- **Suplantación (phishing):** los avisos por correo (ADR 0012) deben ser sin enlace de entrada o con la dirección fija `app.residapp.es` sin parámetros, para que la familia aprenda a no pulsar enlaces del correo.

## 4. Choques con los ADR y con `antes-de-datos-reales.md`

- **ADR 0007:** solo el punto 5 (ver 1.7). Puerta abierta 4 (líneas 114 a 116), la contradicción FAM-10, sigue abierta e independiente. Pérdida de passkeys al pasar de `azurewebsites.net` a `dev.`: ya prevista (línea 122), solo datos de prueba.
- **ADR 0012:** compatible. `avisos.residapp.es` cumple el requisito 4 (líneas 31 a 34). El correo con código de acceso (línea 13) hace del correo un segundo factor: la protección del dominio frente a suplantación (SPF/DKIM/DMARC) pasa de recomendable a **necesaria** antes de producción.
- **ADR 0011:** sin choque. No añadir a las landings chatbots ni asistentes de IA (serían un tratamiento nuevo, fuera de los casos aprobados). Las frases sobre IA o dictado de la landing deben coincidir con el ADR 0009/0011.
- **`docs/privacidad/antes-de-datos-reales.md`:** no recoge nada del dominio ni de la landing. **Propuesta [Decidir]** añadir tres líneas: (a) política de privacidad, aviso legal y cookies de las landings validados en es y cat; (b) `app.residapp.es` creado con DNS y TXT de verificación, HSTS y cabeceras cerradas; (c) la política de la aplicación (`Views/Home/Privacy.cshtml`, hoy "sin redactar") como plantilla adoptada por cada residencia.

## A verificar o decidir

1. **[Decidir]** Titular de las webs (persona o sociedad) y alojamiento de las landings.
2. **[Decidir]** Ocultar el botón hasta producción.
3. **[Decidir]** Versión legal que prevalece (es o cat) y quién actualiza ambas.
4. **A verificar (abogado):** si el art. 128-1 del Codi de consum obliga a la información legal en catalán en una web B2B; texto vigente de la LSSI.
5. **A verificar:** valores por defecto de `UseHsts()` en la versión de .NET del proyecto; que `dev.` envíe `noindex`.
6. **[Decidir]** Plazo de conservación de los contactos comerciales (el análisis propone 6 a 12 meses: sin contrastar).
7. **[Decidir]** Cuándo endurecer DMARC.
