# Citas externas de los residentes (calendario, avisos y «Me encargo yo»): lectura de protección de datos

**BORRADOR del agente `dpo-residapp`. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-17. Segunda lectura experta, no un dictamen. Es una lectura previa al esquema SQL: no modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado en el repositorio o en el texto literal de `normativa-local/` (con archivo y línea); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar en fuente oficial. No incluyo datos reales. Los textos normativos vienen de la base local (`normativa-local/INDICE.md`, generada el 2026-11-14; comprobación más antigua 2026-11-12, menos de una semana). Son consolidados, no versión oficial; las leyes catalanas están en castellano (BOE).

Términos, en una frase cada uno. **Dato de salud**: cualquier dato que diga algo sobre la salud de una persona, incluida la atención sanitaria que recibe; la ley lo trata como «categoría especial» y exige más garantías. **Responsable**: quien decide para qué y cómo se usan los datos (la residencia). **Encargada**: quien los trata por cuenta del responsable y solo según sus instrucciones (ResidApp). **Subencargado**: un proveedor de la encargada (Azure, Microsoft para el correo). **EIPD** (evaluación de impacto): análisis de riesgos por escrito que el responsable debe hacer antes de empezar un tratamiento delicado. **Minimización**: guardar y enseñar solo lo necesario. **Auditoría**: el registro de quién hizo qué y cuándo. **Seguridad por filas**: regla de la base de datos que impide que un centro lea las filas de otro.

## Conclusión

**Admisible con condiciones.** Una cita externa (tipo + especialidad + lugar + fecha) es un dato de salud y la función es legítima para la atención del residente. No hay tercero nuevo (Mistral no interviene; el correo ya existe) ni categoría de dato nueva respecto a lo que ResidApp ya trata. Lo que sí es nuevo, y es el riesgo importante, es esto:

1. **Es la primera función donde la familia ve, por defecto y para todos los familiares autorizados, información sobre la atención sanitaria del residente que ella no ha pedido.** La ley no da a la familia ese derecho por sí misma: la información sobre la salud se comparte con las personas vinculadas «en la medida que el paciente lo permita de manera expresa o tácita» (Ley 41/2002, art. 5.1). ResidApp hoy no guarda la voluntad del residente sobre qué ve su familia. Con «siempre la especialidad» a toda la familia, una consulta de Salud Mental, Oncología o Infecciosas puede llegar a un hijo al que el residente no quería contárselo.
2. **El «lugar» y el «qué preparar» son vías de fuga iguales que la especialidad.** «Hospital de día oncológico» o «Centro de salud mental» dicen lo mismo que la especialidad. Si se oculta una y no la otra, no se ha ocultado nada.
3. **La nota «al terminar» puede convertirse en una segunda historia clínica** escrita por perfiles que no deberían ver contenido clínico (Administración).
4. **Los avisos por correo pasan de «un destinatario» a «todos los autorizados»** y son los primeros que dicen algo (que hay una cita externa que cubrir) a personas que no hicieron nada para recibirlos.

Se puede construir **ya con datos inventados y los envíos apagados**. **Antes de usarlo con una familia real** hacen falta las cuatro cosas del apartado 9 (la decisión sobre la voluntad del residente, la información a residentes y familias, el contrato, y la EIPD del centro con una línea sobre este módulo).

Mi recomendación sobre CJ y «siempre la especialidad»: **mantenerla como valor por defecto, pero no como regla sin excepciones** (apartado 4.2). CJ misma dice que en algún caso habría que ponerlo solo como «consulta»: la ley la respalda.

---

## 1. Qué datos, de quién, quién es quién

### 1.1 Datos [Hecho, `citas-externas-modulo-cj.html`, líneas 373 a 388 y respuestas de CJ del 2026-11-17]

| Dato | De quién | ¿Salud? |
|---|---|---|
| Residente, día y hora | Residente | Sí, en combinación con lo siguiente: revela que recibe atención |
| Tipo, especialidad o motivo, lugar | Residente | **Sí** |
| Qué preparar | Residente | Puede serlo (ayuno, medicación, un informe) |
| Transporte (modo y estado) y acompañante (modo y estado) | Residente | Dato logístico; con la fecha, revela la cita |
| Estado de la cita (programada…, «no se acudió») | Residente | Sí |
| Nota «al terminar» | Residente | **Sí, y es contenido clínico** (apartado 7) |
| Quién se encarga, «No puedo», cuándo | **Familiar** (identificador de su cuenta y vínculo) | No es salud, pero dice que esa persona acompañará a un residente a una cita |
| Si la familia ve la cita u oculta | Residente | Dato de gestión |

Definición legal: «datos relativos a la salud: datos personales relativos a la salud física o mental de una persona física, incluida la prestación de servicios de atención sanitaria, que revelen información sobre su estado de salud» (RGPD, art. 4.15, `rgpd.txt`, línea 57), y el considerando 35 incluye «la información sobre la persona física recogida [...] con ocasión de la prestación de tal asistencia» (`rgpd-considerandos.txt`, línea 40). Saber que alguien tiene «Cardiología, hospital X, el día 25» entra ahí. **Conclusión: toda la ficha es dato de salud**, aunque cada campo suelto parezca inocente.

### 1.2 Papeles [Hecho: ADR 0007 y `antes-de-datos-reales.md`; aplico lo ya leído]

- **Responsable: la residencia.** Decide si usa la función, qué ve cada perfil y qué ve la familia.
- **Encargada: ResidApp.** Ejecuta las instrucciones del centro: «tratará los datos personales únicamente siguiendo instrucciones documentadas del responsable» (RGPD, art. 28.3.a, `rgpd.txt`, línea 330). El contrato debe fijar «el objeto, la duración, la naturaleza y la finalidad del tratamiento, el tipo de datos personales y categorías de interesados» (art. 28.3, línea 329): **hay que añadir al objeto del contrato (o al anexo de módulos, si es módulo) la «agenda de citas externas»**.
- **Subencargados:** Azure (base de datos y web) y Microsoft Azure Communication Services (correo, ya existente). **No hay ninguno nuevo** y la función no usa IA ni dictado.
- **La familia** son, a la vez, destinatarios de información (del residente) y titulares de datos propios (su nombre, su correo, su decisión de «me encargo»).

### 1.3 Base jurídica [Opinión; Decidir DPO]

- **Cita del residente.** Art. 9.2.h RGPD: tratamiento necesario para «prestación de asistencia o tratamiento de tipo sanitario o social, o gestión de los sistemas y servicios de asistencia sanitaria y social, sobre la base del Derecho de la Unión o de los Estados miembros o en virtud de un contrato con un profesional sanitario» (`rgpd.txt`, línea 124), con la garantía del art. 9.3: lo trata «un profesional sujeto a la obligación de secreto profesional [...] o cualquier otra persona sujeta también a la obligación de secreto» (línea 127; la LOPDGDD, art. 5, extiende el deber de confidencialidad a todos los que intervienen, `lopdgdd.txt`, línea 89). Base del art. 6: 6.1.b (contrato de residencia) en un centro privado; 6.1.e si es público. La disposición adicional 17.ª de la LOPDGDD ampara en el art. 9.2.h los datos de salud «regulados» por la Ley 41/2002 «y sus disposiciones de desarrollo» (línea 811); la Llei 21/2000 no figura en esa lista: **[A verificar]** si basta esa disposición o se apoya el art. 9.2.h en el contrato asistencial.
- **Datos del familiar (quién se encarga).** No hay contrato con el familiar para esto. Lo más ajustado es el interés legítimo del centro en coordinar el transporte y el acompañamiento (art. 6.1.f), con el derecho de oposición que lleva (art. 21; en un centro público, 6.1.e). Es voluntario: nadie está obligado a pulsar «Me encargo yo». **[Decidir DPO]**.
- **No hace falta consentimiento** del residente para guardar la cita en la agenda del centro. **Sí hace falta ese permiso, o un equivalente (permiso expreso o tácito, o la valoración del médico), para que la familia vea esa información** (apartado 4).

