# Procedimiento de derechos de las personas de ResidApp

**Escrito para Orlando (persona de contacto de privacidad de ResidApp) y CJ (suplente): para usarlo cuando llegue una petición, sin ser jurista.**

**BORRADOR del agente `dpo-residapp`, 2026-11-14. Lo valida una persona experta (abogado o DPO) antes de usarlo con datos reales.** No es un dictamen ni sustituye a un delegado de protección de datos. Hoy no hay sociedad titular ni persona experta contratada ni contrato firmado con ningún centro ([`guia-cumplimiento-propietarios.md`](guia-cumplimiento-propietarios.md), apartado 9), así que **hoy no puede llegar una petición real del caso A**; este documento es para tenerlo listo. Desarrolla el apartado 6.1 y la fila 11 de la tabla de documentos de esa guía.

Las citas legales salen de `normativa-local/` (índice del 2026-11-14): RGPD consolidado a 2016-05-04; LOPDGDD, BOE act. 2026-10-08; Ley 41/2002, BOE act. 2025-12-18; Llei 21/2000, BOE act. 2026-02-16 (en castellano; el original catalán está en el DOGC); Ley 8/2021, BOE act. 2025-12-20; Llei 32/2010, BOE act. 2026-07-13; directrices 07/2020 del CEPD (v2.1, PDF); directrices de la AEPD para contratos de encargado (2018, PDF). Son textos consolidados: sirven para consultar, no son la versión oficial. **Lo que no está en esa base lo marco «no verificado» y no lo doy por cierto.**

**Decisiones y condiciones que este documento aplica (no las resuelve):**

- **No se anonimiza ni se borra a familiares reales** con la función del script `0087` hasta que una persona experta decida por escrito entre **borrar** y **bloquear** (art. 32 LOPDGDD). Orlando, 2026-11-03 (opción A «sin copia») y su condición posterior, recogida en `antes-de-datos-reales.md` (apartado 8) y en la guía (apartado 6.1). Aquí es una **condición**: apartado 2.3.
- **CJ, de suplente**, en brechas (Orlando, 2026-11-13): solo avisa al centro, anota y registra; no contiene. Para derechos **propongo lo mismo** (apartado 7); no está decidido.
- **Sin datos reales hasta tener cobertura definida** para noches y vacaciones (Orlando, 2026-11-13, protocolo de brechas, apartado 5.2). Afecta también a este procedimiento: el reenvío al centro lo hace alguien.
- **La app no regula la organización interna del centro** (Orlando, 2026-11-10): aquí solo se dice qué hace ResidApp y qué debe pedir al centro por contrato.

Palabras que se usan:

- **Derecho (de las personas):** lo que una persona puede pedir sobre sus datos: ver qué datos hay y recibir copia (**acceso**), corregirlos (**rectificación**), que se borren (**supresión**), que se congele su uso mientras se aclara algo (**limitación**), recibirlos en un fichero para llevarlos a otro sitio (**portabilidad**), oponerse a que se usen (**oposición**) y no ser objeto de una decisión tomada solo por una máquina (**decisiones automatizadas**).
- **Solicitante:** la persona que lo pide, o quien la representa.
- **Responsable / encargada:** el responsable decide para qué se usan los datos y es quien debe contestar; la encargada los trata por su cuenta y le ayuda. En los datos de las residencias, **la residencia es la responsable y ResidApp la encargada**.
- **Bloquear:** guardar el dato apartado, sin que nadie lo vea ni lo use, solo para ponerlo a disposición de un juez o una autoridad durante el plazo en que pueden pedirse responsabilidades (LOPDGDD art. 32). **Anonimizar a un familiar** es el nombre del botón de ResidApp que borra su nombre, teléfono y correo y cierra su acceso; **no es anonimato total** (`expurgo-y-supresion.md`, apartados 3.8 y 3.9).
- **Autoridad de control:** la AEPD o, en el ámbito catalán, la APDCAT.

---

## 0. Si te llega una petición ahora

1. **Anota el día y la hora** en que te llegó y por qué canal (correo, teléfono, carta, en persona, a través de un centro). Ese día cuenta: **el reloj de un mes ya está en marcha** (apartado 3).
2. **No contestes de fondo.** No digas si esa persona está o no en un centro, ni qué datos hay, ni qué se va a hacer. No mandes ningún dato, y menos clínico, por correo, chat o WhatsApp.
3. **No pidas más datos de los necesarios.** No pidas DNI ni documentos: comprobar quién es la persona lo hace el centro (RGPD art. 12.6). Si te lo mandaron adjunto, no lo copies a ningún sitio más (apartado 4, paso 3).
4. **¿De quién son los datos?** Si son de residentes, familias o personal **de un centro**, es el **caso A**: **reenvía la petición al centro hoy** con la plantilla 6.2, manda el acuse de la plantilla 6.1 y avisa a Orlando o a CJ. Si son datos de ResidApp como empresa (vuestros contactos, cuentas de Plataforma), es el **caso B**: contesta ResidApp, dentro del mes.
5. **Si la petición viene de un juzgado, la policía o una autoridad** (también la AEPD o la APDCAT): **no entregues nada**. Avisa a la persona experta y al centro afectado **el mismo día** (apartado 4.7).
6. **Rellena el registro de peticiones** (apartado 6.5) ahora, no al final. **Sin el nombre de la persona.**
7. **Si no sabes qué hacer:** no borres ni cambies nada en ResidApp. Reenviar, anotar y esperar es siempre mejor que borrar.

---

## 1. Quién es responsable de qué

### 1.1 Dos casos

| | **Caso A: datos de un centro, que ResidApp trata POR ENCARGO** | **Caso B: datos que ResidApp trata COMO RESPONSABLE** |
|---|---|---|
| Qué datos | Residentes, familias y profesionales **de un centro**; su base; sus registros técnicos con IP | Cuentas del personal de Plataforma y sus aparatos autorizados; contactos que lleguen por las landings y buzones; datos de quienes trabajan en el proyecto en GitHub; registros de seguridad con IP de operadores (`guia-cumplimiento-propietarios.md`, apartado 2.3) |
| Quién contesta a la persona | **El centro.** El RGPD (art. 12) obliga al responsable; la prueba de haber respondido «recaerá sobre el responsable» (LOPDGDD art. 12.4) | **ResidApp** |
| Qué hace ResidApp | **Ayuda** al centro «a través de medidas técnicas y organizativas apropiadas, siempre que sea posible» (RGPD art. 28.3.e). **No contesta de fondo** salvo que el contrato lo prevea: el encargado «podrá tramitar, por cuenta del responsable» las solicitudes «si así se estableciere en el contrato» (LOPDGDD art. 12.3) | Atiende y responde ella, con los mismos derechos y plazos |
| Quién decide si procede | **El centro.** Según el CEPD, valorar si las peticiones son admisibles lo hace el responsable, y los plazos del capítulo III **no se amplían porque el encargado deba aportar información** (directrices 07/2020, v2.1, apdos. 130 a 132, p. 38 del PDF; resumen mío de un texto en inglés) | ResidApp (Orlando; persona experta si hay duda) |
| Autoridad | La del **centro** (apartado 1.3) | La AEPD, salvo que la persona experta diga otra cosa (`protocolo-brechas-borrador.md`, apartado 2.2). A verificar cuando exista la sociedad |

**Regla práctica:** si los datos los cargó o los usa un centro, es el **caso A**. Si son datos de ResidApp como empresa, es el **caso B**. Si la petición toca los dos tipos, se aplican **los dos** procedimientos.

**ResidApp no se pone en contacto por su cuenta con residentes ni familias.** La LOPDGDD (art. 33.2) dice que «tendrá la consideración de responsable del tratamiento y no la de encargado quien en su propio nombre y sin que conste que actúa por cuenta de otro, establezca relaciones con los afectados». Por eso la respuesta de fondo la da siempre el centro, y los escritos de ResidApp dicen que actúa **por cuenta del centro**.

**Qué debe elegir el contrato.** Las directrices de la AEPD para contratos de encargado (PDF, págs. 10 y 11) piden que el contrato diga si al encargado le corresponde **atender y responder** las solicitudes o si su **única obligación es comunicar** al responsable que se ha ejercido un derecho, y en ambos casos «la forma y el plazo». **Propuesta:** la segunda (comunicar + ayudar técnicamente), porque Plataforma no ve residentes ni contenido clínico (`antes-de-datos-reales.md`, apartados 8 y 9). Lo decide la persona experta con Orlando.

### 1.2 Si una persona escribe a ResidApp en vez de al centro

