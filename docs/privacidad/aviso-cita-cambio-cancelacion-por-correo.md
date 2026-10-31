# Aviso por correo cuando el centro cambia o cancela una cita confirmada: lectura de privacidad y condiciones de construcción

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-10-31. Segunda lectura experta, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL), **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado.

Complementa `docs/privacidad/aviso-cita-por-correo.md` (el «borrador de la propuesta») y `docs/privacidad/aviso-cita-icono-en-correo.md` (el «borrador del icono»), y el ADR 0012 (`docs/decisiones-arquitectura/0012-envio-correos.md`). **No modifico ninguno de los tres**: el apartado 6 lista las frases que una persona o la sesión principal tendría que tocar.

**Alcance:** (a) el aviso nuevo «el centro ha cambiado o cancelado una cita confirmada» (decisión de Orlando, 2026-10-31), para los dos modos de citas (reserva directa y solicitud previa); (b) las condiciones que el recordatorio del día antes (pieza 2 del ADR 0012) debe cumplir **al compartir tabla, clave y marco** con estos avisos. **Queda fuera** el texto y el momento de envío del recordatorio, que necesita su propia lectura, y el código de acceso (pieza 3).

Términos que uso, en una frase cada uno: **encargada** es quien trata los datos por cuenta de otro (ResidApp, por cuenta de la residencia); **clave única** es una regla de la base de datos que impide guardar dos filas con los mismos valores, y aquí es lo que evita mandar el mismo correo dos veces; **interés legítimo** es una de las razones que permite tratar datos sin consentimiento cuando el centro tiene un motivo razonable y la persona no sale perjudicada; **vista previa** es lo que el móvil o la bandeja enseñan de un correo sin abrirlo (remitente, asunto y primeras palabras); **phishing** es un correo falso que imita a uno auténtico para que alguien entre en una página trampa.

## Decisiones de Orlando y estado (2026-10-31)

Orlando decidió, a las preguntas de este borrador: **un solo aviso** («hay un cambio») sin distinguir cambio y cancelación; **con** la línea «Aquí no ponemos los detalles…»; **sin** la casilla «No mandar aviso; voy a llamar yo»; envío **diferido** con una tarea programada dentro de la aplicación (no una Azure Function); **sin aviso** cuando el centro cierra una solicitud o retira una propuesta; y, para que la tarea vea las filas de todos los centros, un **usuario de base de datos propio** con permisos mínimos (`docs/privacidad/tarea-correo-identidad-rls.md`).

**Construido, apagado por defecto** (script `0082` y `0083`; ADR 0012, «Estado de la construcción»). Las condiciones N1 a N14 están cubiertas por pruebas (`AvisoCambioCitaTests`, `TareaCorreoRlsTests`), salvo: **N12** (sin reintentos: un fallo queda FALLIDO; hay tarea programada pero aún no reintenta); el **texto de las pantallas de Administración (N13)** no cambia, y el Manual y «Flujo de trabajo» cuentan el aviso con condicionales. N4, «el motivo nunca se lee», ya no depende solo del código: el rol de la tarea tiene un `DENY` sobre `citas_cambios.motivo`. **Sigue pendiente, antes de encenderlo con direcciones reales:** la confirmación de CJ y la ampliación de FAM-10, la información a la familia ampliada (apartado 7), la validación humana de este borrador (DPO real o abogado), la vía de purga del registro y el contrato de Azure.

## Conclusión

**Admisible con condiciones, y con una recomendación que cambia la propuesta de partida:** mandar **un único aviso, «hay un cambio en una cita»**, sin distinguir entre reprogramación y cancelación, sin día, hora, equipo, modalidad ni motivo, a **una sola persona: quien reservó la cita**. Un correo así no contiene datos de salud (art. 4.15 y 9 RGPD) y no añade tratamiento de datos: es la misma dirección, la misma tabla y el mismo marco que el aviso de propuesta. La propuesta de partida de Orlando es correcta en lo esencial; mis matices son cuatro.

El riesgo más importante **no es el texto, es cuándo se dispara y cómo se lee en el centro**:

1. **Dispararlo donde no debe.** Hoy hay cuatro caminos que dejan una cita `CANCELADA` o `REPROGRAMADA`: Administración cancela, Administración reprograma, la propia familia cancela o reprograma, y **la baja del residente cancela las citas futuras con el actor `ADMINISTRACION`** (`SqlResidentStatusRepository.cs`, líneas 98 a 113). Si el aviso se engancha a «cualquier cita cancelada por Administración», una familia recibiría «tienes un cambio en una cita» **tras el fallecimiento o el traslado de su familiar**. Debe engancharse solo en los dos métodos de Administración (apartado 3, N1 y N8).
2. **Que Administración crea que ya avisó.** El envío no tiene reintentos (ADR 0012, líneas 57 a 61) y puede fallar o ir a una dirección no leída. Las pantallas de hoy dicen «Avísala también por teléfono» (`ReprogramarCita.cshtml`, línea 21): ese texto debe quedarse y el correo **no puede presentarse como el aviso** (apartado 2.6).
3. **Un correo sobre «un cambio» llegando a destiempo** (fallecimiento, hospitalización, el centro cancela antes de hablar con la familia): el texto es genérico, pero el momento puede ser delicado. Es una decisión humana, no del texto (apartado 2.6, casilla opcional).
4. **Que el motivo salga por un descuido.** El motivo lo escribe Administración en texto libre de hasta 500 caracteres (`Appointments.cs`, líneas 179 a 188; `0054_...sql`, línea 121) y CJ decidió que la familia **no** lo ve (5.6, `portal-familiar-confirmaciones-cj.respuestas.json`, líneas 120 a 125). Basta con que la consulta que prepara el envío **nunca lea esa columna** (N4).

