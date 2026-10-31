# Aviso por correo cuando el centro propone una cita: lectura de privacidad y condiciones de construcción

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-10-30. Segunda lectura experta, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL), **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado.

**Alcance:** solo la primera pieza del ADR 0012 (`docs/decisiones-arquitectura/0012-envio-correos.md`): el aviso genérico cuando Administración propone una cita (CJ, tercera tanda 6.1, opción C). El recordatorio del día antes y el código de acceso quedan fuera y exigirán su propia lectura.

## Conclusión

**Admisible con condiciones.** Un correo que dice solo «tienes una propuesta de cita pendiente en ResidApp», sin nombres, sin fecha y sin motivo, no contiene datos de salud (art. 4.15 y 9 RGPD) y es proporcionado. El riesgo más importante **no es el texto, es a quién llega**: la dirección la escribe Administración (no la familia) y no se verifica (`FamilyMember.cs`, líneas 143 a 148: solo comprueba una «@»). Si está mal escrita, un desconocido recibe un aviso de «ResidApp». Con texto genérico el daño es bajo, pero hay que decirlo a la familia antes (art. 13) y no enviar nunca a quien ya no debe recibirlo (baja, suspensión, vínculo roto).

Se puede construir **ya con datos de prueba**, con el envío apagado por defecto. **Antes de la primera dirección real** hace falta que una persona valide la información a la familia, el procedimiento de confirmar la dirección y el contrato de Azure (apartado 4).

## 1. Qué hace hoy el código [Hecho]

- Proponer es `AppointmentRequestsApplicationService.ProposeAsync` (`AppointmentRequestsApplicationService.cs`, líneas 31 a 42) y delega en `SqlAppointmentRequestRepository.ProposeAsync` (líneas 54 a 82): una transacción que reserva el hueco, pasa la solicitud a `PROPUESTA`, escribe el paso en `citas_solicitudes_cambios` y la auditoría `APPOINTMENT_REQUEST_PROPOSE` (`AppointmentRequestSql.cs`, líneas 81 a 101), y hace `Commit` (línea 81). El servicio de aplicación solo recibe un `true`: no sabe qué familiar ni qué número de cambio. **El envío debe engancharse después del `Commit`, nunca dentro.**
- La propuesta caduca a las 48 horas o 24 horas antes de la cita, lo que llegue antes (`0070_...sql`, líneas 22 a 47). Una solicitud puede volver a `PENDIENTE` («otro horario») y de nuevo a `PROPUESTA` (`0070`, líneas 56 a 72): **una misma solicitud puede tener varias propuestas el mismo día.**
- La solicitud guarda `vinculo_id` y `cuenta_id` de la persona que la envió (`0057_...sql`, líneas 21 a 39). El vínculo lleva al `familiar_id`, y `dbo.familiares.correo` es `NVARCHAR(254) NULL` (`0022_...sql`, línea 25). La cuenta del portal está en `familiares.cuenta_id`, también opcional (`0052_...sql`, líneas 17 a 21).
- Quién puede recibir ya está definido en `SqlFamilyPortalReader.AccessibleLinks` (`SqlFamilyPortalReader.cs`, líneas 23 a 38): vínculo no desvinculado, autorización más reciente `ACTIVA` y vigente, residente `ACTIVE` y sin suspensión abierta.
- La baja cierra las solicitudes abiertas (`0078_...sql`, líneas 1 a 9), pero **una suspensión no las toca**: una propuesta puede seguir viva con el residente suspendido, y en ese caso el portal ya no la deja ver (`AccessibleLinks`). El correo no debe adelantarse al portal.
- El correo se edita sin historial: `UPDATE dbo.familiares SET ... correo = @Email` y una auditoría solo con la acción (`SqlResidentFamilyRepository.cs`, líneas 151 a 165). El formulario lo llama «Correo electrónico (opcional)» y no explica para qué se usará (`AdministracionModels.cs`, líneas 135 a 138).
- `eventos_auditoria` exige una cuenta que actúa y un perfil de una lista fija (`0002_...sql`, líneas 707 y 719): **un envío del sistema no cabe ahí** sin inventar un actor.
- Nada de esto está construido: no hay `IEmailSender`, ni tarea programada, ni tabla de envíos (ADR 0012, línea 7).

## 2. Preguntas 1 a 7

### 2.1 (P1) ¿Es admisible, y qué hace falta antes del primer correo real?

**Con condiciones.** Razones:

