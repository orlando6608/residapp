# 0001. Modelo mínimo para Residente y estado basal

- **Estado:** alineado con `LBF-CONNECT-2026-09-06-V1.1`; D1/Drizzle aprobados para validación local ficticia
- **Fecha original:** 3 de septiembre de 2026
- **Alineación documental:** 6 de septiembre de 2026
- **Ámbito:** primer bloque vertical Residente/Basal, exclusivamente con datos ficticios
- **Fuentes vigentes:** línea base `LBF-CONNECT-2026-09-05-V1`, PRD v0.4, matriz de permisos v0.2, wireframes canónicos y `AGENTS.md` v1.3

Este registro técnico no sustituye ni modifica las fuentes funcionales. Si aparece una contradicción, prevalece la jerarquía documentada en `docs/README.md`.

## Decisión mínima

Separar cuatro conceptos antes de introducir persistencia:

1. **Estructura autorizada:** Centro y Unidad delimitan el aislamiento operativo. Edificio y planta son opcionales; habitación y plaza/cama forman parte de la ubicación administrativa.
2. **Cuenta y perfil activo:** una Cuenta puede tener varias asignaciones de perfil, pero cada petición contiene un único perfil activo que debe pertenecer a esas asignaciones. Los permisos de perfiles distintos no se combinan.
3. **Residente administrativo:** identidad, centro, unidad y ubicación son datos administrativos. El alta deja el basal pendiente y no equivale a una valoración profesional.
4. **Versión basal:** ciclo explícito `BORRADOR -> FIRMADO_VIGENTE -> HISTORICO`. Solo una versión puede estar vigente por residente; reemplazarla debe firmar la nueva y archivar la anterior dentro de una misma operación transaccional.

La edad será un dato derivado de la fecha de nacimiento y la fecha de consulta, no un valor persistido. Los identificadores serán opacos y no secuenciales; conocer un identificador nunca autoriza el acceso.

## Contratos implementados ahora

- Los seis perfiles del sistema y el requisito de perfil activo explícito.
- Referencias tipadas de Cuenta, Centro, Unidad, Residente y Versión basal.
- Identidad administrativa mínima: nombre, fecha de nacimiento, sexo documentado y ubicación.
- Sexo documentado: catálogo cerrado `male`, `female`, `other`, `unknown`; fuente administrativa, sin inferencia ni texto libre.
- Las nueve áreas basales del PRD y las cuatro categorías documentadas de cognición.
- Cabecera versionada del basal, firma profesional y sustitución trazable de la versión vigente.
- Política RBAC/ABAC para identidad y basal con cuenta activa, perfil activo, centro, unidad, residente, permiso específico, autorización familiar y obligación de auditoría para Dirección Clínica.

No se implementa contenido clínico de ejemplo, persistencia en navegador ni datos reales.

## Propuesta de persistencia para revisión

Esta propuesta define el mínimo que deberá revisarse antes de crear `db/schema.ts` o una migración Drizzle.

| Entidad propuesta | Campos mínimos | Relaciones y restricciones |
| --- | --- | --- |
| `centers` | `id`, `name` | `id` opaco y único. |
| `units` | `id`, `center_id`, `name` | FK a Centro; unicidad de nombre dentro del centro. |
| `accounts` | `id`, `status` | Estado explícito; una cuenta suspendida no opera. |
| `account_profile_scopes` | `id`, `account_id`, `profile`, `center_id` | Unicidad por cuenta, perfil y centro. El perfil activo no se guarda aquí como permiso global: procede del contexto autenticado de la petición. |
| `account_unit_scopes` | `profile_scope_id`, `unit_id` | La Unidad debe pertenecer al mismo Centro del ámbito de perfil. |
| `account_resident_scopes` | `profile_scope_id`, `resident_id` | Materializa asignaciones o vínculos autorizados sin sustituir la autorización familiar. |
| `profile_permissions` | `profile_scope_id`, `permission` | Solo permisos configurables definidos por producto; sin comodines implícitos. |
| `residents` | `id`, `center_id`, identificador interno, nombre, `birth_date`, `documented_sex`, estado | FK a Centro; identificador opaco; la edad no se persiste; la ubicación vigente no se duplica en esta entidad. |
| `resident_locations` | `id`, `resident_id`, `unit_id`, edificio/planta/habitación/plaza cuando apliquen, inicio, fin, autoría | Un único intervalo vigente por residente; cambiar ubicación cierra el intervalo actual y abre otro sin sobrescribir el histórico. |
| `baseline_versions` | `id`, `resident_id`, `version_number`, `status`, `reason`, `created_by`, `created_profile`, `created_at`, `signed_by`, `signed_profile`, `signed_at`, `superseded_by_id`, `superseded_at` | Unicidad por residente y número de versión; como máximo una fila `FIRMADO_VIGENTE` por residente; las firmadas no se sobrescriben. |
| `baseline_area_entries` | `baseline_version_id`, `catalog_version`, `area`, valor estructurado, fuente y fecha cuando procedan | Un registro por cada una de las nueve áreas y versión; catálogo `BASAL_AREAS_V0_1`; sin puntuación conjunta. |
| `baseline_cognition` | `baseline_version_id`, `category`, `etiology`, `gds`, `source`, `source_date` | Etiología y GDS solo si constan; conserva fuente y fecha. |
| `baseline_barthel_items` | `baseline_version_id`, `catalog_version`, `item`, `option_code`, `score` | Diez ítems de `BARTHEL_COMUN_V0_1`, puntuación reproducible y total automático sobre 100; no guardar solo el total. |

