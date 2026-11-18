# Retomar la construcción en una sesión nueva

Este fichero es **lo único que se lee al arrancar una sesión** (con Claude, ChatGPT o una persona). Tiene que caber en pantalla: el estado vigente, la siguiente tarea y las trampas que hoy pueden morder. Todo lo demás vive en otros ficheros y se consulta cuando hace falta (tabla del final).

**Estado a 2026-11-18** (partido el relevo ese día; el historial anterior se movió tal cual a [historial/](historial/)).

## Cómo se mantiene (al cerrar cada bloque de trabajo)

1. **Sustituye** «Estado actual»; no añadas una entrada debajo.
2. La entrada que sustituyes se copia **tal cual** al final del historial del periodo en curso (`historial/`; crea un fichero nuevo cuando el último pase de unas 25.000 palabras).
3. Números de PR, hashes y «CI en verde» los guarda git: aquí solo va lo que una persona nueva necesita para seguir.
4. Si algo ocupa más de ~15 líneas, va a su `pendientes-*.md` o a un ADR y aquí queda un puntero.
5. Una lección que valga para más de un bloque va a [lecciones-tecnicas.md](lecciones-tecnicas.md); un problema que dejas sin corregir, a [avisos-abiertos.md](avisos-abiertos.md).

## Prompt para empezar

