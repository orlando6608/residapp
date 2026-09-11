# Directrices de Migración de Arquitectura: ResidApp (.NET 10 LTS)

## 1. Contexto General
Vamos a migrar el esqueleto de ingeniería sociosanitaria ubicado en `docs/legado-cloudflare/` hacia un monolito robusto en ASP.NET Core MVC (.NET 10), C# 14 y SQL Server. El objetivo es preservar la lógica defensiva y las validaciones clínicas inalteradas, elevando la estructura a patrones empresariales.

## 2. Reglas de Diseño de Ingeniería (C# 14 / .NET 10)

### A. Identificadores Fuertemente Tipados (DDD)
Para replicar los Branded Types de TypeScript (`EntityId<Entity>`), debes implementar estructuras de registros de solo lectura (`readonly record struct`) que envuelvan un `Guid`:
* Mapear `ResidentId`, `CenterId`, `UnitId`, `AccountId`, `BaselineVersionId`.
* Utilizar constructores primarios. Deben autovalidarse garantizando que el Guid no sea `Guid.Empty`.

### B. Mapeo del Dominio Clínico (C# 14 `field` keyword)
Traduce las lógicas de `lib/domain/baseline/` utilizando las nuevas propiedades respaldadas por campos de C# 14 para mantener las validaciones compactas dentro de las entidades:
* **Catálogos Estrictos:** Mapear enums para perfiles (`AUXILIAR`, `ENFERMERIA`, `MEDICINA`, etc.), áreas basales (9 áreas) y sexos biológicos (`male`, `female`, `other`, `unknown`).
* **Reglas Cruzadas en Propiedades:** Implementar la lógica del fichero `baseline-area.ts` usando la sintaxis `init => field = ...` para lanzar excepciones de dominio inmediatas si se violan reglas como:
  * Restricción de líquidos `NO_APLICA` válida ÚNICAMENTE si la vía es `ENTERAL`.
  * Pasar una etiología de demencia si la categoría no es `DEMENCIA_DOCUMENTADA`.
  * Exigir texto libre descriptivo si el enum seleccionado es `OTRA` u `OTRO`.

### C. Persistencia con Dapper y SQL Server
El script `0001_resident_baseline_foundation.sql` contiene triggers complejos de SQLite que abortan inserciones mediante `RAISE(ABORT)`. Dado que utilizaremos un enfoque híbrido en .NET 10:
* Traduce esas restricciones transaccionales a procedimientos almacenados y consultas SQL nativas ejecutadas mediante **Dapper** dentro de transacciones ACID (`IDbTransaction`).
* Traduce las tablas que contienen `answer_payload` (JSON) a columnas de tipo `NVARCHAR(MAX)` en SQL Server, utilizando `System.Text.Json` para serializar y deserializar los objetos de configuración de las 9 áreas asistenciales.
* Mantener la unicidad indexada condicional (ej: solo un borrador activo por residente).

### D. Motor de Autorización Deny-By-Default
Traduce de forma exacta el comportamiento del motor puro `authorizeResidentBaseline` (`policy.ts`) utilizando la infraestructura de **Claims-Based Authorization** y políticas personalizadas de ASP.NET Core 10:
* Toda petición debe validar el contexto del `AuthorizationSubject`: cuenta activa, autenticada y mapeo del perfil activo exclusivo por operación.
* Implementar la obligación de auditoría médica (`ClinicalDetailAuditObligation`): si el perfil es `DIRECCION_CLINICA` y lee un historial, la operación se aprueba pero exige escribir de forma atómica e inmutable un registro en la tabla `audit_events`.

## 3. Mapeo de Archivos para la Migración
* `docs/legado-cloudflare/lib/domain/shared/identifiers.ts` ──> `src/ResidApp.Shared/Identifiers.cs`
* `docs/legado-cloudflare/lib/authorization/policy.ts` ──> `src/ResidApp.Infrastructure/Security/AuthorizationPolicies.cs`
* `docs/legado-cloudflare/lib/application/resident-baseline-service.ts` ──> `src/ResidApp.Application/Services/BaselineService.cs`
* `database/migrations/0001_resident_baseline_foundation.sql` ──> Portar estructura relacional adaptada a tipos SQL Server en `database/scripts/0001_init_sqlserver.sql`
