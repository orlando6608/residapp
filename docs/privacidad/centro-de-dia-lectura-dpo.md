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

**Actualización del 2026-11-21 (tarde): el piloto será un Centro de Día completamente privado, sin plazas concertadas.** Lo que cambia en la autoridad de control, el ENS y la base jurídica, qué debe decir el ADR 0017, qué debe llevar el script `0112` y la lista mínima antes de personas reales están en el apartado 6. Lo anterior no se ha borrado; el apartado 6.3 marca lo que cambia.

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

## 6. Piloto: Centro de Día completamente privado (añadido el 2026-11-21)

**Qué es esta sección.** Los apartados 0 a 5 se escribieron sin saber la titularidad del piloto. Orlando ha decidido (traspaso `docs/tareas/alta-prioridad/pendientes-centro-de-dia.md`, apartado 1) que el piloto será **un Centro de Día completamente privado, sin plazas concertadas**, y que el producto debe contemplar más adelante plazas mixtas (concertadas y privadas), que según él es lo habitual en España y Cataluña. **[A verificar]** ese resumen suyo: no lo he podido confirmar en fuente oficial. No borro nada de lo anterior: aquí digo qué sigue valiendo, qué pasa a «cuando haya plazas mixtas» y qué cambio. Alcance: fases 1 a 4 del plan, que no dependen de las respuestas de CJ y no recogen datos nuevos de personas. He colocado la sección antes de «Qué debe validar una persona» para que lo nuevo quede junto al resto del análisis.

Términos nuevos, en una frase cada uno. **Disparador** (*trigger*): regla guardada en la base de datos que se ejecuta sola cuando cambia una fila y puede rechazar el cambio, sea cual sea el programa que lo pidió. **Seguridad por filas**: filtro de la base de datos que hace que cada centro solo vea sus filas (ADR 0008). **Procedimiento con permisos del dueño** (`EXECUTE AS OWNER`): programa guardado en la base que se ejecuta con los permisos de su dueño y no con los de quien lo llama; sirve para hacer una sola cosa concreta que la aplicación no puede hacer por su cuenta. **Tabla de solo añadir**: tabla en la que nadie puede cambiar ni borrar filas desde la aplicación. **ENS**: Esquema Nacional de Seguridad, el conjunto de medidas de seguridad que la ley impone al sector público y a quien le presta servicios.

### 6.1 Lo que sé y lo que no sobre el piloto

- **[Decidido por Orlando, sin verificar]** Privado, sin plazas concertadas.
- **Lo que no sé y cambia la respuesta** (pregunto a Orlando en 6.8):
  1. **¿Cobra el centro, directa o indirectamente, dinero público?** «Sin conciertos» no es «sin relación con la Administración». La Ley 39/2006 prevé una prestación económica «vinculada al servicio» que se reconoce «únicamente cuando no sea posible el acceso a un servicio público o concertado» y que «estará, en todo caso, vinculada a la adquisición de un servicio»; las Administraciones «supervisarán, en todo caso, el destino y utilización de estas prestaciones» (art. 17.1 a 17.3, `ley-39-2006.txt:273-275`). Un centro privado puede, pues, tener usuarios que le pagan con esa ayuda y una Administración que pida justificar el servicio. También habría que saber si recibe subvención o convenio.
  2. **¿Tendrá profesionales sanitarios propios** (enfermería, medicina)? Condiciona la base del art. 9.2.h (que admite «un contrato con un profesional sanitario», `rgpd.txt:124`) y si la documentación asistencial se rige por la Llei 21/2000 (apartado 3.4).
  3. **Forma jurídica y autorización administrativa del centro.** La Llei 12/2007 exige que las entidades de iniciativa privada soliciten «la autorización o presenten la comunicación previa» y, si hay establecimiento, «la autorización, en todo caso» (art. 71.1, `llei-12-2007.txt:586`), con inscripción de oficio en el registro de entidades, servicios y establecimientos sociales (art. 71.4, línea 589). Un centro real ya debería estar ahí; no cambia la autoridad de control, pero es el primer dato del alta.

### 6.2 Autoridad de control y ENS con el piloto privado

Los dos temas se deciden por **quién es el centro y para quién presta el servicio**, no por el tipo de servicio. Por eso, en el producto, **el tipo (`RESIDENCIA` o `CENTRO_DE_DIA`) no debe codificar la titularidad ni los conciertos** (ver ADR 0017, punto 9, en 6.4).

