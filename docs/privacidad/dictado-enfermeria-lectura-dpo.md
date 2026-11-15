# Dictado por voz en Enfermería: lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-15. Segunda lectura experta del agente `dpo-residapp`, no un dictamen ni una designación de DPO. Es una consulta **previa a cualquier ADR o código**: no se ha decidido ni construido nada. No modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado en el repositorio o en el texto literal de la base normativa local (`normativa-local/`, índice del 2026-11-14; un texto consolidado no es la versión oficial); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar en fuente oficial. No contiene datos reales.

## Conclusión

**Viable con condiciones, y por fases.** La opción que recomiendo como primer paso es la **variante (a)**: transcribir lo dicho con Voxtral y dejar el texto en el campo con el foco, **sin segundo modelo**, empezando por la pantalla Valoración (campos de texto) y con las constantes en una fase posterior y aparte. La **variante (b)** (un modelo que reparte lo dicho entre los cinco campos) **no la recomiendo ahora**.

El riesgo más importante no es jurídico sino de **exactitud del registro clínico**: un error de transcripción en un texto de Enfermería, o en una cifra, acaba en la historia clínica y puede llegar a Medicina en un escalado. Todo lo demás (contrato con Mistral, EIPD, información) ya es una condición abierta para el dictado de Auxiliar; ampliar a Enfermería **no crea un problema jurídico nuevo de fondo, pero sí agranda los que ya había** y añade dos puntos que en Auxiliar no existían: texto clínico libre más denso y un campo (Registrar evento, Observación) que **no se puede corregir una vez guardado**.

Nada de esto cambia el estado actual: mientras no haya contrato con Mistral que cubra datos de salud, retención cero aprobada, EIPD y una persona experta que valide, el dictado (de cualquier perfil) solo se usa con audios y textos inventados (`docs/seguridad/dictado-por-voz-dossier-dpo.md:3-4`).

## 1. Lo que hay hoy (hechos del repositorio)

- **[Hecho]** El dictado solo existe para Auxiliar: el caso de uso rechaza cualquier otro perfil (`src/ResidApp.Application/UseCases/SuggestDailyChangeFromVoice.cs:63`, `scope.Profile != SystemProfile.Auxiliar`). El ADR 0011 lo declara «el único caso de uso aprobado» (`0011-arquitectura-ia.md:38`) y dice que «lo que no recoja este ADR no está aprobado» (línea 22).
- **[Hecho]** Flujo actual: audio, servidor, Voxtral, Mistral Small con esquema, sugerencia revisada en pantalla; no se guarda audio ni texto; auditoría `VOICE_DICTATION_USED` escrita **antes** de enviar el audio; no se envía el nombre del residente (`0009-nfc-voz-acceso-fisico.md:26-51`; `SuggestDailyChangeFromVoice.cs:75-77`).
- **[Hecho]** El modelo de Auxiliar casi solo elige opciones de un catálogo cerrado, pero ya admite **texto libre por área** (hasta 1000 caracteres, `SuggestDailyChangeFromVoice.cs:35`) y su instrucción manda «sin nombres de personas» (`Prompts/registrar-cambio.md:5, 7`). La diferencia con Enfermería es de **grado**, no de naturaleza.
- **[Hecho]** Pantalla Valoración: cinco textos libres (Hallazgos, Valoración, Actuaciones, Comunicaciones, Resultado, hasta 2000 caracteres cada uno: `Models/EnfermeriaModels.cs:472-490`; `NursingAssessment.cs:81`) más constantes. Se guarda como **borrador** (`Valoracion.cshtml:87`), editable hasta decidir el desenlace.
- **[Hecho]** Pantalla Registrar evento: Observación (hasta 2000) y datos clínicos pertinentes; la pantalla dice «La observación no se puede editar una vez guardada» (`RegistrarEvento.cshtml:20-23`; `EnfermeriaModels.cs:159-169`).
- **[Hecho]** El flujo clínico prohíbe el resumen diagnóstico automático en el escalado (`docs/flujos-clinicos/valoracion-escalado-enfermeria.md:29, 50`).
- **[Hecho]** El ADR 0009 ya fija el modo estricto para valores clínicos dictados: «cada valor clínico rellenado por voz exige revisión individual y el botón de guardar permanece desactivado hasta que todos estén revisados» (`0009:42-43`) y que la confirmación se hace «siempre en pantalla» (líneas 20-22).
- **[Hecho]** Rango de temperatura del dictado de Auxiliar fijado por CJ el 2026-10-25: de 34,0 a 42,0 °C; fuera de él no se sugiere (`Domain/Auxiliar/DailyChangeTemperature.cs:3-12`). Es un precedente directo para las constantes.
- **[Hecho]** El dossier para el DPO sigue sin respuesta y tiene abiertas: cobertura del DPA con datos de salud (el anexo dice «categorías especiales de datos: ninguna»), entrenamiento, retención cero, región, subencargados, versión vigente del DPA (`dictado-por-voz-dossier-dpo.md:70-81`).

