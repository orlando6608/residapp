# Dictado por voz: ¿Mistral o Azure? Lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-15. Segunda lectura experta del agente `dpo-residapp`, no un dictamen ni una designación de DPO. Es una consulta **previa a cualquier ADR o código**: no se ha decidido ni construido nada. Continúa `docs/privacidad/dictado-enfermeria-lectura-dpo.md` y el dossier `docs/seguridad/dictado-por-voz-dossier-dpo.md`. No modifica código, ADR ni otros documentos. No contiene datos reales.

Etiquetas: **[Hecho]** leído hoy en un texto o una página oficial (con su URL) o verificado en el repositorio; **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar. Una página de documentación de Microsoft o de Mistral **no es un contrato**: describe lo que el proveedor dice hacer, y solo obliga lo que está en el contrato firmado.

## Conclusión

**Azure AI Speech (opción B) es, sobre el papel, mejor candidata que Mistral para la variante (a) solo transcripción. Azure OpenAI (opción C) no lo es.** Con una condición grande: lo que hace buena a la opción B sale de páginas de documentación, y **no he podido leer el contrato (el DPA de Microsoft) ni la lista de subencargados**. Por eso la recomendación es provisional y se vuelve firme solo cuando una persona lea esos dos documentos y Microsoft confirme por escrito tres cosas (apartado 6).

Lo más importante, por orden:

1. **Retención.** **[Hecho]** Microsoft escribe que en la transcripción en tiempo real y en la «fast transcription» (transcripción rápida de un archivo) «Microsoft does not retain or store the data provided by customers». Es una afirmación general de la página, **sin trámite de aprobación**. En Mistral, la retención cero **hay que pedirla y Mistral puede denegarla**. En Azure OpenAI, el comportamiento por defecto incluye un almacén de abuso con posible revisión humana, y quitarlo solo está abierto a clientes con equipo de cuenta de Microsoft.
2. **Lo que cambia de sitio, no desaparece.** Cambiar de proveedor no elimina la necesidad de contrato revisado, EIPD, información al personal ni análisis de transferencias (capítulo V RGPD). Cambia **qué huecos quedan** (apartado 4).
3. **Un obstáculo práctico:** **[Hecho]** Spain Central (la región de dev, ADR 0014) **no admite hoy Azure Speech independiente**. El recurso de voz tendría que estar en otra región de la UE.
4. **Exactitud: no hay evidencia a favor de ninguno.** Hay que medirlo con la prueba de precisión, con voces humanas (apartado 4.3).
5. **Momento.** No hay datos reales, ni EIPD, ni contrato con Mistral cerrado. Elegir proveedor **ahora** es lo más barato: cambiarlo después de la EIPD la obliga a revisarse (art. 35.11 RGPD).

## 1. Fuentes: qué he podido leer y qué no

Todas las páginas de Microsoft y de Mistral las leí el 2026-11-15. Con `WebFetch` obtengo el texto de la página, pero no siempre íntegro: lo indico.

**Leídas (texto de la página oficial):**

