# Centro de Día como segunda modalidad de centro: lectura de protección de datos

**BORRADOR del agente `dpo-residapp`. NO está validado. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-21. Segunda lectura experta, no un dictamen. Es una lectura de la **fase 0** (análisis y consulta): no hay nada construido y no modifica código, esquema, ADR ni otros documentos. Rama `docs-centro-de-dia-alcance`, partida de `main` (75abe2b).

Etiquetas: **[Hecho]** verificado en el repositorio o en el texto literal de `normativa-local/` (con archivo y línea); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar en fuente oficial. No incluyo datos reales, ni país, ni titular, ni dirección de ningún centro. Los textos normativos vienen de la base local (`normativa-local/INDICE.md`, generada el 2026-11-20 21:41; menos de una semana). Son consolidados, no versión oficial; las leyes catalanas están en castellano (BOE) y el original catalán está en el DOGC. No he usado resúmenes de web.

Términos, en una frase cada uno. **Dato de salud**: cualquier dato que diga algo sobre la salud de una persona, incluida la atención que recibe; la ley lo trata como «categoría especial» y exige más garantías (art. 9 RGPD). **Responsable**: quien decide para qué y cómo se usan los datos (el centro). **Encargada**: quien los trata por cuenta del responsable y solo según sus instrucciones (ResidApp). **EIPD** (evaluación de impacto): análisis de riesgos por escrito que el responsable hace antes de empezar un tratamiento delicado. **RAT** (registro de actividades de tratamiento): la lista, que el responsable debe tener, de qué hace con los datos, para qué y por cuánto tiempo (art. 30). **Minimización**: guardar y enseñar solo lo necesario. **Inmutable**: una fila que, una vez guardada, ni se cambia ni se borra desde la aplicación; por eso purgarla exige un procedimiento aparte. **Bloqueo** (art. 32 LOPDGDD): reservar un dato sin usarlo, a disposición de jueces y autoridades, hasta que prescriban las responsabilidades. **Opt-in**: que la persona lo pida o lo active ella, en lugar de recibirlo por defecto. **Art. 14**: la obligación de informar a quien aparece en nuestros datos pero no nos los dio él mismo (por ejemplo, una persona autorizada a recoger a un usuario).

## Conclusión

**Admisible con condiciones por fases. Las fases 1 a 4 (modalidad, plazas, vocabulario, horario) no crean tratamientos nuevos de personas y se pueden preparar con datos de prueba. La fase 5 (parte diario) no es admisible tal como está planteada** («reutilizar el registro cotidiano del Auxiliar y `comunicaciones_familiares`»): hay una contradicción con los flujos clínicos que no resuelvo (apartado 0) y trae el riesgo nuevo más serio. Lo más importante:

1. **Contradicción con `docs/flujos-clinicos/` (aviso y paro).** El registro cotidiano del Auxiliar es un registro de **cambios observados** que va a **Enfermería**, no a la familia; el Auxiliar «no puede aprobar publicaciones»; y `comunicaciones_familiares` está atada a un **evento clínico** en el esquema. Alimentar a la familia con el registro del Auxiliar salta el circuito «evento → revisión → texto familiar aprobado → publicación». Lo decide CJ con Orlando (apartado 0).
2. **En un Centro de Día la persona vuelve cada tarde a casa, con quien recibe el parte.** Una lesión, una caída o una «sospecha» escritas en el parte llegan al hogar donde, si hay un problema de trato, puede estar el origen. El parte a la familia no es el registro clínico: debe preparar el mensaje un profesional y no puede copiar lesiones ni sospechas (apartado 3.2).
3. **Qué ve la familia depende de la voluntad del usuario** (Ley 41/2002, art. 5.1; Llei 21/2000, art. 3.1), y en un Centro de Día es más probable que el usuario conserve capacidad y que haya varios familiares con intereses distintos. Hace falta el mismo interruptor por persona que ya existe para las citas externas, y probablemente un nivel por familiar (apartado 2.4).
4. **El correo diario solo es admisible como aviso sin contenido** («tienes un aviso nuevo»), por una plantilla única y **solo a quien lo active** (opt-in). El alcance (`alcance.md:41`) y el ADR 0012 (línea 29) excluyen contenido sanitario por correo; no se puede extender «a todos los autorizados» como en las citas externas (apartado 3.3).
5. **Aparecen datos nuevos o con otro sentido**: asistencia, horas de entrada y salida, dietas (algunas revelan religión o creencias: otra categoría del art. 9), incidencias del día (a menudo con **terceros**: otros usuarios), personas que recogen (que **no dieron sus datos ellas** y exigen información conforme al art. 14), domicilio y transporte (apartado 1).
6. **Volumen y conservación.** Un parte inmutable por persona y día, con texto libre de hasta 2.000 caracteres, durante 15 años por defecto, no es proporcionado (art. 5.1.c y e). Hay que separarlo de `comunicaciones_familiares` y darle plazo propio (apartado 3.4).
7. **La columna `tipo_de_servicio` no es un dato personal** y su inmutabilidad es una garantía, pero **no es una instrucción documentada del centro por sí sola**: la modalidad debe constar en el alta o el contrato (art. 28.3.a), y las funciones de modalidad se comprueban en el servidor con el centro verificado, no con la cookie (apartado 3.1).
8. **Autoridad y seguridad.** Por su naturaleza (servicio social de atención diurna, a menudo público o concertado) es **más probable** que aplique la APDCAT (Llei 32/2010, art. 3) y el Esquema Nacional de Seguridad que en un centro privado puro. **[Suposición]**: falta la titularidad y los conciertos del piloto. Y una entidad con residencia y Centro de Día en el mismo edificio plantea una pregunta de diseño (apartado 2.6).

**Bloquean construir:** ver el apartado 4 (resumen: nada de la fase 5 sin ADR propio, especificación de CJ y una lectura mía del esquema; nada de correo diario sin FAM-10 y alcance actualizados por escrito; y ningún dato real de un Centro de Día sin las condiciones previas del apartado 4.3).

---

## 0. Contradicción detectada con `docs/flujos-clinicos/` (aviso; no la resuelvo)

La fase 5 del plan propone un parte diario por usuario «reutilizando el registro cotidiano del Auxiliar» y un resumen a la familia «reutilizando `comunicaciones_familiares`». Eso choca con lo que dicen los flujos y el esquema:

| Qué dice el repositorio | Dónde | Choque con el plan |
|---|---|---|
| El Auxiliar elige **una de tres acciones excluyentes**: «Sin cambios», «No valorable» o «Registrar cambio» | `docs/flujos-clinicos/registro-cotidiano-auxiliar.md:3`, `:65` | El parte de un Centro de Día es informativo y **positivo** (participó, comió); no es un «cambio». «Sin cambios» no significa «todo bien» y «No valorable» «nunca se interpreta como estabilidad» (`:24`) |
| El registro cotidiano **va a la bandeja de Enfermería**; la clasificación «no constituye un diagnóstico» | `registro-cotidiano-auxiliar.md:31-32`, `:61` | No hay ningún paso que lo lleve a la familia |
| El circuito es «registro cotidiano o evento → revisión de Enfermería → … → cierre → texto familiar → aprobación → publicación → consulta familiar» | `docs/flujos-clinicos/indice.md:13` | El parte diario saltaría la revisión y el cierre |
| El Auxiliar **no puede** «aprobar publicaciones» | `indice.md:48` | Quien rellena el parte en un Centro de Día es, casi siempre, el Auxiliar |
| `comunicaciones_familiares.evento_id` es `NOT NULL` y con índice único por evento; el texto es de hasta 2.000 caracteres e inmutable | `database/scripts/0009_enfermeria_cierre_evento.sql:128`, `:141`, `:145`; `FamilyCommunication.cs:29` | Un día normal **no tiene evento**; reutilizarla exige cambiar el esquema o crear otra tabla |
| Las áreas 8 («Participación / relación social»), 9 («Incidencias / caídas») y 10 solo tienen **texto libre obligatorio** | `DailyChangeAreaOptionsCatalog.cs:60-62`, `:67-69` | Copiar texto libre del Auxiliar a la familia es lo contrario de la minimización (apartado 3.2) |

**Para y decide CJ con Orlando** (preguntas 5, 6 y 13 del apartado 5): si el parte es una función **nueva** (con su propio flujo, perfiles y revisión) o un derivado del registro cotidiano. Lo que sigue (apartados 3.2 a 3.4 y 4) vale en ambos casos como condiciones de privacidad; **no propongo qué diseño elegir**, porque es un flujo clínico y operativo.

---

## 1. Datos nuevos o con otro sentido en un Centro de Día

| Dato | De quién | ¿Salud (art. 9)? | Observaciones |
|---|---|---|---|
| **Que la persona es usuaria de un Centro de Día** (y su pauta: días y jornada) | Usuario | **Sí, por inferencia** [Opinión] | Un servicio de atención diurna para personas mayores o dependientes (Llei 12/2007, cartera 1.2.2.1, línea 999; Ley 39/2006, art. 24, línea 327) revela dependencia o necesidad de apoyo. En una residencia ya ocurría con el hecho de residir; aquí además **fija un calendario** |
| **Asistencia diaria** (vino / no vino) | Usuario | Sí, con ese contexto | Es también la prueba de que se prestó el servicio (gestión, concierto público) |
| **Causa de la ausencia** | Usuario | **Sí si dice «ingreso», «enfermedad»…** | Minimizar: lista cerrada corta («avisó la familia» / «sin aviso» / «otra»). No pedir causa clínica |
| **Hora de entrada y de salida** | Usuario | No por sí sola | Dato de **rutina**: dice cuándo la vivienda queda vacía y cuándo vuelve a casa. Riesgo de seguridad física si lo ve quien no debe. Y es dato de gestión, no clínico |
| **Quién recoge** (nombre, parentesco, teléfono) y **lista de personas autorizadas** | **Terceros** (familiares, cuidadora contratada, vecino…) | No | **Dato de un tercero que no lo ha dado él** (art. 14 RGPD, líneas 175-203). Puede ser alguien sin cuenta en el portal. No pedir DNI ni foto |
| **Lista de personas a quienes NO entregar** | Terceros | No, pero **muy sensible** | Puede revelar conflicto familiar, medidas judiciales o violencia. Guardar solo el hecho («no autorizada»), nunca el motivo |
| **Comidas: ingesta** («comió todo / poco / nada») | Usuario | **Sí** | Ya es el área «Alimentación/hidratación» del registro cotidiano (`DailyChangeAreaOptionsCatalog.cs:11-17`) |
| **Dietas** (textura, diabética, sin sal…) y **alergias alimentarias** | Usuario | **Sí** | La ficha de alergias ya existe (`antes-de-datos-reales.md`, apartado 11) y no se duplica |
| **Dieta por motivos religiosos o de creencias** (halal, kosher, vegetariana por convicción…) | Usuario | **Sí, otra categoría del art. 9.1** («convicciones religiosas o filosóficas», `rgpd.txt:112`) | Un campo «tipo de dieta» puede acabar revelándolas. No hay que guardarlo como «religión»: solo la restricción alimentaria |
| **Participación y relación social** | Usuario | Sí, si describe conducta o ánimo | Hoy es texto libre obligatorio (ver apartado 0) |
| **Incidencias del día** (caídas, conducta, lesiones) | Usuario **y a menudo otros usuarios** | **Sí** | La causa de una incidencia suele ser otro usuario («se cayó jugando con X»). **Nunca nombrar a otras personas usuarias** |
| **Domicilio y datos del hogar** (dirección de recogida, con quién vive, cuidador principal) | Usuario y terceros | No (el domicilio), sí si describe su situación de salud o dependencia | En la residencia el domicilio es el centro; aquí es **la vivienda real**. No hay un campo de domicilio hoy; si se añade (transporte) es un dato nuevo y de rutina |
| **Transporte** (modo, ruta, hora, ayudas técnicas) | Usuario | Puede serlo (silla de ruedas, ambulancia) | Si lo presta una empresa, es **otro tratamiento y otro tercero**: ResidApp no integra listas de ruta con terceros |
| **Información que aporta la familia por la mañana** («durmió mal», «no tomó la pastilla») | Usuario, a través de un tercero | **Sí** | Es información de terceros (CTMDC, `ctmdc-informacio-tercers.txt:77-92`). El alcance excluye la mensajería libre (`alcance.md:40`) |
| **Quién ve el parte y cuándo** (auditoría) | Profesionales y familiares | No | Como en el resto: solo identificadores |

Definición legal: «datos relativos a la salud: datos personales relativos a la salud física o mental de una persona física, incluida la prestación de servicios de atención sanitaria, que revelen información sobre su estado de salud» (RGPD, art. 4.15, `rgpd.txt:57`).

**Lo que no hay que recoger** (condición de diseño): DNI o foto de quien recoge; motivo por el que no se entrega a alguien; posición o recorrido del usuario o del vehículo (Ley 39/2006, art. 14.7.c, línea 247: respeto a la privacidad «especialmente cuando se utilicen sistemas de sensores, monitoreo y recolección de datos»); identificación de los usuarios con pulsera NFC, huella o cámara para fichar la asistencia (un identificador biométrico del art. 9.1 o el ADR 0009 exigen ADR, EIPD y su propia lectura; no se plantea); y datos de convivientes que no son cuidadores.

---

## 2. Qué cambia respecto a la Residencia

### 2.1 Roles [Hecho: ADR 0007; el art. 28 RGPD no cambia]

- **Responsable: el centro.** **Encargada: ResidApp.** **Subencargados: los mismos** (Azure, Microsoft para el correo; Mistral solo si el centro activa el dictado). Un Centro de Día no añade ningún subencargado.
- **Lo nuevo** es que el objeto del contrato de encargado (art. 28.3: «el objeto, la duración, la naturaleza y la finalidad del tratamiento, el tipo de datos personales y categorías de interesados», `rgpd.txt:329`) debe incluir las categorías nuevas: asistencia, dietas, incidencias del día, **personas que recogen** (interesados nuevos) y, si procede, transporte.
- **Quien presta el servicio de transporte, si es una empresa, no es subencargado de ResidApp:** es un proveedor del centro. Que el centro le entregue listas es decisión suya, fuera de ResidApp.