## 2. Base jurídica y proporcionalidad (pregunta 1)

**¿Cambia la base jurídica por ser texto clínico libre?** No. La base es la misma que la del resto del registro asistencial:

- Art. 9.1 RGPD: «Quedan prohibidos el tratamiento de [...] datos relativos a la salud [...]»; se levanta con el art. 9.2.h: «el tratamiento es necesario para fines de [...] diagnóstico médico, prestación de asistencia o tratamiento de tipo sanitario o social, o gestión de los sistemas y servicios de asistencia sanitaria y social, sobre la base del Derecho de la Unión o de los Estados miembros [...]», y 9.3: lo trata «un profesional sujeto a la obligación de secreto profesional, o bajo su responsabilidad». Enfermería lo cumple.
- LOPDGDD, disposición adicional 17.ª, 1: amparan el art. 9.2.h los tratamientos de salud regulados, entre otras, en «c) La Ley 41/2002» (RGPD consolidado a 2016-05-04 y LOPDGDD, BOE act. 2026-10-08).
- La Ley 41/2002, art. 15.2, incluye en el contenido mínimo «m) La evolución y planificación de cuidados de enfermería», «n) La aplicación terapéutica de enfermería» y «ñ) El gráfico de constantes». Lo que Enfermería dicta es, por tanto, **historia clínica**, y la Llei 21/2000, art. 9.3, exige que «quedan registrados todos los cambios e identificados los médicos y los profesionales asistenciales que los han realizado» (texto del BOE en castellano; el original catalán está en el DOGC).

**Lo que cambia no es la base sino la necesidad y la proporcionalidad (art. 5.1.c).** El art. 5.1.c exige datos «adecuados, pertinentes y limitados a lo necesario». El dictado **no es necesario** para prestar la asistencia: es una comodidad (manos ocupadas, rapidez). Eso no lo hace ilícito, pero obliga a justificarlo en la EIPD (art. 35.7.b: «una evaluación de la necesidad y la proporcionalidad de las operaciones de tratamiento con respecto a su finalidad») y a mantener siempre el camino manual. **[Opinión]** La proporcionalidad se sostiene si el beneficio es real y medido (tiempo ahorrado, fiabilidad) y si el tercero no recibe más de lo imprescindible.

**¿Más riesgo de identificar al residente por el contexto, sin decir el nombre?** Sí, algo más. Razones:

1. **[Hecho]** El art. 4.1 RGPD define como identificable a quien se puede determinar «directa o indirectamente [...] mediante [...] uno o varios elementos propios de la identidad física, fisiológica, genética, psíquica [...]», y el considerando 26 manda tener en cuenta «todos los medios, como la singularización, que razonablemente pueda utilizar el responsable del tratamiento o cualquier otra persona». Un texto clínico detallado («fractura de cadera derecha hace tres semanas, ingresó desde el Hospital X, hijo único») puede singularizar a una persona aunque no diga su nombre.
2. **[Hecho]** El ADR 0011 ya recoge que anonimizar «no es la garantía principal» (línea 79-80): lo que protege es el contrato y la configuración del proveedor. Aquí se mantiene: **el texto dictado sigue siendo dato personal de salud** aunque no lleve nombre.
3. **[Opinión]** Pero el riesgo práctico de que **Mistral** reidentifique es bajo mientras la clave sea única y no se envíe ningún identificador (ni centro, ni unidad, ni cuenta: dossier, líneas 25-31). El riesgo mayor es otro: **terceras personas nombradas de pasada**. El campo **Comunicaciones** («se avisó a su hija María», «hablé con el doctor X») es, por su naturaleza, el que más nombres de familiares y profesionales va a contener. Valoración, Hallazgos y Actuaciones también, pero menos.
4. **[Hecho]** La voz puede captar a otras personas sin querer. El CEPD (directrices 02/2021, apartado de usuarios accidentales, párr. 30) avisa de que se pueden captar «audios de personas que no tenían intención de utilizar» el sistema. En Enfermería el dictado ocurre en pasillos, despachos de planta o junto a la cama: puede entrar una conversación ajena o de otro residente.
5. **[Hecho]** El dictado se hace con la identificación del residente ya fijada en pantalla, y su nombre no se envía. Eso se mantiene y es la medida principal.

