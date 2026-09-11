# Frontera servidor–autorización–D1

Implementación técnica del 7 de septiembre de 2026, subordinada a `LBF-CONNECT-2026-09-06-V1.1`
y ADR 0003. Rama `feat/server-auth-d1-boundary`, creada desde `main` limpio y sincronizado
con `origin/main` en `a105d3e7cd0a167262a607eb62788357865ed428`.

## Arquitectura y responsabilidades

```text
SessionIdentityProvider, ligado a la petición
  -> externalSubject verificado por el adaptador
  -> authorization-subject-repository: cuenta ACTIVE + profile_scope seleccionado
  -> centro/unidad activos + ubicación/episodio vigente + restricción individual + permisos
  -> request-context: política pura + token opaco ligado a una operación
  -> resident-baseline-service: consume solo el ejecutor específico autorizado
  -> request-context: comprueba token/clase y llama al único repositorio correspondiente
  -> authorized-d1: revalidación de la misma evidencia dentro del batch
  -> repositorios existentes + triggers inmutables de 0001
  -> resultado tipado o error externo normalizado
```

El puerto de sesión devuelve únicamente `externalSubject`. Su implementación definitiva deberá
verificar autenticidad, caducidad y revocación de sesión antes de devolverlo. No se implementan
login, contraseñas, MFA ni proveedor productivo. `accounts.external_subject`, ya único en D1,
resuelve la cuenta sin recibir su ID desde el cliente. Solo `ACTIVE` opera; `SUSPENDED` es
el estado de desactivación de cuenta que admite `0001`. La revocación de perfil utiliza
`profile_scopes.status = REVOKED`; no se inventa un tercer estado de cuenta.

La selección explícita incluye `profileScopeId` y `centerId`. Ambos son selectores no confiables;
D1 comprueba pertenencia a la cuenta, coincidencia de centro y concesión activa. No se escoge
automáticamente un perfil ni se agregan los de la cuenta. En el alta se valida `unitId` contra
el grant. En firma y lectura la unidad procede de la ubicación vigente del residente. La
proyección recupera exclusivamente metadatos de autorización, no texto clínico ni identidades
administrativas del residente. La firma además exige que cuenta y perfil sean los del creador.

`request-context.ts` convierte esa proyección en el contrato de la política existente, sin
alterar sus decisiones. Resuelve solo el ámbito de la operación solicitada. El permiso basal
se obtiene del motivo persistido del borrador: `ALTA` exige `BASELINE_INITIAL_COMPLETE`; revisión
programada o cambio consolidado exigen `BASELINE_REEVALUATE`. No se acepta el permiso requerido
desde el cliente. Dirección exige `CLINICAL_DETAIL_READ`, finalidad `SUPERVISION_CLINICA` y la
obligación de auditoría. No se habilitan `D1-P04`–`D1-P06` ni nuevas funciones de los perfiles.

El token devuelto es un objeto vacío, congelado y sin prototipo ni propiedades reflectables.
Un `WeakMap` privado conserva acción, perfil seleccionado, cuenta, centro, unidad, residente,
borrador en SIGN y obligación de recurso/finalidad en READ, junto con la conexión revalidada.
Una marca TypeScript distingue CREATE/SIGN/READ y cada ejecutor comprueba en runtime la instancia
auténtica y la clase concedida. Copiar, deserializar o forzar un cast no transfiere autoridad.
Los servicios resuelven un contexto nuevo en cada llamada y no aceptan contexto, cuenta, perfil
funcional, permisos, cabeceras, cookies ni identidad externa como atributos del comando.
Rechazan campos adicionales y copian los datos antes del primer `await`.

### Cierre del bypass detectado en PR #3

Se elimina `databaseForRequest`: ningún consumidor puede extraer D1 del token. La resolución y
los tres ejecutores residen en el mismo módulo para mantener privado el registro de capacidades.
Cada ejecutor tiene una llamada fija al repositorio permitido; no existe callback, selector de
repositorio ni entrada SQL. Una capacidad READ solo ejecuta la lectura con su auditoría obligatoria;
esa escritura técnica no concede un permiso genérico de INSERT. SIGN solo firma el borrador
capturado. CREATE solo crea en el ámbito autorizado. Los campos de autoridad se reconstruyen
desde el registro privado, sin propagar campos adicionales del payload.