- **Base jurídica [Suposición, Decidir DPO].** La dirección es un dato personal del familiar, no de salud. Art. 6.1.b (servicio que el centro presta a la familia) cuando el familiar es representante o parte; art. 6.1.f (interés legítimo del centro en coordinar visitas y citas) en el resto; art. 6.1.e si el centro es público. No hace falta consentimiento si el aviso es operativo y no comercial. La LSSI prohíbe las comunicaciones comerciales no solicitadas por correo; un aviso de servicio no lo es **[Suposición; a verificar con abogado, no cito el artículo de memoria]**. El art. 9 no entra: el correo no lleva dato de salud.
- **Finalidad nueva (art. 6.4).** Hoy el correo se recogió como «contacto (opcional)», sin explicar que se usaría para avisos de la aplicación. Es compatible si se le dice a la familia **antes** (cláusula del apartado 6).
- **Contradicción con FAM-10 [Hecho].** FAM-10 dice «sin email, SMS, WhatsApp ni push en el piloto» (`docs/legado-cloudflare/docs/product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md`, línea 339; ADR 0007, línea 114). CJ decidió lo contrario el 2026-10-25 (opción C). Debe quedar escrito en el alcance del producto que FAM-10 se modifica, para que la información a la familia y el alcance no se contradigan. Lo decide CJ por escrito.
- **Verificar la dirección.** Mi criterio, para esta pieza: **no exigir verificación técnica** (código por correo) porque el texto es genérico y el daño de un error es bajo, **siempre que** (a) Administración lea la dirección a la persona y la confirme al darla de alta (procedimiento, no código), (b) el correo diga «si no esperabas este aviso, ignóralo» y (c) la familia pueda quitarla pidiéndolo. La verificación técnica llega sola con el código de acceso (pieza 3 del ADR 0012). **[Decidir DPO]** si acepta esto o exige antes una casilla «dirección confirmada con la persona» (`correo_confirmado_en`, se borra al cambiar el correo). Si el DPO la pide, es una columna nueva en `familiares` y el envío se condiciona a ella.

**Se puede construir ya, con datos de prueba:** puerto, adaptador, plantillas, tabla de envíos, pruebas, el interruptor y la lista de direcciones permitidas.

**Lo valida una persona antes de datos reales:** textos (ES y CA, el catalán lo revisa quien sepa catalán, ADR 0013 dice que CJ), información a la familia, base jurídica, procedimiento de confirmar la dirección, contrato de Azure como encargado, retención de los registros de ACS (B10), conservación del registro de envíos, y la modificación de FAM-10.

### 2.2 (P2) ¿Revela el correo un dato de salud o que vive en una residencia? Asunto y texto

- **Dato de salud: no.** No dice quién, cuándo, con quién ni para qué. «Cita» sola no dice que sea sanitaria.
- **Vínculo con un servicio asistencial: marginal.** La combinación «ResidApp» + «cita» sugiere, a quien lo vea, que la persona tiene relación con un servicio asistencial. No es dato de salud, pero **la vista previa de la pantalla de bloqueo, la bandeja compartida de una pareja o una dirección mal escrita** lo muestran a otros. El nombre «ResidApp» como remitente **no es un problema por sí mismo** (evoca «residencia» solo a quien ya lo sepa), pero no debe haber nada más que lo aclare. Mitigaciones: asunto y primera línea igual de genéricos (la vista previa enseña la primera línea), sin nombre del centro, sin «residencia», «residente», «salud», «médico» ni «enfermería»; sin nombre ni saludo personalizado; sin logotipo ni nombre del centro; sin imágenes remotas ni ningún recurso que se cargue desde un servidor; la única imagen es el icono de ResidApp, incrustado en el propio mensaje (`aviso-cita-icono-en-correo.md`), sin texto y sin más sugerencia que la marca de la aplicación.
- **Alternativa más prudente [Decidir]:** «Tienes una novedad pendiente en ResidApp». Pierde claridad para la familia y CJ ya aceptó la palabra «cita» (6.1 y 6.2). Recomiendo el texto con «cita» y que quede como una alternativa si el DPO prefiere.
- **Phishing.** Un correo con «propuesta de cita» y un enlace a un inicio de sesión es el patrón típico de fraude. Por eso: un único enlace en texto visible, sin parámetros ni identificadores ni código, que nunca inicia sesión por sí mismo, y una frase contra el fraude.
- **Idioma de los textos:** el ADR 0012 (línea 43) pide el mismo contenido en las dos lenguas. Para esta pieza el idioma es el del centro (castellano en el piloto, ADR 0013 línea 6), así que el catalán se entrega probado pero **no se usa** hasta que exista el ajuste del centro y la información previa en catalán.

**Texto propuesto, castellano (plantilla `AVISO_PROPUESTA_CITA`, versión 1, idioma `es`):**