**Minimización que exigiría** (art. 5.1.c y art. 25.2, «por defecto, solo sean objeto de tratamiento los datos personales que sean necesarios para cada uno de los fines específicos»):

- Solo viaja a Mistral **el audio de esa dictada**. Nunca el texto ya escrito en otros campos, ni la «Observación original» que la pantalla muestra, ni el basal, ni el historial. **[Hecho]** Esto enlaza con el criterio del 2026-10-21 recogido en el ADR 0011: «la IA no lee datos de residentes ni redacta, resume, prioriza o hace triaje» (`0011:72-73`).
- Dictado **por campo y corto**: un audio por campo, con un tope de duración menor que el actual de 5 MB (`SuggestDailyChangeFromVoice.cs:32`). **[Decidir]** CJ, con datos de uso real, qué duración es razonable.
- Aviso permanente en pantalla, más fuerte que el de Auxiliar: «No digas nombres de personas (ni del residente, ni de familiares, ni de compañeros). No dictes en un sitio donde se oiga a otras personas.»
- **[Opinión]** Valorar **dejar fuera del dictado el campo Comunicaciones**, o mostrarle el aviso reforzado, por la probabilidad de nombres de terceros. Lo decide CJ.
- Sin nombres y sin identificadores en el registro técnico (ya es así: `dictado-por-voz-dossier-dpo.md:36-39`).
- **Fijar la versión del modelo** de transcripción en lugar de `voxtral-mini-latest`: un modelo que cambia solo cambia la exactitud sin que nadie lo decida (art. 35.11: revisar la EIPD «al menos, cuando exista un cambio del riesgo»). **[Hecho]** El dossier ya avisa de que los dos modelos usan nombre «latest» (líneas 60-61).

## 3. Variante (a) frente a variante (b) (pregunta 2)

| Criterio | (a) Solo transcripción en el campo con el foco | (b) Salida estructurada que reparte en cinco campos y extrae constantes |
|---|---|---|
| Datos que salen | El audio de un campo | Audio más el texto de la transcripción a un segundo modelo (mismo proveedor) |
| Quién decide dónde va cada frase | La persona (el campo que ha elegido) | El modelo |
| Riesgo de que el modelo infiera o complete | Casi nulo (solo transcribe) | Real: «Hallazgos», «Valoración» y «Resultado» no son lo mismo y la frontera es un juicio clínico |
| Exactitud (art. 5.1.d) | Errores de oído (cifras, nombres de fármacos) | Esos mismos más errores de reparto y de interpretación |
| Revisión humana | Sencilla: lee un texto en su campo | Más difícil: hay que comprobar qué se movió, qué se añadió y qué se quitó |
| Encaje con el ADR 0011 | Nuevo caso de uso («transcripción literal»), pero sin generar contenido | Más cerca de «redactar» o «resumir», que el criterio del 2026-10-21 excluye |
| Producto sanitario (MDR) | Los sistemas «speech-to-text» no son producto sanitario según la MDCG 2019-11 | Menos claro; ver abajo |

**Preferible: (a).** Razones, por orden:

1. **Exactitud y autoría.** **[Hecho]** El art. 5.1.d RGPD exige datos «exactos» y que se supriman o rectifiquen «sin dilación» los inexactos. En (a) la persona es la autora del texto y lo lee en el mismo sitio donde lo guardará. En (b) el modelo toma decisiones de organización sobre un registro clínico. Atribuir una frase al campo «Valoración» es atribuirle a la enfermera un **juicio clínico** que quizá no emitió.
2. **Art. 22 RGPD.** **[Hecho]** El art. 22.1 reconoce el derecho «a no ser objeto de una decisión basada únicamente en el tratamiento automatizado [...] que produzca efectos jurídicos en él o le afecte significativamente de modo similar». **[Opinión]** Ni (a) ni (b) lo activan mientras: (i) el sistema no decida nada asistencial (ya descartado: clasificación, desenlace, motivo de escalado, protocolo urgente), y (ii) la revisión sea real. Pero (b) **debilita el punto (ii)**: cuanto más trabajo hace el modelo, más tentador es pulsar «Está bien» sin mirar (el propio dossier lo reconoce para Auxiliar: líneas 112-113). Si la revisión se vuelve un trámite, el argumento de que hay intervención humana pierde fuerza y, sobre todo, aumenta el riesgo clínico.
3. **Minimización.** **[Opinión]** (b) añade un segundo tratamiento del texto (otro modelo, otro riesgo de configuración, de retención y de cambio de versión). El proveedor es el mismo, así que la ganancia de (a) es moderada, pero es ganancia.
4. **Coherencia con ADR 0010 y ADR 0011.** **[Hecho]** La IA solo se usa «donde las reglas deterministas no bastan» (`0011:78`). El reparto entre campos no necesita un modelo: lo hace la persona al tocar el campo.
5. **Reglamento de IA.** **[Hecho]** Art. 6.1 (versión consolidada 2026-07-27): un sistema es de alto riesgo si es un producto (o componente de seguridad) sujeto a evaluación de conformidad de terceros según la legislación de productos; la MDCG 2019-11 dice de los «speech-to-text systems»: «Communication systems are normally based on software for general purposes, and do not fall within the definition of a medical device» (líneas 1039-1048). **[Opinión]** (a) cae claramente ahí. Para (b), un extractor que mueve constantes a campos estructurados está más cerca de una acción «beyond storage, archival, communication» (MDCG, línea 500-503); probablemente no sea producto sanitario porque no tiene finalidad médica propia, pero **conviene que lo confirme una persona con conocimiento regulatorio** si se llegara a plantear. No he leído la regla 11 del MDR en esta consulta. **[A verificar]**.