Es normal: la política de privacidad dice «puedes escribir a ResidApp (apartado 1), que trasladará tu petición al centro» (`politica-privacidad-portal.md`, apartado 12; `Privacy.cshtml`, apartado 12, **marcado PENDIENTE** porque falta el contrato). La ley lo permite: «el ejercicio del derecho no podrá ser denegado por el solo motivo de optar el afectado por otro medio» (LOPDGDD art. 12.2).

Qué hace ResidApp (**propuesta; decide Orlando**; nada de esto está en un contrato todavía):

1. **Reenvía al centro el mismo día hábil**, y como máximo el siguiente (la guía, apartado 6.1, dice «el mismo día laborable»; plazo propio a fijar en el contrato). Cada día que ResidApp se retrasa lo pierde el centro, que es quien responde (CEPD, arriba).
2. **Acusa recibo al solicitante** con un texto que **no confirme ni desmienta** que esa persona o su familiar esté en un centro (plantilla 6.1): decirlo ya sería dar un dato.
3. **No contesta sobre el fondo**, no comprueba identidad, no decide si procede y no promete plazos de borrado.
4. Si no se sabe a qué centro va: pide **solo el nombre del centro**, sin más datos. Si no lo sabe, lo anota y avisa a la persona experta; no adivina.
5. **Anota** (apartado 6.5) y **vigila** que el centro acuse recibo.

**Buzón de entrada:** hoy **no existe** un buzón único de privacidad para `residapp.es` y `residapp.cat` (`guia-cumplimiento-propietarios.md`, apartado 6.1, «pendiente»; `landings-y-passkeys-por-subdominio.md`, línea 32). Sin él no hay a dónde dirigir la gente.

### 1.3 Autoridad en el caso A (la del centro)

- **AEPD** con carácter general. **APDCAT** si el centro entra en el art. 3 de la Llei 32/2010: instituciones públicas, Generalitat, entes locales y entidades vinculadas (letras a a d); entidades privadas controladas por esos entes por capital, ingresos o dirección (letra e); y «las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios» (letra f).
- **Hoy no se sabe qué tipo de centro es el piloto** (público, concertado o privado). Una residencia concertada podría entrar por la letra f: **lo decide la persona experta**. Cada centro lo fija en su ficha de contactos. Debe constar en las respuestas a las personas (RGPD art. 12.4: posibilidad de reclamar ante la autoridad).

---

## 2. Los derechos, uno a uno

### 2.1 Cuadro

Etiquetas: **[Hecho]** comprobado en el repositorio (archivo y línea); **[Propuesta]** mía; **[Decidir]** lo fija una persona concreta.

| Derecho | Qué dice la norma | Qué hace hoy ResidApp (técnicamente) | Qué no existe o no se puede |
|---|---|---|---|
| **Acceso** (RGPD art. 15; LOPDGDD art. 13) | Confirmar si se tratan datos y dar acceso y **copia** «en un formato electrónico de uso común» (art. 15.1 y 15.3). La copia «no afectará negativamente a los derechos y libertades de otros» (15.4) | **[Hecho]** Lectura **en pantalla** para el personal con permiso: «Ver historial» y «Línea temporal», solo lectura (`Manual.cshtml`, 1580-1612); informe de derivación firmado en PDF (1558-1559); ficha del familiar con sus datos y si el correo está confirmado (477-480); «Trazabilidad clínica» de Dirección Clínica: quién hizo qué sobre un residente (976); «Mi cuenta» de cada cuenta: sesiones con IP y últimos accesos (188, 195) | **[Hecho]** **No hay exportación ni copia de la historia de un residente**: la exportación solo cubre estructura y censo de residentes activos, sin contenido clínico (`Exportar.cshtml`, 16-19; `anexo-modulos-contrato-borrador.md`, 38 y 313). No hay pantalla para que un residente o una familia vean todos sus datos. **No está montado** el «sistema de acceso remoto» que el art. 13.2 LOPDGDD da por atendido el derecho. **[A verificar]** si alguien puede leer lo clínico de un residente dado de baja (`expurgo-y-supresion.md`, línea 39) |
| **Rectificación** (art. 16; LOPDGDD art. 14) | Corregir datos inexactos y completar los incompletos. La persona indica qué dato y qué corrección y, si hace falta, aporta documentación (LOPDGDD 14) | **[Hecho]** «Corregir identidad» (nombre, fecha de nacimiento, sexo): motivo obligatorio, la versión anterior queda en el historial de la ficha (`Manual.cshtml`, 419-422). «Editar» al familiar (nombre, teléfono, correo): queda en la auditoría **sin los valores** (477-480; `direccion-confirmada-familia.md`, 26). Valoraciones y basal: **nueva versión**, no se pisa lo anterior (1606, 1627) | **[Hecho]** Lo ya escrito en la historia **no se modifica**: «nada del historial ni de la línea temporal se puede modificar» (`Manual.cshtml`, 1627). La Llei 21/2000, art. 9.3, exige que «quedan registrados todos los cambios e identificados los médicos y los profesionales asistenciales que los han realizado». Rectificar un dato clínico es **una anotación nueva del profesional**, no un borrado |
| **Supresión** (art. 17; LOPDGDD art. 15) | Borrar sin dilación en los casos del art. 17.1, **salvo** si el tratamiento es necesario «para el cumplimiento de una obligación legal» o «para la formulación, el ejercicio o la defensa de reclamaciones» (17.3.b y e). Al suprimir o rectificar hay que **bloquear** (LOPDGDD art. 32) | **[Hecho]** Familia: «Anonimizar a este familiar…» (solo Administración principal; **no se puede deshacer**; `Manual.cshtml`, 499-507; `FamilyAnonymization.cs`, 16-18). Foto: «Quitar la foto» y «No quiere foto» (408-412). Dirección de correo: borrarla desde «Editar» (477). Personal: **sin función**, solo procedimiento escrito (`expurgo-y-supresion.md`, apartado 4) | Ver 2.2 a 2.5. **Hoy no se usa con familias reales** (2.3). No se borra: historia clínica dentro de plazo, auditoría, accesos con IP, sesiones, citas y comunicados |
| **Limitación** (art. 18; LOPDGDD art. 16) | Congelar el uso mientras se verifica una exactitud, una oposición o hasta que el interesado reclame, etc. El dato limitado «solo podrá ser objeto de tratamiento, con excepción de su conservación» con consentimiento o para reclamaciones (18.2). **Debe constar claramente en los sistemas** (LOPDGDD 16.2) | **[Hecho]** **No existe ninguna marca ni función de limitación** (búsqueda en `src/`: solo aparece en el texto de `Privacy.cshtml`). Lo más cercano es cortar acceso: suspender o revocar la autorización de una familia (`Manual.cshtml`, 510-514) o suspender la cuenta del personal (636-639) | **[Hecho]** No se puede dejar un dato «limitado» dentro de la ficha. **[Decidir]** persona experta: qué hace el centro si se la piden (apartado 9) |
| **Portabilidad** (art. 20; LOPDGDD art. 17) | Recibir en formato estructurado los datos que **la persona facilitó**, solo si la base es **consentimiento o contrato** y el tratamiento es automatizado (20.1). No aplica a una misión de interés público (20.3) | **[Hecho]** Solo el CSV administrativo del censo y la estructura (`Exportar.cshtml`). **[Suposición]** Casi nada de lo que trata ResidApp se apoya en consentimiento: según la propuesta de base jurídica solo la foto (`politica-privacidad-portal.md`, B5, fila «Foto», 392); el resto, contrato u obligación legal, a elegir por el centro | No hay exportación por persona. **[Decidir]** persona experta y centro: a qué datos aplica |
| **Oposición** (art. 21; LOPDGDD art. 18) | Solo contra tratamientos basados en misión pública o interés legítimo (6.1.e o f); el responsable deja de tratar salvo «motivos legítimos imperiosos» o defensa de reclamaciones (21.1). Contra la mercadotecnia directa, siempre (21.2 y 21.3) | **[Hecho]** Para «no quiero más avisos por correo»: **borrar la dirección**, no solo quitar la confirmación (`Manual.cshtml`, 477). El interruptor propio de la familia en el portal (pregunta 3.2 de CJ) **no existe** (`direccion-confirmada-familia.md`, 64 y 174) | **[Hecho]** No hay forma de apartar a una persona de otros tratamientos (auditoría, accesos). **No consta** una exclusión por residente del dictado por voz **[A verificar]** |
| **No ser objeto de decisiones automatizadas** (art. 22) | No ser objeto de una decisión «basada únicamente en el tratamiento automatizado» con efectos jurídicos o similares (22.1) | **[Hecho en el ADR]** La IA solo da **sugerencias** que se validan «en servidor como cualquier entrada» y revisa el profesional (`0011-arquitectura-ia.md`, 35); «la plataforma no diagnostica ni decide prioridades clínicas» (68-71); «el alcance excluye automatizar decisiones» (91) | Respuesta tipo: «no hay decisiones automatizadas». **[Hecho, comprobado en el código el 2026-11-14]** El único uso de IA en el código es el dictado de «Registrar cambio»: sugiere áreas y opciones, «la persona la revisa y la confirma antes de registrar nada», lo devuelto se valida contra un catálogo cerrado y ni el audio ni el texto se guardan (`SuggestDailyChangeFromVoice.cs`, 11 a 26 y 89). **[A verificar]** que no haya otro tratamiento automatizado con efectos sobre una persona (p. ej. avisos o alertas) antes de afirmarlo por escrito: se ha revisado el dictado, no todo el código |
| **Retirar el consentimiento** (art. 7.3) | «Será tan fácil retirar el consentimiento como darlo» | **[Hecho]** La foto: quitarla y anotar «No quiere foto»; al dar de baja se borra (`Manual.cshtml`, 408-410) | Solo la foto consta con base de consentimiento (propuesta B5, no decidida) |

