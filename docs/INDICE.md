# Índice de fuentes de verdad

**Para qué sirve.** Aquí encuentras **qué documento manda** sobre cada tema y cuáles solo lo acompañan. No repite su contenido: apunta a él. Para saber **en qué punto está el trabajo**, mira [continuar-construccion.md](prompts-ia/continuar-construccion.md); para las **reglas de colaboración**, [CLAUDE.md](../CLAUDE.md). Lo que valga para todas las personas y herramientas va al repositorio, no a la memoria privada de una de ellas.

**Cómo se mantiene.** Cuando crees, muevas o retires un documento que sea la fuente de un tema, actualiza su fila en el mismo cambio. Un puntero que ya no apunta a nada se corrige o se quita; no se deja. La prueba [IndiceDeFuentesTests](../tests/UnitTests/IndiceDeFuentesTests.cs) falla si un enlace apunta a algo que ya no existe, si un ADR numerado no tiene su enlace o si una carpeta de `docs/` queda sin ningún enlace; lo que una prueba no puede saber (que la fuente que dice cada fila siga siendo la correcta) se revisa a mano. Las palabras que pueden sonar técnicas se explican al final.

## Mapa por tema

### Producto

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| Qué es ResidApp y qué pretende | [vision-general.md](producto/vision-general.md) y [objetivos.md](producto/objetivos.md) | Las metas de `objetivos.md` son hipótesis del piloto, no estándares clínicos. |
| Módulos, perfiles y límites de cada perfil | [alcance.md](producto/alcance.md) | Es la referencia vigente. Su «Nota de vigencia» dice dónde está la matriz de permisos original y qué decisiones suyas siguen abiertas. |
| Qué está construido y qué falta | [roadmap.md](producto/roadmap.md) y, por vertical, los `pendientes-*.md` de [tareas/alta-prioridad/](tareas/alta-prioridad/) | |
| Lo pospuesto a propósito | [backlog-diferido.md](tareas/futuro/backlog-diferido.md) | Traerlo de vuelta al alcance lo decide el Product Owner. |
| Una función con diseño aprobado | [citas-externas-diseno.md](producto/citas-externas-diseno.md) y [ficha-antecedentes-salud-diseno.md](producto/ficha-antecedentes-salud-diseno.md) | Cada uno declara su estado en la línea «Estado» de su cabecera. |
| Propuestas sin decidir | [antecedentes-medicacion-alergias-propuesta.md](producto/antecedentes-medicacion-alergias-propuesta.md) y la [especificación de dashboards](<producto/Especificación de dashboards de ResidApp.md>) | Borrador o propuesta: no deciden nada todavía. |
| Estudio de mercado y de experiencia de uso | [estudio-mercado-ux-2026-10.md](producto/estudio-mercado-ux-2026-10.md) | Referencia no vinculante. |

### Requisitos y pantallas

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| Procesos asistenciales paso a paso | [flujos-clinicos/](flujos-clinicos/indice.md): empieza por `indice.md`; hay un fichero por proceso | Si el código los contradice, se para y se avisa. |
| Qué necesita cada perfil y cómo se comprueba | [historias-usuarios/](historias-usuarios/): un fichero por perfil, más `plataforma-modulos.md` | |
| Qué muestra cada pantalla | [wireframes-funcionales/](bocetos-pantallas/wireframes-funcionales/) | Propuesta de interfaz; no es fuente de reglas de negocio. |
| Colores, iconos, accesibilidad y uso en móvil | [guia-diseno-sistema-visual.md](bocetos-pantallas/guia-diseno-sistema-visual.md) y [directrices-pwa-movil.md](decisiones-arquitectura/directrices-pwa-movil.md) | La guía de diseño es **vinculante**: se cumple al maquetar cualquier vista. Las directrices fijan cómo se traduce al móvil y a la tablet. |
| La landing (la web pública de presentación) | Maqueta visual: [maqueta_residapp_landingPage.html](bocetos-pantallas/maqueta_residapp_landingPage.html); textos y pendientes: [landing-textos-borrador.md](tareas/futuro/landing-textos-borrador.md) y [pendientes-landing.md](tareas/futuro/pendientes-landing.md) | La maqueta es solo guía visual y carga recursos de terceros que no se pueden publicar tal cual (ver [landings-y-passkeys-por-subdominio.md](privacidad/landings-y-passkeys-por-subdominio.md)). |
| Preguntas, valores y decisiones de CJ | [pendientes-cj/](pendientes-cj/README.md) | Sus **respuestas no están en el repositorio**: viven en el servidor. `tools/respuestas-cj.ps1` las baja a `respuestas-cj-local/`, que git ignora. |
| Documentos de trabajo de Orlando (guías y decisiones de dominio) | [pendientes-cj/orlando/](pendientes-cj/orlando/) | No son para CJ. |

