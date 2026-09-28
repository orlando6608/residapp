# Pendientes tras el andamiaje inicial y el port del vertical residente/basal

Estado al 2026-09-12, después de completar Paso 0 (andamiaje), Paso 1 (identificadores/enums
compartidos), Paso 2 (dominio asistencial), Paso 3 (aplicación + infraestructura Dapper/SQL Server) y
Paso 4 (ejecución contra un motor real, cableado de `ResidApp.Web` y tests) sobre el vertical
residente/basal. `dotnet build src/ResidApp.sln` compila sin avisos ni errores; 47 tests reales pasan
(ver punto 3).

**Revisado el 2026-09-28:** los pendientes de este documento están cerrados (puntos 1, 2.1 y 3
actualizados). Se conserva como registro de las desviaciones deliberadas (punto 5) y de los bugs
encontrados (punto 6).

## 1. DDL de SQL Server — ejecutado y verificado contra un motor real

`database/scripts/0001_init_sqlserver.sql` se ejecutó contra una instancia real de SQL Server local
(`ACER-ORLANDO`, base `ResidApp`), sin modificar el script: 22 tablas, 27 triggers, 51 checks y 224
índices creados correctamente. El único obstáculo fue un detalle del cliente `sqlcmd` (necesita `-I`
para activar `QUOTED_IDENTIFIER`; sin él, `CREATE INDEX` falla con el error 1934), no un problema del
script.

**Actualización 2026-09-28 — los 27 triggers verificados uno por uno** en
`tests/IntegrationTests/DatabaseTriggerTests.cs`, sobre filas reales generadas con los repositorios
(alta, dos firmas de basal, lectura de Dirección, borrador cancelado): cada escritura prohibida se rechaza
con su código, y las transiciones permitidas (revocar ámbito/unidad/permiso una vez, cerrar episodio y
ubicación una vez, avanzar la revisión del borrador exactamente en 1) siguen funcionando. Se comprobó
además, con una muestra de tres triggers (`TR_ps_revoke_only`, `TR_bv_immutable`, `TR_bdb_active_guard`),
que sus tests fallan si se desactiva el trigger. Dos hallazgos:

- `TR_rcb_update_guard` (`BASELINE_CURRENT_UPDATE_INVALID`) es **inalcanzable**: cambiar `centro_id` en
  `basales_vigentes_residente` rompe antes la FK compuesta a `residentes(centro_id, id)`, y los triggers
  `AFTER` se evalúan después de las restricciones. El cambio queda rechazado igualmente (error 547); el
  test lo documenta así en lugar de darlo por verificado.
- Borrar `basales_borrador_barthel` con ítems choca antes con la FK de los ítems; el trigger solo se
  alcanza sobre un Barthel sin ítems, que es como lo prueba el test.

El trigger maestro `baseline_versions_validate_insert` sigue sin traducirse por decisión deliberada (ver
punto 5).

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

**Resuelto el 2026-09-14** desde el vertical Enfermería: `CreateBaselineDraft`, `SaveBaselineDraftArea`,
`SaveBaselineDraftBarthel`, `CancelBaselineDraft` y las pantallas de `EnfermeriaBasalController`, cuya
confirmación envía a `BaselineController/Sign`. El ciclo completo (crear, 9 áreas, Barthel, firmar, leer
como Dirección Clínica) está cubierto por `SqlBaselineRepositoryDraftTests.FullCycle_*`. Se conserva abajo
el análisis original como contexto.

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

**Actualización 2026-09-28:** la suite completa suma 146 tests (37 unitarios, 108 de integración, 1
funcional) incluyendo los de Auxiliar, Enfermería y los triggers. El camino de éxito de `SignDraftAsync`
y de `ReadAsClinicalDirectionAsync` con un basal realmente firmado, que aquí figuraba como no cubierto, lo
cubre ya `SqlBaselineRepositoryDraftTests.FullCycle_*`.

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