**Gratuito:** atender un derecho es gratuito; solo se puede cobrar un canon razonable o negarse si la solicitud es «manifiestamente infundada o excesiva» y lo demuestra el responsable (RGPD art. 12.5). Exigir un canon fuera de esos casos es infracción **muy grave** (LOPDGDD art. 72.1.j); cobrar uno cuando sí cabe, pero por encima de los costes, es infracción **leve** (art. 74.b). **Repetitivo:** pedir el acceso más de una vez en seis meses, salvo causa legítima (LOPDGDD art. 13.3). Lo aprecia el centro, no ResidApp.

**Por qué importa no retrasarse:** impedir, obstaculizar o no atender reiteradamente un derecho es infracción **muy grave** (LOPDGDD art. 72.1.k) y no atender una solicitud es **leve** (art. 74.c). Es del centro; ResidApp, si se retrasa en reenviar, le hace perder días.

### 2.2 Historia clínica: límites de los derechos (Ley 41/2002, Llei 21/2000)

Todo esto es **del centro**. ResidApp solo debe saber que existe, para no prometer lo que la ley no deja. La LOPDGDD dice que cuando una ley especial establece un régimen especial para los derechos «se estará a lo dispuesto en aquellas» (art. 12.5).

- **Conservación (frente a la supresión).** Llei 21/2000, art. 12.4: debe conservarse «como mínimo durante quince años desde la fecha de alta de cada proceso asistencial» una lista de documentos (consentimientos informados, informes de alta, de exploraciones complementarias, etc.); art. 12.6: el resto «puede destruirse» a los cinco años; art. 12.7: lo relevante para la asistencia, la investigación o las reclamaciones se conserva según los criterios de la comisión técnica y «a efectos judiciales». Ley 41/2002, art. 17.1: mínimo «cinco años». **ResidApp propone 15 años desde la baja por defecto y no borra nada sola** (`expurgo-y-supresion.md`, apartado 1; `Manual.cshtml`, 434). Por eso una petición de supresión de la historia **se deniega con motivos** (RGPD art. 17.3.b y e) mientras dure el plazo; el centro contesta por escrito, explica el motivo y dice que puede reclamar (RGPD art. 12.4). **[A verificar, abierto]** si una residencia cuenta como «centro sanitario» a efectos de esos artículos (`expurgo-y-supresion.md`, línea 28).
- **Quién lo decide al terminar:** «la dirección médica del centro sanitario» (Llei 21/2000, art. 12.8); no está construido el expurgo.
- **Acceso del paciente.** Tiene derecho a acceder y a copia, «con las reservas» del apartado siguiente, y **corresponde a los centros regular el procedimiento** (Ley 41/2002, art. 18.1; Llei 21/2000, art. 13.1).
- **Reservas.** El acceso no puede ir en perjuicio «del derecho de terceros a la confidencialidad de los datos de los mismos» ni del derecho de los profesionales a **reservar sus «observaciones, apreciaciones o anotaciones subjetivas»** (Llei 21/2000, art. 13.2; Ley 41/2002, art. 18.3). **[Hecho]** ResidApp **no tiene** campo ni marca de anotación subjetiva (solo aparece en documentos de privacidad): separar eso antes de entregar una copia es trabajo humano del centro.
- **Por representación.** «Puede ejercerse también por representación, siempre que esté debidamente acreditada» (Llei 21/2000, art. 13.3; Ley 41/2002, art. 18.2). La acreditación la comprueba el centro. Ver apartado 5.1 (Ley 8/2021).
- **Accesos de otras personas.** Judiciales, epidemiológicos, de salud pública, de investigación o docencia: «se rige por lo dispuesto en la legislación vigente en materia de protección de datos personales» (Ley 41/2002, art. 16.3; **no verificado** qué desarrollo catalán lo regula). El personal de administración solo accede a lo relacionado con sus funciones (Ley 41/2002, art. 16.4; Llei 21/2000, art. 11.4).
- **Quién accedió.** Si un residente o su representante pregunta «quién ha visto mi historia»: hay un registro de accesos, pero **si se entrega y con qué detalle es una pregunta abierta** para la persona experta (nombre del profesional, texto libre del motivo que puede nombrar a terceros; `historial-tras-traslado-05-conservacion-motivo-y-purga.md`, 54-56). Hasta que se decida, **no se entrega**; se anota. Que haya un registro de accesos no es opcional: la Ley 41/2002 (art. 16.7) manda que las comunidades autónomas regulen el procedimiento para que quede constancia del acceso a la historia clínica y de su uso; la norma catalana que lo desarrolla **no está en la base** y no se ha verificado.

### 2.3 Familiares: la condición del `0087`

**Hecho:** la función existe en la aplicación (`AdministracionController.Anonimizar.cs`; `FamilyAnonymization.cs`; script `0087_anonimizar_familiar.sql`) y su cabecera dice que no se usa con familias reales hasta decisión escrita (líneas 13-15). **No hay un interruptor técnico** que lo impida (comprobado el 2026-11-14: ni `AdministracionController.Anonimizar.cs` ni `AdministracionApplicationService.AnonymizeFamilyMemberAsync` consultan ninguna opción; la vista solo exige la casilla de confirmación y rechaza al contacto urgente o tutor legal de un residente que sigue en el centro): solo hay un aviso escrito (`Manual.cshtml`, 506-507) y la cabecera de `0087` (líneas 13 a 15). Cualquier Administración principal puede pulsar el botón con una familia real.

**Condición de este procedimiento:** si un familiar de un centro **real** pide que se borren sus datos, **no se usa «Anonimizar a este familiar…»** hasta que exista la decisión escrita de la persona experta entre:

- **(A) borrar sin copia**, que exige que la persona experta diga por escrito que los datos de contacto de un familiar no generan responsabilidades que obliguen a bloquear, o una excepción de la autoridad (LOPDGDD art. 32.5); o
- **(B) borrar con bloqueo**: copia apartada, sin lectura para nadie, a disposición de un juez o una autoridad durante el plazo de prescripción (LOPDGDD art. 32.2 a 32.4) y destrucción después. Es lo que la ley dice si no hay excepción; hoy **no está construido** (`expurgo-y-supresion.md`, apartado 3.3, punto 1).

Mientras tanto, el centro (con ayuda de ResidApp si hace falta) puede hacer **lo reversible**: contestar dentro del plazo explicando que la decisión está pendiente; **cortar el acceso** al Portal Familiar (suspender o revocar la autorización: `Manual.cshtml`, 510-514) y **dejar de mandar avisos por correo borrando la dirección** (477). **[A verificar, pregunta para la persona experta]** El borrado de la dirección o del teléfono desde «Editar» **sobrescribe** el valor (la auditoría no guarda el anterior: `direccion-confirmada-familia.md`, 26): si es un caso de bloqueo (art. 32.1: «cuando proceda a su rectificación o supresión»), **la misma pregunta vale para cada rectificación desde «Editar»**. No lo resuelvo.

**Otras condiciones del mismo borrador** (`expurgo-y-supresion.md`, 3.3): una cuenta compartida entre centros no se toca desde un solo centro; no se anonimiza a quien sea contacto urgente único o tutor legal de un residente que sigue en el centro (la propia pantalla lo impide, `Manual.cshtml`, 505); no hay que prometer anonimato total (el identificador de acceso, el vínculo con el residente y los textos escritos por el personal siguen).

### 2.4 Personal (profesionales)