`authorized-d1` conserva el adaptador transaccional ya probado, accesible solo por este ejecutor.
El servicio de aplicación deja de importar repositorios y recibe únicamente resultados de dominio.
No se cambia el esquema, la política de permisos ni la semántica de los tres recorridos.

La regla ESLint `architecture/d1-boundary` es un error obligatorio dentro de `pnpm lint` y
`pnpm check`, por tanto también en el workflow existente de GitHub Actions. Se aplica por defecto
a todos los módulos JS/TS: app, componentes, worker y cualquier transporte futuro, aunque use otro
nombre o directorio. No hay una excepción global para `lib/application/**` ni para `tests/**`.
Protege `db/client`, todos los repositorios ejecutables actuales/futuros, `request-context`,
`authorized-d1` y el proveedor sintético. Normaliza rutas relativas, absolutas, alias `@/`,
extensiones e índices. Comprueba imports, reexports, import dinámico y require; rechaza destinos
calculados y cargas glob. No admite desactivaciones inline ni puentes desde producto hacia tests.

Excepciones exactas (consumidores `.ts`; la misma ruta con otra extensión no hereda permiso):

| Consumidor autorizado | Destinos internos permitidos |
| --- | --- |
| `lib/application/resident-baseline-service.ts` | `lib/authorization/request-context` |
| `lib/authorization/request-context.ts` | Repositorios `resident`, `baseline`, `audit`, `authorization-subject` y `authorized-d1` |
| `db/repositories/authorized-d1.ts` | `authorization-subject-repository` para revalidar evidencia |
| `tests/db-persistence.test.ts` | Repositorios `resident`, `baseline` y `audit` sobre D1 local sintética |
| `tests/server-auth-d1-boundary.test.ts` | Contexto/ejecutores y proveedor sintético para pruebas negativas |

`db/client` no tiene consumidores exceptuados. `d1.ts` expone solo tipos borrados por compilación,
sin conexión ni operaciones ejecutables; se permite solo `import type` para componer el puerto D1 existente.
La misma regla impide añadirle declaraciones ejecutables o reexportaciones.
Ninguna excepción permite reexportar módulos protegidos. Ampliar esta lista implica un cambio
explícito en la regla revisada; añadir un archivo de transporte no obtiene privilegios.

Son dos controles complementarios: la capacidad elimina el acceso genérico en runtime y ESLint
impide importar el adaptador o saltarse los ejecutores desde otro módulo. `server-only` sigue
protegiendo la frontera cliente. No se presenta ESLint como aislamiento frente a código que
modifique deliberadamente el núcleo autorizado o la propia configuración de validación.

## Restricciones individuales

El contrato 0002 establece que el acceso profesional se concede principalmente por centro y
unidad y que el ámbito individual añade una restricción cuando existe. Se conserva ese modelo:

- Sin historial de `profile_resident_scopes` para el grant profesional, la ubicación dentro
  de una unidad expresamente concedida delimita los residentes efectivos.
- Cuando existe alguna fila individual, incluso revocada, el residente necesita una concesión
  individual vigente del mismo grant y debe seguir en una unidad autorizada.
- Revocar la última concesión individual deja el grant sin residentes efectivos; nunca lo
  convierte automáticamente en acceso completo a la unidad.
- Auxiliar y Familiar no obtienen el ámbito profesional por unidad. No se construyen
  autorizaciones familiares ni servicios de esos perfiles en este bloque.

La presencia del historial permite interpretar de forma conservadora las restricciones sin
añadir flags ni migración. Retirar una restricción individual para ampliar el ámbito requerirá
una decisión y un procedimiento explícitos posteriores; este bloque no proporciona esa acción.

## Revalidación y atomicidad

`authorized-d1.ts` adapta el contrato D1 existente y antepone a **cada** batch una sentencia que
recalcula la misma proyección utilizada por la política y la compara con la evidencia autorizada.
Incluye los IDs de concesión de perfil, unidad y permisos, el vínculo individual y la ubicación,
por lo que una revocación y nueva concesión no rehabilitan silenciosamente el contexto anterior.