```
Asunto: Tienes una propuesta de cita pendiente en ResidApp

Hay una propuesta de cita pendiente de tu respuesta en ResidApp.

Para verla, entra en ResidApp: https://app.residapp.es

Las propuestas tienen un plazo para responder.

No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.

Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.

—
Recibes este aviso porque esta dirección figura como contacto en ResidApp. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta y solo para enviarte este aviso. Para saber más, ejercer tus derechos de protección de datos o dejar de recibir estos avisos, dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa.
[TITULAR DE RESIDAPP: nombre o razón social, NIF, domicilio]
```

**Texto propuesto, catalán (misma plantilla y versión, idioma `ca`; lo revisa quien sepa catalán):**

```
Assumpte: Tens una proposta de cita pendent a ResidApp

Hi ha una proposta de cita pendent de la teva resposta a ResidApp.

Per veure-la, entra a ResidApp: https://app.residapp.es
De moment, la pantalla a la qual porta l'enllaç és en castellà.

Les propostes tenen un termini per respondre.

No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.

Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.

—
Reps aquest avís perquè aquesta adreça figura com a contacte a ResidApp. El responsable de les teves dades és el centre que la va registrar; ResidApp les tracta per compte seu i només per enviar-te aquest avís. Per saber-ne més, exercir els teus drets de protecció de dades o deixar de rebre aquests avisos, adreça't al centre. A l'apartat «Privacidad» del portal (en castellà) trobaràs la informació completa.
[TITULAR DE RESIDAPP: nom o raó social, NIF, domicili]
```

Notas del texto: el enlace sale de la configuración del entorno (en desarrollo, `dev.residapp.es`), nunca de la cabecera `Host`; el pie no nombra al centro (decisión de Orlando) pero identifica a quien responde por los datos («el centro que la registró») y a quien envía; «dejar de recibir» se resuelve pidiéndolo al centro (no hay enlace de baja, porque un enlace de baja sin sesión sería un enlace con token, que prefiero evitar); la línea del titular es opcional según lo que decida Orlando sobre quién es legalmente ResidApp (`landings-y-passkeys-por-subdominio.md`).

### 2.3 (P3) Varios residentes o varias personas con la misma dirección

- **Un familiar con varios residentes:** `residentes_familiares` es un vínculo por residente y la solicitud cuelga del vínculo. **Un correo por propuesta**, nunca una lista («tienes 2 propuestas»), nunca un recuento, nunca nombres. Dos propuestas = dos correos idénticos; es feo pero no filtra nada. Si el DPO o CJ quieren menos correos, se resuelve con una frase fija («tienes propuestas pendientes»), no con un resumen por residente.
- **Varias personas con la misma dirección (pareja, hijos):** el texto no lleva nombre ni residente, así que quien lo lee no aprende nada que no sepa. **Destinatario único: el familiar que envió la solicitud** (el de su vínculo y su cuenta), no «todas las personas con acceso a ese residente». Enviar a todos revelaría a unos familiares que otro ha pedido una cita y multiplicaría el riesgo de direcciones equivocadas.
- **Mensaje con un solo destinatario** en `Para`, sin `Cc` ni `Cco`: la prueba de ACS de hoy salió con dos destinatarios visibles en la cabecera `To` (`dominio-y-correo.respuestas.json`, línea 85). En producción eso expondría las direcciones entre sí.

### 2.4 (P4) Registro de envíos

**Tabla propuesta `dbo.envios_correo`** (nombre a decidir; sigue el estilo del repositorio: centro con seguridad por filas, códigos cerrados con `CHECK`, restricciones y disparador contra cambios de identidad):

| Columna | Para qué |
|---|---|
| `id` | Identificador de la fila |
| `centro_id` | Aislamiento por centro (seguridad por filas, `seg.pol_centro`) |
| `plantilla_codigo` | Valor cerrado (`AVISO_PROPUESTA_CITA`) |
| `plantilla_version` | Versión del texto enviado (prueba de qué se mandó) |
| `idioma` | `es` o `ca` |
| `familiar_id` | A quién (identificador, **no** la dirección) |
| `referencia_id` | La solicitud (más adelante, la cita) |
| `referencia_clave` | El número del cambio `PROPUESTA` en `citas_solicitudes_cambios` (para el recordatorio, el día) |
| `estado_codigo` | `PENDIENTE` (reservado), `ENVIADO` (aceptado por ACS), `FALLIDO`, `OMITIDO` |
| `motivo_codigo` | Valor cerrado y opcional: `SIN_CORREO`, `SIN_ACCESO`, `PROPUESTA_NO_VIGENTE`, `ENVIO_APAGADO`, `ACS_RECHAZADO`, `ACS_NO_DISPONIBLE`, `TIEMPO_AGOTADO` |
| `acs_operacion_id` | El identificador de operación que devuelve ACS; sirve para buscar el envío en ACS si hace falta |
| `intentos` | Número de intentos |
| `creado_en`, `resuelto_en` | Cuándo se reservó y cuándo terminó |

