# Recordatorio de cita por correo (el día antes): lectura de privacidad y condiciones de construcción

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-14. Segunda lectura experta, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea), **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado.

Complementa, sin repetirlos: `docs/privacidad/aviso-cita-por-correo.md` (propuesta), `docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md` (cambio; sobre todo 2.5), `docs/privacidad/direccion-confirmada-familia.md` y `docs/privacidad/tarea-correo-identidad-rls.md`. **No modifico ninguno** de ellos ni el ADR 0012: el apartado 6 lista las frases que habría que tocar. Lo ya resuelto en esos documentos (un solo destinatario, `Para` sin copias, marco y pie comunes, sin seguimiento de aperturas, sin dirección en registros, clave única, interruptor propio, lectura con la misma consulta que el portal) **se hereda tal cual** y aquí solo se enumera cuando cambia o se aplica de otra forma.

Términos, una frase cada uno: **encargada** es quien trata datos por cuenta de otro (ResidApp, por cuenta de la residencia); **clave única** es una regla de la base de datos que impide guardar dos filas con los mismos valores (aquí evita mandar dos veces el mismo recordatorio); **procedimiento almacenado** es un programa pequeño que vive dentro de la base de datos y hace una tarea concreta con permisos propios; **EXECUTE AS OWNER** es hacer que ese programa se ejecute con los permisos del dueño de la base, no con los de quien lo llama; **vista previa** es lo que el móvil enseña de un correo sin abrirlo; **franja** es el tramo de horas permitido para enviar.

## Conclusión

**Admisible con condiciones.** Es el tercer aviso genérico del mismo marco y no añade categoría de dato ni destinatario nuevos. Pero hay **tres novedades reales** respecto a los dos avisos ya construidos, y las tres condicionan el diseño:

1. **Primer correo que lleva un dato de la cita: «mañana».** Los otros dos avisos no dicen día alguno. «Mañana» es una fecha relativa, no es dato de salud y CJ lo aprobó (6.2 B), pero **la frase «no dicen la fecha» que hoy se lee a las familias y se publica en la política deja de ser cierta** para este aviso (apartado 6, lo más urgente de documentar).
2. **Primer envío que nadie desencadena.** Los otros dos nacen de un clic de Administración o de la familia; este lo dispara el reloj. Eso exige una franja horaria, un límite de antigüedad y un diseño de «reserva» que no abra permisos de escritura de más al usuario de la tarea (apartados 2 y 7).
3. **Primer envío que puede llegar en un momento delicado sin que nadie lo haya tocado:** el residente fallece u hospitalizan esa noche y la baja aún no está registrada; el correo sale igual (apartado 3, riesgo residual).

Se puede construir **ya con datos de prueba y apagado por defecto**. Antes de la primera dirección real, además de lo heredado: la información a la familia reescrita (apartado 6) y que «Siempre activo» esté encendido en la Web App (sin él, la tarea no corre a las 10:00 y los recordatorios se pierden **sin error visible**).

## 0. Qué hay hoy en el repositorio [Hecho]

- **Decisión de CJ (6.2 B, 2026-10-25):** recordatorio el día antes, «Tienes una cita mañana en ResidApp», sin decir con quién ni para qué; un segundo recordatorio (C) es «demasiado para el piloto» (`respuestas-cj-local/confirmaciones-tercera-tanda-cj.respuestas.json`, líneas 134 a 139). La FAM-10 reescrita por CJ nombra «recordatorio de cita» (`docs/bocetos-pantallas/wireframes-funcionales/portal-familiar.md`, línea 22), así que **el recordatorio sí está cubierto por la modificación de FAM-10**, a diferencia del aviso de cambio.
- **Tabla compartida** `dbo.envios_correo` (`0081`, líneas 17 a 44): clave única (`plantilla_codigo`, `referencia_id`, `referencia_clave`); `plantilla_codigo` NVARCHAR(32) y `motivo_codigo` NVARCHAR(24); `referencia_clave` NVARCHAR(40) (cabe `aaaa-mm-dd`). Los dos CHECK (plantilla y motivo) se han rehecho ya en `0082`, `0084` y `0089`: la lista de motivos de hoy está en `0089`, líneas 26 a 28.
- **El rol `residapp_tareas` no tiene INSERT** en `envios_correo`; solo `SELECT` y `UPDATE` de cinco columnas (`0083`, líneas 30 y 31). Sí puede leer `citas` (`id, centro_id, estado_codigo, inicio_en, cuenta_id, vinculo_id`), `familiares.correo` y `correo_confirmado_en`, `residentes.estado`, suspensiones y autorizaciones (`0083`, líneas 32 a 38; `0084`, línea 61). **No** puede leer `citas.creada_en`, `citas_cambios.ocurrido_en` ni `citas_cambios.motivo` (con `DENY`, línea 40).
- **El cierre de avisos sin resolver (`0089`) está escrito por plantilla**: solo mira `AVISO_PROPUESTA_CITA` y `AVISO_CAMBIO_CITA` (líneas 47 y 48). **Un `RECORDATORIO_CITA` que se quede PENDIENTE no lo cerraría nadie** (apartado 7). La purga a 12 meses (`0085`) no distingue plantilla y sí lo cubriría.
- **El servicio y el almacén del aviso de cambio** (`ChangeNoticeService.cs`, líneas 15 a 127; `SqlChangeNoticeStore.cs`, líneas 22 a 49 y 89 a 121) son el patrón: reservar, reclamar (`intentos` pasa a 1), volver a comprobar con `AccessibleLinks`, un correo, resolver. Allí la espera es de 5 minutos y la tarea corre cada minuto (`ChangeNoticeWorker.cs`, línea 24).
- **Cita y quién la reservó:** `citas.cuenta_id` y `vinculo_id` son de quien reservó y no cambian (`0054_citas_reserva_directa.sql`, líneas 89, 90 y 144). En la reserva directa, la cita nace con `creada_en = now` (`SqlFamilyAppointmentRequests.cs`, líneas 175 a 181 para la aceptación de una propuesta; la reserva directa es análoga).
- **Antelación mínima configurable de 0 a 720 horas** (`0054`, línea 31; `Appointments.cs`, líneas 131 y 175 a 176), y **Administración puede reprogramar o proponer con antelación 0** (`SqlAppointmentServiceRepository.cs`, línea 257; `SqlAppointmentRequestRepository.cs`, línea 69). Es decir: existen citas creadas para mañana o para hoy mismo.
- **Zona horaria:** la aplicación exige `Europe/Madrid` al arrancar (`ServerTimeZone.cs`, línea 9; `ci-cd.yml`, líneas 232 a 234). El servidor de base de datos de Azure trabaja en UTC: «el día local» hay que calcularlo con la zona, no con la fecha UTC.
- **El Manual y el flujo dicen hoy «Tampoco hay recordatorios de citas»** (`Manual.cshtml`, línea 582; `flujo-de-trabajo.html`, línea 1830) y la política del portal dice «hoy hay dos» (`Privacy.cshtml`, línea 103).

