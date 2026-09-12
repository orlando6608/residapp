# Integridad SQL del prototipo legado (Residente/Basal)

## Alcance

Este documento inventaria la capa de **integridad a nivel de motor de datos** que el prototipo Cloudflare/D1 implementa en `docs/legado-cloudflare/db/migrations/0001_resident_baseline_foundation.sql` (1622 líneas; 29 tablas, 56 índices, 56 triggers; única migración existente, inmutable — cualquier evolución del esquema debe hacerse en `0002` o posterior).

No repite el catálogo clínico. Para el contenido de Barthel, las nueve áreas basales y sus reglas de campo, la fuente es `docs/legado-cloudflare/docs/architecture/0002-contrato-datos-residente-basal.md` ("el contrato de datos"), que ya lo documenta exhaustivamente. Este documento cubre lo que ese contrato no cubre: **cómo el propio SQL fuerza esas reglas y las de autorización/workflow**, con independencia de lo que haga la capa de aplicación — es decir, la red de seguridad que actúa aunque el código TypeScript tenga un error o se salte una validación.

Para el resto del contexto arquitectónico (por qué D1/Drizzle, por qué SQLite, gates `DB-T01`–`DB-T16`), ver `docs/legado-cloudflare/docs/architecture/0001-modelo-minimo-residente-basal.md` y `0003-aprobacion-d1-drizzle.md`. Para el enfoque de traducción a .NET 10, ver `instrucciones-migracion-net10.md` en esta misma carpeta.

## Patrón general de claves foráneas

Todas las FK del esquema usan `ON UPDATE no action ON DELETE restrict` — ninguna fila histórica, de auditoría o clínica puede desaparecer por borrado en cascada. La mayoría son **FK compuestas** que incluyen `center_id` (y a menudo `resident_id`, `draft_id` o `baseline_version_id`) junto con el identificador simple, p. ej.:

```sql
FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict
```

Esto impide, a nivel de motor, que una fila referencie una entidad de otro centro, residente o versión — defensa en profundidad redundante con las comprobaciones de la capa de aplicación, no un sustituto de ellas.

## Categorías de triggers

### 1. Inmutabilidad append-only (abortan siempre en UPDATE/DELETE)

Un par de triggers `_no_update`/`_no_delete` (o solo `_no_delete` cuando la tabla no admite UPDATE alguno) que abortan incondicionalmente:

- `barthel_catalog_options_no_update` / `_no_delete` → `BARTHEL_CATALOG_IMMUTABLE`
- `baseline_versions_no_update` / `_no_delete` → `BASELINE_VERSION_IMMUTABLE`
- `baseline_version_areas_no_update` / `_no_delete` → `BASELINE_VERSION_AREA_IMMUTABLE`
- `baseline_version_barthel_no_update` / `_no_delete` → `BASELINE_VERSION_BARTHEL_IMMUTABLE`
- `baseline_version_barthel_items_no_update` / `_no_delete` → `BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE`
- `baseline_supersessions_no_update` / `_no_delete` → `BASELINE_SUPERSESSION_IMMUTABLE`
- `baseline_draft_contributions_no_update` / `_no_delete` → `BASELINE_CONTRIBUTION_IMMUTABLE`
- `profile_scopes_no_delete`, `profile_permissions_no_delete`, `profile_unit_scopes_no_delete`, `profile_resident_scopes_no_delete` → sus respectivos `PROFILE_*_IMMUTABLE`
- `idempotency_operations_no_delete` → `IDEMPOTENCY_OPERATION_IMMUTABLE`
- `audit_events_no_update` / `_no_delete` → `AUDIT_EVENT_IMMUTABLE`
- `resident_current_baselines_no_delete` → `BASELINE_CURRENT_DELETE_FORBIDDEN`
- `baseline_drafts_no_delete` → `BASELINE_DRAFT_DELETE_FORBIDDEN`

El catálogo `barthel_catalog_options` está protegido igual que los datos clínicos ya firmados: es un catálogo de referencia fijo, no editable ni siquiera por migración de datos.

### 2. "Solo revocar" / "solo cerrar" (una única transición terminal permitida)

Triggers `BEFORE UPDATE ... WHEN <condición que detecta cualquier cambio no autorizado>` que abortan si cambia cualquier campo distinto del que cierra el registro, o si el registro ya estaba cerrado:

- `profile_scopes_revoke_only`, `profile_permissions_revoke_only`, `profile_unit_scopes_revoke_only`, `profile_resident_scopes_revoke_only` → solo permiten pasar de `ACTIVO`/sin revocar a revocado; cualquier otro cambio aborta con su `*_IMMUTABLE`.
- `resident_center_episodes_close_only`, `resident_location_intervals_close_only` → solo permiten fijar `valid_until` (cerrar el intervalo); cualquier otro campo modificado, o cerrar un intervalo ya cerrado, aborta con `RESIDENT_EPISODE_IMMUTABLE` / `RESIDENT_LOCATION_IMMUTABLE`. Ejemplo literal (`resident_center_episodes_close_only`):
  ```sql
  WHEN OLD.`valid_until` IS NOT NULL
    OR NEW.`id` <> OLD.`id` OR NEW.`resident_id` <> OLD.`resident_id` OR NEW.`center_id` <> OLD.`center_id`
    OR NEW.`internal_reference` IS NOT OLD.`internal_reference` OR NEW.`valid_from` <> OLD.`valid_from`
    OR NEW.`created_at` <> OLD.`created_at` OR NEW.`created_by_account_id` <> OLD.`created_by_account_id`
    OR NEW.`created_by_profile` <> OLD.`created_by_profile` OR NEW.`valid_until` IS NULL
  ```
- `center_location_config_versions_close_only` → mismo patrón para las versiones de configuración de ubicación del centro.
- `idempotency_operations_transition_guard` → solo permite `IN_PROGRESS` → `SUCCEEDED`.

### 3. Guarda de transición de estado del borrador basal

`baseline_drafts_transition_guard` (`BEFORE UPDATE ON baseline_drafts`) aborta con `BASELINE_DRAFT_TRANSITION_INVALID` si: el estado origen no era `ACTIVE`; cambia cualquier campo inmutable (residente, centro, unidad, autor, fecha de creación); el nuevo estado no es uno de `ACTIVE`/`CANCELLED`/`SIGNED`; al permanecer `ACTIVE` no se incrementa `draft_revision` en exactamente 1 (control de concurrencia optimista); al pasar a un estado terminal `draft_revision` cambia; o al pasar a `SIGNED` no existe ya una fila en `baseline_versions` con `source_draft_id` igual al borrador.

### 4. "Mutable solo mientras el borrador padre esté ACTIVE"

Cada tabla de contenido de borrador (`baseline_draft_areas`, `baseline_draft_barthel`, `baseline_draft_barthel_items`, `baseline_draft_contributions`) tiene su propio trío INSERT/UPDATE/DELETE que comprueba que el `baseline_drafts` referenciado siga `ACTIVE`, y que los campos identificadores no cambien en UPDATE:

- `baseline_draft_areas_insert_only_while_active` / `_mutable_only_while_active_update` / `_delete` → `BASELINE_DRAFT_AREA_PARENT_NOT_ACTIVE` / `BASELINE_DRAFT_AREA_IMMUTABLE`
- Mismo patrón para `baseline_draft_barthel_*` y `baseline_draft_barthel_items_*` → `BASELINE_DRAFT_BARTHEL(_ITEM)_PARENT_NOT_ACTIVE` / `_IMMUTABLE`
- `baseline_draft_contributions_insert_only_while_active` → `BASELINE_CONTRIBUTION_PARENT_NOT_ACTIVE`

### 5. Validación de JSON en INSERT/UPDATE (texto abierto "OTRO/OTRA")

`baseline_draft_areas_payload_validate_insert` y su gemelo `_payload_validate_update` (disparado solo con `UPDATE OF answer_payload`) aplican, vía `json_extract`/`json_each` sobre la columna JSON `answer_payload`, la misma regla en ambos sentidos ("si se elige la opción abierta, el texto es obligatorio; si no se elige, el texto debe estar vacío") para seis campos, todos con el mismo código de error `BASELINE_OPEN_TEXT_INVALID`:

| Área | Opción abierta | Campo de texto exigido |
| --- | --- | --- |
| CONTINENCIA | `managementCodes` contiene `OTRO` | `managementOtherText` |
| COMUNICACION | `usualFormsCodes` contiene `OTRA` | `usualFormOtherText` |
| CONDUCTA | `patternCodes` contiene `OTRA` | `patternOtherText` |
| AYUDAS_HABITUALES | `aidCodes` contiene `OTRO_PRODUCTO_DE_APOYO` | `otherSupportProductText` |
| AYUDAS_HABITUALES | `aidCodes` contiene `OTRO` | `otherSupportText` |
| COGNICION | `etiologyCode = 'OTRA'` | `etiologyOtherText` |
| COGNICION | `clinicalReferenceSourceCode = 'OTRA'` | `clinicalReferenceSourceOtherText` |
| ALIMENTACION | `swallowingPrecautionsCode = 'PRECAUCIONES_DOCUMENTADAS'` | `swallowingPrecautionsText` |

