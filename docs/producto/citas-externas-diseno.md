# Citas externas de los residentes: diseño y valores por defecto

**Estado: PROPUESTA de ingeniería (2026-11-17), pendiente del visto bueno de Orlando. Sin SQL ni código todavía.** Parte de las respuestas de CJ del 2026-11-17 (`citas-externas-modulo-cj`, 14 de 14; viven en el servidor), de la lectura del DPO (`docs/privacidad/citas-externas-lectura-dpo.md`, borrador) y de las decisiones de Orlando del mismo día: **módulo base** con interruptores por centro para la familia y los correos (apagados por defecto), interruptor por residente y acción «Ya no puedo».

## 1. Qué es

Un registro de las citas del residente **fuera del centro** (pruebas, consulta, tratamiento en hospital de día o fisioterapia, otra), con un calendario para el personal y, después, para la familia, y avisos cuando a una cita le falta transporte o acompañante. Es una agenda y un recordatorio: no organiza turnos ni rutas, no adjunta informes, no tiene chat ni se sincroniza con nada de fuera (CJ lo confirmó, 5.1). **Toda la ficha de la cita es dato de salud**, por eso lleva campos cerrados y límites cortos.

## 2. Valores por defecto para lo que falta

Son propuestas mías, elegidas por ser lo más prudente y lo más fácil de cambiar después. Si no estás de acuerdo con alguna, dímelo antes de que escriba el script.

| # | Decisión | Valor por defecto | Por qué |
|---|---|---|---|
| 1 | **Día y hora** | Se guardan como **fecha del centro (hora de Madrid) y hora opcional**; «hora por confirmar» es hora vacía. No se guardan en UTC | Un calendario por días y los avisos «a 7 y a 2 días» se calculan por fecha; en UTC, una cita a las 00:30 caería en el día equivocado |
| 2 | **Quién registra y cambia** | **Administración siempre** (como las citas de ahora). **Enfermería solo con un permiso nuevo `EXTERNAL_APPOINTMENT_MANAGE`**, apagado por defecto, que concede Administración por perfil (como el del basal). **Medicina solo ve** | Es lo que dijo CJ (3.1 C) con el permiso por perfil; apagado por defecto lo pide el DPO (art. 25.2). En el centro piloto lo dejaremos concedido |
| 3 | **Quién ve** | Administración, todo el centro. Enfermería y Medicina, **las citas de los residentes de sus unidades** (hoy). Auxiliar, solo hoy y mañana (PR 2). Dirección Clínica y Plataforma, no | CJ 3.2 y lectura del DPO (tabla del apartado 2). El filtro por unidad lo hace la aplicación; la base filtra por centro |
| 4 | **Tipo** | Lista cerrada de CJ: **prueba**, **consulta**, **tratamiento** y **otra** | CJ 1.2 |
| 5 | **Especialidad** | Lista cerrada, sin texto libre, con una marca «visible a la familia por defecto». Ocultas por defecto las del DPO (salud mental, oncología, infecciosas, adicciones, paliativos, genética) **y, hasta que CJ decida, ginecología, urología y proctología** | Lo prudente: mostrar una especialidad luego es fácil; ocultarla cuando ya se vio, no. En este PR solo la ve el personal |
| 6 | **Lugar** | Una línea de **80 caracteres**, solo el nombre del centro sanitario; la base rechaza saltos de línea. Ayuda visible junto al campo | Condición del DPO: «unidad de oncología» sería la especialidad |
| 7 | **Qué preparar** | Casillas (ayuno, último informe, tarjeta sanitaria, medicación, silla de ruedas, otra) **más una línea de 100 caracteres** con ayuda | Condición del DPO: «suspender el anticoagulante» revela medicación |
| 8 | **Transporte y acompañante** | Cada uno con un **modo** (transporte: familia, centro, ambulancia, sin determinar; acompañante: familiar, personal del centro, lo lleva el transporte, no necesita, sin determinar) y un **estado** (pendiente o confirmado). «No necesita» queda confirmado solo; «sin determinar» siempre pendiente | Es la ficha 2.1 que CJ validó |
| 9 | **«Cubierta» o «falta algo»** | Cubierta si transporte y acompañante están confirmados o no hacen falta. Con hora por confirmar sale «Por confirmar» | Es lo que enseñan los bocetos de CJ |
| 10 | **Estado de la cita** | **Programada**, **confirmada por el hospital**, **realizada**, **no se acudió**, **cancelada**. «Reprogramada» no es un estado: es un cambio de fecha que queda en el historial | Un estado «reprogramada» sería un callejón sin salida (¿y luego qué?); el historial ya lo cuenta |
| 11 | **Cancelar o reprogramar** | Motivo de **lista cerrada** más, opcional, una línea de **100 caracteres** con ayuda «sin diagnósticos ni nombres». Una cancelación por baja lleva el texto fijo de la aplicación | Condición del DPO |
| 12 | **Baja del residente** | Las citas externas **futuras se cancelan solas**, con causa fija `BAJA_RESIDENTE` y texto fijo, en la misma transacción que la baja (patrón de `0078`) | Lectura del DPO, 6.3 |
| 13 | **Suspensión** | Las citas **siguen vivas** (la cita es real), con una marca «residente suspendido» para el personal; la familia no recibe avisos | Opinión del DPO; la confirma CJ |
| 14 | **Dos personas editan a la vez** | La cita lleva un **número de versión**; si alguien guardó antes, la segunda persona recibe «Otra persona ha cambiado esta cita» y no pisa | Patrón de la configuración de citas (`0054`) |
| 15 | **Historial y auditoría** | Cada cambio es una fila de solo añadir (quién, perfil, cuándo, acción, causa), igual que las citas de ahora. La auditoría lleva solo identificadores, nunca el contenido de un campo | Lectura del DPO, 6.4 |
| 16 | **Aviso a Administración en su inicio** | Lista «Citas externas con algo sin cubrir» para las que empiezan **dentro de 7 días** (a **2 días o menos**, más destacado), con residente, fecha y «falta transporte/acompañante», **sin especialidad ni lugar**. Se calcula al abrir el inicio, sin tabla ni tarea programada. **Solo Administración** (no Enfermería, salvo que un centro lo pida) | CJ 3.3 con su comentario; DPO 5.1 |
| 17 | **Nota «al terminar»** | PR siguiente: lista cerrada («sin cambios», «nueva cita», «pendiente de informe», «cambio de pauta», «traslado», «otra») más 150 caracteres, con versiones y autoría; la escribe Enfermería (y Medicina no, por ahora) y la leen **Enfermería y Medicina, no Administración** | Lectura del DPO, apartado 7; CJ 2.2 B. **La lectura solo por personal clínico la confirma CJ** |