## 1. Texto del asunto y del cuerpo (pregunta 1)

**¿Es aceptable «mañana» sin revelar nada?** **Sí, con una precisión sobre la información a la familia.** [Suposición; Decidir DPO]

- **El aviso de cambio no dice «mañana» ni ningún día** (`ChangeNoticeTemplate.cs`, líneas 24 a 31): el recordatorio es el primer correo con una referencia temporal. Pero es **relativa** (no «el 12 de noviembre»), sin hora, sin con quién, sin para qué, sin lugar. Quien vea la vista previa aprende «esta persona tiene mañana una cita en algo llamado ResidApp». Es el mismo nivel de pista que «cita» (aceptada por CJ en 6.1 y 6.2) más un adverbio. **No es dato de salud** (art. 4.15 y 9 RGPD: no dice nada sobre la salud, ni el equipo, ni la modalidad).
- **Ventaja de «mañana» sobre la fecha:** no deja en la bandeja una fecha real consultable y, si el correo se abre tarde, no dice algo falso con apariencia de fecha. **Inconveniente:** un correo leído dos días después dice «mañana» y no es cierto. Se mitiga con la franja de envío (apartado 2) y con el hecho de que el correo lleva siempre su fecha de recepción en la bandeja.
- **No se añade la hora.** CJ dijo «sin decir con quién ni para qué» y la hora es un dato de la cita que la familia ve al entrar. Si CJ la quisiera después, es una decisión nueva (repetir esta lectura): la hora en la vista previa permite deducir rutinas.
- **Claramente distinto de los otros dos:** el asunto del recordatorio contiene «cita mañana»; el del cambio, «un cambio en una cita»; el de la propuesta, «propuesta de cita». Las tres primeras líneas (lo que enseña la vista previa) son distintas entre sí. El código de plantilla (`RECORDATORIO_CITA`) también.

**Texto propuesto, castellano (plantilla `RECORDATORIO_CITA`, versión 1, idioma `es`):**

```
Asunto: Tienes una cita mañana en ResidApp

Tienes una cita mañana en ResidApp.

Para ver los detalles, entra en ResidApp: https://app.residapp.es

Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.

No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.

Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.

—
[Mismo pie que los otros avisos, sin cambios (ProposalNoticeTemplate.FooterEs)]
```

**Texto propuesto, catalán (misma plantilla y versión, idioma `ca`; lo revisa el traductor externo, ADR 0012 requisito 7):**

```
Assumpte: Tens una cita demà a ResidApp

Tens una cita demà a ResidApp.

Per veure'n els detalls, entra a ResidApp: https://app.residapp.es
De moment, la pantalla a la qual porta l'enllaç és en castellà.

Aquí no posem els detalls, per protegir la teva privadesa: els veuràs en entrar.

No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.

Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.

—
[Mateix peu que els altres avisos (ProposalNoticeTemplate.FooterCa)]
```

Notas:

1. **Reutiliza las frases ya aprobadas** («Aquí no ponemos los detalles…», «No respondas…», contra el fraude, pie): menos texto que validar y mismas pruebas A1 a A8 y N5 a N6. Lo nuevo es solo la primera línea y la segunda.
2. **«Centro» solo en la frase «llama al centro»**, nunca en el asunto ni en la primera línea (regla A7 del borrador del icono; N6 del cambio).
3. **La plantilla recibe solo idioma y URL.** No recibe fecha, hora, equipo, modalidad, residente ni nada de la cita. El «mañana» es texto fijo: **por eso el código que envía debe garantizar que es verdad** (apartado 2, regla de la fecha).
4. **Sin plural y sin recuento.** Si la misma persona tiene dos citas mañana, recibe **dos correos idénticos** (uno por cita, clave del ADR). Es feo pero no filtra nada y coincide con lo ya decidido para las propuestas (`aviso-cita-por-correo.md`, 2.3). Alternativa más agradable, no recomendada ahora: un solo correo por familiar y día (la clave sería el familiar y la fecha, y «la cita sigue confirmada» pasaría a «alguna sigue confirmada»); toca el ADR y complica las comprobaciones. [Decidir Orlando, solo si el volumen molesta en el piloto.]
5. **Frase opcional** que ayuda a lograr lo que CJ quiere (menos citas perdidas), sin decir nada nuevo: «Si no vas a poder acudir, avísalo cuanto antes.» Sin la palabra «centro». [Decidir CJ.] No la incluyo en el texto propuesto para no ampliar lo aprobado.
6. **«Cita» no dice «presencial»:** las citas telefónicas y por videoconferencia existen (`0054`, línea 43); por eso no se habla de «acudir» en el texto base.

## 2. Hora de envío y tope de antigüedad (pregunta 2)

**Propuesta, con cifras: franja de envío de las 10:00 a las 19:00, hora de Madrid, en el día natural anterior a la cita. Fuera de ella, no se envía y no se recupera.** [Suposición de ingeniería; Decidir Orlando y CJ]

