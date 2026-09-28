# Retomar la construcción en una sesión nueva

Estado a 2026-09-28. Actualízalo al cerrar cada bloque de trabajo, para que la siguiente sesión (con
Claude, ChatGPT o una persona) arranque sin reconstruir el contexto.

## Prompt para empezar

> Retomamos la construcción de ResidApp. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`) y `docs/tareas/alta-prioridad/pendientes-enfermeria.md`.
> Mira si CJ ha completado algo en `docs/pendientes-cj/`. Después comprueba que la suite pasa en verde
> contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Dónde estamos

- Todo el trabajo está en `main` y desplegado en Azure; no hay ramas ni cambios locales pendientes.
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1
  (parcial), 2, 8, 9 y 10, más los rangos de referencia de constantes (fase 1 y su pantalla).
- La base local `ResidApp` tiene los scripts `0001` a `0008` y su registro en `dbo.scripts_aplicados`
  desde el 2026-09-28. Hay una copia previa en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0007-20260928.bak`.
- Suite: 61 unitarios, 127 de integración y 1 funcional, todos en verde.

## Siguiente tarea: Enfermería, historia 3

Cerrar un evento y decidir la comunicación familiar (`ENF-06`, `ENF-12`):
[`docs/historias-usuarios/enfermeria.md`](../historias-usuarios/enfermeria.md) y
[`docs/flujos-clinicos/valoracion-escalado-enfermeria.md`](../flujos-clinicos/valoracion-escalado-enfermeria.md).

- «Pasar a decisión asistencial» desde la valoración. Cerrar es una de las cuatro salidas; las otras
  (seguimiento, escalado a Medicina, protocolo urgente) son las historias 4, 5 y 6.
- El cierre es idempotente y los eventos cerrados salen de las bandejas.
- Hay que ampliar los estados de `eventos_asistenciales` (hoy PENDIENTE y EN_VALORACION, con el trigger
  `TR_ea_transition_guard`) y cerrar el borrador de `valoraciones_enfermeria`. Todo ello va en un script
  `0009` nuevo.
- **Antes de diseñar hay que resolver una cosa:** «preparar una comunicación familiar» exige aprobación
  humana y se muestra como «Equipo asistencial del centro». Las publicaciones y su aprobación pertenecen a
  los verticales Portal Familiar y Administración, que no existen todavía. Hay que acotar qué parte
  mínima se construye ahora, por ejemplo guardar la comunicación preparada, pendiente de aprobación. Es
  una decisión de alcance: proponerla al usuario y no inventarla.

Después, en orden: historias 4, 5, 7, 6 y 11 (detalle en `pendientes-enfermeria.md`), y luego Medicina.

## Reglas de trabajo propias de este repositorio

- **Push:** siempre con confirmación explícita del usuario. El push a `main` aplica los scripts nuevos
  y el seed en Azure SQL.
- **Esquema:** siempre un script nuevo numerado; nunca se edita uno ya aplicado en Azure.
- **Manual:** actualiza `Views/Home/Manual.cshtml` cuando cambie una pantalla que describa o se construya
  un módulo marcado como «Próximamente».
- **Interfaz:** siempre en español, incluidos los enums que aparecen en un `<select>`. Sigue
  `docs/bocetos-pantallas/guia-diseno-sistema-visual.md` (Bootstrap 5, WCAG AA, `btn-lg`, nada de texto
  blanco sobre amarillo).
- **Valores clínicos:** nunca los inventes. Si hace falta uno, se pide a CJ con un documento en
  `docs/pendientes-cj/`.
- **Contradicciones:** si el código contradice `docs/flujos-clinicos/`, para y avisa.

## Lecciones técnicas que conviene no redescubrir

- **Decimales en formularios:** la cultura del servidor es es-ES. Un decimal enlazado desde
  `type="text"` convierte «37.8» en 378. Usa `type="number" step="0.1"`, que ASP.NET Core enlaza con
  cultura invariante gracias al campo oculto `__Invariant`. En un campo oculto que reenvía un decimal,
  escribe el valor en formato invariante y añade también `__Invariant`.
- **`[Range]` con decimales:** necesita `ParseLimitsInInvariantCulture = true`; si no, da un error 500
  en es-ES.
- **`OUTPUT` en SQL:** no se puede usar `UPDATE ... OUTPUT` sin `INTO` en tablas con triggers. Comprueba
  las filas afectadas con la revisión en el `WHERE`.
- **Códigos de error:** los que lanza la BD o el dominio se traducen a acceso denegado, entrada inválida
  o conflicto mediante patrones en `src/ResidApp.Application/Errors/ApplicationResult.cs`. Un código nuevo
  necesita su patrón allí.
- **Patrones ya asentados** que se reutilizan en vez de reinventarlos:
  - Concurrencia optimista por revisión o versión, con un mensaje de conflicto neutro que conserva lo
    escrito.
  - Tablas de solo inserción con triggers `INSTEAD OF UPDATE, DELETE` que lanzan `THROW`.
  - Comprobación de ámbito deny-by-default en cada caso de uso.
  - Auditoría en `eventos_auditoria`.
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`.
