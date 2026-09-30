# Roadmap

## Enfoque de esta hoja de ruta

La hoja de ruta original del prototipo legado (consolidación documental → línea base funcional → decisión de persistencia D1/Drizzle → dominio y autorización → bloques verticales → integración → preparación de piloto → piloto real) asumía la plataforma Cloudflare/D1 y quedó superada por el cambio de arquitectura hacia el monolito ASP.NET Core / SQL Server. De aquella hoja de ruta se conserva únicamente el orden funcional de migración de los bloques verticales, que sigue siendo válido.

## Bloques verticales y su estado

| Bloque vertical | Estado |
| --- | --- |
| Residente / Basal | Completado — ver detalle más abajo |
| Auxiliar | Completado (historias 1-6), pendiente de validación por CJ |
| Enfermería | En curso — ver `docs/tareas/alta-prioridad/pendientes-enfermeria.md` |
| Medicina | En curso — ver `docs/tareas/alta-prioridad/pendientes-medicina.md` |
| Familia / Portal Familiar | No iniciado |
| Administración | No iniciado |
| Dirección / Coordinación Clínica | En curso — ver `docs/tareas/alta-prioridad/pendientes-direccion.md` |

## Estado detallado del vertical Residente / Basal

Estado a fecha 2026-09-28.

**Completado (hasta 2026-09-12):**

- Andamiaje de la solución (`ResidApp.sln` con los proyectos Domain, Application, Infrastructure, Shared y Web).
- Identificadores fuertemente tipados y enums compartidos.
- Dominio asistencial (entidades Resident/Baseline con sus validaciones).
- Capa de aplicación e infraestructura de persistencia (Dapper + SQL Server) para este vertical.
- Script de base de datos ejecutado y verificado contra una instancia real de SQL Server (22 tablas, 27 triggers, 51 checks, 224 índices).
- `ResidApp.Web` cableado para el alta de residente: inyección de dependencias, cadena de conexión, identidad de sesión de desarrollo (no autenticación real) y pantalla funcionando de extremo a extremo.
- 47 tests reales en verde (unitarios, de integración contra SQL Server real y funcionales a través de la Web), sustituyendo a los placeholders.
- 3 bugs de producción encontrados al ejecutar por primera vez contra un motor real, corregidos (detalle en `docs/tareas/alta-prioridad/pendientes-migracion-inicial.md`, punto 6).
- `dotnet build` compila sin errores ni avisos.

**Completado después (2026-09-14 a 2026-09-28):**

- Autoría del borrador de basal (crear, 9 áreas, Barthel, cancelar, confirmar y firmar) construida desde el vertical Enfermería; la firma y la lectura de Dirección Clínica se demuestran de extremo a extremo con test (`SqlBaselineRepositoryDraftTests.FullCycle_*`).
- Los 27 triggers de `0001` verificados uno por uno contra SQL Server real (`DatabaseTriggerTests`). Uno de ellos (`TR_rcb_update_guard`) resulta inalcanzable porque una FK rechaza antes el mismo cambio; queda documentado.
- Selección de ámbito activo para cuentas con varios perfiles/centros.

Sin pendiente crítico propio. Detalle en `docs/tareas/alta-prioridad/pendientes-migracion-inicial.md`.

## Decisiones abiertas heredadas del legado

| Decisión | Estado |
| --- | --- |
| Autenticación y segundo factor productivos | En el prototipo legado, la selección de proveedor de autenticación seguía en estado "Propuesto" (nunca aceptada), con Auth0 como recomendación principal y WorkOS AuthKit como respaldo condicionado. En la nueva arquitectura .NET esta decisión sigue completamente abierta: no hay proveedor equivalente decidido todavía. |
| SLA, RPO, RTO y copias de seguridad | Pendiente de definir antes de producción. |
| EIPD, contratos y seguridad | Pendiente de formalizar antes de tratar datos reales. |
| Configuración real de citas, equipos y tiempos | Pendiente de definir junto con cada centro piloto. |
| Criterios de actualización familiar relevante | Pendiente de acordar entre dirección clínica y centro. |
| Política de corrección de notas clínicas y su conservación | Pendiente de acordar entre centro y responsable de protección de datos. Mientras tanto, la app aplica una política provisional (2026-09-30): el autor corrige solo las valoraciones de Enfermería y médica durante 6 h y después añade rectificaciones; todo se conserva (ver `docs/tareas/alta-prioridad/pendientes-enfermeria.md`, historia 11, bloque 3). |

## Próximos pasos

Residente/Basal y Auxiliar están construidos. Enfermería y Medicina tienen construidas todas sus historias
salvo lo que depende de otros verticales (ver `docs/tareas/alta-prioridad/pendientes-enfermeria.md` y
`pendientes-medicina.md`). Dirección / Coordinación Clínica está en curso: la supervisión operativa en solo
lectura está hecha y la lectura clínica auditada espera las finalidades que fije CJ (ver
`docs/tareas/alta-prioridad/pendientes-direccion.md`). Después vienen Administración y Familia / Portal
Familiar, que depende de ella y de la decisión del proveedor de identidad.

El despliegue en Azure (App Service `app-residapp-dev` + Azure SQL `sqldb-residapp-dev`) y el pipeline
de CI/CD (build + tests contra SQL Server real, y deploy que aplica el esquema y los datos ficticios de
desarrollo) funcionan desde 2026-09-28. Las decisiones de arquitectura técnica para cada paso se
documentan en `docs/decisiones-arquitectura/instrucciones-migracion-net10.md`, no en este roadmap.
