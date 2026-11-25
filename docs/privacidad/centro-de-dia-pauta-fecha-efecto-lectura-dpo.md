# Centro de Día, asistencia: fecha de efecto de la pauta (script 0116)

**BORRADOR del agente `dpo-residapp`. NO está validado. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-25. Segunda lectura experta, no un dictamen. Rama `fase5a-pauta-fecha-efecto`. Complementa los apartados 3.1 y 3.3 de `centro-de-dia-asistencia-lectura-dpo.md`. No modifica código, esquema ni ADR.

Etiquetas: **[Hecho]** verificado en el repo; **[Opinión]** mi criterio; **[Decidir]** lo fija una persona; **[Propuesto, sin validar por CJ]** valor de producto que no valido. Términos: **pauta** = qué días de la semana se espera a cada persona; **de solo añadir** = la aplicación no cambia ni borra filas, un cambio es una fila nueva; **fecha de efecto** = primer día en que una versión de la pauta cuenta.

## Conclusión

**Admisible, con tres condiciones y un defecto de diseño que conviene arreglar antes de escribir el script.** No añade categoría de datos ni finalidad: la pauta ya existía; solo pasa de «la última versión vale para todo el pasado» a «cada versión vale desde su fecha», lo que **mejora la exactitud** (art. 5.1.d RGPD) y la prueba de lo que se esperaba cada día (art. 5.2).

Riesgo principal: **que un cambio con fecha pasada reescriba en silencio lo que la lista de seguimiento considera «esperado»** y haga desaparecer a alguien de la lista. Se acepta si queda a la vista (condición 2).

## Hallazgos y condiciones

### 1. Defecto de diseño: una versión futura equivocada bloquea las siguientes (a corregir)

**[Hecho]** `0115` es de solo añadir (`0115_asistencia_centro_de_dia.sql:138-139`, `TR_asp_immutable`). Con tu regla «la fecha no puede ser anterior a la de la versión anterior» y fechas hasta hoy+90, quien registre por error una versión con fecha +80 **no puede corregirla** y nadie puede registrar un cambio anterior a esa fecha hasta que llegue. Es un callejón sin salida creado por la combinación de solo añadir y orden obligatorio.

**Acción propuesta [Opinión]:** (i) en vez de «no anterior a la versión anterior», exige «no anterior a la fecha de la versión **en vigor hoy**» (la última con `vigente_desde <= hoy`); (ii) el día D se espera según **la versión de mayor `numero` con `vigente_desde <= D`** (no la de mayor fecha). Así la versión nueva sustituye a una futura equivocada, y sigue sin poder tocar lo anterior a la versión ya en vigor. Es compatible con tu punto (3). Si prefieres mantener tu regla, hay que documentar que una fecha mal puesta no tiene remedio hasta que llegue, y limitar la fecha futura (el +90 es **[Propuesto, sin validar por CJ]**).

Dos detalles de la regla: define qué pasa si **ninguna** versión cumple `vigente_desde <= D` (propongo: no se espera a nadie ese día, y se evita guardando la primera versión como «desde siempre» como dices); y si dos versiones llevan la **misma fecha**, vale la de mayor `numero` (permite corregir un error del mismo día). Una versión con la misma máscara que la anterior sigue rechazada: conforme (4).

### 2. Fechas pasadas (pregunta c): admisibles, pero con constancia visible, no solo guardada

No es un problema de legalidad: art. 5.1.d RGPD exige poder rectificar sin dilación lo inexacto, y negar corregir una pauta mal registrada perpetuaría un dato erróneo. Pero tiene un efecto concreto que conviene decir claro:

- **Quitar días esperados con fecha pasada** puede sacar a alguien de la lista de seguimiento (los `NO_ASISTIO` ya anotados siguen en la base, pero dejan de contar como «días esperados seguidos»). Es el sentido de «falsa seguridad» de la lectura anterior.
- **Añadir días esperados con fecha pasada** no inventa ausencias: un día sin anotar sigue rompiendo la cadena.
- La fila ya guarda cuenta, perfil y momento (`0115:58-60`). **Eso basta como prueba**, y la prueba existe desde la inserción. Lo que falta es que **sea visible**: que el historial (punto 4) marque «Registrado el [fecha] con efecto desde [fecha anterior]» cuando `vigente_desde < fecha de registro`. Sin eso, la constancia existe pero nadie la ve.
- **La realidad gana a lo previsto** (criterio de `centro-de-dia-asistencia-lectura-dpo.md:75`): si hay fila de asistencia, se muestra aunque la pauta retroactiva diga que no tocaba.
- **Sin tope hacia atrás [Propuesto, sin validar por CJ].** Mi opinión es que, si CJ no pone tope, debe constar que la limitación efectiva es «no anterior a la versión en vigor» (punto 1), que ya acota el daño al tramo posterior a ese cambio.

### 3. Pantalla y permisos (pregunta b): el historial completo añade poco riesgo, con un matiz