CFS y Pfeiffer quedan excluidos del producto estructurado por la línea base v1. No se crearán campos, permisos, activos ni catálogos provisionales para recuperarlos.

### Restricciones e índices previstos

- Índices de ámbito por `center_id`, `unit_id` y `resident_id` para filtrar antes de recuperar contenido.
- Índices o mecanismos transaccionales equivalentes para garantizar un único `BORRADOR`, una única versión `FIRMADO_VIGENTE` y un único intervalo de ubicación vigente por residente.
- Índice único `(resident_id, version_number)` y enlace explícito `superseded_by_id`.
- FKs o comprobaciones equivalentes que impidan asociar una Unidad o un Residente a otro Centro.
- Firma y sustitución del basal idempotentes, con clave de operación y transacción en el futuro repositorio.
- Auditoría append-only para escrituras y para cada lectura clínica detallada de Dirección; su diseño transversal se cerrará antes de persistir.

### Secuencia de migración prevista

1. Resolver las decisiones administrativas aún abiertas y aprobar una propuesta física concreta contra la línea base v1.
2. Crear una primera migración versionada de estructura, cuentas, ámbitos, residentes y basal.
3. Añadir repositorios server-side y fixtures exclusivamente ficticios.
4. Validar aislamiento por centro/unidad/residente, una sola versión vigente, idempotencia y sustitución concurrente.
5. Conectar la interfaz solo después de que las políticas y los repositorios estén probados.

## Contradicciones funcionales resueltas por la línea base v1

- Barthel queda fijado como `BARTHEL_COMUN_V0_1`, con diez respuestas y total automático sobre 100.
- Las nueve áreas basales usan `BASAL_AREAS_V0_1` y no generan una puntuación conjunta.
- CFS y Pfeiffer quedan fuera del producto estructurado.
- El basal admite un único borrador activo por residente, firma exclusiva del creador, versiones firmadas inmutables y rectificación mediante nueva versión vinculada.
- La ubicación actual deriva del único intervalo vigente del historial.
- La ventana de seis horas se limita a cursos/notas clínicas habilitados y nunca se aplica al basal.

Estas reglas proceden de `LBF-CONNECT-2026-09-06-V1.1` y no deben reabrirse durante el diseño D1/Drizzle.

## Decisiones cerradas para 0001

- `D1-P01`: sexo documentado usa `male`, `female`, `other` y `unknown`.
- `D1-P02`: UUID técnico global y código operativo único dentro del episodio del centro.
- `D1-P03`: habitación/plaza son nulas en base y su obligatoriedad se valida según configuración versionada del centro.
- `D1-P08`: `NINGUNA`/`NO_DOCUMENTADO` en ayuda técnica; `NO_APLICA` solo para alimentación exclusivamente enteral; texto obligatorio para `OTRO/OTRA`.

La creación y aplicación de `0001` quedan autorizadas exclusivamente en una D1 local desechable con datos sintéticos para ejecutar `DB-T01`–`DB-T16`. La revisión del SQL y el resultado de las pruebas condicionan su aceptación; no se autoriza aplicación remota.

## Decisiones que permanecen fuera de la primera entrega

- `D1-P04`: traslado e inactivación/reactivación.
- `D1-P05`: cancelación excepcional por ausencia del creador.
- `D1-P06`: inicio de rectificación basal.
- Política productiva de autenticación, segundo factor, conservación y auditoría antes de utilizar datos reales.

Estas decisiones no bloquean la validación local de `0001`. Sí bloquean las funciones correspondientes y cualquier uso con datos reales o entorno remoto hasta su aprobación expresa.