Clave única (`plantilla_codigo`, `referencia_id`, `referencia_clave`): impide enviar dos veces. **Cambio respecto al ADR 0012 (línea 73):** para la propuesta la clave es **el número del cambio, no el día**; con «por día», un segundo «otro horario» el mismo día no avisaría. Para el recordatorio, cita y día.

**No guardar:** la dirección (ni su huella, que seguiría señalando a la persona), el asunto, el cuerpo, los enlaces, el nombre de nadie, el `residente_id` (se llega por la solicitud), la cuenta de Administración que propuso (ya consta en `citas_solicitudes_cambios`), IP, agente de usuario, el texto de una excepción (puede traer la dirección) ni ningún código.

**Conservación [Decidir DPO]:** el registro solo hace falta para no duplicar (días) y para explicar un aviso que no llegó (meses). Propongo **12 meses desde `resuelto_en` como tope**, o menos si el DPO quiere; es una propuesta de ingeniería, **no un plazo legal**, y no he encontrado ninguno que lo fije (a verificar con el DPO). El idioma anotado es una copia del idioma del familiar: misma conservación (`portal-familiar-dos-idiomas.md`, apartado (b), punto 7). Las filas `PENDIENTE` olvidadas se tratan como `FALLIDO`. El resto de tablas del repositorio son inmutables; **esta necesita una vía de purga** (como la del motivo en `historial-tras-traslado-05`): permitir `UPDATE` solo de `PENDIENTE` a un estado final, y `DELETE` solo desde un procedimiento de purga.

**Encaje con la auditoría:** `APPOINTMENT_REQUEST_PROPOSE` ya deja quién propuso, cuándo y a qué residente. **No añadir acción de auditoría por el envío:** `eventos_auditoria` exige una cuenta que actúa y un perfil de la lista (`0002`, líneas 707 y 719) y un envío del sistema no tiene ninguno. El registro de envíos se une a la solicitud por `referencia_id` y `referencia_clave`; eso basta para responder «¿salió el aviso de esta propuesta?».

**¿Registrar rechazo o rebote?**
- **Rechazo inmediato** (ACS rechaza la petición, dirección no válida, límite de envíos, caída): **sí**, como `FALLIDO` con un código cerrado, sin texto de la excepción.
- **Rebote posterior** (el servidor del destinatario devuelve el correo): **recomendable, no necesario en esta pieza.** «Aceptado por ACS» no es «entregado». Un rebote duro es la mejor señal de una dirección mal escrita (art. 5.1.d, exactitud) y Administración debería poder corregirla, pero **sin Event Grid** (un tema de sistema de ACS se crea en ubicación global y puede guardar el evento en cualquier centro de datos de Microsoft; fuente en el apartado 2.6) y sin guardar la dirección en el registro. Si se construye, con la consulta del estado a ACS y un estado `REBOTADO`/`ENTREGADO` cerrado.

### 2.5 (P5) Fallos, reintentos, dirección equivocada, baja o suspensión

- **Nunca dentro de la transacción de proponer, y nunca una causa de fallo de la propuesta** (ADR 0012, requisito 6; `ProposeAsync` ya hizo `Commit`). Con tiempo máximo corto **[Suposición de ingeniería]** y la excepción atrapada: se registra `FALLIDO` y se sigue.
- **Reintentos:** sin tarea programada (ADR 0012, líneas 18 a 21) no hay quién reintente. En esta pieza **no reintentar solo**; el aviso dentro del portal cubre a la familia. Cuando llegue el `BackgroundService`, todo reintento: repite la comprobación de elegibilidad, tiene tope de intentos y de edad (nunca después de que caduque la propuesta).
- **Comprobar en el momento de enviar, no al proponer.** Se vuelve a leer de la base: la solicitud sigue en `PROPUESTA`, sin caducar y con el **mismo** número de cambio; las condiciones de `AccessibleLinks` (se reutiliza la misma consulta, no una copia: si no, divergirán) y el correo **actual** del familiar. Esto cubre: baja entre medias (la solicitud ya está `CERRADA`), suspensión (el portal ya no deja ver la propuesta), vínculo desvinculado, autorización revocada o caducada, propuesta sustituida, y correo cambiado después de proponer (se usa el nuevo).
- **Dirección equivocada:** el riesgo es bajo con este texto, pero real. Mitigaciones: confirmar la dirección al darla de alta (procedimiento), frase «si no esperabas este aviso, ignóralo», ninguna información personal en el mensaje, y que la familia pueda quitarla. Como el correo no guarda historial de valores (`SqlResidentFamilyRepository.cs`, línea 152), **no se podrá probar a posteriori a qué dirección fue** un aviso concreto; se acepta así por minimización. **Si una familia dice que un desconocido recibió el aviso:** es un incidente de seguridad de bajo riesgo que se anota en el registro interno de brechas (art. 33.5) y el DPO decide si se notifica (con este texto, lo probable es que no). Debe estar en el procedimiento de brechas.
- **Una solicitud por familiar sin correo:** `OMITIDO / SIN_CORREO`, sin error.

