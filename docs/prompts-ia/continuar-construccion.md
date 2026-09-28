# Retomar la construcción en una sesión nueva

Estado a 2026-09-28. Actualízalo al cerrar cada bloque de trabajo, para que la siguiente sesión (con
Claude, ChatGPT o una persona) arranque sin reconstruir el contexto.

## Prompt para empezar

> Retomamos la construcción de ResidApp. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`) y `docs/tareas/alta-prioridad/pendientes-enfermeria.md`.
> Mira si CJ ha completado algo en `docs/pendientes-cj/`. Después comprueba que la suite pasa en verde
> contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Dónde estamos

- Todo el trabajo está en `main`. La historia 3 (script `0009`) ya está desplegada en Azure: el pipeline
  terminó con éxito el 2026-09-28. La historia 4 (script `0010`) está commiteada en local y **pendiente de
  push**; compruébalo con `git status` al empezar. El estado de un run se consulta sin autenticación en
  `https://api.github.com/repos/orlando6608/residapp/actions/runs?branch=main` (`gh` no está instalado).
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1
  (parcial), 2, 3, 4, 8, 9 y 10, más los rangos de referencia de constantes (fase 1 y su pantalla).
- La base local `ResidApp` tiene los scripts `0001` a `0010` registrados en `dbo.scripts_aplicados`.
  Hay una copia previa a `0010` en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0010-20260928.bak`.
  La base local tiene además dos eventos de prueba cerrados del escenario integrado («Prueba manual
  historia 3: tos.», con su comunicación pendiente de aprobación, y «Prueba manual historia 4: tos.», que
  pasó por un seguimiento completo).
- Suite: 79 unitarios, 140 de integración y 1 funcional, todos en verde.
- Hay dos scripts con el número `0005` (`0005_auxiliar_opciones_rapidas.sql` y
  `0005_enfermeria_borrador_basal.sql`). Es inofensivo, porque el runner los registra por nombre completo y
  son independientes entre sí. **No los renombres:** el runner los volvería a ejecutar y el despliegue en
  Azure fallaría. Antes de crear un script, comprueba cuál es el último número.

## Siguiente tarea: Enfermería, historia 5

Escalar un evento a Medicina (`ENF-06`, `ENF-09`):
[`docs/historias-usuarios/enfermeria.md`](../historias-usuarios/enfermeria.md),
[`docs/flujos-clinicos/valoracion-escalado-enfermeria.md`](../flujos-clinicos/valoracion-escalado-enfermeria.md)
(salida «c», estado «Escalado a Medicina»; wireframe ENF-10) y
[`docs/flujos-clinicos/valoracion-conducta-medicina.md`](../flujos-clinicos/valoracion-conducta-medicina.md).

- Es la tercera salida de la decisión asistencial y se puede escalar desde EN_VALORACION y desde
  EN_SEGUIMIENTO. Se activa su tarjeta en `Views/Enfermeria/Decision.cshtml`.
- Reutiliza el patrón de `StartFollowUpAsync` y `CloseAsync` de `SqlNursingAssessmentRepository`
  (revisión, auditoría, solo inserción). El estado y sus transiciones van en un script `0011` nuevo, con
  `0010` como referencia.
- El escalado transmite observación, basal vigente, valoración, constantes, actuaciones y motivo, sin
  ningún resumen automático. Escalar no cierra el evento: el desenlace es de Medicina.
- **Antes de diseñar hay que resolver una cosa:** el evento escalado «pasa a la bandeja de Medicina», y el
  vertical Medicina no existe todavía. Hay que acotar qué parte mínima se construye ahora (por ejemplo, el
  escalado con su motivo y una bandeja de Medicina de solo lectura) y qué pasa con la valoración de
  Enfermería al escalar. Es una decisión de alcance: proponerla al usuario y no inventarla.

Después, en orden: historias 7, 6 y 11 (detalle en `pendientes-enfermeria.md`), y luego Medicina.

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
- **Comandos locales:** la solución está en `src/ResidApp.sln`, así que se ejecuta
  `dotnet test src/ResidApp.sln`. La app se levanta con `dotnet run --launch-profile http` desde
  `src/ResidApp.Web`. Con `--no-launch-profile` no carga los user-secrets y falla por falta de la cadena de
  conexión.
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`.