El personal es del centro (caso A) salvo el de Plataforma (caso B). **No hay función de anonimización**; lo que hay es el procedimiento escrito de `expurgo-y-supresion.md`, apartado 4, que **no está ejecutado nunca** (el script SQL es un borrador sin probar):

1. La Administración del centro **suspende la cuenta** y **revoca los perfiles** (`Manual.cshtml`, 636-639) y anula la invitación pendiente.
2. ResidApp, **con instrucción escrita del centro y dos personas**, borra passkeys y credenciales de esa cuenta (4.3 de ese documento).
3. **No se tocan** el nombre, el identificador de acceso, las sesiones, los accesos con IP ni la auditoría: aparecen en informes firmados y en registros que no se pueden borrar (`expurgo-y-supresion.md`, apartados 4 y 4.4).

**Lo que hay que decir a la persona** (4.4 de ese documento): su nombre e identificador seguirán en los informes de derivación que firmó y en los registros de actividad mientras dure la documentación clínica; su IP queda en los registros de seguridad hasta que se fije un plazo; quién puede verlos es el personal con permiso de auditoría.

### 2.5 Lo que hoy no se puede borrar y no tiene plazo

`eventos_auditoria`, `eventos_acceso` (con IP) y `sesiones` son **inmutables y sin plazo decidido** (`expurgo-y-supresion.md`, apartado 2.8; `politica-privacidad-portal.md`, 174-176). También los textos libres que escribió el personal en filas inmutables (llamadas, comunicados, comentarios de cita de la familia; `expurgo-y-supresion.md`, 3.3 punto 3). **Sin plazo escrito no se cumple el art. 5.1.e del RGPD**: es una puerta abierta, no una respuesta. Si la persona pide su borrado, la respuesta honesta es: «se conserva por seguridad y por obligación legal; el plazo está por fijar», y **se anota** para la persona experta. También quedan en copias de seguridad hasta que caduquen y en los registros del servicio de correo (`expurgo-y-supresion.md`, 2.9).

---

## 3. Plazos

| Hito | Plazo | De dónde sale |
|---|---|---|
| **Respuesta del centro a la persona** | «Sin dilación indebida y, en cualquier caso, en el plazo de **un mes** a partir de la recepción de la solicitud» | RGPD art. 12.3 |
| **Prórroga** | **Otros dos meses** «en caso necesario, teniendo en cuenta la complejidad y el número de solicitudes». El centro **avisa dentro del primer mes** y dice los motivos | RGPD art. 12.3 |
| Si no se da curso | Explicar las razones y la posibilidad de **reclamar ante la autoridad y de ir a los tribunales**, «a más tardar» en un mes | RGPD art. 12.4 |
| **Cómo se cuenta el mes** | Un plazo en meses «concluirá el mismo día en que se produjo el hecho que determina su iniciación» en el mes de vencimiento; si ese mes no tiene ese día, el último del mes; si el último día es sábado, domingo o festivo, pasa al **primer día hábil siguiente**. Ejemplo (inventado): recibida el 10 de marzo, vence el 10 de abril; si es sábado, el lunes 12 | LOPDGDD, disposición adicional 3.ª, letras c y d |
| Si el centro tiene DPO | La persona puede dirigirse antes a él; el DPO comunica la decisión «en el plazo máximo de dos meses» | LOPDGDD art. 37.1 |
| **Reenvío de ResidApp al centro** (caso A) | **Propuesta:** el mismo día hábil, y como máximo el siguiente | Guía, 6.1 («el mismo día laborable»); **decide Orlando** |
| **Ayuda técnica de ResidApp al centro** | **Propuesta:** respuesta o fecha firme en **2 días hábiles** desde que el centro la pide; entrega en **5 días hábiles**. Deja al centro más de dos semanas de margen sobre el mes | Propuesta mía; **decide Orlando**; irá en el contrato |
| **Respuesta de ResidApp** (caso B) | Un mes, ampliable como arriba | RGPD art. 12.3 |
| Registro de peticiones | Se rellena **el día de la recepción**; se cierra con la respuesta | Apartado 6.5 |

**Desde cuándo corre el mes cuando la petición llegó a ResidApp.** El art. 12.3 dice «a partir de la recepción de la solicitud» por el responsable y no aclara si cuenta la recepción por su encargado. El CEPD dice que los plazos no se amplían porque la información deba venir del encargado. **Hipótesis de trabajo prudente:** el mes cuenta desde el día en que **llegó a ResidApp**, y el contrato lo dirá. **Lo confirma la persona experta** (apartado 9).

**Dos plazos distintos:** las **horas** de las brechas (24 h de ResidApp, 72 h del centro) corren seguidas; el **mes** de los derechos se cuenta en meses y se mueve al día hábil si acaba en día inhábil (disposición adicional 3.ª).

---

## 4. Los pasos, en orden

### 4.1 Recibir y anotar

- Anota el día, la hora y el canal (apartado 6.5). Quien recibe una petición, **la anota**: el suplente también.
- **No copies la petición ni lo que cuente la persona al registro**: ahí solo van fechas, tipo de derecho y centro.
- Si te llega por teléfono o en persona: no hay obligación de que sea por escrito; apunta el motivo y pide que la reenvíe al centro (o, si es el caso B, que la mande por escrito). **No se puede denegar por el canal elegido** (LOPDGDD art. 12.2).

### 4.2 Clasificar

¿Quién pide? (residente, representante, familiar, profesional, otro.) ¿Qué derecho? ¿A qué centro afecta? ¿Es el **caso A o el caso B**? ¿Pide **datos de otra persona** (apartado 5)? ¿Es **urgente** (alguien pide parar ya un envío o un acceso)? Si dudas, trátalo como caso A y reenvía.

### 4.3 Identidad: la comprueba el centro, sin pedir de más

- «Cuando el responsable tenga dudas razonables en relación con la identidad ... podrá solicitar que se facilite la información adicional necesaria» (RGPD art. 12.6). Es una facultad **del responsable**.
- **ResidApp no pide documentos.** Si el solicitante los adjuntó, se **reenvían al centro por el canal acordado y se borran** de ResidApp **[Propuesta]**. Minimizar es un principio del RGPD (art. 5.1.c; no releído hoy).
- En el **caso B**, Orlando valida la identidad con **lo mínimo**: por ejemplo, que la petición venga del mismo correo con el que se dio de alta o escribió, o una pregunta que solo esa persona pueda responder. **[Decidir]** con la persona experta qué se acepta.
- Si la persona pide **por un tercero** (un familiar por un residente), ver apartado 5.

### 4.4 Reenviar, acusar recibo y esperar al centro (caso A)

1. Reenvía con la plantilla 6.2 al **contacto de privacidad del centro y a su suplente** (la lista real vive fuera del repositorio; ver `protocolo-brechas-borrador.md`, apartado 5.3). **Sin datos clínicos ni más datos de los que la persona ya puso en su mensaje.**
2. Acusa recibo al solicitante con la plantilla 6.1.
3. Pide al centro **acuse de recibo** y anótalo. Sin acuse, insiste por el otro canal.
4. El centro decide si procede, qué entrega y qué deniega. **ResidApp no decide.**

### 4.5 Ayudar técnicamente (caso A)

Solo cuando el centro lo pide, **por escrito** (RGPD art. 28.3.a: «instrucciones documentadas del responsable»). Qué puede hacer ResidApp hoy y qué no:

| El centro pide | Qué se puede hacer hoy | Ojo |
|---|---|---|
| Una copia de lo que consta de una persona | **Que el propio centro lo consulte** desde sus pantallas (2.1). ResidApp **no extrae** historias clínicas: Plataforma **no ve residentes ni lo clínico** (`antes-de-datos-reales.md`, 136-138) y **no lee** la auditoría de otros centros (`0088_rls_eventos_auditoria.sql`, 5-6) | **No existe una exportación de la historia.** Si hace falta antes de tener la función, lo decide la persona experta; no se «arregla» dándole a Plataforma acceso clínico |
| Corregir un dato de contacto o de identidad | Administración del centro, desde las pantallas (2.1) | El cambio vale para **todos los residentes** vinculados a ese familiar (`Manual.cshtml`, 488) |
| Borrar a un familiar | **Hoy no** (2.3) | Condición escrita |
| Cerrar una cuenta del personal | Suspensión y revocación: el centro. Borrado de passkeys y credenciales: ResidApp, con instrucción, dos personas y script revisado (`expurgo-y-supresion.md`, 4.3) | Nada de SQL a mano ni pegar claves en el chat |
| Registros de acceso del personal o de la cuenta | El centro ve la auditoría administrativa de su ámbito, **sin filtro por quien hizo la acción** (`Manual.cshtml`, 774-785). Extraer «todo lo de una persona» exige una consulta técnica que **hoy no existe como procedimiento** | Ver 5.2 |
| Dejar de mandar correos a una dirección | Borrar la dirección (`Manual.cshtml`, 477) | Quitar la confirmación no basta |

