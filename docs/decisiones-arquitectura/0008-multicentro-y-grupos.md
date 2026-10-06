# ADR 0008 — Varios centros en una base de datos y grupos empresariales

Estado: **propuesta** (2026-10-06, solo documentación). Pendiente de validación de CJ y del responsable de protección de datos. No se
ha tocado `src/` ni `database/`.

## Contexto

Se quiere que la misma aplicación y el mismo despliegue de Azure sirvan a un cliente con una residencia y a otro con cincuenta,
agrupadas bajo una misma empresa (`Grupo → Centros`), y que un centro o un grupo pueda pasar a su propia base de datos sin reescribir
la capa de acceso a datos.

Lo que ya hay (verificado el 2026-10-06):

- **El esquema es multi-centro por filas.** Todas las tablas de negocio llevan `centro_id NOT NULL` y las claves foráneas son
  compuestas `(centro_id, id)`: el motor impide referenciar filas de otro centro. Los identificadores son `UNIQUEIDENTIFIER`, así que
  mover un centro a otra base no genera colisiones.
- **Hay un único punto de conexión:** `SqlConnectionFactory`, con una sola cadena (`ConnectionStrings:ResidApp`), que usan 37
  clases de Infrastructure (repositorios, directorios y escritores) (Dapper, SQL a mano). No existe `ITenantProvider` ni nada equivalente.
- **Globales:** `cuentas` (con `sujeto_externo` único) y `centros`. `ambitos_perfil` une cuenta y centro (`centro_id NOT NULL`); una
  cuenta puede tener ámbitos en varios centros y elige uno activo.
- **La autorización la resuelve SQL**, no los claims (ADR 0007).
- **`PLATAFORMA` provisiona centros y no ve contenido clínico** (ADR 0006), y vive en un centro reservado porque toda la
  autorización trabaja con un centro.
- **No hay RLS.** La defensa es la capa de aplicación, las claves compuestas y los triggers.

## Decisión

1. **El tenant es el centro (`centro_id`).** No se renombra a «residencia» ni se añade ningún identificador de tenant paralelo. La
   opción «varias residencias en una base de datos» ya está cumplida a nivel de datos.
2. **El centro nunca viaja en claims del proveedor de identidad.** Sale del ámbito activo ya validado contra SQL (ADR 0007, punto 5).
3. **Sin borrado lógico genérico (`Activo`/`FechaBaja`).** El modelo ya es append-only/close-only con `estado` y triggers
   `no_delete`; un segundo mecanismo duplicaría el existente.
4. **Grupo empresarial** (cuando haya un cliente con varias residencias, no antes):
   - tabla `dbo.grupos (id, codigo, nombre_visible, estado, creado_en)` y `centros.grupo_id UNIQUEIDENTIFIER NULL` con clave foránea.
     Un cliente de una sola residencia queda con `grupo_id = NULL`;
   - **no** se añade `grupo_id` a ninguna tabla clínica: `centro_id` sigue siendo el único tenant de los datos;
   - **administrador de grupo, primera versión sin perfil nuevo:** una cuenta con ámbito `ADMINISTRACION` en cada centro del grupo.
     Funciona con el multi-ámbito actual y no rompe la regla «todo trabaja con un centro». Plataforma, al crear un centro dentro de un
     grupo, podrá conceder ese ámbito a los administradores del grupo (incremento posterior);
   - **vistas transversales del grupo** (informes entre residencias), solo si producto las pide: un ámbito propio, de solo
     agregados no clínicos, modelado como el centro reservado del ADR 0006. No son un salto de RLS sobre tablas clínicas.
5. **RLS por centro como segunda barrera, por fases.** El centro de la cookie del ámbito activo no es de fiar (no está firmada), así
   que `SESSION_CONTEXT` no lleva el centro sino el **sujeto verificado por el proveedor de identidad y el id del ámbito activo**,
   fijados en cada apertura de conexión en `SqlConnectionFactory` (el pool reutiliza conexiones) con `read_only`. El predicado
   (`seg.fn_centro_del_ambito`) comprueba en SQL que ese ámbito es de esa cuenta, que ambos están activos y que el ámbito es del
   centro de la fila: una cookie manipulada no sale de los ámbitos de la propia cuenta. La `SECURITY POLICY` lleva `FILTER` y `BLOCK`
   (insertar y actualizar) por tabla.
   - **Los predicados de RLS se aplican también a `dbo` y a sysadmin.** La función deja pasar a los miembros de `db_owner` (scripts,
     seeds, tests y, hoy, el usuario de la aplicación en Azure), así que la política queda **latente hasta que la aplicación se
     conecte con un usuario que no sea `db_owner`**. Un `db_owner` puede desactivarla: protege de errores de la aplicación, no de
     quien administra la base.
   - **Las tablas clínicas no tienen ningún salto.** El «administrador global» ya existe como `PLATAFORMA` y, por el ADR 0006, no ve
     contenido clínico. Su salto se limita a las tablas que escribe al provisionar: `centros`, `unidades`, `cuentas`,
     `ambitos_perfil`, `ambitos_perfil_unidad` y `eventos_auditoria`.
   - El login lista los ámbitos de una cuenta antes de elegir centro: esas consultas (`cuentas`, `ambitos_perfil`) van en un modo de
     autenticación propio, fuera de las tablas clínicas.
   - El ámbito llega al factory por `ITenantContext` (puerto en Application; `RequestTenantContext` en Web lee el sujeto de
     `ISessionIdentityProvider` y el id de ámbito de la cookie). Sin ámbito, la conexión no lleva contexto y el usuario limitado no ve
     ninguna fila.