**Una variante intermedia que sí podría tener sentido más adelante (no pedida):** (a) más una **lectura determinista de las cifras** (sin modelo): la persona toca el campo de una constante y dice «treinta y seis con nueve»; Voxtral lo transcribe y una regla fija lo convierte en 36,9. Cumple el criterio del ADR 0010 y evita el segundo modelo.

**Fuera de este análisis, pero relacionado:** **[Hecho]** la pantalla ya compara las constantes con rangos de referencia y avisa (`Valoracion.cshtml:22-25`; `VitalSignReferenceRanges.cs:45-50`). La MDCG 2019-11 (líneas 1051-1052) dice que «A software module generating alarms based on the monitoring and analysis of patient specific physiological parameters is qualified as a medical device». Un aviso de «fuera de rango» no es lo mismo que una alarma automática, pero **vale la pena preguntarlo a una persona con conocimiento regulatorio** antes de producción, con dictado o sin él. Lo menciono porque, si las constantes se dictan, el aviso se alimentará de lo dictado. **[A verificar]**.

## 4. Constantes por voz: exactitud y seguridad del paciente (pregunta 3)

**[Opinión]** Las cifras son lo de **más riesgo y menos beneficio** del dictado: una cifra se teclea en un segundo y, hablada, tiene dos puntos de error (leer el aparato, y que el modelo oiga bien). En español hay confusiones típicas: «quince/cincuenta», «sesenta/setenta», «ochenta y ocho/ochenta y nueve», «con» o «coma» para el decimal, tensión sistólica y diastólica intercambiadas, glucemia en mg/dL frente a mmol/L. Por eso las pondría **en una segunda fase**, cuando se hayan medido los errores del dictado de texto.

Si se hacen, condiciones mínimas:

1. **Modo estricto ya decidido** (`0009:42-43`): cada valor, revisión individual, y guardar desactivado hasta revisarlos todos. Añado: **el valor se muestra con su unidad y su nombre en grande, junto al campo**, y la revisión no puede ser un «Aceptar todo».
2. **Rango de plausibilidad por constante, fijado por CJ**, distinto de los rangos de referencia. Fuera del rango plausible, **no se sugiere** (precedente de la temperatura: 34,0 a 42,0, `DailyChangeTemperature.cs`). **[Hecho]** Los rangos de referencia actuales sirven para avisar al profesional, no para filtrar errores de oído (p. ej. saturación 95-100, glucemia 70-120: `VitalSignReferenceRanges.cs:49-50`): otro uso, otra decisión de CJ.
3. **Una constante por dictado**, nunca varias en una frase libre: reduce el reparto equivocado.
4. **Cifras de la persona correcta.** La identificación del residente la fija la pantalla (no la voz: `0009:48-50`). Mostrar el nombre del residente en la pantalla de revisión, bien visible.
5. **Sin consecuencias automáticas.** Un valor dictado no dispara por sí solo ningún aviso nuevo distinto del que ya existe al teclear, ni un escalado. **[Hecho]** El flujo ya lo dice: el sistema «no genera ningún resumen diagnóstico ni decide automáticamente» (`valoracion-escalado-enfermeria.md:29`).
6. **Prueba de precisión específica para cifras** antes de datos reales: frases inventadas, voces del personal voluntario, ruido de planta, acentos y los pares confusos de arriba. La prueba actual (31 frases con voz sintética limpia, sin validar por CJ: dossier 114-116) **no sirve** para Enfermería. **[Decidir]** CJ: la tasa de error de cifras aceptable antes de pasar a real (propongo cero errores no detectados por la revisión en el conjunto de pruebas).
7. **Fármacos y abreviaturas** (texto, no constantes): los nombres de medicamentos parecidos son otro punto de error de oído. La pantalla de revisión debe resaltar visualmente que el texto es de dictado hasta que la persona lo toque o lo confirme (como ya hace el ADR 0009: `0009:36-41`).