Se puede construir **ya con datos de prueba y apagado por defecto**. Antes de la primera dirección real hace falta lo de siempre (información a la familia, FAM-10, contrato de Azure) **más una cosa nueva**: la información que se da a la familia hoy habla de «algo pendiente que responder» y deja de ser cierta (apartado 2.4).

## 1. Qué hace hoy el código [Hecho]

- **Cancelar y reprogramar (Administración).** `AppointmentServiceApplicationService.CancelAsync` y `RescheduleAsync` (`AppointmentServiceApplicationService.cs`, líneas 84 a 101) llaman al repositorio y devuelven solo `true`: **no devuelven el número del cambio**. En el repositorio, `CancelAsync` (`SqlAppointmentServiceRepository.cs`, líneas 222 a 239) y `RescheduleAsync` (líneas 241 a 279) hacen una transacción que actualiza la cita, escribe el paso en `citas_cambios` con el motivo (líneas 237 y 277) y hace `Commit`. Es el mismo patrón que el de proponer, donde el servicio sí recibe el número (`AppointmentRequestsApplicationService.cs`, línea 45): **para enganchar el envío, estos dos métodos tendrían que devolverlo, y el envío iría después del `Commit`.**
- **Quién reservó.** `dbo.citas` guarda `vinculo_id` y `cuenta_id` de quien la reservó (`0054_...sql`, líneas 89 y 90) y no cambian nunca (disparador, líneas 141 a 146). Con la solicitud previa, la cita nace con el vínculo y la cuenta de quien aceptó la propuesta (`SqlFamilyAppointmentRequests.cs`, líneas 175 a 181). El vínculo lleva al `familiar_id` y de ahí a `familiares.correo`, como en la propuesta.
- **Qué ve el resto de la familia.** La agenda de la familia filtra por `cuenta_id` (`SqlFamilyAppointments.cs`, línea 299; `ReadOwnAsync`, línea 279): **otro familiar con acceso al mismo residente no ve esa cita.** CJ lo decidió así: «Solo quien la reservó» (5.3, `portal-familiar-confirmaciones-cj.respuestas.json`, líneas 100 a 103).
- **Qué ve quien la reservó.** La consulta de cambios lee solo acción, perfil y fecha (línea 308) y marca si el cambio fue del centro (línea 320); **no lee el motivo**. La familia ve «el centro la cambió» y, en una reprogramación, la hora nueva (Manual, `Manual.cshtml`, línea 555; CJ 5.6).
- **La baja.** Cancela las citas futuras y cierra las solicitudes abiertas como hecho del sistema, con el actor `ADMINISTRACION` y `causa_codigo = 'BAJA_RESIDENTE'` (`SqlResidentStatusRepository.cs`, líneas 98 a 113; `0078_...sql`, líneas 11 a 14). La suspensión no las toca.
- **Quién puede recibir.** `SqlFamilyPortalReader.AccessibleLinks` exige vínculo vigente, autorización activa y vigente, residente `ACTIVE` y sin suspensión abierta. El aviso de propuesta ya lo reutiliza (`SqlProposalNoticeStore.cs`, línea 52) y es lo que debe reutilizarse aquí.
- **Lo que ya existe del correo.** `envios_correo` con clave única (`plantilla_codigo`, `referencia_id`, `referencia_clave`) (`0081_...sql`, línea 44); `plantilla_codigo` solo admite `AVISO_PROPUESTA_CITA` (línea 33) y `motivo_codigo` tiene una lista cerrada que no incluye casos de cita (líneas 36 y 37): **añadir plantillas y motivos exige un script nuevo**, no editar `0081`. `ProposalNoticeService` (líneas 19 a 52) ya hace reserva, comprobación en el momento de enviar, lista de direcciones permitidas, tiempo máximo de 10 segundos y registro sin dirección.
- **Lo que dicen las pantallas de Administración hoy.** «La familia verá que el centro la ha cancelado… Queda en la auditoría» (`AdministracionController.Citas.cs`, líneas 200 y 245) y «Avísala también por teléfono» (`ReprogramarCita.cshtml`, línea 21).
- **FAM-10.** La propuesta de redacción que espera la firma de CJ enumera «propuesta de cita, recordatorio y código de acceso» (`aviso-citas-por-correo-cj.html`, líneas 314 y 315) y **no incluye el cambio ni la cancelación**. En `docs/pendientes-cj/` no existe `aviso-citas-por-correo-cj.respuestas.json` (consulta de ficheros del 2026-10-31): FAM-10 sigue sin modificar por escrito.
- **Nada de esto está construido.** El ADR 0012 solo recoge la pieza 1 (líneas 57 a 65).

## 2. Preguntas 1 a 7

### 2.1 (P1) ¿Basta el texto genérico? ¿Revela algo «cambio» o «cancelada»?

**Basta, y recomiendo «cambio».** Razones:

