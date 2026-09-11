# ADR 0003. Aprobación de Cloudflare D1 y Drizzle ORM

**Proyecto:** Connect — Plataforma asistencial y de comunicación con familias  
**Identificador:** `ADR-0003-D1-DRIZZLE`  
**Versión:** 1.2
**Fecha efectiva:** 7 de septiembre de 2026
**Estado documental:** APROBADA; IMPLEMENTACIÓN VERIFICADA LOCALMENTE
**Decisión:** APROBACIÓN CON IMPLEMENTACIÓN LOCAL VERIFICADA
**Línea base funcional de referencia:** `LBF-CONNECT-2026-09-06-V1.1`  
**Ámbito autorizado por esta propuesta:** prototipo y validación técnica con datos exclusivamente ficticios  
**Ruta canónica:** `docs/architecture/0003-aprobacion-d1-drizzle.md`

> Esta decisión no autoriza datos personales o clínicos reales, un piloto real ni producción. Tampoco modifica la línea base funcional. Traduce sus invariantes a una decisión técnica revisable.

## 1. Decisión adoptada

Se aprueba:

1. **Cloudflare D1** como base de datos relacional del prototipo desplegado en Cloudflare Workers.
2. **Drizzle ORM y Drizzle Kit** como capa tipada y generador de migraciones SQL.
3. Una **base D1 separada por entorno** y, durante el prototipo ficticio, compartida lógicamente por varios centros con `center_id` obligatorio en todo agregado sujeto a centro.
4. Los principios, restricciones y modelo candidato de este ADR como dirección técnica para la primera vertical `Residente → ubicación → basal`.
5. Migraciones SQL versionadas, revisadas y aplicadas de forma progresiva; queda prohibido modificar directamente una base remota compartida mediante `drizzle-kit push`.

D1 y Drizzle están configurados y `0001` ya está integrada en `main`. La revisión SQL y las pruebas aplicables se realizaron exclusivamente sobre una D1 local desechable con datos sintéticos. `0001` es inmutable: toda evolución del esquema debe comenzar en `0002` o en una migración posterior. Su aplicación remota continúa prohibida.
## 2. Dictamen ejecutivo

| Nivel | Decisión | Alcance |
| --- | --- | --- |
| Elección D1 + Drizzle | **APROBADA** | Prototipo con datos ficticios |
| Topología del prototipo | **APROBADA** | Una D1 por entorno; multicentro lógico con `center_id` |
| Diseño lógico Residente/Basal | **APROBADO COMO DIRECCIÓN TÉCNICA** | Su traducción física debe superar gates y revisión |
| Preparación de scaffolding y esquema | **INTEGRADA** | Dependencias, configuración, binding y repositorios versionados |
| Migración física `0001` | **INTEGRADA Y VERIFICADA LOCALMENTE** | 29 tablas, 56 índices, 56 triggers; SQL y pruebas en verde |
| Flujos `D1-P04` a `D1-P06` | **DIFERIDOS** | No se implementan en la primera entrega sin decisión funcional |
| Uso con datos reales o producción | **NO APROBADO** | Requiere DPO/EIPD, seguridad, contratos, retención, RPO/RTO y revisión productiva |

La decisión fija la dirección técnica y sus límites; la primera migración ya está cerrada documentalmente sin ampliar el uso autorizado de D1.
## 3. Evidencia revisada en `main`

La línea base funcional `LBF-CONNECT-2026-09-06-V1.1` y sus artefactos congelados se preservan. La implementación se integró mediante el PR #1, fusionado en `main` con el commit `cc7e028b8fb4aa5ed881df9d3f8c57a773e541c9`.

| Elemento | Estado observado | Consecuencia |
| --- | --- | --- |
| Esquema Drizzle | Modular, presente en `db/schema/` | Contrato tipado alineado con la migración |
| `drizzle.config.ts` y dependencias | Configurados | Drizzle Kit disponible para comprobar y generar migraciones posteriores |
| Migración SQL | `0001_resident_baseline_foundation.sql` integrada | Primer historial físico versionado; no se reescribe |
| Binding D1 | `DB` configurado en `wrangler.jsonc` | Disponible solo para validación local autorizada |
| Repositorios de persistencia | Presentes en `db/repositories/` | Operaciones server-side y atómicas del bloque fundacional |
| Pruebas | Persistencia, dominio, autorización y documentación en verde | Restricciones, concurrencia e idempotencia verificadas localmente |
| Aplicación remota | Ausente | Sigue expresamente prohibida |