## 5. Registrar evento: una advertencia propia

**[Hecho]** La observación de un evento **no se puede editar** una vez guardada (`RegistrarEvento.cshtml:20-23`; flujo clínico: «La observación original de un evento no es editable», línea 53). Si el texto dictado tiene un error y se guarda, ese error **queda para siempre** en el registro original (aunque la valoración posterior lo aclare). En Valoración, en cambio, hay borrador editable y, tras cerrar, existen correcciones (`AssessmentCorrection.cs`).

**[Opinión]** Por eso: **primero Valoración, después Registrar evento**, y en Registrar evento un paso de confirmación explícito antes de guardar («He leído y corregido el texto dictado»), además de la marca «Sugerido por voz» (`0009:36-41`). Para «Datos clínicos pertinentes» en ese mismo formulario vale lo mismo.

## 6. Encargado y subencargado: contrato, transferencias y EIPD (pregunta 4)

**Cadena.** **[Hecho]** Según el ADR 0007, la residencia es responsable, ResidApp encargada y Mistral subencargado. Art. 28.2 RGPD: «El encargado del tratamiento no recurrirá a otro encargado sin la autorización previa por escrito, específica o general, del responsable». Art. 28.4: al subencargado se le imponen «las mismas obligaciones de protección de datos» que a ResidApp. **[Opinión]** La autorización del centro a ResidApp debe decir **para qué perfiles y pantallas** se usa el dictado: la instrucción documentada es del centro (art. 28.3.a), y no es lo mismo autorizar Auxiliar que Enfermería. Por defecto, activar el módulo no debe dar acceso a ningún perfil: cada perfil se concede aparte (art. 25.2; `modulos-opcionales-lectura-dpo.md`, apartado 9.6).

**Qué hay que tener en el contrato (centro-ResidApp) y en el DPA de Mistral**, además de lo que ya recoge el dossier (líneas 70-81):

1. **Descripción de datos e interesados** (art. 28.3: «el tipo de datos personales y categorías de interesados»): datos de salud de residentes, **texto clínico de Enfermería con detalle**, voz del personal y, de pasada, nombres de terceros que se dijeron por error. El DPA de Mistral dice hoy «categorías especiales de datos: ninguna»: hay que corregirlo por escrito.
2. **Confirmación escrita de que Mistral no usa el audio ni el texto para entrenar**, que Voxtral no se usa para identificar a la persona que habla ni para inferir emociones, y que el uso con retención cero está aprobado para **los dos servicios si se usan los dos** (solo uno en la variante (a)).
3. **Transferencias**: art. 28.3.a incluye las instrucciones sobre transferencias a terceros países. **[Hecho]** Art. 44 RGPD: las transferencias solo se realizan si responsable y encargado cumplen las condiciones del capítulo V, «incluidas las relativas a las transferencias ulteriores». Mistral es una empresa francesa y el procesamiento por defecto es en la UE (dossier, 43-48), pero **sus propios subencargados** pueden estar fuera: lista actual y ubicación, **[A verificar]**; si hay un destinatario fuera de la UE, garantías del art. 46 (cláusulas tipo) y una valoración propia. **[Opinión]** Un proveedor europeo con un subencargado estadounidense sigue exigiendo analizar la transferencia, aunque el servidor esté en la UE. No he evaluado esto: es cuestión para la persona experta.
4. **Asistencia** (art. 28.3.e y f): que Mistral ayude a ResidApp a atender derechos, a hacer la EIPD y a notificar brechas; que avise «sin dilación indebida» de una brecha (art. 33.2: «El encargado del tratamiento notificará sin dilación indebida al responsable [...] las violaciones»).
5. **Cambios de subencargados y de modelo**: aviso previo y derecho de oposición (art. 28.2) y compromiso de no cambiar la versión del modelo sin avisar. **[Opinión]** Un cambio de versión del modelo es, para la EIPD, un posible «cambio del riesgo» (art. 35.11).
6. **Supresión y auditoría** al terminar (art. 28.3.g y h): con retención cero no hay nada que devolver; hay que dejarlo escrito.
7. **Quién firma** a nombre de quién la cadena centro-ResidApp-Mistral: sigue sin decidir (dossier, líneas 83-84).

**EIPD.**

