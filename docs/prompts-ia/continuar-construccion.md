# Retomar la construcción en una sesión nueva

Estado a 2026-09-28. Actualízalo al cerrar cada bloque de trabajo, para que la siguiente sesión (con
Claude, ChatGPT o una persona) arranque sin reconstruir el contexto.

## Prompt para empezar

> Retomamos la construcción de ResidApp. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`), `docs/tareas/alta-prioridad/pendientes-enfermeria.md` y
> `docs/tareas/alta-prioridad/pendientes-medicina.md`.
> Mira si CJ ha completado algo en `docs/pendientes-cj/`. Después comprueba que la suite pasa en verde
> contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Dónde estamos

- Todo el trabajo está en `main`. Hasta el script `0012` (historias 2 y 3 de Medicina y 7 de Enfermería)
  está desplegado en Azure; su pipeline terminó con éxito el 2026-09-28. La historia 4 de Medicina (script
  `0013`) está commiteada en local y **pendiente de push**; compruébalo con `git status` al empezar. El
  estado de un run se consulta sin autenticación en
  `https://api.github.com/repos/orlando6608/residapp/actions/runs?branch=main` (`gh` no está instalado).
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1
  (parcial), 2, 3, 4, 5, 7, 8, 9 y 10, más los rangos de referencia de constantes (fase 1 y su pantalla).
  Medicina está en curso: historias 1, 2, 3 y 4 (escalados, valoración médica, indicaciones y cierre
  médico).
- La base local `ResidApp` tiene los scripts `0001` a `0013` registrados en `dbo.scripts_aplicados`.
  Hay una copia previa a `0013` en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0013-20260928.bak`.
  La base local tiene además eventos de prueba del escenario integrado:
  - «Prueba manual historia 3: tos.»: cerrado, con comunicación pendiente de aprobación.
  - «Prueba manual historia 4: tos.»: cerrado tras un seguimiento completo.
  - «Prueba manual historia 5: disnea.»: cerrado por Medicina, con una comunicación pendiente y una
    indicación que Enfermería realizó después del cierre. Su motivo de escalado quedó como
    «Desaturaci%F3n…» por la lección de curl de más abajo.
  - «Prueba manual Medicina: fiebre.»: con indicación pendiente, que Enfermería leyó y realizó.
  - «Prueba manual cierre Enfermería tras 0013: mareo.»: cerrado por Enfermería.
- Suite: 92 unitarios, 155 de integración y 1 funcional, todos en verde.
- Hay dos scripts con el número `0005` (`0005_auxiliar_opciones_rapidas.sql` y
  `0005_enfermeria_borrador_basal.sql`). Es inofensivo, porque el runner los registra por nombre completo y
  son independientes entre sí. **No los renombres:** el runner los volvería a ejecutar y el despliegue en
  Azure fallaría. Antes de crear un script, comprueba cuál es el último número.

## Siguiente tarea: decidir el orden

Hay que proponer al usuario el orden antes de empezar. Un evento escalado ya puede terminar con el cierre
médico (historia 4 de Medicina).

- **Medicina, historia 5: seguimiento médico y continuidad entre turnos** (detalle en
  `pendientes-medicina.md`). Se parece al seguimiento de Enfermería (`0010`), pero tiene «objetivo» y la
  decisión explícita de transferir o conservar el seguimiento al cambio de turno.
- **Enfermería, historia 6: protocolo urgente y derivación a Urgencias** (`DER-01` a `DER-06`, común con
  Medicina, [`derivacion-urgencias.md`](../flujos-clinicos/derivacion-urgencias.md)). Incluye un informe
  firmado en PDF (hay que decidir cómo se genera) y una «actualización relevante» obligatoria para la
  familia con el registro del intento de llamada, que toca el Portal Familiar.

Después, el historial (historia 11 de Enfermería, 9 de Medicina), el evento propio de Medicina y el resto.

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
  - Cierre de evento común a Enfermería y Medicina en `ClinicalEventCloser`, con la regla de cada perfil en
    `ClinicalEventCloseRule`.
- **Nuevos estados del evento:** añádelos también a `ClinicalEventStatusDisplay` (`EnfermeriaModels.cs`).
  Si no, la insignia muestra el nombre interno en inglés.
- **Comandos locales:** la solución está en `src/ResidApp.sln`, así que se ejecuta
  `dotnet test src/ResidApp.sln`. La app se levanta con `dotnet run --launch-profile http` desde
  `src/ResidApp.Web`. Con `--no-launch-profile` no carga los user-secrets y falla por falta de la cadena de
  conexión.
- **Interbloqueos entre tests:** los tres proyectos de test se ejecutan en paralelo contra la misma BD. Un
  `UPDATE` o una lectura por una columna sin índice recorre la tabla entera y puede provocar interbloqueos
  intermitentes. Pasó con `valoraciones_enfermeria.evento_id` y se resolvió con `IX_ve_evento` en `0011`.
  Toda columna por la que se filtre un `UPDATE` necesita un índice. Si un test falla solo a veces, ejecuta
  `dotnet test src/ResidApp.sln` varias veces y busca `xml_deadlock_report` en `system_health`.
- **Acentos con curl en Git Bash:** `--data-urlencode` recibe los argumentos en Latin-1, así que «ó» llega
  como `%F3` y ASP.NET lo guarda tal cual. Un navegador envía UTF-8 y se guarda bien. En pruebas con curl,
  escribe los acentos ya codificados en UTF-8 (`--data "Form.Motivo=Desaturaci%C3%B3n"`).
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`.