Fuentes internas revisadas:

- [`db/README.md`](https://github.com/cjfernandez94/connect-residencias/blob/main/db/README.md)
- [`package.json`](https://github.com/cjfernandez94/connect-residencias/blob/main/package.json)
- [`wrangler.jsonc`](https://github.com/cjfernandez94/connect-residencias/blob/main/wrangler.jsonc)
- [Contrato de datos Residente/Basal `0002`](https://github.com/cjfernandez94/connect-residencias/blob/main/docs/architecture/0002-contrato-datos-residente-basal.md)
- [Declaración de línea base funcional vigente](https://github.com/cjfernandez94/connect-residencias/blob/main/docs/product/baselines/2026-09-06-declaracion-linea-base-funcional-v1.1.md)
- [Política de autorización](https://github.com/cjfernandez94/connect-residencias/blob/main/lib/authorization/policy.ts)
- [Dominio basal](https://github.com/cjfernandez94/connect-residencias/blob/main/lib/domain/baseline/baseline.ts)
- [Pruebas de autorización Residente/Basal](https://github.com/cjfernandez94/connect-residencias/blob/main/tests/resident-baseline-authorization.test.ts)

## 4. Adecuación de D1 y Drizzle

### 4.1 Motivos para seleccionar D1

- Encaja con el destino actual Cloudflare Workers y evita introducir un backend de base de datos independiente durante el prototipo.
- Ofrece semántica SQL basada en SQLite, claves foráneas, índices, restricciones y operaciones por lotes transaccionales.
- Permite crear bases con jurisdicción `eu`; la jurisdicción debe fijarse al crear la base y no puede añadirse después.
- Incluye recuperación a un punto temporal mediante Time Travel, con la ventana dependiente del plan.
- El modelo de muchas bases pequeñas permite reconsiderar una topología por centro si una revisión productiva futura lo exige.

### 4.2 Motivos para seleccionar Drizzle

- Mantiene el esquema tipado junto al código TypeScript.
- Genera migraciones SQL inspeccionables y compatibles con revisión Git.
- Expone el driver `drizzle-orm/d1` para el binding de Workers.
- Permite expresar claves, índices y restricciones; para invariantes no cubiertas de forma fiable por su DSL se conservará SQL explícito en migraciones.

### 4.3 Límites aceptados

- Cada base D1 procesa escrituras de forma serial; el rendimiento depende de la duración de las consultas. El prototipo deberá medir carga antes de extrapolar a producción.
- Cada base tiene límites de tamaño, consulta y parámetros que deben verificarse contra la carga real.
- Los tipos TypeScript y los catálogos `enum` de Drizzle no sustituyen validación runtime ni restricciones `CHECK`.
- Time Travel no sustituye una política aprobada de conservación, exportación, restauración y continuidad.
- La compatibilidad técnica y el cifrado gestionado por el proveedor no constituyen por sí solos cumplimiento normativo sanitario o del RGPD.

Referencias oficiales:

- [Cloudflare D1](https://developers.cloudflare.com/d1/)
- [API D1 y atomicidad de `batch()`](https://developers.cloudflare.com/d1/worker-api/d1-database/)
- [Claves foráneas en D1](https://developers.cloudflare.com/d1/sql-api/foreign-keys/)
- [Ubicación y jurisdicción de datos D1](https://developers.cloudflare.com/d1/configuration/data-location/)
- [Límites de D1](https://developers.cloudflare.com/d1/platform/limits/)
- [Time Travel y restauración](https://developers.cloudflare.com/d1/reference/time-travel/)
- [Drizzle con Cloudflare D1](https://orm.drizzle.team/docs/get-started/d1-new)
- [Índices y restricciones de Drizzle](https://orm.drizzle.team/docs/indexes-constraints)
- [Migraciones con Drizzle Kit](https://orm.drizzle.team/docs/migrations)

## 5. Topología aprobable para el prototipo

### 5.1 Propuesta

- `local`: SQLite/D1 local de Wrangler, descartable y con fixtures ficticios.
- `development`: D1 remota propia, con jurisdicción `eu`.
- `staging`: D1 remota propia, con jurisdicción `eu`.
- `production`: fuera de esta aprobación; no se crea ni recibe datos hasta la revisión productiva.
- Binding estable en código: `DB`.
- Una misma base de prototipo puede contener varios centros ficticios, pero toda fila sujeta a centro y toda consulta autorizada deben conservar y filtrar `center_id`.
- No se activa replicación de lectura en el primer bloque.

### 5.2 Justificación

Una base compartida reduce complejidad de migración, pruebas y consultas multicentro durante el prototipo. La columna de partición lógica y las claves compuestas mantienen el diseño migrable hacia bases por centro.

La decisión **base compartida frente a base por centro para datos reales queda expresamente abierta**. Deberá resolverse con DPO, responsable técnico y centro piloto considerando aislamiento, operaciones, usuarios multicentro, auditoría, restauración y ausencia de transacciones entre bases.

## 6. Principios obligatorios del esquema

1. Identificadores opacos UUID v4 en `TEXT`, generados por el servidor.
2. Fechas y horas generadas por servidor, almacenadas como UTC en formato canónico.
3. `center_id` no se deduce de datos enviados por cliente; procede del recurso y del grant autorizado.
4. Claves foráneas activas, `ON DELETE RESTRICT` como regla general y sin borrado en cascada de historia clínica o auditoría.
5. Catálogos funcionales reforzados mediante `CHECK`, no solo mediante tipos TypeScript.
6. Unicidades críticas reforzadas mediante índices únicos, incluidos índices parciales cuando proceda.
7. Registros firmados, históricos, aportaciones y auditorías protegidos contra `UPDATE` y `DELETE` mediante SQL explícito.
8. Las transiciones de estado se ejecutan en repositorios server-side; ningún formulario proporciona autoría, perfil, centro, unidad o fecha/hora confiables.
9. Toda operación crítica utiliza clave idempotente y conserva su resultado.
10. Ninguna consulta clínica se autoriza por conocer un identificador.
11. No se persiste edad; se deriva de fecha de nacimiento y fecha de consulta.
12. CFS y Pfeiffer no existen en tablas, columnas, fixtures ni migraciones.

## 7. Modelo físico candidato del primer bloque

El diseño separa contenido mutable de borrador y versiones firmadas inmutables. Así, sustituir la versión vigente no exige reescribir el contenido firmado anterior.

### 7.1 Organización, identidad y autorización

| Tabla candidata | Responsabilidad | Restricciones esenciales |
| --- | --- | --- |
| `centers` | Centro provisionado por plataforma | PK opaca; código único; estado explícito |
| `units` | Unidad subordinada al centro | FK a centro; `UNIQUE(center_id, code)`; baja lógica |
| `accounts` | Cuenta autenticable localmente referenciada | Sujeto externo único; estado activo/suspendido; sin secretos de autenticación |
| `profile_scopes` | Asignación de un perfil a una cuenta y centro | Un solo perfil por fila; concesión y revocación trazables; sin perfil activo global |
| `profile_unit_scopes` | Unidades autorizadas dentro del grant | Unidad del mismo centro; concesión/revocación; sin comodines implícitos |
| `profile_resident_scopes` | Restricción o vínculo individual cuando proceda | Residente dentro del ámbito; no sustituye autorización familiar |
| `profile_permissions` | Permisos configurables del mismo grant | Catálogo cerrado; concesión/revocación append-only; un permiso activo por grant/código |

Las claves compuestas o restricciones equivalentes deberán impedir unir un `unit_id` de un centro con un grant de otro. El perfil activo permanece en la sesión validada y se registra en cada operación auditada; no se convierte en una capacidad acumulativa de la cuenta.

### 7.2 Residente y ubicación longitudinal

| Tabla candidata | Responsabilidad | Restricciones esenciales |
| --- | --- | --- |
| `residents` | Identidad estable del residente | PK opaca; nombre, fecha de nacimiento; sexo `male/female/other/unknown` documentado y no inferido; estado técnico |
| `resident_center_episodes` | Asociación temporal del residente con un centro | Centro, identificador interno del episodio y fechas; máximo un episodio activo |
| `resident_location_intervals` | Ubicación temporal dentro de centro/unidad | Unidad obligatoria; edificio/planta/habitación/plaza según configuración; máximo un intervalo activo; no solapamiento |

La ubicación actual se deriva del intervalo abierto. Un traslado cierra el intervalo previo y abre uno nuevo en una misma operación. Los registros de estructura usados históricamente se inactivan, no se borran.

### 7.3 Borrador basal mutable

| Tabla candidata | Responsabilidad | Restricciones esenciales |
| --- | --- | --- |
| `baseline_drafts` | Cabecera y propiedad del borrador | Máximo un borrador `ACTIVE` por residente; creador y perfil inmutables; revisión optimista |
| `baseline_draft_areas` | Nueve áreas editables | `UNIQUE(draft_id, area_code)`; catálogo exacto de nueve áreas; payload versionado y validado |
| `baseline_draft_barthel` | Cabecera Barthel común | Un registro por borrador; instrumento `BARTHEL_COMUN_V0_1` |
| `baseline_draft_barthel_items` | Diez respuestas y puntuaciones | `UNIQUE(barthel_id, item_code)`; opción/puntuación válidas; exactamente diez para firmar |
| `baseline_draft_contributions` | Autoría individual de aportaciones | Append-only; cuenta, perfil, ámbito, componente, revisión y hora de servidor |

Estados físicos propuestos para borrador: `ACTIVE`, `CANCELLED`, `SIGNED`. Solo `ACTIVE` admite edición. La propiedad de firma no cambia cuando otro profesional aporta.

### 7.4 Versión basal firmada e inmutable

| Tabla candidata | Responsabilidad | Restricciones esenciales |
| --- | --- | --- |
| `baseline_versions` | Cabecera firmada | `UNIQUE(resident_id, version_number)`; autor y firmante idénticos; motivo, fuente y fecha obligatorios |
| `baseline_version_areas` | Copia firmada de las nueve áreas | Nueve filas por versión; `UNIQUE(version_id, area_code)`; inmutable |
| `baseline_version_barthel` | Resultado Barthel firmado | Instrumento y total automático validado; inmutable |
| `baseline_version_barthel_items` | Diez respuestas firmadas | Diez filas; opción y puntuación; inmutable |
| `resident_current_baselines` | Puntero a la única versión vigente | `resident_id` PK; `baseline_version_id` único |
| `baseline_supersessions` | Enlace entre versión sustituida y nueva | Anterior y nueva del mismo residente; cada anterior se sustituye una sola vez |

La condición vigente/histórica se deriva del puntero y del enlace de sustitución. El contenido firmado no cambia al publicar una nueva versión. Una rectificación apunta a la versión vigente que pretendía corregir y siempre crea un borrador y una versión nuevos.

### 7.5 Operaciones y auditoría

| Tabla candidata | Responsabilidad | Restricciones esenciales |
| --- | --- | --- |
| `idempotency_operations` | Reintentos seguros | `UNIQUE(account_id, action_code, operation_id)`; hash de petición y resultado estable |
| `audit_events` | Escrituras y accesos auditables | Append-only; actor, perfil, centro, unidad, residente, recurso, acción, finalidad y hora |

La lectura clínica detallada de Dirección se registra en `audit_events` antes de devolver contenido. Si la escritura de auditoría falla, la lectura falla cerrada.

## 8. Restricciones e índices mínimos

La migración `0001` deberá incluir, como mínimo:

- `UNIQUE(units.center_id, units.code)`.
- máximo un `resident_center_episode` activo por residente.
- máximo un `resident_location_interval` activo por residente.
- máximo un `baseline_draft` activo por residente.
- `UNIQUE(baseline_versions.resident_id, baseline_versions.version_number)`.
- un único puntero vigente por residente y una versión vigente no compartida entre residentes.
- `UNIQUE` de área por borrador/versión.
- `UNIQUE` de ítem Barthel por evaluación.
- `CHECK` de estados, perfiles, permisos, sexo `male/female/other/unknown`, nueve áreas, ayuda técnica sin `NO_APLICA`, aplicabilidad enteral, textos `OTRO/OTRA`, motivos basales, fuente, categorías cognitivas, GDS 1–7 cuando conste y catálogo Barthel.
- coherencia temporal: fin posterior al inicio; firma no anterior a creación.
- claves foráneas de ámbito y pertenencia a centro.
- índices de consulta que comiencen por `center_id` y continúen por `unit_id`, `resident_id`, estado o fecha según la consulta.
- triggers que rechacen `UPDATE` y `DELETE` en versiones firmadas, respuestas firmadas, aportaciones y auditoría.

La completitud transversal —nueve áreas, diez ítems y total Barthel reproducible— se valida dentro de la operación atómica de firma y se cubre con pruebas de integración. No debe confiarse únicamente a la interfaz.

## 9. Firma basal atómica e idempotente

La operación `signBaselineDraft` deberá:

1. Resolver cuenta, perfil activo y grants desde fuentes server-side.
2. Reclamar o recuperar la clave idempotente.
3. Verificar residente activo, ubicación vigente, borrador `ACTIVE`, revisión esperada y creador firmante.
4. Validar nueve áreas, fuente/fecha, condicionales y diez ítems Barthel.
5. Insertar cabecera y contenido de la versión firmada.
6. Insertar el enlace de sustitución cuando exista una vigente previa.
7. Crear o actualizar el puntero de vigente con condición sobre la versión esperada.
8. Marcar el borrador como `SIGNED` y asociarlo a la versión creada.
9. Escribir auditoría y completar el resultado idempotente.

Todas las sentencias forman un único `D1Database.batch()`. D1 documenta que los lotes se ejecutan secuencialmente y que un fallo aborta o revierte la secuencia completa. Los UUID se generan antes del lote para no depender de resultados intermedios.

Dos firmas concurrentes del mismo borrador deben producir una sola versión. La segunda recibe el resultado idempotente existente o un conflicto controlado, nunca una segunda versión.

## 10. Migraciones y estructura de archivos

Estructura integrada:

```text
db/
  client.ts
  schema/
    index.ts
    organization.ts
    authorization.ts
    residents.ts
    baseline.ts
    audit.ts
  migrations/
    0001_resident_baseline_foundation.sql
  repositories/
    resident-repository.ts
    baseline-repository.ts
    authorization-subject-repository.ts
    audit-repository.ts
drizzle.config.ts
```

Cambios técnicos integrados:

- `drizzle-orm` y `drizzle-kit` están instalados en sus ámbitos correspondientes;
- `drizzle.config.ts` usa dialecto SQLite, esquema modular y salida en `db/migrations`;
- `wrangler.jsonc` declara el binding `DB` y el directorio de migraciones;
- existen scripts `db:generate`, `db:check`, `db:migrate:local` y `db:test`; no existe script de aplicación remota;
- se conserva un único diario de migraciones; Wrangler es el aplicador local y Drizzle Kit el generador/comprobador;
- `drizzle-kit push` contra bases remotas compartidas está prohibido;
- `0001` no se edita: todo cambio posterior crea `0002` o una migración posterior.

Cada SQL generado se revisa manualmente, especialmente reconstrucciones de tablas y tratamiento de claves foráneas. D1 mantiene las claves foráneas activas y durante migraciones solo permite diferir su comprobación, no desactivarla de forma persistente.

## 11. Pruebas de aceptación de la persistencia

La siguiente tabla conserva los criterios que se demostraron durante la validación local:

| ID | Prueba | Resultado exigido |
| --- | --- | --- |
| `DB-T01` | Migrar una base local vacía | Éxito reproducible |
| `DB-T02` | Aplicar de nuevo sin migraciones nuevas | Sin cambios ni duplicados |
| `DB-T03` | `PRAGMA foreign_key_check` | Cero violaciones |
| `DB-T04` | Insertar centro/unidad cruzados | Rechazo |
| `DB-T05` | Abrir dos ubicaciones activas | Rechazo |
| `DB-T06` | Abrir dos borradores activos | Rechazo |
| `DB-T07` | Firmar con cuenta o perfil distinto del creador | Rechazo |
| `DB-T08` | Firmar con área o ítem Barthel incompleto | Rechazo sin estado parcial |
| `DB-T09` | Dos firmas concurrentes | Una sola versión vigente |
| `DB-T10` | Reintentar la misma firma | Mismo resultado, sin duplicado |
| `DB-T11` | Modificar o borrar versión firmada | Rechazo por base de datos |
| `DB-T12` | Sustituir una vigente | Nueva vigente; anterior intacta e histórica |
| `DB-T13` | Leer detalle como Dirección sin auditoría | Fallo cerrado |
| `DB-T14` | Modificar o borrar auditoría/aportación | Rechazo |
| `DB-T15` | Manipular centro, unidad, residente o perfil | Denegación sin fuga de existencia |
| `DB-T16` | Ejecutar `pnpm check` | Typecheck, lint, tests y ambos builds en verde |

Los fixtures serán sintéticos y no contendrán nombres, fechas, textos clínicos ni identificadores reales.

## 12. Autorización y puertas de la migración `0001`

### 12.1 Coherencia documental

La decisión `DEC-CONNECT-2026-09-06-D1-P01-P08` y la línea base incremental `LBF-CONNECT-2026-09-06-V1.1` cierran los dos bloqueos funcionales de la primera migración. El commit y el tag de `LBF-CONNECT-2026-09-05-V1` permanecen intactos como evidencia histórica.

### 12.2 Estado de D1-P01 a D1-P08

| ID | Decisión | Estado | Efecto |
| --- | --- | --- | --- |
| `D1-P01` | Sexo `male/female/other/unknown`, fuente administrativa, sin inferencia o texto libre | **APROBADA** | Incorporar a identidad y pruebas negativas |
| `D1-P02` | UUID global + código operativo único dentro del episodio | **APROBADA** | Incorporar a `0001` |
| `D1-P03` | Habitación/plaza nulas; obligatoriedad según configuración versionada | **APROBADA** | Incorporar a `0001` |
| `D1-P04` | Traslado e inactivación/reactivación | **DIFERIDA** | Sin flujo operativo; denegación por defecto |
| `D1-P05` | Cancelación excepcional por ausencia del creador | **DIFERIDA** | Sin actor ni grant operativo |
| `D1-P06` | Inicio de rectificación basal | **DIFERIDA** | Sin botón, endpoint ni permiso operativo |
| `D1-P07` | Aportaciones append-only sin transferencia de firma | **APROBADA COMO SOPORTE** | El esquema puede soportarla; la interfaz puede aplazarse |
| `D1-P08` | `NINGUNA`/`NO_DOCUMENTADO`, enteral y textos `OTRO/OTRA` | **APROBADA** | Incorporar a validación de firma y pruebas |

### 12.3 Trabajo realizado y límite vigente

Se instalaron y configuraron D1/Drizzle, el cliente, el esquema modular, los repositorios y la migración `0001_resident_baseline_foundation.sql`. `0001` se aplicó solo a una D1 local desechable con fixtures sintéticos para ejecutar y documentar `DB-T01`–`DB-T16`, pruebas adicionales y `pnpm check`.

### 12.4 Aceptación y prohibiciones

`0001` fue aceptada tras la revisión SQL y la superación de todas las pruebas aplicables. Permanece inmutable: no se corrige ni se regenera; cualquier cambio futuro del esquema debe hacerse mediante `0002` o una migración posterior.

Continúa prohibido:

- aplicar `0001` a development, staging o cualquier D1 remota;
- usar `drizzle-kit push` contra bases compartidas;
- introducir datos personales o clínicos reales;
- implementar `D1-P04`–`D1-P06` por inferencia;
- iniciar piloto o producción.

### 12.5 Resultado de implementación

- **PR integrado:** [PR #1](https://github.com/cjfernandez94/connect-residencias/pull/1).
- **Merge commit:** `cc7e028b8fb4aa5ed881df9d3f8c57a773e541c9` en `main`.
- **Migración:** `0001_resident_baseline_foundation.sql`, con 29 tablas, 56 índices y 56 triggers.
- **Integridad del SQL:** SHA-256 final `39B05B14AB2E9A31C2396A2D0665A36207D735C4921FD752B97C8C408AFA2EE8`.
- **Validación:** 28/28 pruebas de persistencia y 138/138 pruebas generales superadas; incluye `DB-T01`–`DB-T16` y pruebas adicionales.
- **Límite no alterado:** la aplicación de `0001` queda autorizada exclusivamente en D1 local desechable con datos sintéticos. No se autoriza ninguna migración remota, datos reales, piloto ni producción.

## 13. Condiciones antes de datos reales

Fuera del alcance de `0001`, pero obligatorias antes de un piloto real:

- EIPD y validación del DPO.
- Responsables, encargados, contratos, bases jurídicas y derechos.
- Autenticación productiva, segundo factor, recuperación y revocación.
- Modelo de amenazas, pruebas de seguridad y gestión de vulnerabilidades.
- Política de retención, rectificación, eliminación, exportación y acceso a auditoría.
- RPO, RTO, plan de copias y ensayo documentado de restauración.
- Decisión de base compartida o por centro para el piloto real.
- Revisión del proveedor, jurisdicción `eu`, transferencias y servicios auxiliares.
- Observabilidad sin texto clínico completo ni secretos.
- Pruebas de carga y verificación de límites D1.

## 14. Alternativas consideradas

| Alternativa | Ventaja | Coste o riesgo | Decisión propuesta |
| --- | --- | --- | --- |
| D1 + Drizzle | Integración natural con Workers, SQL revisable y tipado | Límites SQLite/D1 y disciplina extra para invariantes críticas | **Seleccionar para prototipo** |
| D1 sin Drizzle | Menos abstracción | Más SQL manual y menos contrato tipado | Rechazar |
| PostgreSQL gestionado | Más capacidad transaccional, consulta y operación madura | Nueva infraestructura, conexión y mayor complejidad ahora | Reconsiderar antes de producción si D1 no supera gates |
| Una D1 por centro desde el prototipo | Aislamiento físico temprano | Multiplica migraciones, operaciones y complejidad multicentro | Aplazar a revisión de piloto real |
| Persistencia en navegador | Implementación rápida | Incompatible con seguridad, concurrencia y trazabilidad | Prohibida |

## 15. Riesgos y mitigaciones

| Riesgo | Mitigación obligatoria |
| --- | --- |
| Cruce de centros | `center_id`, FKs compuestas, filtro server-side, pruebas negativas |
| Dos borradores o basales vigentes | Índices únicos y conflicto controlado |
| Firma parcial | Único lote transaccional |
| Reintento duplicado | Tabla de idempotencia y respuesta estable |
| Mutación de historia | Separación borrador/versión, triggers append-only |
| Deriva entre Drizzle y SQL | Generación, revisión, `drizzle-kit check` y prueba desde base vacía |
| Migración incompatible con FK D1 | Revisión de SQL y `PRAGMA defer_foreign_keys` solo cuando sea necesario |
| Base compartida sobredimensionada | Métricas, índices, pruebas de carga y puerta de topología antes del piloto real |
| Recuperación insuficiente | Time Travel más exportación/restore ensayados según RPO/RTO futuro |
| Documentación obsoleta | PR editorial previo y prueba automatizada de fuentes canónicas |

## 16. Texto formal de aprobación actualizado

> Se aprueban Cloudflare D1 y Drizzle ORM para el prototipo Connect con datos exclusivamente ficticios. La migración `0001`, integrada en `main`, ha superado la revisión SQL y las pruebas locales sintéticas, pero solo puede aplicarse a una D1 local desechable. `0001` es inmutable y cualquier evolución deberá utilizar `0002` o una migración posterior; la aplicación remota permanece prohibida. `D1-P04`, `D1-P05` y `D1-P06` permanecen diferidas y denegadas por defecto. No se autorizan datos reales, piloto real ni producción.

## 17. Registro de decisión

| Campo | Valor |
| --- | --- |
| Decisión | D1 + Drizzle configurados; `0001` integrada y verificada solo en local |
| Responsable de producto | Aprobación expresa el 6 de septiembre de 2026 |
| Responsable técnico | Debe revisar implementación, SQL y resultados de pruebas |
| DPO / privacidad | Obligatorio antes de datos reales y topología productiva |
| Fecha efectiva | 6 de septiembre de 2026 |
| Línea base | `LBF-CONNECT-2026-09-06-V1.1` |
| Entorno permitido para `0001` | D1 local desechable con datos sintéticos |
| Entornos no autorizados | D1 remota, piloto y producción |
| Evidencia de aceptación | SQL revisado; 28/28 persistencia; 138/138 pruebas generales; `pnpm check` |
| Siguiente puerta | Primer bloque vertical; cualquier cambio físico empieza en `0002` |

## 18. Efecto sobre la línea base funcional

Esta decisión es técnica y subordinada a `LBF-CONNECT-2026-09-06-V1.1`. La revisión 1.1 incorpora la decisión funcional aprobada y no modifica ni reetiqueta la línea base histórica v1.

Si durante el diseño físico aparece una necesidad de cambiar significado, permiso, estado, flujo, pantalla o criterio de prueba, se detiene esa parte y se tramita mediante el control de cambios funcional. Solo entonces sería necesaria una nueva línea base.

---

**Fin de `ADR-0003-D1-DRIZZLE` v1.1.**