- **Dato de salud: no.** Ni «cambio» ni «cancelada» dicen quién, cuándo, con quién ni para qué. «Cita» sola ya la aceptó CJ (6.1 y 6.2) y no dice que sea sanitaria.
- **Qué añade la palabra «cancelada».** Una información sobre lo que ha hecho el centro, no sobre la persona. Para quien lea la vista previa por encima del hombro o comparta bandeja, «cita cancelada» es una cita menos, no una situación de salud. **Riesgo bajo, no nulo:** «cancelada» es más fácil de leer como «le ha pasado algo» que «cambio», y es el gancho clásico del phishing («tu cita ha sido cancelada, entra para reprogramarla»). «Cambio» describe igual de bien la reprogramación y no promete nada.
- **El tono no se pierde:** el portal ya llama a las dos cosas «el centro la cambió» (Manual, línea 555), así que el correo usa el mismo vocabulario que la pantalla a la que lleva.
- **Reglas del marco que se mantienen** (borrador de la propuesta, 2.2, y del icono, A7): asunto y primera línea igual de genéricos (la vista previa enseña la primera línea); **ni «centro» en el asunto ni en la primera línea** (la palabra aparece solo en las frases aprobadas del final, «llama al centro» y el pie); sin nombre, saludo personalizado, número ni plural («una de tus citas» sugiere que hay varias; se evita). Un solo enlace, en texto visible, sin parámetros.

**Texto propuesto, castellano (plantilla `AVISO_CAMBIO_CITA`, versión 1, idioma `es`):**

```
Asunto: Tienes un cambio en una cita en ResidApp

Hay un cambio en una cita que tienes en ResidApp.

Para verlo, entra en ResidApp: https://app.residapp.es

Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.

No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.

Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.

—
[Mismo pie que el aviso de propuesta, sin cambios: «Recibes este aviso porque esta dirección figura como contacto en ResidApp. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta y solo para enviarte este aviso. Para saber más, ejercer tus derechos de protección de datos o dejar de recibir estos avisos, dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa.»]
```

**Texto propuesto, catalán (misma plantilla y versión, idioma `ca`; lo revisa quien sepa catalán, ADR 0013):**

```
Assumpte: Tens un canvi en una cita a ResidApp

Hi ha un canvi en una cita que tens a ResidApp.

Per veure'l, entra a ResidApp: https://app.residapp.es
De moment, la pantalla a la qual porta l'enllaç és en castellà.

Aquí no posem els detalls, per protegir la teva privadesa: els veuràs en entrar.

No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.

Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.

—
[Mateix peu que l'avís de proposta, sense canvis.]
```

Notas: (i) la línea «Aquí no ponemos los detalles…» es opcional; ayuda a la familia a entender por qué el correo es tan escueto y no da pistas a un tercero. Si el DPO la considera ruido, se quita sin otra consecuencia. (ii) El pie es **idéntico** al del aviso de propuesta («solo para enviarte este aviso» sigue siendo cierto con cualquier plantilla). (iii) El enlace sale de la configuración (`Correo__UrlBase`), nunca de `Host`.

### 2.2 (P2) ¿Distinguir cancelación de reprogramación, o un único aviso?

**Recomiendo un único aviso.**

| | Un aviso («hay un cambio») | Dos avisos («cambio de hora» / «cancelada») |
|---|---|---|
| **Utilidad para la familia** | La persona tiene que entrar de todos modos para ver la hora nueva o que está cancelada. Se pierde poco: saber antes de abrir si tiene que volver a pedir cita. | Un poco más de información sin abrir la aplicación. |
| **Privacidad** | Nada revelado salvo «cita» y «cambio». | Añade lo que hizo el centro. No es salud, pero es más contenido en la vista previa. |
| **Fraude (phishing)** | Menos urgencia en el texto. | «Cancelada» + enlace es el patrón típico de fraude. |
| **Mantenimiento** | Una plantilla, en dos idiomas, que revisar. | Dos plantillas por idioma: cuatro textos que aprobar, y versiones que mantener iguales. |
| **Coherencia con la pantalla** | El portal ya dice «el centro la cambió» para ambas. | El correo diría más que la pantalla. |

**Si Orlando o CJ quisieran distinguir** (decisión suya, no recomendada): la distinción mínima aceptable es **solo el verbo en la primera línea**, sin hora, sin equipo, sin modalidad y sin «el centro»: «Una cita que tenías en ResidApp ha cambiado de hora» y «Una cita que tenías en ResidApp se ha cancelado». Habría que repetir esta lectura y el asunto seguiría siendo genérico («Tienes un cambio en una cita en ResidApp») para que la vista previa no cambie. **No aceptable**, aunque se pida: la hora nueva, el equipo, la modalidad, el motivo, el nombre del residente o del centro, ni «tu cita médica/de enfermería» (cita con un equipo asistencial puede revelar un asunto de salud, y así lo razonó CJ en 5.3).

### 2.3 (P3) Destinatario: ¿quien reservó, o todos los familiares con acceso?

**Quien reservó la cita (`citas.cuenta_id` y `citas.vinculo_id`), nadie más.**

