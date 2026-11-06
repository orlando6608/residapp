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

## Familia (2026-11-06, cuentas `almendros-orlando-pablo` y una cuenta nueva de familiar con tres residentes)

Para recorrerlas hizo falta preparar el lado de Administración y de Enfermería por la pantalla, como lo haría el centro: se dio de alta un
familiar y se vinculó a tres residentes (ADM-27), se abrieron y activaron sus autorizaciones (ADM-29), se le dio acceso al portal y se
entregó su invitación (ADM-30); Enfermería valoró y cerró un evento preparando una comunicación (ENF-05 a ENF-07) y Administración la publicó
antes de su hora (ADM-34) y publicó una corrección (ADM-35); y se cambió el modo del servicio de citas (ADM-37) y la hora de corte (ADM-36),
restaurando después los dos.

| Ficha | Estado | Qué se vio |
|---|---|---|
| FAM-01 Entrar al portal y cambiar de residente | Recorrida | La invitación abre «Elige tu contraseña» (solo contraseña, sin código del móvil) y «Guardar y continuar» lleva al portal. Con un residente se entra directo a su pantalla; con varios sale «Mis residentes» con una tarjeta por residente (y «Propuesta de cita pendiente» o «N novedades nuevas» cuando toca). «Cambiar de residente» vuelve a esa lista, «Salir» cierra la sesión y «Iniciar sesión» (Usuario, Contraseña, «Entrar») vuelve a entrar; con clave equivocada sale «Usuario o contraseña incorrectos. Tras 5 intentos fallidos seguidos, la cuenta se bloquea 15 minutos.» En el móvil (375 px) «Cambiar de residente» y «Salir» están tras el botón de menú. Con las autorizaciones suspendidas sale «Ahora no tienes acceso a ningún residente» y, al reactivarlas, vuelve todo. |
| FAM-02 Leer las novedades | Recorrida | «Hay N novedades nuevas desde tu última visita» y la etiqueta «Nueva» en la primera visita; los filtros «Todas», «Relevantes» y «Ordinarias»; la novedad abierta con su tipo, fecha, texto y firma «Equipo asistencial del centro»; «Tiene una corrección» → «Ver la corrección» → «Ver la novedad original»; «Volver a las novedades». |
| FAM-03 Resumen diario o semanal | Recorrida | Con el corte pasado, sale la tarjeta «Resumen diario», con «Lo que el equipo ha publicado entre el … y el …, todo junto», las novedades (original y corrección) y «Son las mismas novedades de más abajo.»; la etiqueta «Nuevo» sale solo en la primera visita tras el corte. Para verlo hizo falta adelantar la hora de corte (ADM-36); hoy, con el horario normal, saldría tras las 19:00. |
| FAM-04 Reservar una cita | Recorrida | «Citas con el equipo asistencial» → «Reservar una cita» → huecos por día bajo cada equipo y modalidad → «Revisa tu reserva» (residente, con quién, cómo, cuándo) → «Confirmar la reserva» → «Cita confirmada.» con estado «Confirmada». |
| FAM-05 Solicitar una cita | Recorrida | «Solicitar una cita» con los campos de la ficha (con quién, cómo, motivo, comentario, días, parte del día); enviada vacía sale «Elige con quién quieres la cita.», «Elige cómo quieres la cita.» y «Elige el motivo de la cita.»; bien rellena, «Solicitud enviada.» y estado «Pendiente de gestión». |
| FAM-06 Responder a la propuesta | Recorrida | «Propuesta de cita pendiente» en «Mis residentes» y «Tienes una propuesta de cita esperando tu respuesta» con «Ver la propuesta»; «El centro te propone…» con el plazo; «Pedir otro horario» avisa «Hemos avisado al centro de que necesitas otro horario. Te propondrá uno nuevo.» y Administración lo ve; «Aceptar y confirmar la cita» da «Cita confirmada.»; «Cancelar la solicitud» pide confirmar («Sí, cancelar la solicitud» / «No, mantenerla») y da «Solicitud cancelada.» Sin recorrer: la propuesta que caduca sin respuesta. |
| FAM-07 Cambiar o cancelar una cita | Recorrida | «Reprogramar» → hueco nuevo → «Revisa el cambio» con «Hueco nuevo» y «Hueco actual (se libera al confirmar)» → «Confirmar el cambio» → «Cita reprogramada.»; «Cancelar la cita» → «¿Cancelar esta cita?» → «No, mantenerla» la deja y «Sí, cancelar la cita» da «Cita cancelada. El hueco queda libre.»; el «Historial» anota cada cambio. Sin recorrer: el límite de antelación mínima (no hay cita a menos de 24 horas) y el aviso por correo. |
| FAM-08 Qué información me corresponde y qué hacer si es urgente | Recorrida | Ninguna pantalla muestra valoraciones, constantes, basal, medicación, historial, indicaciones ni derivaciones; no hay imágenes ni con un residente que tiene foto (Manuel); cada pantalla recuerda «Este portal no es un canal urgente.» |