### 6. Reverificación de autorización dentro de SQL (no solo en la capa de aplicación)

- `resident_center_episodes_no_overlap` (`BEFORE INSERT ON resident_center_episodes`): antes de aceptar un episodio, comprueba en SQL que la cuenta/perfil creador tenga un `profile_scopes` activo de `ADMINISTRACION`, o de `ENFERMERIA` con el permiso `RESIDENT_IDENTITY_CREATE` no revocado (`RESIDENT_CREATE_NOT_AUTHORIZED`); y que el nuevo intervalo no solape en el tiempo con uno existente del mismo residente (`RESIDENT_EPISODE_OVERLAP`).
- `resident_location_intervals_validate_insert`: repite la misma comprobación de grant/permiso pero exigiendo además ámbito de unidad (`profile_unit_scopes`) (`RESIDENT_CREATE_NOT_AUTHORIZED`); exige que el episodio referenciado exista, pertenezca al mismo residente/centro y siga abierto (`RESIDENT_EPISODE_NOT_ACTIVE`); rechaza solapamiento temporal de ubicaciones (`RESIDENT_LOCATION_OVERLAP`); y valida contra `center_location_config_versions` que habitación/plaza estén habilitadas y, si son obligatorias, no vengan vacías (`RESIDENT_LOCATION_CONFIG_INVALID`).

Es decir: la autorización por perfil/ámbito/permiso y la configuración de habitación/plaza del centro no son solo un `if` en TypeScript — el motor de datos las repite de forma independiente antes de aceptar la fila.

### 7. El trigger maestro: `baseline_versions_validate_insert`

Es, con diferencia, el trigger más complejo (líneas 1047–1276 del `.sql`). Antes de aceptar una fila en `baseline_versions` (es decir, antes de dejar firmar un basal), comprueba en una sola transacción todo lo siguiente, en orden:

1. **Autorización del firmante** (`BASELINE_SIGN_NOT_AUTHORIZED`): la cuenta está activa; tiene un `profile_scopes` activo del perfil firmante en ese centro; tiene un `profile_unit_scopes` no revocado para la unidad de creación; tiene el permiso correcto según el motivo — `BASELINE_INITIAL_COMPLETE` si `reason_code = 'ALTA'`, si no `BASELINE_REEVALUATE` — no revocado; y el residente tiene una `resident_location_intervals` vigente (`valid_until IS NULL`) en esa misma unidad.
2. **Borrador de origen válido y coherente** (`BASELINE_DRAFT_NOT_SIGNABLE`): el `source_draft_id` existe, está `ACTIVE`, pertenece al mismo residente/centro/unidad, el residente está `ACTIVE`, y motivo/fuente/fecha/autor coinciden exactamente entre borrador y versión a insertar.
3. **Numeración correlativa** (`BASELINE_VERSION_NUMBER_INVALID`): `version_number` debe ser exactamente `max(version_number anterior del residente) + 1`.
4. **Exactamente 9 áreas** (`BASELINE_AREAS_INCOMPLETE`): cuenta las `baseline_draft_areas` del borrador con `catalog_version_code = 'BASAL_AREAS_V0_1'`; si no son 9, aborta.
5. **Componentes obligatorios completos por área** (`BASELINE_AREA_COMPONENTS_INCOMPLETE`): para cada una de las 9 áreas, comprueba que sus campos obligatorios no sean `NULL`/vacíos en el JSON — p. ej. Movilidad exige `displacementModeCode`+`technicalAidCode`+`transferCode`; Alimentación exige sus 5 campos; Continencia exige `urinationCode`+`bowelCode`+al menos un valor en `managementCodes`; Conducta exige al menos un `patternCodes` si `statusCode = 'PATRONES_CONDUCTUALES_HABITUALES'`; etc.
6. **Valores dentro del catálogo cerrado de cada área** (`BASELINE_AREA_CATALOG_INVALID`): repite en SQL, vía `json_extract`/`json_each`, la validación completa de enums de las 9 áreas — el mismo catálogo que documenta ADR-0002 y que implementa `lib/domain/baseline/validation.ts` en TypeScript. Incluye las reglas cruzadas: `NO_APLICA` en textura/líquidos solo si `routeCode = 'ENTERAL'`; exclusión mutua de `NINGUNO`/`NO_DOCUMENTADO` en selecciones múltiples; etiología solo si `categoryCode = 'DEMENCIA_DOCUMENTADA'`; GDS solo `GDS_1`..`GDS_7`/`NO_DOCUMENTADO`; si hay etiología o GDS documentado, exige fuente clínica distinta de `NO_DOCUMENTADO` y fecha no vacía.
7. **Barthel completo** (`BASELINE_BARTHEL_INCOMPLETE`): exige una fila en `baseline_draft_barthel` con `instrument_version_code = 'BARTHEL_COMUN_V0_1'`, fecha de evaluación no nula, **exactamente 10** filas en `baseline_draft_barthel_items`, y `total_score` igual a la suma exacta de las puntuaciones de esos 10 ítems.