| Fuente | URL | Fecha de la página | Qué aporta |
|---|---|---|---|
| Datos, privacidad y seguridad de Speech to text | https://learn.microsoft.com/en-us/azure/ai-foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security | ms.date 2026-03-31, actualizada 2026-08-26 | Retención y almacenamiento de Speech |
| Regiones de Azure Speech | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions | ms.date 2026-10-28, actualizada 2026-11-10 | Dónde se procesa; qué regiones admiten transcripción rápida; Spain Central |
| Fast transcription (crear) | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create | Leída solo hasta el carácter 100 000 de 125 640 | Idiomas, límites, lista de frases |
| Idiomas de Speech | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support?tabs=stt | Leída hasta el carácter 100 000 de 171 433 | es-ES, ca-ES |
| LLM Speech y MAI-Transcribe | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/llm-speech y https://learn.microsoft.com/en-us/azure/ai-services/speech-service/mai-transcribe | 2026-10-28 y 2026-09-22 | Modelos de transcripción basados en LLM dentro de Speech |
| Transcripción de OpenAI frente a Speech | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/transcribe-overview | 2026-07-27 | Qué recomienda Microsoft para cada caso |
| Transcripción por lotes | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/batch-transcription-create | 2026-03-30 | Almacenamiento y `timeToLiveHours` (no la usaríamos) |
| Datos, privacidad y seguridad de Azure OpenAI / modelos vendidos por Azure | https://learn.microsoft.com/en-us/azure/ai-foundry/responsible-ai/openai/data-privacy | ms.date 2026-05-18, actualizada 2026-06-05 | Retención, abuso, entrenamiento, zonas de datos |
| Vigilancia de abuso | https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/abuse-monitoring | 2026-05-13 | Revisión humana y «vigilancia modificada» |
| Acceso limitado | https://learn.microsoft.com/en-us/azure/ai-foundry/responsible-ai/openai/limited-access | actualizada 2026-06-05 | Quién puede pedir la vigilancia modificada |
| Disponibilidad por región de los modelos | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability y la página de modelos https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure | 2026-09-03 y 2026-09-21 | Whisper y gpt-4o-transcribe en la UE |
| ENS en Microsoft | https://learn.microsoft.com/en-us/compliance/regulatory/offering-ens-spain | ms.date 2025-03-21, actualizada 2026-06-02 | Servicios de Azure dentro del alcance del ENS nivel alto |
| Guía de EIPD para clientes de Azure | https://learn.microsoft.com/en-us/compliance/regulatory/gdpr-dpia-azure | 2026-05-05 | Papel de Microsoft, subencargados, transferencias, categorías especiales |
| EU Data Boundary | https://learn.microsoft.com/en-us/privacy/eudb/eu-data-boundary-learn | 2026-07-21 | Qué compromete Microsoft en la UE |
| Mistral: DPA | https://legal.mistral.ai/terms/data-processing-addendum | «Effective: July 27, 2026» | Entrenamiento, subencargados, categorías especiales |
| Mistral: retención cero | https://docs.mistral.ai/admin/monitor-comply/zero-data-retention | sin fecha visible | Qué cubre y cómo se pide |
| Mistral: dónde se guardan los datos | https://help.mistral.ai/en/articles/347629-where-do-you-store-my-data-or-my-organization-s-data | sin fecha visible | UE por defecto; excepciones |
| Mistral: transcripción de audio | https://docs.mistral.ai/capabilities/audio_transcription | sin fecha visible | Idiomas y vocabulario de contexto |

`WebFetch` devuelve a veces un resumen y no el texto literal. Las citas en inglés de las páginas de Microsoft me llegaron como texto de la página; las de Mistral me llegaron resumidas con comillas parciales y las trato como resumen.

**No he podido leer (y por tanto NO verificado):**