| Pregunta | Texto | Piloto privado sin conciertos | Cuando haya plazas mixtas |
|---|---|---|---|
| **¿APDCAT o AEPD?** | Llei 32/2010, art. 3: la APDCAT actúa sobre los tratamientos de «a) Las instituciones públicas», «b) La Administración de la Generalidad», «c) Los entes locales», «e) Las entidades de derecho privado que cumplan, como mínimo, uno de los tres requisitos» (capital, ingresos o órganos directivos mayoritariamente públicos), «f) Las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios» (`llei-32-2010.txt:26-35`) | **[Opinión] AEPD.** Un centro privado autorizado no es, por sí mismo, parte del sistema público: «El sistema público de servicios sociales está integrado por [...] los que la Administración concierte con las entidades de iniciativa social o privada» (Llei 12/2007, art. 14.1, línea 183); entra en la Red de Atención Pública solo si está «acreditado» (art. 70.1, línea 579) y las entidades privadas «pasan a formar parte» de la Red cuando gestionan servicios públicos «mediante el establecimiento del contrato correspondiente» (art. 75.2, línea 613). Sin concierto, contrato ni acreditación no veo la letra f), y las letras a) a e) y h) exigen vínculo público. **[A verificar]** por la persona experta si la autorización, o los usuarios con prestación vinculada (6.1.1), bastan para la letra f) (lo creo improbable) | **Dos autoridades en un mismo centro es posible**: la letra f) dice «tratamientos vinculados a la prestación de dichos servicios», o sea, los de las personas con plaza concertada irían a la APDCAT y los de las privadas a la AEPD. Lo decide una persona experta. Para el producto: un solo responsable por centro, un solo RAT y una sola EIPD que lo reflejen; la política parametriza `[AUTORIDAD]` y no lo fija en código |
| **¿ENS?** | RD 311/2022, art. 2.1: «de aplicación a todo el sector público»; art. 2.3: se aplica también a entidades privadas que «en virtud de una relación contractual, presten servicios o provean soluciones a las entidades del sector público para el ejercicio por estas de sus competencias y potestades administrativas» (`ens-rd-311-2022.txt:68,70`). LOPDGDD, disp. adic. 1.ª.2: las medidas del ENS las aplican «los responsables enumerados en el artículo 77.1» (sector público) (`lopdgdd.txt:756`) | **No hay obligación que yo vea**: el centro no es sector público ni presta servicios a una entidad pública mediante contrato. **[Suposición]** sin conciertos, convenios ni contratos con una Administración; la prestación vinculada paga al usuario, no al centro, y no crea contrato (6.1.1; **[A verificar]**). Sigue valiendo el art. 32 RGPD (seguridad adecuada al riesgo) y el ENS como referencia voluntaria | La plaza concertada hace que el centro preste un servicio por cuenta de la Administración y trasladará a ResidApp requisitos del ENS (política de seguridad, declaración o certificación de conformidad: `ens-rd-311-2022.txt:70-73`). Si es **una sola instancia** para todas las plazas, el ENS afectaría probablemente a todo el sistema del centro. **[A verificar]**. Es un coste de ResidApp, no del diseño de los centros; no se prepara ahora |
| **¿Delegado de protección de datos obligatorio?** | RGPD, art. 37.1.c: cuando las actividades principales consistan en «el tratamiento a gran escala de categorías especiales de datos» (`rgpd.txt:442`); LOPDGDD, art. 34.1.l: «Los centros sanitarios legalmente obligados al mantenimiento de las historias clínicas de los pacientes» (`lopdgdd.txt:277`); el art. 34.2 admite designarlo de forma voluntaria | **[A verificar]**: un centro pequeño no parece «gran escala» (considerando 91: «una cantidad considerable de datos personales a nivel regional, nacional o supranacional», `rgpd-considerandos.txt:96`), pero si tiene autorización sanitaria y está obligado a llevar historias clínicas podría caer en el art. 34.1.l (depende de 6.1.2). La designación voluntaria es recomendable | Más probable cuanto más usuarios |
| **¿EIPD?** | AEPD: «será necesario realizar una EIPD en la mayoría de los casos en los que dicho tratamiento cumpla con dos o más criterios» de su lista (`aepd-lista-eipd.txt:38-41`); el criterio 4 (categorías especiales) y el 9 («mayores con algún grado de discapacidad [...] personas que acceden a servicios sociales», líneas 65-69, 79-82) se cumplen | **Sí, aunque el piloto sea pequeño**: el tamaño no la evita. El apartado 4.2 se reduce a los riesgos de las funciones que el piloto use | Igual, con los riesgos de cada función añadida |
| **¿Registro de actividades?** | RGPD, art. 30.5: la excepción para menos de 250 personas no se aplica si el tratamiento «incluya categorías especiales de datos» (`rgpd.txt:368`) | Hace falta, en el centro y en ResidApp | Igual |

**Qué sigue valiendo ya, también para el piloto**: (a) el método (leer el art. 3 de la Llei 32/2010 contra los hechos del centro) y la regla de que **la titularidad y los conciertos se piden en el alta o el contrato** de cada centro, porque deciden autoridad, ENS y contrato; (b) que el texto legal no fije la autoridad en código; (c) que la persona experta es necesaria (por datos de salud de personas vulnerables y criterios 4 y 9, no por la hipótesis de «centro concertado»).

**Qué pasa a «cuando haya plazas mixtas»**: la APDCAT para parte de los tratamientos; el ENS y la declaración de conformidad; la base 6.1.c o contractual para justificar la plaza ante la Administración; los plazos de conservación que fije la Administración concertante; la Administración como destinataria de datos (información art. 13.1.e) ; y un **dato nuevo por persona**, «plaza concertada o privada», que revela una prestación o un grado de dependencia reconocido (dato de salud por inferencia) y necesita su propia lectura antes de existir. No lo creo ahora; solo lo anoto para que el ADR 0017 no lo dé por hecho.