Ninguna ficha de Familia necesitó cambios. Sin errores de consola ni desplazamiento lateral.

De paso se observó en otras guías, para confirmarlo cuando les toque: **ENF-05** dice que, tras «Empezar valoración», se pulsa
«Completar valoración» (o «Continuar valoración»); en la pantalla, «Empezar valoración» abre directamente el formulario de la valoración
(«Guardar borrador», «Volver sin guardar»), y «Completar valoración» solo aparece al volver al detalle sin haber guardado. ADM-27, ADM-29,
ADM-30, ADM-34, ADM-35, ADM-36, ADM-37 y ADM-40 funcionaron como dicen sus fichas en lo que se usó; ENF-05 a ENF-07 y ENF-14 también (queda ENF-14
sin pulsar «Corregir»).

Datos que quedaron en la base local, en Los Almendros: el familiar «Familiar guía 222147» (cuenta `guia-fam-222147`, clave solo en la carpeta
temporal de la sesión) vinculado a Antonio, Manuel y Rosa con autorización Activa; una cita confirmada de Antonio (lunes 09/11, 11:00) y otra
cancelada; solicitudes de cita resueltas o canceladas; el evento de Manuel valorado y cerrado, con su comunicado publicado y una corrección.
El servicio de citas volvió a «Reserva directa» y el horario de comunicados a 19:00, 60 minutos y resumen semanal.

## Medicina (2026-11-06, cuenta `almendros-orlando-medicina`)

Para tener qué valorar se crearon por pantalla los eventos necesarios: eventos propios de Medicina (Carmen, Antonio), uno de Auxiliar que
Enfermería valoró y escaló (Rosa, ENF-08) y la lectura y cumplimiento de una indicación por Enfermería (ENF-11).