- **El texto del DPA de Microsoft** («Microsoft Products and Services Data Protection Addendum», edición de noviembre de 2026, página https://www.microsoft.com/licensing/docs/view/Microsoft-Products-and-Services-Data-Protection-Addendum-DPA). La página de Microsoft solo da enlaces a archivos Word (`.docx`). Descargué el de noviembre de 2026 (6,3 MB), pero la herramienta no lo convierte a texto; una copia en PDF de abril de 2025 alojada por un tercero tampoco se pudo leer. **Lo que digo del contrato de Microsoft sale de la guía de EIPD de Microsoft (arriba), no del DPA.**
- **La lista de subencargados de Microsoft** (https://aka.ms/subprocessor, que redirige a la página `servicetrust.microsoft.com`; carga con JavaScript y no devuelve texto) y **la de Mistral** (https://trust.mistral.ai/subprocessors; igual).
- **Los certificados** del ENS, ISO 27001, etc. (solo leí que Microsoft los declara; no los certificados).
- **La versión íntegra** de dos páginas largas (fast transcription: faltan los últimos 25 640 caracteres; idiomas: faltan 71 433).
- **Las cifras de retención de Mistral sin retención cero** (solo la frase del DPA «la duración del contrato»).
- **Las condiciones de producto de Microsoft** (Product Terms) y las de Mistral (términos comerciales).

**No existe en `normativa-local/`** (índice del 2026-11-14, comprobación más antigua del 2026-11-12, menos de una semana): el DPA de Microsoft, el de Mistral, ni una guía de la AEPD o la APDCAT sobre proveedores de transcripción o IA en la nube sanitaria. No he añadido nada al catálogo: el script solo baja PDF y el DPA de Microsoft es `.docx`. Lo que sí he usado de la base local: RGPD (arts. 28 y 44) y ENS (`op.nub.1`, `ens-rd-311-2022.txt`).

## 2. Lo que hay hoy en el repositorio

- **[Hecho]** `IaOptions.ProveedoresAutorizados = ["Mistral"]` y `Validate` impide arrancar con otro (`src/ResidApp.Infrastructure/IA/IaOptions.cs:15, 28-36`). La propiedad `Proveedor` es **una sola** para toda la IA (línea 17).
- **[Hecho]** Los puertos ya están separados: `ISpeechTranscriber` (audio a texto) e `IStructuredGenerator` (salida con esquema) en `src/ResidApp.Application/Ports/IVoiceDictation.cs:8-19`. Un solo adaptador implementa los dos: `MistralClient : ISpeechTranscriber, IStructuredGenerator` (`src/ResidApp.Infrastructure/IA/MistralClient.cs:20`).
- **[Hecho]** Modelos por defecto: `voxtral-mini-latest` y `mistral-small-latest` (`IaOptions.cs:46, 48`), «latest» incluido (el dossier ya avisa: líneas 60-61).
- **[Hecho]** ADR 0011, línea 58: «Ser independiente del proveedor **no significa tener varios**. Se construye un solo adaptador». Línea 85: otros proveedores «necesitan su ADR, contrato y EIPD». Un «modelo local» sería «el candidato natural».
- **[Hecho]** La variante (a) de Enfermería **no usa** `IStructuredGenerator`: solo transcribe (lectura anterior, apartado 3).
- **[Hecho]** El dev está en Spain Central (`docs/decisiones-arquitectura/0014-entorno-produccion.md:19`); la región de producción no está decidida (`docs/tareas/futuro/pendientes-landing.md:54`).
- **[Hecho]** El dossier recoge ya las lagunas de Mistral: DPA con «categorías especiales de datos: ninguna», entrenamiento ambiguo entre dos textos, retención cero por aprobar, subencargados y región por confirmar (`dictado-por-voz-dossier-dpo.md:57-81`).

## 3. Tabla comparativa

Todo lo de la tabla es lo que **dicen las páginas del proveedor** (salvo lo marcado). «B» es Azure AI Speech con la transcripción rápida (un archivo de audio que se sube y devuelve texto de una vez: encaja con nuestro flujo «grabar, parar, enviar»). «C» es Azure OpenAI con `whisper` o `gpt-4o-transcribe`.

| Fila | A) Mistral (hoy) | B) Azure AI Speech (UE) | C) Azure OpenAI (UE) |
|---|---|---|---|
| **¿Conserva el audio o el texto? ¿Cuánto?** | Sin retención cero: el anexo del DPA fija la retención en «the duration of the Agreement» (la duración del contrato). Con retención cero (ZDR): «controls whether supported API inputs and outputs are retained after Mistral returns a response»; cubre `/v1/audio/transcriptions` y el chat. Disponible «on paid plans» y solo previa petición. | «When doing real-time speech to text, fast transcription, pronunciation assessment, and speech translation, Microsoft does not retain or store the data provided by customers.» En tiempo real, el audio se procesa «only on the Azure's server memory, and no data is stored at rest». **La transcripción por lotes sí almacena** (en tu contenedor o en el de Microsoft, con `timeToLiveHours` de 6 horas a 31 días); no la usaríamos. | Los modelos son «stateless»: no guardan nada en el modelo. **Pero existe un almacén de vigilancia de abuso**: se muestrean y guardan peticiones marcadas para posible revisión humana, en la geografía del recurso. La cifra «30 días» **no aparece en las páginas que he leído**: [A verificar]. La página habla de «prompts and completions (text and image)»; **si el audio entra en ese almacén no lo dice**: [A verificar]. |
| **¿Revisión humana del proveedor?** | El dossier solo menciona «moderación automática en la API», que con ZDR no se aplica (DPA: «Abuse monitoring on APIs is excluded when zero data retention is activated»). Si hay revisión humana sin ZDR: [A verificar]. | La página de Speech **no menciona** revisión humana ni vigilancia de abuso. Silencio no es garantía: [A verificar] por escrito con Microsoft. | **Sí, por defecto y cuando hace falta:** «human eyes-on review might be introduced»; personal autorizado de Microsoft, y para despliegues en el EEE, «located in the European Economic Area». Se evita solo con la «vigilancia modificada», que está «available only to customers and partners managed by a Microsoft account team or under an eligible program». Que ResidApp cumpla eso: [A verificar]; **[Suposición]** una empresa pequeña probablemente no. |
| **¿Se usa para entrenar?** | DPA 2.3 (resumen): Mistral puede entrenar «in accordance with its Privacy Policy, unless Customer is or has opted-out of training». Los términos comerciales dicen lo contrario en general (dossier 57-61). La página de ZDR: «You do not need ZDR to opt out of model training». Hay que desactivarlo y comprobarlo. | La página de Speech **no lo dice**. La guía de EIPD de Microsoft: «Microsoft won't use Customer Data or information derived from it for profiling or for advertising or similar commercial purposes», y Microsoft usa «Personal Data to support a limited set of business operations» como **responsable propio**, normalmente agregados. El entrenamiento de modelos no se nombra: [A verificar] por escrito. | **Explícito:** prompts y respuestas «are NOT used to train any generative AI foundation models without your permission or instruction» y «NOT available to OpenAI or other providers of Models sold by Azure». |
| **¿Dónde se procesa? ¿Subencargados ulteriores? ¿Fuera del EEE (cap. V)?** | «By default, your data is hosted in the European Union.» Hay un punto de acceso de EE. UU. que ResidApp no usa. Según su ayuda, algunas funciones pueden mover datos temporalmente fuera de la UE (lista en su Trust Center, **no legible**). El DPA autoriza transferencias a países con adecuación o con cláusulas tipo. Autorización general de subencargados, con 10 días para oponerse. | «Azure Speech stores and processes speech data in the region where you create your resource» (la excepción es Voice Live, que no usaríamos). Transcripción rápida disponible en regiones UE: Francia Centro, Alemania Centro-Oeste, Italia Norte, Norte de Europa, Suecia Central y Oeste de Europa. **Spain Central: «not currently supported for standalone speech processing»**. Subencargados y transferencias: ver DPA de Microsoft (no leído). Según su guía de EIPD, las transferencias fuera del EEE llevan garantías del art. 46 RGPD, cláusulas tipo y el Marco de Privacidad de Datos UE-EE. UU. **El «EU Data Boundary»** compromete a almacenar y procesar datos de clientes en la UE/AELC, «subject to limited circumstances» de transferencia fuera. | Depende del tipo de despliegue. **Regional (Standard):** en la región. **Data Zone UE:** «in that or any other European Union Member Nation». **Global:** «any geography where the model is deployed». Modelos: `whisper` Standard en Suecia Central, Oeste de Europa (y Noruega, Suiza: fuera de la UE); `gpt-4o-transcribe` Data Zone UE en Francia Central y Suecia Central; `gpt-transcribe` (2026-07-28) solo Global Standard en lo que leí. `gpt-4o-transcribe` consta como **Preview** en la página de modelos, y para previews Microsoft avisa de que «may employ different privacy practices, including with respect to abuse monitoring». |
| **¿Qué contrato aplica? ¿Cubre datos de salud? ¿Quién firma? ¿Cuántos encargados añade?** | DPA de Mistral, incorporado «by reference» a los términos comerciales. **Anexo: «Special categories of personal data (if applicable): None.»** Hay que pedir confirmación o ampliación por escrito. Firma o acepta ResidApp (decisión de quién y a nombre de quién: dossier 83-84). **Añade 1 subencargado nuevo** (Mistral) **más los suyos** (lista no legible). | DPA de Microsoft («Microsoft Products and Services DPA», parte de las Product Terms): **no leído**. Según la guía de EIPD de Microsoft, Microsoft actúa como encargado y «Azure isn't designed to process special categories of personal data on a large scale. However, a data controller could use Microsoft Azure to process the enumerated special categories». Eso no es una exclusión contractual, pero **tampoco es la cobertura expresa de datos de salud**: lo confirma el DPA. Firma ResidApp como cliente de Azure. **No añade una entidad nueva:** Microsoft ya aloja la base de datos y la web (ADR 0007). Sí amplía el tipo de datos que le llegan (audio con voz y contenido clínico). | Mismo DPA y misma entidad que B. Microsoft hospeda los modelos «in Microsoft's Azure environment» y estos «do NOT interact with any services operated by providers [...] for example, OpenAI». **No añade entidad nueva.** Cobertura de datos de salud: igual que B, no leído. |
| **Certificaciones útiles** | **No he podido verificar ninguna** (Trust Center no legible; el dossier no las recoge). | La página de Microsoft sobre el ENS dice que Azure cumple el ENS nivel alto según BDO y lista dentro del alcance «Azure AI Services (includes [...] Azure AI Speech)». Vigencia: «two years, with an annual surveillance audit». **No he leído el certificado** (`aka.ms/AzureENS`). ISO 27001 y otras: no verificadas. | Misma página: «Azure AI Services (includes Azure OpenAI [...])» dentro del alcance del ENS. Mismas reservas. |
| **Qué configurar para «retención cero» o lo más parecido** | 1) Pedir la retención cero a Mistral con justificación (puede denegarla). 2) Confirmación escrita de que cubre los dos servicios usados (en la variante (a), solo la transcripción). 3) Desactivar el entrenamiento y comprobarlo. 4) Modelos fuera de «Labs». 5) Solo el punto de acceso de la UE. | 1) Recurso de Speech en una región UE de la lista. 2) Usar **transcripción rápida o en tiempo real, nunca por lotes**. 3) **No** activar diarización (la página dice que extrae «unique voice characteristics signals»: es justo lo que queremos evitar por el análisis de dato biométrico de la lectura anterior). 4) **No** entrenar modelos personalizados con audios o textos (eso sí guarda datos). 5) Usar la lista de frases (se manda con cada petición, no se guarda). 6) Revisar que los registros de diagnóstico de Azure no guarden contenido: [A verificar]. 7) Confirmación escrita de Microsoft sobre revisión humana, entrenamiento y telemetría. | 1) Despliegue **regional o Data Zone UE**, nunca Global. 2) Pedir la «vigilancia modificada» (si se puede) y comprobar `ContentLogging = false` en el recurso. 3) Evitar las funciones con estado (Responses API, finalizaciones almacenadas, Assistants, Files). 4) Evitar modelos en preview. |