- **[Hecho]** El art. 35.1 RGPD exige EIPD «antes del tratamiento» cuando el riesgo es alto, «en particular si utiliza nuevas tecnologías»; el art. 35.3.b la exige para el «tratamiento a gran escala de las categorías especiales de datos». La lista de la AEPD (art. 35.4) presume necesaria la EIPD «en la mayoría de los casos» con dos o más criterios; aquí concurren como mínimo el 4 (categorías especiales), el 9 (sujetos vulnerables: «mayores con algún grado de discapacidad») y el 10 («nuevas tecnologías o un uso innovador de tecnologías consolidadas»), y probablemente el 7 (datos a gran escala, según el tamaño del centro y del conjunto de centros: **[A verificar]**).
- **[Hecho]** El art. 35.1 dice también que «una única evaluación podrá abordar una serie de operaciones de tratamiento similares que entrañen altos riesgos similares». **[Opinión]** Mi propuesta es **no hacer una EIPD para Auxiliar y rehacerla después**: hacer **una EIPD del dictado por voz** con un anexo por perfil (Auxiliar, Enfermería) y por tipo de contenido (opciones de catálogo; texto clínico libre; cifras). Hoy **no hay EIPD** (dossier, línea 119): el momento de incluir a Enfermería es ahora. Si ya existiera, habría que **revisarla** (art. 35.11), no hace falta rehacerla, porque cambian el contexto y el riesgo pero no la naturaleza del tratamiento.
- La EIPD la hace el **responsable** (el centro), recabando el asesoramiento del DPO si lo hay (art. 35.2); ResidApp, como encargada, aporta la información (art. 28.3.f). **[Hecho]** El art. 35.7 fija el contenido mínimo: descripción, necesidad y proporcionalidad, riesgos y medidas. Para la estructura, hay una guía de la APDCAT (2025, catalán) y otra de la AEPD (2021) en la base local; **no las he leído enteras en esta consulta**.
- **[Hecho]** Si, tras las medidas, el riesgo sigue siendo alto: consulta previa a la autoridad (art. 36). **[Suposición]** Con retención cero, sin identificadores y con revisión humana, no debería llegar a ese punto; lo confirma la EIPD.

**Autoridad de control.** **[Hecho]** El art. 3 de la Llei 32/2010 (BOE act. 2026-07-13) incluye en el ámbito de la APDCAT, entre otros: «e) Las entidades de derecho privado que cumplan, como mínimo, uno de los tres requisitos [...]: [...] Segundo.–Que sus ingresos presupuestarios provengan mayoritariamente de dichos entes públicos» y «f) Las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios». **[Opinión]** Una residencia pública entra de lleno; una **concertada** de la red de servicios sociales de la Generalitat probablemente entre por e) o f); una **totalmente privada sin financiación pública** iría a la AEPD. **Falta el dato del tipo de centro piloto**: es una pregunta para Orlando y condiciona también si aplica el Esquema Nacional de Seguridad.

## 7. Información y derechos de los profesionales de Enfermería (pregunta 5)