### 1.4 Registro de actividades (art. 30) y EIPD

- **Registro:** hay que **añadir una actividad nueva** (no basta con ampliar «avisos de citas»): «Agenda de citas externas de residentes y coordinación con la familia». Propuesta en el apartado 10.
- **EIPD:** el art. 35.3.b exige EIPD en el «tratamiento a gran escala de las categorías especiales»; y la lista de la AEPD (art. 35.4) incluye los puntos 4 (categorías especiales) y 9 (sujetos vulnerables, «mayores con algún grado de discapacidad [...] personas que acceden a servicios sociales»), que ResidApp ya cumple (`aepd-lista-eipd.txt`, líneas 65 a 82; ADR 0015, línea 156). **El módulo no crea una EIPD nueva, pero cambia el riesgo** y el art. 35.11 pide revisarla: «el responsable examinará si el tratamiento es conforme con la evaluación de impacto [...] al menos cuando exista un cambio del riesgo» (`rgpd.txt`, línea 420). **[Opinión]** Basta un anexo a la EIPD del centro con los riesgos de este módulo (apartado 10.2). La consulta previa a la autoridad (art. 36) solo llegaría si, tras las medidas, el riesgo siguiera siendo alto: no lo veo.
- **Producto sanitario:** un calendario no ayuda a decidir tratamientos ni diagnósticos, así que **no veo que la regla 11** del Reglamento (UE) 2017/745 («programas informáticos destinados a proporcionar información que se utiliza para tomar decisiones con fines terapéuticos o de diagnóstico», `mdr.txt`, línea 2560) **le alcance**. Dejaría de ser cierto si se añadieran alertas clínicas (por ejemplo, «suspender el anticoagulante»): **no se añaden** (el campo «qué preparar» es logística, apartado 3). **[A verificar]** por una persona experta.

### 1.5 Autoridad de control (AEPD o APDCAT)

El art. 3 de la Llei 32/2010 (`llei-32-2010.txt`, líneas 25 a 38) incluye en el ámbito de la APDCAT, entre otros: «a) Las instituciones públicas», «b) La Administración de la Generalidad», «c) Los entes locales»; «e) Las entidades de derecho privado que cumplan, como mínimo, uno de los tres requisitos [capital, ingresos presupuestarios u órganos directivos mayoritariamente públicos]»; y «f) Las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios».

| Tipo de centro piloto | Autoridad según ese artículo | Seguridad |
|---|---|---|
| Público (de la Generalitat o de un ente local) | APDCAT (art. 3, letras a a d) | Esquema Nacional de Seguridad obligatorio |
| Privado con plazas concertadas | **[A verificar]**: la letra f) apunta a la APDCAT para los tratamientos «vinculados a la prestación» del servicio público concertado, y es discutible si los de las plazas no concertadas siguen en la AEPD | Depende del concierto; lo decide un abogado |
| Privado sin concierto | AEPD | Sin ENS |

**Dato que falta y que necesito:** titularidad del centro piloto y si tiene plazas concertadas (Llei 12/2007; ADR 0015, línea 157). El proyecto supone «privado pequeño (AEPD, sin ENS)» (`antes-de-datos-reales.md`, línea 171): es una suposición. Para esta función cambia solo a quién se reclama y el texto de la cláusula; no cambia el diseño.

---

## 2. Reparto de lo que ve cada perfil (propuesta)

Tabla de lo que cada perfil recibe **del servidor** (no «oculto en pantalla»: lo que no corresponde no sale en la respuesta).

| Campo | Administración | Enfermería (sus unidades) | Medicina (sus unidades) | Auxiliar | Familia |
|---|---|---|---|---|---|
| Residente, día y hora | Sí | Sí | Sí | Solo hoy y mañana | Sí, de su residente |
| Tipo (general) | Sí | Sí | Sí | No | Sí |
| Tipo detallado y especialidad | Sí | Sí | Sí | **No** | Según el control de visibilidad (4.2) |
| Lugar | Sí | Sí | Sí | **No** | Con la especialidad (4.2) |
| Transporte y acompañante (modo y estado) | Sí | Sí | Sí | Solo si es persona del centro (para saber quién sale) | Sí |
| **Nombre** de quien se encarga | Sí | Sí | Sí | **No** (a decidir con CJ) | **No** |
| Qué preparar | Sí | Sí | Sí | Sí | Sí |
| Nota «al terminar» | **No** | Sí | Sí | **No** | **No** |
| Quién cambió qué | Auditoría | | | | |

Notas:

- **[Hecho]** Un perfil de Administración puede acceder solo a lo que necesita para su función: «El personal que se ocupa de las tareas de administración y gestión de los centros sanitarios puede acceder sólo a los datos de la historia clínica relacionados con las mencionadas funciones» (Llei 21/2000, art. 11.4, `llei-21-2000.txt`, línea 117). Registrar y mover citas es gestión; **leer lo que el especialista indicó no lo es**. Por eso propongo que la nota del apartado 7 **no la lea Administración**. Es un matiz respecto al permiso por perfil de CJ («Enfermería y Administración registran y cambian»): registran y cambian la cita; la nota la anotan y leen los perfiles clínicos. **[Decidir CJ]**.
- El Auxiliar puede necesitar saber **quién** viene a recoger al residente (es una persona vulnerable que sale del centro). Ese es un protocolo del centro, no de la aplicación; propongo «Familiar» sin nombre y que CJ diga si hace falta el nombre.
- **Seguridad por filas y unidades [Hecho, ADR 0008 recogido en ADR 0015 línea 29]:** la base solo separa por **centro**; la separación por **unidad** la hace la aplicación. Hay que probar que Enfermería de la unidad B no ve las citas de la unidad A (y que un traslado de unidad mueve la visibilidad). Una cuenta con perfil en dos centros ve cada centro por separado; una cuenta de familia que lo es en dos centros no mezcla residentes de centros distintos en un mismo calendario.

---

## 3. Condiciones de diseño de los campos

Un texto libre acaba conteniendo lo que nadie previó. El precedente del proyecto es la ficha de enfermedades: `<input>` de una línea con `maxlength`, ayuda visible junto al campo, la base de datos que rechaza lo que sobra, y ninguna zona de observaciones (`antes-de-datos-reales.md`, líneas 176 y 177). Aplíquese lo mismo.

| Campo | Condición | Longitud (propuesta mía, ajustable) | Ayuda visible junto al campo |
|---|---|---|---|
| Tipo | Lista cerrada de CJ: prueba (analítica, radiografía, ecografía), consulta, tratamiento (hospital de día, fisioterapia), otra | — | — |
| Especialidad | **Lista cerrada.** Sin texto libre. Cada especialidad lleva una marca **«visible a la familia por defecto: sí/no»** (4.2). «Otra» sin texto o con una etiqueta de 40 caracteres que solo ve el personal clínico y de Administración | — | — |
| Lugar | `<input>` de **una línea**, nunca `textarea`; la BD rechaza saltos de línea | 80 | «Solo el nombre del hospital o centro. No escribas el servicio, la unidad ni el motivo.» |
| Qué preparar | **Casillas cerradas** (ayuno, llevar el último informe, tarjeta sanitaria, medicación, silla de ruedas, otra) más una línea corta opcional | 100 | «Solo lo que hay que llevar o hacer para salir. Las indicaciones clínicas (suspender un fármaco, dosis) van por Medicina, no aquí.» |
| Nota «al terminar» | Apartado 7 | 150 | Apartado 7 |
| Hora «por confirmar» | Booleano; no admite texto | — | — |
| Estado de la cita | Lista cerrada; «no se acudió» **sin causa en texto**: la causa (rechazo, ingreso) es clínica y va a la historia | — | — |
| Motivo de cancelar o reprogramar | **Lista cerrada** más, como mucho, una línea de 100. Una cancelación por baja lleva el texto fijo de la aplicación | 100 | «No escribas diagnósticos ni nombres.» |