**Lectura de la tabla [Opinión].** B es la única de las tres cuyas propias páginas dicen «no se guarda» sin pedir un permiso especial. C tiene una garantía explícita de no entrenamiento, pero el comportamiento por defecto (almacén de abuso y revisión humana) es peor para datos de salud que lo que ofrece B. A depende de que Mistral apruebe la retención cero y de que amplíe el DPA. Ninguna cierra sola los puntos del dossier.

## 4. Valoración jurídica

### 4.1 ¿Una opción reduce o aumenta el riesgo frente a otra?

**C aumenta el riesgo frente a B.** Los datos de salud que llegarían a Microsoft por C quedarían, por defecto, expuestos a un almacén de abuso y a personal humano de Microsoft (aunque ubicado en el EEE), con una vía de salida que depende de ser cliente con equipo de cuenta. **[Opinión]** Sin esa excepción, C no es defendible para audio clínico cuando existe B. Además, Microsoft recomienda B para el caso que nos ocupa: en su tabla «Real-time text entry and document generation through voice dictation» aparece «Recommended» para Azure Speech y «Not available» para el modelo de OpenAI (esa fila habla de tiempo real; nuestro flujo es un archivo grabado, pero la orientación del producto es la misma).

**B frente a A, [Opinión]:**

| Aspecto | B (Azure Speech) | A (Mistral) |
|---|---|---|
| Retención documentada | Mejor: sin trámite | Peor: depende de que Mistral apruebe la retención cero |
| Datos de salud en el contrato | Sin leer; Microsoft no los excluye en su guía | Texto leído: «Special categories: None» |
| Entidad nueva en la cadena (art. 28.2 y 28.4) | No (ya es subencargado por el alojamiento) | Sí (más sus propios subencargados) |
| Empresa matriz fuera de la UE | **Sí: EE. UU.** Obliga a analizar transferencias y acceso remoto (cap. V) | No (Mistral es francesa), pero sus subencargados pueden estarlo: lista no legible |
| Concentración | Mayor: base de datos, web y voz en un mismo proveedor | Menor |
| Región | Otra región de la UE distinta de Spain Central | «Unión Europea» por defecto (el dossier no precisa el país) |
| Encaje ENS (si el centro es público o concertado) | Dentro del alcance declarado del ENS alto | Sin verificar |
| Madurez del trabajo hecho | Hay que construir un adaptador nuevo | Hecho y probado (dossier, apartado 9) |