## 3. Cómo se construye

Seis PR pequeños, cada uno con su revisión de calidad y su documentación de usuario:

1. **Esquema y reglas (sin pantallas).** Script `0101`: tablas `citas_externas` y `citas_externas_cambios` (solo añadir), seguridad por filas por centro, permiso `EXTERNAL_APPOINTMENT_MANAGE`, la matriz de `0091`, el catálogo de expurgo y la causa de baja. Dominio, puerto y repositorio con pruebas de aislamiento (otro centro, otra unidad, Plataforma).
2. **Pantallas del personal.** Registrar, cambiar, cancelar y dar por realizada una cita (lista «Próximas»), con las ayudas y límites de campos, y el aviso del inicio de Administración.
3. **Calendario del personal** mensual y semanal y la vista del Auxiliar de hoy y mañana (sin especialidad, lugar, nota ni nombre del familiar; prueba por reflexión).
4. **Nota «al terminar» y siguiente cita** (tabla de versiones, solo personal clínico).
5. **Familia, solo ver:** los interruptores por centro y por residente, el nivel de visibilidad por cita y el calendario de la familia.
6. **Familia, actuar y correo:** «Me encargo yo», «No puedo» y «Ya no puedo» (asignación atómica, identificador y no nombre), y los correos nuevos (un mensaje por persona, con la comprobación antes de enviar).

**Antes de ofrecer la parte de la familia con familias reales** hacen falta las respuestas de CJ a las preguntas que quedan (apartado 4) y la validación de una persona experta.

## 4. Lo que queda por cerrar con CJ

Un documento breve para CJ (después de aprobar este diseño): los **tres niveles de visibilidad** por cita (completa, solo general, oculta), la **lista de especialidades ocultas por defecto** (incluidas las tres que dejo ocultas), que la **nota «al terminar»** la lean solo Enfermería y Medicina, si el **aviso de 7 días** se limita a citas en las que la familia tiene algo que cubrir y si el **Auxiliar** necesita saber quién recoge al residente. Y, para Orlando y el DPO, la titularidad del centro piloto (AEPD o APDCAT).
