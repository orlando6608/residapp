# Plataforma asistencial y de comunicación con familias

Plataforma web para residencias geriátricas que estructura el registro cotidiano de los residentes, la detección y seguimiento de cambios, la continuidad entre turnos y la comunicación de información aprobada a familiares autorizados.

> Nombre descriptivo provisional. La marca comercial definitiva aún no está decidida.

## Estado del proyecto

- **Fecha de referencia:** 9 de septiembre de 2026
- **Estado funcional:** base técnica ejecutable; perfiles y bloques funcionales todavía no implementados.
- **Ámbito inicial:** residencias geriátricas.
- **Datos permitidos en esta fase:** exclusivamente ficticios.

La definición funcional vigente está congelada por la línea base incremental `LBF-CONNECT-2026-09-06-V1.1`: PRD v0.5, matriz de permisos v0.2.1, seis wireframes canónicos y matriz de trazabilidad v0.2. `AGENTS.md` v1.4 gobierna el trabajo. La línea base v1, su commit y su tag permanecen intactos como evidencia histórica. D1 y Drizzle están configurados y la migración `0001` está integrada en `main`: contiene 29 tablas, 56 índices y 56 triggers. Su revisión SQL y las pruebas locales sintéticas han concluido correctamente; `0001` permanece limitada a D1 local desechable, es inmutable y cualquier evolución física debe comenzar en `0002` o una migración posterior.

Este proyecto todavía **no está autorizado para tratar datos reales de salud** ni debe presentarse como una solución sanitaria lista para producción. Antes de un piloto real serán necesarias, como mínimo, una evaluación de impacto en protección de datos, validación jurídica, revisión de seguridad, definición de la arquitectura productiva y acuerdos con el centro piloto.

## Propósito

La plataforma busca resolver cinco problemas principales:

- Registrar de forma breve, estructurada y trazable el estado cotidiano de cada residente.
- Detectar cambios respecto a su situación basal y mantenerlos visibles hasta su resolución.
- Evitar pérdidas de información en relevos, seguimientos y escalados asistenciales.
- Separar el contenido clínico interno de la información comprensible que puede comunicarse a la familia.
- Permitir que cada centro configure su estructura, usuarios, autorizaciones, publicaciones y citas sin ampliar indebidamente los permisos.

## Flujo funcional principal

```text
Alta administrativa
  -> estado basal profesional
  -> registro cotidiano o evento observado
  -> revisión de Enfermería
  -> seguimiento o escalado
  -> valoración médica e indicaciones, si procede
  -> derivación, si procede
  -> cierre profesional
  -> redacción y aprobación del texto familiar
  -> publicación
  -> consulta por el familiar autorizado
```

La configuración organizativa, las autorizaciones familiares, la agenda de citas, la auditoría y la supervisión clínica acompañan al flujo principal, pero no sustituyen ninguna actuación asistencial.

## Perfiles

| Perfil | Responsabilidad principal | Límite esencial |
|---|---|---|
| Auxiliar | Cierre cotidiano, registro de cambios observados y aviso directo cuando proceda | No diagnostica, valora clínicamente, modifica el basal, escala a Medicina ni aprueba publicaciones |
| Enfermería | Basal según permiso, bandejas, valoración, seguimiento, continuidad, escalado, derivación, cierre y comunicación | No altera observaciones ajenas ni gestiona autorizaciones familiares |
| Medicina | Escalados, eventos propios, valoración, conducta, indicaciones, seguimiento, derivación, cierre y comunicación | No automatiza decisiones clínicas ni atribuye actuaciones a otros profesionales |
| Familiar | Consulta de publicaciones aprobadas y gestión de citas habilitadas | No accede al contenido clínico interno ni selecciona a un profesional concreto |
| Administración | Estructura, usuarios, roles, asignaciones, turnos, autorizaciones, publicaciones, citas y auditoría administrativa | No realiza actos clínicos ni accede por defecto al historial clínico |
| Dirección/Coordinación Clínica | Supervisión del proceso, indicadores e informes agregados | No interviene clínicamente desde este perfil; el detalle clínico exige permiso específico, es de solo lectura y queda auditado |

Una cuenta puede tener varios roles, pero sus capacidades no se suman. El usuario debe seleccionar explícitamente el perfil activo y cada operación se autoriza de nuevo con ese perfil.

## Alcance del prototipo