### 6.3 Qué cambio de lo anterior

| Apartado | Decía | Ahora, para el piloto |
|---|---|---|
| Conclusión, punto 8 | Más probable APDCAT y ENS; falta titularidad | **Cambia** para el piloto: AEPD y sin ENS obligatorio, como **[Opinión]** pendiente de 6.1. Se mantiene como regla general del producto |
| 2.2, fila «Asistencia diaria» | 6.1.b o 6.1.e; 6.1.c si hay concierto | **Cambia**: 6.1.b (contrato con el usuario o su representante). La 6.1.c queda para plazas concertadas; la prestación vinculada **[A verificar]** |
| 2.2, fila «Asistencia y cuidados» (art. 9.2.h) | El Derecho debe ser el de la Llei 12/2007 o la Ley 39/2006; la DA 17.ª no las cita; consentimiento como alternativa | **Se matiza y mejora**: añado la LOPDGDD, art. 9.2: los tratamientos del art. 9.2.h «fundados en el Derecho español deberán estar amparados en una norma con rango de ley» y esa norma «podrá amparar el tratamiento de datos en el ámbito de la salud cuando así lo exija la gestión de los sistemas y servicios de asistencia sanitaria y social, pública y privada» (`lopdgdd.txt:106-107`). La pregunta a la persona experta es más concreta: ¿la Llei 12/2007 «exige» ese tratamiento en un servicio privado autorizado? **[A verificar]** si una ley autonómica vale como «norma con rango de ley» del Derecho español. El consentimiento explícito sigue siendo alternativa, pero es débil si negarlo impide recibir el servicio **[Opinión]** |
| 2.5, primer párrafo | Un Centro de Día es más probable público o concertado (Ley 39/2006, art. 14.8) | Vale como regla general del producto; **no describe al piloto**, cuya titularidad ya se conoce |
| 2.5, «privado pequeño (AEPD, sin ENS) no vale sin comprobar» | No vale sin comprobar | **Para el piloto pasa a ser la hipótesis de trabajo**, con las tres comprobaciones de 6.1 |
| 3.4, «¿centro sanitario?» | Más débil en un Centro de Día | Sigue; depende de 6.1.2 (profesional sanitario y autorización sanitaria) |
| 4.3, punto 2 | Titularidad y conciertos, autoridad, ENS | Reducido a confirmar 6.1.1 a 6.1.3. Los puntos 1, 3, 4, 5, 6 y 7 **no cambian**. Añado: decidir si se designa delegado (6.2) |
| 5, pregunta 1 a Orlando | Titularidad | **Contestada** (privado, sin conciertos); sustituida por las de 6.8 |
| 5, pregunta 8 a Orlando | La persona experta es más necesaria porque «probablemente concertado o público» | La razón cambia, la necesidad no: datos de salud de personas vulnerables y criterios 4 y 9 de la AEPD |

### 6.4 Fase 1 (tipo de servicio) con el piloto privado

#### 6.4.1 Mis cinco condiciones del apartado 3.1

**Siguen en pie, y el piloto no relaja ninguna.** Que sea privado no cambia que `tipo_de_servicio` no es un dato personal ni que cambiarlo con datos cambiaría fines, bases y plazos. Lo que añade o precisa:

1. **Elección explícita en el alta.** Con el valor por defecto de la base (`RESIDENCIA`), un formulario que se olvidara del campo clasificaría mal un Centro de Día, y como el error solo se corrige con el centro vacío, descubrirlo con personas dadas de alta obliga a crear otro centro. Recomiendo que el formulario **no preseleccione** el tipo y muestre antes de confirmar «Tipo de servicio: [X]. No se podrá cambiar cuando haya personas». **[Opinión]**; es la pregunta 3 de 6.8.
2. **El centro reservado de Plataforma no es un servicio.** `0026` lo creó en `dbo.centros` (`0026_perfil_plataforma.sql:22-24`) y recibirá `RESIDENCIA` por defecto. Ninguna pantalla debe mostrarle etiqueta de tipo y el procedimiento de corrección debe rechazarlo (como `CENTER_TIMEZONE_CENTER_INVALID` en `0109:84-85`). Si no, «Plataforma · Residencia» aparecería en el selector de ámbito del operador.
3. **La modalidad consta en el contrato o el alta** (art. 28.3.a). El piloto privado no añade cláusulas de Administración, pero el contrato debe nombrar los dos tipos y qué categorías de datos corresponden a cada uno. Hoy **no hay parte contratante por el lado de ResidApp** (`antes-de-datos-reales.md`, apartados 7 y 13).
4. **Lectura siempre del centro verificado en la base**, no de la cookie del ámbito (ADR 0008, punto 5; ADR 0015, líneas 31 y 110-114). En la fase 1 solo afecta a la etiqueta (un fallo es de exactitud, art. 5.1.d); cuando una función dependa del tipo (fase 5), un fallo sería un tratamiento sin instrucción.
5. **Ninguna función por tipo nace encendida** y cada función nueva con datos de personas pasa por mi lectura del esquema. Esto es lo que impide que «Centro de Día» se convierta en un interruptor encubierto de 5a a 5d.