**Antes de ejecutar nada irreversible:** dos personas de ResidApp, instrucción escrita del centro, anotar fuera de la base (solo identificadores) para **reaplicar tras restaurar una copia** (`expurgo-y-supresion.md`, 2.9 y 4.3). Orlando es hoy la única persona con acceso técnico completo (`protocolo-brechas-borrador.md`, 5.1); la segunda persona **no existe** hasta que se decida (guía, apartado 7.2).

### 4.6 Entregar sin enviar datos sensibles por canales inseguros

1. **Quien entrega a la persona es el centro**, no ResidApp. ResidApp entrega **al centro**, por el canal acordado, lo que este pidió.
2. **Nunca** datos de salud por correo normal sin cifrar, mensajería instantánea, ni a una dirección que no sea del centro o del propio solicitante comprobado. No se pegan datos en el chat de trabajo, en el repositorio (es público) ni en capturas.
3. **Propuesta:** fichero cifrado con una clave que viaja por **otro canal**, o entrega en mano o en la sede del centro. **Hoy ResidApp no tiene un canal seguro de entrega** (`[CANAL ACORDADO]`: decide Orlando con la persona experta; **A verificar** qué ofrece el servicio de correo y de almacenamiento ya contratado). El centro entregará al solicitante en el formato que pida, «en un formato electrónico de uso común» si lo pidió por medios electrónicos (RGPD art. 15.3 y 12.3).
4. Si la persona pide que se le conteste por un medio electrónico, el centro responde por medio electrónico «cuando sea posible» (RGPD art. 12.3); si no hay canal seguro, se le ofrece recogerlo en mano.
5. Comprueba que lo entregado **no incluye datos de otras personas** (RGPD art. 15.4; reservas del apartado 2.2).

### 4.7 Si interviene una autoridad, un juzgado o la policía

- **Reclamación ante la AEPD o la APDCAT** (RGPD art. 77: «derecho a presentar una reclamación ante una autoridad de control»). Es **contra el responsable**, el centro. ResidApp **coopera si la autoridad lo pide** (RGPD art. 31) y aporta al centro «toda la información necesaria para demostrar el cumplimiento» (art. 28.3.h). **Qué hacer en cuanto llegue algo a ResidApp** (guía, apartado 6.2): avisar a la persona experta y al centro **el mismo día**, no contestar de fondo sin consultar y guardar contratos, registro de actividades y pruebas de lo hecho. La autoridad puede remitir la reclamación al DPO del centro, que responde en un mes (LOPDGDD art. 37.2).
- **Un juzgado o el Ministerio Fiscal.** El encargado solo trata datos con instrucciones del responsable, salvo que una norma le obligue; en ese caso informa al responsable de esa exigencia «salvo que tal Derecho lo prohíba» (RGPD art. 28.3.a). ResidApp **no entrega datos de un centro por su cuenta**: avisa a la persona experta y al centro el mismo día; la entrega, si procede, la decide la persona experta y se hace al amparo de lo que el centro, que custodia la historia, deba aportar. Los datos **bloqueados** sirven precisamente para esto (LOPDGDD art. 32.2). Plataforma, además, no ve contenido clínico.
- **La policía u otra autoridad administrativa:** **no entregues nada** sin que la persona experta lo valide. **No verificado** en la base qué norma regula esas peticiones ni qué exige una orden judicial en este caso.
- **Anota** la petición (apartado 6.5) sin copiar su contenido.

### 4.8 Cerrar y anotar

Se cierra cuando: el centro ha acusado recibo del reenvío; el centro ha contestado a la persona (o ha avisado de la prórroga dentro del mes); ResidApp ha entregado lo que le pidió; y se ha anotado todo. **Si se suprimió o rectificó algo en ResidApp** por instrucción del centro, anota **fuera de la base** (solo identificadores y fechas) para reaplicarlo tras una restauración de copia, y comprueba si hay que **avisar a los destinatarios** de la rectificación o supresión «salvo que sea imposible o exija un esfuerzo desproporcionado» (RGPD art. 19; sobre si un encargado cuenta como destinatario, `expurgo-y-supresion.md`, 2.9). Después, pregúntate: ¿hay que cambiar este procedimiento, el contrato, la política de privacidad o el registro de actividades?

---

## 5. Peticiones de terceros

### 5.1 Familiares que piden información o datos de un residente

**No es lo mismo pedir «información de cómo está» que ejercer el derecho de acceso a la historia.**

- **Información asistencial.** «El titular del derecho a la información es el paciente. También serán informadas las personas vinculadas a él, por razones familiares o de hecho, en la medida que el paciente lo permita de manera expresa o tácita» (Ley 41/2002, art. 5.1; Llei 21/2000, art. 3.1). Si el paciente carece de capacidad, a juicio del médico, la información se pone «en conocimiento de las personas vinculadas» (Ley 41/2002, art. 5.3; Llei 21/2000, art. 3.3). **Lo decide el centro (el médico responsable), no ResidApp.** En ResidApp, lo que una familia ve lo fija la **autorización** que da la Administración del centro: Activa y sin caducar (`Manual.cshtml`, 1731-1737), y solo ve las novedades publicadas (1744-1759). ResidApp **no amplía** lo que una familia puede ver porque lo pida por correo.
- **Acceso a la historia clínica de un paciente vivo.** Solo el paciente o su **representante debidamente acreditado** (Ley 41/2002, art. 18.2; Llei 21/2000, art. 13.3). Un familiar sin representación acreditada **no tiene** ese derecho por ser familiar. **[Decidir]** persona experta: si la Llei 21/2000, art. 3.3, y la Ley 41/2002, art. 9.3.a, amplían algo para pacientes sin capacidad y sin representante.
- **Quién es el representante.** La **Ley 8/2021** reformó el sistema: las medidas de apoyo (voluntarias, guarda de hecho, curatela, defensor judicial; Código Civil, arts. 249 y 250, en la redacción que da la Ley 8/2021, apartado «Veintitrés» de su artículo segundo, «Modificación del Código Civil») deben atender a «la voluntad, deseos y preferencias» de la persona, y solo «en casos excepcionales» pueden incluir «funciones representativas» (art. 249). **No podrán ejercer medidas de apoyo «quienes, en virtud de una relación contractual, presten servicios asistenciales, residenciales o de naturaleza análoga»** (art. 250): ni la residencia ni su personal pueden ser quien decida por el residente. **En la práctica:** una residencia con capacidad disminuida no justifica por sí sola «que decida su familia»; el centro pide la **resolución o el documento** que acredita el apoyo y su alcance. ResidApp **no comprueba** esa acreditación: en la ficha existe la figura de «tutor legal» (`Manual.cshtml`, 461-465 y 505), pero **no consta** que se guarde la resolución ni que se verifique **[A verificar]**. **Aviso de vocabulario:** la propia exposición de motivos de la Ley 8/2021 dice que «se eliminan del ámbito de la discapacidad no sólo la tutela, sino también la patria potestad prorrogada y la patria potestad rehabilitada» (la tutela queda para menores); para un adulto, la figura de apoyo con representación es la curatela. La etiqueta «Tutor legal» de la app puede no corresponderse con ninguna figura legal de un residente adulto: se anota para la persona experta (apartado 9) y **no se cambia** aquí.
- **Fallecidos.** LOPDGDD art. 3.1: las personas «vinculadas al fallecido por razones familiares o de hecho así como sus herederos» pueden dirigirse al responsable o encargado «al objeto de solicitar el acceso a los datos personales de aquella y, en su caso, su rectificación o supresión», salvo que el fallecido lo hubiera prohibido o lo establezca una ley; también las personas que él haya designado (art. 3.2). Si el fallecido era **una persona con discapacidad**, esas facultades las pueden ejercer además «quienes hubiesen sido designados para el ejercicio de funciones de apoyo, si tales facultades se entendieran comprendidas en las medidas de apoyo prestadas por el designado» (art. 3.3, segundo párrafo): relevante en una residencia, y **nada de esto lo comprueba ResidApp**. **Pero para la historia clínica** la Ley 41/2002 (art. 18.4) permite el acceso solo «a las personas vinculadas a él, por razones familiares o de hecho, salvo que el fallecido lo hubiese prohibido expresamente y así se acredite», y **no** facilita «información que afecte a la intimidad del fallecido ni a las anotaciones subjetivas de los profesionales, ni que perjudique a terceros». Y la LOPDGDD (art. 12.5) remite al régimen especial. **[Decidir]** persona experta: cómo se concilian «herederos» (LOPDGDD) y «personas vinculadas» (Ley 41/2002) y si hay un equivalente catalán: **la Llei 21/2000 del texto de la base no tiene una disposición propia sobre fallecidos** (su art. 13 tiene tres apartados). **[Hecho]** Al dar de baja por fallecimiento, la baja corta el acceso de las familias al Portal (`Manual.cshtml`, 1738) y los datos se conservan; la lista de anonimización recoge «sus allegados o herederos (ha fallecido)» (`FamilyAnonymization.cs`, 12), pero **eso es para un familiar que pide borrar sus propios datos**, no para el residente fallecido.
- **Un familiar que pide borrar sus propios datos** (nombre, teléfono, correo): derecho propio, caso del apartado 2.3.