- Cuentas individuales, sesiones y modelo multirol.
- Organización por centro, edificio o planta opcionales, unidad, habitación y plaza o cama.
- Usuarios, cargos, asignaciones, turnos y ámbitos autorizados.
- Ficha del residente y estado basal versionado.
- Índice de Barthel mediante sus diez ítems.
- Registro cotidiano: `Sin cambios`, `No valorable` o `Registrar cambio`.
- Eventos ordinarios y prioritarios, bandejas compartidas, seguimientos y continuidad entre turnos.
- Valoración de Enfermería, escalado a Medicina e indicaciones médicas con lectura y ejecución separadas.
- Módulo común de derivación para Enfermería y Medicina.
- Publicaciones familiares separadas del contenido clínico, con aprobación humana previa.
- Portal Familiar privado para publicaciones y citas.
- Citas configurables por centro en una única modalidad activa: reserva directa o solicitud previa.
- Administración organizativa y auditoría administrativa.
- Supervisión operativa e indicadores agregados para Dirección/Coordinación Clínica.

## Fuera de alcance actual

- Sustituir la historia clínica oficial o el software integral del centro.
- Diagnosticar, prescribir, solicitar pruebas, realizar triaje o recomendar tratamientos automáticamente.
- Utilizar la plataforma como canal de emergencias.
- Mensajería libre o chat clínico entre familiares y profesionales.
- Enviar contenido sanitario por correo electrónico, SMS, WhatsApp o enlaces públicos.
- Aplicación móvil nativa, modo clínico sin conexión o notificaciones push en el piloto.
- Gestión de medicación, facturación, nóminas, fichaje o gestión laboral avanzada.
- Videollamada propia.
- Rankings nominativos de trabajadores o evaluaciones automáticas de mala praxis.
- Exportación masiva de historias clínicas o integración inicial con historias clínicas externas.
- Flujos específicos de centros de día y SAAD antes de validar el modelo en residencias.

## Principios funcionales invariantes

- **Basal como referencia:** todo cambio se interpreta respecto a la situación habitual documentada.
- **Autoría fiel:** quien observa, valora, indica, ejecuta, corrige o cierra firma su propia actuación.
- **Objetos separados:** observación, valoración, conducta, ejecución, publicación familiar y cita no son el mismo registro.
- **Responsabilidad de equipo:** un evento abierto no queda ligado permanentemente a quien inició la valoración.
- **Mínimo privilegio:** el acceso depende de centro, unidad, residente, asignación, perfil activo, autorización y finalidad.
- **Autorización en servidor:** ocultar una acción en la interfaz no constituye una medida de seguridad suficiente.
- **Inmutabilidad trazable:** un registro firmado no se elimina ni se sobrescribe silenciosamente.
- **Vencimiento no equivale a cierre:** los pendientes fuera de plazo continúan abiertos y visibles.
- **Automatización no clínica:** el sistema puede ejecutar reglas operativas, pero no redacta, interpreta ni decide clínicamente.
- **Privacidad por diseño y por defecto:** deben minimizarse los datos, su exposición, retención y permisos desde el modelo.

## Base técnica actual

La base instalada y fijada en `pnpm-lock.yaml` utiliza:

- Next.js 16.3.4 con App Router.
- React y React DOM 19.2.8.
- TypeScript 5.9.3 en modo estricto.
- vinext 1.0.0-beta.9, Vite 8.2.2 y Wrangler 4.128.0 para validar el destino Cloudflare Workers.
- CSS propio como sistema visual principal.
- Lógica de servidor en Next.js sobre Cloudflare Workers, sin backend independiente.
- ESLint y `node:test` para validación local.

Cloudflare D1 y Drizzle ORM están configurados para el prototipo ficticio mediante `docs/architecture/0003-aprobacion-d1-drizzle.md`. La migración física `0001_resident_baseline_foundation.sql` está integrada en `main` y se validó únicamente en D1 local desechable con datos sintéticos. No existe aplicación remota autorizada, `0001` no se modifica y cualquier cambio del esquema deberá incorporarse mediante `0002` o una migración posterior. El [ADR 0005](docs/architecture/0005-seleccion-proveedor-autenticacion.md) está integrado en `main` con estado **Propuesto**: Auth0 con núcleo europeo es el candidato provisional y WorkOS AuthKit permanece como alternativa condicionada. No existe proveedor contratado, SDK integrado ni tenant productivo. La autenticación productiva continúa pendiente de un spike técnico, revisión contractual y decisión final. Esta propuesta no equivale a una arquitectura sanitaria productiva ni autoriza datos reales.