### Arquitectura y datos

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| Decisiones de arquitectura | [decisiones-arquitectura/](decisiones-arquitectura/): los ADR numerados de 0006 en adelante (los 0001 a 0005 eran del prototipo anterior) | Lista por tema, debajo de esta tabla. |
| Esquema de la base de datos | [database/scripts/](../database/scripts/) | Un fichero numerado por cambio; el último número ocupado se ve en la carpeta. Los ya aplicados en Azure no se editan. |
| Datos de ejemplo y centros de prueba | [database/seed/](../database/seed/) | Datos ficticios. |
| Usuarios de la base y aislamiento entre centros | [database/seguridad/](../database/seguridad/) | |
| Consultas de diagnóstico y operaciones puntuales | [database/diagnostico/](../database/diagnostico/) y [database/operaciones/](../database/operaciones/) | |
| Quién puede hacer qué | [ProfilePermissions.cs](../src/ResidApp.Domain/Accounts/ProfilePermissions.cs), la política de [Application/Authorization/](../src/ResidApp.Application/Authorization/) y sus pruebas (`ResidentBaselinePolicyTests`) | El resumen por perfil está en `alcance.md`, «Roles y responsabilidades». |
| Reglas de negocio que ejecuta la aplicación | [src/ResidApp.Domain/](../src/ResidApp.Domain/) y sus pruebas, en [tests/](../tests/) | |
| Modelo de datos del residente y del estado basal | [modelo-datos-basal-residente.html](decisiones-arquitectura/modelo-datos-basal-residente.html) (hay una copia en [PDF](decisiones-arquitectura/modelo-datos-basal-residente.pdf)) | Explica el modelo y contiene la propuesta de renombrado a español que aplicó el script `0002`; sus nombres de tablas en inglés son los de antes de ese renombrado. Los nombres vigentes están en los scripts. |
| Dominios, correo y portal en dos idiomas | [antes-de-datos-reales.md](privacidad/antes-de-datos-reales.md) (apartado 6), [ADR 0012](decisiones-arquitectura/0012-envio-correos.md) y [ADR 0013](decisiones-arquitectura/0013-portal-familiar-dos-idiomas.md) | Dos PDF de partida: [estrategia de dominios y correos](decisiones-arquitectura/Resumen_Estrategia_Correos_ResidApp.pdf) y [hoja de ruta de infraestructura](decisiones-arquitectura/Configuracion_Dominio_y_SSL_ResidApp.pdf). No llevan fecha y pueden estar superados en detalles: ante una duda, manda lo decidido después. |
| Por qué Dapper y no Entity Framework | [eleccion-acceso-datos-ef-core-vs-dapper.md](decisiones-arquitectura/eleccion-acceso-datos-ef-core-vs-dapper.md) | |
| La migración desde el prototipo anterior | [instrucciones-migracion-net10.md](decisiones-arquitectura/instrucciones-migracion-net10.md) y [integridad-sql-basal-legado.md](decisiones-arquitectura/integridad-sql-basal-legado.md) | Referencia de cómo se tradujo; el esquema vigente son los scripts. |
| Cuánto crece la auditoría | [analisis-crecimiento-auditoria.md](decisiones-arquitectura/analisis-crecimiento-auditoria.md) | |

**ADR por tema:** [0006](decisiones-arquitectura/0006-perfil-plataforma.md) perfil Plataforma y centro reservado · [0007](decisiones-arquitectura/0007-proveedor-identidad.md) identidad y acceso · [0008](decisiones-arquitectura/0008-multicentro-y-grupos.md) varios centros y grupos · [0009](decisiones-arquitectura/0009-nfc-voz-acceso-fisico.md) NFC y dictado por voz · [0010](decisiones-arquitectura/0010-importacion-exportacion-ficheros.md) importar y exportar por ficheros · [0011](decisiones-arquitectura/0011-arquitectura-ia.md) inteligencia artificial (**toda función con IA lo sigue**) · [0012](decisiones-arquitectura/0012-envio-correos.md) correos a las familias · [0013](decisiones-arquitectura/0013-portal-familiar-dos-idiomas.md) Portal Familiar en castellano y catalán · [0014](decisiones-arquitectura/0014-entorno-produccion.md) entorno de producción · [0015](decisiones-arquitectura/0015-modulos-opcionales.md) módulos opcionales por centro · [0016](decisiones-arquitectura/0016-dispositivos-autorizados.md) dispositivos autorizados. Al crear un ADR, añade su número aquí.