### 2.6 (P6) Azure Communication Services Email: contrato, ubicación y registros

Fuentes consultadas hoy (2026-10-30), páginas de Microsoft Learn: `learn.microsoft.com/azure/communication-services/concepts/privacy` (actualizada el 2026-09-23) y `.../concepts/analytics/logs/email-logs`.

**Lo que dice Microsoft [Hecho en fuente oficial]:**
- La ubicación de datos se elige al crear el recurso (Europe). Los datos en reposo quedan en esa geografía, **pero «pueden transitar o procesarse en otras geografías»**. Para el correo: el contenido se procesa «en tiempo real» en la ubicación de datos; los registros de entrega van a Azure Monitor donde el cliente elige el espacio; el nombre de usuario del remitente se guarda hasta que se borre; **y «para prevenir y detectar spam y abuso, el sistema retiene temporalmente las direcciones de los correos que rebotaron de forma permanente»**. La página **no fija cuánto dura esa retención temporal ni dice si el contenido se guarda**.
- Los registros de ACS Email son tres categorías: **envío** (solo recuentos de destinatarios, sin direcciones), **estado de entrega** (lleva `RecipientId`, la **dirección** de cada destinatario, y el estado y los códigos SMTP) y **interacción del usuario** (aperturas y clics, con la dirección y el agente de usuario).
- Event Grid con ACS se crea en una ubicación global y puede guardar eventos en cualquier centro de datos.
- La cabecera del correo de prueba muestra que salió por un servidor de Europa occidental (`mail-westeuropeazacs...`, `dominio-y-correo.respuestas.json`, línea 85) [Hecho]. Eso confirma el camino de esa prueba, no el de todos los casos.

**Qué comprobar del contrato (para el DPO, no lo doy por hecho):**
1. Que el **Anexo de Protección de Datos de Microsoft** vigente cubre a Azure Communication Services Email como servicio y a Microsoft como encargado de ResidApp. **A verificar** en el documento vigente de Microsoft.
2. **La cadena es: residencia (responsable) → ResidApp (encargada) → Microsoft (subencargada).** Microsoft debe constar en la lista de subencargados del contrato de encargado entre la residencia y ResidApp, con el aviso previo de cambios (art. 28.2 y 28.4).
3. **Transferencias:** «puede transitar o procesarse en otras geografías». Con el texto genérico, lo que podría salir de la UE es una dirección y la hora; igualmente pedir al DPO que anote la garantía (certificación de Microsoft en el Marco de Privacidad de Datos UE-EE. UU., o cláusulas tipo). **A verificar.**
4. Cuánto retiene ACS las direcciones de rebotes y si el contenido se guarda tras entregarlo: **preguntar a Microsoft por escrito**; no consta.
5. La suscripción de Azure se comparte con otros proyectos (`docs/seguridad/hallazgos.md`, línea 441): el espacio de registros de ACS debe ser **propio del proyecto**, no uno compartido.