- **Coherencia con lo que CJ ya decidió.** Solo quien reservó ve y cambia la cita (5.3, líneas 100 a 103, con el argumento de minimización del art. 5.1.c RGPD). **Los demás familiares no la ven en su agenda** (`SqlFamilyAppointments.cs`, línea 299). Un correo a ellos les diría «hay un cambio en una cita» y, al entrar, no encontrarían nada: es útil para nadie y **les revela que existe una cita** que CJ decidió que no vieran.
- **Riesgo de «todos»:** (i) más direcciones, y cada una puede estar mal escrita (la dirección la teclea Administración y solo se comprueba que lleve una «@», `FamilyMember.cs`, líneas 143 a 148); (ii) revela entre familiares que alguien pidió una cita con el equipo; (iii) cada familiar debería haber sido informado de que recibirá avisos de citas ajenas; (iv) rompe el destinatario único que sostiene el análisis de la propuesta.
- **Riesgo de «solo quien reservó»:** esa persona puede haber perdido el acceso (desvinculada, autorización caducada, cuenta cambiada). Entonces **no se envía** (`OMITIDO / SIN_ACCESO`) y nadie recibe correo: el teléfono es el canal, como hoy. Se acepta: es más seguro no avisar que avisar a quien ya no debe.
- **Si el reservador cambió de cuenta:** se usa la cuenta y el vínculo guardados en la cita, no «el familiar principal» ni «el contacto de urgencia».
- **Un solo `Para`, sin `Cc` ni `Cco`** (borrador de la propuesta, 2.3). **Un correo por cambio**, nunca una lista de cambios ni un recuento.
- **Dos cambios seguidos en la misma cita** (por ejemplo, Administración reprograma y a los diez minutos cancela): son dos números de cambio y por defecto **dos correos idénticos**. Es feo pero no filtra nada. Si molesta, se resuelve con espera de unos minutos y enviar solo el último, cuando exista la tarea programada (no antes).

### 2.4 (P4) Base jurídica e información a la familia

**La base jurídica no cambia respecto al aviso de propuesta [Suposición; Decidir DPO]:** art. 6.1.b (servicio que el centro presta a la familia, si es representante o parte), 6.1.f (interés legítimo del centro en coordinar las citas) en el resto, 6.1.e si el centro es público. No hay dato del art. 9: el correo no lleva salud. No hace falta consentimiento mientras el aviso sea operativo y no comercial (LSSI, a verificar con abogado; no cito el artículo de memoria).

**Qué sí cambia:**

1. **La finalidad se amplía** (art. 6.4 RGPD, compatibilidad): de «avisarte de que tienes algo pendiente que responder» a «avisarte de novedades sobre tus citas: una propuesta, un cambio o cancelación hecho por el centro y, más adelante, un recordatorio». El texto del alta y del portal que propone el borrador de la propuesta (apartado 6) dice «algo pendiente que responder… por ejemplo una propuesta de cita»: **ya no es exacto**, porque un cambio no es algo que haya que responder. Hay que actualizarlo **antes** del primer envío real del aviso nuevo (art. 13 RGPD). Texto propuesto en el apartado 7.
2. **Derecho a no recibirlos.** Con 6.1.f (o 6.1.e) existe el **derecho de oposición** (art. 21 RGPD) y su existencia debe mencionarse explícitamente a la persona, de forma clara y separada (art. 21.4; texto comprobado el 2026-10-31 en una reproducción del Reglamento, `privacy-regulation.eu/es/21.htm`, **no oficial: contrastar con EUR-Lex o el BOE**). Con 6.1.b no se aplica del mismo modo, pero el modo práctico es el mismo: **la familia pide al centro que quite su correo** y Administración lo borra de la ficha. Una consecuencia que la familia debe entender: **quitar el correo apaga todos los avisos** (propuesta, cambio, recordatorio), y los cambios del centro solo se verían al entrar en ResidApp. Un interruptor por tipo de aviso es recomendable, no necesario.
3. **Dónde se avisa**, además del campo del correo y la política del portal: una línea fija en la pantalla de la agenda de citas («Si el centro cambia o cancela una cita que reservaste y tenemos tu correo, te mandaremos un aviso sin detalles. Los detalles solo se ven aquí»), para quien ya tiene el correo guardado de antes y nunca vio el texto del alta. **Las familias que ya dieron su correo antes de existir este aviso** reciben, por tanto, una finalidad nueva: la residencia debe decidir cómo se lo comunica (Decidir DPO: ¿basta la política y la línea de la agenda, o hay que avisar a cada familia?). Con datos de prueba no aplica.
4. **El registro de actividades** pasa a describir «avisos sobre citas» en general y no solo «cuando el centro propone una cita» (apartado 5).

**Qué no cambia:** quién es responsable (la residencia) y encargada (ResidApp), Azure Communication Services como subencargado, el contrato, la ubicación de datos, la ausencia de seguimiento de aperturas, el registro de envíos sin dirección, y la autoridad de control (AEPD o APDCAT, a verificar según el tipo de centro). **No veo una evaluación de impacto nueva**: no hay categoría de dato nueva ni destinatario nuevo. Basta una línea en la del Portal Familiar. [Decidir DPO.]

### 2.5 (P5) Tabla, plantilla y clave compartidas con el recordatorio

**Tabla `envios_correo`: sí, compartida. Plantilla: una por aviso. Clave: la de la tabla, con la referencia distinta.**

| Aviso | `plantilla_codigo` | `referencia_id` | `referencia_clave` |
|---|---|---|---|
| Propuesta (existe) | `AVISO_PROPUESTA_CITA` | la solicitud | número del cambio `PROPUESTA` |
| **Cambio o cancelación** | `AVISO_CAMBIO_CITA` | **la cita** | **número del cambio en `citas_cambios`** (reprogramación o cancelación) |
| Recordatorio | `RECORDATORIO_CITA` | la cita | **fecha de la cita** (día local, `aaaa-mm-dd`), no el día de envío |

Por qué:

- **Dos plantillas distintas, no una con parámetro.** Cada texto se versiona y se aprueba por separado y la plantilla no recibe nada más que idioma y URL (condición 7 de la propuesta). Una plantilla «de cita» con un parámetro «qué pasó» recibiría justo lo que se quiere que no reciba.
- **Marco y adaptador compartidos** (`EmailLayout`, `IEmailSender`, el icono): sí. Es lo que mantiene comprobable que no hay recursos remotos ni texto oculto (borrador del icono, apartado 4).
- **Clave del cambio = cita + número del cambio.** Cada cambio tiene su número (`UX_citas_cambios_numero`, `0054`, línea 128): dos reprogramaciones, dos avisos; el mismo cambio dos veces, uno. La clave del recordatorio es la **fecha de la cita**: si la cita se mueve de día, hay un recordatorio nuevo para el día nuevo; si se mueve de hora el mismo día, no se repite.
- **Qué se guarda y qué no (minimización).** Nada nuevo: ni la dirección, ni el tipo de cambio, ni la hora, ni el motivo, ni la fecha de la cita fuera de la clave del recordatorio. **Una fecha de cita en `referencia_clave` es información que ya está en `citas`**; con la misma conservación que el resto de la tabla (12 meses como tope, propuesta de ingeniería, **no plazo legal**, [Decidir DPO]) no abre un tratamiento nuevo. Al unir `referencia_id` y `referencia_clave` con `citas_cambios` se sabe si fue cancelación o reprogramación: ese dato ya está ahí y no se duplica.
- **El volumen sube** (una cita puede dar un recordatorio y varios avisos de cambio) y la **vía de purga sigue sin existir** (ADR 0012, línea 64). Pasa de «recomendable» a **imprescindible antes de producción**.
- **Script nuevo (`0082` o el siguiente libre):** ampliar `CK_envios_correo_plantilla` con los dos códigos y `CK_envios_correo_motivo` con `CITA_NO_VIGENTE` y `CITA_PASADA` (caben en las columnas: 17 y 24 caracteres; `plantilla_codigo` NVARCHAR(32), `motivo_codigo` NVARCHAR(24), `referencia_clave` NVARCHAR(40)). El disparador `TR_envios_correo_guard` y la seguridad por filas no cambian.
- **Un interruptor por plantilla**, además de `Correo__Activo` (por ejemplo `Correo__AvisoCambioCita`, por defecto apagado). Así se puede encender el aviso de propuesta en el piloto sin encender el de cambio mientras espera su validación.
- **Reglas del recordatorio al convivir con estos avisos** (no leo aquí su texto): comprobar en el momento de enviar que la cita sigue `CONFIRMADA`, que su fecha local es la de la clave y que el residente sigue activo; nunca recordar una cita cancelada o movida a otro día; hora de envío fija y razonable (no de madrugada), decisión de ingeniería; envío desde la tarea programada con tope de edad. **Si el centro cambia a mañana una cita y también toca recordatorio, la familia puede recibir dos correos el mismo día:** aceptable, pero es otra razón para que ambos textos sean claramente distintos.

### 2.6 (P6) Efectos sobre Administración, y riesgos nuevos

- **No presentar el correo como el aviso.** El texto de las pantallas de Administración debe seguir diciendo «Avísala también por teléfono» (`ReprogramarCita.cshtml`, línea 21) y **no** prometer que la familia ha sido avisada: el envío puede fallar, la dirección puede no existir o no leerse. Si se añade una frase («se enviará un aviso genérico por correo a quien la reservó, si tiene correo»), debe depender de que el envío esté encendido, para no decir algo falso con el interruptor apagado.
- **Casilla opcional «No mandar aviso por correo; voy a llamar yo»** [Decidir Orlando + CJ]. Evita el correo a destiempo en casos delicados (fallecimiento antes de la baja, hospitalización), pero es una excepción que cualquiera puede marcar por descuido. Si se añade, la acción queda en la auditoría de la cita y en el registro de envíos como `OMITIDO / DECIDIDO_POR_ADMIN` (motivo nuevo). **No es imprescindible**; se puede construir sin ella.
- **Más correos, más hábito de pulsar enlaces.** Cada aviso útil enseña a la familia a abrir el enlace de un correo de «ResidApp»; el código de acceso (pieza 3) será el objetivo más atractivo de un fraude. Medidas: mismo marco, mismo remitente, **una sola dirección en cada correo**, la frase contra el fraude en todos, DMARC reforzado antes de producción (borrador de la propuesta, 2.6).
- **Tiempo de respuesta de la pantalla.** Si el envío se hace dentro de la petición (como la propuesta, hasta 10 segundos de tope, `ProposalNoticeService.cs`, línea 17), Administración espera ese tiempo cuando ACS va lento. Decisión de ingeniería; no cambia la privacidad.

### 2.7 (P7) Resumen de lo que cambia en los documentos

Las listas del apartado 6 (ADR 0012 y borrador de la propuesta) y el apartado 4 (quién decide qué).

## 3. Condiciones que el código debe cumplir (comprobables con pruebas)

Se suman a las 16 del borrador de la propuesta y a las del borrador del icono, que siguen valiendo (una sola dirección en cada correo, sin recursos remotos, sin `Reply-To`, sin dirección en registros, etc.). Las nuevas, con prefijo **N**:

1. **N1. Solo dos puntos de enganche, y después del `Commit`.** `CancelAsync` y `RescheduleAsync` de **Administración**. **No** en: la reserva (`CREADA`), la finalización, la cancelación o reprogramación hecha por la familia (`SqlFamilyAppointments.cs`, líneas 202 y 230), ni la cascada de la baja (`SqlResidentStatusRepository.cs`, líneas 98 a 113). Los dos métodos del repositorio devuelven el número del cambio. Pruebas: cada camino con un `IEmailSender` falso cuenta cuántos envíos se piden (Administración cancela: 1; reprograma: 1; familia cancela o reprograma: 0; finaliza: 0; baja con citas futuras: 0).
2. **N2. Un solo destinatario: quien reservó.** El correo se lee de `citas.cuenta_id` y `citas.vinculo_id` **en el momento de enviar**, con `SqlFamilyPortalReader.AccessibleLinks` (la misma consulta que el portal, no una copia). Prueba: con dos familiares con acceso al mismo residente, solo se envía al que reservó; el mensaje tiene un `Para` y ningún `Cc` ni `Cco`.
3. **N3. Se vuelve a comprobar al enviar.** Se omite (`OMITIDO`) si: la cita ya no existe; el último cambio de la cita **no es** el notificado (hubo otro después); el actor del cambio no es `ADMINISTRACION` o `causa_codigo` no es nulo (cualquier causa de sistema, hoy `BAJA_RESIDENTE`); en una reprogramación, la cita ya no está `CONFIRMADA` o su hora no es la `inicio_nuevo_en` del cambio; **la hora relevante ya pasó** (la nueva en una reprogramación, la original en una cancelación): `CITA_PASADA`; el residente no está activo, tiene una suspensión abierta o el vínculo o la autorización ya no valen (`SIN_ACCESO`); el familiar no tiene correo (`SIN_CORREO`). Una prueba por caso.
4. **N4. El motivo nunca se lee.** La consulta que prepara el envío no selecciona `citas_cambios.motivo`, ni el `detalle` de las solicitudes. Prueba: el texto de la consulta no contiene esas columnas (revisión + prueba sobre la constante SQL), y el generador de texto no tiene ningún parámetro de motivo.
5. **N5. La plantilla recibe solo idioma y URL** (condición 7 del borrador de la propuesta, intacta). Una sola plantilla para cambio y cancelación; **la acción del cambio no entra en `Render`**. Prueba por reflexión sobre la firma.
6. **N6. Texto aprobado y comparable.** Asunto y texto plano son literalmente los del apartado 2.1, en `es` y `ca`; el texto visible del HTML es el del texto plano más «ResidApp»; mismas pruebas A1 a A8 del borrador del icono aplicadas a esta plantilla. La palabra «centro» solo puede aparecer en las frases aprobadas del final (A7), **nunca en el asunto ni en el primer párrafo**: prueba nueva sobre esas dos zonas.
7. **N7. No se envía dos veces.** Clave única (`AVISO_CAMBIO_CITA`, cita, número del cambio). Prueba de concurrencia: dos llamadas simultáneas, un envío; dos reprogramaciones seguidas, dos avisos.
8. **N8. Nada tras la baja.** Si el residente se da de baja entre el cambio y el envío, `OMITIDO / SIN_ACCESO` (lo da `AccessibleLinks`: residente `ACTIVE`). Prueba: cancelar y dar de baja antes de enviar.
9. **N9. Ningún fallo cambia el resultado de la acción.** Un `IEmailSender` que lanza excepción o tarda más del máximo: la cita queda cancelada o reprogramada, `Ok` para Administración, y el registro dice `FALLIDO` con código cerrado.
10. **N10. Interruptor propio.** `Correo__Activo` falso **o** `Correo__AvisoCambioCita` falso (por defecto): no se reserva fila ni se envía. En desarrollo, solo a la lista de direcciones permitidas.
11. **N11. Registro sin contenido.** Las columnas de la tabla no cambian (condición 12 de la propuesta); los códigos nuevos son cerrados; la prueba de esquema sigue fallando ante cualquier columna con dirección, asunto, cuerpo, hora o motivo.
12. **N12. Sin reintentos hasta que haya tarea programada.** Cuando exista: todo reintento repite N3, con tope de intentos y **nunca después de la hora relevante de la cita**.
13. **N13. Textos de Administración.** Las pantallas de cancelar y reprogramar conservan «Avísala también por teléfono»; ninguna dice que «la familia ha sido avisada por correo».
14. **N14. Logs.** Solo plantilla, estado y código; nunca dirección ni excepción completa (condición 13 de la propuesta).

## 4. Qué queda para una persona

**Orlando** (producto y arranque):
1. Aceptar «un único aviso» (recomendado) o pedir dos; fijar el asunto definitivo («cambio» o «novedad»); aceptar la línea «Aquí no ponemos los detalles…».
2. Decidir si hace falta la casilla «No mandar aviso; voy a llamar yo».
3. Decidir si el envío es dentro de la petición o espera a la tarea programada (y su efecto en que haya dos correos si se cambia dos veces).
4. Decidir qué ocurre con los otros cambios del centro sobre la familia que **no** cubre esta decisión: cuando el centro **cierra una solicitud o retira una propuesta** (`AdministracionController.SolicitudesCitas.cs`, línea 112) la familia ya recibió el aviso de propuesta, así que podría llegar a un correo «pendiente» que ya no existe. Mi lectura: **no ampliar ahora**, pero que esté decidido por escrito.

**CJ** (regla de negocio): extender la redacción de FAM-10 que espera su firma para que incluya «cambio o cancelación de una cita por el centro»; confirmar que el aviso nuevo es un cambio de producto aceptado (hoy solo consta la decisión de Orlando); revisar el catalán. **Sin esto, FAM-10 y el alcance contradirían a la aplicación.**