- **Su voz es dato personal.** **[Hecho]** Art. 4.1 RGPD: son datos personales los que permiten identificar a alguien mediante «elementos propios de la identidad física, fisiológica [...]». El audio de una enfermera lo es, y llega a un tercero.
- **No es dato biométrico especial.** **[Hecho]** Art. 4.14: datos obtenidos «a partir de un tratamiento técnico específico [...] que permitan o confirmen la identificación única de dicha persona»; art. 9.1 solo prohíbe «datos biométricos dirigidos a identificar de manera unívoca a una persona física». **[Opinión]** Mientras el sistema **no** cree huellas de voz ni identifique al hablante (y el contrato con Mistral lo prohíba), no hay dato biométrico especial. Es la respuesta a la pregunta 7 del dossier. Condición: el sistema nunca debe usar la voz para identificar ni para autenticar (el ADR 0009 ya la descarta: líneas 48-50).
- **Emociones.** **[Hecho]** El art. 5.1.f del Reglamento de IA (consolidado a 2026-07-27) prohíbe usar sistemas de IA «para inferir las emociones de una persona física en los lugares de trabajo» salvo motivos médicos o de seguridad. **[Opinión]** Transcribir no es inferir emociones, pero conviene dejar escrito en el contrato y en el ADR que **no se usa** esa capacidad.
- **Deber de información** (arts. 12 y 13 RGPD). La informa el **centro** como empleador: quién trata su voz, para qué (transcribir), que va a Mistral (subencargado), que no se guarda, que no se usa para evaluarla, plazos (auditoría) y derechos. **[Opinión]** El texto lo redactaría yo como parte de la política para profesionales (aún no existe: `antes-de-datos-reales.md:147`).
- **Uso no obligatorio.** **[Opinión]** El dictado debe ser **opcional por persona**, sin consecuencias si no se usa. El consentimiento **no** sirve aquí como base: el considerando 43 dice que no es fundamento válido cuando «exista un desequilibrio claro entre el interesado y el responsable», que es lo habitual entre empleada y empleador. La base más probable es la ejecución del contrato o el interés legítimo del centro (art. 6.1.b o f): **[Decidir]** el DPO real. Si fuera 6.1.f, existe el derecho de oposición (art. 21).
- **No se convierte en control laboral.** **[Hecho]** El art. 20.3 del Estatuto de los Trabajadores permite medidas de vigilancia «guardando [...] la consideración debida a su dignidad»; el art. 89.3 LOPDGDD admite la grabación de sonidos en el trabajo solo cuando sean «relevantes los riesgos para la seguridad» y con proporcionalidad e intervención mínima (pensado para vigilancia, no para el dictado, pero el audio **sí** es una grabación). **[Opinión]** Condición: el audio no se guarda, y las filas de `VOICE_DICTATION_USED` (que dicen quién dictó) **no se usan para medir ni evaluar** a nadie; el contrato con el centro lo debe decir. Es la misma regla que se propuso para los contadores de uso en `modulos-opcionales-lectura-dpo.md`, apartado 3.
- **Móvil personal.** **[Hecho]** El ADR 0009 admite «móviles personales Android» (línea 55). El art. 87.3 LOPDGDD exige que los empleadores establezcan **criterios de utilización** de los dispositivos digitales, con participación de los representantes de los trabajadores, e informar de ellos. **[Opinión]** Si el micrófono es el del móvil personal de la enfermera, esos criterios deben existir. **[A verificar]** con asesoría laboral si el art. 87 cubre también el dispositivo propio usado para trabajar.
- **Formación.** **[Hecho]** El art. 4 del Reglamento de IA obliga a proveedores y a responsables del despliegue a promover la «alfabetización en materia de IA» del personal que usa el sistema. **[Opinión]** Cabe en una explicación corta (qué hace el dictado, que se equivoca, que debes revisarlo) dentro de la información al personal.

## 8. Condiciones mínimas de la ampliación (pregunta 6)

**Imprescindibles antes de datos reales** (se suman a las del dossier del dictado de Auxiliar):

1. El DPA de Mistral cubre datos de salud, **por escrito**, y confirma no entrenamiento y retención cero aprobada (dossier 77-79).
2. Autorización del centro a ResidApp para usar a Mistral **que mencione el perfil Enfermería** y qué pantallas.
3. EIPD del dictado que **incluya Enfermería**, con la proporcionalidad justificada y la prueba de precisión de Enfermería (no la de Auxiliar).
4. Información entregada al personal y a las personas residentes y sus representantes; criterios de uso del móvil si es personal.
5. Una persona experta (DPO real o abogado) que lo valide.
6. Modelo de transcripción **con versión fija**.

**Condiciones de diseño** (para el ADR):

7. Solo la variante (a) en la primera versión; el audio de un campo; nada más sale.
8. Marca «Sugerido por voz» en cada campo dictado hasta que la persona lo toque o lo confirme (`0009:36-41`); botón de guardar sin bloqueo en texto, **bloqueado** en constantes hasta revisarlas una a una.
9. Aviso fijo y reforzado sobre nombres; **sin dictado** en los campos de decisión asistencial, escalado y protocolo urgente.
10. Auditoría previa al envío, como hoy, y **sin uso para evaluar a las personas**.
11. Apagable por centro, por perfil y de golpe para todos si Mistral tiene un incidente.
12. Si el módulo de dictado es opcional por centro (decidido para el dictado: `pendientes-landing.md:35`), que el interruptor de **Enfermería** sea independiente del de Auxiliar. **[Opinión]** Un centro puede querer Auxiliar sí y Enfermería no.

**Recomendable:** medir durante el piloto, con datos agregados y sin personas, tasa de correcciones por campo, para decidir si se amplía.

## 9. Qué debería recoger el ADR (pregunta 7)

Una modificación del ADR 0011 (nuevo caso de uso aprobado) y del 0009 (alcance del dictado), con:

1. **Caso de uso nuevo y acotado**: «transcripción literal a un campo de texto con el foco, para los campos de Valoración y, después, de Registrar evento», con la lista cerrada de campos. Lo que no esté en la lista no está aprobado.
2. **Lo descartado, escrito**: reparto automático entre campos, clasificación del evento, decisión asistencial, motivo de escalado, protocolo urgente y resúmenes. Y que **no se envía al modelo ningún dato que no sea el audio del campo**.
3. **El criterio de cada variante**: por qué se elige (a) y qué haría falta para abrir (b) (el procedimiento del ADR 0011, líneas 93-95: caso de uso que lo necesite, visto bueno de CJ, contrato y EIPD, ADR nuevo).
4. **Revisión y marca en pantalla**: reglas de la sección 8, modo estricto en constantes, confirmación previa en Registrar evento por ser inmutable.
5. **Fases**: Valoración (texto), luego Registrar evento, luego constantes; cada una con su criterio de salida medible.
6. **Subencargado y contrato**: Mistral, versión de modelo fija, retención cero como condición, sin entrenamiento, sin identificación de hablante, sin inferencia de emociones.
7. **Auditoría**: `VOICE_DICTATION_USED` con el perfil, sin contenido y con la regla de no usarla para evaluar personas.
8. **Perfiles y módulo**: interruptor independiente por perfil; ningún perfil lo recibe por defecto.
9. **Condiciones de datos reales**: cada una de la sección 8 que no sea de diseño, como lista de comprobación previa.
10. **Prueba de precisión de Enfermería**: conjunto con datos inventados, voz humana, ruido, números y fármacos parecidos, con umbral fijado por CJ.

## A verificar o decidir

**Decidir (Orlando):**

1. Tipo de centro piloto (público, concertado o privado): de él depende la autoridad (AEPD o APDCAT) y si aplica el ENS.
2. Si se empieza por Valoración (texto) y se deja Registrar evento y las constantes para después (mi propuesta).
3. Si el módulo de dictado tiene interruptor independiente para Enfermería.
4. Quién firma la cadena centro-ResidApp-Mistral, y a nombre de quién.
5. Si, dado el beneficio esperado, vale la pena el esfuerzo extra de contrato, EIPD y prueba específica antes de datos reales, o si Enfermería espera a que el dictado de Auxiliar esté ya validado por la persona experta.

**Decidir (CJ):**

6. Qué campos de Valoración admiten dictado (¿se excluye «Comunicaciones»?).
7. Tope de duración por dictado.
8. Rango de plausibilidad de cada constante (si se llega a la fase de cifras) y el umbral de error aceptable de la prueba.
9. Si hace falta una pantalla de confirmación en Registrar evento por ser inmutable y si es aceptable para el flujo.
10. Valida o corrige el conjunto de frases de prueba de Enfermería.

**Decidir (DPO real o abogado):**

11. Base jurídica en cada población: residentes (art. 9.2.h), personal (6.1.b o 6.1.f; no el consentimiento).
12. EIPD con alcance «dictado por voz» y anexo por perfil.
13. Transferencias ulteriores de Mistral; garantías del art. 46 si procede.
14. Texto de información al personal, a residentes y a representantes.
15. Si el art. 87 LOPDGDD cubre el móvil personal usado como herramienta de trabajo.
16. Plazo de conservación de las filas de auditoría del dictado (sigue sin decidir).
17. Si el aviso fijo basta o debe avisar el sistema cuando crea oír un nombre (pregunta 10 del dossier). **[Opinión]** Un detector posterior no impide el envío (el audio ya salió); solo sirve para quitar el nombre del texto antes de guardarlo.

**A verificar (fuente oficial):**

- Región y lista de subencargados de Mistral en los dos servicios, y qué versión del DPA rige (dossier 81-82).
- Calificación de un extractor de constantes y del aviso por rangos de referencia como producto sanitario: regla 11 del MDR y MDCG 2019-11, con una persona con conocimiento regulatorio.
- Que el art. 12 de la Llei 21/2000 se aplique a una residencia (conservación del registro dictado; ya abierto).
- Contenido de las guías de EIPD de la APDCAT y de la AEPD para el anexo (no leídas enteras).
- Si el considerando 26 y la doctrina de la AEPD sobre singularización exigen alguna cautela adicional para texto clínico sin nombre: **no encontré una guía específica en la base local**; si hay una resolución de la AEPD o de la APDCAT sobre dictado clínico por voz con proveedores de IA, hay que localizarla.

**Normas que faltan en la base local y que sería útil añadir:** la regla 11 del MDR sí está (`mdr.txt`) pero no la he leído en esta consulta; no hay una guía del CEPD sobre «intervención humana significativa» en decisiones automatizadas (las directrices WP251 sobre decisiones automatizadas) ni una guía de la AEPD o APDCAT sobre IA generativa en el entorno sanitario.

**Yo no soy DPO designado ni abogado.** Todo lo anterior lo valida una persona antes de usarlo.