La sentencia usa `json(CASE ... END)`: con evidencia idéntica recibe JSON válido; si la evidencia
ha cambiado o desaparecido, recibe un literal inválido y aborta el batch antes de ejecutar la
operación. No crea registros auxiliares ni modifica el esquema. Se verifica tanto en SQLite
en memoria como en el runtime D1 local de Miniflare. La ejecución atómica se apoya en el
[contrato oficial de D1 batch](https://developers.cloudflare.com/d1/worker-api/d1-database/).

También se protegen las lecturas preliminares y la consulta/recuperación de resultados
idempotentes. Un resultado previo nunca exime de autorización vigente. Tras un fallo se consulta
la evidencia para distinguir una revocación de un fallo técnico, sin devolver errores SQL al
consumidor. Los resultados de batch incompletos o con `success: false` fallan cerrados.

La política de acciones sigue en un solo motor puro. El SQL compara hechos, sin duplicar una
segunda matriz de roles/permisos. Los repositorios y triggers previos mantienen además sus
validaciones y atomicidad. Un cambio en los hechos relevantes, incluso una ampliación de permisos,
puede exigir una nueva petición: se prioriza una autorización estable sobre reutilizar la antigua.

## Servicios y errores

`createResidentBaselineService(database, session)` es una fábrica interna de servidor. Sin
proveedor se utiliza uno que devuelve ausencia de identidad y deniega. Expone tres métodos:

| Método | Entrada seleccionable | Resultado |
| --- | --- | --- |
| `createResident` | Perfil/centro/unidad, identidad administrativa sintética, ubicación opcional, clave idempotente | Residente, episodio y ubicación inicial, atómicos y auditados |
| `signBaseline` | Perfil/centro/residente/borrador, revisión esperada, clave idempotente | ID y número de versión firmada; autoría procede del borrador y de la sesión |
| `readDirectionBaseline` | Perfil/centro/residente, vigente o histórico, finalidad | Cabeceras basales que devuelve el repositorio existente, tras auditoría por versión |

La lectura de Dirección conserva exactamente la proyección existente: ID, número, motivo y
fecha de firma. No se añaden áreas, Barthel ni otro contenido clínico. La auditoría se inserta
antes del SELECT de contenido en el mismo batch; ese SELECT enlaza con los registros de acceso
recién insertados. Si falla la auditoría no se devuelve contenido ni un resultado vacío exitoso.

Los métodos retornan `ApplicationResult<T>`. Cuenta desconocida, perfil ajeno, revocación,
recurso inexistente y recurso fuera de ámbito producen la misma forma serializada:

```json
{"ok":false,"error":{"code":"ACCESS_DENIED","message":"No se puede acceder a esta operación."}}
```

Errores técnicos producen `UNAVAILABLE`; entradas inválidas, `INVALID_INPUT`; reutilización
incompatible de clave idempotente, `CONFLICT`. No se devuelven causas, SQL, IDs internos ni detalles
del proveedor. No se implementa transporte HTTP ni se afirma indistinguibilidad temporal constante.

## Protección de servidor y desarrollo sintético

Puerto de sesión, proveedor sintético, política, contexto, servicios, normalización, cliente D1
y repositorios ejecutables llevan `import "server-only"`. El archivo `d1.ts` contiene únicamente
tipos borrados en compilación. El esquema declarativo no se modifica. Se utiliza la dependencia
`server-only` ya instalada, conforme a la
[protección de importaciones de Next](https://nextjs.org/docs/app/getting-started/server-and-client-components#preventing-environment-poisoning).
No hay `use server`, Server Actions, rutas públicas ni pantallas nuevas.

El proveedor sintético no se conecta automáticamente a los servicios. Requiere un sujeto
`synthetic-*` configurado por código de servidor y `CONNECT_SYNTHETIC_SESSION_ENABLED=1` en el
proceso. Solo lo permite con `NODE_ENV=test`, o con `NODE_ENV=development` **y**
`CONNECT_LOCAL_DEVELOPMENT=1`. Revalida esas condiciones en cada invocación; en producción,
staging o entorno desconocido falla cerrado aunque las dos opciones estén activadas.
Ninguna de ellas se obtiene del navegador. No se añaden archivos de variables locales.

Los scripts `test` y `db:test` añaden la condición estándar de Node `--conditions=react-server`
para importar los módulos protegidos. No se sustituye ni se desactiva `server-only`. Las pruebas
cliente crean procesos sin esa condición y builds aislados de Next/vinext; los dos builds de
producto de `pnpm check` siguen ejecutándose secuencialmente.

## Matriz identificada de pruebas

Las pruebas `BOUNDARY-T*` están en `tests/server-auth-d1-boundary.test.ts`, salvo T24, en
`tests/server-only-boundary.test.ts`. Todas usan datos sintéticos. T23 contiene 14 casos y T24
contiene tres pruebas; T31 contiene dos. Entrega inicial: **48 pruebas**.

La revisión del PR #3 añade otras **25 pruebas**: 15 `CAP-T*` en la suite de frontera y
10 `CAP-ARCH-*` en `tests/d1-architecture.test.ts`. Se conservan las 48 anteriores y se adapta
BOUNDARY-T25 a la opacidad del token. La matriz de frontera queda en **73 pruebas**.

| IDs nuevos | Cobertura |
| --- | --- |
| CAP-T01 (3) | READ no expone prepare/batch para INSERT, UPDATE ni DELETE; superficie exportada cerrada |
| CAP-T02 (6) | Todas las reutilizaciones cruzadas READ/SIGN/CREATE denegadas incluso con casts forzados |
| CAP-T03 | SIGN conserva borrador, autor, perfil y centro ante sustituciones del payload |
| CAP-T04 | READ conserva residente/recurso/finalidad; CREATE conserva autor y ámbito |
| CAP-T05 | Tres capacidades legítimas, resultados sin D1 y errores de tipos para clases incompatibles |
| CAP-T06 (3) | Revocación entre capacidad y batch en CREATE/SIGN/READ sin estados parciales |
| CAP-ARCH-01 (6) | Imports prohibidos desde app, componentes, worker y tres ubicaciones futuras sin privilegios |
| CAP-ARCH-02 | Rutas, alias, mayúsculas, reexports, require, import dinámico, tipos y cargas no verificables |
| CAP-ARCH-03 | Excepciones nominales positivas y negativas; sin herencia por extensión, puentes ni desactivación inline |
| CAP-ARCH-04 | El transporte puede importar el servicio y el contrato D1 de tipos |
| CAP-ARCH-05 | El contrato público D1 no puede añadir código ejecutable ni reexportaciones |

Las pruebas de arquitectura usan `ESLint.lintText` con la configuración real del repositorio
y módulos simulados en memoria. No añaden rutas, endpoints ni pantallas. BOUNDARY-T24a/T24b
siguen comprobando además las importaciones cliente en procesos nuevos y builds Next/vinext.

| ID | Caso | Resultado |
| --- | --- | --- |
| BOUNDARY-T01 | Ausencia de identidad; cero escrituras | OK |
| BOUNDARY-T02 | Identidad desconocida; denegación neutra | OK |
| BOUNDARY-T03 | Cuenta suspendida con grants conservados | OK |
| BOUNDARY-T04 | Perfil inexistente | OK |
| BOUNDARY-T05 | Perfil inactivo/revocado | OK |
| BOUNDARY-T06 | Perfil de otra cuenta | OK |
| BOUNDARY-T07 | Centro fuera del grant | OK |
| BOUNDARY-T08 | Unidad del mismo centro sin concesión | OK |
| BOUNDARY-T09 | Residente de la misma unidad fuera de restricción individual | OK |
| BOUNDARY-T10 | Permiso ausente; no se heredan permisos de otros perfiles | OK |
| BOUNDARY-T11 | Permiso revocado con sesión abierta y firma ya realizada | OK |
| BOUNDARY-T12 | Revocación de unidad y última concesión individual | OK |
| BOUNDARY-T13 | Cambio malicioso de perfil; firma no transferible entre perfiles | OK |
| BOUNDARY-T14 | Inyección de cuenta, permisos, identidad, perfil, cabeceras y cookies | OK |
| BOUNDARY-T15 | Aislamiento bidireccional de centros, incluso con cuenta multicentro | OK |
| BOUNDARY-T16 | Resultado serializado idéntico para inexistente/no autorizado | OK |
| BOUNDARY-T17 | Alta atómica/idempotente y autoría server-side | OK |
| BOUNDARY-T18 | Firma inicial, reintento y reevaluación autorizados | OK |
| BOUNDARY-T19 | Denegación de firma por otra cuenta | OK |
| BOUNDARY-T20 | Lectura de vigente e histórico de Dirección con auditoría por versión | OK |
| BOUNDARY-T21 | Fallo de auditoría: sin contenido, sin estado parcial, error técnico | OK |
| BOUNDARY-T22 | Proveedor sintético deshabilitado por defecto y en entornos no permitidos | OK |
| BOUNDARY-T23 | 14 carreras: alta (cuenta/perfil/unidad/permiso); firma y lectura (los cuatro + residente) | OK |
| BOUNDARY-T24a | 13 módulos sensibles rechazados en procesos sin condición servidor | OK |
| BOUNDARY-T24b | Builds reales de Next y vinext rechazan `use client` → servicio | OK |
| BOUNDARY-T25 | Token opaco congelado y rechazo de contexto copiado | OK |
| BOUNDARY-T26 | Mutación del comando durante la espera de sesión no cambia la operación | OK |
| BOUNDARY-T27 | Resultado idempotente de alta denegado tras revocación | OK |
| BOUNDARY-T28 | Tres casos de uso y revocación concurrente en D1 real local de Miniflare | OK |
| BOUNDARY-T29 | Revocación entre resolver contexto y recuperar resultado idempotente | OK |
| BOUNDARY-T30 | Dos firmas simultáneas con la misma clave, una versión y resultado | OK |
| BOUNDARY-T31 | Centro/unidad inactivados antes del batch; alta revertida | OK |
| BOUNDARY-T32 | Sesión expirada y fallo de proveedor sin filtrar detalles internos | OK |

Suites de persistencia conservadas íntegramente en comportamiento e identificadores:

| IDs | Cobertura | Resultado |
| --- | --- | --- |
| DB-T01–DB-T03 | Migración local vacía, reaplicación, integridad referencial | 3/3 OK |
| DB-T04–DB-T06 | Centro/unidad cruzados, ubicación única, borrador único | 3/3 OK |
| DB-T07–DB-T10 | Autoría, completitud, concurrencia D1 real e idempotencia de firma | 4/4 OK |
| DB-T11–DB-T14 | Inmutabilidad, sustitución, auditoría fallida y append-only | 4/4 OK |
| DB-T15–DB-T16 | Manipulación de ámbito y contrato de la puerta de validación | 2/2 OK |
| RES-T01–RES-T05 | Alta atómica/idempotente, clave incompatible, revocación de permiso/unidad y rollback | 5/5 OK |
| TOCTOU-T01–TOCTOU-T02 | Revocación de firma y lectura auditada antes del batch | 2/2 OK |
| CHAIN-T01–CHAIN-T05 | Versión inicial, sucesión consecutiva, vigente, tiempo y puntero | 5/5 OK |

El único ajuste de la suite previa es la expectativa del comando `db:test` en DB-T16 para
incluir la condición `react-server`; no se elimina ni debilita ninguna comprobación.

## Validación de entrega

- `pnpm db:test`: **28/28 OK**.
- Matriz de frontera: **73/73 OK**, incluidas las **25/25** añadidas tras la revisión.
- `pnpm check`: **211/211 pruebas OK**, typecheck, lint, build Next y build Vite/vinext Workers correctos.
- `git diff --check`: **OK**, sin errores.
- `git diff main -- db/migrations db/schema`: vacío. Sin `0002`.
- Pruebas documentales tras cerrar este informe: **6/6 OK**.
- Revisión del contenido: 22 archivos de texto en el PR, ocho en el ajuste adicional; cero rutas de credenciales,
  bases locales, `.wrangler`, variables privadas o claves; cero coincidencias con patrones de
  claves privadas y tokens; configuración D1 intacta y solo identificadores sintéticos nuevos.
- Los builds terminan con código 0. Vite/vinext emite avisos `INEFFECTIVE_DYNAMIC_IMPORT` en
  módulos de la dependencia; no se modifica esa dependencia ni se amplía el alcance para eliminarlos.
- SHA-256 de `0001_resident_baseline_foundation.sql`:
  `39B05B14AB2E9A31C2396A2D0665A36207D735C4921FD752B97C8C408AFA2EE8`.

## Archivos modificados

| Archivo | Cambio |
| --- | --- |
| `db/client.ts` | Marcador de servidor |
| `db/repositories/audit-repository.ts` | Marcador de servidor |
| `db/repositories/authorization-subject-repository.ts` | Proyección relacional de identidad, perfil y ámbitos |
| `db/repositories/authorized-d1.ts` | Revalidación de evidencia en cada batch |
| `db/repositories/baseline-repository.ts` | Marcador de servidor |
| `db/repositories/resident-repository.ts` | Marcador de servidor |
| `docs/architecture/0004-frontera-servidor-autorizacion-d1.md` | Arquitectura, decisiones, límites, matriz y resultados |
| `lib/application/errors.ts` | Resultados y normalización externa de errores |
| `lib/application/resident-baseline-service.ts` | Tres casos de uso internos |
| `lib/authorization/README.md` | Estado real y enlace a este informe |
| `lib/authorization/policy.ts` | Marcador de servidor; reglas puras intactas |
| `lib/authorization/request-context.ts` | Token opaco y tres ejecutores cerrados con D1 privado |
| `lib/authorization/server.ts` | Reexportación del catálogo existente |
| `lib/session/session-provider.ts` | Puerto de sesión y proveedor cerrado por defecto |
| `lib/session/synthetic-session-provider.ts` | Proveedor sintético con activación y entorno explícitos |
| `package.json` | Condición `react-server` en los scripts de pruebas |
| `tests/db-persistence.test.ts` | Expectativa del comando actualizada en DB-T16 |
| `tests/server-auth-d1-boundary.test.ts` | 60 pruebas de frontera y capacidades |
| `tests/server-only-boundary.test.ts` | Tres pruebas de protección cliente |
| `eslint.config.mjs` | Regla de arquitectura obligatoria y sin excepciones inline |
| `tooling/eslint/d1-boundary.mjs` | Lista nominal de imports internos y contrato público de tipos |
| `tests/d1-architecture.test.ts` | Diez pruebas de la configuración ESLint real |

El commit adicional de cierre del bypass modifica ocho archivos: configuración y regla ESLint,
servicio, contexto/ejecutores, las dos suites de capacidades/arquitectura, README de autorización
y este informe. No vuelve a modificar repositorios, `server-only`, dependencias ni scripts.

## Discrepancias documentales y límites

1. PRD v0.5, matriz v0.2.1 y trazabilidad v0.2 conservan frases que sitúan D1/Drizzle como
   pendientes; la línea base v1.1 y el ADR 0003 posterior autorizan la implementación local,
   ahora verificada e integrada. Se aplica esa decisión posterior sin editar los artefactos
   canónicos congelados ni sus huellas.
2. El ADR 0003 §10 enumera `authorization-subject-repository.ts` como parte de la estructura
   integrada, pero ese archivo no existía en el commit de partida. Este bloque lo implementa.
   El ADR también conserva un pie v1.1 bajo una cabecera v1.2 y lenguaje candidato en algunas
   secciones; no se modifica en esta tarea.
3. La matriz y los wireframes describen traslados y algunas operaciones futuras, pero la
   decisión posterior mantiene `D1-P04`–`D1-P06` bloqueadas. No se habilitan. ENF-19 limita el
   alta enfermera y la asignación de habitación; el servicio reutiliza el caso técnico de alta
   ya existente, sin definir un nuevo recorrido de Enfermería ni resolver esa pantalla aquí.
4. `0001` no distingue cuenta revocada de suspendida: se acepta exclusivamente `ACTIVE`.
   Una taxonomía productiva distinta requiere decisión posterior; no hace falta para denegar
   la cuenta desactivada ni los grants revocados del bloque actual.

No se ha identificado una carencia estructural que exija una migración para estos tres casos.
No hay entidades, columnas, restricciones ni índices nuevos, modificaciones de `0001`, snapshots,
configuración D1, autenticación productiva, datos reales, endpoints, pantallas ni dependencias.

Pendientes: adaptador definitivo de sesión y su aprovisionamiento seguro, selección visual de
perfil, transporte y controles HTTP, política productiva de restricción individual y supervisión,
revisión de seguridad/privacidad y rendimiento. El acceso D1 autorizado añade verificaciones por
consulta; su coste se deberá medir antes de ampliar el bloque. Los repositorios de bajo nivel
quedan sujetos a la lista de imports permitidos: cualquier integración futura debe entrar por
los servicios de aplicación y la validación rechaza el acceso directo.
No se habilita D1 remota ni se declara aptitud para producción sanitaria.
