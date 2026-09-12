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

Estado a fecha 2026-09-11.

**Completado:**

- Andamiaje de la solución (`ResidApp.sln` con los proyectos Domain, Application, Infrastructure, Shared y Web).
- Identificadores fuertemente tipados y enums compartidos.
- Dominio asistencial (entidades Resident/Baseline con sus validaciones).
- Capa de aplicación e infraestructura de persistencia (Dapper + SQL Server) para este vertical.
- `dotnet build` compila sin errores.

**Pendiente crítico:**

- El script de base de datos (22 tablas, triggers y checks) nunca se ha ejecutado contra una instancia real de SQL Server: los triggers de inmutabilidad y las validaciones de contenido JSON no están verificados.
- `ResidApp.Web` sigue siendo la plantilla en blanco: sin inyección de dependencias registrada, sin cadena de conexión, sin controladores ni vistas del dominio, y sin autenticación real.
- No existen tests propios del vertical (reglas de validación, motor de autorización por perfil, casos de uso, repositorios contra una instancia real).

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

Cerrar los pendientes críticos del vertical Residente/Basal (validar el script de base de datos contra una instancia real de SQL Server, exponer pantallas mínimas en `ResidApp.Web`, añadir tests propios) antes de iniciar la migración del siguiente bloque vertical, Auxiliar, siguiendo el orden indicado arriba. Las decisiones de arquitectura técnica para cada paso se documentan en `docs/decisiones-arquitectura/instrucciones-migracion-net10.md`, no en este roadmap.