Por qué estas tres líneas importan:

- **Lugar.** «Unidad de oncología», «CSMA», «Hospital de día de salud mental» **son** la especialidad. Se trata igual que ella (4.2) y se pide solo el nombre del centro.
- **Qué preparar.** Lo ve el Auxiliar y, según el caso, la familia. «Preparación de colonoscopia» o «suspender el anticoagulante» revelan diagnóstico o medicación. Casillas cerradas cubren el 90 % de usos.
- **Sin IA, sin dictado, sin importar** la carta de cita del hospital por lectura automática de texto: CJ ha dejado fuera adjuntos y sincronizaciones, y el ADR 0011 solo autoriza el dictado en «Registrar cambio». Cualquier ampliación necesita ADR nuevo, contrato y EIPD.

### 3.1 Qué especialidades merecen ser «ocultas por defecto» a la familia

El RGPD no gradúa los datos de salud: son todos «categoría especial» (art. 9.1, `rgpd.txt`, línea 112). La gradación es de **riesgo para la persona si se sabe**, y la hace el DPO con CJ. Propuesta de partida, **sin cifras ni norma detrás**, para decidir:

- **Ocultas por defecto** (la familia ve «consulta fuera del centro»): salud mental (psiquiatría, psicología clínica), oncología y radioterapia, enfermedades infecciosas (donde un VIH sea posible), unidades de conductas adictivas, cuidados paliativos, genética.
- **A decidir** (íntimas pero menos reveladoras en personas mayores): ginecología, urología, proctología.
- **Visibles por defecto**: geriatría y medicina interna, cardiología, neumología, digestivo, endocrinología, nefrología, neurología, traumatología y rehabilitación, oftalmología, otorrinolaringología, dermatología, odontología, podología, análisis, radiología y ecografía.

El personal siempre puede **ocultar más** en una cita concreta; mostrar una especialidad oculta por defecto exige una acción explícita («el residente o su representante lo ha permitido»), que se audita.

---

## 4. La familia

### 4.1 Qué puede ver y por qué hay que ir con cuidado

**[Hecho]** Hoy la familia solo ve las citas **que ella misma reservó**: CJ lo decidió por minimización (`aviso-cita-cambio-cancelacion-por-correo.md`, líneas 113 a 119). Las citas externas son de otra naturaleza: no son del familiar sino del residente, y **no las pidió la familia**. Eso eleva el listón.

- La Ley 41/2002 (art. 5.1, `ley-41-2002.txt`, línea 56): «El titular del derecho a la información es el paciente. También serán informadas las personas vinculadas a él, por razones familiares o de hecho, en la medida que el paciente lo permita de manera expresa o tácita». La Llei 21/2000 (art. 3.1, `llei-21-2000.txt`, línea 35) lo dice igual: «Debe informarse a las personas a él vinculadas en la medida en que éste lo permita expresa o tácitamente». Y para quien no puede entender (art. 3.3, línea 37): «Si el paciente, a criterio del médico responsable de la asistencia, no es competente para entender la información [...] debe de informarse también a los familiares o a las personas a él vinculadas».
- **[Hecho]** El «autorizado» de ResidApp es la autorización que da **Administración** a un vínculo (`0022_familiares_autorizaciones.sql`, líneas 6 a 8; `SqlFamilyPortalReader.cs`, líneas 23 a 38): es un permiso de acceso al portal, **no** la voluntad del residente. Que el familiar esté «autorizado» no prueba por sí solo que el residente permita que sepa su especialidad.
- RGPD, art. 25.2: «por defecto, solo sean objeto de tratamiento los datos personales que sean necesarios para cada uno de los fines específicos» y «los datos personales no sean accesibles, sin la intervención de la persona, a un número indeterminado de personas físicas» (`rgpd.txt`, línea 310). Los autorizados no son un número indeterminado, pero el principio apunta a que lo sensible no sea visible por defecto.

**Mi lectura:** la regla «todas las citas visibles por defecto» es defendible **si el centro ha recogido el permiso tácito o expreso del residente** (o de quien le apoya) al conceder la autorización, y si hay una salida fácil para quien no lo da. La aplicación no puede asegurarlo; sí puede facilitarlo (4.3).

### 4.2 Ocultar por cita y ver siempre la especialidad: mi recomendación

CJ eligió «siempre la especialidad» (A de la 4.2) y la opción 4.1 B («todas por defecto, el personal puede ocultar una concreta»). **Recomiendo cambiarlo a esto, sin quitar nada de lo que CJ pidió:**

1. **Tres niveles por cita**, y el personal elige el nivel al registrar o después:
   - **Completa:** tipo detallado, especialidad y lugar (lo que CJ quiere por defecto).
   - **Solo general:** «consulta / prueba / tratamiento fuera del centro» con día, hora, transporte, acompañante y qué preparar; sin especialidad ni lugar. (Es lo que CJ llama «en algún caso, solo el centro»).
   - **Oculta:** la familia no la ve. Entonces **tampoco hay «Me encargo yo»**: el transporte y el acompañante se hablan por teléfono.
2. **Valor por defecto según la marca de la especialidad** (apartado 3.1): visible → «completa»; delicada → «solo general».
3. **Motivo:** el art. 5.1 de la Ley 41/2002 y el art. 3.1 de la Llei 21/2000 hacen de la voluntad del residente la puerta. Sin al menos «solo general», el centro queda sin herramienta cuando el residente diga «a mi hija no se lo cuentes». Que CJ lo vea como excepción poco frecuente no cambia que la ley la exige.
4. **Mi razón práctica:** la familia puede cubrir transporte y acompañamiento sin conocer la especialidad (basta «consulta fuera del centro, hora, lugar general, qué llevar»), y CJ misma dice que si falta, la familia llama al centro. El coste de pasar a «solo general» es una llamada; el coste de lo contrario es una revelación que no se deshace.
5. **Si Orlando o CJ mantienen «siempre la especialidad» sin excepción** **[Decidir]**: el riesgo (mala revelación a un familiar) queda como **riesgo aceptado por escrito en la EIPD del centro**, con responsable y fecha, y el procedimiento de admisión debe recoger «¿qué puede ver su familia?» para cada residente.

### 4.3 La voluntad del residente: cómo se recoge sin construir un sistema

- **Procedimiento del centro (imprescindible, no es código):** al conceder la autorización de acceso a un familiar, la residencia pregunta al residente (o a quien le apoya) si permite que esa persona vea la información de sus citas fuera del centro, y lo deja escrito en la historia. **[A verificar con la residencia]** si el procedimiento de alta ya lo recoge.
- **Interruptor por residente (recomendado, pequeño):** «La familia no ve las citas externas de esta persona» (Enfermería o Administración, con auditoría). Evita acordarse cita por cita, que es el punto frágil: el valor por defecto sería «visible» y cada olvido es una revelación. Sin este interruptor, el control por cita es solo defensa parcial. **[Decidir CJ y Orlando]**.
- **Residentes con apoyo (Ley 8/2021):** el art. 249 del Código Civil (en la redacción de esa ley) manda que «las personas que presten apoyo deberán actuar atendiendo a la voluntad, deseos y preferencias de quien lo requiera» y, solo en casos excepcionales, permite funciones representativas (`ley-8-2021.txt`, líneas 333 a 336). Consecuencias: (a) **la capacidad no se presume ausente por vivir en una residencia**; (b) la decisión de qué ve la familia sigue siendo, en lo posible, del residente, y si no puede, de quien le representa o apoya y del criterio del médico (Llei 21/2000, art. 3.2 y 3.3); (c) ResidApp **no decide** si alguien es competente: solo da las herramientas. **[A verificar]** `0043_familiares_referente_tutor.sql` (líneas 11 a 13) marca «tutor legal» y «referente» con una terminología anterior a la Ley 8/2021; no es objeto de esta lectura, pero un «tutor» formal no es lo mismo que un familiar autorizado y la pantalla no debería equiparar ambos.
- **Un familiar autorizado no es quien decide** por el residente: es quien ayuda a cubrir una cita. «Me encargo yo» no es consentir nada ni hablar con el médico.