#### 6.4.2 Qué debe decir el ADR 0017

1. **Decisión:** columna `tipo_de_servicio` en `dbo.centros`, valores `RESIDENCIA` (por defecto, y el de todos los centros existentes) y `CENTRO_DE_DIA`; nombre elegido para no chocar con «modalidad» de cita ni con «tipo» de titularidad.
2. **Por qué no es un módulo del ADR 0015:** no se contrata, no se enciende ni se apaga; define la naturaleza del servicio, y cambiarla con datos cambiaría finalidades, bases jurídicas y plazos. Por eso no hay anexo, ficha versionada ni vigencia.
3. **Quién lo fija y con qué base:** Plataforma, en el alta, **según lo que declara el centro** en el alta o el contrato (RGPD art. 28.3.a). El centro no lo cambia desde ninguna pantalla.
4. **Inmutabilidad y corrección:** solo se corrige un error de alta, solo con el centro **sin ninguna persona** (ni residentes, de cualquier estado incluidas bajas y suspendidos, ni familiares), con motivo de una lista cerrada de un único valor (`ERROR_DE_ALTA`), sin texto libre y con auditoría solo de identificadores. Con personas, se crea un centro nuevo; **sus datos no se trasladan** (la historia no viaja entre centros: asunto abierto, 6.4.4 y apartado 2.6).
5. **Fuente de verdad:** la columna; el historial de solo añadir es constancia de quién y cuándo. Coherencia entre ambos garantizada por disparador y por prueba.
6. **Lectura y dependencias:** el tipo se lee del centro verificado en la base (nunca de la cookie), con un único ayudante. **Lista cerrada de lo que depende del tipo** en este ADR: etiqueta, ocultar habitación y cama, vocabulario, valores por defecto del horario. Cualquier función nueva por tipo (asistencia, parte, correo, quién recoge, transporte) exige ADR propio, lectura del esquema, interruptor apagado por defecto y comprobación en el caso de uso.
7. **Qué NO es ni cambia:** no autoriza ningún tratamiento nuevo; no cambia la seguridad por filas, los permisos de perfil, los subencargados ni quién es responsable.
8. **Visibilidad:** quién ve qué (tabla de 6.5), etiqueta **de texto** (no solo color) con un único ayudante, y **no aparece** en correos, títulos de pestaña, direcciones, cookies ni en la auditoría de ninguna persona.
9. **Titularidad y conciertos:** el tipo de servicio **no** los codifica ni los deduce. Si el producto necesita registrarlos (plazas mixtas), serán otro dato, con otro ADR y otra lectura (autoridad de control, ENS, dato de «plaza concertada» por persona, 6.2).
10. **Consecuencias para los papeles:** el contrato de encargado y el RAT nombran los tipos; la EIPD de cada centro lleva solo los riesgos de lo que el centro use.
11. **Verificación exigida:** pruebas de inmutabilidad, de permisos (6.4.3, punto 9) y de aislamiento; prueba por reflexión de que toda acción que dependa del tipo lo comprueba en el caso de uso.
12. **Asuntos abiertos, sin resolver en el ADR:** traslado entre un servicio y otro de la misma entidad (alta nueva, sin historial compartido; qué se exporta y con qué base); cuentas con perfiles en los dos centros (ADR 0007, pendiente del DPO real); un solo responsable o dos.

#### 6.4.3 Qué debe llevar el script `0112`

(Confirmar el número con `git fetch`: el último usado era `0111`.) Mismo patrón que `0109` (`0109_zona_horaria_centro.sql`) y `0090`/`0091`.

