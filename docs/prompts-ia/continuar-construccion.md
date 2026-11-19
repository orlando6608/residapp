# Retomar la construcción en una sesión nueva

Este fichero es **lo único que se lee al arrancar una sesión** (con Claude, ChatGPT o una persona). Tiene que caber en pantalla: el estado vigente, la siguiente tarea y las trampas que hoy pueden morder. Todo lo demás vive en otros ficheros y se consulta cuando hace falta (tabla del final).

**Estado a 2026-11-19** (el historial anterior está en [historial/](historial/); el estado sustituido se copió tal cual al principio de `historial/2026-11-19_en_adelante.md`).

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

## Estado actual (2026-11-19)

- **Hito 0.5.0 cerrado (2026-11-19):** release fusionada (#302), tag `v0.5.0`, `docs/INDICE.md` y los documentos de usuario resellados a v0.5.0 (#341 y #342); no se subió ningún `data-version`. `Directory.Build.props` está en 0.5.0.
- **Citas externas (módulo base): los seis PR están fusionados** (#325, #327, #328, #330, #337 y #340; scripts `0101` a `0105`; el siguiente libre es `0106`). Lo último, el PR 6: la familia actúa («Me encargo yo», «Ya no puedo», «No puedo») y recibe correos a 7 días, la víspera y 2 días si sigue sin cubrir; el interruptor de correos del centro y el de entorno (`Correo:AvisoCitaExterna`) vienen **apagados**. Decisiones de Orlando, lo construido y lo que cambió respecto al plan: apartado 11 (11.6) de `docs/tareas/alta-prioridad/pendientes-citas-externas.md`; privacidad: 14.2 de `docs/privacidad/antes-de-datos-reales.md`. El cálculo de avisos es un procedimiento (`calcular_avisos_citas_externas`) porque como función en línea tardaba minutos.
- **Pendiente del PR 6 (QA, sin prueba, no bloqueante):** residente suspendido o de baja al actuar y al comprobar; citas a 90 y 91 días; `CorreoOptions.AvisoCitaExterna` apagado por defecto; franja 10:00 a 18:59 y cambio de hora; citas `CONFIRMADA_HOSPITAL`; «Me encargo yo» frente a «Ya no puedo» o a la confirmación del personal a la vez; baja de una persona con dos residentes; auditoría de la baja y el alta del aviso; «Persona anonimizada»; la rama de invitación `ALTA`; y criterios de aceptación en las historias de usuario. **Trampa:** las pruebas que llaman a `reservar_avisos_citas_externas_en` recorren todos los centros; van en la colección `AvisoCambioCita` y, con la máquina cargada, dan falsos tiempos de espera (ejecútalas solas).
- **Pendiente de documentación, a decidir por Orlando:** la guía 5 de CJ (prueba 11) no dice que la baja también cancela las citas externas; arreglarlo exige subir su `data-version`. `Views/Home/Privacy.cshtml:106` («ni se enciende centro por centro», «hoy hay tres» avisos) se contradice con el interruptor por centro: lo valida una persona con el DPO. Ninguna guía de CJ cubre las citas externas. El dominio de las passkeys de `acceso-y-sesion.html` se cambió a `dev.residapp.es` (ADR 0007): confírmalo contra la configuración de Azure.
- **A decidir por una persona (hallazgos de QA, sin tocar):** (1) si el mes del portal debe marcar un día que solo tiene citas canceladas (hoy sí); (2) si quitar una restricción o encender el centro debe pedir una confirmación en el servidor que lleve el recuento visto (hoy la pantalla con el recuento es la confirmación); (3) una cita visible ya cancelada no se puede ocultar, solo restringir a todo el residente; (4) las pruebas que QA aún sugiere: POST directo por perfil (Medicina, Auxiliar, Dirección), escapado HTML y las 28 especialidades en el HTML, y concurrencia real de dos cambios a la vez. La frase «Esta información la ve quien está autorizado…» solo es cierta si el centro tiene el procedimiento de voluntad: la valida la persona experta.
- **El prototipo legado ya no está en el árbol** (etiqueta `legado-cloudflare-final`; se lee con `git show legado-cloudflare-final:docs/legado-cloudflare/<ruta>`). Las decisiones abiertas de su matriz de permisos están en «Nota de vigencia» de `docs/producto/alcance.md`; la del acceso excepcional del equipo técnico es el apartado 15 de `docs/privacidad/antes-de-datos-reales.md` (a validar por una persona experta; recuérdaselo a Orlando).
- **Pendiente del PR 4 (QA y DPO):** probar por HTTP el POST directo por perfil de las pantallas del personal, el escapado HTML, Enfermería con ámbito o cuenta revocados; que Administración no distinga «sin nota» de «nota sin siguiente cita»; la historia de usuario de las citas externas (no hay ninguna en `docs/historias-usuarios/`); un documento breve para CJ; y las preguntas al DPO real (plazo de la nota, derecho de acceso del residente, art. 32 LOPDGDD frente al historial de versiones, cómo vaciar una versión con datos que no debían estar). Detalle en `historial/2026-11-02_a_2026-11-18.md` (primera entrada).
- **Sin decidir:** las citas atrasadas de un residente dado de baja quedan abiertas y su residente ya no está activo para cerrarlas (D3 del PR 2). **A probar en Azure dev con dos centros:** la violación de unicidad de `operacion_id` (`0102`) y el comportamiento de los bloqueos de `0104` bajo la seguridad por filas.
- Pruebas a 2026-11-19 (PR 6): unitarias 712; integración 955; funcionales 545.

## Siguiente tarea

Antes de proponer nada, haz `git fetch` y mira `origin/main` y los PR abiertos: otras sesiones lo cambian. Las citas externas están construidas y el hito 0.5.0 está cerrado; lo que sigue depende de Orlando, de CJ o de una decisión.

1. **Citas externas: lo que falta no es código.** Validación de una persona experta antes del primer correo real (base jurídica, textos, catalán, retención, «borrar o bloquear»; apartado 14.2 de `docs/privacidad/antes-de-datos-reales.md`), la confirmación escrita de CJ de que FAM-10 incluye estos avisos, probar en Azure dev con dos centros y encender los interruptores con datos de prueba, y las decisiones de documentación de «Estado actual». Si Orlando lo pide: cerrar los escenarios de QA sin prueba.

2. **Probar el dictado real (Orlando).** Con `docs/pendientes-cj/orlando/guia-pruebas-dictado.html`: grabar, revisar una a una y recibir la sugerencia de Mistral en `almendros-orlando-auxiliar`, con frases inventadas. Incorpora lo que cuente (frase, lo entendido, lo marcado, tiempos, errores). Sigue sin probarse en un móvil real. Sueltos del dictado: que una enfermera confirme «decul» y el rango 34–42 °C, y rotar la clave de Mistral.
3. **Antes de usar la app con residentes o familias reales** (lista completa en `docs/privacidad/antes-de-datos-reales.md`): una persona experta (DPO real o abogado) valida los borradores de `docs/privacidad/` (Orlando la busca; el dossier está preparado); se decide el plazo de conservación del historial de módulos; se nombra un suplente con permiso mínimo en Azure para el corte global del dictado; se valida el texto público de privacidad de la ficha de medicación; y no se anonimizan familiares (script `0087`) hasta que haya decisión escrita sobre borrar o bloquear (art. 32 LOPDGDD). **Recuérdaselo a Orlando antes de activar el centro piloto o de fusionar algo que toque estas funciones.** Recomendaciones del DPO sin hacer: una alerta de Azure Monitor sobre el aviso de «marca de entorno ausente» y prohibir restaurar producción sobre desarrollo o al revés (anotado en el ADR 0015).
4. **Esperan a CJ:** que vuelva a probar las pruebas 2, 5, 6, 9 y 13 de la guía 2; que confirme la medición de plazos por caso (3.1) y si le basta el atajo «Guardar e indicar a Enfermería»; las dos preguntas de `modulos-por-centro-aviso-cj`; `modulos-base-y-opcionales-cj` (hasta que la landing tenga la sección de precios); y de la ficha de antecedentes, «No refiere» frente a «No toma» y el «dato automático» del PDF. **Esta lista sale del README de `pendientes-cj` y de las entradas del relevo; no se ha contrastado con las respuestas del servidor.** Cuando Orlando avise de que CJ ha respondido, baja las respuestas con `tools/respuestas-cj.ps1` y no rellenes lo que falte. Además: el catalán en toda la aplicación (`docs/pendientes-cj/app-catalan-castellano-cj.html`, 9 preguntas); lo que hay que hacer cuando conteste está en `docs/tareas/alta-prioridad/pendientes-catalan.md` (apartado 3).
5. **Pequeños, solo si Orlando los pide:** traslado por Enfermería con permiso (cuando un centro lo pida); I5 del ADR 0007 (pulsera NFC) y los pendientes de passkeys que anotó el relevo del 28/10 (**no verificados**: contrástalos con el ADR 0007 antes de proponerlos; por ejemplo, `Acceso__PasskeyDominio` consta como `dev.residapp.es` desde el 2026-10-30, pero hay una contradicción anotada con el dominio que citan las guías de CJ); la zona horaria (ver [avisos-abiertos.md](avisos-abiertos.md)); y el reintento ante el error 1205 (decidido no hacerlo todavía).
6. **Aparcado a propósito** (cada uno tiene su nota en el historial): dispositivos autorizados (ADR 0016, plan D0 a D6; empezar por D0 consultando antes a `dpo-residapp`), dashboards, dictado en Enfermería y elección de proveedor (Azure o Mistral), entorno de producción (ADR 0014) y privatizar el repositorio (esperan a que exista la sociedad titular).

## Trampas vigentes

- **Códigos de acción de auditoría: 32 caracteres como máximo** (`eventos_auditoria.accion_codigo` es NVARCHAR(32)); por eso `EXT_APPOINTMENT_*` y no `EXTERNAL_APPOINTMENT_*`.
- **Pruebas con fechas: usa el día de Madrid**, no el del reloj del servidor. `InDays` de `CitasExternasTests` fallaba unas horas cada noche por eso.
- **Rutas de MVC:** con la ruta por defecto `{controller}/{action}/{id?}`, una redirección a `Ver` sale como `/CitasExternas/Ver/<guid>` y no como `?id=`; en las pruebas, saca el identificador de la ruta entera.
- **Un script SQL ya aplicado en Azure no se edita nunca**, y fusionar en `main` aplica los scripts nuevos en Azure dev (ver «Git y despliegue» en `CLAUDE.md`).
- **Tras cada despliegue, Azure dev da 500 o 502 durante 30–45 s:** sondea antes de dar un fallo por real. Esta y otras trampas del entorno: [lecciones-tecnicas.md](lecciones-tecnicas.md).

## Dónde está el resto

| Qué | Dónde |
|---|---|
| Entradas «RELEVO» anteriores (el porqué y el cómo de cada bloque) | [historial/](historial/) (se busca por texto; no se lee entero) |
| Lecciones técnicas, del entorno y cómo se verifica un bloque | [lecciones-tecnicas.md](lecciones-tecnicas.md) |
| Avisos abiertos (fuera de alcance, sin corregir) | [avisos-abiertos.md](avisos-abiertos.md) |
| Qué documento manda sobre cada tema, y qué hacer si dos se contradicen | [INDICE.md](../INDICE.md) |
| Reglas de trabajo del repositorio (Git y despliegue, esquema, interfaz, valores clínicos, idioma…) | [CLAUDE.md](../../CLAUDE.md) |
| Tareas de construcción por vertical | `docs/tareas/alta-prioridad/pendientes-*.md` |
| Respuestas y documentos de CJ | `docs/pendientes-cj/README.md` |
| Decisiones de arquitectura | `docs/decisiones-arquitectura/` |