### 2.2 Bases jurídicas y finalidades (propuesta; [Decidir] el DPO real)

| Tratamiento | Residencia (lo ya valorado) | Centro de Día |
|---|---|---|
| Asistencia y cuidados del usuario | Art. 6.1.b (contrato) o 6.1.e (público) con 9.2.h y 9.3 | Igual en el art. 6. **Pero el art. 9.2.h exige «Derecho de la Unión o de los Estados miembros» o «un contrato con un profesional sanitario»** (`rgpd.txt:124`): en un Centro de Día **sin profesional sanitario contratante** (servicio social), la base debe ser el Derecho (Llei 12/2007; Ley 39/2006) y no el contrato. La disposición adicional 17.ª LOPDGDD (línea 811) **no menciona** esas dos leyes (lista Ley 14/1986, 31/1995, 41/2002…). **[A verificar]** y, si no basta, **consentimiento explícito** (9.2.a) como alternativa |
| Asistencia diaria y horas (gestión del servicio, prueba de prestación) | No existía | 6.1.b o 6.1.e; si hay concierto con la Administración, puede ser también obligación legal o contractual de justificar la plaza (6.1.c). **[A verificar]** qué exige cada Administración |
| Parte diario a la familia | Comunicados por evento: 6.1.b + 9.2.h, y voluntad del paciente (Ley 41/2002, art. 5.1) | Igual, con **más peso de la voluntad del usuario** (apartado 2.4). El destinatario no es parte del contrato |
| Persona que recoge | No existía | **6.1.f (interés legítimo del centro en entregar a la persona correcta y proteger al usuario)**, con información art. 14 y derecho de oposición (art. 21). La ejecución de la voluntad del usuario o su representante puede encuadrarse también en 6.1.b |
| Correo diario | Solo avisos de citas | Opt-in; base: consentimiento (6.1.a) o interés legítimo con baja clara (art. 21.4). **[Decidir]** (apartado 3.3) |
| Transporte propio | No existía | 6.1.b |

### 2.3 Información a las personas (art. 12, 13 y 14)

- **Usuario o su representante (art. 13):** hoy la política dice «residente», «residencia», «habitación o plaza» (`Privacy.cshtml`: 26 líneas con «residencia» o «residente»; `politica-privacidad-portal.md:58`). La información debe ser «concisa, transparente, inteligible y de fácil acceso, con un lenguaje claro y sencillo» (art. 12.1, `rgpd.txt:139`). **Mantener «residente» como término del producto (decisión de Orlando) es admisible en pantallas internas; en los textos legales y en lo que ve la familia debe leerse «persona usuaria del centro de día» o «[CENTRO]»**, no «residencia». La Llei 12/2007 usa «persona usuaria» y fija el derecho a «información suficiente y comprensible» y a «información previa con relación a cualquier intervención» (art. 10.a y c, líneas 133 y 135). Hay que añadir: asistencia y horas, parte diario, quién ve qué, correo, personas que recogen, transporte.
- **Familiares (art. 13):** lo ya hecho para el Portal Familiar, más el parte y el aviso por correo.
- **Personas autorizadas a recoger (art. 14):** el centro las obtiene del usuario o su familia, no de ellas. Debe informarles «dentro de un plazo razonable, y a más tardar dentro de un mes» o «a más tardar en el momento de la primera comunicación» (art. 14.3, `rgpd.txt:194-195`), salvo que ya dispongan de la información o suponga «un esfuerzo desproporcionado» (14.5.a y b, líneas 199-200). **Propuesta práctica**: una hoja corta que entrega quien las apunta (la familia), con el texto de [RESIDENCIA/CENTRO]; la residencia decide cómo acreditarlo. No es código.
- **Medios para las personas con capacidad disminuida:** como en la Residencia; la Ley 8/2021 (apoyos) y la Ley 41/2002, art. 5.2, mandan informar «de modo adecuado a sus posibilidades de comprensión» (`ley-41-2002.txt:57`).

### 2.4 La familia o el tutor que ve información diaria

- **Regla** [Hecho]: «El titular del derecho a la información es el paciente. También serán informadas las personas vinculadas a él, por razones familiares o de hecho, en la medida que el paciente lo permita de manera expresa o tácita» (Ley 41/2002, art. 5.1, `ley-41-2002.txt:56`); y si no puede entender, la información se pone en conocimiento de las personas vinculadas (art. 5.3, línea 58). La Llei 21/2000, art. 3.1 y 3.3, es equivalente (ya citada en `citas-externas-lectura-dpo.md`, apartado 4.1).
- **Lo que cambia en un Centro de Día** [Suposición, la confirma CJ]: el usuario **conserva más capacidad** (muchos viven en su hogar y deciden), la información es **diaria** (no por evento), y **la familia es plural** (cónyuge mayor, hijos, una cuidadora contratada). Un parte diario sin la voluntad del usuario convierte la excepción en rutina.
- **Una cuidadora contratada** (persona que atiende al usuario en casa y lo trae y lo recoge) es habitual y plantea la duda de si es «persona vinculada de hecho». **[A verificar]** por la persona experta. Entre tanto, no se le da cuenta del Portal Familiar salvo decisión escrita del usuario o su representante.
- **Condiciones** (las mismas del interruptor de las citas externas, `antes-de-datos-reales.md`, apartado 14.1): (a) interruptor por usuario «la familia no recibe el parte»; (b) **un nivel por familiar** (hoy el interruptor es «todo o nada», apartado 14.1 punto 1, y en un Centro de Día es más probable que se necesite); (c) la parte de familia **apagada por defecto en cada centro** y con confirmaciones al encenderla (art. 25.2).
- **Riesgo de maltrato en el hogar:** como el usuario vuelve cada tarde, el parte nunca contiene lesiones, «sospechas», juicios sobre la familia ni textos que un profesional no pueda sostener. Las lesiones y sospechas se tratan por el protocolo del centro, no por el portal. **[A verificar]** con la persona experta si el centro, como servicio social, tiene deber de comunicar situaciones de riesgo o sospecha de maltrato y a quién (no está en la base normativa local).
- **Ley 8/2021:** la capacidad no se presume ausente; el apoyo atiende a la voluntad y las preferencias del usuario. Ver `citas-externas-lectura-dpo.md`, apartado 4.3.

### 2.5 Titularidad, autoridad de control y seguridad

El art. 3 de la Llei 32/2010 (`llei-32-2010.txt:26-38`) fija el ámbito de la APDCAT: «a) Las instituciones públicas», «b) La Administración de la Generalidad», «c) Los entes locales», «e) Las entidades de derecho privado que cumplan [...] uno de los tres requisitos» (capital, ingresos o órganos directivos mayoritariamente públicos), y «f) Las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios».