**DPO real o abogado:** aceptar el criterio de «cambio» sin distinguir; la finalidad ampliada y cómo se comunica a las familias que ya dieron su correo; la mención del derecho de oposición; la conservación del registro de envíos (propuesta 12 meses) y su purga; si basta una línea en la evaluación de impacto del Portal Familiar; que la LSSI no se aplica a este aviso.

**Residencia:** cómo avisa a la familia cuando el correo no llega o la persona que reservó ya no tiene acceso; qué hace Administración ante un cambio delicado (llamar antes).

**Se puede construir ya, apagado y con datos de prueba:** puerto y servicio del aviso, plantilla en `es` y `ca` (marcada como borrador), script de tabla (códigos nuevos), consulta de comprobación (N3), retorno del número de cambio en los dos métodos de Administración, interruptor propio, y todas las pruebas con el `IEmailSender` falso. **No se enciende con direcciones reales** hasta tener validados: la información a la familia (apartado 7), FAM-10 ampliado, la validación del DPO sobre la finalidad ampliada y el contrato de Azure.

## 5. Entrada del registro de actividades (art. 30): lo que cambia respecto al borrador de la propuesta

| Campo | Propuesta |
|---|---|
| Nombre de la actividad | «Avisos genéricos por correo a familiares sobre sus citas: propuesta, cambio o cancelación hecha por el centro y recordatorio» |
| Finalidad | Que el familiar sepa que hay una novedad en una cita suya y la consulte dentro de la aplicación. El correo no lleva datos del residente, de la cita ni de salud. |
| Personas afectadas | Familiares y representantes con correo registrado, acceso al portal y que reservaron o solicitaron la cita |
| Datos | Dirección de correo; identificador del familiar, de la solicitud o la cita y del cambio; plantilla, versión, idioma, estado y momento del envío (sin contenido). **Sin datos de salud.** |
| Base jurídica, destinatarios, transferencias, seguridad, autoridad | Igual que el aviso de propuesta; en seguridad, añadir «destinatario único: quien reservó la cita; el motivo del cambio no se lee ni se envía» |
| Conservación | La misma que el registro de envíos: **[Decidir DPO]**, propuesta 12 meses |

Parte B (ResidApp, encargada): sin cambios; la instrucción de la residencia debe incluir los tres tipos de aviso.

## 6. Qué cambiar en el ADR 0012 y en el borrador de la propuesta (lista de frases; no lo hago yo)

**ADR 0012** (`docs/decisiones-arquitectura/0012-envio-correos.md`):

1. **Líneas 3 a 7 (cabecera):** añadir la decisión de Orlando del 2026-10-31 (aviso de cambio o cancelación de cita confirmada) y su estado.
2. **Líneas 11 a 17 (Contexto, lista de decisiones):** añadir un punto «Aviso de cambio o cancelación por el centro (Orlando, 2026-10-31; **no** pasó por CJ: pendiente de su confirmación)». La frase «tres preguntas» (línea 11) y «ambas cosas» (línea 21) dejan de ser exactas.
3. **Línea 28 (Requisito 1):** «Solo que hay algo pendiente en ResidApp o el código» → añadir «o que hay un cambio en una cita». Añadir: «El correo no distingue entre reprogramación y cancelación, no dice día, hora, equipo, modalidad ni motivo, y el motivo del cambio no se lee nunca».
4. **Líneas 34 a 35 (Requisito 3):** añadir «y, para el recordatorio, la fecha de la cita como clave; para el cambio, el número del cambio de la cita».
5. **Líneas 38 a 39 (Requisito 5):** «Sin correo no hay aviso ni recordatorio» → «ni aviso de cambio».
6. **Líneas 40 a 41 (Requisito 6):** «la propuesta de cita se guarda igual y el envío se reintenta» → «la propuesta, el cambio o la cancelación se guardan igual; el envío se reintenta solo cuando exista tarea programada y nunca después de la hora de la cita».
7. **Líneas 42 a 47 (Requisito 7):** la lista de plantillas pasa a tres (`AVISO_PROPUESTA_CITA`, `AVISO_CAMBIO_CITA`, `RECORDATORIO_CITA`) más el código de acceso.
8. **Líneas 57 a 65 (Estado de la construcción):** añadir la pieza «aviso de cambio o cancelación» como no construida; en «Falta» añadir la ampliación de FAM-10, la información a la familia ampliada y la vía de purga (que pasa a ser imprescindible antes de producción).
9. **Líneas 88 a 89 (Recomendación, clave única):** «(plantilla, cita o cuenta, día; para el aviso de propuesta, el número del cambio PROPUESTA, no el día)» → añadir «para el aviso de cambio, el número del cambio de la cita; para el recordatorio, la fecha de la cita».
10. **Líneas 90 a 91 (Orden de construcción):** el aviso de cambio no necesita tarea programada y puede ir antes del recordatorio.
11. **Líneas 95 a 100 (Lo que debe revisar el DPO):** añadir «la finalidad ampliada a todos los avisos de citas, el destinatario único y el criterio de no distinguir cambio de cancelación».
12. **Líneas 104 a 105 (Consecuencias):** «los tres puntos de CJ» → cuatro, con el aviso de cambio.

**`aviso-cita-por-correo.md`** (borrador de la propuesta):

