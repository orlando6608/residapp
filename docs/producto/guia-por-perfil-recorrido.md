# Guía por perfil: recorrido con sesión real

Registro de qué fichas de la Guía se han recorrido de verdad en la aplicación, con una cuenta real del perfil, y qué se encontró. Es la
base para sellar la Guía (`revisadoPara`): un sello sin recorrido solo diría que las pruebas automáticas pasan, no que una persona puede
seguir las fichas. Plan y decisiones: `guia-por-perfil-plan.md`.

**Cómo se recorre.** En la base local (`ACER-ORLANDO/ResidApp`), con la skill `probar-en-navegador` (Chrome sin ventana): se entra con la
cuenta del perfil (las cuentas `*-orlando` de los centros de prueba, o cuentas nuevas sembradas) y se sigue cada ficha paso a paso, con los
textos exactos de pantalla. Se anota lo que no coincide y se corrige la ficha. Las cuentas y datos de prueba se quedan en la base local
(la auditoría no se borra). No se usan datos reales.

**Estados.** *Recorrida*: los pasos y lo que dice que pasa después coinciden con la pantalla. *Recorrida con cambios*: se corrigió algo.
*No recorrida*: sin motivo para no hacerlo, pendiente. *No automatizable*: depende de un dispositivo o de algo que el guion no puede hacer.

## Plataforma (2026-11-06, cuenta `plataforma-orlando`)

| Ficha | Estado | Qué se vio |
|---|---|---|
| PLA-01 Entrar con passkey | No automatizable | Hace falta una passkey real. Sí se vio que, sin passkey, tras la contraseña y el código la app lleva a «Mi cuenta» (como dice la ficha), que «Mi panel» lleva a «Centros» y que ahí está «Nuevo centro». En «Mi cuenta», antes de elegir ámbito, la cabecera no trae «Mi panel» ni «Cambiar ámbito» (en otros perfiles, con ámbito elegido, sí): no es un error de la ficha. |
| PLA-02 Crear un centro nuevo | Recorrida con cambios | Los siete campos y los botones son los de la ficha; el aviso «Centro creado. Para que su administrador pueda entrar, pulsa «Administraciones y unidades»…» coincide; la hoja trae usuario, centro, «Vale hasta» (72 horas), código QR e «Imprimir». **Cambio:** la ficha decía que la nueva Administración «entra en el Inicio de Administración» y lo encuentra «sin usuarios ni estructura». En realidad entra al Inicio general y, con «Mi panel», llega al Inicio de Administración, que tiene 0 residentes, su propia cuenta, la unidad que se creó con el centro y «No hay nada pendiente». Cerrado el hueco C8 del plan. |
| PLA-03 Crear la invitación de una Administración | Recorrida | Al crear otra hoja, abrir la anterior da «Esta invitación ya no vale: se ha usado, se ha anulado o ha caducado», como dice la ficha. |
| PLA-04 Administración principal | Recorrida | «Quitar la marca de principal» → «Ya no es Administración principal.»; «Marcar como Administración principal» → «Marcada como Administración principal.»; el primer administrador de un centro nuevo ya sale «Principal». |
| PLA-05 Añadir una unidad al ámbito | Recorrida | Con una segunda Administración (dada de alta por la primera, ADM-10) sin la unidad nueva (ADM-03): sale «Fuera de su ámbito» y «Añadir a su ámbito»; el aviso es «Unidad añadida al ámbito de esa Administración. Queda en la auditoría.» y la unidad pasa a «En su ámbito». |
| PLA-06 Ver, suspender o reactivar una cuenta de varios centros | Recorrida | La fila trae nombre, identificador, centros y perfiles; «Suspender» no pide confirmación y avisa «Cuenta suspendida en todos sus centros: no podrá entrar hasta que la reactives.»; sale la etiqueta «Suspendida» y el botón pasa a «Reactivar» («Cuenta reactivada.»). |
| PLA-07 Invitación a una cuenta de varios centros | Recorrida | Con la cuenta suspendida no hay botones de invitación; activa, sale «Invitación de recuperación» y la hoja (usuario, centro «Plataforma (operación de ResidApp)», «Vale hasta», QR, «Imprimir»). |
| PLA-08 Añadir o quitar una passkey | No automatizable | Hace falta un dispositivo con passkey, y registrar una en la cuenta compartida `plataforma-orlando` dejaría sin servir su contraseña y su código (y la última no se puede quitar). Pendiente de que una persona lo pruebe con una cuenta de Plataforma propia. |