1. **Columna:** `ALTER TABLE dbo.centros ADD tipo_de_servicio NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_centros_tipo_servicio DEFAULT 'RESIDENCIA'` y `CONSTRAINT CK_centros_tipo_servicio CHECK (tipo_de_servicio IN ('RESIDENCIA','CENTRO_DE_DIA'))`. Collation binaria, como `estado` (`0002:71`). Los centros existentes quedan en `RESIDENCIA` por el valor por defecto; no se rellenan filas de historial para ellos (como en `0109:11`).
2. **Historial, tabla de solo añadir** (nombre propuesto `dbo.tipos_servicio_centro`): `id`, `centro_id` (FK a `centros`), `numero`, `tipo_codigo` (mismo CHECK), `motivo_codigo` con CHECK `IN ('ALTA','ERROR_DE_ALTA')`, `cambiado_por_cuenta_id` (FK a `cuentas`), `cambiado_por_perfil` con CHECK `= 'PLATAFORMA'`, `cambiado_en`. **Sin recuentos, sin texto libre.** CHECK: `ALTA` solo con `numero = 1`; `ERROR_DE_ALTA` con `numero >= 1`. Índice único `(centro_id, numero)`.
3. **Disparador de solo añadir** (`INSTEAD OF UPDATE, DELETE` que lanza error, como `TR_zc_immutable`, `0109:76-78`) y **disparador de validación** (`AFTER INSERT`, como `TR_zc_valid`): rechaza el centro reservado `PLATAFORMA` y exige que cada corrección **cambie de verdad** el tipo respecto a la fila anterior o, si no hay fila anterior (centro anterior a `0112`), respecto de `RESIDENCIA`.
4. **Seguridad por filas** en la tabla nueva, las tres piezas de `0109:95-98`: filtro `seg.fn_centro_o_plataforma`, bloqueo de inserción `seg.fn_estructura_escribe` (la autora es la cuenta del ámbito activo y solo perfil `PLATAFORMA`) y bloqueo de cambio `seg.fn_centro_del_ambito`. `dbo.centros` **sigue sin seguridad por filas** (`0068:12`); no se le añade.
5. **Permisos de la aplicación** (`residapp_aplicacion`): sobre la tabla nueva `GRANT INSERT` y `DENY UPDATE, DELETE` (como `0109:102-103`); sobre `centros`, **`DENY UPDATE (tipo_de_servicio)`**. Hoy la aplicación tiene `UPDATE` de la tabla entera (`0091:53`, `(N'centros', 1, 0)`) para cambiar el nombre; un `DENY` de columna gana al `GRANT` de tabla (`0091:14-16`). Así **la aplicación no puede cambiar el tipo por la vía ordinaria**, ni por un SQL manipulado. La prueba que compara la matriz de permisos de `0091` con los efectivos debe aprender esta excepción de columna.
6. **Procedimiento de corrección con permisos del dueño**, `dbo.corregir_tipo_servicio_centro` (única vía de cambio; solo `GRANT EXECUTE` a `residapp_aplicacion`, no a `PUBLIC`; ni `residapp_tareas`):
   - **Comprobar quién llama.** Como el procedimiento corre con los permisos del dueño, **se salta los bloqueos de la seguridad por filas** (`IS_MEMBER(N'db_owner') = 1` está en `0030:35`, `0090:37` y `0090:94`). El bloqueo de inserción que en `0109` garantizaba que solo Plataforma escribe **deja de proteger aquí**: el procedimiento debe verificar por su cuenta que `SESSION_CONTEXT('ambito_perfil_id')` es un ámbito activo de perfil `PLATAFORMA` de la cuenta indicada. Es la trampa principal de este diseño.
   - Rechazar el centro reservado, un centro inexistente y un tipo igual al actual.
   - **Comprobar que el centro está vacío con permisos del dueño**: no hay ninguna fila en `residentes` ni en `familiares` del centro, en cualquier estado. Con permisos de la aplicación la seguridad por filas devolvería cero filas a Plataforma, que no tiene ámbito en ese centro, y **parecería vacío un centro con personas** (mismo motivo por el que `recuento_citas_centro` usa permisos del dueño, `0109:106-119`).
   - **Impedir un alta concurrente**: bloquear la fila del centro y las lecturas de personas hasta confirmar. El alta de residente (`SqlResidentRepository.cs:50-59`) no bloquea la fila del centro, así que hace falta una **prueba de concurrencia** (alta de residente mientras se corrige) y vigilar los interbloqueos (precedente `0107`).
   - Insertar la fila de historial (número siguiente, motivo de lista cerrada) y cambiar la columna, **en la misma transacción** que el evento de auditoría que escribe la aplicación (`CENTER_SERVICE_TYPE_CHANGE`, solo cuenta, perfil `PLATAFORMA`, centro y recurso; como `SqlCenterTimeZones.cs:93-97`). **Ni el tipo ni el motivo en la auditoría**: están en la fila de historial.
   - **No devolver cifras**: ni cuántas personas hay ni de qué tablas. Solo éxito o un código de error cerrado (por ejemplo `CENTER_SERVICE_TYPE_NOT_EMPTY`). Una cifra sería innecesaria para decidir y ampliaría lo que Plataforma sabe de un centro.
7. **Disparador de guarda sobre `centros`** (nuevo, `AFTER UPDATE`; no tocar `0095`): si cambia `tipo_de_servicio` (comparación binaria), exige que la última fila de historial tenga ese tipo y que el centro esté vacío. Es la defensa en profundidad si alguien con permisos de dueño actualiza a mano, y es lo único que pararía el cambio si se retirara el `DENY`.
8. **Alta de centro** (no es SQL del script, pero el script lo presupone): el caso de uso que crea el centro (`SqlPlatformCenterRepository.cs:92`) inserta el centro con el tipo elegido y la **fila 1 `ALTA`** en la misma transacción, como ya hace con la zona (`SqlCenterTimeZones.InsertCreationAsync`). Una prueba comprueba que todo centro creado por la aplicación tiene su fila 1.
9. **Lo que NO se concede:** `residapp_tareas` no recibe nada (solo tiene permisos de columna sobre otras tablas y `centros` no está entre ellas: `0083:30-38`); una prueba comprueba que no puede leer la columna. Cuando la tarea de correo la necesite (5c) será otro script con su lectura.
10. **Cobertura y papeles:** la tabla nueva entra en la cobertura de seguridad por filas y en la matriz de permisos; la cuenta del operador en ella es un dato personal seudonimizado (como en `0109`, apartado 1 de `zona-horaria-centro-lectura-dpo.md`) y su **plazo de conservación queda sin decidir, junto al de `modulos_centro` y `zonas_centro`**.
11. **Una sola transacción**, con `SET XACT_ABORT ON`, como `0109`.