**Dos matices que no son a favor de B:**

- **Cap. V (transferencias).** Art. 44 RGPD: solo se realizan transferencias si el responsable y el encargado cumplen el capítulo V, «incluidas las relativas a las transferencias ulteriores». Con Microsoft hay que analizar el acceso remoto desde fuera de la UE y la base del Marco de Privacidad de Datos UE-EE. UU. **No he verificado el estado actual de ese marco ni los compromisos del DPA**: lo analiza la persona experta. Con Mistral, el mismo análisis, sobre una lista de subencargados que tampoco he podido leer. **Ninguna opción evita este análisis.**
- **Spain Central.** El recurso de voz en otra región UE significa que **el audio sale de España** (aunque no de la UE). Hay que decírselo al centro y reflejarlo en el registro de actividades (art. 30) y en la información. **[Opinión]** No es un problema jurídico por sí mismo (el RGPD no exige quedarse en el país), pero puede importar a un centro público o concertado por razones contractuales o de ENS.

### 4.2 ¿Cambia la EIPD?

**[Opinión]** Sí, en el contenido, no en la obligación. La EIPD sigue siendo necesaria (criterios 4, 9 y 10 de la lista de la AEPD, ya en la lectura anterior), y la hace el centro. Cambian las secciones de: proveedor y contrato, transferencias, retención, tercero nuevo (B lo reduce) y concentración (B la aumenta). Una sola EIPD «dictado por voz» con **el proveedor como anexo** sirve para cualquiera. Lo que no conviene es escribirla sobre Mistral y cambiar después: **art. 35.11**, revisión «al menos, cuando exista un cambio del riesgo».