Motivos:

- **A partir de las 10:00:** (a) es horario en el que la familia puede reaccionar y **el centro está abierto** para que la familia lo llame si hay que cambiar algo; un correo a las 07:00 o a las 22:00 pide una acción que no se puede hacer; (b) deja una hora de margen para que Administración registre una baja o una suspensión ocurrida por la noche (riesgo del apartado 3); (c) evita el sueño y el trabajo de madrugada.
- **Hasta las 19:00:** el correo sigue siendo «de ayer para hoy» con tiempo de avisar; más tarde empieza a ser un aviso de noche con vista previa en la pantalla de bloqueo, y el centro ya está cerrado. Es un criterio de uso razonable, **no una exigencia legal**; las horas exactas las puede cambiar la residencia o CJ sin volver a esta lectura mientras sigan en horario diurno.
- **Normalmente sale a las 10:00 y pocos minutos:** la tarea corre cada minuto (`ChangeNoticeWorker.cs`, línea 24). La franja larga existe para absorber un reinicio de la aplicación (un despliegue a las 10:00) sin perder el aviso del día.

**Qué hacer si la tarea estuvo caída:**

- **No se recupera fuera de la franja.** Si a las 19:00 no salió, **no sale nunca**: el texto dice «mañana» y mandarlo el mismo día de la cita (o de madrugada) sería falso. Tampoco se crea un segundo texto («hoy») sin aprobarlo: sería una plantilla nueva y CJ dijo que solo uno.
- **Qué se anota:** si la fila ya estaba reservada, `OMITIDO / FUERA_DE_PLAZO` (motivo nuevo, apartado 7). Si la tarea ni siquiera llegó a reservarla, no hay fila: **no se reserva fuera de la franja**.
- **Al arrancar la tarea tras una caída larga no hay «puesta al día».** Cada vuelta calcula «mañana» con la hora de ese momento.
- **«Si ya pasó la hora de la cita»:** con esta franja es imposible (la cita es de otro día), pero se mantiene la comprobación `CITA_PASADA` como defensa.
- **Regla de la fecha (la que hace verdadero el «mañana»):** la fecha local de la cita, calculada con `Europe/Madrid`, **debe ser igual a la clave de la fila y al día siguiente al de hoy** (hora local). Si no, no se envía. Con los cambios de hora de marzo y octubre esto no afecta a la franja (ocurren de madrugada); hay que probar el cálculo en esos dos domingos.

**Reintentos:** ninguno en esta pieza, igual que en los otros avisos (política «como mucho una vez», `0089`, líneas 7 a 13): un correo duplicado o tardío es peor que uno que falta. Si algún día se añaden, solo dentro de la franja y con tope de intentos, repitiendo todas las comprobaciones.

## 3. Qué debe comprobar la tarea justo antes de enviar (pregunta 3)

Se aplica en este orden (el orden decide el código que queda en el registro; es el de D5 del borrador de la casilla). Todo se lee **en el momento de enviar**, nunca de lo que había al reservar.

| # | Comprobación | Si falla, queda `OMITIDO` con motivo |
|---|---|---|
| 1 | Interruptores: `Correo__Activo` y el propio del recordatorio (apartado 7), ambos encendidos | no se reserva ni se envía (sin fila) |
| 2 | Estamos dentro de la franja 10:00 a 19:00 (hora de Madrid) | `FUERA_DE_PLAZO` |
| 3 | La cita existe y su estado sigue siendo `CONFIRMADA` (no `CANCELADA` ni `FINALIZADA`) | `CITA_NO_VIGENTE` |
| 4 | La fecha local de la cita **es la de la clave** (si no, la movieron de día) | `CITA_NO_VIGENTE` |
| 5 | La fecha local de la cita **es mañana** | `FUERA_DE_PLAZO` |
| 6 | La hora de la cita no ha pasado (defensa) | `CITA_PASADA` |
| 7 | **Quien reservó** (`citas.cuenta_id`, `citas.vinculo_id`) sigue con acceso, con la misma consulta del portal (`SqlFamilyPortalReader.AccessibleLinks`, no una copia): vínculo sin desvincular, autorización `ACTIVA` y vigente, **residente `ACTIVE` y sin suspensión abierta** | `SIN_ACCESO` |
| 8 | Tiene correo | `SIN_CORREO` |
| 9 | La dirección está confirmada (`correo_confirmado_en` no nulo) | `DIRECCION_NO_CONFIRMADA` |
| 10 | Destino permitido (lista de pruebas en desarrollo) | `DESTINO_NO_PERMITIDO` |

Matices:

- **Residente «activo, no suspendido ni de baja»:** lo da el punto 7 (`resident.estado = 'ACTIVE'` y ninguna suspensión con `finalizada_en IS NULL`: `SqlFamilyPortalReader.cs`, líneas 29 a 32). La baja además cancela las citas futuras (`SqlResidentStatusRepository.cs`, líneas 98 a 113), así que cae antes por el punto 3. **La suspensión no cancela citas**, por eso el punto 7 es el que la cubre: con el residente suspendido el portal no deja ver la cita y el correo no debe adelantarse a él.
- **Autorización vigente:** [Suposición] evaluarla con la **fecha de la cita** (mañana) y no con la de hoy: si caduca hoy, mañana la persona no podrá ver la cita y un «Tienes una cita mañana» llevaría a una pantalla vacía. Es un cambio de un parámetro respecto a los otros avisos.
- **Destinatario:** **solo quien reservó la cita** (cuenta y vínculo guardados en ella), no «el familiar principal» ni otros con acceso al mismo residente. Mismas razones que el cambio (`aviso-cita-cambio-cancelacion-por-correo.md`, 2.3): la agenda de la familia filtra por `cuenta_id` y CJ decidió «solo quien la reservó» (5.3), así que **un recordatorio a otro familiar le revelaría una cita que no ve**. Si quien reservó perdió el acceso, nadie recibe correo y el teléfono es el canal (aceptado).
- **Citas por solicitud previa:** la cita nace cuando la familia acepta la propuesta, con la cuenta y el vínculo de quien aceptó (`SqlFamilyAppointmentRequests.cs`, líneas 175 a 181). **Se recuerda a esa persona y a nadie más**; la solicitud y su `detalle` no se leen.
- **Qué no se lee nunca:** `citas_cambios.motivo` y `citas_solicitudes.detalle`. La tarea sigue sin permiso (`DENY`, `0083`, línea 40). El recordatorio **no necesita** leer `citas_cambios` desde el código de la tarea.
- **Un solo `Para`, sin `Cc` ni `Cco`; sin `Reply-To` propio; un solo enlace en texto visible** (heredado).
- **Hallazgo aparte, no solo del recordatorio [Hecho]:** `AccessibleLinks` **no mira el estado de la cuenta** (`cuentas.estado`), solo el vínculo y la autorización. Si Plataforma suspende una cuenta de familia por seguridad (`PlatformApplicationService.cs`, línea 21) pero la autorización sigue `ACTIVA`, la persona seguiría recibiendo los tres avisos. La tarea no tiene `SELECT` sobre `cuentas`. [A verificar si la suspensión de la cuenta revoca ya la autorización; si no, decidir si se concede `SELECT (id, estado)` a `residapp_tareas` y se añade la condición, para los tres avisos.]
- **Riesgo residual que el texto no resuelve (igual que el riesgo 3 del cambio):** el residente fallece o es hospitalizado por la noche, la baja o la suspensión no están registradas a las 10:00 y la familia recibe «Tienes una cita mañana». El texto es genérico, pero el momento puede doler. Orlando decidió **no** añadir una casilla «no mandar aviso» para el cambio; **recomiendo lo mismo aquí** (la aplicación no regula la organización del centro), y que la residencia lo recoja en su procedimiento: registrar bajas y suspensiones a primera hora. [Decidir residencia y DPO.]

## 4. Citas creadas con poca antelación o para el mismo día (pregunta 4)

**Regla propuesta: no se recuerda lo que la familia acaba de reservar, aceptar o mover. Se omite con un motivo nuevo, `CITA_RECIENTE`.** [Suposición; Decidir CJ]

- **Citas de hoy (mismo día):** **nunca se reservan**. Solo se buscan las citas cuya fecha local es mañana. No hay fila ni correo.
- **Cita para mañana a primera hora, reservada o aceptada hoy o ayer a última hora:** un «Tienes una cita mañana» a los pocos minutos de reservar es ruido y puede confundir («¿tengo otra?»). **Se omite la cita cuya reserva o aceptación, o su último movimiento a otro día u hora, ocurrió el mismo día natural (hora de Madrid) que el envío, o después.** Una cita reservada anteanoche a las 23:00 sí se recuerda a las 10:00.
- **Cambio del centro el mismo día:** si Administración reprograma una cita a mañana el día de hoy, la familia ya recibe (o recibirá en 5 minutos) el aviso de cambio. Con la misma regla (cualquier `CREADA` o `REPROGRAMADA` de hoy, de quien sea) **no se manda además el recordatorio**, para no dar dos correos seguidos. Se acepta el coste: esa familia no recibe un «mañana» explícito; el aviso de cambio y el teléfono ya cubren la novedad.
- **Cómo se comprueba sin dar permisos de más al rol de la tarea:** la tarea **no** lee `citas.creada_en` ni `citas_cambios.ocurrido_en` (no tiene permiso). Lo hace el procedimiento que reserva (apartado 7), que se ejecuta como propietario, y deja la fila ya cerrada: `OMITIDO / CITA_RECIENTE`. Así se conserva la explicación («¿por qué no me llegó?») sin ampliar permisos. Alternativa: no crear fila alguna y repetir la comprobación cada minuto; pierde la trazabilidad.
- **Motivo nuevo, código cerrado:** `CITA_RECIENTE` (13 caracteres, cabe en NVARCHAR(24)). Significa «reservada, aceptada o movida el mismo día del envío o después».

## 5. Cuando la cita cambia después de reservar el recordatorio (pregunta 5)

| Situación | Qué ocurre | Resultado en el registro |
|---|---|---|
| La cita se **cancela** antes del envío | No se envía. Si la cancela Administración, la familia recibe el aviso de cambio (no el recordatorio) | `OMITIDO / CITA_NO_VIGENTE` |
| La cita se **mueve a otro día** antes del envío | No se envía por el día viejo (la fecha ya no es la de la clave). El día nuevo tiene su clave y su propio recordatorio la víspera nueva, si no es «reciente» | `OMITIDO / CITA_NO_VIGENTE` en la fila vieja |
| La cita se mueve a **otra hora del mismo día** | Se envía (el texto no lleva hora). La clave no cambia, no hay recordatorio doble | `ENVIADO` |
| Se mueve a una hora **ya pasada** el mismo día | No se envía | `CITA_PASADA` |
| La **baja** del residente cancela las citas | Igual que cancelar; **no** hay aviso de cambio (N1) ni recordatorio | `CITA_NO_VIGENTE` |
| El residente se **suspende** | La cita sigue confirmada, pero no se envía | `SIN_ACCESO` |
| **Ya se envió** y luego el centro cambia o cancela | El aviso de cambio sale 5 minutos después. **Dos correos el mismo día** | `ENVIADO` y la fila del cambio |

**¿Dos correos el mismo día son aceptables?** **Sí, es la consecuencia correcta.** El recordatorio ya salió y el cambio lo corrige; callar el segundo sería peor. Es la razón por la que los dos asuntos y primeras líneas son distintos (apartado 1). No añado una frase al aviso de cambio del tipo «ignora el recordatorio anterior»: diría implícitamente algo que hoy el cambio calla (que hubo un recordatorio, y por tanto la fecha). La familia que lo ignore dispone del teléfono («Avísala también por teléfono» sigue vigente, N13).