### 5.2 Personal que pide sus propios datos o los registros de acceso a ellos

El personal de un centro es del caso A: **el centro responde** (también como empleador; las normas laborales las revisa la asesoría laboral). El de Plataforma es caso B: responde ResidApp.

1. **Sus datos de cuenta, perfiles y sesiones:** los ve él en «Mi cuenta» (sesiones con IP, últimos accesos: `Manual.cshtml`, 188-196); la Administración del centro ve cuentas y perfiles de su ámbito.
2. **«¿Quién ha entrado en mi cuenta o ha mirado mis datos?»:** las entradas y fallos recientes de su cuenta, en «Mi cuenta». Más allá, está en `eventos_acceso` (con IP) y en la auditoría. Ni la auditoría administrativa del centro ni «Mi cuenta» permiten una **lista completa por persona** (`Manual.cshtml`, 780-781: no se filtra ni se cuenta por quien hizo la acción: «la auditoría sirve para revisar qué pasó, no para comparar a los trabajadores»).
3. **«Dame todo lo que hay de mí»** (acceso, art. 15): incluye IP y registros. Hoy **no hay procedimiento ni consulta preparada**. Si el centro lo pide, hace falta una **consulta técnica por cuenta**, con instrucción escrita, dos personas y solo para esa persona; Plataforma no lee la auditoría de otros centros (`0088`, 5-6) y quien tiene acceso directo a la base es Orlando **[A verificar]**. **Choca** con la regla de no hacer informes sobre personas (`direccion-confirmada-familia.md`, D10): no es un choque real si lo pide la propia persona, pero el procedimiento debe escribirse y limitarse a eso.
4. **Datos de terceros dentro de esos registros:** la auditoría lleva el identificador del residente y, a veces, un texto libre de justificación (`expurgo-y-supresion.md`, 2.8). **No se entrega el identificador ni el texto libre de residentes** a un profesional por el art. 15; se da fecha, tipo de acción y centro **[Propuesta; decide la persona experta]** (RGPD art. 15.4).
5. **Su supresión:** apartado 2.4 y 2.5. No se borra la auditoría ni los accesos mientras esté el plazo sin fijar.
6. **No se usan estos datos para evaluar su rendimiento:** la propia pantalla lo dice («para detectar accesos indebidos, no para valorar el desempeño», `Manual.cshtml`, 1623). Derechos digitales laborales: LOPDGDD arts. 87 a 91 (leídos el 2026-11-14): tratan de dispositivos digitales del empleador (87, que exige informar de los criterios de uso), desconexión (88), videovigilancia y grabación de sonidos (89), geolocalización (90) y convenios (91); **ninguno regula expresamente un registro de accesos a una aplicación**, así que si el art. 87 alcanza a ese registro lo decide la asesoría laboral.

### 5.3 Otros terceros

Aseguradoras, abogados, otros centros, entidades de servicios sociales, autoridades: **no se entrega nada desde ResidApp**. Se reenvía al centro como cualquier petición y, si es una autoridad, se sigue 4.7. Cuando una persona llega «con un poder» o «en nombre de» otra, aplica 5.1.

---

## 6. Plantillas

Con `[ ]` para completar. **Sin datos reales** en este repositorio. Se guardan y envían fuera de él.

### 6.1 Acuse al solicitante (ResidApp recibió una petición del caso A)

> **Asunto:** `[ResidApp] Hemos recibido tu mensaje`
>
> Hola. Hemos recibido tu mensaje del `[FECHA]`. ResidApp es la herramienta que usan algunos centros; **no decide sobre los datos de los centros ni puede contestar sobre ellos**. Si tus datos están en algún centro que usa ResidApp, tu petición corresponde a ese centro, que es quien tiene que contestarte. Hemos trasladado tu mensaje a `[el responsable de privacidad del centro que nos indicaste / «el centro que nos indiques»]` **para que te conteste él**, normalmente en el plazo de un mes desde que lo recibimos. **No hemos podido comprobar quién eres ni qué datos hay**: eso lo hace el centro.
>
> Si te dirigiste al centro equivocado o prefieres hacerlo directamente, escríbele a `[CONTACTO DE PRIVACIDAD DEL CENTRO, si lo conoces y es público]`. También puedes reclamar ante `[AUTORIDAD]` si crees que no se respetan tus derechos.
>
> `[NOMBRE DE ResidApp / CONTACTO DE PRIVACIDAD DE RESIDAPP]`

(Si la persona **no indicó ningún centro**, pregúntale solo el nombre del centro y no confirmes nada más. Si escribió desde una cuenta que no se reconoce, no digas si existe o no.)

### 6.2 Reenvío al centro

> **Para** `[CONTACTO DE PRIVACIDAD DEL CENTRO]` **y** `[SUPLENTE DEL CENTRO]`. **De** `[CONTACTO DE PRIVACIDAD DE RESIDAPP]`. **Copia** `[SUPLENTE DE RESIDAPP]`.
>
> **Asunto:** `[ResidApp] Petición de derechos recibida (ref. DER-AAAA-NN)`
>
> Te escribo como encargada del tratamiento de `[RESIDENCIA]`. El `[FECHA Y HORA]` recibimos por `[CANAL]` una petición de `[derecho: acceso / rectificación / supresión / limitación / portabilidad / oposición / otro]` de una persona que dice ser `[residente / representante de un residente / familiar / profesional / otra]`. **Te la trasladamos íntegra, tal como llegó**, en el adjunto `[ADJUNTO]` (`[por el canal acordado]`). **No hemos contestado sobre el fondo, no hemos comprobado la identidad ni hemos tocado ningún dato.** Hemos enviado a la persona un acuse que no confirma ni desmiente que tenga datos en tu centro.
>
> El plazo de un mes del art. 12.3 del RGPD **corre desde `[FECHA]`**, `[según el criterio de prudencia de ResidApp: pendiente de confirmar por la persona experta]`. **Necesitamos de vosotros:** (1) acuse de recibo respondiendo a este correo; (2) quién del centro lleva el asunto; (3) si queréis nuestra ayuda técnica, **por escrito**, indicando qué hay que hacer y en qué plazo (nuestro compromiso es `[2 días hábiles para responder y 5 para entregar]`, en la cláusula `[ ]` del contrato).
>
> `[CONTACTO DE PRIVACIDAD DE RESIDAPP: nombre, correo y teléfono]`

Apunta hora de envío y hora del acuse del centro en el registro.

### 6.3 Respuesta de ResidApp al centro con lo pedido (ayuda técnica)

> **Asunto:** `[ResidApp] Respuesta a vuestra solicitud de ayuda (ref. DER-AAAA-NN)`
>
> Siguiendo vuestra instrucción del `[FECHA]` (art. 28.3.a del RGPD), hemos hecho lo siguiente: `[acción concreta, sin datos de personas: p. ej. «hemos anulado la invitación pendiente de la cuenta con identificador interno [ ]»]`. `[Resultado y comprobación: ]`. **Lo que no hemos podido o no debemos hacer, y por qué:** `[p. ej. «no existe una exportación de la historia clínica»; «el borrado de la auditoría no es posible: es inmutable y su plazo está por fijar»]`.
>
> Hemos hecho constar la operación `[fuera de la base, solo con identificadores]` para reaplicarla si se restaura una copia. **Entrega de lo pedido:** `[por el canal acordado: cifrado, clave por otro canal / en mano / otro]`. **No incluye datos de personas en el cuerpo de este correo.** La respuesta a la persona, y su identificación, son de `[RESIDENCIA]`.

### 6.4 Caso B (ResidApp responsable): acuse y respuesta