### 4.3 Exactitud (art. 5.1.d RGPD)

**No puedo decir que una sea más exacta.** Lo que sí consta:

- **[Hecho]** Azure admite **lista de frases** (`phraseList`) en transcripción rápida; Mistral anuncia «context biasing» («custom vocabulary») para Voxtral Mini Transcribe V2 (resumen de su página). Ninguna ventaja sobre la otra en vocabulario clínico sin medirlo.
- **[Hecho]** El **modelo de voz a medida** de Azure (custom speech) admite es-ES con audios etiquetados y textos, pero **no la transcripción rápida** (la tabla de regiones marca el soporte de modelos a medida para tiempo real y por lotes) y, sobre todo, **entrenarlo exige guardar audios y textos**. **[Opinión]** No lo usaría con datos reales; con datos inventados, sería otro tratamiento que evaluar.
- **[Hecho]** Azure ofrece modelos de transcripción **basados en LLM** (LLM Speech, MAI-Transcribe-2; en la UE solo Norte y Oeste de Europa) y el «Azure Transcribe» por defecto. Su página de privacidad de Speech es anterior (marzo de 2026, actualizada en agosto) a la versión «07 2026» de LLM Speech y **no nombra los modelos LLM**: [A verificar] si la frase «no retiene ni guarda» los cubre. **[Suposición]** Un modelo generativo puede «arreglar» o completar lo dicho (el riesgo de inexactitud más peligroso en cifras y fármacos); un modelo clásico, menos. **Hay que medirlo.**
- **[Hecho]** **Catalán.** Azure lista `ca-ES` en transcripción rápida. La página de Voxtral Mini Transcribe V2 lista 13 idiomas con el español pero **sin el catalán** (resumen de su página, a confirmar). **[Decidir]** CJ: ¿van a dictar profesionales del piloto en catalán? La interfaz de ResidApp es solo en español, pero el personal puede hablar catalán entre sí y dictar en esa lengua. Si la respuesta es sí, pesa a favor de B.

**La prueba de precisión (`tools/PruebaDictado`) hoy solo mide Voxtral y Mistral Small** (`tools/PruebaDictado/README.md:5-6`), y **fabrica los audios con la voz sintética de Voxtral** (README, líneas 21 y 51). **[Opinión]** Un audio sintético de Voxtral probablemente favorece a Voxtral. Para comparar de verdad hacen falta: grabaciones **humanas** de voluntarios con frases inventadas, ruido de planta, números y fármacos parecidos, acentos y, si CJ lo pide, catalán; con un mismo conjunto de frases para los dos y un umbral fijado por CJ (ya propuesto en la lectura anterior: cero errores de cifras no detectados por la revisión). Añadir un adaptador de Azure a la herramienta es trabajo de código: no lo hago aquí.

### 4.4 Lo que no cambia con ningún proveedor

- Base jurídica (art. 9.2.h) y proporcionalidad: igual.
- Revisión humana de lo dictado, marca «Sugerido por voz», aviso de no decir nombres, auditoría previa al envío: igual.
- Información al personal y a residentes y representantes; EIPD; autorización del centro (art. 28.2): igual, con el nombre del proveedor correcto.
- Reglamento de IA y producto sanitario: el razonamiento de la lectura anterior (la transcripción de voz a texto no es producto sanitario según la MDCG 2019-11) no depende del proveedor.
- **Voz como dato biométrico:** mientras no se active la diarización (Azure) ni se use la voz para identificar, igual que en la lectura anterior.

## 5. Qué tendría que cambiar en el ADR

Hoy el ADR 0011 y `IaOptions` autorizan solo a Mistral. Para Azure Speech, **[Opinión]**:

1. **Lista de proveedores autorizados.** Por función, no global: hoy `IaOptions.Proveedor` y `ProveedoresAutorizados` son únicos (`IaOptions.cs:15-17`). Habría que separar, por ejemplo, «transcripción» y «salida estructurada», cada una con su lista cerrada. Es un ajuste de configuración (no del dominio): los puertos `ISpeechTranscriber` e `IStructuredGenerator` ya existen y están separados.
2. **Un adaptador nuevo** para `ISpeechTranscriber` (no existe). Tendría que cumplir las mismas reglas que `MistralClient`: dirección fija en el código (para que la clave no vaya a donde diga un ajuste), un solo reintento ante fallo transitorio, registro sin contenido, auditoría previa. **[A verificar]** que los formatos de audio que graba el navegador (el del móvil, en especial) están en la lista de formatos que admite la transcripción rápida (WAV, MP3, OPUS/OGG, FLAC, WMA, AAC, ALAW, MULAW, AMR, WebM, SPEEX): la grabación en un iPhone suele dar MP4/AAC y la página no nombra el contenedor MP4.
3. **El criterio «ser independiente del proveedor no significa tener varios».** **[Opinión]** Sigue valiendo si se formula bien: **un proveedor autorizado por función**, cada uno con su contrato, su EIPD y su ADR. Eso **no es «tener varios»** para la misma tarea. Lo que sí rompería el criterio es mantener **dos** transcriptores en producción (Mistral y Azure para lo mismo): doble contrato, doble EIPD, doble superficie de ataque.
4. **Texto del ADR 0011:** sustituir «un solo adaptador (Mistral)» por la lista por función; mantener «lo que no recoja este ADR no está aprobado» (línea 22); dejar escrito que Azure OpenAI **no** queda autorizado (motivos de la tabla: almacén de abuso y revisión humana) y que se vuelve a abrir solo con la vigilancia modificada aprobada.
5. **ADR 0009:** donde diga «Voxtral», decir «el transcriptor autorizado»; y las condiciones por proveedor (región, retención, modelo con versión fija) pasan a un anexo por proveedor.
6. **Información al centro.** La autorización del art. 28.2 debe nombrar a Microsoft **para el servicio de voz** (hoy el alojamiento ya está), con región, y avisar de que **el audio sale de Spain Central** si el recurso está en otra región. Si el centro ya autorizó a Microsoft solo para base de datos y web, hay que ampliarla por escrito.
7. **Versión del modelo.** Azure «Transcribe» (el modelo por defecto) lo gestiona Microsoft sin versión visible en lo que leí; LLM Speech dice que sus versiones «roll out automatically» y «you don't need to change your client». **[Opinión]** Eso es lo contrario de lo que la lectura anterior pedía (versión fija): un modelo que cambia solo cambia la exactitud sin decisión de nadie. [A verificar] si se puede fijar versión en el modelo clásico; si no, hay que decidir si la prueba de precisión se repite periódicamente.

**¿Un solo proveedor o uno por función?** **[Opinión]** Lo coherente con el ADR es **un proveedor por función y decidir las dos funciones a la vez**. Tres combinaciones:

| Combinación | Qué significa | Mi lectura |
|---|---|---|
| 1. Todo Mistral | Como hoy | Válida, con las condiciones del dossier y de la lectura anterior |
| 2. Voz en Azure Speech, estructura en Mistral Small | El audio va a Microsoft; el **texto** (que sigue siendo dato de salud) va a Mistral | Enfermería (a) usaría **solo** Microsoft; Auxiliar seguiría necesitando el contrato con Mistral. Dos contratos para Auxiliar, uno para Enfermería |
| 3. Todo Azure (Speech + OpenAI para estructura) | Un proveedor | Hereda el problema de C (revisión humana en el almacén de abuso) para el texto; **no la recomiendo** mientras no exista la vigilancia modificada |

## 6. Veredicto

**Para la variante (a) solo transcripción: Azure AI Speech, transcripción rápida (o en tiempo real) con el modelo clásico, en una región UE (Francia Central, Suecia Central, Norte u Oeste de Europa; la elige la dirección con el centro). No Azure OpenAI.** Mantengo Mistral como alternativa válida si la lectura del DPA de Microsoft o la respuesta de Microsoft sobre revisión humana y entrenamiento sale mal, o si se prefiere no concentrar todo en un proveedor con matriz estadounidense.

**Por qué:** la retención está declarada sin trámite, la entidad ya está en la cadena (un encargado menos por autorizar), el ENS lo cubre sobre el papel, y la variante (a) no necesita un segundo modelo, así que no se arrastra el problema de la salida estructurada.

