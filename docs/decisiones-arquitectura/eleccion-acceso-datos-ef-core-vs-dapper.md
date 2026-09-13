# Entity Framework Core "Code First" frente a Dapper

## Contexto

El acceso a datos actual (`README.md:138-142`) se declara como hecho consumado, sin ADR propio que lo respalde:

> "Persistencia en SQL Server con acceso mediante Dapper (sin ORM), dentro de transacciones ACID explícitas."

`docs\decisiones-arquitectura\instrucciones-migracion-net10.md` (sección C) confirma Dapper como mecanismo de traducción del legado SQLite/D1 a SQL Server, pero es una instrucción operativa, no una comparación razonada frente a un ORM:

> "Traduce esas restricciones transaccionales a procedimientos almacenados y consultas SQL nativas ejecutadas mediante Dapper dentro de transacciones ACID (`IDbTransaction`)."

Ninguno de los dos documentos justifica por qué Dapper y no Entity Framework Core. La única comparación de ORM que existe en todo el repositorio es `docs\legado-cloudflare\docs\architecture\0003-aprobacion-d1-drizzle.md`, y es sobre Drizzle en el prototipo TypeScript/Cloudflare — ese razonamiento nunca se trasladó a la decisión .NET. En la práctica, Dapper se adoptó por continuidad con el patrón "SQL explícito" del legado, no por una decisión comparada y documentada. Este documento cubre ese vacío.

La razón real del diseño actual está en `docs\decisiones-arquitectura\integridad-sql-basal-legado.md`: 56 reglas de negocio (inmutabilidad append-only, revoke-only, guardas de transición de estado, concurrencia optimista, "auditoría o nada") viven como triggers y `CHECK` de SQL Server, descritas explícitamente como

> "la red de seguridad que actúa aunque el código... tenga un error o se salte una validación."

`src\ResidApp.Infrastructure\Persistence\SqlBaselineRepository.cs` es la implementación real de ese diseño: transacciones ADO.NET manuales, concurrencia optimista con `RowsAffected == 1` sobre `draft_revision` (líneas 164-179), y una lectura para Dirección Clínica (`ReadAsClinicalDirectionAsync`) que combina en una sola sentencia SQL dinámica multi-statement: verificación de autorización (5 `JOIN`), escritura de auditoría vía `OUTPUT INTO` sobre una tabla variable, y la lectura final. Los repositorios ya implementan interfaces de puerto (`IResidentRepository`, `IBaselineRepository` en `ResidApp.Application.Ports`), desacopladas de Dapper, lo que facilitaría introducir una implementación alternativa sin tocar la capa de aplicación. Ningún `.csproj` del repo referencia hoy Entity Framework Core.

## La premisa a corregir: no es "lecturas rápidas vs. lentas"

El motivo por el que se plantea este documento es la idea de que Dapper convendría reservarlo para las lecturas complejas donde importa la velocidad, y usar EF Core Code First para el resto. Esa premisa no encaja con lo que realmente hace `SqlBaselineRepository`: el motivo por el que usa SQL explícito no es el rendimiento de lectura, sino que esas operaciones combinan lectura, escritura de auditoría y reglas de negocio impuestas por triggers que actúan como red de seguridad independiente del código de aplicación. Con `.AsNoTracking()` y proyecciones, EF Core no es sensiblemente más lento que Dapper para consultas relacionales normales; la diferencia real de `ReadAsClinicalDirectionAsync` es un patrón de una sola sentencia con tabla variable y `OUTPUT INTO` que no tiene equivalente directo en LINQ.

La frontera correcta, por tanto, no es "lectura simple vs. compleja", sino: **¿esta tabla vive protegida por triggers de integridad estructural y concurrencia optimista fina, o es sustancialmente CRUD?**

## Qué implica realmente "Code First" aquí

Code First significa que las clases C# y las migraciones generadas a partir de ellas son la fuente de verdad del esquema. Eso choca con que hoy la fuente de verdad es un script SQL escrito a mano (`database\scripts\0001_init_sqlserver.sql`, 22 tablas, 27 triggers, 51 `CHECK`) que codifica esas 56 reglas de negocio. Hay dos formas de reconciliarlo si se adopta EF Core para alguna tabla:

- **Reimplementar las reglas como validación en `SaveChanges`/interceptores de EF.** Se pierde la propiedad de "red de seguridad independiente de un bug en la aplicación", que es un objetivo de diseño explícito y documentado — si el código de aplicación tiene el bug, ya no hay nada por debajo que lo pare.
- **Usar migraciones de EF pero incluir el SQL de los triggers dentro de ellas (`migrationBuilder.Sql(...)`).** Mantiene Code First en sentido estricto — el historial de migraciones en git sigue siendo el punto de partida para `dotnet ef database update` — sin renunciar a la protección a nivel de motor. Es el único patrón honesto de "Code First" aplicable a una tabla que necesite ese nivel de garantía; en la práctica, para las tablas que se proponen más abajo para EF Core, no hace falta porque no tienen ese nivel de trigger.