### 4.4 Varios familiares

- **Primero que pulsa, se queda [Hecho en la propuesta de CJ].** Condición técnica: la asignación es **atómica** (una sola instrucción que solo cambia si nadie la tiene; dos pulsaciones simultáneas, una gana y la otra recibe «ya lo tiene otra persona»). Pruebas de concurrencia.
- **Los demás ven «lo hará un familiar» sin nombre.** Es el mínimo. Hay que saber que en una familia de dos hermanos, el otro lo deduce. No se puede evitar y es aceptable.
- **El nombre solo lo ve el personal con permiso** (Administración, Enfermería, Medicina). Nunca el Auxiliar ni la familia (propuesta; el Auxiliar solo si CJ lo pide).
- **Quien pulsa debe ver lo mismo que ve el resto de la familia** y, además, lo que necesita para cumplir (hora, lugar general, qué preparar). Si la cita está «oculta», no hay tarea.
- **Falta una función que CJ no ha pedido y que es necesaria:** **«Ya no puedo»** (soltar la tarea). Sin ella, quien pulsó se queda con ella para siempre y la cita sale como «cubierta» sin estarlo. Al soltar, el personal ve el aviso. **[Decidir CJ y Orlando]**.
- **La tarea se suelta sola** si el familiar pierde el acceso (vínculo roto, autorización revocada o caducada, cuenta suspendida o anonimizada, residente de baja o suspendido): se usa la misma consulta del portal (`SqlFamilyPortalReader.AccessibleLinks`) en el momento de mirar el estado, no una copia. Si no, el calendario del personal dice «cubierta» por alguien que ya no puede.
- **Anonimizar a un familiar (`0087`):** la tabla de tareas guarda el **identificador del vínculo o la cuenta, no el nombre**. Así, al anonimizar, el nombre desaparece de la pantalla del personal con el resto. **[Hecho]** el trabajo está en `expurgo-y-supresion.md`, apartado 3.

### 4.5 ¿Crea «Me encargo yo» un dato nuevo del familiar? Sí.

Es un dato personal del familiar: su identificador, el hecho de que acompañará o transportará a un residente, y la fecha. No es de salud, pero la combinación con la cita lo roza. Consecuencias:

- **Información previa (art. 13 RGPD):** junto a los botones, una línea fija. Propuesta en el apartado 10.3. El texto del portal debe decir que el personal del centro ve su nombre y que los otros familiares solo ven «lo hará un familiar».
- **Auditoría con solo identificadores [Hecho, patrón de `SqlResidentStatusRepository.cs`, líneas 115 a 117: cuenta, perfil, centro, unidad, residente, tipo y número de recurso, acción, hora]:** acciones nuevas (por ejemplo `EXTERNAL_APPOINTMENT_FAMILY_TAKE`, `_DECLINE`, `_RELEASE`) con perfil `FAMILIAR`. **Ni especialidad, ni lugar, ni nota, ni «qué preparar» en la auditoría**: Administración la ve (`AdministrativeAudit.cs`) y no debe aprender así lo que no puede leer en la ficha.
- **Historial de cambios de la tarea:** una tabla de solo añadir (como `citas_cambios`) que admita el perfil `FAMILIAR` **y** Enfermería, Administración. Hoy `CK_citas_cambios_perfil` solo admite `FAMILIAR` y `ADMINISTRACION` (`0054_citas_reserva_directa.sql`, línea 125): **no se reutiliza esa tabla**.
- **«No puedo»:** puede guardarse como evento, pero **no debe usarse para valorar a las familias** (quién «nunca puede»). Sin contadores por familiar, sin informe por familiar. Si el art. 4.4 del RGPD («elaboración de perfiles») se llegara a aplicar, es por este uso, que no se construye.
- **Retención del dato del familiar:** apartado 6.

---

## 5. Avisos

### 5.1 Al personal, en la pantalla de inicio

- **[Hecho]** CJ: avisos a 7 y 2 días, solo en pantalla, sin correo; para ambulancia y acompañante, solo Administración.
- **Qué muestra:** residente (con el mismo nombre que enseña el resto de la app), fecha, hora y «falta transporte» o «falta acompañante». **No la especialidad ni el lugar**: la pantalla de inicio es la que más se ve de pasada en un control de enfermería. Quien necesite el detalle abre la ficha, con su permiso.
- **A quién:** a quien tenga el **permiso** «gestionar citas externas» en las unidades de ese residente, no a un perfil fijo. Que el aviso de ambulancia y acompañante vaya solo a Administración (CJ) es una regla de reparto de avisos; el permiso decide quién puede **cambiar**, el aviso decide quién se **entera**. **[Opinión]** Un riesgo funcional (no de privacidad): si Enfermería registra y cambia, pero no recibe el aviso, un hueco no lo ve quien lo sabe arreglar. Es de CJ.
- **Se calcula al abrir la pantalla**, sin tabla de «avisos leídos por persona»: guardar quién ha visto qué aviso sería vigilar al personal y no hace falta.
- **Sin push, sin SMS, sin WhatsApp, sin calendario externo (.ics)**: CJ los dejó fuera y es lo correcto. Un `.ics` o un enlace a Google Calendar llevaría datos de salud a un tercero sin contrato.
- **Imprimir.** Una lista semanal impresa en un tablón es el sitio donde esto se filtra. Si se ofrece imprimir, que sea **solo la vista del Auxiliar** (hora y qué preparar, sin especialidad ni lugar) y con la línea «Confidencial». Si se quiere impresión completa, es otra decisión con la misma cautela.
- **Dispositivo.** Que la respuesta del calendario no se guarde en la memoria del móvil o del navegador (`Cache-Control: no-store`, sin entrar en `sw.js`). **[A verificar]** con el patrón actual del servicio de la PWA.

### 5.2 Correo a la familia (7 días, víspera, 2 días si sigue sin cubrir)

**¿Sigue siendo admisible con los avisos actuales?** **Sí, con tres cambios.** El marco es el de `aviso-cita-por-correo.md`, `aviso-cita-cambio-cancelacion-por-correo.md` y `recordatorio-cita-por-correo.md`: apagado por defecto; solo a correo **confirmado** («sin la marca no sale ningún aviso», `antes-de-datos-reales.md`, línea 106); un mensaje por destinatario, sin `Cc` ni `Cco`; texto cerrado, sin residente, centro, hora, especialidad ni detalle; comprobación de acceso en el momento de enviar con la misma consulta del portal; registro de envíos sin dirección; purga a 12 meses (el procedimiento `0085` **no distingue plantilla**, `recordatorio-cita-por-correo.md`, línea 26).

Los tres cambios:

1. **Plantillas nuevas, no reutilizar `RECORDATORIO_CITA`.** Ese correo dice «Tienes una cita mañana en ResidApp» y va a quien reservó **su** cita con el centro. En una cita externa la cita es del residente: el mismo texto daría a entender al familiar que tiene una cita propia (confusión, y patrón de fraude). Propuesta de texto, tomada de la propia propuesta de CJ y sin palabras que digan «residente», «centro», «salud» o «médico»: «Tienes un aviso sobre una cita en ResidApp». Una plantilla para el aviso de 7 días y la víspera, otra para «sigue sin cubrir». **Condición:** el generador de texto recibe solo idioma y la dirección de la aplicación, como los demás (`aviso-cita-por-correo.md`, apartado 3, condición 7), con prueba de comparación exacta del texto.
2. **Hay que añadir las plantillas nuevas al cierre de avisos sin resolver (`0089`).** Está escrito por plantilla: uno que se quede PENDIENTE no lo cerraría nadie (`recordatorio-cita-por-correo.md`, línea 26; `0092_recordatorio_cita.sql`, líneas 24 a 27).
3. **El destinatario cambia: ya no es solo «quien reservó».** CJ: a quien se encarga y, si nadie, a **todos** los autorizados con correo confirmado. Esto rompe el «destinatario único» que sostenía el análisis anterior (`aviso-cita-por-correo.md`, línea 95; `aviso-cita-cambio-...md`, líneas 113 a 119, que advertía de los riesgos de «todos»: más direcciones, revelar entre familiares que hay una cita, cada uno debe haber sido informado). Lo acepto **con estas condiciones**: (a) **un mensaje por persona**, sin lista ni nombres de los otros; (b) **nunca para una cita oculta o en nivel «oculta»**; (c) **información previa a cada familiar** (apartado 10.3), incluida la ampliación de finalidad (art. 6.4 y 13.3 RGPD); (d) un aviso nunca sale si el residente ha causado baja, está suspendido, o el vínculo o la autorización ya no valen (la comprobación se repite al enviar: lo que pasó con el aviso de cambio, N3 y N8); (e) con el derecho de oposición mencionado si la base es el interés legítimo o la misión pública: el art. 21.1 lo da frente a tratamientos basados en el art. 6.1, letras e) o f), y el 21.4 exige que «a más tardar en el momento de la primera comunicación con el interesado» sea «mencionado explícitamente [...] y presentado claramente y al margen de cualquier otra información» (`rgpd.txt`, líneas 261 y 264); (f) **no se manda el aviso de 7 días a todos** cuando la cita **no pide nada a la familia** (transporte y acompañante cubiertos por el centro): es la misma minimización del art. 5.1.c (`rgpd.txt`, línea 75), y evita decenas de correos que solo dicen «hay una cita». **[Decidir CJ]**: CJ pidió «7 días antes y la víspera»; lo que propongo es acotar a las citas en las que la familia tiene algo que hacer o ya se ha encargado.

**«Solo si sigue sin cubrir».** Es un correo que el sistema decide por su estado, no por una acción de nadie, como el recordatorio. **Condición:** al reservar el envío (procedimiento de la propia tarea, como `dbo.reservar_recordatorios_cita`, `0092`, líneas 14 a 20, que **no da INSERT al rol de la tarea**) y **otra vez al enviar** se comprueba que sigue sin cubrir; si alguien se ha encargado entre medias, se omite como `OMITIDO` con motivo cerrado. La clave que impide el doble envío debe incluir el **tramo** (7 días, víspera, 2 días) y la **fecha de la cita**, para que reprogramar cree avisos nuevos sin duplicar (`referencia_clave` admite 40 caracteres). Un correo a las 02:00 no es razonable: la franja del recordatorio actual (10:00 a 18:59, `0092`, línea 17) sirve.

**Momento delicado.** El aviso de 2 días «sigue sin cubrir» puede llegar a una familia el día después de que el residente fallezca o ingrese, si la baja todavía no está registrada. Es el mismo riesgo residual del recordatorio (`recordatorio-cita-por-correo.md`, línea 17). Se acepta con las comprobaciones de arriba; el centro debe saber que **registrar el fallecimiento o el ingreso a tiempo** es lo que corta los avisos.

**Información a la familia sobre FAM-10.** La FAM-10 reescrita nombra propuesta, recordatorio y código de acceso; el aviso de cambio se amplió aparte. **[Decidir CJ]** que consta por escrito que FAM-10 incluye ahora estos avisos de citas externas (su respuesta del 2026-11-17 lo decide en lo sustancial; falta que se refleje en el alcance).

---

## 6. Retención, supresión, baja, auditoría

### 6.1 Retención

No encuentro en la base local un plazo específico para una agenda de citas. Lo que hay:

- **El historial clínico se conserva** por tipos: quince años desde el alta de cada proceso para los consentimientos, informes de alta, de exploraciones complementarias, etc. (Llei 21/2000, art. 12.4, `llei-21-2000.txt`, línea 124) y «puede destruirse» a los cinco años el resto (art. 12.6, línea 133). ResidApp aplica 15 años desde la baja a todo lo clínico (`expurgo-y-supresion.md`, apartado 1; `antes-de-datos-reales.md`, líneas 115 a 119). **[A verificar]** si una residencia es «centro sanitario» a efectos del art. 12.
- **Lo que fija CJ y la lectura de ficha: las citas existentes «siguen al residente» y su plazo propio es «sin cifra mía»** (`expurgo-y-supresion.md`, línea 72).

Propuesta, por capas **[Decidir DPO]**:

| Dato | Criterio propuesto |
|---|---|
| **Ficha de la cita** (tipo, especialidad, lugar, estado, transporte y acompañante en modo) | Sigue al residente, igual que las citas actuales: 15 años desde la baja por defecto. Es lo más simple y compatible con el catálogo de expurgo. El centro puede acortar si la ley lo permite |
| **Nota «al terminar»** | Es contenido clínico: mismo plazo que lo clínico, y con autoría (Llei 21/2000, art. 9.3: «debe garantizarse que quedan registrados todos los cambios e identificados los médicos y los profesionales asistenciales que los han realizado», `llei-21-2000.txt`, línea 82) |
| **Quién se encarga y «No puedo»** (dato del **familiar**, no clínico) | **Criterio propio**, más corto que lo clínico: solo hace falta mientras se organiza la cita y para resolver una incidencia posterior. La tabla de solo añadir lleva el vínculo; la purga exige un **procedimiento con permiso aparte**, como `0085` y `0089`, no quitar protecciones (`antes-de-datos-reales.md`, línea 163). El plazo lo fija el DPO real; hasta entonces, no se guarda el nombre en ninguna otra tabla ni en la auditoría más allá del identificador |
| **Avisos por correo** | Los 12 meses del registro de envíos ya cubren las plantillas nuevas si la purga no distingue plantilla |
| **Auditoría** | La de siempre (plazo sin decidir) |

### 6.2 Catálogo de expurgo y permisos

**[Hecho]** El catálogo de expurgo debe listar **cada tabla** alcanzable desde `residentes`, con una prueba que falla si aparece una tabla nueva sin entrada (`expurgo-y-supresion.md`, línea 95). **Las tablas de citas externas, su historial y las tareas deben entrar en el catálogo desde el primer script**, con su categoría (ficha: B; nota: B; tareas del familiar: C, no clínica). Además, `CoberturaRlsTests` obliga a cubrir cada tabla nueva con seguridad por filas o a justificar la excepción, y `PermisosAplicacionTests` a declararla en la matriz de permisos de `0091` (ADR 0015, línea 29; `antes-de-datos-reales.md`, líneas 152 a 156).

### 6.3 Baja y suspensión