**Recomendación para B10 (registros de actividad de `acs-residapp-dev`)** [Decidir Orlando con el DPO; el texto de B10 está en `dominio-y-correo.html`, líneas 371 a 373]:
- **Seguimiento de aperturas y clics: apagado** en el dominio `avisos.residapp.es`, y **no activar** la categoría de interacción del usuario. Vigilar si una familia abrió un aviso no es necesario y entra en el almacenamiento en el dispositivo y el seguimiento (LSSI, art. 22, a verificar con abogado).
- **Registros de envío:** activarlos (solo recuentos).
- **Registros de estado de entrega:** activarlos **solo** si se envía a un espacio de Log Analytics propio, en la UE, con **30 días de retención** como punto de partida (el propio texto de B10 lo propone; el valor lo decide el DPO), acceso limitado a quien administre el proyecto y sin copia a Event Hubs ni a una cuenta de almacenamiento. Son los que permiten saber si un aviso rebotó; llevan la dirección, así que son datos personales y entran en el registro de actividades. Con datos de prueba da igual; **antes de datos reales** lo confirma el DPO.
- **DMARC `p=none`** no impide que alguien se haga pasar por `avisos.residapp.es`. Para un correo con enlace a un inicio de sesión, subirlo a `quarantine` o `reject` **antes de producción**, una vez revisados los informes. [Recomendable antes de producción.]
- **Sin `Reply-To`:** la cabecera del correo de prueba trae `Reply-To: ResidApp <no-responder@...>` y `Sender` (`dominio-y-correo.respuestas.json`, línea 85) aunque el ADR 0012 (línea 7) dice «sin Reply-To». Puede ser cosa de la herramienta «Try Email» del portal. **Comprobar las cabeceras del primer envío real hecho con el SDK.** Si una respuesta va a un buzón que no existe, **la familia puede escribir ahí algo sobre su familiar y se pierde**: por eso el texto dice «llama al centro» y no solo «no respondas».

### 2.7 (P7) Qué cambiar o añadir en `docs/privacidad/`

**Imprescindible antes de usar con direcciones reales:**
1. **Registro de actividades** (art. 30): una actividad nueva, parte A (residencia) y parte B (ResidApp), apartado 5 de este documento.
2. **Información al familiar** (art. 13) en castellano y catalán: texto del apartado 6, en el alta (junto al campo del correo) y en la política del Portal Familiar. Hoy `Views/Home/Privacy.cshtml` está sin redactar.
3. **Procedimiento de alta:** Administración lee la dirección a la persona y la confirma; cómo se quita; qué hacer si alguien dice que recibió un aviso ajeno (brechas, art. 33).
4. **Contrato de encargado** (art. 28): Azure Communication Services (Microsoft) en la lista de subencargados; ubicación de datos y retención de registros de ACS según el apartado 2.6.
5. **Decisión escrita de CJ** de que FAM-10 queda modificado (aviso por correo sin contenido) y actualizar el alcance del producto (lo hace Orlando o una sesión de código).
6. **`antes-de-datos-reales.md`:** añadir un apartado 7 con estos puntos (no lo modifico).

**Imprescindible antes de producción:** DMARC reforzado; decisión sobre B10 y su retención; conservación del registro de envíos y vía de purga; catalán revisado; autoridad de control (AEPD o APDCAT) según el tipo de centro piloto, **a verificar** (`historial-tras-traslado-01`, fila «Autoridad de control»); evaluación de impacto del Portal Familiar con una línea sobre este envío.

**Recomendable, puede esperar:** casilla técnica de dirección confirmada; estado de entrega y rebote; texto en el Manual de la aplicación (`Views/Home/Manual.cshtml`) cuando se construya; opción de la familia de apagar los avisos desde el portal.

## 3. Condiciones que el código debe cumplir (comprobables con pruebas)

