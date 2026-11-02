# Pendientes de la landing (`residapp.es` y `residapp.cat`)

Estado al 2026-11-02. Sale del análisis [`analisis-landing-page.html`](../../pendientes-cj/archivados/analisis-landing-page.html) (archivado el mismo día: lo decidido por Orlando el 29/10 está en él y lo que falta para construirla está aquí). **No hay nada construido**: no existe landing publicada ni alojamiento elegido. La maqueta de partida es [`maqueta_residapp_landingPage.html`](../../bocetos-pantallas/maqueta_residapp_landingPage.html); no se publica tal cual.

Una *landing* es la página pública de presentación del producto, la que ve quien aún no tiene cuenta. Una *página estática* es una página fija, sin base de datos ni inicio de sesión.

## Decidido por Orlando (2026-10-29)

- `residapp.es` es la landing en castellano y `residapp.cat` la landing en catalán; esta última avisa de que la plataforma está en castellano. Las dos llevan a la aplicación en `app.residapp.es` (producción, aún inexistente).
- La landing va **aparte de la aplicación** (sitio estático) y se **rediseña**: coherente con la app y conforme a la [guía de diseño](../../bocetos-pantallas/guia-diseno-sistema-visual.md) (Bootstrap 5, WCAG 2.2 AA, claro y oscuro, logo real `logo.svg`).
- **Nada de terceros**: ni librerías servidas desde otro sitio (la maqueta carga Tailwind desde jsdelivr), ni letras, ni fotos de Unsplash. Cargar algo de fuera envía la IP de cada visitante a un tercero.
- No enlaza al Manual ni al Flujo de trabajo, ni a nada de `dev.residapp.es`.
- Lleva precios y planes por censo del centro, con módulos opcionales cobrados aparte; FAQ escrita a mano en los dos idiomas; y como chat, un **asistente guiado sin IA** (botones y respuestas escritas por nosotros). Un bot con IA conversacional queda fuera hasta que haya un ADR nuevo (el 0011 solo aprueba el dictado por voz) y lo revise el DPO real.
- Las frases prudentes de la sección 8 del análisis sustituyen a las de la maqueta (sin absolutos como «cumplimiento total» o «infranqueable»).

## Qué hay que hacer

### Antes de publicar nada (etapa 1: landing en fase de pilotos)

1. **Decidir si se hace ya o tras el piloto real** (Orlando). Sin eso, el resto espera.
2. **Alojamiento**: cualquier alojamiento de páginas fijas en Europa, barato y con certificado gratuito (Azure Static Web Apps o el plan que ya haya en CDmon, a comprobar). Elegirlo (Orlando).
3. **Rediseño** según la guía visual, con imágenes propias o con licencia (nunca capturas de residentes reales) y todo guardado en el propio sitio.
4. **Textos**: sustituir las frases de la tabla de la sección 3 del análisis por las de la sección 8; quitar tarifas y «exportación avanzada» mientras no existan; cada frase se contrasta con el código y con `docs/producto/` (la landing no puede contradecir a la app). Selector es/cat como simple enlace entre `residapp.es` y `residapp.cat`.
5. **Contacto**: al principio, un enlace `mailto:` o un buzón compartido con la información de protección de datos al lado, no un formulario. Un formulario real exigiría guardar los datos aparte de la base clínica, un sistema contra correo basura sin rastreadores y un contrato con el servicio que lo recoja. Con aviso «No escriba datos de residentes» sobre cualquier cuadro libre. Decidir a qué dirección llega y quién lo atiende (Orlando). Hoy no hay buzón `@residapp.es` (Microsoft 365: «todavía no»).
6. **Páginas legales** en castellano y catalán: aviso legal con el titular (nombre o razón social, NIF y domicilio), política de privacidad y declaración de cookies (si no pone ninguna, basta decirlo). El titular es una decisión pendiente de Orlando (persona física o sociedad).
7. **Botón «Acceso clientes»** oculto hasta que exista `app.residapp.es`.
8. **FAQ y asistente guiado** en los dos idiomas, contrastados con la aplicación y con las frases prudentes; las respuestas sobre seguridad, protección de datos y dictado por voz las valida la persona experta.
9. **Validación de la persona experta** (DPO real o abogado) de las páginas legales, la FAQ y las frases (ver [`antes-de-datos-reales.md`](../../privacidad/antes-de-datos-reales.md)). Es el bloqueo para publicar.

### Precios (etapa 3: comercial)

10. **Importes de los planes**: los fijan Orlando y CJ (los de la maqueta no tienen base). Una sola fuente de precios para las dos landings.
11. **Qué es base y qué es módulo**: depende de [`modulos-base-y-opcionales-cj`](../../pendientes-cj/modulos-base-y-opcionales-cj.html) (contestado por CJ el 2026-10-30: todo es base salvo exportar la estructura/censo y el dictado por voz).
12. **Interruptor de módulos por centro** en la aplicación (hoy no existe, ni topes por residentes ni facturación); encaja con el ADR 0008 (multicentro). Mientras no exista, lo no construido se anuncia como «próximamente» o no se anuncia.
13. Antes de publicar un precio: titular, condiciones de contratación, IVA y contrato de encargado con cada centro; qué obligaciones tiene publicar precios en una web B2B, **a validar con un abogado**.

## Estado de las dependencias

- Titular de ResidApp y persona experta: pendientes, antes del primer centro real (`antes-de-datos-reales.md`, apartado 7).
- `app.residapp.es` y Microsoft 365: antes de hablar con un centro real.
- Versión catalana de la landing: coherente con el [ADR 0013](../../decisiones-arquitectura/0013-portal-familiar-dos-idiomas.md); la aplicación sigue solo en castellano, así que la página catalana no puede prometer catalán en la plataforma.
- Lo que puede decirse ya, antes del piloto real: «en fase de pilotos», «diseñada pensando en la privacidad desde el principio», «el centro sigue siendo el responsable de los datos de sus residentes», «sin datos reales hasta cerrar el contrato y la evaluación de impacto». Las demostraciones, con datos ficticios.