- **Lo que cambia con el Centro de Día:** la Ley 39/2006 prevé que el servicio de centro de día «se prestará a través de la oferta pública de la Red de Servicios Sociales [...] mediante centros y servicios públicos, o concertados, ya sean privados o de iniciativa social, debidamente acreditados» (art. 14.8, `ley-39-2006.txt:248`; el centro de día está en el catálogo, art. 15). Por eso, **[Suposición]**, un Centro de Día es más probable que sea público o concertado que una residencia privada pura, y por tanto que aplique el art. 3.f) de la Llei 32/2010 (APDCAT para los tratamientos «vinculados a la prestación» del servicio concertado). El proyecto supone «privado pequeño (AEPD, sin ENS)» (`antes-de-datos-reales.md:171`): **no vale sin comprobar** para un Centro de Día.
- **Esquema Nacional de Seguridad:** obligatorio para el sector público y, además, para entidades privadas que «en virtud de una relación contractual, presten servicios o provean soluciones a las entidades del sector público para el ejercicio por estas de sus competencias» (RD 311/2022, art. 2.3, `ens-rd-311-2022.txt:70`). Un Centro de Día **concertado** puede caer en este supuesto y trasladar a ResidApp requisitos de ENS (declaración o certificación de conformidad, `:72-73`). **[A verificar]** por un abogado con el concierto concreto.
- **Efecto en el diseño:** ninguno en el esquema; sí en el contrato (a quién se reclama, ENS) y en la cláusula ([AUTORIDAD]). **Dato que falta y que necesito:** titularidad del piloto y si hay plazas concertadas (pregunta 1 a Orlando).

### 2.6 Una entidad con residencia y Centro de Día [Decidir]

Es habitual que la misma entidad ofrezca ambos servicios, con personal y espacios compartidos. Con una modalidad por centro e inmutable, serían **dos centros distintos** en ResidApp (cada uno aislado por la seguridad por filas, ADR 0008). Consecuencias de privacidad: (a) el paso de un usuario del Centro de Día a la residencia (frecuente) sería un **alta en otro centro**: la historia no viaja sola; hace falta decidir si se exporta y con qué base (misma entidad responsable, pero dos instancias técnicas); (b) una cuenta de personal o de familia con acceso a los dos centros ya está contemplada, pero sus reglas están pendientes del DPO (ADR 0007, «cuentas en varios centros»); (c) el contrato y el RAT deben describir las dos actividades. No lo resuelvo; lo pregunto a Orlando (pregunta 2).

---

## 3. Por apartado

### 3.1 La columna `tipo_de_servicio`

- **¿Es un dato personal?** No. Describe al centro (una entidad), y el RGPD «no regula el tratamiento de datos personales relativos a personas jurídicas» (considerando 14, `rgpd-considerandos.txt:19`). Salvedad menor: si un centro lo explota una persona física, su nombre ya está en `nombre_visible` y la columna no añade nada.
- **RAT (art. 30):** la columna **no** crea una entrada. Sí la crean los tratamientos nuevos (asistencia y horas; parte diario y aviso a la familia; personas que recogen; transporte si procede), como se hizo con las citas externas (`citas-externas-lectura-dpo.md`, apartado 10.1). El RAT de ResidApp como encargada (art. 30.2) incorpora la modalidad como parte de la descripción del servicio.
- **EIPD:** el centro ya está en el supuesto del art. 35.3.b (gran escala de categorías especiales) y en los puntos 4 y 9 de la lista de la AEPD (`aepd-lista-eipd.txt:65-82`; «mayores con algún grado de discapacidad», «personas que acceden a servicios sociales»). **No exige una EIPD nueva de ResidApp, pero cada Centro de Día necesita la suya** (o el anexo de modalidad a la del centro; art. 35.1: «una única evaluación podrá abordar una serie de operaciones de tratamiento similares»), y el art. 35.11 pide revisarla cuando cambie el riesgo. Riesgos propios: apartado 4.2.
- **Contratos:** la modalidad debe constar en el **alta o el contrato** del centro: Plataforma solo ejecuta lo que el centro declara (art. 28.3.a), igual que la zona horaria (`zona-horaria-centro-lectura-dpo.md`, apartado 5, punto 3). Como **no es un módulo del ADR 0015**, **no hay anexo con vigencia, ni ficha versionada, ni apagado**: la instrucción documentada es el contrato general, que debe nombrar la modalidad y las categorías de datos de la modalidad (apartado 2.1).
- **Inmutable: lo veo bien**, porque cambiar la modalidad de un centro con datos cambiaría los tratamientos (finalidad, bases, plazos) sin pasar por contrato ni EIPD. **[Opinión]** que Plataforma pueda **corregir un error de alta solo mientras el centro no tenga ninguna persona usuaria**, con motivo cerrado y auditoría sin texto libre (patrón de `zonas_centro`); con datos, se crea un centro nuevo. **[Decidir]** Orlando.
- **Condiciones de código:**
  1. Default `RESIDENCIA`; restricción de la base de datos con los dos valores; **inmutable por disparador**, no solo por pantalla.
  2. **Las funciones de modalidad (parte, asistencia, recogida) se comprueban en el caso de uso con el centro verificado, nunca con la cookie del ámbito** (ADR 0015, líneas 31 y 110-114) y con una prueba por reflexión que falla si una acción de modalidad no la comprueba (el patrón de `[RequiereModulo]`).
  3. La seguridad por filas sigue siendo por centro: nada de la modalidad la relaja.
  4. La modalidad **no entra en la auditoría** como dato del residente ni en el correo.
  5. Una modalidad nunca habilita un tratamiento nuevo **por defecto**: lo clínicamente o familiarmente nuevo nace apagado en el centro.

### 3.2 El parte diario y su resumen a la familia

**Lo que veo admisible:** un resumen diario **preparado por un profesional** con listas cerradas, sin copiar lo clínico del Auxiliar, con la voluntad del usuario respetada, apagado por defecto en el centro y con la misma lógica de publicación, corrección y retirada que los comunicados actuales (FAM-03 a FAM-10: publicación inmutable, correctora vinculada, retirada excepcional; `portal-familiar.md:46-50`).

**Condiciones (valen para cualquier diseño; ver apartado 0):**