- **Baja del residente: las citas externas futuras se cancelan solas, igual que las actuales [Hecho, patrón de `SqlResidentStatusRepository.cs`, líneas 96 a 137]:** estado `CANCELADA`, causa fija `BAJA_RESIDENTE` y motivo de texto fijo de la aplicación (nunca el que escriba una persona), acción de auditoría `..._CANCEL_DISCHARGE` con la cuenta de Administración que dio la baja, y todo en la misma transacción. Se necesita **una causa nueva en el historial de la tabla de citas externas** (el `CHECK` actual no la tiene). **Las tareas de la familia sobre esa cita se cierran con ella.**
- **Qué pasa con los avisos:** ninguna cita cancelada por baja manda correo; los avisos al personal desaparecen al cambiar el estado; y la familia deja de ver al residente porque `AccessibleLinks` exige residente `ACTIVE` (línea 29 del mismo archivo).
- **Suspensión (hospitalización, ausencia temporal):** hoy no cancela las citas y el portal deja de mostrar al residente. **[Opinión]** Para las citas externas, las dejaría vivas (la cita es real) pero sin avisos a la familia (los corta la misma consulta) y con una marca visible para el personal «residente suspendido». **[Decidir CJ]**.
- **Solicitud de supresión de datos de un familiar (art. 17):** lo que se escribió **de** un familiar se resuelve con la rutina de `0087` (nombre, teléfono y correo) y la tabla de tareas no guarda nombre. Lo que se conserva (la cita del residente) lo es por obligación y por defensa de reclamaciones (art. 17.3.b y e), como en las otras tablas inmutables. **Bloqueo (LOPDGDD, art. 32):** sigue pendiente la decisión general del DPO real sobre «borrar o bloquear», que ya condiciona `0087` (`antes-de-datos-reales.md`, líneas 123 a 127).

### 6.4 Auditoría

**[Hecho]** Decisión general: las **lecturas** del personal asistencial no se auditan hoy (`antes-de-datos-reales.md`, línea 175). Propuesta para este módulo, en coherencia:

| Se audita (siempre, con solo identificadores) | No se audita |
|---|---|
| Crear, cambiar fecha o hora, cambiar estado, cancelar, reprogramar | Que alguien abra el calendario |
| Cambiar la visibilidad para la familia (nivel y marca por residente) y **mostrar** una especialidad oculta por defecto | El contenido de ningún campo |
| Anotar o cambiar la nota «al terminar» | |
| Acciones de la familia: tomar, no poder, soltar | |
| Cancelación por baja (patrón actual) | |
| **Leer la nota «al terminar» por un perfil clínico** | **Recomendación**, no obligación: seguir la decisión general; si se añade una pantalla de «versiones anteriores», auditar su lectura desde el primer día |

**Lo que lleva cada fila:** cuenta, perfil, centro, unidad, residente, tipo de recurso, identificador del recurso, acción, hora. Nada más. **El servidor no devuelve a una vista para Auxiliar los campos que no corresponden**; una prueba por reflexión sobre el modelo de esa vista comprueba que no existe propiedad de especialidad, lugar, nota ni nombre del familiar.

### 6.5 Entre centros y entre unidades

- La seguridad por filas aísla por **centro**; entre unidades lo hace la aplicación (ADR 0015, línea 29). **Pruebas obligatorias:** (1) un perfil del centro A no ve citas del centro B ni siquiera con su cookie de ámbito manipulada (el centro se toma del ámbito verificado, no de la cookie, ADR 0008 punto 5 recogido en ADR 0015, línea 31); (2) Enfermería o Medicina de otra unidad no las ve; (3) una familia vinculada en dos centros ve cada centro por separado; (4) Plataforma **no ve** citas externas (no ve residentes ni contenido clínico, ADR 0006).

---

## 7. La nota «al terminar»

**Riesgo:** una nota libre de unas líneas es el inicio de una segunda historia clínica, sin la autoría, la integridad ni los controles de la historia. CJ eligió la B: nota breve más la opción de crear la siguiente cita. **Admisible con estas condiciones:**

1. **No sustituye a la historia.** El informe del especialista y las decisiones clínicas siguen en lo que ya existe (Medicina, indicaciones). La nota solo avisa de **qué cambia para el centro**.
2. **Estructura, no prosa.** Una lista cerrada («sin cambios», «nueva cita», «pendiente de informe», «cambio de pauta», «traslado», «otra») más, **opcionalmente**, una línea de **150 caracteres** como máximo. La **siguiente cita** es un campo propio (fecha prevista o «en N meses») que no cuelga del texto.
3. **Ayuda visible:** «Anota solo qué debe hacer el centro («pendiente de informe», «revisión en 6 meses»). No escribas diagnósticos, resultados, dosis ni nombres. Las indicaciones clínicas las registra Medicina.»
4. **Un `<input>` de una línea**, no un `textarea`; la base de datos rechaza saltos de línea y lo que pase de 150.
5. **Quién la escribe y quién la lee:** Enfermería (y Medicina si CJ lo pide) la escribe; **la leen Enfermería y Medicina**. **No Administración** (Llei 21/2000, art. 11.4) ni Auxiliar, ni nunca la familia, ni va a correo ni a avisos ni a auditoría.
6. **Versionada y con autoría:** cada cambio es una fila nueva con cuenta y hora (Llei 21/2000, art. 9.3). No se edita ni se borra en sitio. Sin «anular por error» hoy: el mismo problema que la ficha de antecedentes (`antes-de-datos-reales.md`, línea 175, condición 1); o se implementa la anulación o el riesgo se acepta por escrito en la EIPD con procedimiento de corrección.
7. **Si el residente cambia de unidad o centro,** la nota va con la historia del residente, no con la unidad.
8. **Sin IA, sin dictado** en este campo (ADR 0011).

---

## 8. ¿Módulo base u opcional? Consecuencias de privacidad (sin decidir por Orlando)

### 8.1 Si es **opcional** (módulo del ADR 0015)

| Cuestión | Qué pasa |
|---|---|
| **Catálogo y ficha** | Entrada nueva (por ejemplo `CITAS_EXTERNAS`) con **ficha de privacidad versionada** y condiciones previas (ADR 0015, líneas 52 a 59; ejemplo ya hecho: `ficha-modulo-dictado-voz.md`). Borrador de ficha v1 en el apartado 11 |
| **Quién lo enciende** | Solo Plataforma, a petición del centro, con anexo del contrato, vigencia y versión de la ficha aceptada (ADR 0015, líneas 88 a 92). **Es la mejor forma de documentar la «instrucción del centro»** (art. 28.3.a) y de forzar la revisión de la EIPD (art. 35.11) **antes** de que el centro lo use |
| **Datos propios** | **Sí** (`HasOwnData = true`, a diferencia del dictado). Apagado o vencido ⇒ **solo lectura** (ADR 0015, líneas 77 a 80): se ocultan crear y editar; sigue la consulta y la exportación de lo registrado |
| **Bloqueo importante** | «Un módulo no guarda datos reales de residentes hasta que tenga definida su salida (consulta y copia)» (ADR 0015, línea 79, citando la lectura previa). **La exportación de hoy es solo estructura y censo**: este módulo no podría usarse con residentes reales hasta que exista la exportación de **sus** datos. Es un bloqueo general (fin de contrato, art. 28.3.g), pero aquí se vuelve explícito |
| **Qué significa «solo lectura» aquí** | Para el **personal**: se ve lo registrado, con un cartel «módulo desactivado el [fecha]: no se actualiza» (como la lectura previa propone para datos que dejan de actualizarse, `modulos-opcionales-lectura-dpo.md`, líneas 139 a 140). Para la **familia**: **no ve futuras citas** (un calendario que ya no se mantiene induce a creer que el transporte está cubierto); «Me encargo yo» desaparece. **Los avisos y correos se detienen**: son acciones, no lecturas. Las citas futuras **no se cancelan solas** (no se comunica nada clínico por apagar): Plataforma avisa por escrito al centro de que quedan citas por gestionar fuera de la aplicación |
| **Política de privacidad** | Puede decir «solo si el centro lo activa»: verdad con el incremento 6 del ADR 0015 |
| **Correo** | Es **base** (no módulo): se enciende por entorno, con un interruptor propio por plantilla además de `Correo__Activo`. El módulo no hace verdadera la frase «si el centro lo activa» para el correo (ADR 0015, punto 10); el interruptor por plantilla sí permite apagar estos avisos |