> **Acuse:** «Hemos recibido el `[FECHA]` tu petición de `[derecho]` sobre los datos que ResidApp trata como responsable `[contacto comercial / cuenta de Plataforma / otro]`. Para atenderla necesitamos `[solo lo imprescindible para identificarte, p. ej. escribir desde la misma dirección con que nos contactaste]`. Te contestaremos como máximo el `[FECHA = un mes]`; si es complejo, te avisaremos antes con el motivo (hasta tres meses en total). Puedes reclamar ante `[AUTORIDAD]`.»
>
> **Respuesta:** «`[Qué datos tenemos, de dónde vienen, para qué, por cuánto tiempo y a quién se comunican / qué hemos corregido / qué hemos borrado y qué no por obligación legal.]` Puedes reclamar ante `[AUTORIDAD]`.»

### 6.5 Registro de peticiones (sin datos de las personas)

Guárdalo **fuera del repositorio** (es público), con acceso solo a Orlando y CJ. `[DÓNDE Y CUÁNTO TIEMPO: decidir]`. **Nada de nombres, correos ni teléfonos del solicitante**; la identificación y el expediente están en el registro de derechos **del centro** (`expurgo-y-supresion.md`, 3.6: «anótalo en el registro de derechos de tu centro»). Una ficha por petición.

| Campo | Contenido |
|---|---|
| Referencia | `DER-AAAA-NN` |
| Recibida (fecha, hora) | La del día de recepción en ResidApp |
| Canal | Correo / buzón / teléfono / carta / en persona / vía el centro |
| Caso | A (por encargo) / B (como responsable) |
| Centro | Nombre del centro (el cliente), no de la persona |
| Población | Residente / representante / familiar / profesional / otra |
| Derecho | Acceso / rectificación / supresión / limitación / portabilidad / oposición / decisiones automatizadas / retirada de consentimiento / otro |
| Origen | Autoridad o juzgado (sí/no) |
| Reenviada al centro | Fecha y hora; a quién (rol, no nombre) |
| Acuse del centro | Fecha y hora |
| **Fecha límite** | Día de recepción + 1 mes, ajustada a día hábil (DA 3.ª LOPDGDD) |
| Prórroga | Sí/no; fecha del aviso del centro |
| Ayuda pedida a ResidApp | No / sí: tipo (lista cerrada: consulta, corrección, cierre de cuenta, correo, otra); fecha de petición y de entrega |
| Resultado | Atendida / atendida en parte / denegada por obligación legal / pendiente de decisión de la persona experta / retirada por la persona. **Lo comunica el centro** |
| Reclamación ante autoridad | No / sí: fecha; avisada la persona experta |
| Fecha de cierre | |
| Qué se cambia | Procedimiento, contrato, política, registro de actividades: sí/no |
| Notas | Sin datos de personas |

---

## 7. Quién hace qué cuando no estás tú, y simulacro

### 7.1 Reparto

| Persona | Papel |
|---|---|
| **Orlando** | Titular: contacto de privacidad de ResidApp; recibe; reenvía; presta la ayuda técnica; responde en el caso B |
| **CJ** (suplente; **falta su aceptación por escrito**, la misma que para brechas) | **Propuesta, por analogía con lo decidido para brechas el 2026-11-13:** recibe, **anota** y **reenvía al centro** con la plantilla 6.2 y el acuse 6.1; avisa a Orlando y a la persona experta. **No** extrae datos, **no** ejecuta nada en la base, **no** contesta de fondo y no da ayuda técnica. **No está decidido para derechos**: lo decide Orlando |
| **Persona experta** | Pendiente de contratar: valida este documento y asesora cuando haya una duda, una denegación o una autoridad |
| **Contacto del centro y suplente** | Reciben, comprueban la identidad, deciden y contestan |

**Si Orlando es parte** (por ejemplo, él es la persona que pide, o su cuenta de Plataforma): lo gestiona CJ y queda anotado.

**Qué cambia en vacaciones o por la noche:** el mes de la persona **no se detiene**, y cada día de retraso de ResidApp lo pierde el centro. **No hay cobertura definida** (`protocolo-brechas-borrador.md`, apartado 5.2: opción E, sin datos reales hasta tenerla). Un reenvío automático o una respuesta automática del buzón único que diga «tu mensaje se traslada al centro» podría servir de red de seguridad; **[Decidir]** Orlando. **El buzón único no existe aún.**

### 7.2 Simulacro

**Cuándo:** antes de cargar datos reales; **propuesta:** en la misma sesión que el simulacro de brechas (`protocolo-brechas-borrador.md`, apartado 7). Frecuencia después: `[Decidir]`.

**Cómo (ejercicio de mesa, con datos inventados, sin tocar producción):** Orlando y CJ eligen un escenario; uno hace de quien recibe y el otro de suplente. Se **cronometra** desde la recepción.

| Escenario ficticio | Qué se comprueba |
|---|---|
| Una hija escribe al buzón de ResidApp pidiendo «la historia clínica de mi madre» | Que no se contesta de fondo ni se confirma nada; plantilla 6.1; reenvío con 6.2 en el plazo; registro sin datos |
| Un familiar pide «que borréis mi teléfono y mi correo» | La condición del 0087 (apartado 2.3): se explica, se corta el acceso o los avisos si procede y **no se anonimiza** |
| Una auxiliar pide «todo lo que hay de mí, con las IP» | Que se reenvía al centro, que no se extrae nada y que el registro lo recoge |
| Llega un requerimiento de un juzgado a ResidApp | Que no se entrega nada, se avisa a la persona experta y al centro **el mismo día** (4.7) |
| Una persona escribe con un documento de identidad adjunto | Que se reenvía por canal acordado y se borra la copia (4.3) |

**Acta (sin datos de personas):** fecha, participantes, escenario, horas (recepción, reenvío), qué salió bien, qué falló, cambios acordados con responsable y fecha, dónde se guarda.

---

## 8. Qué hay en el sistema y qué falta

**Existe:** «Corregir identidad» y «Editar» familiar; versiones en valoraciones y basal; «Quitar la foto» y «No quiere foto»; suspender y revocar cuentas, perfiles y autorizaciones; «Mi cuenta» (sesiones y últimos accesos); «Anonimizar a este familiar…» (no se usa con familias reales); auditoría administrativa por ámbito; lectura en solo lectura de la historia con «Trazabilidad clínica» para Dirección Clínica; exportación CSV del censo y la estructura.

**Falta, antes de datos reales (resumen):**

1. **Buzón único** de privacidad para ResidApp y contactos reales (persona de contacto, suplente y persona experta).
2. **Cláusula del contrato de encargado** sobre derechos: comunicar o atender, forma y plazos, canal de entrega, ayuda técnica, instrucciones por escrito. El contrato **no existe** ni hay sociedad.
3. **Decisión escrita sobre bloquear o borrar** (art. 32 LOPDGDD), para familiares y para cada rectificación.
4. **Plazos de `eventos_acceso`, `sesiones` y auditoría** y su vía de borrado.
5. **Vía de consulta y copia del historial** de un residente (acceso, representación y fin de contrato): no existe (`expurgo-y-supresion.md`, línea 442, fila 18).
6. **Una forma de marcar un dato como limitado** o decidir por escrito cómo se atiende (LOPDGDD art. 16.2).
7. **Procedimiento para el acceso del personal a sus registros**, con límite a datos de terceros.
8. **Canal seguro de entrega** al centro.
9. **Acreditación del representante:** qué documento se guarda en ResidApp, si alguno.
10. **Segunda persona** para ejecutar operaciones irreversibles (guía, apartado 7.2).
11. **Política de privacidad definitiva** con el canal real del centro y el compromiso de ResidApp (hoy `PENDIENTE`).

---

## 9. A verificar o decidir

**Para la persona experta (jurídico):**