6. **Separación futura a otra base de datos: la unidad es el grupo, no el centro.** Un cliente grande puede querer su propia base. El
   resolvedor sería `centro → grupo → cadena de conexión`. No se construye ahora: sería una abstracción sin segundo destino.
   Cuando exista un caso real hará falta un **catálogo** (`cuentas`, `centros`, `grupos` y la entrada de `ambitos_perfil`) separado de
   las bases de datos de datos, porque las claves foráneas a `cuentas` no cruzan bases y el login necesita saber a qué base ir antes de
   conocer el centro. Tendrá su propio ADR.
7. **Regla desde ya:** ninguna consulta ni clave foránea nueva cruza centros fuera del perfil `PLATAFORMA`. Es lo que mantiene
   posible el punto 6.

## Acceso del grupo a datos clínicos

Decisión del usuario (2026-10-06): **un administrador de grupo no puede ver datos clínicos de varias residencias. Solo estructura y
agregados.** Si cada residencia es responsable del tratamiento de sus datos, esto evita la base jurídica y el contrato de encargo que
exigiría una lectura clínica transversal.

Consecuencias para el diseño:

- El ámbito de grupo (punto 4) se queda en agregados no clínicos y estructura; no se plantea ninguna variante con lectura clínica.
- Un administrador de grupo ve contenido clínico de un centro solo si tiene ahí un ámbito de un perfil clínico, como cualquier otra
  cuenta, y siempre un centro cada vez (el ámbito activo).
- Queda por definir, cuando se construya la vista de agregados, qué cifras entran y si necesitan un mínimo para no identificar a un
  residente en un centro pequeño. Es una decisión de producto de ese momento.

## Incrementos (cada uno con su encargo)

| # | Incremento | Condición para empezarlo |
|---|---|---|
| G0 | Este ADR y la actualización de `continuar-construccion.md` | — |
| G1 | RLS por centro. **Hecho (2026-10-06; `0030` y `0031` en `main` y aplicados a Azure de desarrollo, PR #1; tanda 1 del basal, `0032`, en la rama `g1-rls-basal`):** script `0030` sobre `residentes`, `eventos_asistenciales` y `familiares`; `ITenantContext` y `SESSION_CONTEXT` en `SqlConnectionFactory` (parámetro opcional: los tests siguen construyendo `new SqlConnectionFactory(cadena)`); `RlsCentroTests` (10 pruebas, suplantando un usuario sin login con `EXECUTE AS`). La aplicación entera se ha probado con un usuario sin `db_owner` (`database/seguridad/crear_usuario_aplicacion.sql`, variable `RESIDAPP_TEST_APP_CONNECTION_STRING`, paso nuevo del CI): los 68 tests funcionales pasan, aunque no crean filas en `familiares` ni en `eventos_asistenciales`. Las diez hijas que no tenían `centro_id` lo llevan ya (script `0031`, claves compuestas con su padre), así que todas las tablas de datos pueden recibir política. **Pendiente:** cubrir las 53 tablas restantes con `centro_id` (el test `CoberturaRlsTests` las lista); crear ese usuario en Azure y cambiar la cadena de la app (decisión de despliegue aparte); el salto de `PLATAFORMA` en las tablas de provisión | Aceptar este ADR |
| G2 | `grupos` y `centros.grupo_id` (script `0031`), alta de centro con grupo opcional en `/Plataforma`, con auditoría | Un cliente con varias residencias |
| G3 | Catálogo y resolvedor `centro → grupo → BD` | Un cliente que pida su propia base, con ADR propio |

## Consecuencias

- Hoy no cambia ningún comportamiento de la aplicación.
- Un cliente con una residencia y uno con cincuenta usan el mismo esquema; solo cambia cuántos centros hay y si tienen grupo.
- El riesgo de G1 es operativo (pool de conexiones, modo de autenticación, usuario sin `db_owner`), no de modelo. Por eso se prueba
  antes en la base de desarrollo, con dos centros sembrados.