**Sobre la frase de CJ «solo un recordatorio»** [Decidir CJ confirmar]: la clave `(plantilla, cita, fecha local)` garantiza **un recordatorio por cita y por fecha**. Si una cita se mueve a otra fecha lejana, esa nueva fecha tiene su propio recordatorio. Creo que es lo que CJ quiere (descartaba un *segundo recordatorio de la misma cita*); conviene que lo confirme por escrito para que no se lea como «una vez en la vida de la cita».

## 6. Información a la familia, registro de actividades y política (pregunta 6)

**Lo que debe cambiar antes del primer envío real**: la frase «los avisos no dicen… la fecha» pasa a ser inexacta (art. 13 RGPD: la información debe ser fiel a lo que se hace). Texto exacto a tocar, por documento:

**a) Texto junto al campo del correo (alta y ficha del familiar; castellano).** Sustituye el texto de `aviso-cita-cambio-cancelacion-por-correo.md`, apartado 7 (el recordatorio estaba entre corchetes):
«Si nos das tu correo, el centro te avisará cuando haya algo que mirar en ResidApp sobre tus citas: una propuesta de cita pendiente de tu respuesta, un cambio o una cancelación hecha por el centro en una cita que reservaste, y un recordatorio el día antes de una cita confirmada. Los avisos no dicen el nombre de la persona a la que cuidas, ni el motivo, ni su salud, ni la hora: solo que entres en ResidApp. El recordatorio, además, dice que tienes una cita mañana. Es voluntario y puedes quitarlo cuando quieras pidiéndolo al centro; si lo quitas, no recibirás ningún aviso por correo y verás las novedades al entrar en ResidApp.»
**Catalán:** «Si ens dónes el teu correu, el centre t'avisarà quan hi hagi alguna cosa a mirar a ResidApp sobre les teves cites: una proposta de cita pendent de resposta, un canvi o una cancel·lació feta pel centre en una cita que vas reservar, i un recordatori el dia abans d'una cita confirmada. Els avisos no diuen el nom de la persona de qui tens cura, ni el motiu, ni la seva salut, ni l'hora: només que entris a ResidApp. El recordatori, a més, diu que tens una cita demà. És voluntari i el pots treure quan vulguis demanant-ho al centre; si el treus, no rebràs cap avís per correu i veuràs les novetats en entrar a ResidApp.»

**b) Ayuda de la ficha del familiar** (`Views/Administracion/Familiar.cshtml`, líneas 77 y 78): «que tiene una propuesta de cita pendiente o que el centro ha cambiado o cancelado una cita que reservó» → añadir «, o un recordatorio el día antes de una cita»; y «Los avisos no dicen el nombre del residente, ni el centro, ni la fecha, ni el motivo» → «Los avisos no dicen el nombre del residente, ni el centro, ni la hora, ni el motivo; el recordatorio solo dice que tiene una cita mañana».

**c) Guion que Administración lee a la familia** (`direccion-confirmada-familia.md`, apartado 5, «Guion para decir a la persona»): «Los avisos no dicen el nombre de su familiar, ni la fecha, ni el motivo» → «Los avisos no dicen el nombre de su familiar, ni la hora, ni el motivo; el recordatorio solo dice que tiene una cita mañana». Es la forma en la que se cumple el art. 13 al recoger el dato, así que **esta es la frase más importante de cambiar**.

**d) Línea fija en la agenda de citas de la familia** (la del apartado 7 de `aviso-cita-cambio-cancelacion-por-correo.md`, aún sin construir): «Si tenemos tu correo, te mandaremos un aviso sin detalles cuando el centro cambie o cancele una cita que reservaste, y un recordatorio el día antes de cada cita confirmada («Tienes una cita mañana»). Los detalles solo se ven aquí.»

**e) Política del portal** (`Views/Home/Privacy.cshtml`; borrador `politica-privacidad-portal.md`):
- Línea 103: «Hoy hay dos» → «Hoy hay tres», y añadir «y «Tienes una cita mañana» (el día antes de una cita confirmada, entre las 10:00 y las 19:00; se envía a quien la reservó y solo si la cita sigue confirmada)».
- Línea 104: «ni la fecha» → «ni la hora»; añadir «El recordatorio solo dice que tienes una cita mañana».
- Línea 109: «solo qué aviso, a qué familiar (por su identificador interno), cuándo y si salió» → añadir «y, en el recordatorio, el día de la cita».
- Cuadro de la línea 318 del borrador: el recordatorio pasa de «No construido» a «Construido, apagado por defecto».

**f) Manual y flujo:** `Manual.cshtml`, línea 582, «Tampoco hay recordatorios de citas» y `flujo-de-trabajo.html`, línea 1830, «Recordatorios de citas. Hoy solo hay dos avisos genéricos…». Los reescribe la sesión principal con `doc-usuario-residapp` al cerrar la construcción (regla del proyecto), no yo.

**g) Registro de actividades (art. 30).** La actividad ya se llama «…propuesta, cambio o cancelación… y recordatorio» (`aviso-cita-cambio-cancelacion-por-correo.md`, apartado 5), así que **no hay actividad nueva**. Cambios de campos:

| Campo | Cambio |
|---|---|
| Finalidad | Añadir: «y que no olvide una cita ya confirmada» |
| Datos | Añadir: «el día local de la cita (en la clave del registro de envíos, solo para el recordatorio)»; sigue sin hora, motivo, equipo ni modalidad |
| Seguridad | Añadir: «envío programado por una tarea del sistema, sin persona conectada, solo entre las 10:00 y las 19:00 del día anterior; no se envía si la cita ya no está confirmada, fue reservada o movida ese mismo día, o el residente está suspendido o de baja; destinatario único (quien reservó); el motivo del cambio y el detalle de la solicitud no se leen» |
| Conservación | Sin cambio (la misma del registro de envíos: tope propuesto de 12 meses, purgado por `dbo.purgar_envios_correo`) |
| Parte B (ResidApp) | La instrucción de la residencia debe decir expresamente que incluye **envíos programados sin intervención de una persona** (hasta ahora, los avisos salían tras una acción) |

