# ResidApp — plataforma asistencial y de comunicación con familias

Plataforma web para residencias geriátricas que estructura el registro cotidiano de los residentes, la detección y seguimiento de cambios, la continuidad entre turnos y la comunicación de información aprobada a familiares autorizados.

> Nombre descriptivo provisional. La marca comercial definitiva aún no está decidida.

## Estado del proyecto

- **Fecha de referencia:** 12 de septiembre de 2026.
- **Arquitectura:** monolito **ASP.NET Core MVC (.NET 10) / SQL Server**, en migración activa desde un prototipo previo sobre Cloudflare Workers/D1, conservado íntegro en [`docs/legado-cloudflare/`](docs/legado-cloudflare/) como evidencia histórica.
- **Estado funcional:** solo el vertical **Residente/Basal** está en construcción; el resto de verticales (Auxiliar, Enfermería, Medicina, Familia/Portal Familiar, Administración, Dirección/Coordinación Clínica) no se ha iniciado. El alta de residente funciona de extremo a extremo (verificada contra SQL Server real); la firma de basal está cableada pero no es demostrable todavía (ver [Limitaciones actuales conocidas](#ejecución-local)). Ver el detalle en [Hoja de ruta](#hoja-de-ruta).
- **Ámbito inicial:** residencias geriátricas.
- **Datos permitidos en esta fase:** exclusivamente ficticios.

Este proyecto todavía **no está autorizado para tratar datos reales de salud** ni debe presentarse como una solución sanitaria lista para producción. Antes de un piloto real serán necesarias, como mínimo, una evaluación de impacto en protección de datos, validación jurídica, revisión de seguridad, definición de la arquitectura productiva y acuerdos con el centro piloto (ver [Decisiones pendientes](#decisiones-pendientes)).

## Propósito

La plataforma busca resolver cinco problemas principales:

- Registrar de forma breve, estructurada y trazable el estado cotidiano de cada residente.
- Detectar cambios respecto a su situación basal y mantenerlos visibles hasta su resolución.
- Evitar pérdidas de información en relevos, seguimientos y escalados asistenciales.
- Separar el contenido clínico interno de la información comprensible que puede comunicarse a la familia.
- Permitir que cada centro configure su estructura, usuarios, autorizaciones, publicaciones y citas sin ampliar indebidamente los permisos.

La plataforma **no** diagnostica, prescribe, solicita pruebas, recomienda tratamientos, decide prioridades clínicas ni deriva automáticamente. Tampoco sustituye canales urgentes, llamadas, emergencias, la historia clínica oficial ni los protocolos asistenciales del centro.

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
| Auxiliar | Cierre cotidiano, cambios observados y aviso directo cuando proceda | No diagnostica, valora clínicamente, modifica basal, escala a Medicina ni aprueba publicaciones |
| Enfermería | Basal según permiso, bandejas, valoración, seguimiento, escalado, derivación, indicaciones, cierre y comunicación | No altera observaciones ajenas ni gestiona autorizaciones familiares |
| Medicina | Basal según permiso, escalados, eventos propios, valoración, conducta, indicaciones, seguimiento, derivación, cierre y comunicación | No automatiza decisiones ni atribuye actos a otros |
| Familiar | Consulta publicaciones y gestiona citas habilitadas para residentes autorizados | No accede a contenido clínico interno ni elige profesional concreto |
| Administración | Estructura subordinada, identidades, ubicaciones, roles, asignaciones, turnos, autorizaciones, citas y auditoría administrativa | No provisiona centros, participa en actos clínicos ni accede por defecto al historial clínico |
| Dirección Clínica | Supervisión de proceso, indicadores e informes agregados | No interviene clínicamente desde este perfil; el detalle clínico solo se consulta en lectura, con permiso y auditoría |

Una cuenta puede tener uno o varios perfiles autorizados, pero debe seleccionar o cambiar explícitamente el perfil activo. Toda actuación conserva cuenta, perfil activo, ámbito y fecha/hora. Los permisos de perfiles distintos no se combinan.

## Alcance funcional

| Módulo | Funciones incluidas |
| --- | --- |
| Acceso | Cuentas individuales, sesión, recuperación, bloqueo y segundo factor según política |
| Organización | Provisionamiento de centros; edificios y plantas opcionales; unidades, habitaciones y plazas/camas configurables |
| Identidades | Usuarios multirol, cargos separados, asignaciones y estados de cuenta |
| Residentes | Identidad, historial de ubicación, basal vigente/versionado, Barthel común y cognición documentada |
| Registro cotidiano | Sin cambios, no valorable y cambio observado |
| Eventos | Ordinarios/prioritarios, bandejas, valoración, seguimiento, continuidad y cierre |
| Medicina | Escalados, conducta, indicaciones, seguimiento médico y continuidad |
| Derivación | Módulo común, vista previa, firma y PDF vinculado al evento |
| Publicaciones | Texto separado, aprobación humana, programación, corrección y retirada excepcional |
| Portal Familiar | Publicaciones publicadas, autorizaciones activas y citas según modalidad del centro |
| Administración | Familias, autorizaciones, contacto urgente, publicaciones, turnos, citas y auditoría |
| Dirección Clínica | Supervisión operativa, detalle clínico condicionado, indicadores e informes agregados |

## Fuera de alcance

- Sustituir la historia clínica oficial o el software integral del centro.
- Diagnóstico, prescripción, solicitud de pruebas, triaje o recomendaciones automáticas.
- Mensajería libre entre familia y profesionales.
- Contenido sanitario por email, SMS, WhatsApp o enlaces públicos.
- Notificaciones push en el piloto.
- App móvil nativa; la primera versión es web adaptable/PWA.
- Modo clínico offline con sincronización posterior.
- Administración de medicación, facturación, nóminas, fichaje o gestión laboral avanzada.
- Videollamada propia; solo puede ofrecerse como modalidad si el centro dispone del medio externo adecuado.
- Rankings nominativos de productividad o evaluaciones automáticas de mala praxis.
- Exportación masiva de historias clínicas individuales.
- Integraciones con historias clínicas externas en la primera versión.
- Flujos específicos de Centro de Día y SAAD antes de validar el modelo inicial en residencias.
- CFS y Pfeiffer dentro del estado basal.

## Principios funcionales invariantes

Estos principios gobiernan cualquier implementación de la plataforma, con independencia de la tecnología que la soporte.

**Autoría y trazabilidad**

- **Basal como referencia:** los cambios se comparan con la situación habitual del residente.
- **Autoría fiel:** quien observa registra; quien valora, indica, ejecuta, corrige o cierra firma su propia actuación.
- **Firma no transferible:** nadie firma un borrador basal creado por otra persona.
- **Inmutabilidad trazable:** un registro firmado no se elimina ni se sobrescribe silenciosamente.
- **Versionado temporal:** el basal y la ubicación conservan el estado aplicable en cada momento.
- **Separación de objetos:** observación, valoración, conducta, ejecución, publicación familiar y cita son objetos distintos.

**Seguridad y acceso**

- **Responsabilidad de equipo:** un evento abierto no pertenece permanentemente a la cuenta que inició la valoración.
- **Mínimo privilegio:** el acceso está limitado por centro, unidad, residente, asignación, perfil activo, autorización, permiso específico y finalidad.
- **Autorización en servidor:** ocultar botones no constituye seguridad.
- **Datos confiables:** identidad, perfil, ámbito, autoría y fecha/hora se obtienen de la sesión y del contexto server-side, nunca de campos libres del cliente.
- **Denegación por defecto:** ninguna capacidad se concede por ausencia de una prohibición explícita.

**Continuidad y privacidad**

- **Vencimiento no equivale a cierre:** un objeto fuera de plazo sigue abierto y visible hasta que se produce una actuación profesional.
- **Automatización no clínica:** el sistema puede programar una publicación aprobada o reservar un hueco; no redacta, interpreta ni decide clínicamente.
- **Urgencias fuera del portal:** ninguna pantalla, cita o publicación sustituye el protocolo urgente ni el contacto directo.
- **Privacidad por diseño y por defecto:** se minimizan datos, exposición, retención y permisos desde el propio modelo.

## Objetivos y métricas del piloto

Las siguientes metas son hipótesis de validación del piloto, no estándares clínicos universales ni umbrales automáticos de calidad asistencial.

| Objetivo | Indicador | Meta inicial |
| --- | --- | --- |
| Registro viable | Tiempo mediano para cerrar un día sin cambios | <= 60 s por residente |
| Continuidad | Cambios ordinarios revisados antes del cierre diario | >= 95 % |
| Seguridad operativa | Eventos prioritarios de Auxiliar con aviso directo documentado | 100 % |
| Trazabilidad | Actuaciones firmadas con autor, perfil activo y marca temporal | 100 % |
| Relevo | Eventos abiertos visibles tras cambio de turno | 100 % |
| Comunicación | Publicaciones con aprobación profesional previa | 100 % |
| Acceso | Operaciones clínicas y familiares validadas en servidor | 100 % |
| Basal | Basales vigentes con versión, autoría, firma y catálogo identificables | 100 % |
| Usabilidad | Tareas críticas completadas sin ayuda | >= 90 % |

## Arquitectura y stack técnico actual

- **ASP.NET Core MVC** sobre **.NET 10**, en **C# 14**, como monolito (sin backend independiente).
- Persistencia en **SQL Server** con acceso mediante **Dapper** (sin ORM), dentro de transacciones ACID explícitas.
- Identificadores fuertemente tipados (`readonly record struct` envolviendo `Guid`) y un motor de autorización propio deny-by-default (`ResidentBaselinePolicy`), evaluado desde la capa de aplicación — no delega en el pipeline de autorización de ASP.NET Core.

Estructura del repositorio:

```text
src/
  ResidApp.sln              Solución .NET
  ResidApp.Domain/          Entidades y reglas del dominio asistencial
  ResidApp.Application/     Casos de uso y servicios de aplicación
  ResidApp.Infrastructure/  Persistencia (Dapper/SQL Server) y seguridad
  ResidApp.Shared/          Identificadores y tipos compartidos
  ResidApp.Web/             Proyecto ASP.NET Core MVC (interfaz)
tests/
  UnitTests/                Pruebas unitarias (xUnit)
  IntegrationTests/         Pruebas de integración (xUnit)
  FunctionalTests/          Pruebas funcionales (xUnit)
database/
  scripts/                  Scripts DDL de SQL Server
  seed/                     Datos de desarrollo exclusivamente ficticios
docs/
  producto/                 Documentación funcional viva (propósito, alcance, objetivos, roadmap)
  decisiones-arquitectura/  Decisiones técnicas de la migración a .NET
  tareas/                   Backlog priorizado
  legado-cloudflare/        Prototipo histórico (Next.js/Cloudflare), solo como referencia
```

El detalle de las decisiones de diseño de esta migración (mapeo de identificadores, reglas de dominio, persistencia, autorización) está en [`docs/decisiones-arquitectura/instrucciones-migracion-net10.md`](docs/decisiones-arquitectura/instrucciones-migracion-net10.md).

## Ejecución local

El SDK de .NET está fijado en [`global.json`](global.json) (`10.0.204`, `rollForward: latestFeature`).

```bash
dotnet build src/ResidApp.sln
dotnet test src/ResidApp.sln
```

Si `dotnet test` falla por resolución de workloads (por ejemplo, un manifiesto de `microsoft.net.sdk.macos` ausente), es una caché de workloads desincronizada a nivel de máquina, no un problema del proyecto; el workaround verificado es:

```bash
dotnet test src/ResidApp.sln -p:MSBuildEnableWorkloadResolver=false
```

`database/scripts/0001_init_sqlserver.sql` ya se ha ejecutado y verificado contra una instancia real de SQL Server (22 tablas, 27 triggers, 51 checks, 224 índices); ver `dev_seed_residente_basal.sql` en [`database/seed/`](database/seed/) para poblarla con datos ficticios mínimos.

**Limitaciones actuales conocidas** (no ocultarlas ni darlas por resueltas):

- No existe, ni en este puerto ni en el prototipo legado, un caso de uso para crear el contenido de un borrador de basal (las 9 áreas + Barthel): `BaselineController/Sign` y `/Direction` están cableados contra la aplicación pero no se pueden demostrar de extremo a extremo hasta que exista esa capacidad (pertenece al vertical Enfermería/Medicina).
- La identidad de sesión de `ResidApp.Web` (`DevAuthController`) es un selector de cuenta ficticia por cookie, no autenticación real; la decisión de proveedor productivo sigue abierta (ver [Decisiones pendientes](#decisiones-pendientes)).
- Verificación uno por uno del resto de los 27 triggers (más allá de lo que ya cubren los tests de integración) sigue pendiente — bloqueada por el primer punto.

Detalle completo, incluidos los bugs de producción encontrados y corregidos al ejecutar por primera vez contra un motor real, en [`docs/tareas/alta-prioridad/pendientes-migracion-inicial.md`](docs/tareas/alta-prioridad/pendientes-migracion-inicial.md).

No deben añadirse secretos, credenciales ni datos personales reales al repositorio, los fixtures, las pruebas, los logs, las capturas o las demostraciones.

## Hoja de ruta

El orden funcional de migración de los bloques verticales se hereda del prototipo legado; el resto de esa hoja de ruta original (ligada a Cloudflare/D1) ha quedado superado.

| Bloque vertical | Estado |
| --- | --- |
| Residente / Basal | En curso |
| Auxiliar | No iniciado |
| Enfermería | No iniciado |
| Medicina | No iniciado |
| Familia / Portal Familiar | No iniciado |
| Administración | No iniciado |
| Dirección / Coordinación Clínica | No iniciado |

Detalle del vertical Residente/Basal — completado: andamiaje de la solución, identificadores y enums compartidos, dominio asistencial (Resident/Baseline con validaciones), capa de aplicación e infraestructura Dapper/SQL Server verificada contra un motor real, alta de residente funcionando de extremo a extremo en `ResidApp.Web`, 47 tests reales en verde. Pendiente crítico: el resto se detalla en [Ejecución local](#ejecución-local) y en [`docs/producto/roadmap.md`](docs/producto/roadmap.md).

## Decisiones pendientes

| Decisión | Estado |
| --- | --- |
| Autenticación y segundo factor productivos | En el prototipo legado la selección de proveedor seguía en estado "Propuesto" (nunca aceptada). En la arquitectura .NET esta decisión sigue completamente abierta: no hay proveedor equivalente decidido. |
| SLA, RPO, RTO y copias de seguridad | Pendiente de definir antes de producción. |
| EIPD, contratos y seguridad | Pendiente de formalizar antes de tratar datos reales. |
| Configuración real de citas, equipos y tiempos | Pendiente de definir junto con cada centro piloto. |
| Criterios de actualización familiar relevante | Pendiente de acordar entre dirección clínica y centro. |
| Política de corrección de notas clínicas y su conservación | Pendiente de acordar entre centro y responsable de protección de datos. |

Estas decisiones no bloquean la construcción del prototipo con datos ficticios, pero sí condicionan cualquier piloto real o despliegue productivo.

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

## Documentación y fuentes de verdad

La documentación funcional viva está en [`docs/producto/`](docs/producto/):

- [`vision-general.md`](docs/producto/vision-general.md) — qué es ResidApp y el problema que resuelve.
- [`objetivos.md`](docs/producto/objetivos.md) — métricas del piloto y principios invariantes.
- [`alcance.md`](docs/producto/alcance.md) — módulos, roles y límites de alcance.
- [`roadmap.md`](docs/producto/roadmap.md) — estado real de la migración por vertical.

Las decisiones técnicas de la migración a .NET están en [`docs/decisiones-arquitectura/`](docs/decisiones-arquitectura/), y el backlog priorizado en [`docs/tareas/`](docs/tareas/).

[`docs/legado-cloudflare/`](docs/legado-cloudflare/) conserva íntegro el prototipo original (Next.js/Cloudflare Workers/D1) como evidencia histórica del PRD, la matriz de permisos y los wireframes que dieron origen al alcance actual. **No gobierna ninguna implementación nueva**: ante cualquier contradicción entre el legado y `docs/producto/`, prevalece `docs/producto/`.

## Criterio de terminado

Una tarea se considera terminada cuando:

- El comportamiento está alineado con la documentación funcional vigente en `docs/producto/`.
- La autorización se valida en servidor.
- Los estados, la autoría y la trazabilidad quedan preservados.
- No se han añadido datos reales, secretos ni decisiones clínicas automáticas.
- Las pruebas relevantes se han ejecutado y sus resultados se han informado.
- Se enumeran los archivos modificados y se declaran las limitaciones o contradicciones restantes.
