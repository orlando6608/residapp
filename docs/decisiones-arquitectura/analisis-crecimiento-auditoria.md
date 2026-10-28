# Análisis: cuánto crece la auditoría y qué índices necesita

Fecha: 2026-10-28. Motivo: preocupaba que `dbo.eventos_auditoria` crezca mucho «con tan pocos datos y en desarrollo». Esto es lo que se midió y lo que se decidió.

**En una frase:** el crecimiento que se ve en local lo producen las pruebas, no el uso; la tabla es pequeña por fila y el problema real no era su tamaño sino que **dos de las consultas que la leen recorrían todo el centro**. Eso se arregló con el script `0080`.

## 1. De dónde viene el crecimiento

- La base de la aplicación local (`ResidApp`, la de los centros de prueba) tiene **124 filas** de auditoría. Es lo que produce el uso de verdad.
- La base de las pruebas (`ResidAppPruebas`) recibe unas **38.000 filas por pasada de la suite** (en la última medición, unas 100.000 desde el último reinicio de SQL, con solo 4.350 en la parte de una pasada que se pudo medir). No es una anomalía del diseño: cada prueba crea **su propio centro** (se contaron 557 centros y 965 cuentas en 4.352 filas), y cada paso de preparación (renombrar una cuenta, dar de alta un residente, crear un horario, añadir franjas de citas) deja su fila. Los más repetidos son `ACCOUNT_RENAME` (11,5 %), `RESIDENT_CREATE` (11 %), `SCHEDULE_CREATE` (9 %) y `APPOINTMENT_WINDOW_ADD` (8 %): preparación de pruebas, no actividad.
- **No es solo la auditoría:** en la misma base de pruebas hay también 43.000 cuentas, 43.000 ámbitos de perfil y 23.000 operaciones de idempotencia. Todo lo que crean las pruebas se queda. Es normal en una base de pruebas; el CI usa una base nueva en cada ejecución.
- **Cuánto pesa una fila:** unos 225 bytes en la tabla y 75 a 105 bytes en cada índice. 38.000 filas son unos 10 MB con todos sus índices. Un centro real de unas 60 plazas escribiría, a ojo, unos cientos de filas al día (las lecturas auditadas son la mayoría): decenas de MB al año. SQL Server lo aguanta de sobra; lo que hace falta es una política de conservación (ver 4).
- **Ojo con una conclusión equivocada:** en la sesión se vació `ResidAppPruebas` con `TRUNCATE` creyendo que su tamaño causaba los tiempos de espera. La tabla no era grande (no salía entre las 8 mayores, por debajo de 7 MB). Los tiempos de espera venían de que el equipo se quedó con poca memoria libre (unos 650 MB, con SQL Server esperando `SLEEP_BPOOL_STEAL`); con 1,9 GB libres las mismas pruebas pasan en 9 segundos. Vaciar la tabla no hacía falta; no hizo daño (era la base local de pruebas, autorizado).

## 2. Qué índices tenía y cómo se usaban

| Índice | Columnas | Uso medido |
| --- | --- | --- |
| `PK_audit_events` (agrupado) | `id` (GUID aleatorio) | 152 recorridos completos (los `COUNT` de las pruebas por cuenta) |
| `IX_audit_scope_time` | centro, unidad, residente, fecha | 1.225 búsquedas y 1.393 recorridos, ninguna buena |
| `IX_audit_recurso_accion` | recurso, acción (incluye perfil) | 30.842 búsquedas: es el que más trabaja y funciona bien |
| `IX_audit_declaracion` (filtrado) | declaración | 20 búsquedas: pequeño y bien |

El problema: en `IX_audit_scope_time` la **unidad va en medio**, y es nula en muchos eventos, así que no se puede buscar por fecha ni por residente sin recorrer todo el centro. Lo afirmaba mal `pendientes-administracion.md` («cubre el acceso por centro y fecha»).

## 3. Medición con 300.000 filas simuladas

Se creó en `tempdb` (borrada después) una tabla con la misma forma, 300.000 filas, un centro con 250.000 (un año de uso) y dos con 25.000, y se midieron las consultas reales:

| Consulta | Antes (lecturas de página) | Después de `0080` |
| --- | --- | --- |
| Pantalla de auditoría de Administración (centro, 30 días, acciones, más recientes primero) | 6.834 | 15 |
| Trazabilidad de un residente (DIR-14) | 6.319 | 20 |
| Todo lo de una cuenta (revisión de accesos) | 8.972 | 3, pero **no se creó el índice** |

Coste: los índices nuevos pesan unos 26 y 31 MB por 300.000 filas (el que se quita pesaba 23). Como se quita `IX_audit_scope_time`, la tabla sigue con **cuatro índices** y no se encarece la escritura (que es lo único que recibe).

## 4. Decisiones

- **Hecho, script `0080`:** `IX_audit_centro_tiempo (centro, fecha DESC) INCLUDE (acción, unidad)` e `IX_audit_residente_tiempo (centro, residente, fecha DESC) INCLUDE (acción, unidad)`; se elimina `IX_audit_scope_time` (nada lo nombra). Solo índices: ni columnas ni datos nuevos.
- **No hecho a propósito:**
  - **Índice por cuenta:** ninguna pantalla lo usa. Se crea cuando la revisión de accesos del DPO lo necesite.
  - **Cambiar la clave agrupada (GUID aleatorio) por fecha:** se probó (200 lotes de 500 filas). La clave aleatoria deja la tabla al 96 % de fragmentación y un 45 % más de páginas, pero con la clave por fecha el índice único de `id` se fragmenta igual y la inserción no fue más rápida en la prueba. Además los identificadores los genera la aplicación (`NEWID()` en decenas de `INSERT`). No compensa a esta escala.
  - **Reducir lo que se audita:** todo lo auditado responde a una regla de trazabilidad (lecturas clínicas, cambios administrativos). Lo único que sobra son filas de preparación de pruebas.
- **Pendiente de decidir (Orlando, CJ y el DPO real):** el **plazo de conservación** de la auditoría (hoy no hay ninguno; está anotado en `docs/privacidad/historial-tras-traslado-05-…`). Con un plazo, la forma de aplicarlo más limpia es **dividir la tabla por fecha** (particionado) y retirar periodos enteros, que exigiría reconstruir la tabla con el trigger de inmutabilidad: es un cambio mayor que no urge.
- **Cuándo volver a mirar:** si algún día la auditoría de un centro pasa de varios millones de filas, o si se añade una pantalla que consulte por cuenta o por recurso de otra forma.

## 5. Para quien ejecute las pruebas en local

- La base de las pruebas es `ResidAppPruebas` y los scripts nuevos hay que aplicarlos a ella **y** a `ResidApp` (`SQL_SERVER=ACER-ORLANDO SQL_DATABASE=… SQLCMD_EXTRA=-C bash database/aplicar-scripts.sh`).
- Antes de echarle la culpa a la auditoría, mira la memoria libre del equipo: con menos de 1 GB las pruebas dan tiempos de espera de SQL en cualquier prueba, y los procesos de compilación (`dotnet build-server shutdown`), el navegador y VS Code ocupan mucho.
- Para ver cuántas filas hay sin escanear la tabla: `SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID('dbo.eventos_auditoria') AND index_id IN (0,1)`.