1. **Bloquear o borrar** (LOPDGDD art. 32) para familiares, y si cada rectificación o borrado desde «Editar» (nombre, teléfono, correo) exige bloqueo y copia. Si basta (A) o se construye (B), y el plazo de bloqueo.
2. **Contrato:** si ResidApp solo **comunica** o también **atiende** los derechos (LOPDGDD art. 12.3; directrices AEPD, págs. 10 y 11), y la redacción de los plazos (reenvío, ayuda).
3. **Desde cuándo corre el mes** si la petición llegó primero a ResidApp (apartado 3); el art. 12.3 no lo aclara.
4. **Identidad y representación:** qué documento acredita la representación de un residente con capacidad disminuida (Ley 8/2021), quién lo comprueba y qué se guarda. Cómo cuadra con el art. 250 del Código Civil (según la Ley 8/2021: el personal de la residencia no puede ser quien apoya). Qué puede ver una familia **sin** representante acreditado de un paciente sin capacidad (Ley 41/2002, art. 5.3 y 9.3.a; Llei 21/2000, art. 3.3).
5. **Fallecidos:** cómo se concilian LOPDGDD art. 3 (herederos y vinculados) con la Ley 41/2002, art. 18.4 (vinculados, con límites), y si Cataluña tiene una regla propia (no consta en el texto de la Llei 21/2000 de la base).
6. **Acceso:** qué se entrega a quien pide ver sus **registros de acceso** (residente, representante o profesional) y si el motivo de acceso cuenta como documentación clínica (`historial-tras-traslado-05`, 54-60); qué se considera «anotación subjetiva» y quién la separa.
7. **Limitación:** qué es limitar en un sistema de solo inserción y qué hace el centro si la piden.
8. **Portabilidad y oposición:** a qué tratamientos aplican, según la base jurídica que elija cada centro.
9. **Aplicabilidad del art. 12 de la Llei 21/2000 y de la Ley 41/2002 a una residencia** y quién es la «dirección médica» (`expurgo-y-supresion.md`, parte B, filas 4 y 22).
10. **Autoridad del centro** (AEPD o APDCAT) según su tipo (apartado 1.3), y si el centro entra en el art. 34.1.l de la LOPDGDD (centros sanitarios con historias clínicas), que le obligaría a tener DPO.
11. **Peticiones de la policía u otras autoridades** y requerimientos judiciales: qué exige la ley para que ResidApp o el centro entreguen (no verificado en la base).
12. **Decisiones automatizadas:** el dictado solo sugiere y se confirma (comprobado, apartado 2.1); confirmar con la persona que revise el código que no hay ningún otro tratamiento automatizado con efectos sobre una persona (art. 22), antes de afirmarlo por escrito.
13. **Información al personal** sobre auditoría y accesos y derechos digitales laborales (LOPDGDD arts. 87 a 91: leídos, no regulan expresamente un registro de accesos; ver apartado 5.2).
14. **La etiqueta «Tutor legal» de la app** (fichas de familiares): tras la Ley 8/2021 la tutela queda para menores y el apoyo con representación de un adulto es la curatela (apartado 5.1); si procede renombrar la marca o añadir la figura que corresponda. Es cambio de código, lo decide Orlando con la persona experta.
15. **Fallecidos con discapacidad:** quién puede pedir acceso cuando hay personas designadas para funciones de apoyo (LOPDGDD art. 3.3) y cómo lo acredita el centro.
16. **Registro de accesos a la historia clínica:** qué exige la norma catalana que desarrolla el art. 16.7 de la Ley 41/2002 (no está en la base).
17. **Dónde se guarda el registro de peticiones y cuánto tiempo** (`[ ]`).

**Para Orlando (producto y organización):**

1. **Plazo interno de reenvío** (propuesta: mismo día hábil, como máximo el siguiente) y **de ayuda técnica** (propuesta: 2 días hábiles para responder, 5 para entregar).
2. **Qué hace CJ** de suplente en derechos (propuesta: recibir, anotar y reenviar; no extraer ni ejecutar) y su **aceptación escrita**.
3. **Buzón único de privacidad** y quién lo atiende; si hay respuesta automática.
4. **Canal seguro de entrega** al centro y qué se considera aceptable mientras no exista.
5. **Si se bloquea técnicamente** el botón «Anonimizar a este familiar…» para centros reales hasta la decisión del apartado 9.1 (hoy solo hay un aviso escrito). **Orlando, no yo, decide si se toca el código.**
6. **Qué ofrecer mientras no existe exportación de la historia** (apartado 8, punto 5), y cuándo construirla.
7. **Dónde y cómo se guarda el registro de peticiones**, con acceso solo a Orlando y CJ.
8. **Frecuencia del simulacro** y si se une al de brechas.

**A verificar (hechos no confirmados):**

- Si alguien puede leer lo clínico de un residente dado de baja.
- Si, además del dictado (ya comprobado), el código excluye cualquier otra decisión automatizada.
- Si existe una exclusión por residente del dictado por voz.
- Quién puede leer los registros por cuenta con acceso directo a la base (hoy, Orlando) y cómo se haría una extracción por persona.
- Qué canal seguro ofrecen los servicios ya contratados.
- Si ResidApp guarda de alguna forma la resolución o documento que acredita un representante.

**Citas que quedan sin verificar:** **todo lo que no está en la base**: guías del CEPD sobre derechos de los interesados y sobre acceso (no están), guía de la APDCAT sobre derechos, normas catalanas de desarrollo de la historia clínica distintas de la Llei 21/2000, y normas sobre peticiones policiales.

---

## 10. Fuentes y lectura

**Base normativa local (`normativa-local/`, índice 2026-11-14):** RGPD (arts. 7.3, 12, 15 a 22, 28, 31 y 77, leídos); LOPDGDD (arts. 3, 12 a 18, 32, 33, 34.1, 37 y 72 a 74 parcialmente, disposición adicional 3.ª y 17.ª); Ley 41/2002 (arts. 5, 9, 14 a 19, leídos); Llei 21/2000 (arts. 3, 7 y 9 a 14, leídos); Ley 8/2021 (arts. 249 a 255 del Código Civil, leídos); Llei 32/2010 (art. 3); directrices 07/2020 del CEPD (apdos. 130 a 132); directrices de la AEPD para contratos de encargado (apartado E y cláusula j).

**Repositorio, leído entero:** `protocolo-brechas-borrador.md`, `expurgo-y-supresion.md` (partes A y B, hasta el final), `direccion-confirmada-familia.md`, `Exportar.cshtml`, `FamilyAnonymization.cs`, `0087_anonimizar_familiar.sql` (solo las 60 primeras líneas).

**Repositorio, leído en parte:** `guia-cumplimiento-propietarios.md` (apartados 2, 6 y líneas sueltas del resto); `politica-privacidad-portal.md` (apartados 8, 12, 13 y B5); `anexo-modulos-contrato-borrador.md` (solo líneas buscadas por «derechos», «petición», «7.4»); `antes-de-datos-reales.md` (apartado 8 y líneas sueltas, no entero); `Manual.cshtml` (secciones «Mi cuenta», fotos, baja, familiares, auditoría, historial, Portal Familiar: no entero); `Privacy.cshtml` (apartados 11 a 14); `AdministracionController.Anonimizar.cs` (80 primeras líneas); `0088_rls_eventos_auditoria.sql` (30 primeras); `0011-arquitectura-ia.md` (líneas 66 a 95); `historial-tras-traslado-05` (50-64); `registros-tecnicos-lectura-dpo.md` (líneas sueltas).

**No leído:** el código de los repositorios SQL salvo lo citado en `expurgo-y-supresion.md`; el contrato de encargado (no existe); el ENS.

**Segunda pasada (sesión principal, 2026-11-14), qué se comprobó contra las fuentes:** (1) **38 citas literales** entre comillas del RGPD, la LOPDGDD, la Ley 41/2002, la Llei 21/2000 y la Ley 8/2021 aparecen tal cual en `normativa-local/`; (2) se releyeron enteros los arts. 3, 12, 13, 14, 15, 16, 17, 18, 32, 34.1.l y 37 de la LOPDGDD, sus arts. 72.1.j y k, 74.b y c, 87 a 91 y la disposición adicional 3.ª; los arts. 5, 9, 16, 17 y 18 de la Ley 41/2002; los arts. 3, 9, 11, 12 y 13 de la Llei 21/2000; los arts. 249 y 250 del Código Civil según la Ley 8/2021; y el art. 20 del RGPD; (3) los apartados 130 a 132 de las directrices 07/2020 del CEPD y las págs. 10 y 11 de las directrices de la AEPD dicen lo que el borrador resume; (4) las líneas citadas de `Manual.cshtml` (más de 30), de `FamilyAnonymization.cs`, `Exportar.cshtml`, `0087`, `0088`, ADR 0011 y de los documentos de `docs/privacidad/` corresponden a lo que se afirma; (5) en el código, no hay interruptor que bloquee «Anonimizar a este familiar…» y el dictado solo sugiere. **Correcciones hechas:** `artículo único` pasa a `artículo segundo` (Ley 8/2021); distinción entre las infracciones 72.1.j y 74.b; el art. 18.4 de la Ley 41/2002 añade «y así se acredite»; se añaden el art. 3.3 de la LOPDGDD, el art. 16.7 de la Ley 41/2002 y el aviso sobre «Tutor legal». **No comprobado en la segunda pasada:** el resto de las líneas del `Manual.cshtml` y el contenido completo de los documentos de `docs/privacidad/`; el art. 5.1.c del RGPD; el art. 21.2 y 21.3 del RGPD.