**h) ¿Hace falta algo en la política general antes del primer envío real?** Solo lo de (e). **No hace falta consentimiento** si el recordatorio es operativo y no comercial: **comprobado hoy en `normativa-local/lssi.txt`** (BOE actualizado el 2025-12-18): el art. 21.1 prohíbe las «comunicaciones publicitarias o promocionales» no solicitadas y el anexo, letra f, define comunicación comercial como la dirigida a «la promoción, directa o indirecta, de la imagen o de los bienes o servicios» de una persona u organización con actividad profesional. Un recordatorio de una cita ya confirmada no promociona nada; la interpretación la valida el abogado. **Base jurídica (art. 6.1.b, 6.1.f o 6.1.e)** y **derecho de oposición mencionado de forma clara y aparte** si la base es el interés legítimo o la misión pública (art. 21.4 RGPD, comprobado el 2026-11-14 en `normativa-local/rgpd.txt`: «a más tardar en el momento de la primera comunicación… será mencionado explícitamente… y presentado claramente y al margen de cualquier otra información»): sin cambio respecto al aviso de cambio, pero **la oposición cubre ahora tres avisos**, y «quitar el correo» los apaga todos. Finalidad ampliada (art. 6.4): se resuelve con la información de (a) a (e); las familias con correo ya dado antes necesitan que se les cuente (como en el cambio) [Decidir DPO].

**i) Evaluación de impacto:** sin cambios (no hay categoría de dato ni destinatario nuevos). Una línea en la del Portal Familiar basta: «avisos programados». [Decidir DPO.]

## 7. Esquema SQL, quién reserva y permisos del rol (pregunta 7)

**Cambios de esquema necesarios (un script nuevo, el siguiente número libre; hoy el último es `0091`):**

1. **`CK_envios_correo_plantilla`:** añadir `'RECORDATORIO_CITA'` (17 caracteres; cabe en NVARCHAR(32)).
2. **`CK_envios_correo_motivo`:** añadir `'CITA_RECIENTE'` y `'FUERA_DE_PLAZO'`. **El CHECK se rehace entero con la lista de `0089`** (líneas 26 a 28: `SIN_CORREO, SIN_ACCESO, DIRECCION_NO_CONFIRMADA, PROPUESTA_NO_VIGENTE, CITA_NO_VIGENTE, CITA_PASADA, DESTINO_NO_PERMITIDO, ACS_RECHAZADO, ACS_NO_DISPONIBLE, TIEMPO_AGOTADO, RESULTADO_DESCONOCIDO`) **más los dos nuevos**; olvidar uno rompería filas existentes. Actualizar el comentario de `CITA_NO_VIGENTE` («ya no está confirmada o ya no es de la fecha de la clave»).
3. **Un procedimiento nuevo que reserva** (siguiente punto) y su `GRANT EXECUTE` al rol `residapp_tareas`.
4. **`dbo.cerrar_envios_correo_sin_resolver` hay que reescribirlo** (`ALTER PROCEDURE`) **para que incluya el recordatorio**; si no, un recordatorio reclamado que no llega a resolverse quedaría PENDIENTE para siempre. Reglas propuestas: reclamado (`intentos >= 1`) y de más de 30 minutos → `FALLIDO / RESULTADO_DESCONOCIDO` («sin confirmar», como en el cambio); **sin reclamar** (`intentos = 0`) y de más de 24 horas → `OMITIDO / FUERA_DE_PLAZO` (aquí sí es seguro decir «no se envió», porque nunca se intentó, y ya no puede enviarse: pasó la franja). El permiso del rol (solo `EXECUTE`) no cambia.
5. **Sin columnas nuevas** en `envios_correo`, `citas` ni `familiares`. La clave del recordatorio es la fecha local de la cita en texto `aaaa-mm-dd`.
6. **No cambia** `TR_envios_correo_guard`, la seguridad por filas ni la purga.

**¿Quién reserva? Propongo que lo haga la propia tarea, pero por un procedimiento, no con permiso de escritura en la tabla.**

- **Por qué no reservar al crear la cita** (como hace el cambio): dejaría una fila PENDIENTE por cada cita futura durante semanas, y cada cambio de fecha dejaría una fila vieja; el estado «pendiente» sería falso mucho tiempo (art. 5.1.d RGPD, exactitud).
- **Por qué no darle `INSERT` en `envios_correo` al rol:** pasaría de «solo puede resolver filas existentes» (`0083`, línea 31) a «puede fabricar filas de cualquier plantilla, centro y familiar», lo que empeora la integridad del registro si se roba esa credencial. La propia lectura del rol lo advirtió (`tarea-correo-identidad-rls.md`, H4: «no concederlo ahora ni por si acaso»).
- **Propuesta:** `dbo.reservar_recordatorios_cita(@idioma, @version)` con `WITH EXECUTE AS OWNER`, el mismo patrón que `0085` y `0089`, que:
  1. **calcula «mañana» él mismo** con la zona de Madrid (no recibe fechas del llamante: el rol no puede elegir qué día reservar) y **no hace nada fuera de la franja 10:00 a 19:00** (defensa por si el código falla). [A verificar: el nombre de zona que acepta `AT TIME ZONE` en Azure SQL para Madrid; no lo he ejecutado.]
  2. Busca las citas `CONFIRMADA` de todos los centros cuya fecha local es mañana y que **no** tienen ya fila `RECORDATORIO_CITA` con esa clave (`NOT EXISTS`, y captura el error de clave única por si dos copias de la aplicación coinciden).
  3. Las **recientes** (apartado 4) se insertan **ya cerradas** como `OMITIDO / CITA_RECIENTE` con `resuelto_en` y sin intentos; el resto, como `PENDIENTE` con `intentos = 0`. Las dos con el `familiar_id` del vínculo de la cita.
  4. **Devuelve solo cuántas filas creó**: ni ids, ni familiares, ni direcciones (igual que `0085` y `0089`).
  5. Lee `citas.creada_en` y `citas_cambios.ocurrido_en` como propietario; **no lee nunca `citas_cambios.motivo`**.