### 8.2 Si es **base** (todo centro lo tiene)

- **Sin ficha versionada, sin anexo, sin vigencia, sin solo lectura.** Pero **todos los centros reciben un tratamiento nuevo** (que la familia vea información sanitaria) sin que cada uno haya revisado su EIPD y su información. La instrucción del centro es el contrato general: hay que incluir la agenda de citas externas en su objeto desde el primer día y actualizar registro y EIPD antes de que ningún centro lo use.
- **El centro necesita aun así un interruptor propio** para la parte de la familia: «Citas externas para la familia: apagado por defecto» y «Avisos por correo de citas externas: apagado por defecto» (patrón de `citas_configuracion_centro`, modo desactivado). Es lo que exige el art. 25.2 (privacidad por defecto) y es la salida de un centro que no quiera esa visibilidad. **Sin ese interruptor, base es la opción menos prudente.**
- **La salida de datos** también hace falta (art. 28.3.g), pero no por regla del módulo sino general.
- **Dependencia de las otras piezas base:** Portal Familiar, avisos por correo, autorizaciones.

### 8.3 Mi recomendación (sin decidir por Orlando)

**Desde privacidad, prefiero «opcional».** Razones: (1) la activación es una **decisión documentada del responsable** y obliga a cumplir antes lo que importa aquí (EIPD, información a residentes y familias, procedimiento de voluntad del residente); (2) CJ dice que **puede esperar**, y un módulo que se activa en el centro que lo pide es más fácil de introducir en un piloto sin cargarlo a todos; (3) el diseño de «solo lectura» ya lo tiene resuelto el ADR. **Coste real:** el bloqueo de la exportación de sus datos (8.1) y que la familia no vea nada cuando se apague. Si se prefiere «base», **hay que añadir el interruptor por centro apagado por defecto** (8.2) y llevar las mismas condiciones a `antes-de-datos-reales.md` como lista de comprobación.

Una tercera vía **[Opinión]**: dos piezas, ambas dentro del mismo módulo: calendario del personal y avisos en pantalla (sin tercero ni familia) y, aparte y apagada por defecto en cada centro, la **parte de la familia** (calendario, botones y correo). Es lo más prudente porque el riesgo principal está en la familia, no en el personal.

---

## 9. Qué valida una persona antes de datos reales, y qué se puede construir ya

### 9.1 Se puede construir ya, con datos inventados y envíos apagados

- Tablas, permisos y pruebas (seguridad por filas, matriz de `0091`, catálogo de expurgo).
- Listas cerradas, longitudes y ayudas; el nivel de visibilidad por cita y por residente.
- Vistas por perfil con una prueba por reflexión de que la vista del Auxiliar no tiene los campos de especialidad, lugar, nota ni nombre del familiar.
- Tareas de la familia con asignación atómica, soltar y suelta automática por pérdida de acceso.
- Plantillas de correo (es y ca, marcadas como borrador) con interruptor propio, enviadas solo al sustituto de pruebas y a la lista de direcciones permitidas.
- Avisos en pantalla de inicio (cálculo al abrir, sin tabla de lecturas).
- Cancelación por baja con causa fija.
- **Datos de prueba inventados**: nada de especialidades de personas reales ni capturas con datos reales en el Manual.

### 9.2 Antes de usarlo con una familia real (una persona experta)

1. **Voluntad del residente.** Procedimiento de la residencia que recoge qué puede ver su familia y dónde se anota (4.3); y el criterio del DPO sobre «todas por defecto» (4.1).
2. **Base jurídica** del tratamiento de la cita (art. 6 y 9.2.h) y de los datos del familiar (6.1.f u otra).
3. **Contrato de encargado** con el objeto ampliado (o anexo de módulos si es módulo); registro de actividades y EIPD con el anexo del apartado 10.
4. **Información al residente o su representante y a cada familiar**, en castellano y, si se pide, catalán (apartado 10.3).
5. **Conservación** del dato del familiar y de la ficha (6.1), y la política de borrar o bloquear (LOPDGDD, art. 32).
6. **Lista de especialidades «ocultas por defecto»** (DPO y CJ).
7. **FAM-10** y el alcance del producto actualizados por escrito.
8. **Autoridad de control** (AEPD o APDCAT) y, si es público o concertado, el Esquema Nacional de Seguridad.
9. **Si es módulo:** la exportación de sus datos y las demás condiciones de la ficha (apartado 11).
10. El contrato de **Azure Communication Services**, B10 y DMARC en `quarantine` antes de enviar correos con direcciones reales (lo que ya consta en `antes-de-datos-reales.md`, apartado 7).

---

## 10. Borradores para adoptar por la residencia

### 10.1 Entrada del registro de actividades (art. 30)

**Parte A, la residencia (responsable):**

| Campo | Contenido propuesto |
|---|---|
| Nombre | Agenda de citas externas de residentes y coordinación con la familia |
| Finalidad | Registrar las citas sanitarias de un residente fuera del centro; organizar su preparación, transporte y acompañamiento; que la familia autorizada pueda ver lo que el residente permita y ofrecerse a ayudar; avisar al personal y a la familia de lo que falta por cubrir |
| Personas afectadas | Residentes (a menudo con capacidad disminuida o con apoyos); familiares y representantes con acceso al Portal Familiar; profesionales |
| Datos | De salud: tipo, especialidad, lugar, estado y nota breve de lo indicado; logísticos: fecha, hora, transporte, acompañante, qué preparar; del familiar: identificador, decisión de encargarse o no y fecha; técnicos: auditoría sin contenido |
| Base jurídica | **[Decidir DPO]** art. 6.1.b o 6.1.e (cita del residente) con 9.2.h y 9.3; 6.1.f (datos del familiar) |
| Destinatarios | Personal con permiso por perfil y unidad; familia autorizada, según el nivel de visibilidad; Azure (alojamiento y correo) como subencargados de ResidApp |
| Transferencias | Datos en Azure (UE preferida); correo: tránsito posible por otras geografías (`aviso-cita-por-correo.md`, apartado 2.6). **A verificar** |
| Conservación | Ficha y nota: la historia clínica del residente (15 años desde la baja por defecto, **a confirmar**); dato del familiar: plazo propio **[Decidir DPO]**; registro de envíos: 12 meses |
| Seguridad | Vistas por perfil y unidad con el servidor filtrando; seguridad por filas por centro; auditoría sin contenido; textos de correo sin datos; envío tras comprobar acceso; sin IA ni adjuntos ni calendarios externos |
| Autoridad de control | AEPD o APDCAT (apartado 1.5), **a verificar** |

**Parte B, ResidApp (encargada):** tratar estos datos por instrucción de la residencia; no usarlos para otro fin; no conceder acceso a Plataforma; devolver o suprimir al terminar el contrato (art. 28.3.g).

### 10.2 Anexo a la EIPD del centro: riesgos y medidas de este módulo

| Riesgo | Medida |
|---|---|
| Que un familiar vea una cita que el residente no quería contarle | Procedimiento de voluntad; niveles de visibilidad; interruptor por residente; valor por defecto por especialidad |
| Que el «lugar» o el «qué preparar» revelen la especialidad | Se tratan igual que la especialidad; lista cerrada y ayudas |
| Nota convertida en segunda historia clínica | Lista cerrada más 150 caracteres; solo perfiles clínicos; sin IA; versionada |
| Datos de terceros o diagnósticos en textos libres | Ayuda, límites en la base, procedimiento de corrección (el de «datos de otro residente») |
| Aviso por correo a destinatario equivocado o en mal momento | Texto sin datos; correo confirmado; comprobación al enviar; baja y suspensión |
| Pantalla de inicio o impresión visibles | Sin especialidad ni lugar; impresión solo con la vista del Auxiliar |
| Vigilancia de las familias («nunca puede») | Sin informes ni contadores por familiar |
| Calendario desactualizado en solo lectura | Cartel; la familia no ve citas futuras |