Ninguna de estas 7 comprobaciones vive solo en la aplicación: si algún camino de código (un bug, una migración de datos, un script ad hoc) intentara insertar una versión firmada incompleta o inconsistente, SQLite la rechaza igualmente.

### 8. Guardas de "copia fiel" al firmar

`baseline_version_areas_copy_guard`, `baseline_version_barthel_copy_guard` y `baseline_version_barthel_items_copy_guard` (todos `BEFORE INSERT`) comprueban que cada fila que se inserta en las tablas de la versión firmada coincida campo a campo — incluida la propia autoría y fecha de registro — con la fila correspondiente del borrador de origen. Si no coincide exactamente, abortan (`BASELINE_SIGNED_AREA_NOT_FROM_DRAFT`, `BASELINE_SIGNED_BARTHEL_NOT_FROM_DRAFT`, `BASELINE_SIGNED_BARTHEL_ITEM_NOT_FROM_DRAFT`). Esto impide que el propio acto de firmar reescriba silenciosamente el contenido clínico.

### 9. Gobierno del puntero de "vigente" (una única versión activa por residente)

- `resident_current_baselines_validate_insert` (`BEFORE INSERT`): solo permite crear el puntero si la versión referenciada es la `version_number = 1` de ese residente, con sus 9 áreas + 1 evaluación Barthel + 10 ítems ya presentes, y `activated_at` coincide exactamente con `valid_from` de la versión (`BASELINE_CURRENT_INITIAL_INVALID`).
- `resident_current_baselines_validate_update` (`BEFORE UPDATE`): solo permite avanzar el puntero de la versión anterior a la siguiente si existe una `baseline_supersessions` que enlace exactamente esas dos versiones, la siguiente es `version_number` consecutivo con fecha de firma y vigencia posteriores, y las fechas de activación/supersesión coinciden; además revalida que la nueva versión también tenga sus 9 áreas + Barthel completos (`BASELINE_CURRENT_TRANSITION_INVALID`).
- `resident_current_baselines_no_delete`: el puntero nunca se borra (`BASELINE_CURRENT_DELETE_FORBIDDEN`).

### 10. `baseline_supersessions_validate_insert`

Antes de aceptar el enlace "versión anterior → versión nueva", exige que la versión anterior sea realmente la que figura como vigente en `resident_current_baselines`, que la nueva sea exactamente `version_number + 1` con `signed_at`/`valid_from` posteriores, y que `superseded_at` coincida con el `valid_from` de la nueva versión y sea posterior a la firma y vigencia de la anterior (`BASELINE_SUPERSESSION_INVALID`).

## Idempotencia y auditoría obligatoria (a nivel de CHECK, no de trigger)

- **`idempotency_operations`**: clave única `(account_id, action_code, operation_id)`; `CONSTRAINT idempotency_action_check` limita `action_code` a `RESIDENT_CREATE`, `BASELINE_SIGN`, `CLINICAL_DETAIL_READ`; `CONSTRAINT idempotency_result_check` exige que `result_resource_id`/`result_json`/`completed_at` sean los tres `NULL` mientras `status = 'IN_PROGRESS'` y los tres no-`NULL` (con `result_json` validado por `json_valid`) cuando `status = 'SUCCEEDED'`. El trigger `idempotency_operations_transition_guard` (categoría 2) impide cualquier transición de estado que no sea `IN_PROGRESS → SUCCEEDED`.
- **`audit_events`**: append-only (categoría 1); `CONSTRAINT audit_events_direction_read_check` obliga, por diseño de base de datos y no solo de aplicación, a que toda fila con `action_code = 'CLINICAL_DETAIL_READ'` tenga `active_profile = 'DIRECCION_CLINICA'`, `purpose_code = 'SUPERVISION_CLINICA'`, y `unit_id`/`resident_id` no nulos:
  ```sql
  CONSTRAINT "audit_events_direction_read_check" CHECK("audit_events"."action_code" <> 'CLINICAL_DETAIL_READ'
    or ("audit_events"."active_profile" = 'DIRECCION_CLINICA'
      and "audit_events"."purpose_code" = 'SUPERVISION_CLINICA'
      and "audit_events"."unit_id" is not null
      and "audit_events"."resident_id" is not null))
  ```
  Ninguna lectura clínica detallada de Dirección Clínica puede quedar sin ese registro de auditoría con ámbito completo — la obligación no depende de que el código que llama recuerde escribirla.