## Arquitectura actual

```text
app/                       App Router, layout, estilos y pantalla mínima
components/                Límite de componentes de interfaz
  clinical/                Reserva para componentes asistenciales comunes
lib/
  domain/                  Límite para entidades, estados e invariantes
  authorization/           Contrato server-side con denegación por defecto
db/                        Cliente, esquema Drizzle, repositorios y migración 0001 integrada
worker/                    Runtime Workers y binding D1; sin worker personalizado
tests/                     Pruebas con node:test
docs/                      Fuentes funcionales originales e índice
```

La presentación, el dominio, la autorización y la persistencia permanecen separados. No se han creado rutas de los seis perfiles para evitar pantallas vacías o permisos ficticios.

## Ejecución local reproducible

El entorno canónico comprobado para este repositorio es **Node.js 24.20.0 LTS** y **pnpm 11.19.0**. `.node-version` fija la versión exacta de Node para las herramientas compatibles, mientras que `package.json` exige Node `>=24.20.0 <25` y fija pnpm mediante `packageManager`.

Desde una terminal normal, situada en la raíz del proyecto, ambos ejecutables deben estar disponibles en `PATH`:

```bash
node --version
pnpm --version
```

Los resultados esperados para reproducir exactamente la validación son `v24.20.0` y `11.19.0`. Tras instalar Node por el mecanismo habitual del equipo, preparar pnpm por **una** de estas vías:

```bash
# Si Corepack está disponible con la instalación de Node
corepack enable pnpm
pnpm --version

# Alternativa si esa instalación no proporciona Corepack
npm install --global pnpm@11.19.0
```

Volver a comprobar las versiones antes de instalar el proyecto. El repositorio no usa ni documenta rutas privadas del runtime de Codex.

```bash
pnpm install --frozen-lockfile
pnpm dev
```

La aplicación local se abre normalmente en `http://localhost:5173`. En esta base no se requieren variables de entorno.

Validaciones disponibles:

```bash
pnpm typecheck
pnpm lint
pnpm test
pnpm build
pnpm build:next
pnpm check
```

`pnpm check` ejecuta, en este orden, typecheck, lint, tests, build nativo de Next.js y build Vite/vinext para Cloudflare Workers. Los dos builds son secuenciales porque comparten archivos generados; el destino Workers queda el último. No deben lanzarse ambos builds en paralelo. `0001` ya está integrada e inmutable: su aplicación se mantiene autorizada únicamente en una D1 local desechable con datos sintéticos; ninguna D1 remota está autorizada.

Si `node` o `pnpm` no se reconocen, el problema es la preparación de la terminal y debe corregirse antes de validar; no debe solventarse apuntando a cachés o rutas internas de una herramienta.

No deben añadirse secretos, credenciales ni datos personales reales al repositorio, los fixtures, las pruebas, los logs, las capturas o las demostraciones.

## Fuentes de verdad

La línea base funcional vigente es `LBF-CONNECT-2026-09-06-V1.1`, declarada en `docs/product/baselines/2026-09-06-declaracion-linea-base-funcional-v1.1.md`. `LBF-CONNECT-2026-09-05-V1`, su commit `3850238f…` y el tag `functional-baseline-2026-09-05-v1` se conservan intactos como evidencia histórica.

Ante una contradicción, se aplica esta jerarquía:

1. Decisión explícita posterior, aprobada y registrada mediante el procedimiento de cambio.
2. PRD v0.5 consolidado.
3. Matriz de permisos de seis perfiles v0.2.1.
4. Wireframe Markdown canónico del perfil afectado.
5. `AGENTS.md` v1.4.
6. Implementación y pruebas existentes, únicamente como evidencia del estado técnico.

Documentación funcional canónica:

- `docs/product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md`
- `docs/product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md`
- los seis archivos de `docs/product/wireframes/` enumerados en la declaración v1.1;
- `docs/product/traceability/2026-09-06-matriz-trazabilidad-funcional-v0.2.md`.

Los PDF y DOCX de `docs/` anteriores al 5 de septiembre de 2026 son versiones cerradas e históricas: permanecen en Git, pero no gobiernan nuevas implementaciones. Las contradicciones no se resuelven silenciosamente ni ampliando permisos por inferencia.
## Desarrollo y validación

Antes de modificar código:

1. Leer `AGENTS.md`, el PRD, la matriz de permisos y el wireframe relevante.
2. Inspeccionar el estado real del repositorio y cualquier instrucción adicional aplicable al directorio.
3. Evaluar el impacto sobre interfaz, dominio, autorización, persistencia, autenticación, arquitectura, dependencias y pruebas.
4. Detener y hacer visible únicamente la parte conflictiva si existe una contradicción documental.

Cada cambio debe incluir, según proceda:

- Pruebas unitarias o de dominio.
- Casos positivos y negativos de autorización en servidor.
- Build cuando afecte compilación, rutas, dependencias o integración.
- Validación de renderizado y accesibilidad cuando cambie la interfaz.
- Pruebas de idempotencia y concurrencia en firma, valoración, publicación y reserva de citas.
- Confirmación de que los vencidos siguen abiertos, los relevos conservan la autoría y las correcciones preservan el original.

No debe afirmarse que una modificación funciona si las pruebas pertinentes no se han ejecutado.

## Protección de datos, seguridad y accesibilidad

El prototipo debe aplicar minimización, separación de ámbitos, control de acceso en servidor, trazabilidad y privacidad por defecto. El objetivo de accesibilidad es **WCAG 2.2 nivel AA**.

Antes de introducir datos reales deben resolverse, como mínimo:

- Evaluación de impacto en protección de datos y participación del DPO.
- Base jurídica, responsabilidades, contratos y política de conservación.
- Autenticación productiva, segundo factor, revocación y recuperación segura.
- Modelo de amenazas, pruebas de seguridad y gestión de vulnerabilidades.
- Cifrado, secretos, logs, monitorización y respuesta a incidentes.
- Backups y objetivos de disponibilidad, RPO y RTO.
- Aislamiento entre centros y validación integral de RBAC/ABAC.
- Procedimiento para derechos de las personas y gestión de brechas.

Referencias de orientación:

- [Reglamento General de Protección de Datos](https://eur-lex.europa.eu/ES/legal-content/summary/general-data-protection-regulation-gdpr.html)
- [AEPD: evaluación de impacto](https://www.aepd.es/preguntas-frecuentes/2-tus-obligaciones-como-responsable-del-tratamiento/10-evaluacion-de-impacto)
- [AEPD: Evalúa-Riesgo RGPD](https://www.aepd.es/guias-y-herramientas/herramientas/evalua-riesgo-rgpd)
- [OWASP Application Security Verification Standard](https://owasp.org/www-project-application-security-verification-standard/)
- [Web Content Accessibility Guidelines 2.2](https://www.w3.org/TR/WCAG22/)

## Hoja de ruta

| Fase | Resultado | Datos |
|---|---|---|
| 1. Consolidación | Línea base `LBF-CONNECT-2026-09-06-V1.1`: PRD v0.5, permisos v0.2.1, seis wireframes y trazabilidad v0.2 | Ninguno |
| 2. Repositorio | Inicialización limpia o auditoría requisito a requisito | Ficticios |
| 3. Dominio y autorización | Entidades, estados, RBAC/ABAC y pruebas | Ficticios |
| 4. Bloques verticales | Residente/basal → Auxiliar → Enfermería → Medicina → Familia → Administración → Dirección | Ficticios |
| 5. Integración | Flujos de extremo a extremo, concurrencia, auditoría y accesibilidad | Ficticios |
| 6. Preparación del piloto | EIPD, contratos, seguridad, arquitectura y operación | Sin datos reales |
| 7. Piloto real | Centro, unidad y usuarios limitados | Solo tras superar las puertas de salida |

## Decisiones todavía pendientes

- Vocabulario final de algunos campos tras pruebas de usabilidad.
- Roles exactos autorizados para reevaluar el estado basal en el centro piloto.
- Política de corrección, rectificación y conservación antes de usar datos reales.
- Configuración de citas, equipos y tiempos de cada centro.
- Autenticación productiva y segundo factor.
- Infraestructura productiva, backups, SLA, RPO y RTO.

Estas decisiones no bloquean la construcción del prototipo con datos ficticios, pero sí condicionan cualquier piloto real o despliegue productivo.

## Criterio de terminado

Una tarea se considera terminada cuando:

- El comportamiento está alineado con el PRD, la matriz de permisos y el wireframe aplicable.
- La autorización se valida en servidor.
- Los estados, la autoría y la trazabilidad quedan preservados.
- No se han añadido datos reales, secretos ni decisiones clínicas automáticas.
- Las pruebas relevantes se han ejecutado y sus resultados se han informado.
- Se enumeran los archivos modificados y se declaran las limitaciones o contradicciones restantes.
