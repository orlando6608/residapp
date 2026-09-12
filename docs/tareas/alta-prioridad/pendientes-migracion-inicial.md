# Pendientes tras el andamiaje inicial y el port del vertical residente/basal

Estado al 2026-09-12, después de completar Paso 0 (andamiaje), Paso 1 (identificadores/enums
compartidos), Paso 2 (dominio asistencial), Paso 3 (aplicación + infraestructura Dapper/SQL Server) y
Paso 4 (ejecución contra un motor real, cableado de `ResidApp.Web` y tests) sobre el vertical
residente/basal. `dotnet build src/ResidApp.sln` compila sin avisos ni errores; 47 tests reales pasan
(ver punto 3).

## 1. DDL de SQL Server — ejecutado y verificado contra un motor real

`database/scripts/0001_init_sqlserver.sql` se ejecutó contra una instancia real de SQL Server local
(`ACER-ORLANDO`, base `ResidApp`), sin modificar el script: 22 tablas, 27 triggers, 51 checks y 224
índices creados correctamente. El único obstáculo fue un detalle del cliente `sqlcmd` (necesita `-I`
para activar `QUOTED_IDENTIFIER`; sin él, `CREATE INDEX` falla con el error 1934), no un problema del
script.

Verificado indirectamente mediante los tests de integración del punto 3 contra esa misma instancia:
alta de residente con episodio/ubicación/auditoría/idempotencia, y el camino "sin borrador" de firma de
basal. **Sigue sin verificarse uno por uno** el resto de los 27 triggers (por ejemplo, la inmutabilidad
de residentes cerrados o el trigger maestro `baseline_versions_validate_insert` con un borrador
completo) — bloqueado por el punto 2.1 de más abajo.

## 2. `ResidApp.Web` — cableado real para Residente; Basal cableado pero no demostrable

Ya no es la plantilla en blanco. Hecho:

- DI registrada en `Program.cs`: `SqlConnectionFactory`, `SqlResidentRepository`,
  `SqlBaselineRepository`, `SqlAuthorizationEvidenceProvider`, los 3 casos de uso y
  `ResidentBaselineApplicationService`.
- Cadena de conexión real vía `dotnet user-secrets` (nunca en `appsettings.json`).
- Identidad de sesión de desarrollo: `DevSessionIdentityProvider` + `DevAuthController`
  (`/DevAuth/Login`), basada en cookie propia — **no es autenticación real**, la decisión de proveedor
  productivo sigue completamente abierta (ver `README.md`, "Decisiones pendientes").
- `ResidentsController`/`Create`: alta de residente de extremo a extremo, verificada manualmente (curl)
  y con test funcional automatizado (`ResidentsFlowTests`).
- `BaselineController`/`Sign` y `/Direction`: cableados contra `SignBaseline` y `ReadDirectionBaseline`,
  con formularios y vistas propias.

### 2.1. Hueco identificado: no existe capacidad para crear el contenido de un borrador de basal

Al cablear `BaselineController` se confirmó que **ni este puerto ni el prototipo legado**
(`lib/application/resident-baseline-service.ts`) tienen un caso de uso para crear el contenido de un
borrador de basal (las 9 áreas + Barthel): `signBaseline`/`SignBaseline` siempre asumieron un `draftId`
ya existente en `baseline_drafts`. Esto no es una regresión de este paso, es un hueco pre-existente que
el cableado de la Web ha hecho visible.

Consecuencia práctica: `BaselineController/Sign` y `/Direction` no se pueden demostrar hoy end-to-end
(sin un borrador real, `Sign` siempre devuelve `BASELINE_SIGN_NOT_AUTHORIZED`; `Direction` sobre un
residente sin basal firmado siempre devuelve `CLINICAL_DETAIL_READ_NOT_AUTHORIZED`, por diseño — ver
"auditoría o nada" en `integridad-sql-basal-legado.md`). No se ha improvisado un sembrado SQL a mano
para forzar un caso de éxito: replicaría, sin diseño previo en C#, la lógica del trigger maestro de 7
comprobaciones (`baseline_versions_validate_insert`).

Construir esa capacidad de autoría de borrador pertenece al vertical Enfermería/Medicina
(`docs/flujos-clinicos/gestion-basal-barthel.md`, `BAS-01` a `BAS-19`, común a ambos perfiles según
`checklist-construccion-mvp.md`), no a este documento.