### 10.3 Textos de ayuda e información (castellano; catalán si se pide)

**Junto a «Me encargo yo» (familia):** «Si pulsas «Me encargo yo», el personal del centro verá tu nombre. Otros familiares solo verán que lo hará un familiar. El centro no te llamará solo por esto: las citas no son un canal urgente.» (La frase «el centro se pondrá en contacto contigo por teléfono» del boceto E, línea 625, **no debe aparecer** salvo que la residencia decida prometerla: la aplicación no organiza el centro.)

**En la pantalla de la cita (familia):** «Esta información la ve quien está autorizado a acompañar a [tu familiar] y solo en la medida en que él o ella lo permite. Si crees que no debería verla, avisa al centro.»

**Cláusula informativa al familiar (segunda capa, para que la residencia la adopte):** responsable ([RESIDENCIA], [DPO]); finalidad (agenda de citas y coordinación del transporte y del acompañamiento); base jurídica (interés legítimo u otra, **[Decidir DPO]**); datos (nombre e identificador, y tu decisión de encargarte); destinatarios (el personal del centro; tu proveedor de correo a través de ResidApp si activas los avisos); conservación (**[plazo]**); derechos y cómo ejercerlos, **incluida la oposición** de forma clara y separada si la base es el interés legítimo (art. 21.4); reclamación ante [AUTORIDAD]; y que, si no pulsas, no pasa nada.

**Junto al campo del correo (ampliación de la ya existente, `aviso-cita-cambio-cancelacion-por-correo.md`, apartado 7):** añadir «…y avisos sobre citas fuera del centro de la persona a la que cuidas, sin decir cuándo, dónde ni de qué».

**Información al residente o su representante (para la residencia):** qué datos de sus citas se registran, quién los ve en el centro, y que **su familia solo los ve si él (o quien le apoya) lo permite**; cómo cambiarlo. Revisar el catálogo de especialidades ocultas con CJ y el DPO.

---

## 11. Ficha de privacidad v1 del módulo `CITAS_EXTERNAS` (si es opcional) — borrador

**Estado:** borrador (2026-11-17). Lo valida una persona antes de activarlo en un centro con datos reales. Sigue el formato de `ficha-modulo-dictado-voz.md`.

- **Qué hace.** Registra las citas del residente fuera del centro, las muestra en un calendario por perfil, avisa de lo que falta por cubrir y, si el centro lo activa, permite a la familia autorizada ver la cita y ofrecerse a ayudar.
- **Qué datos trata, de quién.** Residente: tipo, especialidad, lugar, fecha, estado, qué preparar, nota breve de lo indicado. Familiar: identificador, decisión de encargarse. No guarda adjuntos, informes ni fotos.
- **Quién interviene.** Ningún tercero nuevo. Correo (Microsoft ACS) por el interruptor propio de la plantilla.
- **Quién accede.** Administración, Enfermería y Medicina por unidad; el Auxiliar solo hoy y mañana sin especialidad; la familia según nivel de visibilidad y voluntad del residente.
- **Datos propios (`HasOwnData = true`).** Al apagar o vencer: solo lectura para el personal con cartel; la familia no ve futuras citas; avisos y correos se detienen; las citas futuras no se cancelan solas y Plataforma avisa al centro.
- **Conservación.** Ficha y nota: la de la historia clínica del residente; dato del familiar y registro de envíos: plazos propios.
- **Salida de datos.** Exportación de las citas del residente al centro (consulta y copia). **Condición bloqueante antes de datos reales.**
- **Cómo se corta.** El módulo se apaga por Plataforma a petición del centro; los correos, con `Correo__Activo` y el interruptor de plantilla. No hay corte global dentro de la aplicación (decisión de Orlando del 2026-11-16).
- **Condiciones previas (códigos de `condiciones_codigos`; hoy ninguna cumplida):**

| Código | Condición |
|---|---|
| `ANEXO_FIRMADO` | Anexo de módulos del contrato firmado, como instrucción documentada del centro (art. 28.3.a), con plazo de apagado |
| `EIPD_CENTRO` | La EIPD del centro incluye el anexo de este módulo (10.2) |
| `INFORMACION_RESIDENTES_FAMILIAS` | Residentes o representantes y familias informados (10.3) |
| `PROCEDIMIENTO_VOLUNTAD_RESIDENTE` | La residencia tiene escrito cómo recoge qué puede ver cada familia |
| `SALIDA_DE_DATOS` | Existe la exportación de las citas externas |

---

## A verificar o decidir

**Orlando**
1. Módulo base u opcional (apartado 8). Mi inclinación, sin decidir por él: opcional, con la parte de la familia apagada por defecto en cada centro.
2. Si se acepta el **interruptor por residente** «la familia no ve las citas externas de esta persona» (4.3).
3. Si se añade «**Ya no puedo**» (soltar la tarea) y la suelta automática por pérdida de acceso (4.4).
4. Titularidad del centro piloto y plazas concertadas (1.5).

**CJ**
5. Los tres niveles de visibilidad por cita (4.2) frente a «siempre la especialidad»; si las especialidades ocultas por defecto son las del apartado 3.1.
6. Que la nota «al terminar» la lea solo personal clínico y no Administración (apartado 2 y 7); estructura cerrada más 150 caracteres.
7. Si el Auxiliar necesita el nombre de quien recoge al residente (apartado 2).
8. Acotar el aviso de 7 días a las citas en las que la familia tiene algo que hacer (5.2, punto 3.f); si Enfermería debe recibir el aviso de acompañante o ambulancia cuando también cambia citas (5.1).
9. Cómo se muestran las citas de un residente suspendido (6.3).
10. Que FAM-10 y el alcance constan actualizados (5.2).

**DPO real o abogado**
11. Base jurídica de la cita y del dato del familiar (1.3); si basta la disposición adicional 17.ª de la LOPDGDD o se apoya en el contrato asistencial.
12. Si la residencia es «centro sanitario» a efectos de la Llei 21/2000 y qué plazo corresponde a ficha y nota (6.1); plazo propio del dato del familiar.
13. Borrar o bloquear (LOPDGDD, art. 32).
14. Si basta el permiso tácito del residente para mostrar la especialidad a la familia con la excepción de las delicadas, o hay que pedir permiso expreso (4.1, 4.2).
15. Autoridad de control y ENS según el tipo de centro (1.5).
16. Que el registro, la EIPD y la información a residentes y familias (apartado 10) son suficientes.

**A verificar (no confirmado en texto literal)**
- Que `sw.js` no guarde respuestas del calendario en el dispositivo (5.1).
- Que la terminología de `es_tutor_legal` frente a la Ley 8/2021 (4.3).
- Llei 12/2007 de servicios sociales: no he buscado un artículo concreto sobre registro de actividades asistenciales ni derechos de información del usuario; **a verificar** si fija algo sobre agenda o acompañamiento.
- Que la regla 11 del Reglamento (UE) 2017/745 no alcanza a un calendario (1.4).

Fuentes consultadas el 2026-11-17: `normativa-local/` (rgpd, rgpd-considerandos, lopdgdd, ley-41-2002, llei-21-2000, llei-32-2010, ley-8-2021, mdr, aepd-lista-eipd; versiones y fechas en `normativa-local/INDICE.md`); repositorio (archivos y líneas citados). No he usado resúmenes de web.