1. **Después del `Commit`, nunca dentro, y un fallo no cambia el resultado.** Prueba: un `IEmailSender` falso que lanza excepción y otro que tarda más del máximo; `ProposeAsync` devuelve `Ok`, la solicitud queda `PROPUESTA` y el registro dice `FALLIDO` con código cerrado.
2. **Un solo destinatario: el familiar de la solicitud.** El correo sale del `familiar_id` del vínculo de esa solicitud, leído en el momento de enviar. Prueba: con dos familiares con acceso al mismo residente, solo se envía al que hizo la solicitud; el mensaje tiene un destinatario y ningún `Cc` ni `Cco`.
3. **Elegibilidad al enviar, con la misma consulta que el portal.** Reutiliza `AccessibleLinks` (no una copia). Pruebas, una por caso, que no envían y registran `OMITIDO`: baja, suspensión, vínculo desvinculado, autorización revocada o caducada, solicitud cerrada, propuesta caducada, propuesta sustituida por otra (número de cambio distinto), correo vacío.
4. **Sin correo no hay envío ni error** (`OMITIDO / SIN_CORREO`).
5. **Una solicitud, un mensaje; nunca agrupa.** Prueba: dos propuestas al mismo familiar, o dos familiares con la misma dirección, generan mensajes separados sin recuentos ni nombres.
6. **No se envía dos veces.** Reserva previa con índice único (`plantilla_codigo`, `referencia_id`, `referencia_clave`) y la clave es el número del cambio `PROPUESTA`, no el día. Prueba de concurrencia: dos llamadas simultáneas, un envío; «otro horario» y nueva propuesta el mismo día, dos avisos.
7. **La plantilla no recibe ningún dato personal.** El generador de texto solo recibe idioma y la dirección base de la aplicación; no recibe residente, familiar, centro, unidad, equipo, motivo, fecha ni profesional. Prueba: render en `es` y en `ca` y comprobación de que el texto es el aprobado (comparación exacta con el fichero versionado) y que no contiene nada de esa lista. El icono no es un parámetro: es una constante incrustada en el ensamblado; para el HTML, el texto visible (sin etiquetas) es el del texto plano más la palabra «ResidApp» de la cabecera.
8. **Paridad de plantillas.** Los ficheros `es` y `ca` tienen las mismas claves y la misma versión (ADR 0013, línea 70); si falta uno, usa el otro. La plantilla en catalán incluye la frase de que la pantalla es en castellano.
9. **Enlace.** Sale de la configuración del entorno, nunca de `Host` ni de cabeceras; sin parámetros, sin identificadores ni tokens, sin enlace de seguimiento. **El HTML no carga nada de ningún servidor**: ni imágenes enlazadas, ni píxel, ni hoja de estilo, letra de Internet o fondo con `url(...)`. La única imagen es el icono de ResidApp, incrustado en el mensaje (`src` que empieza por `cid:`). El enlace va siempre en texto visible: el texto del enlace es la propia URL. Pruebas: el conjunto de direcciones `http` o `https` del HTML y del texto plano es exactamente {la URL configurada}; el HTML tiene un único `<a>`, con `href` y texto visible iguales a esa URL; el único `<img>` tiene `src` con `cid:`; el HTML solo usa las etiquetas, atributos y clases de una lista cerrada (`aviso-cita-icono-en-correo.md`, apartado 4).
10. **Sin `Reply-To`.** El mensaje construido no lleva `ReplyTo`; comprobación manual de cabeceras en el primer envío real (apartado 2.6).
11. **Idioma de una sola fuente y cerrado.** `es` o `ca`; hasta que exista el ajuste del centro, un valor de configuración (`es`); la aplicación no lee `Accept-Language` ni deduce el idioma del nombre (ADR 0012, líneas 45 a 48).
12. **Registro sin contenido.** Las columnas son las del apartado 2.4 y ninguna otra; prueba de esquema que falla si aparece una columna con dirección, asunto o cuerpo; el código de fallo es un valor cerrado, no el mensaje de la excepción.
13. **Registro de la aplicación (logs) sin direcciones.** Ni la dirección ni la excepción completa de ACS (puede contenerla). Prueba o revisión: el `ILogger` solo recibe plantilla, estado y código.
14. **Interruptor y salvaguarda de pruebas.** El envío está apagado si falta `Correo__CadenaConexion` o `Correo__Activo` es falso (por defecto, apagado). En el entorno de desarrollo solo se envía a una lista de direcciones permitidas: los datos de prueba pueden llevar correos de personas reales (la de Orlando, la de CJ). Los tests de CI usan siempre el `IEmailSender` falso.
15. **Aislamiento.** La tabla nueva entra en `seg.pol_centro` (filtro y bloqueo, como `0057` línea 115 a 121); ninguna pantalla muestra direcciones ni el registro. Opcional: una línea a Administración, sin dirección («aviso por correo: enviado / no enviado / la familia no tiene correo»).
16. **Cadena de conexión** nunca en el repositorio, en logs ni en pantalla (ya decidido, ADR 0012 línea 7).

## 4. Qué queda para una persona

- **DPO real o abogado:** base jurídica (6.1.b o 6.1.f), si se exige verificar la dirección, la cláusula informativa, la conservación del registro de envíos y de los registros de ACS, el contrato de encargado y la lista de subencargados, la garantía de transferencias, DMARC, y si la LSSI no se aplica a este aviso.
- **CJ:** FAM-10 modificado por escrito; revisión del catalán.
- **Orlando:** B10; quién es legalmente ResidApp para el pie; si se prefiere «novedad» a «propuesta de cita».
- **Residencia:** cómo avisa a la familia cuando el aviso no llega, y el procedimiento de alta.

## 5. Entrada del registro de actividades (art. 30)

**Parte A, la residencia (responsable):**