| Ficha | Estado | Qué se vio |
|---|---|---|
| MED-01 Orientarse en el Inicio | Recorrida | «Mi panel» abre el «Inicio de Medicina» con las tarjetas «Escalados y eventos propios», «Protocolos urgentes», «Seguimientos», «Indicaciones» (con «sin leer»), «Residentes» y «Comunicaciones», con sus recuentos. Un evento con indicación pendiente ya no cuenta en «Escalados». Sin recorrer: el aviso de hitos fuera de plazo (con los plazos de ahora no llegó a salir). |
| MED-02 Ver mis residentes y abrir su ficha | Recorrida | Filtros «Nombre», «Estado basal» y «Unidad» con «Buscar» y «Limpiar»; tarjetas con foto o iniciales, ubicación y «Basal vigente»/«Basal pendiente»; la ficha trae «Registrar evento», «Ver historial», «Eventos abiertos», «Estado basal vigente», «Iniciar reevaluación» o «Crear borrador de basal» (esta cuenta tiene el permiso) y el contacto urgente. |
| MED-03 Registrar un evento propio | Recorrida | Campos «Observación», «Clasificación inicial» (Ordinario, Prioritario) y «Datos clínicos pertinentes»; vacío sale «Indica qué observas.»; «Guardar y valorar» avisa «Evento registrado. Completa tu valoración médica.» y abre la valoración; el evento queda «En valoración médica» y Enfermería no lo recibe. |
| MED-04 Valorar un escalado | Recorrida con cambios | El escalado de Enfermería muestra su motivo, constantes y actuaciones; «Iniciar valoración médica» registra quién y cuándo y **abre directamente el formulario** (la ficha decía que después se pulsaba «Completar valoración médica»: corregida; ese botón solo sale al volver al detalle sin guardar). Bajo cada constante sale «Referencia del centro: …»; «Guardar borrador» avisa «Valoración médica guardada.» |
| MED-05 Elegir la conducta y dar una indicación | Recorrida | Las cuatro salidas; la indicación pide texto («Escribe la indicación.») y fecha prevista o criterio; avisa «Indicación registrada. Enfermería de la unidad la verá en su bandeja de indicaciones.»; el evento pasa a «Con indicación pendiente» y la tarjeta «Indicaciones» la muestra «Pendiente de lectura»; Enfermería la lee y la registra como realizada (ENF-11). |
| MED-06 Seguimiento médico | Recorrida | «Iniciar seguimiento médico» (objetivo, fecha o criterio) → «Seguimiento médico iniciado.»; «Registrar revisión», «Reprogramar» (sin justificación: «Para reprogramar indica una nueva fecha o criterio y justifica el cambio.»), «Transferir» al equipo entrante con su nota («Transferencia registrada…»), «Confirmar recepción» (opcional) y «Resolver», que lleva a la conducta («Estás resolviendo el seguimiento médico…»). Sin recorrer: «Conservar» y el mensaje de unidad sin equipos. |
| MED-07 Protocolo urgente | Recorrida con cambios | Activar con nota opcional; el protocolo ofrece «Registrar actuación», «Registrar evolución» y «Registrar contacto» (la sección se llama «Registrar contacto con un servicio»; **ficha corregida**, decía que era el nombre del botón); un contacto con hora futura da «Para registrar un contacto indica el servicio y la hora del contacto, que no puede ser futura.» |
| MED-08 Derivar a Urgencias | Recorrida con cambios | Motivo (obligatorio: «Escribe el motivo de la derivación.») y datos opcionales; la vista previa marca cada sección «Dato automático» o «Escrito por ti» con su huella; «Firmar y generar PDF» avisa «Informe de derivación firmado…» y «Descargar el informe en PDF» baja un PDF válido; «Registrar la llamada» (**«A quién se llama» es un texto que se escribe, no una lista**: ficha corregida), «Hora de la llamada» y «Resultado»; sin llamada sale «Todavía no consta ningún intento de llamada. Hace falta al menos uno para cerrar el evento.» |
| MED-09 Cerrar el episodio | Recorrida con cambios | «Cerrar el evento» con resumen y «Pendientes»; sin decidir sale «Decide si se comunica a la familia.»; «Evento cerrado. Enfermería no tiene que cerrarlo.» Tras derivar a Urgencias viene «Sí: Preparar comunicación» y el tipo solo admite **Relevante** (añadido a la ficha). Sin recorrer: el botón desactivado con indicaciones sin cumplir (la tarjeta «Cerrar el evento» de la conducta sigue activa con una indicación pendiente; la ficha dice «el botón sale desactivado»: **a confirmar** con otro evento). |
| MED-10 Corregir o rectificar mi valoración | Recorrida (solo la corrección) | Dentro de las 6 horas, «Corregir valoración» con «Motivo de la corrección» y «Guardar corrección» avisa «Valoración médica corregida.»; el detalle muestra «Valoración corregida por su autor» con su motivo y «Puedes corregirla hasta …»; «Añadir rectificación» no sale dentro del plazo. **Sin recorrer:** la rectificación (a partir de las 19:33 de hoy para la valoración de Carmen). |
| MED-11 Corregir un comunicado antes de publicarse | Recorrida | «Comunicaciones» lista el comunicado con «Se publica el …»; «Corregir» ofrece «Tipo de comunicación» y «Texto para la familia»; «Guardar la corrección» avisa «Comunicado corregido. La familia verá el texto corregido.» |
| MED-12 Historial y línea temporal | Recorrida | «Ver historial» con «Eventos cerrados» (cada uno con su basal y unidad de la fecha), «Ver detalle», «Ver ese basal», «Versiones del basal» con «Ver versión» y «Ver línea temporal» por días con «Ver evento». Sin recorrer: «Episodios cerrados de la unidad anterior»: con una cuenta que ve las dos plantas no sale tras un traslado (la ficha ya lo dice como condicional); haría falta un profesional cuyo ámbito no incluya la unidad anterior. |
| MED-13 Crear, completar y firmar el basal | Recorrida | «Crear borrador de basal» (Motivo, Fuente de la información, Fecha) → «Borrador creado. Completa ahora las nueve áreas y el índice de Barthel.»; cada área «guardada» y «Completada»; «Guardar Barthel» → «Índice de Barthel guardado.»; «Confirmar y firmar» → «Firmar estado basal» → «Basal firmado»; la ficha muestra después «Iniciar reevaluación». Sin recorrer: que sin el permiso el botón no sale (necesita una cuenta de Medicina sin permiso). |

Cuatro retoques de redacción en `medicina.json` (MED-04, MED-07, MED-08 y MED-09); las pruebas de la Guía (29) siguen en verde. Sin errores de
consola.

De paso se confirmó una discrepancia en **ENF-05**, para corregir en el recorrido de Enfermería: «Empezar valoración» abre directamente el formulario
de la valoración, no el detalle con «Completar valoración». También funcionaron como dicen sus fichas ENF-08 (escalar, con «Escribe el motivo del
escalado.» y «Evento escalado a Medicina.»), ENF-10 (sin seguimientos abiertos en Enfermería: la transferencia a «Enfermería Planta 1» no le sale
a la bandeja de Seguimientos; **a confirmar** en su recorrido), ENF-11 (en lo usado) y ADM-22 (trasladar).

Datos que quedaron en la base local, en Los Almendros: eventos cerrados de Carmen (con su comunicado ya corregido y pendiente de publicar a las 19:00), de Antonio (sin comunicación) y de Rosa (derivada a Urgencias con informe firmado y su comunicado relevante pendiente); basal firmado de Carmen; Antonio
trasladado a Planta 2 y devuelto a Planta 1 · 101 · A; un PDF de prueba en la carpeta temporal de la sesión.

Pendiente en las demás guías: Enfermería, Dirección, Administración y «Para todos».
