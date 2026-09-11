# Pendientes tras el andamiaje inicial y el port del vertical residente/basal

Estado al 2026-09-11, después de completar Paso 0 (andamiaje), Paso 1 (identificadores/enums
compartidos), Paso 2 (dominio asistencial) y Paso 3 (aplicación + infraestructura Dapper/SQL Server)
sobre el vertical residente/basal. `dotnet build ResidApp.sln` compila sin errores ni avisos; los 3
proyectos de test (plantilla) pasan.

## 1. DDL de SQL Server sin ejecutar contra un motor real

`database/scripts/0001_init_sqlserver.sql` (22 tablas, triggers, índices y checks) se escribió
traduciendo con cuidado el original SQLite de `docs/legado-cloudflare/db/migrations/0001_resident_baseline_foundation.sql`,
pero **nunca se ha corrido contra una instancia real de SQL Server** — no había motor disponible en
esta máquina al generarlo. Antes de dar por buena la capa de datos hace falta:

- Levantar una instancia (local, contenedor o Azure SQL) y aplicar el script tal cual.
- Confirmar que los triggers `INSTEAD OF UPDATE, DELETE` / `AFTER UPDATE` disparan y bloquean lo que
  deben (inmutabilidad de residentes cerrados, transición de estados del borrador/versión de basal).
- Confirmar que los `CHECK (ISJSON(...)=1)` y los `CHECK` con `JSON_VALUE` aceptan/rechazan los
  payloads reales que produce `SqlBaselineRepository`.
- Probar `SqlResidentRepository`/`SqlBaselineRepository` end-to-end contra esa instancia (hoy solo
  están verificados por lectura, no ejecutados).

## 2. `ResidApp.Web` es la plantilla MVC en blanco

`dotnet new mvc` sin modificar. Falta:

- Registrar en DI `ResidentBaselineApplicationService` y sus dependencias (`SqlConnectionFactory`,
  repositorios, proveedor de evidencia de autorización).
- Cadena de conexión real a SQL Server (`appsettings.json` / secretos de entorno).
- Controladores y vistas propios — hoy no existe ninguna pantalla del dominio, solo el scaffold por
  defecto (`HomeController`, vistas de ejemplo).
- Autenticación real de sesión (`ISessionIdentityProvider` está definido como puerto pero no tiene
  implementación de infraestructura todavía).

## 3. Sin tests propios del vertical

`UnitTests`, `IntegrationTests` y `FunctionalTests` solo tienen el test de plantilla que genera
`dotnet new xunit`. Falta cobertura real de:

- Reglas de validación de `Resident`/las 9 respuestas de área/`BarthelAssessment`.
- Motor de autorización (`ResidentBaselinePolicy`) por perfil y por rama alcanzable/inalcanzable.
- Casos de uso de `ResidentBaselineApplicationService` (éxito, idempotencia, conflicto de
  concurrencia `BASELINE_DRAFT_REVISION_CONFLICT`, denegación de acceso).
- Los repositorios Dapper, una vez exista una instancia de SQL Server contra la que correr
  `IntegrationTests`.

## 4. Entorno .NET de esta máquina

`global.json` estaba fijado a `10.0.302`, que ya no está instalado (se repineó a `10.0.204`, el único
SDK de .NET 10 presente). Además, `dotnet test` por CLI falla con un error de resolución de workloads
(`microsoft.net.sdk.macos`, manifiesto `26.5.10284` ausente en disco) — es una caché de workloads de
Visual Studio desincronizada a nivel de máquina, no algo del proyecto. Workaround verificado:
`dotnet test src/ResidApp.sln -p:MSBuildEnableWorkloadResolver=false`. Arreglo de fondo pendiente:
`dotnet workload repair` o reinstalar los workloads de móvil desde el instalador de Visual Studio.

## 5. Desviaciones deliberadas de fidelidad 1:1 con el prototipo TS (ya documentadas en el código, listadas aquí para visibilidad)

- `SqlBaselineRepository` exige `RowsAffected == 1` al cerrar el borrador de basal y lanza
  `BASELINE_DRAFT_REVISION_CONFLICT` si no — el TS original no comprobaba `changes` ahí (hueco TOCTOU
  teórico). Mejora intencional, no un bug a "corregir" alineándolo con el original.
- No se tradujo el trigger SQLite `baseline_versions_validate_insert` (~230 líneas): la transacción
  C# ya copia los campos desde el borrador leído en la misma transacción, la revalidación sería
  tautológica con el único punto de entrada actual. Reconsiderar si aparece otro punto de escritura.
- Se omitió el puerto `IAuditTrail` que contemplaba el plan original, por no tener uso real todavía.
- Ramas de `ResidentBaselinePolicy` inalcanzables con el cableado actual de `RequestAuthorizationContext`
  (autorización familiar en `RESIDENT_IDENTITY_READ`; lectura de basal para `AUXILIAR/ENFERMERIA/MEDICINA`)
  se portaron igual, por fidelidad al motor puro — están documentadas como código inalcanzable hoy, no
  se "arreglaron" silenciosamente.
- FKs hacia tablas de verticales fuera de alcance (`buildings/floors/rooms/places`,
  `barthel_catalog_options`) se omitieron deliberadamente; añadir cuando se porten esos verticales.