**Condiciones para que el veredicto sea firme (todas, antes de datos reales):**

1. Una persona experta **lee el DPA de Microsoft** (noviembre de 2026 o la versión vigente) y la lista de subencargados, y confirma que cubre datos de salud, transferencias y el aviso de cambios de subencargados.
2. **Confirmación escrita de Microsoft** de tres cosas sobre transcripción rápida o en tiempo real: (i) no hay revisión humana ni almacén de abuso; (ii) el audio y el texto no se usan para entrenar; (iii) los registros de telemetría no incluyen el contenido. Y si el modelo LLM Speech o MAI queda cubierto por la misma frase.
3. Prueba de precisión **con voces humanas** (apartado 4.3) que no sea peor que Voxtral; umbral de CJ.
4. Región elegida y reflejada en la información al centro, en el registro de actividades y en la EIPD.
5. Diarización y entrenamiento de modelos **desactivados**, anotado en el ADR.
6. Todas las condiciones de la lectura anterior (EIPD, información al personal, interruptor por perfil, etc.): el proveedor no las sustituye.

**Decisiones que quedan:**

- **Orlando:** (a) si cambia de proveedor o se queda con Mistral; (b) combinación 1, 2 o 3; (c) tipo de centro piloto, que condiciona la autoridad (AEPD o APDCAT) y si el ENS exige servicios en la nube certificados (**[Hecho]** RD 311/2022, `[op.nub.1.2]`: «los sistemas de información que los soportan deberán ser conformes con el ENS o cumplir con las medidas desarrolladas en una guía CCN-STIC»; el refuerzo `[op.nub.1.r1.1]`, «estos deberán estar certificados», depende de la categoría del sistema); (d) región de producción y si se acepta que el audio salga de España.
- **CJ:** idioma del dictado (¿catalán?), umbral de error, voluntarios para grabar frases con voz humana.
- **DPO real o abogado:** lectura de los dos DPA y de las dos listas de subencargados; transferencias a EE. UU. y validez actual del marco que las ampara; si basta una autorización general del centro a ResidApp para Microsoft; EIPD con el anexo de proveedor; si «Microsoft no retiene» en una página de documentación es suficiente garantía o hace falta cláusula contractual.

## A verificar o decidir

**No he podido verificar (resumen):**

1. **Texto del DPA de Microsoft** (noviembre de 2026): cobertura de datos de salud, papeles, avisos y objeción de subencargados, transferencias, supresión, medidas de seguridad. Descargado pero ilegible con las herramientas disponibles.
2. **Lista de subencargados** de Microsoft y de Mistral, y su ubicación.
3. **Revisión humana, entrenamiento y telemetría** de Azure Speech: la página de privacidad de Speech calla; lo que dice de retención solo cubre tiempo real, transcripción rápida, evaluación de pronunciación y traducción, y no nombra LLM Speech ni MAI-Transcribe.
4. La cifra de **«30 días»** de la vigilancia de abuso en Azure OpenAI: no aparece en las páginas leídas (data-privacy de mayo de 2026 y abuse-monitoring de mayo de 2026).
5. Si el **audio** entra en el almacén de abuso de Azure OpenAI.
6. Si ResidApp **cumple los requisitos** para la vigilancia modificada (clientes «managed by a Microsoft account team or under an eligible program»).
7. **Certificados** ENS e ISO reales y su vigencia (Microsoft los declara; no leí los certificados); certificaciones de Mistral.
8. Que `gpt-4o-transcribe` esté en **GA**, no en preview (la página de modelos lo marca «Preview»); estado de `whisper` 001 y de `gpt-transcribe`.
9. Idiomas de Voxtral (catalán): resumen de su página, no texto literal.
10. Que el **formato de audio** de los móviles sea admitido por la transcripción rápida.
11. Estado actual del **Marco de Privacidad de Datos UE-EE. UU.**: no lo he consultado.
12. Si hay guía de la **AEPD o la APDCAT** sobre transcripción por voz con proveedores de IA o cloud en sanidad: no hay nada en `normativa-local/`; no la he buscado en la web de las autoridades.
13. Ubicación física exacta de «West Europe» y «North Europe» (la sé por conocimiento general, no la leí): importa si el centro exige un país concreto.
14. Condiciones comerciales (Product Terms de Microsoft; términos de Mistral): no leídas.
15. Que `MistralClient` cumpla todo lo que el dossier dice (no lo he releído en esta consulta).

**Yo no soy DPO designado ni abogado.** Todo lo anterior lo valida una persona antes de usarlo.