**Alternativa válida (más simple, algo menos estricta):** no poner el `DENY` y que la aplicación inserte el historial bajo la seguridad por filas, dejando que el disparador del punto 7 haga la comprobación del centro vacío llamando a una función con permisos del dueño. Evita la trampa de «comprobar quién llama» pero deja a la aplicación con permiso de escribir la columna. Prefiero el diseño de arriba (mínimo privilegio, art. 32 RGPD), y ambas dejan fuera de la aplicación el cambio sin centro vacío.

#### 6.4.4 Sobre «entidad con residencia y Centro de Día» y el piloto

Con la información que tengo, el piloto no comparte entidad con una residencia. Si la compartiera, el apartado 2.6 sigue igual: serían dos centros distintos y el `0112` no tiene que hacer nada especial, salvo que ninguna pantalla ni procedimiento mueva personas de un centro a otro.

### 6.5 Quién ve el tipo y si plantea problemas de privacidad o de seguridad por filas

| Quién | Qué puede ver | Qué NO debe poder leer |
|---|---|---|
| **Plataforma** | El tipo de cualquier centro (lista, ficha, cuentas con varios centros, vencimientos) y el historial de correcciones con la cuenta operadora | Nada de las personas del centro; **ni siquiera cuántas hay** al corregir (solo «tiene personas» o «no») |
| **Administración y resto del personal del centro** | El tipo de **su** centro; el evento de corrección en la auditoría del centro | El tipo y el historial de otros centros; el nombre de la cuenta operadora (se recomienda mostrar «Plataforma de ResidApp», `antes-de-datos-reales.md` apartado 19, para todos los eventos de Plataforma a la vez) |
| **Cuenta con perfiles en varios centros** | En el selector de ámbito, el tipo de **cada centro en el que tiene un ámbito activo** | El de centros sin perfil; no cruza datos: la seguridad por filas sigue yendo por el ámbito activo |
| **Familia** | Solo, para el vocabulario de la fase 3, el tipo del centro de cada residente al que está vinculada, leído por ese vínculo | Cualquier otro centro; el historial |
| **Tarea de correo** | Nada | La columna (sin `GRANT`) |
| **Visitante sin sesión** | Nada (la política de privacidad genérica no nombra ningún centro) | Cualquier tipo |

**¿Plantea algún problema?** **No de privacidad**: el tipo describe a un centro, no a una persona (considerando 14, `rgpd-considerandos.txt:19`) y el nombre del centro ya se muestra a esas mismas personas. Tres cautelas:

1. **Seguridad por filas:** `dbo.centros` no la tiene (`0068:12`) y la aplicación la lee uniéndola con los ámbitos de la cuenta (`SqlProfileScopeDirectoryProvider.cs:20-25`; `SqlPlatformCenterDirectory.cs:17-25`). Por tanto el tipo de cualquier centro es legible desde cualquier ámbito a nivel de base de datos. **Es aceptable porque no es dato personal, pero no debe usarse como frontera de seguridad.** Los datos de las fases 5a a 5d necesitarán su propia seguridad por filas por centro y, como defensa en profundidad, una regla que solo admita filas en centros de tipo `CENTRO_DE_DIA` (**[Opinión]**, se decide en su lectura).
2. **Una cuenta en los dos servicios de una misma entidad** (residencia y Centro de Día): la etiqueta **ayuda** a no actuar en el centro equivocado (art. 5.1.d, exactitud), pero no resuelve la pregunta pendiente de las cuentas en varios centros (ADR 0007) ni si es un solo responsable.
3. **Etiqueta junto a datos de personas:** que un listado de personas lleve «Centro de Día» revela el servicio que reciben. Solo aparece a personal y familiares que ya lo saben; **no debe salir en correos, títulos de pestaña, direcciones, cookies ni en la auditoría**, ni en documentos que la familia pueda reenviar sin necesidad. En las hojas de invitación y vinculación (`HojaInvitacion`, `HojaVinculacion`), que se entregan en mano, es admisible.

### 6.6 Fases 2 a 4: qué se puede hacer ya con el piloto

Las tres **no recogen datos nuevos de personas**: se pueden construir ya, con datos de prueba, sin esperar a CJ ni a la persona experta. Lo que cambia es qué se puede **mostrar a personas reales** (6.7).