1. **Dos cosas separadas.** (a) El **registro operativo del día** (asistencia, comidas, participación): lo anotan quienes atienden; (b) el **parte a la familia**: un mensaje que prepara un profesional (o se genera con reglas fijas a partir de lo anotado) y que **pasa por la misma regla de la relevancia: «la decide un profesional, nunca el sistema»** (`portal-familiar.md:47`). La autoría visible sigue siendo «Equipo asistencial del centro» (`FamilyCommunication.cs:30`).
2. **Estructura, no prosa.** Listas cerradas por área (asistió; comió: todo / parte / poco / nada según defina CJ; participó en…); como mucho **una línea de texto libre de tope corto** (el precedente del proyecto son 80 a 150 caracteres en las citas externas y 100 en las fichas), con la ayuda visible «Escribe solo lo que se puede contar a la familia. No escribas nombres de otras personas, diagnósticos, lesiones ni sospechas». La base rechaza los saltos de línea y lo que sobre. **Prohibido el `textarea`** y el texto de 2.000 caracteres.
3. **Nunca terceros.** Ninguna incidencia nombra a otro usuario ni a un trabajador. Se anota «incidencia con otra persona» y el detalle va al registro clínico.
4. **Lo que no entra en el parte:** lesiones («hematoma», «herida», «piel enrojecida»), caídas con daño, sospechas, conducta en términos clínicos, medicación, diagnósticos, resultados. Eso sigue el circuito clínico y, en su caso, el teléfono. La lista cerrada de CJ debe mostrar **qué sí**, no qué no.
5. **Ausencia de parte no significa que todo fue bien** (`portal-familiar.md:36`) y el parte **no es un canal urgente**: frase fija «Este resumen no sustituye al contacto directo con el equipo del centro». No se promete una llamada del centro (la aplicación no organiza el centro; mismo criterio que `citas-externas-lectura-dpo.md:374`).
6. **Sin IA.** El ADR 0011 declara el dictado el «único caso de uso aprobado» (línea 38): un «resumen automático» con Mistral u otro modelo, o una tendencia («lleva tres días comiendo poco»), no se plantea sin ADR, contrato y EIPD. Un resumen **por reglas fijas** (juntar lo anotado) es otra cosa y es admisible. **Sin alertas ni cruces** (regla 11 del Reglamento (UE) 2017/745: ver `antes-de-datos-reales.md`, apartado 11, punto 6).
7. **Voluntad del usuario:** interruptor por usuario y, recomendado, por familiar (apartado 2.4), y el parte de un usuario con el interruptor apagado **no se envía ni se ve**, con comprobación en cada lectura (patrón del PR 5 de las citas externas).
8. **Tabla propia, no `comunicaciones_familiares`** (apartados 0 y 3.4): para poder darle un plazo corto y no heredar el `NOT NULL` del evento.
9. **Retirada y corrección:** como hoy (correctora vinculada y retirada con aviso), porque la ficha es inmutable.
10. **Auditoría solo con identificadores** (cuenta, perfil, centro, unidad, usuario, acción). Ni especialidad, ni texto, ni la comida en la auditoría.
11. **Lo que lee cada perfil** (propuesta; el servidor no envía lo que no corresponde, y una prueba por reflexión fija las propiedades de cada vista):

| Dato | Administración | Enfermería / Medicina (sus unidades) | Auxiliar (asignados) | Dirección | Familia |
|---|---|---|---|---|---|
| Asistencia (vino / no vino) y horas | Sí (gestión del servicio) | Sí | Sí | Agregados, sin nombres | Solo el parte de su usuario |
| Ingesta del día | **No** | Sí | Sí | No | Según el parte publicado |
| Dieta y alergias | **Solo la restricción**, sin diagnóstico, si hace falta para el servicio | Sí | Sí | No | No |
| Incidencias del día | **No** | Sí | Las que registra | Agregados | Solo lo publicado |
| Quién recoge | Sí | Sí | Sí (a la hora de entregar) | No | Su propio vínculo |
| Lista de no autorizados | Sí | Sí | Sí | No | No |

El criterio es el de la Llei 21/2000, art. 11.4 (`llei-21-2000.txt:117`): «El personal que se ocupa de las tareas de administración y gestión de los centros sanitarios puede acceder sólo a los datos de la historia clínica relacionados con las mencionadas funciones» (ya aplicado en `citas-externas-lectura-dpo.md`, apartado 2). La asistencia es gestión; la ingesta y las incidencias, no. **Una lista de dietas impresa para cocina** es una vía de fuga conocida: si se pide, solo restricción alimentaria, sin diagnóstico y sin nombre completo (otra decisión, con la misma cautela que la impresión de las citas).

**Qué hace el portal con el parte (propuesta):** visible los mismos 30 días que una publicación ordinaria (`FamilyPortalVisibility.cs:6-7`), con `Cache-Control: no-store` y sin datos en el título de la pestaña, la dirección ni un `aria-label`, como las citas externas.

### 3.3 El correo diario a la familia

**Admisible solo en esta forma:**

- **Texto único y sin contenido:** «Tienes un aviso nuevo en ResidApp» (más la frase de baja ya existente). **Ni nombre del usuario, ni del centro, ni «parte», ni «centro de día», ni hora, ni si hubo incidencia.** Dos plantillas («normal» e «incidencia») **no**: la diferencia ya revela salud (el ADR 0012 pide «ningún correo dice [...] el motivo ni nada de salud», línea 29; el alcance excluye «contenido sanitario por email, SMS, WhatsApp o enlaces públicos», `alcance.md:41`).
- **Solo a quien lo active él mismo (opt-in)**, con **baja desde el portal** sin pasar por Administración. Es distinto del aviso de citas externas (que llega a «todas las personas autorizadas con correo confirmado» si nadie se encargó, `antes-de-datos-reales.md`, apartado 14.2): un correo **diario** a todos es un volumen de unos 250 al año por persona, sin ninguna acción que lo justifique. Base propuesta: consentimiento (6.1.a, revocable) o interés legítimo con la oposición «presentada claramente y al margen de cualquier otra información» (art. 21.4, `rgpd.txt:264`); recomiendo el consentimiento por su claridad. **[Decidir]** DPO. La LSSI, art. 21 (`lssi.txt:236-237`), prohíbe comunicaciones **publicitarias o promocionales** no solicitadas: un aviso de servicio no lo es, **[A verificar]** por una persona experta.
- **Solo con la dirección confirmada** («sin la marca no sale ningún aviso», `antes-de-datos-reales.md:106`) y solo para **días en los que hay parte publicado** (nunca «no ha venido» ni «sin parte»).
- **Franja.** Hoy 10:00 a 18:59 en la zona del centro, fija en código y en dos procedimientos (`0092:59`, `0105:403`; `zonas-horarias-por-centro-estudio.md`, apartado 3.3). El cierre de tarde de 17:00 a 18:00 cabe en esa franja. **No hace falta moverla para el primer correo.** Si se hace configurable por centro: (a) **topes fijados en la base de datos** (el centro no puede poner una hora de madrugada: los valores los decide CJ/Orlando, no yo); (b) un script nuevo que **reemplace** los procedimientos, sin editar `0092` ni `0105`; (c) la hora local del centro, no la del servidor (ya hecho en F2 y F3 de zonas horarias).
- **Comprobación al enviar**, no solo al reservar: baja del usuario, suspensión, parte retirado, interruptor del centro o del usuario apagado, dirección o autorización ya no válida (como `0105`); clave de idempotencia con **día y familiar**; plantilla **añadida al cierre de avisos sin resolver (`0089`) y a la purga de 12 meses** (que no distingue plantilla); interruptor propio de la plantilla además de `Correo__Activo`.
- **Lo que el correo diario no hace:** **no avisa de ausencias ni de «no ha llegado»**. Eso es una **función de seguridad** (una persona con deterioro que sale de casa y no llega) y exige fiabilidad que un correo sin confirmación de entrega no da (el cierre de los pendientes deja `FALLIDO / RESULTADO_DESCONOCIDO`, `antes-de-datos-reales.md`, apartado 7). Si CJ lo quisiera, es otro ADR y otra lectura.
- **Antes de enviar uno real:** lo mismo que el apartado 7 de `antes-de-datos-reales.md` (contrato y configuración de Azure Communication Services, B10, DMARC en `quarantine`, entrega en Outlook.com, titular de ResidApp en el pie), **FAM-10 y el alcance actualizados por escrito por CJ** (hoy FAM-10 nombra propuesta, recordatorio y código; la puerta abierta «FAM-10 frente a correo opcional» sigue pendiente) y la actualización del ADR 0012 (tipos de aviso).