**[Hecho]** Los cuatro perfiles ya ven la pauta vigente y la asistencia día a día, de la que se deduce la pauta real. El historial añade **una serie temporal de cambios** (cuándo se pasó de 5 a 3 días): por inferencia revela evolución de la necesidad de atención diurna y, a veces, un ingreso o un empeoramiento. Es información de la persona atendida, **no de salud directa**, pero mayor que «la pauta de hoy».

- **Administración, Enfermería y Medicina:** la necesitan (explican por qué una persona sale o no en la lista). Sí.
- **Auxiliar [Opinión]:** necesita la **vigente y, si existe, el próximo cambio** (para saber a quién anotar), no todo el historial. Recomiendo limitarlo; si Orlando prefiere uniformidad, es admisible.
- **Familia: nunca**, ni por la ficha ni por el Portal Familiar.
- El historial muestra **días, `vigente_desde`, fecha de registro y perfil** de quien lo hizo; **no el nombre de la cuenta** salvo que haga falta (la cuenta es dato personal del trabajador). **Sin campo de motivo ni nota** del cambio: sería texto libre con riesgo de salud.
- Sin datos en el título de la pestaña, la dirección ni `aria-label`; `Cache-Control: no-store` como la lista. El filtro lo aplica el servidor, no la vista.

### 4. Conservación y registro de actividades (pregunta d)

- **Sin categoría ni finalidad nuevas.** No hace falta un plazo nuevo, pero hay un **efecto sobre el plazo ya pendiente** de la pauta (`centro-de-dia-asistencia-lectura-dpo.md:168`): **una versión antigua no se puede purgar mientras sea la que determina lo «esperado» en algún día de asistencia que se conserva**. Añadir esa regla al catálogo de expurgo (`docs/privacidad/expurgo-y-supresion.md`) cuando se decida el plazo; si no, una purga rompería la lectura de los días antiguos. Lo demás (borrar o bloquear, art. 32 LOPDGDD) sigue pendiente del DPO real.
- **Registro de actividades (art. 30) del centro y de ResidApp:** conviene que la descripción de la pauta diga «con fecha de efecto e historial de versiones visible para Administración, Enfermería, Medicina [y Auxiliar]». No es obligatorio cambiar la estructura, sí que la descripción sea fiel. **EIPD:** una línea en el anexo de riesgos: cambios con efecto retroactivo sobre la lista de seguimiento.
- **Información (arts. 12 y 13):** nada nuevo que decir si ya se informa de la pauta; si no se informa, hay que hacerlo. **[A verificar]** que la política y la cláusula vigentes mencionan la pauta.
- **Auditoría (6): conforme.** Solo identificadores; ni máscara ni fecha. Extiende lo mismo a los **mensajes de error y a los registros técnicos**: que un rechazo («fecha anterior a la versión en vigor») no escriba la fecha ni los días.

### 5. Texto literal para la pantalla (pregunta e)

**Etiqueta del campo:** `Vale desde`

**Ayuda (siempre visible, bajo el campo):**
> Es el primer día en que esta pauta cuenta. Por defecto es hoy. Los días anteriores siguen con la pauta de antes.

**Aviso (solo si la fecha elegida es anterior a hoy):**
> Has elegido una fecha pasada. Cambiará los días en que se esperaba a esta persona desde esa fecha, y por tanto la lista de personas que llevan varios días sin venir. Úsalo solo para corregir un error. Quedará anotado quién lo hizo y cuándo.

**Aviso (solo si la fecha es futura):**
> Esta pauta empezará a contar el [fecha]. Hasta entonces sigue la de ahora.

**Errores:**
> «La fecha no puede ser anterior a la de la pauta que está en vigor ([fecha]).»
> «La fecha no puede ser posterior a [fecha límite].»

**Historial (cabecera y filas):**
> **Pautas anteriores**
> Desde el principio: lunes, miércoles y viernes.
> Desde el 3 de marzo: lunes y miércoles. Anotado el 10 de marzo por Administración.

(«Desde el principio» es lo que se muestra cuando `vigente_desde` es `0001-01-01`; no mostrar nunca esa fecha.)

## A verificar o decidir

- **[Decidir] Orlando / ingeniería:** regla del punto 1 (versión de mayor `numero` con `vigente_desde <= D`; fecha mínima = la de la versión en vigor).
- **[Decidir] Orlando:** si el Auxiliar ve solo la vigente y la próxima, o todo el historial.
- **[Propuesto, sin validar por CJ]:** los 90 días hacia delante, la ausencia de tope hacia atrás y la regla de «misma fecha».
- **[Decidir] DPO real:** plazo de las versiones antiguas, borrar o bloquear, y que el historial visible sea proporcionado.
- **[A verificar]** que la matriz de permisos y las pruebas de `0091` y de cobertura de seguridad por filas no necesitan cambios (la columna no toca permisos; no he leído `0091`).
- **[A verificar]** que la zona usada para «hoy» sea la de `zonas_vigentes_centros` también en el disparador, como en `0115:197-199`, y no la del servidor.
- **[Suposición]** que no hay filas de `asistencia_pauta` con datos reales aún (el valor por defecto `0001-01-01` solo es neutro así).

No soy DPO designado ni abogado.