| Fase | Se puede hacer ya | Condiciones |
|---|---|---|
| **2. Plazas sin fricción** | Ocultar «Habitaciones» y «Cama» en el alta y el menú; corregir «ubicación pendiente»; el rótulo «Sin habitación asignada» según el tipo; etiqueta de tipo en selectores e invitaciones | (1) **Ningún campo nuevo** («días contratados», «jornada» pasa a 5a). (2) Ocultar no borra ni migra nada. (3) La etiqueta sale del centro verificado en la base. (4) La unidad se muestra como «sala», sin datos de dónde vive la persona |
| **3. Vocabulario** | Construirlo por tipo, **primero lo que ve la familia y el alta**; «residente» se queda en pantallas internas (decisión de Orlando) | (1) Un único mecanismo (el del catalán), sin condicionales por las vistas. (2) **Antes de mostrar a personas reales**, la política de privacidad (`Privacy.cshtml`), las cláusulas y las plantillas de correo deben dejar de decir «residencia» en un Centro de Día: es transparencia (arts. 12.1 y 13). Lo leo yo cuando estén. (3) Texto legal como plantilla con `[CENTRO]` y `[AUTORIDAD]` (provisionalmente AEPD) |
| **4. Horario** | Valores por defecto por tipo (publicación a las 18:00 y margen de 0 a 30 min son una propuesta de ingeniería: **los fija CJ**) | (1) Se aplican **solo al crear la configuración** de un centro nuevo; **nunca reescriben la de un centro existente** (sería cambiar un tratamiento sin instrucción, art. 28.3.a). (2) No tocar la franja del correo: el correo diario es 5c. Si se hace configurable, lectura breve mía del script que sustituye `0092` y `0105` |

### 6.7 Lista mínima antes de dar de alta personas reales en un Centro de Día privado

Parte de que el piloto use **solo las funciones que ya existen** (registro cotidiano, eventos, comunicados a la familia con aprobación, etc.) y **no** la asistencia diaria, el parte, el correo diario ni la recogida (5a a 5d): esas tareas siguen en papel del propio centro, que es su responsable, y no son un tratamiento de ResidApp. Es una lista de mínimos: lo que ya consta en `antes-de-datos-reales.md` no se repite salvo para decir que sigue bloqueando.

**De una persona (no es código):**

1. **La persona experta existe y ha validado** los borradores que el piloto usa (hoy no existe: Orlando, 2026-11-02): base del art. 9.2.h para un servicio social privado (6.3), autoridad (AEPD), si se designa delegado (6.2), plazos. *[bloquea todo lo demás que sea jurídico]*
2. **Parte contratante de ResidApp** (sociedad titular) y **contrato de encargado** con el centro (art. 28.3): objeto, duración, naturaleza, finalidad, tipos de datos y categorías de interesados; **nombra los dos tipos de servicio** y los módulos apagados; anexo de subencargados (Azure; Mistral y Azure Communication Services solo si se encienden); **salida de datos al fin del contrato** (art. 28.3.g, que hoy no existe: bloqueante general).
3. **Registro de actividades** del centro (responsable) y de ResidApp (art. 30.2), con las líneas nuevas (zona horaria, tipo de servicio, módulos).
4. **EIPD del centro** (criterios 4 y 9 de la AEPD) con el anexo del apartado 4.2 **reducido a lo que el piloto use**.
5. **Información** (arts. 13 y 12.1) a personas usuarias o sus representantes, familias y personal, y política de privacidad validada y completa (hoy «Texto en revisión»; faltan identidad del prestador, datos del centro, autoridad, plazos, región de Azure y de Mistral) y **adaptada a «centro de día»**. Información laboral al personal con la asesoría que corresponda.
6. **Procedimientos escritos del centro** (los adopta el centro; ResidApp no regula la organización): voluntad del usuario sobre qué ve la familia (Ley 41/2002, art. 5.1), derechos (`procedimiento-derechos-borrador.md`), brechas (`protocolo-brechas-borrador.md`, art. 33), alta y baja de cuentas del personal.
7. **Decisión escrita «borrar o bloquear»** (LOPDGDD, art. 32) y **plazos de conservación** (clínico, `eventos_acceso`, sesiones, auditoría, `zonas_centro`, `modulos_centro`, `tipos_servicio_centro`), con la respuesta a si un Centro de Día es «centro sanitario» a efectos del art. 12 de la Llei 21/2000 (depende de 6.1.2).
8. **Confirmar los hechos de 6.1** (dinero público, profesionales sanitarios, autorización administrativa) y que consten en el alta del centro.
9. **El titular de ResidApp** en el pie del correo, solo si se enciende el correo (`antes-de-datos-reales.md`, apartado 7).

**De código o de infraestructura:**

1. **Entorno de producción.** La base de Azure es de desarrollo; con personas reales hace falta un entorno de producción (ADR 0014, aplazado), en región de la UE, con gestor de secretos y copias, **sin datos de prueba**, con `Privacidad:AnonimizarFamiliarActivo` a `false` y sin `Modulos__ActivarTodos`.
2. **Seguridad pendiente que no depende de la modalidad:** la clave TOTP en claro en `dbo.credenciales` (puerta abierta); si se abre el Portal Familiar, el segundo factor de familiares y FAM-10; decisión por escrito del plazo de `eventos_acceso` (la purga puede esperar al primer vencimiento, la decisión no).
3. **Fases 1 y 3** (tipo y textos legales y de cara a la familia y al alta) **hechas**: sin ellas la información a las personas sería inexacta. La fase 2 es recomendable (evita una «ubicación pendiente» falsa) pero no es de privacidad; la 4 no es necesaria.
4. **Lo que el piloto no use queda apagado** y no se enciende sin sus condiciones de `antes-de-datos-reales.md`: Portal Familiar y su correo (`Correo__Activo`), citas con la familia, dictado, pulsera.
5. **Las pruebas de aislamiento y de permisos en verde** (seguridad por filas, matriz de permisos, inmutabilidad), incluidas las de 6.4.3.
6. **Datos de prueba, capturas y ejemplos** del Manual y la Guía solo inventados.