Incidencias del propio recorrido: sin errores de consola ni desplazamiento lateral en las pantallas recorridas. Quedaron en la base local tres
centros de prueba («Residencia guía …», dos de ellos de intentos que se interrumpieron) con sus administradores.

## Auxiliar (2026-11-06, cuenta `almendros-orlando-auxiliar` y una cuenta nueva sin residentes)

| Ficha | Estado | Qué se vio |
|---|---|---|
| AUX-01 Ver mis residentes y las etiquetas | Recorrida | «Mi panel» abre «Mis residentes»; cada tarjeta trae foto o iniciales, nombre y «unidad · habitación · plaza» (o «Sin habitación asignada»), «Basal vigente»/«Basal pendiente» y «Cierre cotidiano: Pendiente»/«Cerrado hoy». «Cerrado hoy» sale tras un «Sin cambios», un «No valorable» y un cambio (ordinario o prioritario). Un residente sin basal se abre y se cierra igual. Con una cuenta sin residentes sale «No tienes residentes asignados. Los asigna Administración: pídeselo si deberías tenerlos.» **Sin recorrer:** «solo ves a los asignados que están en tus unidades» y el aviso del residente suspendido por ingreso hospitalario (hace falta Administración; se verá en su recorrido). |
| AUX-02 Consultar el basal y la foto | Recorrida | «Estado basal vigente» trae «Barthel total: 100 / 100» y «Firmado el …»; «Consultar estado basal» abre «Índice de Barthel» y las nueve áreas de la ficha, sin ningún botón. La foto sale en la cabecera. Sin basal: «Sin basal vigente registrado para este residente.» |
| AUX-03 Cerrar el turno sin cambios | Recorrida | Tres tarjetas («Sin cambios», «Registrar cambio», «No valorable») más la de «Estado basal vigente»; «Confirmar sin cambios» guarda sin confirmación y avisa «Cierre registrado: sin cambios.» |
| AUX-04 Marcar como no valorable | Recorrida | Sin motivo, el navegador no deja enviar (campo obligatorio, máximo 500 caracteres); con motivo, «Cierre registrado: día marcado como no valorable.» |
| AUX-05 Registrar un cambio ordinario | Recorrida | Las diez áreas, las opciones rápidas y «Añadir nota», la temperatura (el navegador rechaza lo que sale de 34,0 a 42,0), «Ordinario» marcado por defecto, barra «Continuar» fija abajo, «Resumen» con «Volver a editar» (conserva casillas, notas y temperatura) y «Confirmar y enviar». Solo con temperatura sale «Solo has anotado la temperatura…». Aviso final «Cambio registrado: enviado a la bandeja ordinaria de Enfermería.» y el cambio aparece en la «Bandeja de cambios ordinarios» de Enfermería. |
| AUX-06 Registrar un cambio prioritario | Recorrida | Sin motivo: «Elige el motivo prioritario.»; los seis motivos coinciden; la confirmación avisa «Evento prioritario: recuerda aplicar el protocolo del centro. Antes de confirmar, documenta el aviso directo.»; «Aviso directo documentado» es obligatorio; «Confirmar y enviar» es rojo. Aviso final «Cambio registrado: enviado a la bandeja prioritaria de Enfermería.» y el evento aparece en la «Bandeja prioritaria» de Enfermería con el motivo y el aviso escritos. |
| AUX-07 Dictar el cambio por voz | No automatizable | En la base local el dictado no está activado y el guion no tiene micrófono ni servicio de transcripción. Sí se vio que, sin él, «Registrar cambio» no muestra el bloque «Dictar el cambio» y se rellena a mano, como dice la ficha. |
| AUX-08 Corregir o deshacer lo registrado | Recorrida | Tras confirmar, la ficha del residente solo ofrece «Confirmar sin cambios», «Registrar cambio» y «Marcar no valorable»: no hay botón para editar ni anular. «Volver a editar» antes de confirmar sí conserva lo escrito. |

Ninguna ficha de Auxiliar necesitó cambios. Sin errores de consola. Nota para quien repita el recorrido con un guion: al rellenar un campo con
`fill()` después de un intento de envío inválido, la validación del navegador puede dejar el botón sin enviar; una persona que teclea no
tiene el problema (se comprobó con teclado real).

Datos que quedaron en la base local: en Los Almendros (cuentas `*-orlando`), un «Sin cambios» (Carmen), un «No valorable» (Lucía), un cambio
ordinario (Manuel) y uno prioritario (Rosa); y un centro nuevo «Residencia de prueba (navegador …)» con su cuenta de Auxiliar sin residentes.

Pendiente en las demás guías: Familia, Medicina, Enfermería, Dirección, Administración y «Para todos».