### 3.4 Conservación y supresión de la asistencia diaria y del parte

**[Hecho]** El plan de ResidApp: 15 años desde la baja para lo clínico; el art. 12 de la Llei 21/2000 dice que «debe conservarse [...] como mínimo durante quince años desde la fecha de alta de cada proceso asistencial» la documentación del apartado 4 (consentimientos, informes de alta y de exploraciones, etc.) y que el resto «puede destruirse» a los cinco años (art. 12.4 y 12.6, `llei-21-2000.txt:124`, `:133`). **[A verificar]** que un Centro de Día (servicio social de atención diurna, a veces sin profesional sanitario) cuente como «centro sanitario» a efectos de ese artículo: es la misma pregunta abierta de la residencia, **más débil aquí**; la Llei 12/2007 habla de «expedientes» y confidencialidad (art. 9.h, línea 130) y no fija plazo.

| Dato | Propuesta | Quién decide |
|---|---|---|
| **Asistencia diaria** (día, vino / no vino, causa cerrada) | **Plazo propio, no los 15 años por defecto** [Opinión]: mientras sea necesario para justificar la prestación (concierto) y atender reclamaciones. Una persona debe fijar la cifra; criterio de partida: el plazo de prescripción que interese (LOPDGDD arts. 72 a 74, citados en `antes-de-datos-reales.md`, apartado 13) y lo que exija la Administración concertante **[A verificar]** | DPO real y centro |
| **Hora de entrada y de salida** | **Más corto que la asistencia**: es dato de rutina, no clínico. Sin cifra mía | DPO real |
| **Parte a la familia** (el mensaje publicado) | **Plazo propio, más corto que lo clínico**, porque es una copia informativa; lo clínico de origen sigue su plazo. Si se reutilizara `comunicaciones_familiares`, heredaría el criterio actual (por defecto, el de lo clínico: `expurgo-y-supresion.md:71`) para **todos** los partes del día | DPO real y CJ |
| **Persona que recoge** y **lista de autorizados** | Mientras dure la autorización. Al revocarla o al terminar el servicio, borrar o bloquear como el familiar anonimizado (`0087`); **no guardar el nombre en otras tablas ni en la auditoría** más allá del identificador | DPO real |
| **Lista de no autorizados** | Solo el hecho, sin motivo; plazo como la persona que recoge | DPO real |
| **Registro de envíos del correo diario** | Los 12 meses de `envios_correo` (la purga no distingue plantilla) | Hecho |
| **Auditoría** | La de siempre (plazo sin decidir) | DPO real |

- **Proporcionalidad:** 250 partes al año por usuario durante 15 años, inmutables, con texto libre, no superaría el art. 5.1.e («no más tiempo del necesario»). Hay que **separar tablas por plazo**, como ya se hizo en las citas externas (`expurgo-y-supresion.md:73-76`).
- **Purga:** exige un procedimiento `EXECUTE AS OWNER` aprobado por el DPO, no quitar protecciones (`antes-de-datos-reales.md:165`). Las tablas nuevas deben entrar en el catálogo de expurgo desde el primer script (con una prueba que falla si falta una) y en la matriz de permisos de `0091`.
- **Altas y bajas más frecuentes:** un Centro de Día tiene estancias temporales y periodos de prueba; cada reactivación reinicia el cómputo desde la última baja (`expurgo-y-supresion.md`, apartado 1.4). **[A verificar]** con CJ la frecuencia real.
- **Derechos (art. 15):** el usuario o su representante puede pedir su historial de asistencia y de partes; sin pantalla de versiones anteriores, se atiende con una consulta manual que debe figurar en el procedimiento de derechos (`procedimiento-derechos-borrador.md`).
- **Borrar o bloquear (art. 32 LOPDGDD):** sigue pendiente la decisión general del DPO real (`antes-de-datos-reales.md`, apartado 8). El texto literal: «El responsable del tratamiento estará obligado a bloquear los datos cuando proceda a su rectificación o supresión» (apartado 1) y el bloqueo consiste en «la identificación y reserva de los mismos [...] para impedir su tratamiento, incluyendo su visualización» (apartado 2; `lopdgdd.txt:246-247`). Vale igual para la persona que recoge.

---

## 4. Qué consultar antes de construir cada fase y qué condiciones recomiendo

### 4.1 Por fase

| Fase del plan | ¿Datos o tratamientos nuevos de personas? | Qué consultar antes | Condiciones |
|---|---|---|---|
| **1. Modalidad** (`tipo_de_servicio`) | No (dato del centro) | Esta lectura basta. **Con datos de prueba, sin más validación mía** si se cumplen las condiciones del apartado 3.1. Una persona experta debe validar, antes de un Centro de Día real, lo del apartado 4.3 | Las cinco del apartado 3.1; ADR nuevo que diga «no es un módulo» y que la modalidad consta en el contrato |
| **2. Plazas sin fricción** | No, si solo oculta habitación y cama y no marca «ubicación pendiente» | Nada si es solo eso. **Si añade días o jornada de asistencia, es la fase 5a y se lee antes** | Que no cree campo nuevo («días contratados») sin pasar por aquí; mostrar solo la unidad o sala |
| **3. Vocabulario** | No, pero cambia lo que se **informa** | **Sí, necesita mi lectura de los textos de cara a personas**: `Privacy.cshtml`, `politica-privacidad-portal.md`, cláusulas, plantillas de correo, Portal Familiar. No necesito leer las 700 líneas internas | «Residente» puede quedarse en pantallas internas; en textos legales y en lo que ve la familia, «persona usuaria» o «[CENTRO]». El texto legal se parametriza por modalidad. Ninguna frase dice «residencia» a la familia de un Centro de Día |
| **4. Horario** | No, salvo el correo | **Solo si** se configura la franja de envío o se añade un correo: lectura breve mía del script que reemplaza `0092` y `0105`. **Los valores por defecto de la publicación y del margen según modalidad son operativos: los fija CJ** | Topes en la base; un script nuevo; sin tocar la lógica de comprobación al enviar |
| **5a. Asistencia y horas** | **Sí** | **Sí, antes del ADR y del script**: lectura mía del esquema | Apartados 1, 3.2 (tabla de perfiles) y 3.4; tabla propia; causa cerrada; sin domicilio |
| **5b. Parte a la familia** | **Sí** | **Sí**, después de que CJ decida el apartado 0 y defina qué se registra (preguntas 3 a 6, 13). Nada se inventa | Las diez del apartado 3.2; tabla propia; interruptor del centro apagado por defecto y por usuario |
| **5c. Correo diario** | **Sí** (volumen y destinatarios) | **Sí**, con FAM-10 y alcance actualizados por CJ y la actualización del ADR 0012 | Apartado 3.3 completo; opt-in; plantilla única |
| **5d. Quién recoge y transporte** | **Sí** (terceros) | **Sí**, aparte y después: es el tratamiento con más terceros | Apartados 1, 2.3 (art. 14) y 3.4; sin DNI, sin foto, sin motivo, sin geolocalización |