### Privacidad y seguridad

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| Qué hay que cerrar antes de usar datos reales | [antes-de-datos-reales.md](privacidad/antes-de-datos-reales.md) | Lista numerada: es la fuente. |
| Privacidad de cada función (qué datos, con qué base, con qué condiciones) | Los ficheros `*-lectura-dpo.md` y los de cada función, en [privacidad/](privacidad/) | Borradores de `dpo-residapp`: una persona los valida antes de usarlos con datos reales. |
| Procedimientos | [derechos](privacidad/procedimiento-derechos-borrador.md), [brechas](privacidad/protocolo-brechas-borrador.md), [expurgo y supresión](privacidad/expurgo-y-supresion.md) y [corte global del dictado](privacidad/procedimiento-corte-global-dictado.md) | Borradores. |
| Texto público de privacidad | [politica-privacidad-portal.md](privacidad/politica-privacidad-portal.md); en la aplicación, `src/ResidApp.Web/Views/Home/Privacy.cshtml` | |
| Material para una persona experta que revise | [dossier-persona-experta.md](privacidad/dossier-persona-experta.md) y [guia-cumplimiento-propietarios.md](privacidad/guia-cumplimiento-propietarios.md) | |
| Texto literal de las leyes y guías de las autoridades | `normativa-local/` (git la ignora); se baja con `pwsh tools/normativa.ps1`, como explica [privacidad/normativa/README.md](privacidad/normativa/README.md) | Cita de ahí, no de memoria. |
| Hallazgos de seguridad | `docs/seguridad/hallazgos.md` | Fichero local, excluido de git: no existe en un clon nuevo. El resto de [seguridad/](seguridad/) sí está. |

### Trabajo en curso

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| En qué punto estamos y qué sigue | [continuar-construccion.md](prompts-ia/continuar-construccion.md) | Es lo único que se lee al arrancar una sesión. |
| Lecciones, avisos y entradas anteriores | [lecciones-tecnicas.md](prompts-ia/lecciones-tecnicas.md), [avisos-abiertos.md](prompts-ia/avisos-abiertos.md) y [historial/](prompts-ia/historial/) | Se consultan por texto. El historial no decide nada. |
| Tareas por vertical | [alta-prioridad/](tareas/alta-prioridad/), [media-prioridad/](tareas/media-prioridad/), [futuro/](tareas/futuro/) y el [checklist del producto mínimo viable](tareas/minimo-producto-viable/checklist-construccion-mvp.md) | |
| Versión de la aplicación y releases | `src/Directory.Build.props` (única fuente; no se edita a mano), [CHANGELOG.md](../CHANGELOG.md) y `release-please-config.json` | La regla está en [CLAUDE.md](../CLAUDE.md). |
| Integración continua y despliegue | `.github/workflows/`: `ci-cd.yml` (pruebas y despliegue), `codeql.yml` (análisis de seguridad del código), `release-please.yml` (releases) y `sql-dev-nivel.yml` (nivel de la base de desarrollo) | La base de Azure no se crea sola: [pipeline-no-provisiona-bd-azure.md](tareas/alta-prioridad/pipeline-no-provisiona-bd-azure.md). Producción: ADR 0014 (mira su estado). |

### Documentación de usuario

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| Qué documentos hay que mantener al día | [.claude/documentacion-usuario.json](../.claude/documentacion-usuario.json) | Es la única lista; al crear documentación de usuario nueva, se añade ahí. |
| Dónde están | Manual (`/Home/Manual`): `src/ResidApp.Web/Views/Home/Manual.cshtml` · Guía por perfil (`/Guia`): `src/ResidApp.Web/Guia/*.json` · `src/ResidApp.Web/wwwroot/flujo-de-trabajo.html` · `src/ResidApp.Web/wwwroot/acceso-y-sesion.html` · guías de pruebas de CJ: `docs/pendientes-cj/pruebas-*.html` | |
| Cómo se hizo la Guía por perfil | [guia-por-perfil-plan.md](producto/guia-por-perfil-plan.md) y [guia-por-perfil-recorrido.md](producto/guia-por-perfil-recorrido.md) | |

### Cómo se trabaja