**No se exige para el piloto** (queda para plazas mixtas o para 5a–5d): APDCAT, ENS, cláusulas de una Administración, dato de «plaza concertada», información art. 14 a quien recoge, EIPD de asistencia, parte, correo y recogida.

### 6.8 Preguntas que aún necesito de Orlando (una a una, cerradas)

1. **Dinero público.** ¿Alguna persona usuaria del piloto pagará el servicio con una ayuda pública (por ejemplo, la prestación vinculada al servicio de la Ley 39/2006), o el centro recibe subvención, convenio o contrato de una administración? (A) Ninguna ayuda ni subvención. (B) Algún usuario con prestación vinculada, pero el centro no tiene contrato. (C) El centro recibe subvención o convenio. (D) No lo sé todavía.
2. **Profesionales sanitarios.** ¿El centro tendrá enfermería o medicina propia (de plantilla o contratada)? (A) Enfermería y medicina. (B) Solo enfermería. (C) Ninguna: servicio social sin profesional sanitario. (D) No lo sé todavía.
3. **Alta de centro.** ¿El formulario de alta de centro debe obligar a elegir el tipo de servicio, sin valor preseleccionado, y mostrar una confirmación antes de crear el centro? (A) Sí, es mi propuesta. (B) No, preseleccionar «Residencia».

### 6.9 Qué debe validar una persona, a verificar y fuentes de esta sección

**Qué debe validar una persona (además de lo del apartado «Qué debe validar»):** que el piloto privado sin conciertos queda bajo la AEPD y fuera del ENS (6.2) y qué hechos lo desmentirían (6.1); la base del art. 9.2.h para un servicio privado autorizado con el art. 9.2 de la LOPDGDD (6.3); si debe designarse delegado (6.2); el diseño del `0112` (6.4.3), en especial la comprobación de quién llama al procedimiento con permisos del dueño y el centro vacío; y la lista mínima de 6.7.

**A verificar (nuevo):**
- Que una prestación vinculada al servicio, una autorización administrativa o una subvención no hagan aplicable la letra f) del art. 3 de la Llei 32/2010 ni el art. 2.3 del RD 311/2022 (6.2).
- Si una ley autonómica (Llei 12/2007) vale como «norma con rango de ley» del art. 9.2 de la LOPDGDD (6.3).
- Que `HOLDLOCK` o el bloqueo de la fila del centro basten para impedir un alta de residente concurrente con la corrección del tipo, en Azure SQL (6.4.3, punto 6): se decide con la prueba de concurrencia, no con la teoría.
- Que Azure SQL acepte `DENY UPDATE` de columna sobre `dbo.centros` junto al `GRANT` de tabla con el mismo resultado que en `0091` (ingeniería).
- Lo que dice Orlando sobre «plazas mixtas lo habitual en España y Cataluña» (6.1).
- Que un centro pequeño no es «gran escala» a efectos del art. 37.1.c (no tengo la guía WP243 en la base local).

**Fuentes de esta sección (consultadas el 2026-11-21 en `normativa-local/`, texto consolidado, no oficial; las leyes catalanas están en castellano, versión BOE):** `llei-32-2010.txt` (art. 3), `llei-12-2007.txt` (arts. 14, 15, 70, 71, 75), `ley-39-2006.txt` (arts. 14.8 y 17), `ens-rd-311-2022.txt` (arts. 2 y 3), `lopdgdd.txt` (arts. 9, 34; disp. adic. 1.ª y 17.ª), `rgpd.txt` (arts. 9.2.h, 30.5, 37.1), `rgpd-considerandos.txt` (considerando 91), `aepd-lista-eipd.txt`. Repositorio: `docs/tareas/alta-prioridad/pendientes-centro-de-dia.md`; `docs/privacidad/antes-de-datos-reales.md` (apartados 7, 8, 13, 14, 17 a 19) y `zona-horaria-centro-lectura-dpo.md`; scripts `0002`, `0026`, `0030`, `0068`, `0083`, `0090`, `0091`, `0095`, `0109`; `SqlProfileScopeDirectoryProvider.cs`, `SqlPlatformCenterDirectory.cs`, `SqlPlatformCenterRepository.cs`, `SqlCenterTimeZones.cs` y `SqlResidentRepository.cs`.

---

## Qué debe validar una persona

Todo este documento es borrador. En particular: la base del art. 9.2.h (apartado 2.2), la autoridad y el ENS (2.5), el tratamiento de las personas que recogen (1, 2.3), el correo diario en forma de aviso sin contenido y con opt-in (3.3), los plazos de asistencia, horas y parte (3.4), el anexo de riesgos de la EIPD (4.2) y la lista del apartado 4.3. **No soy DPO designado ni abogado.**

Para el piloto privado, además: apartado 6.9 (lo que valida una persona y lo nuevo a verificar).

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