> Retomamos la construcción de ResidApp. Empieza por «Estado actual» de este fichero. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`), el `pendientes-*.md` del vertical en curso (hoy, `docs/tareas/alta-prioridad/pendientes-citas-externas.md`) y
> `docs/tareas/alta-prioridad/pendientes-enfermeria.md`, `docs/tareas/alta-prioridad/pendientes-medicina.md`,
> `docs/tareas/alta-prioridad/pendientes-direccion.md` y `docs/tareas/alta-prioridad/pendientes-administracion.md`.
> Mira si hay respuestas de CJ (desde 2026-11-11 viven en el servidor, no en el repositorio: cuando Orlando te avise del correo «CJ ha respondido…», ejecuta tú `tools/respuestas-cj.ps1` (lee la clave de `~\.residapp\clave-cj.txt`; no la pidas ni la leas) y lee `respuestas-cj-local/`; ver `docs/pendientes-cj/README.md`; la app le avisa por correo cuando CJ responde, si está configurado `PendientesCj__AvisarA`;
> el estado de cada documento está en la última columna del README: las ocho guías de pruebas constan «Pendiente» en esa columna, pero CJ ya ha contestado alguna: mira las respuestas reales antes de darlo por cierto; los avisos por correo de propuesta y de cambio de cita ya están construidos y apagados, ADR 0012; el resto está hecho). Después
> comprueba que la suite pasa en verde contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Estado actual (2026-11-18)

- **`main` incluye el PR #330 (citas externas, PR 4), y la partición del relevo (#331).** El único otro PR abierto es el de release 0.5.0 (#302), que Orlando fusiona cuando quiera cerrar el hito (comprobado con `gh pr list` el 2026-11-18). El último script SQL es `0103`; el siguiente libre es `0104`.
- **El prototipo legado ya no está en el árbol de trabajo** (PR «quitar el legado», 2026-11-18): se conserva en la etiqueta de git `legado-cloudflare-final` y se lee con `git show legado-cloudflare-final:docs/legado-cloudflare/<ruta>`. Las decisiones de su matriz de permisos que siguen abiertas están en «Nota de vigencia» de `docs/producto/alcance.md`; la del acceso excepcional del equipo técnico es el apartado 15 de `docs/privacidad/antes-de-datos-reales.md` (a validar por una persona experta; recuérdaselo a Orlando junto con el resto de esa lista).
- **Citas externas (módulo base): PR 1 a 4 de 6 fusionados** (#325, #327, #328, #330): esquema y reglas, pantallas del personal, calendario y vista del Auxiliar, y nota «al terminar» con siguiente cita. Plan, decisiones y lecciones: `docs/tareas/alta-prioridad/pendientes-citas-externas.md`.
- **El script `0103` (la nota) estaba aplicado solo en la base local; en Azure dev se aplica al fusionar. No he comprobado que ya se haya aplicado:** míralo antes de dar la nota por buena en dev.
- **Siguiente: PR 5, la familia solo ve, sin plan escrito todavía** (interruptores por centro y por residente, nivel de visibilidad por cita, calendario). Después, PR 6 (la familia actúa, y correos).
- **Pendiente del PR 4 (QA y DPO), antes o junto al PR 5:** probar por HTTP el POST directo por perfil, el traslado de unidad, el ámbito restringido o revocado y el escapado HTML; que Administración no distinga «sin nota» de «nota sin siguiente cita»; la historia de usuario de las citas externas (no hay ninguna en `docs/historias-usuarios/`); un documento breve para CJ; y las preguntas al DPO real (plazo de la nota, derecho de acceso del residente, art. 32 LOPDGDD frente al historial de versiones, cómo vaciar una versión con datos que no debían estar). Detalle, en la primera entrada de [historial/2026-11-02_a_2026-11-18.md](historial/2026-11-02_a_2026-11-18.md).
- **Sin decidir:** las citas atrasadas de un residente dado de baja quedan abiertas y su residente ya no está activo para cerrarlas (D3 del PR 2). **A probar en Azure dev con dos centros:** el comportamiento de la violación de unicidad de `operacion_id` (script `0102`) bajo la seguridad por filas.
- Pruebas a 2026-11-18 (PR 4): unitarias 688, integración 879; funcionales 538 en el PR 3, más el recorrido completo de la nota.

## Siguiente tarea

Antes de proponer nada, haz `git fetch` y mira `origin/main` y los PR abiertos: otras sesiones lo cambian. La única tarea de código que toca es el PR 5 (punto 1), que primero necesita plan; lo demás depende de Orlando, de CJ o de una decisión.

1. **Citas externas, PR 5 (familia, solo ver).** Punto de entrada: `docs/tareas/alta-prioridad/pendientes-citas-externas.md`. No tiene plan escrito: planifica y consulta a `dpo-residapp` y a `doc-usuario-residapp` antes de construir (lo dice la cabecera de ese fichero), y mira antes «Pendiente del PR 4» en «Estado actual». La guía de cumplimiento de datos ya está incorporada; queda decidir con Orlando si se quita su HTML de Preguntas.
2. **Probar el dictado real (Orlando).** Con `docs/pendientes-cj/orlando/guia-pruebas-dictado.html`: grabar, revisar una a una y recibir la sugerencia de Mistral en `almendros-orlando-auxiliar`, con frases inventadas. Incorpora lo que cuente (frase, lo entendido, lo marcado, tiempos, errores). Sigue sin probarse en un móvil real. Sueltos del dictado: que una enfermera confirme «decul» y el rango 34–42 °C, y rotar la clave de Mistral.
3. **Antes de usar la app con residentes o familias reales** (lista completa en `docs/privacidad/antes-de-datos-reales.md`): una persona experta (DPO real o abogado) valida los borradores de `docs/privacidad/` (Orlando la busca; el dossier está preparado); se decide el plazo de conservación del historial de módulos; se nombra un suplente con permiso mínimo en Azure para el corte global del dictado; se valida el texto público de privacidad de la ficha de medicación; y no se anonimizan familiares (script `0087`) hasta que haya decisión escrita sobre borrar o bloquear (art. 32 LOPDGDD). **Recuérdaselo a Orlando antes de activar el centro piloto o de fusionar algo que toque estas funciones.** Recomendaciones del DPO sin hacer: una alerta de Azure Monitor sobre el aviso de «marca de entorno ausente» y prohibir restaurar producción sobre desarrollo o al revés (anotado en el ADR 0015).
4. **Esperan a CJ:** que vuelva a probar las pruebas 2, 5, 6, 9 y 13 de la guía 2; que confirme la medición de plazos por caso (3.1) y si le basta el atajo «Guardar e indicar a Enfermería»; las dos preguntas de `modulos-por-centro-aviso-cj`; `modulos-base-y-opcionales-cj` (hasta que la landing tenga la sección de precios); y de la ficha de antecedentes, «No refiere» frente a «No toma» y el «dato automático» del PDF. **Esta lista sale del README de `pendientes-cj` y de las entradas del relevo; no se ha contrastado con las respuestas del servidor.** Cuando Orlando avise de que CJ ha respondido, baja las respuestas con `tools/respuestas-cj.ps1` y no rellenes lo que falte.
5. **Pequeños, solo si Orlando los pide:** traslado por Enfermería con permiso (cuando un centro lo pida); I5 del ADR 0007 (pulsera NFC) y los pendientes de passkeys que anotó el relevo del 28/10 (**no verificados**: contrástalos con el ADR 0007 antes de proponerlos; por ejemplo, `Acceso__PasskeyDominio` consta como `dev.residapp.es` desde el 2026-10-30, pero hay una contradicción anotada con el dominio que citan las guías de CJ); la zona horaria (ver [avisos-abiertos.md](avisos-abiertos.md)); y el reintento ante el error 1205 (decidido no hacerlo todavía).
6. **Aparcado a propósito** (cada uno tiene su nota en el historial): dispositivos autorizados (ADR 0016, plan D0 a D6; empezar por D0 consultando antes a `dpo-residapp`), dashboards, dictado en Enfermería y elección de proveedor (Azure o Mistral), entorno de producción (ADR 0014) y privatizar el repositorio (esperan a que exista la sociedad titular).

## Trampas vigentes

- **Códigos de acción de auditoría: 32 caracteres como máximo** (`eventos_auditoria.accion_codigo` es NVARCHAR(32)); por eso `EXT_APPOINTMENT_*` y no `EXTERNAL_APPOINTMENT_*`.
- **Pruebas con fechas: usa el día de Madrid**, no el del reloj del servidor. `InDays` de `CitasExternasTests` fallaba unas horas cada noche por eso.
- **Rutas de MVC:** con la ruta por defecto `{controller}/{action}/{id?}`, una redirección a `Ver` sale como `/CitasExternas/Ver/<guid>` y no como `?id=`; en las pruebas, saca el identificador de la ruta entera.
- **Un script SQL ya aplicado en Azure no se edita nunca**, y el push a `main` aplica los scripts nuevos en Azure dev (más abajo, «Reglas de trabajo propias»).
- **Tras cada despliegue, Azure dev da 500 o 502 durante 30–45 s:** sondea antes de dar un fallo por real. Esta y otras trampas del entorno: [lecciones-tecnicas.md](lecciones-tecnicas.md).

## Reglas de trabajo propias de este repositorio

- **Push:** siempre con confirmación explícita del usuario. El push a `main` aplica los scripts nuevos
  y el seed en Azure SQL.
- **Rama y PR (decidido el 2026-10-06):** todo cambio con un script de migración en `database/scripts/` va en una rama y se mergea con PR,
  para que el CI lo pruebe antes de `main` (los scripts son inmutables una vez aplicados en Azure). Los cambios sin migración
  (documentación, tests, ajustes de código) pueden ir directos a `main`, salvo que sean grandes o arriesgados. El despliegue ya depende de que
  pasen los tests, así que un cambio roto no llega a Azure en ningún caso; el PR evita dejar un commit en rojo en `main`.
- **Esquema:** siempre un script nuevo numerado; nunca se edita uno ya aplicado en Azure.
- **Documentación de usuario:** la lista de lo que hay que mantener al día vive en `.claude/documentacion-usuario.json`
  (ver `CLAUDE.md`); al crear documentación nueva, se registra allí.
- **Manual:** actualiza `Views/Home/Manual.cshtml` cuando cambie una pantalla que describa o se construya
  un módulo marcado como «Próximamente».
- **Flujo de trabajo ilustrado:** revisa `wwwroot/flujo-de-trabajo.html` cuando cambie el circuito, un perfil o una pantalla
  que aparezca en él, o se construya algo de su recuadro «Lo que todavía no está».
- **Interfaz:** siempre en español, incluidos los enums que aparecen en un `<select>`. Sigue
  `docs/bocetos-pantallas/guia-diseno-sistema-visual.md` (Bootstrap 5, WCAG AA, `btn-lg`, nada de texto
  blanco sobre amarillo, ningún color fijo en las vistas) y revisa cada pantalla en claro y en oscuro.
- **Valores clínicos:** nunca los inventes. Si hace falta uno, se pide a CJ con un documento en
  `docs/pendientes-cj/`.
- **Contradicciones:** si el código contradice `docs/flujos-clinicos/`, para y avisa.
- **Idioma:** responde al usuario siempre en español, también en los mensajes cortos de estado.
## Dónde está el resto

| Qué | Dónde |
|---|---|
| Entradas «RELEVO» anteriores (el porqué y el cómo de cada bloque) | [historial/](historial/) (se busca por texto; no se lee entero) |
| Lecciones técnicas, del entorno y cómo se verifica un bloque | [lecciones-tecnicas.md](lecciones-tecnicas.md) |
| Avisos abiertos (fuera de alcance, sin corregir) | [avisos-abiertos.md](avisos-abiertos.md) |
| Qué documento manda sobre cada tema, y qué hacer si dos se contradicen | [INDICE.md](../INDICE.md) |
| Tareas de construcción por vertical | `docs/tareas/alta-prioridad/pendientes-*.md` |
| Respuestas y documentos de CJ | `docs/pendientes-cj/README.md` |
| Decisiones de arquitectura | `docs/decisiones-arquitectura/` |