| Tema | Fuente de verdad | Ojo |
| --- | --- | --- |
| Reglas de colaboración, para cualquier persona o herramienta | [CLAUDE.md](../CLAUDE.md) | Vive en el repositorio y no en la configuración privada de una herramienta, para que se comparta. |
| Agentes que asesoran o revisan | [.claude/agents/](../.claude/agents/): protección de datos, documentación de usuario, calidad de pruebas y dependencias | Informan o redactan borradores; no deciden. |
| Probar un cambio en un navegador real | [.claude/skills/probar-en-navegador/](../.claude/skills/probar-en-navegador/) | |
| El prototipo anterior | La etiqueta de git `legado-cloudflare-final` | Ver «Palabras que se usan aquí». |

## Quién decide y qué hacer si dos fuentes se contradicen

**Quién decide.** Lo clínico y las decisiones de producto, CJ: sus preguntas y sus respuestas siguen el circuito de [pendientes-cj/README.md](pendientes-cj/README.md). Lo técnico, la seguridad y la versión, Orlando: lo anota la línea «Estado» de cada ADR o documento de diseño, y [CLAUDE.md](../CLAUDE.md) exige su visto bueno para cambiar la versión.

**Si dos fuentes se contradicen**, valen estas reglas, que ya estaban escritas (cada una con su origen):

1. **Bocetos de pantalla frente a producto o historias de usuario:** mandan estos dos; los bocetos son una propuesta de interfaz. (Origen: [bocetos-pantallas/README.md](bocetos-pantallas/README.md).)
2. **El código contradice un flujo clínico:** se para y se avisa; no se resuelve por criterio propio. (Origen: «Reglas de trabajo propias» de [continuar-construccion.md](prompts-ia/continuar-construccion.md).)
3. **Falta un valor clínico:** no se inventa; se pide a CJ con un documento en `docs/pendientes-cj/`. (Origen: ídem.)
4. **Un borrador de privacidad:** no se usa con datos reales hasta que lo valida una persona. (Origen: [CLAUDE.md](../CLAUDE.md).)
5. **El prototipo anterior:** no gobierna nada nuevo; ante una contradicción manda `docs/producto/`. (Origen: [README.md](../README.md).)
6. **Una cita a `docs/pendientes-cj/archivados/…`:** es histórica; ese fichero ya no existe. (Origen: [pendientes-cj/README.md](pendientes-cj/README.md).)
7. **Una entrada de [historial/](prompts-ia/historial/):** refleja lo que se creía en su fecha; manda lo vigente. (Origen: la cabecera de cada fichero del historial.)

## Códigos con prefijo (`ADM-21`, `ENF-23`…): hay tres numeraciones

El mismo código puede significar cosas distintas según dónde aparezca:

| Dónde aparece | Qué es | Ejemplo |
| --- | --- | --- |
| Historias de usuario y flujos clínicos («códigos de origen», «códigos PRD cubiertos») | Un requisito del PRD del prototipo anterior; su texto está ahora en la etiqueta `legado-cloudflare-final` | `BAS-04`: la cognición del estado basal |
| [wireframes-funcionales/](bocetos-pantallas/wireframes-funcionales/) | Una pantalla de un boceto | `ADM-21`: «Configuración del servicio de citas» |
| `src/ResidApp.Web/Guia/*.json` | Una ficha de la Guía por perfil («¿Cómo…?») | `ADM-21`: «¿Cómo corrijo el nombre, la fecha de nacimiento o el sexo documentado de un residente?» |

Al citar un código, di de cuál hablas: «la ficha ADM-21 de la Guía» o «la pantalla ADM-21 del boceto».

## Palabras que se usan aquí

- **Fuente de verdad:** el documento (o el código) que decide cuando dos cosas se contradicen. Una **propuesta** o un **borrador** todavía no decide.
- **Vinculante:** hay que cumplirlo; si no se puede, se pregunta antes de saltárselo.
- **ADR** (registro de decisión de arquitectura): documento corto que explica qué se decidió sobre cómo se construye algo y por qué. Su estado (propuesta, aceptada…) está en la línea «Estado» de su cabecera.
- **Script SQL numerado:** cada cambio de la base de datos es un fichero `NNNN_nombre.sql`; una vez aplicado en Azure no se edita, se añade otro.
- **Etiqueta de git:** una marca fija en el historial. `legado-cloudflare-final` guarda el prototipo anterior, que ya no está en las carpetas; se lee con `git show legado-cloudflare-final:<ruta>`.
- **`dpo-residapp`:** el agente que asesora en protección de datos. Lo que redacta es borrador hasta que una persona lo valida.
- **Product Owner:** la persona que decide qué debe hacer el producto; aquí, CJ.
