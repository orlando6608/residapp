# Roadmap

## Enfoque de esta hoja de ruta

La hoja de ruta original del prototipo legado (consolidación documental → línea base funcional → decisión de persistencia D1/Drizzle → dominio y autorización → bloques verticales → integración → preparación de piloto → piloto real) asumía la plataforma Cloudflare/D1 y quedó superada por el cambio de arquitectura hacia el monolito ASP.NET Core / SQL Server. De aquella hoja de ruta se conserva únicamente el orden funcional de migración de los bloques verticales, que sigue siendo válido.

## Bloques verticales y su estado

| Bloque vertical | Estado |
| --- | --- |
| Residente / Basal | En curso — ver detalle más abajo |
| Auxiliar | No iniciado |
| Enfermería | No iniciado |
| Medicina | No iniciado |
| Familia / Portal Familiar | No iniciado |
| Administración | No iniciado |
| Dirección / Coordinación Clínica | No iniciado |

## Estado detallado del vertical Residente / Basal

Estado a fecha 2026-09-12.

**Completado:**

- Andamiaje de la solución (`ResidApp.sln` con los proyectos Domain, Application, Infrastructure, Shared y Web).
- Identificadores fuertemente tipados y enums compartidos.
- Dominio asistencial (entidades Resident/Baseline con sus validaciones).
- Capa de aplicación e infraestructura de persistencia (Dapper + SQL Server) para este vertical.
- Script de base de datos ejecutado y verificado contra una instancia real de SQL Server (22 tablas, 27 triggers, 51 checks, 224 índices).
- `ResidApp.Web` cableado para el alta de residente: inyección de dependencias, cadena de conexión, identidad de sesión de desarrollo (no autenticación real) y pantalla funcionando de extremo a extremo.
- 47 tests reales en verde (unitarios, de integración contra SQL Server real y funcionales a través de la Web), sustituyendo a los placeholders.
- 3 bugs de producción encontrados al ejecutar por primera vez contra un motor real, corregidos (detalle en `docs/tareas/alta-prioridad/pendientes-migracion-inicial.md`, punto 6).
- `dotnet build` compila sin errores ni avisos.

**Pendiente crítico:**

- No existe, ni en este puerto ni en el prototipo legado, un caso de uso para crear el contenido de un borrador de basal (las 9 áreas + Barthel); sin él, `BaselineController/Sign` y `/Direction` están cableados pero no se pueden demostrar end-to-end. Construir esa capacidad pertenece al vertical Enfermería/Medicina (`gestion-basal-barthel.md`), no a este.
- Verificación uno por uno del resto de los 27 triggers (inmutabilidad, transición de estados) sigue pendiente — bloqueada por el punto anterior.
- Detalle completo en `docs/tareas/alta-prioridad/pendientes-migracion-inicial.md`.

## Decisiones abiertas heredadas del legado

| Decisión | Estado |
| --- | --- |
| Autenticación y segundo factor productivos | En el prototipo legado, la selección de proveedor de autenticación seguía en estado "Propuesto" (nunca aceptada), con Auth0 como recomendación principal y WorkOS AuthKit como respaldo condicionado. En la nueva arquitectura .NET esta decisión sigue completamente abierta: no hay proveedor equivalente decidido todavía. |
| SLA, RPO, RTO y copias de seguridad | Pendiente de definir antes de producción. |
| EIPD, contratos y seguridad | Pendiente de formalizar antes de tratar datos reales. |
| Configuración real de citas, equipos y tiempos | Pendiente de definir junto con cada centro piloto. |
| Criterios de actualización familiar relevante | Pendiente de acordar entre dirección clínica y centro. |
| Política de corrección de notas clínicas y su conservación | Pendiente de acordar entre centro y responsable de protección de datos. |

## Próximos pasos

El vertical Residente/Basal ya está verificado contra un motor real y con pantallas y tests propios; el
único pendiente crítico que le queda (autoría de borrador de basal) pertenece al vertical
Enfermería/Medicina, no bloquea empezar Auxiliar. Antes de iniciar cualquier vertical nuevo sigue
faltando: preparar el despliegue en Azure (App Service + Azure SQL) y un pipeline de CI/CD mínimo, hoy
inexistentes. Las decisiones de arquitectura técnica para cada paso se documentan en
`docs/decisiones-arquitectura/instrucciones-migracion-net10.md`, no en este roadmap.