### 4.2 Riesgos que deben constar en la EIPD de un Centro de Día real (anexo; borrador)

| Riesgo | Medida |
|---|---|
| La familia recibe información que el usuario no quería contar | Interruptor por usuario y por familiar; apagado por defecto; procedimiento escrito de la voluntad del usuario |
| Una lesión o sospecha llega al hogar donde puede estar el origen | El parte no incluye lesiones ni sospechas; protocolo del centro; la persona experta confirma deberes de comunicación |
| El parte copia o duplica la historia clínica | Listas cerradas, una línea de tope, tabla propia, sin IA |
| Terceros (otros usuarios) en el texto de un parte | Ayuda visible, prohibición, procedimiento de corrección (el de «datos de otro residente») |
| Entrega de la persona a quien no debe | Lista de autorizados con procedimiento del centro; no es una función de ResidApp |
| Fuga por listas impresas (dietas, asistencia) | Sin impresión de listas con diagnóstico; si se pide, vista mínima |
| Rutina del usuario conocida por quien no debe (horas) | Horas solo para personal con permiso; nunca en el correo |
| Falsa seguridad («si pasara algo me avisarían por la app») | Frase fija: no es un canal urgente; ausencia de parte no es «todo bien»; sin alerta de «no ha llegado» |
| Correo diario a quien no debe o en mal momento | Opt-in, sin contenido, comprobación al enviar, baja y suspensión |
| Retención desproporcionada de datos diarios | Tablas separadas por plazo; procedimiento de purga aprobado |
| Información aportada por la familia y no verificada | Sin mensajería libre; si CJ lo pide, otra lectura (CTMDC: registrar solo lo necesario y citar la fuente con el detalle mínimo) |

### 4.3 Antes de usar un Centro de Día con datos reales (una persona experta)

1. **Base jurídica del art. 9.2.h para un servicio social** (apartado 2.2): Derecho aplicable o consentimiento explícito.
2. **Titularidad y conciertos del piloto**, autoridad de control (AEPD o APDCAT) y ENS (apartado 2.5).
3. **RAT** (entradas nuevas), **EIPD** del centro con el anexo del apartado 4.2, **contrato de encargado** con las categorías de la modalidad y **información a usuarios, representantes, familiares y personas que recogen** (art. 13 y 14), en castellano y, si se pide, catalán.
4. **Plazos** de asistencia, horas y parte (apartado 3.4) y la decisión general «borrar o bloquear».
5. **Procedimiento del centro** sobre la voluntad del usuario, la lista de autorizados y la comunicación de incidencias (por teléfono). Es de la residencia; ResidApp no regula la organización.
6. **Salida de datos al fin del contrato** (art. 28.3.g), que hoy no existe (`antes-de-datos-reales.md`, apartado 13): bloqueante general.
7. **Todo lo ya pendiente del resto de la aplicación** (apartados 1, 7, 8 de `antes-de-datos-reales.md`). Un Centro de Día no relaja ninguna de esas condiciones.

---

## 5. Preguntas abiertas, por destinatario

### Para Orlando

1. **Titularidad del centro piloto** (pública, concertada o privada) y si tiene plazas concertadas. Condiciona autoridad de control, ENS y contrato (apartado 2.5). Sin ese dato no cierro la lectura de la modalidad para datos reales.
2. **¿Una entidad con residencia y Centro de Día en el mismo edificio son dos centros de ResidApp?** Y qué se hace al pasar un usuario de uno a otro (apartado 2.6).
3. **¿Se puede corregir la modalidad de un centro sin personas usuarias, o es inmutable del todo?** Mi inclinación: corregible con motivo cerrado solo sin datos (apartado 3.1).
4. **¿El parte a la familia y el correo diario van como módulo opcional (ADR 0015, con anexo y ficha versionada) o como base apagada por defecto?** Mi inclinación, como en las citas externas: la parte de familia **apagada por defecto en cada centro**, con confirmaciones.
5. **¿Aceptas que el correo diario sea opt-in, de texto único y sin contenido, distinto del criterio «a todos» de las citas externas?** Si sí, CJ debe actualizar FAM-10 y el alcance por escrito y el ADR 0012 se amplía.
6. **¿Divides la fase 5 en 5a (asistencia y horas), 5b (parte), 5c (correo) y 5d (quién recoge y transporte)**, con mi lectura del esquema antes de cada una? Lo recomiendo.
7. **¿Descartas la alerta «no ha llegado»?** Mi recomendación es no construirla sin ADR propio (apartado 3.3).
8. **La persona experta (DPO real o abogado)** sigue sin existir (Orlando, 2026-11-02). Un piloto con un Centro de Día, probablemente concertado o público, la hace más necesaria; conviene empezar a buscarla ya.
9. **La contradicción del apartado 0** (parte diario frente a los flujos): decidirla con CJ antes de abrir la fase 5.

### Para CJ (preguntas clínicas y operativas, en lenguaje llano; las puede recoger `docs/pendientes-cj/`)