- **La tarea** (una segunda tarea programada, o ampliar la actual) hace, cada minuto y solo con los dos interruptores encendidos y dentro de la franja: llama al procedimiento, lista sus PENDIENTE sin reclamar (`SELECT` ya concedido), las reclama (`UPDATE intentos = 1`, ya concedido), repite las comprobaciones del apartado 3, envía y resuelve. **Es el patrón de `ChangeNoticeService`, con una diferencia: no espera 5 minutos** (no hay nada que dejar «asentarse»; el recordatorio es del día antes).

**Permisos mínimos del rol `residapp_tareas` para el recordatorio:**

| Permiso | Estado |
|---|---|
| `SELECT` en `envios_correo` y `UPDATE (estado_codigo, motivo_codigo, acs_operacion_id, intentos, resuelto_en)` | **Ya existe** (`0083`) |
| `SELECT` por columnas en `citas`, `residentes_familiares`, `familiares (correo)`, `familiares (correo_confirmado_en)`, `residentes (estado)`, suspensiones y autorizaciones | **Ya existen**; bastan |
| `EXECUTE` en `dbo.reservar_recordatorios_cita` | **Nuevo** |
| `EXECUTE` en `dbo.cerrar_envios_correo_sin_resolver` (reescrito) y `dbo.purgar_envios_correo` | Ya existen |
| `INSERT` / `DELETE` en `envios_correo` | **No se concede** |
| `SELECT` de `citas.creada_en`, `citas_cambios.ocurrido_en` | **No se concede** (lo lee el procedimiento) |
| `citas_cambios.motivo` | **Sigue con `DENY`** |
| `SELECT` de `cuentas.estado` | **Solo si se decide** el hallazgo del apartado 3 (para los tres avisos) |

**Otros requisitos de construcción:**

- **Interruptor propio**, por defecto apagado, además de `Correo__Activo` (por ejemplo `Correo__RecordatorioCita`), igual que `Correo__AvisoCambioCita`.
- **«Siempre activo» encendido en la Web App** (ADR 0012, líneas 67 y 126; hoy desactivado). Con el recordatorio es **obligatorio**: sin él, una aplicación dormida a las 10:00 pierde el recordatorio del día sin dejar fila ni error. Debe ir en el despliegue (`ci-cd.yml`), no a mano. Si hay varias copias de la aplicación, la clave única y la reclamación evitan el doble envío.
- **La tarea debe seguir comprobando su identidad** (`HasTaskIdentityAsync`, `ChangeNoticeWorker.cs`, líneas 43 a 54): con el usuario limitado de la aplicación vería cero filas sin error.
- **Índice:** el procedimiento recorre las citas por fecha de inicio sin conocer el centro; con el volumen del piloto no importa, pero la consulta conviene que use `IX_citas_agenda` o uno por `inicio_en` (cuestión de rendimiento, no de privacidad).
- **Logs:** solo plantilla, estado y código; nunca dirección ni excepción completa (N14).

## 8. Condiciones que el código debe cumplir (comprobables con pruebas)

Se suman a las 16 de la propuesta, a las N1 a N14 del cambio y a las D1 a D10 de la casilla. Prefijo **R**:

1. **R1. Texto aprobado y comparable.** Asunto y texto plano son literalmente los del apartado 1, en `es` y `ca`; el texto visible del HTML es el del texto plano más «ResidApp»; pruebas A1 a A8 del icono. «Centro» solo en la frase final, nunca en el asunto ni en el primer párrafo. La plantilla recibe solo idioma y URL (prueba por reflexión sobre la firma). Las tres plantillas tienen primera línea y asunto distintos (prueba que compara los tres).
2. **R2. «Mañana» es verdad.** Prueba: con la cita en la fecha de la clave pero no mañana, no se envía (`FUERA_DE_PLAZO`); con la fecha distinta de la clave, no se envía (`CITA_NO_VIGENTE`). Casos con los cambios de hora de marzo y octubre (reloj simulado, zona `Europe/Madrid`).
3. **R3. Franja.** Reloj simulado a las 09:59 (no envía y no reserva), 10:00 (sí), 18:59 (sí), 19:00 (no, `FUERA_DE_PLAZO`). La tarea que arranca a las 20:00 no reserva ni envía nada.
4. **R4. Una sola dirección.** Con dos familiares con acceso al mismo residente, solo se escribe a quien reservó (cuenta y vínculo de la cita), incluidas las citas por solicitud previa; un solo `Para`.
5. **R5. Una prueba por motivo** del apartado 3 (puntos 2 a 10): cita cancelada, finalizada, movida de día, movida a hora pasada, residente de baja, residente suspendido, vínculo desvinculado, autorización revocada, caducada o que caduca hoy, sin correo, dirección sin confirmar, destino no permitido.
6. **R6. Reciente.** Cita reservada hoy para mañana; cita reprogramada hoy por Administración; cita reprogramada hoy por la familia: las tres quedan `OMITIDO / CITA_RECIENTE` y **no** se envía. Cita reservada anteayer: sí.
7. **R7. No se envía dos veces.** Clave única `(RECORDATORIO_CITA, cita, aaaa-mm-dd)`: dos llamadas simultáneas y dos copias de la aplicación, un envío; una cita movida a otro día y recordada por ambos días, dos recordatorios (uno por fecha).
8. **R8. El procedimiento no abre permisos.** Prueba con el rol real: puede ejecutar el procedimiento, **no** puede `INSERT` en `envios_correo`, **no** puede leer `citas.creada_en`, `citas_cambios.ocurrido_en` ni `motivo`; el procedimiento no devuelve ids ni direcciones; no reserva nada fuera de franja aunque se llame.
9. **R9. Cierre de pendientes.** Un recordatorio reclamado y de más de 30 minutos queda `FALLIDO / RESULTADO_DESCONOCIDO`; uno sin reclamar y de más de 24 horas queda `OMITIDO / FUERA_DE_PLAZO`; nunca `ENVIADO`; no se reabre.
10. **R10. Ningún fallo cambia nada más.** Un `IEmailSender` que lanza o tarda más del máximo deja `FALLIDO` con código cerrado y no detiene al resto del lote.
11. **R11. Interruptores.** Apagado por defecto; con `Correo__Activo` o el propio apagado, no se reserva ni se envía; en desarrollo, solo a las direcciones permitidas.
12. **R12. Registro sin contenido.** Las columnas no cambian; la prueba de esquema sigue fallando ante cualquier columna con dirección, asunto, cuerpo, hora o motivo; `referencia_clave` solo contiene el día `aaaa-mm-dd`.
13. **R13. Sin cambios en el aviso de cambio ni en la propuesta:** las pruebas existentes (`AvisoCambioCitaTests`, `TareaCorreoRlsTests`) siguen pasando.