## 3. Tests — 47 tests reales en verde

`UnitTests` (37), `IntegrationTests` (9) y `FunctionalTests` (1) sustituyen a los placeholders de
`dotnet new xunit`:

- **Dominio** (`UnitTests`): validación de `Resident` (nombre/fecha de nacimiento), `BarthelAssessment`
  (10 ítems, sin duplicados, suma), `CognitionAreaAnswer` como muestra representativa de las reglas
  cruzadas de las 9 respuestas de área (no se replican las 9 completas).
- **Motor de autorización** (`UnitTests`): `ResidentBaselinePolicyTests` cubre cada
  `AuthorizationDenialReason` y el camino "allow" de cada acción, incluida la obligación de auditoría de
  Dirección Clínica.
- **Repositorios Dapper contra SQL Server real** (`IntegrationTests`): alta de residente con auditoría e
  idempotencia (repetición devuelve el mismo resultado sin duplicar; reutilización de clave con payload
  distinto se detecta y rechaza); firma de basal sin borrador previo; lectura de Dirección Clínica sin
  basal firmado ("auditoría o nada", sin escribir auditoría si no hay recurso).
- **Servicio de aplicación completo** (`IntegrationTests`): `ResidentBaselineApplicationServiceTests`
  recorre identidad de sesión → evidencia real en SQL → motor de decisión → repositorio, para
  Administración y Enfermería, con y sin permiso.
- **Camino feliz por HTTP real** (`FunctionalTests`): `ResidentsFlowTests` levanta `ResidApp.Web` con
  `WebApplicationFactory` y repite el login de desarrollo + alta de residente.

No cubierto, por la misma razón del punto 2.1: el camino de éxito completo de `SignDraftAsync` y de
`ReadAsClinicalDirectionAsync` con un basal realmente firmado (no hay forma de sembrar un borrador
válido sin construir antes esa capacidad).

## 4. Entorno .NET de esta máquina — incidencia no reproducida hoy

El SDK instalado ahora es `10.0.401` (además de `10.0.204`, al que sigue fijado `global.json`); con
`rollForward: latestFeature` resuelve sin intervención. `dotnet build src/ResidApp.sln` y
`dotnet test src/ResidApp.sln` han corrido en verde sin necesitar el workaround
`-p:MSBuildEnableWorkloadResolver=false` documentado el 2026-09-11. Se deja documentado por si
reaparece en otra sesión o máquina:

```bash
dotnet test src/ResidApp.sln -p:MSBuildEnableWorkloadResolver=false
```

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

## 6. Bugs reales encontrados y corregidos al ejecutar contra un motor real

Ninguno de los tres era detectable sin ejecutar el código contra SQL Server de verdad — confirma por
qué el punto 1 estaba marcado como crítico:

- **Dapper 2.1.79 no soporta `System.DateOnly` como parámetro**: lanzaba `NotSupportedException` al dar
  de alta un residente (fecha de nacimiento). Corregido con `DapperDateOnlyTypeHandler`
  (`ResidApp.Infrastructure/Persistence/`), registrado explícitamente en `Program.cs` — deliberadamente
  no como `[ModuleInitializer]` dentro de la librería (aviso `CA2255`), sino como llamada explícita desde
  la aplicación.
- **Los 6 identificadores tipados perdían su valor al deserializarse desde JSON**: `AccountId`,
  `CenterId`, `UnitId`, `ResidentId`, `BaselineVersionId` y `BaselineDraftId` (todos `readonly record
  struct` en `ResidApp.Shared/Identifiers.cs`) volvían de `System.Text.Json` con `Value = Guid.Empty`,
  **sin lanzar ninguna excepción**, al releer `result_json` de `idempotency_operations` en una repetición
  idempotente. Corregido añadiendo `[method: JsonConstructor]` a los 6.
- **`SqlBaselineRepository.FindSignIdempotencyAsync` construía mal el parámetro `@AccountId`**:
  `new { input.AccountId.Value, input.OperationId }` genera una propiedad anónima llamada `Value`, no
  `AccountId` — Dapper no encontraba el parámetro y SQL Server fallaba con "Must declare the scalar
  variable". Corregido nombrando el parámetro explícitamente (`AccountId = input.AccountId.Value`).