1. **Línea 7 (Alcance):** «El recordatorio del día antes y el código de acceso quedan fuera» → remitir también a este documento para el aviso de cambio y las condiciones de la tabla compartida.
2. **Línea 95 (Destinatario único):** añadir que para el cambio el destinatario es quien reservó la cita (`citas.cuenta_id` y `citas.vinculo_id`).
3. **Línea 118 (Clave única):** añadir las dos claves nuevas (apartado 2.5).
4. **Línea 122 (Conservación):** precisar que vale para todas las plantillas de la tabla y que la vía de purga sube a «imprescindible antes de producción».
5. **Líneas 208 a 211 y 216 (Registro de actividades):** nombre, finalidad y datos según el apartado 5 de este documento.
6. **Líneas 223 a 227 (Información al familiar):** sustituir por el apartado 7.
7. **Línea 240 (A verificar, punto 10):** «Fuera de alcance, para más adelante: el recordatorio» → quitar el aviso de cambio de esa lista.
8. **Línea 197 y 199 (Qué queda para una persona):** sumar la finalidad ampliada y la aceptación de «cambio».

## 7. Información al familiar (art. 13): textos propuestos para el alta y el portal

Sustituyen a los del apartado 6 del borrador de la propuesta, antes de usar el aviso nuevo con direcciones reales. El recordatorio va entre corchetes: se incluye solo cuando se construya.

**Junto al campo del correo (castellano):** «Si nos das tu correo, el centro te avisará cuando haya algo que mirar en ResidApp sobre tus citas: una propuesta de cita pendiente de tu respuesta, un cambio o una cancelación hecha por el centro en una cita que reservaste [y un recordatorio el día antes de una cita]. El aviso no dice nada sobre la persona a la que cuidas, ni la fecha, ni el motivo, ni su salud: solo que entres en ResidApp. Es voluntario y puedes quitarlo cuando quieras pidiéndolo al centro; si lo quitas, no recibirás ningún aviso por correo y verás las novedades al entrar en ResidApp. [El centro te llamará si algo es urgente: la residencia decide si lo promete.]»

**Catalán:** «Si ens dónes el teu correu, el centre t'avisarà quan hi hagi alguna cosa a mirar a ResidApp sobre les teves cites: una proposta de cita pendent de resposta, un canvi o una cancel·lació feta pel centre en una cita que vas reservar [i un recordatori el dia abans d'una cita]. L'avís no diu res sobre la persona de qui tens cura, ni la data, ni el motiu, ni la seva salut: només que entris a ResidApp. És voluntari i el pots treure quan vulguis demanant-ho al centre; si el treus, no rebràs cap avís per correu i veuràs les novetats en entrar a ResidApp. [El centre et trucarà si alguna cosa és urgent: la residència decideix si ho promet.]»

**En la pantalla de la agenda de citas de la familia (una línea fija):** «Si el centro cambia o cancela una cita que reservaste y tenemos tu correo, te mandaremos un aviso sin detalles. Los detalles solo se ven aquí.»

**Segunda capa (para que la residencia la adopte):** responsable ([RESIDENCIA], [DPO]); finalidad ampliada; base jurídica; **derecho de oposición mencionado de forma clara y separada si la base es el interés legítimo o la misión pública**; destinatarios (el proveedor del correo, a través de ResidApp); conservación; derechos y cómo ejercerlos; reclamación ante [AUTORIDAD]; y que, si no se facilita el correo, no pasa nada: los avisos están también dentro del portal.

## A verificar o decidir

1. **[Decidir Orlando]** Un único aviso «hay un cambio» (recomendado) o dos; asunto («cambio» o «novedad»); línea «Aquí no ponemos los detalles»; casilla opcional «No mandar aviso; voy a llamar yo»; envío dentro de la petición o diferido.
2. **[Decidir CJ]** Ampliar la redacción de FAM-10 a «cambio o cancelación de una cita por el centro» antes de que firme la respuesta pendiente (`aviso-citas-por-correo-cj.html`, líneas 314 y 315); confirmar el producto; revisar el catalán.
3. **[Decidir DPO]** Base jurídica por tipo de familiar (sin cambio respecto a la propuesta); finalidad ampliada y cómo se comunica a quienes ya dieron su correo; mención del derecho de oposición; conservación del registro de envíos (propuesta de ingeniería 12 meses, **no es un plazo legal**) y su purga; una línea en la evaluación de impacto del Portal Familiar.
4. **A verificar (abogado):** que la LSSI no trata este aviso de servicio como comunicación comercial; no cito artículos de memoria.
5. **A verificar:** el texto del art. 21 RGPD contra EUR-Lex o el BOE (la consulta de hoy fue a una reproducción no oficial); la autoridad de control (AEPD o APDCAT) según el tipo de centro piloto; el Esquema Nacional de Seguridad si el centro es público o concertado.
6. **A comprobar al construir:** que `CancelAsync` y `RescheduleAsync` devuelven el número del cambio y que la baja no dispara el aviso (N1 y N8); que la consulta del envío no lee el motivo (N4).
7. **Abierto, fuera de este documento:** cierre de solicitud o retirada de propuesta por el centro (apartado 4, Orlando, punto 4); texto y hora de envío del recordatorio; código de acceso.
8. **Lo que valida una persona antes de datos reales:** textos (es y ca), información a la familia ampliada, FAM-10, criterio de «un único aviso», conservación, contrato de Azure y B10 (heredados de la propuesta).

Fuentes consultadas el 2026-10-31: repositorio (archivos y líneas citados en el texto); `https://www.privacy-regulation.eu/es/21.htm` (reproducción no oficial del art. 21 RGPD, apartados 1 y 4, **a contrastar con EUR-Lex o el BOE**).