| Campo | Contenido propuesto |
|---|---|
| Nombre de la actividad | Avisos genéricos por correo a familiares cuando el centro propone una cita |
| Finalidad | Que el familiar sepa que tiene una propuesta pendiente en el portal y pueda responder a tiempo. El correo no lleva datos del residente ni de salud. |
| Personas afectadas | Familiares y representantes con correo registrado y acceso al Portal Familiar |
| Datos | Dirección de correo; identificador del familiar, de la solicitud y del cambio; plantilla, versión, idioma, estado y momento del envío (sin contenido). **Sin datos de salud.** |
| Base jurídica | **[Decidir DPO]** 6.1.b (representantes o partes) o 6.1.f (resto); 6.1.e si el centro es público |
| Destinatarios | Personal de Administración (puede ver si salió, no la dirección en el registro); Microsoft (Azure Communication Services) como subencargado de ResidApp |
| Transferencias | Datos en Europa; tránsito posible por otras geografías (apartado 2.6). **A verificar** |
| Conservación | Dirección: la del familiar mientras tenga acceso. Registro de envíos: **[Decidir]**, propuesta 12 meses. Registros de entrega de ACS: **[Decidir]**, propuesta 30 días. |
| Seguridad | Texto cerrado y versionado; sin datos en el mensaje; envío tras confirmar la propuesta; comprobación del acceso en el momento de enviar; destinatario único; sin seguimiento de aperturas y sin ningún recurso remoto en el mensaje (el icono de ResidApp va incrustado); seguridad por filas; cadena de conexión fuera del repositorio |
| Autoridad de control | **A verificar** (AEPD o APDCAT) |

**Parte B, ResidApp (encargada):** tratar la dirección y la plantilla por instrucción de la residencia para enviar el aviso mediante Azure Communication Services (subencargado); mantener el registro de envíos; no usar las direcciones para otra cosa; borrar el registro al terminar el contrato o cuando la residencia lo indique.

## 6. Información al familiar (art. 13), propuesta para el alta y el portal

**Junto al campo del correo (castellano):** «Si nos das tu correo, el centro te avisará cuando haya algo pendiente que responder en ResidApp, por ejemplo una propuesta de cita. El aviso no dice nada sobre la persona a la que cuidas ni sobre su salud: solo que entres en ResidApp. Es voluntario y puedes quitarlo cuando quieras pidiéndolo al centro.»

**Catalán:** «Si ens dónes el teu correu, el centre t'avisarà quan hi hagi alguna cosa pendent de respondre a ResidApp, per exemple una proposta de cita. L'avís no diu res sobre la persona de qui tens cura ni sobre la seva salut: només que entris a ResidApp. És voluntari i el pots treure quan vulguis demanant-ho al centre.»

**Cláusula completa (segunda capa)**, para que la residencia la adopte: responsable ([RESIDENCIA], [DPO]); finalidad; base jurídica; destinatarios (el proveedor del correo, a través de ResidApp); conservación; derechos y cómo ejercerlos; reclamación ante [AUTORIDAD]; y que, si no se facilita el correo, no pasa nada: los avisos están también dentro del portal.

## A verificar o decidir

1. **[Decidir DPO]** Base jurídica por tipo de familiar y si basta el procedimiento humano de confirmar la dirección o se exige `correo_confirmado_en`.
2. **A verificar (abogado):** si la LSSI trata este aviso de servicio fuera de las comunicaciones comerciales y qué alcance tiene el art. 22 sobre el seguimiento de aperturas; no cito artículos de memoria.
3. **A verificar:** que el Anexo de Protección de Datos de Microsoft vigente cubre ACS Email; la garantía de transferencias; cuánto retiene ACS las direcciones de rebotes y si guarda el contenido (no consta en la página oficial de privacidad de ACS).
4. **[Decidir Orlando + DPO]** B10: retención de los registros de entrega (propuesta 30 días), espacio propio en la UE, y apagado del seguimiento de aperturas.
5. **[Decidir]** Conservación del registro de envíos (propuesta de ingeniería: 12 meses como tope) y la vía de purga.
6. **[Decidir CJ]** Escribir que FAM-10 queda modificado; confirmar la frase de la propuesta («cita» frente a «novedad»); revisar el catalán.
7. **A comprobar en el primer envío real con el SDK:** que no sale `Reply-To` y que `To` lleva un solo destinatario.
8. **A verificar:** autoridad de control (AEPD o APDCAT) y, si el centro es público o concertado, el Esquema Nacional de Seguridad.
9. **[Decidir Orlando]** Quién firma como titular de ResidApp en el pie (nombre o razón social, NIF, domicilio).
10. **Fuera de alcance, para más adelante:** el recordatorio (sin tarea programada hoy; exige «Siempre activo») y el código de acceso (correo obligatorio y verificado) necesitan su propia lectura; este documento no los cubre.

Fuentes consultadas el 2026-10-30: Microsoft Learn, «Data residency and user privacy for Azure Communication Services» (`https://learn.microsoft.com/azure/communication-services/concepts/privacy`) y «Email logs» (`https://learn.microsoft.com/en-us/azure/communication-services/concepts/analytics/logs/email-logs`).