## 9. Qué queda abierto para la persona experta (pregunta 8)

1. **DPO real o abogado:**
   - Que «mañana» (referencia relativa, sin hora) no es dato de salud ni da más pistas que «cita», y que **la información a la familia con la frase corregida del apartado 6 es suficiente** (art. 13).
   - Base jurídica del recordatorio (6.1.b, 6.1.f o 6.1.e) y la mención clara del derecho de oposición (art. 21.4); finalidad ampliada y cómo se comunica a las familias que ya dieron su correo (art. 6.4).
   - Que la LSSI no se aplica a este recordatorio por ser un aviso de servicio (texto del art. 21.1 y del anexo f ya comprobado; la interpretación es suya).
   - Que el envío programado sin intervención humana entra en la instrucción de la residencia (contrato de encargado, art. 28.3: tratar solo por instrucción documentada).
   - Conservación: el registro de envíos pasa a guardar el **día de la cita** en la clave; confirmar que los 12 meses de tope (propuesta de ingeniería, **no plazo legal**) siguen valiendo.
   - La franja 10:00 a 19:00 como criterio de proporcionalidad, y si el riesgo residual del recordatorio llegando a una familia ya afectada (apartado 3) obliga a algo más que el procedimiento de la residencia.
   - Hallazgo de `cuentas.estado` (apartado 3): si una cuenta de familia suspendida debe seguir recibiendo avisos.
2. **CJ:** confirmar que «solo un recordatorio» significa uno por cita y fecha (apartado 5); decidir la frase opcional «Si no vas a poder acudir, avísalo cuanto antes» (apartado 1, nota 5); que el recordatorio se omita cuando la cita se reservó o se movió ese mismo día (apartado 4); revisión del catalán (traductor externo).
3. **Orlando:** la franja horaria; encender «Siempre activo» en el despliegue; aprobar la reserva por procedimiento (apartado 7) y no por `INSERT`; si se prefiere un correo por familiar y día en lugar de uno por cita; no añadir casilla «no avisar» (como en el cambio); el nuevo script (el siguiente libre, aplicado en dev con la ventana habitual; en producción, la política del procedimiento no obliga a ventana de mantenimiento porque no toca la función de seguridad por filas).
4. **Residencia:** registrar bajas y suspensiones a primera hora; cómo avisa a la familia cuando el correo no llega; que Administración sabe que reciben dos correos si cambia la cita tras el recordatorio.

## A verificar o decidir

1. **[Decidir DPO]** Los puntos del apartado 9.1.
2. **[Decidir CJ]** «Un recordatorio por cita y fecha»; frase opcional de avisar si no se puede acudir; omitir lo reservado o movido ese mismo día; catalán.
3. **[Decidir Orlando]** Franja 10:00 a 19:00; procedimiento de reserva en lugar de `INSERT`; un correo por cita (propuesto) o por familiar y día; «Siempre activo» en el despliegue; sin casilla.
4. **[Decidir residencia]** Procedimiento de bajas y suspensiones; qué hace Administración tras un cambio posterior al recordatorio.
5. **A verificar (técnico):** el nombre de zona horaria de `AT TIME ZONE` para Madrid en Azure SQL (no lo ejecuté); que el cálculo de «mañana» en el procedimiento y en el código de la aplicación coincide en los dos domingos de cambio de hora; que `0089` reescrito conserva el comportamiento del aviso de propuesta y del de cambio; si la suspensión de una cuenta de familia revoca la autorización (hallazgo de `cuentas.estado`).
6. **A verificar:** autoridad de control (AEPD o APDCAT, art. 3 de la Llei 32/2010) según el tipo de centro piloto; Esquema Nacional de Seguridad si el centro es público o concertado (hereda `aviso-cita-cambio-cancelacion-por-correo.md`, punto 5 de «A verificar»).
7. **Hereda sin cambios** y sigue pendiente: contrato con Microsoft (ACS), B10, DMARC en cuarentena, titular de ResidApp en el pie, persona experta (Orlando, 1.6 C), entrega a Outlook (el volumen de recordatorios, uno por cita y día, aumenta el efecto de la reputación del dominio nuevo).
8. **Se puede construir ya, apagado y con datos de prueba:** plantilla en `es` y `ca` (marcada como borrador), script de esquema, procedimiento de reserva, ampliación del cierre de pendientes, segunda tarea o ampliación de la actual, interruptor propio y las pruebas R1 a R13 con el `IEmailSender` falso. **No se enciende con direcciones reales** hasta tener validados el apartado 6 y los puntos heredados.

Fuentes consultadas el 2026-11-14: repositorio (archivos y líneas citados); `normativa-local/lssi.txt` (BOE, actualizado el 2025-12-18; art. 21.1 y anexo, letra f) y `normativa-local/rgpd.txt` (art. 21.4; texto consolidado, no es la versión oficial); `normativa-local/INDICE.md` (comprobación más antigua 2026-11-12). Los artículos 4.15, 5.1.d, 6.1, 6.4, 13 y 28.3 del RGPD se citan por su materia, sin reproducir su redacción literal.