## Coste operativo de introducir EF Core en paralelo

- Dos mecanismos de autoridad de esquema conviviendo: la tabla `__EFMigrationsHistory` de EF junto al script manual ya aplicado en Azure.
- El pipeline de CI/CD actual aplica un único script contra SQL Server real en cada build/deploy; incorporar EF Core exigiría un paso adicional (`dotnet ef database update` o `dotnet ef migrations script` integrado en el mismo pipeline).
- Riesgo de que un `Add-Migration` automático intente generar cambios sobre tablas que EF no conoce del todo bien si alguna vez conviven en el mismo `DbContext` con tablas ajenas a EF.

## Frontera propuesta, si se llegara a adoptar

| Bloque | Motor | Por qué |
|---|---|---|
| Identidad y organización (`accounts`, `centers`, `units`, `residents`) | EF Core Code First | Datos mayormente CRUD, validación simple (unicidad de código, estados Active/Inactive), sin concurrencia optimista fina. |
| Autorización (`profile_scopes`, `profile_unit_scopes`, `profile_resident_scopes`, `profile_permissions`) | EF Core Code First, con el revoke-only como convención de repositorio (nunca exponer `Remove`, solo `Revoke`) | El revoke-only es sencillo de garantizar en la capa de aplicación; no necesita la potencia de un trigger SQL. |
| Basal — borrador y versión firmada (los 10 `baseline_*`) | Dapper + SQL explícito, sin cambios | Concurrencia optimista por `RowsAffected`, append-only/revoke-only por trigger, y el trigger maestro de validación de firma son, por diseño documentado, una red de seguridad independiente del código de aplicación. |
| Episodios y ubicación (`resident_center_episodes`, `resident_location_intervals`) | Dapper | Mismo patrón close-only/no-delete que Basal. |
| Auditoría e idempotencia (`audit_events`, `idempotency_operations`) | Dapper | Inmutabilidad estricta y el patrón "auditoría o nada" con `OUTPUT INTO`; además ya viven dentro de las mismas transacciones Dapper de Basal/Residentes. |

Criterio general para verticales futuros: si una tabla necesita más de 2-3 triggers de integridad estructural (append-only, revoke-only, transition-guard) o concurrencia optimista fina, se queda en Dapper; si es sustancialmente CRUD con validación de aplicación, EF Core Code First.

## Conclusión: opinión y recomendación

**No introducir EF Core ahora**, aunque la frontera de arriba sea la correcta el día que se haga.

- El beneficio para Identidad/Autorización es hoy hipotético: no existe todavía ninguna pantalla de administración de centros/unidades/cuentas que sufra el boilerplate de Dapper. Añadir una segunda tecnología de acceso a datos resolvería un dolor que el código real todavía no tiene.
- El coste es inmediato y no especulativo: dos mecanismos de autoridad de esquema conviviendo, un paso nuevo en un pipeline de CI/CD que hoy funciona con un único script, y una superficie de aprendizaje adicional (migraciones de EF, sus trampas de tracking) en un proyecto con un único script ya probado contra SQL Server real, en pleno desarrollo del vertical más importante (Basal).
- Choca con la propia regla de trabajo del proyecto (`CLAUDE.md`): "usa el mínimo código necesario; evita abstracciones o complejidad especulativa" y "no diseñes para necesidades futuras hipotéticas". Introducir un segundo motor de acceso a datos "por si acaso" para tablas que hoy se gestionan con un puñado de `INSERT`/`UPDATE` sencillos es exactamente ese tipo de complejidad especulativa.
- El único problema real detectado hasta ahora con Dapper — `System.DateOnly` sin `TypeHandler`, documentado en `src\ResidApp.Infrastructure\Persistence\DapperDateOnlyTypeHandler.cs` — se resolvió con un handler de 15 líneas. No es evidencia de que Dapper esté frenando al equipo.

Revisar esta decisión cuando ocurra alguna de estas dos cosas:

1. Se empieza a construir una pantalla de administración CRUD real sobre `accounts`/`centers`/`units` (o similar) y el boilerplate de Dapper para esos formularios se vuelve perceptible.
2. El equipo crece más allá de una persona y conviene que el esquema de las tablas más simples sea autodescriptivo en C# para quien no conozca todavía el script SQL.

Hasta entonces, Dapper cubre las necesidades actuales y añadir EF Core sería resolver un problema que el proyecto no tiene todavía.