1. **Llegada y salida.** En un centro de día, cuando una persona llega y cuando se va, ¿qué hay que anotar? ¿Solo «vino» o «no vino», o también la hora de llegada y la de salida? ¿Quién lo anota?
2. **Si no viene.** ¿Hace falta anotar por qué no ha venido? Si sí, ¿basta una lista corta como «avisó la familia», «no avisó» y «otra», o necesitáis saber más (por ejemplo, que ha ingresado)?
3. **Comidas.** ¿Qué se anota de la comida? ¿Cuánto ha comido (todo, la mitad, poco, nada)? ¿Si necesita ayuda para comer? ¿Esto se cuenta a la familia o solo lo ve el equipo?
4. **Participación.** ¿Qué se quiere contar de las actividades del día (por ejemplo, «participó en taller de memoria»)? ¿Con una lista cerrada o con una frase?
5. **Qué es una «incidencia» para la familia.** ¿Qué cosas del día hay que decir a la familia por escrito a las 17:00 o 18:00 y cuáles solo de palabra (una caída, una herida, un cambio de conducta, un enfado con otra persona)? ¿Quién decide qué se escribe?
6. **Quién escribe el parte y quién lo revisa.** ¿Lo escribe la auxiliar, una enfermera u otro perfil? ¿Hay enfermería todos los días en un centro de día? Hoy la auxiliar no puede aprobar lo que se publica para la familia: ¿se mantiene en un centro de día?
7. **Algo importante a media mañana.** Si pasa algo a las 11:00, ¿cómo se avisa a la familia? (Se supone por teléfono, no por la aplicación.) ¿El resumen de la tarde debe repetirlo o solo remitir a la conversación?
8. **Si alguien no ha llegado.** Si a la hora de siempre no ha llegado, ¿qué hace el centro? ¿Llama a casa? (La aplicación no lo hará por correo.)
9. **Quién recoge.** ¿Solo la familia, o a veces una cuidadora contratada, un vecino, un taxi o una ambulancia? ¿Hay una lista de personas autorizadas? ¿Y personas a quienes no se debe entregar? ¿Qué se pide hoy de cada una (nombre, parentesco, teléfono, DNI)?
10. **Transporte.** ¿Lo hace el propio centro, una empresa o la familia? ¿Qué necesita saber el centro (silla de ruedas, hora de recogida, dirección)?
11. **Dietas.** ¿Qué tipos de dieta hay (de textura, diabética, sin sal, por religión o creencias)? ¿Quién las prepara y quién necesita verlas, por ejemplo cocina?
12. **Lo que cuenta el usuario sobre su familia.** ¿Cómo se pregunta a cada persona qué quiere que sepa su familia (y a quién no)? ¿Dónde se anota hoy?
13. **Qué perfiles hay en un centro de día** (auxiliares, técnicos de atención social, fisioterapeuta, terapeuta ocupacional, psicólogo, enfermería, medicina…) y **cuáles escribirían o leerían el parte**.
14. **Lo que trae la familia por la mañana** («durmió mal», «no se tomó la pastilla»): ¿hoy se anota? ¿Dónde? (La aplicación no tiene mensajes libres.)
15. **Cuánto tiempo ve la familia el parte.** Los comunicados ordinarios se ven 30 días y los relevantes 6 meses: ¿vale lo mismo para un parte diario?
16. **Estancias.** ¿Con qué frecuencia una persona empieza, se va unos meses y vuelve? ¿Y cuántas pasan después a la residencia?
17. **Quién usa el centro.** ¿Solo personas mayores, o también personas más jóvenes con discapacidad o con enfermedad mental? (La lectura cambia si hay menores o personas con discapacidad intelectual.)
18. **Aviso por correo.** Si una familia pide recibir un aviso cada tarde, ¿a qué hora debería llegar y qué días (solo los de asistencia)? (El texto no dirá nada del contenido.)

### Para la persona experta que valide (DPO real o abogado)

1. **Base del art. 9.2.h para un servicio social** (sin profesional sanitario contratante): ¿vale el Derecho (Llei 12/2007; Ley 39/2006) o hace falta consentimiento explícito? ¿Qué papel juega la disposición adicional 17.ª de la LOPDGDD, que no cita esas leyes?
2. **¿Un Centro de Día de mayores es «centro sanitario» a efectos del art. 12 de la Llei 21/2000**, y qué plazo corresponde a la asistencia, las horas y el parte? ¿Qué exige la Administración concertante?
3. **Persona que recoge:** base (6.1.f o 6.1.b), forma práctica de cumplir el art. 14 (incluido el «esfuerzo desproporcionado», 14.5.b), y cómo guardar la lista de no autorizados sin motivo.
4. **Correo diario:** base (consentimiento o interés legítimo), si el aviso de servicio escapa de la LSSI (art. 21), y si un texto único sin contenido es suficiente.
5. **Una cuidadora contratada:** ¿es «persona vinculada de hecho» (Ley 41/2002, art. 5.1)? ¿Qué debe escribir el usuario o su representante para que pueda ver el parte?
6. **Deberes del centro ante una sospecha de maltrato** y cómo se compagina con un parte que llega al hogar.
7. **Dietas por religión o creencias:** ¿alcanza el art. 9.1 («convicciones religiosas o filosóficas»)? ¿Basta guardar la restricción alimentaria sin su motivo?
8. **Autoridad de control y ENS** con el concierto concreto (AEPD o APDCAT; apartado 2.5).
9. **Entidad con residencia y Centro de Día:** dos responsables o uno; el paso de un usuario de un servicio al otro.
10. **Borrar o bloquear** (art. 32 LOPDGDD), ya pendiente.
11. **Nivel por familiar:** si hace falta de forma obligatoria (el interruptor «todo o nada» no basta).

---

## Qué debe validar una persona

Todo este documento es borrador. En particular: la base del art. 9.2.h (apartado 2.2), la autoridad y el ENS (2.5), el tratamiento de las personas que recogen (1, 2.3), el correo diario en forma de aviso sin contenido y con opt-in (3.3), los plazos de asistencia, horas y parte (3.4), el anexo de riesgos de la EIPD (4.2) y la lista del apartado 4.3. **No soy DPO designado ni abogado.**

## A verificar

- Si la disposición adicional 17.ª de la LOPDGDD (línea 811) ampara datos de un servicio social o solo los regulados por las leyes sanitarias que lista.
- Plazos de justificación que exige la Administración a un centro concertado y su relación con la asistencia diaria (no están en la base local).
- Si el art. 12 de la Llei 21/2000 alcanza a un Centro de Día.
- Deberes de comunicación ante sospecha de maltrato (no están en la base local).
- Si la LSSI, art. 21, no alcanza a un aviso de servicio (apartado 3.3).
- La terminología `es_tutor_legal` frente a la Ley 8/2021 (heredado de `citas-externas-lectura-dpo.md`).
- La cifra real de volumen: «unos 250 correos al año por persona» es mi cálculo para una asistencia de cinco días por semana, **[Suposición]**.
- Que un Centro de Día esté fuera de la regla 11 del Reglamento (UE) 2017/745 mientras no haya alertas ni cruces (hay que confirmarlo si se añaden).

## Fuentes consultadas el 2026-11-21

`normativa-local/` (versiones y fechas en `normativa-local/INDICE.md`): `rgpd.txt` (arts. 4.15, 5, 9, 12, 14, 21, 25, 28, 35), `rgpd-considerandos.txt` (14), `lopdgdd.txt` (arts. 28, 32 y disposición adicional 17.ª), `ley-41-2002.txt` (art. 5), `llei-21-2000.txt` (arts. 3, 11, 12), `llei-12-2007.txt` (arts. 9, 10 y cartera 1.2.2.1), `llei-32-2010.txt` (art. 3), `ley-39-2006.txt` (arts. 14, 15, 24), `ens-rd-311-2022.txt` (art. 2), `lssi.txt` (art. 21), `aepd-lista-eipd.txt` (puntos 4 y 9), `ctmdc-informacio-tercers.txt`. Repositorio: `docs/privacidad/antes-de-datos-reales.md`, `citas-externas-lectura-dpo.md`, `zona-horaria-centro-lectura-dpo.md`, `expurgo-y-supresion.md`, `politica-privacidad-portal.md`; `docs/producto/alcance.md` y `zonas-horarias-por-centro-estudio.md`; ADR 0011, 0012 y 0015; `docs/flujos-clinicos/registro-cotidiano-auxiliar.md` e `indice.md`; `docs/historias-usuarios/portal-familiar.md`; scripts `0009`, `0048`, `0051`, `0074`, `0092` y `0105`; `FamilyCommunication.cs`, `FamilyPortalVisibility.cs` y `DailyChangeAreaOptionsCatalog.cs`. El plan de trabajo de la fase 0 está en un fichero local no versionado; lo leído de él (el contenido de las fases 1 a 5) está resumido en el apartado 4.1.