## Catálogo de códigos de error (`RAISE(ABORT, '<código>')`)

Extraído por grep directo sobre el `.sql`; cada código es lanzado por exactamente el/los trigger(s) indicados.

| Código | Trigger(s) que lo lanza(n) | Qué protege |
| --- | --- | --- |
| `BARTHEL_CATALOG_IMMUTABLE` | `barthel_catalog_options_no_update`/`_no_delete` | El catálogo de referencia Barthel es fijo |
| `LOCATION_CONFIG_IMMUTABLE` | `center_location_config_versions_close_only`/`_no_delete` | Solo se puede cerrar una config de ubicación, no editarla |
| `RESIDENT_CREATE_NOT_AUTHORIZED` | `resident_center_episodes_no_overlap`, `resident_location_intervals_validate_insert` | Solo Administración, o Enfermería con `RESIDENT_IDENTITY_CREATE`, da de alta |
| `RESIDENT_EPISODE_OVERLAP` | `resident_center_episodes_no_overlap` | Un residente no puede tener dos episodios de centro solapados |
| `RESIDENT_EPISODE_IMMUTABLE` | `resident_center_episodes_close_only`/`_no_delete` | Un episodio solo se cierra, nunca se reescribe ni se borra |
| `RESIDENT_EPISODE_NOT_ACTIVE` | `resident_location_intervals_validate_insert` | No se puede añadir ubicación a un episodio ya cerrado |
| `RESIDENT_LOCATION_OVERLAP` | `resident_location_intervals_validate_insert` | Un residente no puede tener dos ubicaciones vigentes solapadas |
| `RESIDENT_LOCATION_CONFIG_INVALID` | `resident_location_intervals_validate_insert` | Habitación/plaza deben respetar si el centro las habilita/exige |
| `RESIDENT_LOCATION_IMMUTABLE` | `resident_location_intervals_close_only`/`_no_delete` | Un intervalo de ubicación solo se cierra, nunca se reescribe ni se borra |
| `BASELINE_DRAFT_TRANSITION_INVALID` | `baseline_drafts_transition_guard` | Solo transiciones de estado válidas del borrador, con concurrencia optimista |
| `BASELINE_DRAFT_DELETE_FORBIDDEN` | `baseline_drafts_no_delete` | Un borrador nunca se borra físicamente |
| `BASELINE_DRAFT_AREA_IMMUTABLE` / `BASELINE_DRAFT_AREA_PARENT_NOT_ACTIVE` | triggers `baseline_draft_areas_*` | Un área de borrador solo se edita mientras el borrador esté `ACTIVE` |
| `BASELINE_DRAFT_BARTHEL_IMMUTABLE` / `BASELINE_DRAFT_BARTHEL_PARENT_NOT_ACTIVE` | triggers `baseline_draft_barthel_*` | Igual para la cabecera Barthel del borrador |
| `BASELINE_DRAFT_BARTHEL_ITEM_IMMUTABLE` / `BASELINE_DRAFT_BARTHEL_ITEM_PARENT_NOT_ACTIVE` | triggers `baseline_draft_barthel_items_*` | Igual para cada ítem Barthel del borrador |
| `BASELINE_OPEN_TEXT_INVALID` | `baseline_draft_areas_payload_validate_insert`/`_update` | Toda opción `OTRO`/`OTRA` exige texto, y solo si se eligió esa opción |
| `BASELINE_SIGN_NOT_AUTHORIZED` | `baseline_versions_validate_insert` | Grant, ámbito de unidad, permiso y ubicación vigente del firmante |
| `BASELINE_DRAFT_NOT_SIGNABLE` | `baseline_versions_validate_insert` | El borrador de origen debe existir, estar activo y coincidir en todos sus campos comunes |
| `BASELINE_VERSION_NUMBER_INVALID` | `baseline_versions_validate_insert` | Numeración de versión estrictamente correlativa |
| `BASELINE_AREAS_INCOMPLETE` | `baseline_versions_validate_insert` | Deben existir exactamente las 9 áreas del catálogo `BASAL_AREAS_V0_1` |
| `BASELINE_AREA_COMPONENTS_INCOMPLETE` | `baseline_versions_validate_insert` | Campos obligatorios de cada área no pueden quedar vacíos al firmar |
| `BASELINE_AREA_CATALOG_INVALID` | `baseline_versions_validate_insert` | Todo valor de cada área debe pertenecer a su catálogo cerrado |
| `BASELINE_BARTHEL_INCOMPLETE` | `baseline_versions_validate_insert` | Deben existir los 10 ítems Barthel con total exacto |
| `BASELINE_SIGNED_AREA_NOT_FROM_DRAFT` | `baseline_version_areas_copy_guard` | La versión firmada de un área debe copiar exactamente el borrador |
| `BASELINE_SIGNED_BARTHEL_NOT_FROM_DRAFT` | `baseline_version_barthel_copy_guard` | Igual para la cabecera Barthel firmada |
| `BASELINE_SIGNED_BARTHEL_ITEM_NOT_FROM_DRAFT` | `baseline_version_barthel_items_copy_guard` | Igual para cada ítem Barthel firmado |
| `BASELINE_CURRENT_INITIAL_INVALID` | `resident_current_baselines_validate_insert` | El primer puntero de vigente solo apunta a la versión 1 completa |
| `BASELINE_CURRENT_TRANSITION_INVALID` | `resident_current_baselines_validate_update` | El puntero solo avanza vía una supersesión válida y completa |
| `BASELINE_CURRENT_DELETE_FORBIDDEN` | `resident_current_baselines_no_delete` | El puntero de vigente nunca se borra |
| `BASELINE_SUPERSESSION_INVALID` | `baseline_supersessions_validate_insert` | La sucesión de versiones debe ser consecutiva y temporalmente coherente |
| `BASELINE_SUPERSESSION_IMMUTABLE` | `baseline_supersessions_no_update`/`_no_delete` | Una supersesión, una vez creada, es fija |
| `BASELINE_VERSION_IMMUTABLE` | `baseline_versions_no_update`/`_no_delete` | Una versión firmada nunca se edita ni se borra |
| `BASELINE_VERSION_AREA_IMMUTABLE` | `baseline_version_areas_no_update`/`_no_delete` | Igual para sus áreas |
| `BASELINE_VERSION_BARTHEL_IMMUTABLE` | `baseline_version_barthel_no_update`/`_no_delete` | Igual para su cabecera Barthel |
| `BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE` | `baseline_version_barthel_items_no_update`/`_no_delete` | Igual para cada ítem Barthel |
| `BASELINE_CONTRIBUTION_IMMUTABLE` / `BASELINE_CONTRIBUTION_PARENT_NOT_ACTIVE` | triggers `baseline_draft_contributions_*` | Las aportaciones al borrador son append-only y solo mientras esté activo |
| `PROFILE_SCOPE_IMMUTABLE`, `PROFILE_PERMISSION_IMMUTABLE`, `PROFILE_UNIT_SCOPE_IMMUTABLE`, `PROFILE_RESIDENT_SCOPE_IMMUTABLE` | triggers `profile_*_revoke_only`/`_no_delete` | Los grants de autorización solo se revocan, nunca se editan ni se borran |
| `IDEMPOTENCY_OPERATION_IMMUTABLE` | `idempotency_operations_transition_guard`/`_no_delete` | Una operación idempotente solo transiciona `IN_PROGRESS → SUCCEEDED` |
| `AUDIT_EVENT_IMMUTABLE` | `audit_events_no_update`/`_no_delete` | Un evento de auditoría es append-only |

## Qué implica para la migración a .NET 10

Este inventario no toma la decisión de cómo traducir cada categoría (constraint declarativo de SQL Server, validación en la capa de dominio/aplicación, o procedimiento almacenado equivalente) — esa decisión de mapeo se aborda en `instrucciones-migracion-net10.md`. Lo que este documento aporta es la garantía de que ninguna de estas 56 reglas se pierda por omisión durante la traducción: cada una tiene aquí su nombre de origen, su código de error y la invariante de negocio que protege.
