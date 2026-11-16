# Ficha de alergias, enfermedades y medicación: lectura de protección de datos antes de diseñar el esquema SQL

**BORRADOR. Lo valida una persona antes de usarlo con datos reales: el DPO real o un abogado en lo jurídico, y el médico responsable en lo clínico.** Fecha: 2026-11-15. Segunda lectura experta del agente `dpo-residapp`, no un dictamen ni una designación de DPO. Es una consulta **previa al esquema SQL**: no se ha construido nada y este documento no modifica código, ADR ni otros documentos. No contiene datos de personas reales. Las respuestas de CJ (`respuestas-cj-local\`, carpeta que git ignora) las resumo con mis palabras: no copio sus comentarios, porque el repositorio es público.

Etiquetas: **[Hecho]** verificado en el repositorio (archivo y línea) o en el texto literal de `normativa-local/` (índice generado el 2026-11-14; la comprobación más antigua es del 2026-11-12; son textos consolidados, no la versión oficial); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no confirmado en fuente oficial; **[Validar]** punto que debe revisar una persona experta antes de darlo por bueno. Las definiciones de los términos (dato de salud, responsable, encargado, EIPD, exactitud, versionado, seguridad por filas…) están en el apartado inicial de [`antecedentes-medicacion-alergias-lectura-dpo.md`](antecedentes-medicacion-alergias-lectura-dpo.md) y no las repito.

## Conclusión

**Admisible con condiciones.** No veo una prohibición legal. Lo que CJ pide (alergias, enfermedades con su año y medicación habitual, anotadas al ingreso y usadas en el día a día y en la derivación a Urgencias) es contenido clásico de la historia clínica: la Llei 21/2000, art. 10.1.b, incluye entre los datos clinicoasistenciales los «antecedentes familiares y personales, fisiológicos y patológicos» y las «hojas de tratamiento médico» (`llei-21-2000.txt:98, 102`). La base jurídica es la misma que ya tiene el resto del registro asistencial (apartado 1.2).

Lo que cambia es **qué guarda ResidApp**. Hasta hoy no guarda nada de esto (apartado 1.1). Con la ficha pasa a conservar, versionar y reutilizar listas vivas de diagnósticos y fármacos, y eso obliga a corregir las promesas escritas (política de privacidad, ADR 0011, ADR 0015, alcance) y a cumplir condiciones que antes no hacían falta.

**El riesgo más importante es la exactitud, no la privacidad.** Una medicación o una alergia desactualizada, impresa en un informe firmado que ya no se puede corregir y entregado a Urgencias, es a la vez un riesgo clínico y un incumplimiento del art. 5.1.d RGPD. CJ no quiere revisión periódica y no la exijo: el RGPD no la impone (apartado 1.4). Lo que sí exijo es que **el dato diga la verdad sobre su edad y su origen**: fecha y perfil de la última modificación por apartado, versiones que no se pisan, estados sin valor por defecto y un texto en el informe que **no diga «revisado»** cuando nadie ha revisado nada.

**Cuatro cosas que conviene corregir antes de construir:**

1. **La frase de cautela que CJ aceptó dice «revisado el (fecha) por (perfil)». Sin revisión, esa frase sería falsa** y daría una seguridad que el dato no tiene. Propongo un texto distinto (apartado 4.3) y CJ debe darle el visto bueno.
2. **«No refiere» y «No consta» deben imprimirse distintos.** Lo que CJ explicó (se asume que no hay alergia si nadie la ha documentado) es razonable como criterio clínico dentro del centro, pero en Urgencias la diferencia importa: en un caso alguien preguntó; en el otro nadie pudo confirmarlo. La aclaración 1.1 que ya se le ha enviado a CJ va en esa dirección.
3. **El apartado de medicación es el de mayor riesgo y el que contradice más promesas** (alcance, política, ADR 0011 y 0015). Recomiendo diseñar los tres apartados juntos, porque la estructura es la misma, pero **activar la medicación la última y solo cuando estén corregidos esos documentos** (apartado 7).
4. **Nada de esto se usa con residentes reales sin las condiciones del apartado 5.3** (base jurídica por escrito, información a residentes, registro de actividades, EIPD, procedimiento del centro, entre otras).

## 1. Valoración RGPD y LOPDGDD

### 1.1 Qué hay hoy y qué cambia [Hecho]

- ResidApp **no guarda** medicación, enfermedades ni alergias en ningún esquema, dominio ni pantalla. Lo único parecido es el basal, que recoge deterioro cognitivo con su causa y estadio (`docs/producto/antecedentes-medicacion-alergias-propuesta.md:11`).
- El alcance deja fuera la «administración de medicación» (`docs/producto/alcance.md:45`) y declara que ResidApp no sustituye la historia clínica oficial (`alcance.md:38`). La política de privacidad dice a las familias y al personal: «ResidApp **no** guarda la medicación de las personas residentes ni su administración» (`src/ResidApp.Web/Views/Home/Privacy.cshtml:54`). El ADR 0011 repite que «ResidApp no guarda medicación» (`0011-arquitectura-ia.md:75-77`).
- Hoy el profesional **puede** escribir esos datos a mano, una derivación cada vez, en «Información adicional para Urgencias», con una ayuda que lo pide y que añade «ResidApp no guarda estos datos en otro sitio» (`src/ResidApp.Web/Views/Shared/_Derivacion.cshtml:39-44`). Ese texto quedaría falso con la ficha.
- Diferencia de fondo: el texto libre de una derivación **muere con el informe**; la ficha es un dato **vivo** que se conserva, se versiona, se lee en cada pantalla clínica y se copia en cada informe. Es un tratamiento nuevo, no una mejora del campo.

### 1.2 Base jurídica y condición del art. 9.2 [Validar]

Las fija la residencia (responsable). Lo que propongo para que lo contraste su DPO o su abogado:

- **Art. 9.2.h RGPD** (con el 9.3): tratamiento necesario para «diagnóstico médico, prestación de asistencia o tratamiento de tipo sanitario o social, o gestión de los sistemas y servicios de asistencia sanitaria y social», que puede hacer un profesional «sujeto a la obligación de secreto profesional, o bajo su responsabilidad» (`rgpd.txt:124, 127`). **La 9.2.c** (intereses vitales cuando la persona no puede consentir; `rgpd.txt:119`) refuerza el caso de la urgencia, pero no sustenta por sí sola una ficha permanente. **No se pide consentimiento**: la asistencia no se apoya en él.
- **Norma con rango de ley.** La LOPDGDD, art. 9.2, exige que los tratamientos del 9.2.h fundados en el Derecho español estén amparados en una norma con rango de ley (`lopdgdd.txt:106`). Su disposición adicional 17.ª, 1.c), cita la Ley 41/2002 (`lopdgdd.txt:814`). En Cataluña, los arts. 9 a 12 de la Llei 21/2000 regulan el contenido y la conservación de la historia clínica (`llei-21-2000.txt:79-134`).
- **[A verificar, importante]** La lista de la disposición adicional 17.ª **no incluye** la Llei 12/2007 ni la Ley 39/2006 (`lopdgdd.txt:811-821`). Si la residencia es un servicio social y no un centro sanitario, para la parte de «asistencia social» del 9.2.h un abogado debe señalar la norma con rango de ley que lo ampara. Es la misma pregunta abierta de siempre: si una residencia cuenta como «centro sanitario» a efectos de la Ley 41/2002 y la Llei 21/2000 (`antes-de-datos-reales.md`, apartado 11). **Tiene más peso ahora**: con la ficha, ResidApp guarda contenido que la Llei 21/2000 define como historia clínica. Si la residencia es centro sanitario obligado a mantener historias clínicas, además aplica la designación obligatoria de DPO (LOPDGDD art. 34.1.l, `lopdgdd.txt:277`).
- **Autoridad de control.** Según el art. 3 de la Llei 32/2010 la APDCAT cubre, entre otros, a las instituciones públicas y a las entidades privadas que presten servicios públicos «mediante cualquier forma de gestión directa o indirecta» en lo vinculado a esos servicios (`llei-32-2010.txt:25-38`, apartados a, e, f, h). Una residencia pública o concertada probablemente cae en la APDCAT; una privada sin concierto, en la AEPD. **[Suposición]** Hace falta saber el tipo de centro piloto (público, concertado o privado y si todas las plazas están concertadas); no lo sé. Si es público o concertado, además aplica el ENS (RD 311/2022).
- **Quién fija el contenido.** CJ lo decide como Product Owner, no como responsable del tratamiento. El art. 28.3.a RGPD exige que el encargado trate los datos según «instrucciones documentadas» del responsable (`rgpd.txt:330`). **La residencia piloto debe aceptar por escrito** qué se registra, quién edita y que ResidApp no valida el contenido clínico (no hay catálogo de fármacos ni de diagnósticos). Eso entra en el contrato de encargado y en su anexo.
- **Fines permitidos:** asistencia, continuidad asistencial y derivación. **No valen:** estadística de ResidApp, mejora del producto, entrenamiento de modelos ni listados cruzados («¿quién toma tal fármaco?»), que serían otra finalidad (art. 5.1.b, `rgpd.txt:74`).

### 1.3 Minimización (art. 5.1.c y art. 25.2)

El art. 5.1.c pide datos «adecuados, pertinentes y limitados a lo necesario» (`rgpd.txt:75`) y el art. 25.2, que por defecto solo se traten los necesarios y que no sean accesibles «a un número indeterminado de personas físicas» (`rgpd.txt:310`).

**Mi recomendación anterior era acotar las enfermedades (tope de elementos y solo las relevantes para una urgencia). La retiro en lo esencial**: CJ explica que la lista completa de enfermedades e intervenciones es útil para el seguimiento diario y para derivar, y que no importa que crezca. Es una apreciación clínica que no me corresponde rebatir; además, esos antecedentes son contenido propio de la historia clínica. Acepto la lista completa **con estas condiciones de forma**, que no limitan lo clínico:

| Apartado | Admisible | No admisible |
|---|---|---|
| Alergias | Estado + fármaco o grupo + reacción breve (obligatoria en intolerancia, opcional en alergia, a confirmar con CJ). Etiqueta precisa: «**alergias a medicamentos**» | Alergias no medicamentosas (látex, alimentos, contrastes) sin que CJ lo decida expresamente; narrativa |
| Enfermedades e intervenciones | Nombre corto + año (el año puede ser «no consta»); sin tope clínico de elementos | Antecedentes **familiares** (son datos de otras personas y CJ no los pide); narrativa; valoraciones subjetivas; nombres de terceros |
| Medicación habitual | Nombre, dosis y frecuencia u hora | Lo que convierte la lista en administración: registro de «dado o no dado», alertas de toma, vía, fecha de inicio o fin, prescriptor, motivo de la prescripción, adherencia («no se la toma» dicho por un familiar), stock (apartado 5.2) |
| Todos | Fuente **opcional** y por categoría («informe médico», «receta electrónica», «el propio residente», «familia o cuidadores») | Nombres de quién informó. La CTMDC recomienda citar la fuente «solo en el grado de detalle necesario» y minimizar lo aportado por terceros (`ctmdc-informacio-tercers.txt:82-92`); además el derecho de acceso del residente no puede perjudicar la confidencialidad de terceros (Llei 21/2000 art. 13.2, `llei-21-2000.txt:145`) |

- **Lo que se imprime en Urgencias.** **[Opinión]** Imprimir **todo** lo registrado es defendible: Urgencias es destinatario con finalidad asistencial y deber de secreto, y omitir un dato puede causar daño. Condición: los residentes y representantes deben saber que la lista completa viaja en cada derivación (apartado 3). **[Decidir]** DPO: si hay diagnósticos de especial sensibilidad (salud mental, infección por VIH, adicciones, violencia) que merezcan un tratamiento distinto en el PDF; mi opinión es que no hace falta un filtro, pero el equipo debe saber que todo se imprime.
- **Obligatorio no es lo mismo que inventar.** El apartado se pide siempre, pero «No consta» es una respuesta legítima y no puede penalizarse (apartado 4.1).
- **Una fila por elemento, sin texto libre largo.** Elementos cortos, estructurados, con límite de longitud. Evita que la ficha se convierta en una mini historia clínica con nombres de familiares.

### 1.4 Exactitud (art. 5.1.d) sin revisión periódica

El art. 5.1.d exige datos «exactos y, si fuera necesario, actualizados» y «todas las medidas razonables» para rectificar sin dilación los inexactos (`rgpd.txt:76`); el responsable debe demostrarlo (art. 5.2, `rgpd.txt:79`). **No impone una revisión periódica**: actualizar cuando ocurre algo (nuevo diagnóstico, nueva prescripción, alta hospitalaria, nueva alergia) es una medida razonable **si hay forma de saber cuándo se hizo y quién lo hizo**. Por eso respeto el criterio de CJ y exijo otra cosa: transparencia sobre la edad del dato.

El Reglamento (UE) 2025/327 (Espacio Europeo de Datos de Salud), aunque todavía no es aplicable a esto, va en la misma línea: «cuando se registren o actualicen datos de salud electrónicos personales, en las historias clínicas electrónicas se indicará el profesional sanitario y el prestador de asistencia sanitaria que han realizado dicho registro o actualización, así como el momento» (art. 13, último párrafo; `ehds.txt:291`). Y la Llei 21/2000, art. 9.3: «debe garantizarse que quedan registrados todos los cambios e identificados los médicos y los profesionales asistenciales que los han realizado» (`llei-21-2000.txt:82`).

**Mitigaciones mínimas que exijo (ninguna choca con lo que quiere CJ):**

| # | Mitigación | Por qué |
|---|---|---|
| 1 | **Estados explícitos, sin valor por defecto.** Un apartado sin rellenar es «**pendiente**» (no hay fila); es distinto de «No refiere» y de «No consta». La aplicación nunca los rellena sola ni preselecciona un valor | Evita que «nadie lo ha mirado» se lea como «no hay». Es el criterio del basal: `NO_DOCUMENTADO` no se interpreta como normalidad (`gestion-basal-barthel.md:28`) |
| 2 | **Fecha y perfil de la última modificación, por apartado** (no un solo sello para toda la ficha), generados por la propia versión, no por un acto manual de «revisar» | Si una alergia se actualiza ayer y la medicación hace dos años, un único sello haría parecer fresca la medicación |
| 3 | **Versiones que se añaden y no se pisan**: cada guardado crea una versión con cuenta, perfil, fecha y motivo de una lista cerrada | Llei 21/2000 art. 9.3; patrón del basal y de `informes_derivacion` (`0016_derivacion_urgencias.sql:56-57`) |
| 4 | **Antigüedad visible, no caducidad.** En pantalla y en el PDF consta «última modificación el…». No hay aviso rojo, ni bloqueo, ni deber de revisar: es un dato, no una regla | Honestidad del dato; respeta la decisión de CJ |
| 5 | **Control de concurrencia.** Dos personas que guardan a la vez no pisan una la otra en silencio (la segunda ve que ya hay una versión nueva) | Pérdida de datos clínicos por una carrera; riesgo real con listas largas |
| 6 | **Firmar lo que se vio.** La vista previa del informe muestra las versiones de la ficha; al firmar se comprueba que siguen siendo las mismas y, si no, se vuelve a mostrar | El informe firmado no se corrige (`0016:9`); lo que se previsualiza debe ser lo impreso |
| 7 | **No bloquear la derivación** si la ficha falta o está incompleta (CJ lo decidió: se deriva y el informe advierte) | La regla del proyecto es que documentar no retrasa la atención (`0016:18-19`) |
| 8 | **Texto del informe honesto** (apartado 4.3) | Art. 5.1.a (lealtad y transparencia) y 5.1.d |

**Recomendable, no exigido [Decidir CJ]:** un recordatorio sin obligación en un momento natural (por ejemplo, al cerrar un evento con derivación, que es cuando suelen cambiar la medicación y los diagnósticos): «Si ha cambiado algo, actualiza la ficha». No es revisión periódica; si CJ lo considera carga, se omite.

**Quién responde de que esté al día** es la residencia (responsable, art. 5.2): necesita un procedimiento propio para trasladar a ResidApp lo que cambia en la receta electrónica o en la hoja de tratamiento. **ResidApp no regula la organización del centro** (criterio de Orlando, 2026-11-10): lo que sí puede hacer, y hace con las mitigaciones de arriba, es no esconder la edad del dato y avisar de la limitación en el contrato y en el informe. **[Validar]** el DPO real debe confirmar que ese reparto basta ante una inspección.

### 1.5 Conservación (art. 5.1.e) [Decidir]

- **[Hecho]** El plazo por defecto del proyecto es de 15 años desde la baja del residente (`src/ResidApp.Domain/Residents/ResidentDischarge.cs:25`), basado en el art. 12 de la Llei 21/2000, que fija 15 años desde el alta de cada proceso asistencial para consentimientos, informes de alta y de pruebas, entre otros (apartado 4), y permite destruir «el resto» a los 5 años (apartado 6) sin perjuicio de lo que la comisión técnica considere relevante a efectos asistenciales (apartado 7; `llei-21-2000.txt:124-134`). El cuadro de la CTMDC recomienda 15 años para el curso clínico (`ctmdc-terminis-conservacio.txt:43-48`). Las «hojas de tratamiento» y los antecedentes no figuran en la lista de los 15 años: **[A verificar]** qué plazo corresponde a la ficha. **[Opinión]** Un plazo único, prudente y coherente con el resto de lo clínico (15 años desde la baja) es defendible; el DPO real puede acortarlo.
- **Qué entra en ese plazo:** la ficha viva, todas sus versiones, la copia dentro de cada informe firmado (columna, JSON y PDF) y las copias de seguridad. Ni el borrado selectivo ni la corrección de un informe firmado son posibles sin romper su huella; se tratan como el resto del historial.
- **Versiones anteriores y art. 32 LOPDGDD.** El art. 32 obliga a «bloquear» los datos cuando se rectifican o suprimen: identificarlos y reservarlos «para impedir su tratamiento, incluyendo su visualización» salvo para jueces, Ministerio Fiscal y autoridades (`lopdgdd.txt:246-247`). Contrapeso: la Llei 21/2000 obliga a dejar registrados todos los cambios. **[Opinión]** Las versiones anteriores se conservan, pero **no se muestran en las pantallas ordinarias ni en ningún PDF**; solo en una pantalla de historial con permiso, como el historial del basal. **[Validar]** si eso cuenta como bloqueo suficiente. Es la misma decisión abierta que ya consta para los familiares (borrar frente a bloquear).
- Plazo y vía de borrado de la tabla nueva entran en el catálogo de expurgo (`expurgo-y-supresion.md:95-98`): **[Hecho]** esa prueba todavía no existe; debe cubrir la ficha.

## 2. Quién ve y quién edita, y cómo se audita

### 2.1 Propuesta por perfil [Opinión, a decidir]

La ficha es del residente, no de la unidad ni del evento (como el basal): al trasladarse de unidad la ve el equipo nuevo según las reglas de su ámbito.

| Perfil | Ver | Editar | Motivo |
|---|---|---|---|
| Auxiliar | **No, por defecto** | No | Minimización. Hoy lee un resumen del basal (`src/ResidApp.Application/Authorization/ResidentBaselinePolicy.cs:140-148`) y la ficha **no debe heredar esa regla sin decidirlo**: sus tareas no dependen de la medicación (la administración está fuera de alcance). Si CJ ve una necesidad concreta, se concede como permiso, no como regla |
| Enfermería | Sí, en su ámbito | Sí, **con permiso propio**; CJ la quiere como quien la rellena en el alta | Mismo patrón que `BASELINE_INITIAL_COMPLETE` (`src/ResidApp.Domain/Accounts/ProfilePermissions.cs:14, 28`). Enfermería ya puede dar de alta residentes (`RESIDENT_IDENTITY_CREATE`, línea 13) |
| Medicina | Sí, en su ámbito | **Sí, con permiso propio** (aclaración 1.2, contestada por CJ el 2026-11-15: Enfermería la rellena y la cambia, y Medicina también puede, por ejemplo para corregir una duda; el permiso es el mismo, `HEALTH_BACKGROUND_EDIT`). *Al redactar este borrador estaba pendiente.* | Edita quien tiene la responsabilidad clínica; CJ describe la validación de Medicina como consulta ante una duda |
| Dirección Clínica | **Solo con permiso de lectura clínica, finalidad declarada y auditoría previa** | No | Ver abajo |
| Administración | **No** | No | Solo accede a lo que su función requiere (Llei 21/2000 art. 11.4, `llei-21-2000.txt:117`; `alcance.md:28`). El alta administrativa deja el basal pendiente y no lo completa (`docs/historias-usuarios/administracion.md:17`); la ficha igual |
| Plataforma | No | No | No ve contenido clínico (`alcance.md:30`) |
| Familia (Portal) | **No**, en ninguna pantalla | No | Es contenido clínico interno (`alcance.md:27`; `Privacy.cshtml:65`). La familia **puede aportar** un dato hablando con Enfermería; no se le devuelve por el portal |
| IA (Mistral o cualquier otro) | **No** | No | ADR 0011 (apartado 6 de este documento) |

- **Permisos de edición apagados por defecto**, que concede Administración por perfil, como los del basal (art. 25.2 RGPD). **[Hecho]** Un permiso nuevo exige tocar `ProfilePermissions` y el disparador de catálogo `TR_pp_profile_catalog` (`ProfilePermissions.cs:8-10`). Una consecuencia práctica: si «obligatorio» se interpreta como «Enfermería lo rellena en el alta», el centro **debe haber concedido ese permiso**; si no, la ficha queda «pendiente».
- **«Medicina valida si hay duda» no debe ser un estado de la aplicación.** Un «validado» impreso se leería como garantía clínica. Basta con la autoría de cada versión (cuenta y perfil). **[Opinión]**
- **Ficha por residente y por centro.** Si el residente pasa a otro centro (otro responsable), la ficha no viaja sola: sería una comunicación entre responsables.

### 2.2 Dirección Clínica: sí necesita finalidad declarada [Hecho / Opinión]

El detalle clínico de Dirección ya exige permiso `CLINICAL_DETAIL_READ`, finalidad declarada y auditoría previa (`ResidentBaselinePolicy.cs:171-189`). **La ficha debe seguir exactamente esa regla**: es un tipo de recurso nuevo (`ClinicalResourceType`), con la misma declaración. Dos razones: coherencia (sería absurdo que Dirección viera con motivo el basal y sin motivo la medicación) y que el art. 25.2 pide limitar la accesibilidad por defecto. Dirección ya recibe el PDF de derivación con declaración de finalidad (`src/ResidApp.Application/Ports/IReferralReportRepository.cs:35-39`); no gana acceso nuevo.

### 2.3 Auditoría

- **Cambios (sí, siempre).** Cada versión nueva deja una fila de auditoría con **solo identificadores** (cuenta, perfil, centro, unidad, residente, apartado, versión y acción), **sin el texto de la ficha**: el texto ya está en la versión, y el listado clínico de DIR-14 declara que no lleva texto clínico ni valores (`src/ResidApp.Application/Ports/ClinicalTraceability.cs:5-8`). Una acción nueva exige alta en la lista cerrada y su etiqueta en español, que comprueba un test (`ClinicalTraceability.cs:19-21`).
- **Lecturas de Dirección:** auditadas con su finalidad, como el resto.
- **Lecturas del personal asistencial (Enfermería, Medicina): hoy no se auditan** para el basal (`src/ResidApp.Application/Authorization/RequestAuthorizationContext.cs:256-259`). **[Decidir, DPO real]** Si se auditarán las lecturas es una decisión de todo el producto (lo señalé en la lectura de 2026-11-11, con el criterio de la AEPD de julio de 2026, a verificar). **[Opinión]** No crear una excepción solo para la ficha: que siga la decisión general. Con listas de diagnósticos y fármacos, el argumento para auditar la lectura es algo más fuerte.
- **Descarga del PDF:** ya se audita (`REFERRAL_REPORT_DOWNLOAD`, `ClinicalTraceability.cs:35`).
- **Registros técnicos y telemetría:** el texto de la ficha no puede aparecer en ellos ni en mensajes de excepción.

## 3. Impacto sobre documentos y obligaciones

| Documento | Qué cambia |
|---|---|
| **Política de privacidad** (`Privacy.cshtml:48, 54`; `politica-privacidad-portal.md:59, 67, 111-117, 462`) | Añadir la ficha a «Salud y cuidados» y a los destinatarios (los servicios de urgencias, que hoy no figuran); **sustituir** «ResidApp no guarda la medicación» (línea 54) por una frase que siga siendo cierta. Textos propuestos abajo |
| **ADR 0011** (líneas 75-77) | «ResidApp no guarda medicación» deja de ser cierto. El criterio de que solo CJ reabre lo descartado (`0011:66`) **se cumple**, porque CJ ha decidido la ficha; **no se cumple** para administración, alertas ni cruces, que siguen descartados |
| **ADR 0015** (líneas 34, 141-143) | La frontera con el futuro módulo «Medicamentos»: la ficha de medicación es su primer escalón. Las cautelas anotadas allí **ya aplican a este apartado**: fuente única de verdad frente a los sistemas oficiales, no verse en el Portal Familiar, incompatible con el dictado y «un listado desactualizado en solo lectura es un riesgo clínico». **[Decidir, Orlando con CJ]** si la lista de medicación va en la base (como las alergias) o en el módulo |
| **`alcance.md`** (líneas 10, 14, 38-39, 45) | Incluir la ficha en Residentes y Derivación; aclarar que es una lista informativa de lo que el profesional documenta, que no es administración de medicación y que no genera alertas |
| **`antes-de-datos-reales.md`, apartado 11** | Orlando decidió el 2026-11-13 «alergias primero; las tres cosas, por fases»; CJ ha elegido las tres. **[Decidir, Orlando]** si acepta el cambio y el orden de activación (recomiendo medicación la última) |
| **Información a residentes y representantes** | Cláusula nueva (abajo). Art. 14.1.d y 14.2.f RGPD: categorías de datos y **fuente** cuando no vienen del interesado (`rgpd.txt:180, 191`), plazo máximo de un mes (`rgpd.txt:194`) |
| **Información al personal** | Que cada cambio queda registrado con su cuenta y su perfil; que no sirve para evaluar |
| **Registro de actividades (art. 30)** | Entrada nueva (abajo). No hay un registro único: modelo `historial-tras-traslado-01-registro-actividades.md` |
| **EIPD** | Ver abajo |
| **Contrato de encargado y su anexo** | Nueva categoría de datos; instrucción documentada del contenido; prohibición de uso propio por ResidApp (art. 28.10 si no) |
| **Catálogo de expurgo** (`expurgo-y-supresion.md:95-98`) | Tablas nuevas |
| **Manual, Guía por perfil y guías de CJ** | Consultar al agente `doc-usuario-residapp`: el texto de `_Derivacion.cshtml:39-44` y el manual cambian |

**Textos propuestos [Opinión; Validar]**, en lenguaje llano. Los campos entre corchetes los completa la residencia.

*Política de privacidad, apartado «Personas residentes» (sustituye la frase de la línea 54):*

> **Antecedentes de salud.** El personal de Enfermería (y de Medicina, si el centro lo permite) anota una ficha con tres cosas: las alergias a medicamentos y las intolerancias, las enfermedades e intervenciones quirúrgicas con su año, y la medicación habitual (nombre, dosis y cuándo se toma). Sirve para atender a la persona cada día y para entregar esa información al servicio de urgencias si hay que derivarla. La ficha **no se revisa de forma periódica**: se actualiza cuando ocurre algo. ResidApp **no** guarda la administración de la medicación (qué se da y cuándo), **no** avisa de alergias ni de interacciones y no decide nada por el personal. Esta ficha **no se muestra en el Portal Familiar**.

*Destinatarios:* «Si el centro deriva a la persona a un servicio de urgencias o de emergencias, ese servicio recibe el informe de derivación, que incluye esta ficha. Desde ese momento, el servicio es responsable de su copia.»

*Cláusula de ingreso para residentes y representantes (art. 13 y 14):*

> Para atenderle, el centro [RESIDENCIA] anotará sus alergias a medicamentos, sus enfermedades e intervenciones y la medicación que toma. Esos datos los obtendremos de usted, de su familia o cuidadores, de su informe del médico de cabecera y de su receta electrónica. Los verá solo el personal sanitario del centro que le atiende y, si hay que trasladarle a Urgencias, se entregarán al servicio que le atienda. Los conservaremos [15 años desde su baja: a confirmar por el DPO]. Puede pedirnos acceso, rectificación o los demás derechos escribiendo a [DPO / contacto], y reclamar ante [AEPD / APDCAT].

*Personal:* «Cada cambio en la ficha queda anotado con tu cuenta, tu perfil y la fecha, para saber quién anotó qué y cuándo. No se usa para evaluar tu trabajo.»

**Registro de actividades (propuesta de fila) [Validar]:**

| Campo | Contenido |
|---|---|
| Tratamiento | Ficha clínica de referencia del residente (alergias e intolerancias, enfermedades e intervenciones, medicación habitual) |
| Finalidad | Asistencia y continuidad asistencial; entrega a servicios de urgencias en una derivación |
| Interesados | Residentes (colectivo vulnerable); profesionales (autoría de cada cambio) |
| Datos | Salud (categoría especial); cuenta y perfil de quien registra |
| Base | Art. 6.1 [b/c/e, según el centro] y art. 9.2.h con 9.3; 9.2.c como apoyo en urgencia |
| Destinatarios | Personal sanitario del centro con permiso; servicios de urgencias (responsables independientes de su copia); encargado ResidApp y subencargado Azure. **Mistral: ninguno** |
| Plazo | [15 años desde la baja: a confirmar] |
| Medidas | Seguridad por filas, permisos mínimos, tablas de solo inserción, auditoría sin contenido, sin acceso de Auxiliar, Administración, Plataforma ni familia, sin IA |

**EIPD.** **[Hecho]** No existe ninguna EIPD que actualizar (`docs/producto/roadmap.md:49`) y la general es obligatoria de todos modos: la lista de la AEPD recoge categorías especiales (punto 4), gran escala (7) y mayores con algún grado de discapacidad o personas que acceden a servicios sociales (9), y basta con dos criterios para presumir la necesidad (`aepd-lista-eipd.txt:38-43, 65-82`). **La ficha no necesita una EIPD propia: entra como un anexo de la general**, pero **con análisis real de dos riesgos que el anexo del basal no tenía**: (1) **exactitud y seguridad del paciente** (daño físico por un dato desactualizado o mal transcrito, copia duplicada de la receta electrónica); (2) **sensibilidad por inferencia** (una lista de fármacos revela diagnósticos). Hay que **abrir una EIPD propia, o rehacer el anexo,** si cambia algo de esto: alertas, cruces o bloqueos (apartado 6), dosis con horarios o registro de administración, IA, acceso de Auxiliar o familia, envío automático o integración con sistemas externos, uso agregado. El responsable es la residencia; ResidApp aporta el anexo técnico. **[Suposición]** La consulta previa del art. 36 no parece necesaria si se aplican las medidas; lo decide el DPO.

## 4. Riesgos de lo obligatorio, de la copia duplicada y texto del informe

### 4.1 Riesgos del carácter obligatorio

| Riesgo | Mitigación |
|---|---|
| **Rellenar por trámite.** Con los tres apartados obligatorios, aparece la tentación de marcar «No consta» o «No refiere» para pasar | Sin valor por defecto; «No consta» es una respuesta legítima y no se penaliza; **no se mide ni se compara por profesional** (el alcance excluye rankings nominativos, `alcance.md:47`) |
| **Obligatorio como obstáculo.** Que la ficha bloquee el ingreso, el basal o una derivación pondría el trámite por encima de la atención | **No bloquea nada.** «Obligatorio» significa que se pide siempre y que, si falta, la pantalla lo muestra como «pendiente». CJ ya aceptó que se deriva igualmente |
| **«Pendiente» confundido con «No consta».** Son cosas distintas: en uno nadie ha preguntado | Apartado sin fila = pendiente; el informe lo imprime distinto (4.3) |
| **Residentes ya dados de alta antes de la ficha** (todos los de pruebas, y los que carguen el día del piloto) | Quedan «pendientes»; no se rellenan de forma automática ni con datos del basal |
| **Estados incompletos.** CJ definió cuatro estados para alergias, pero no dijo cómo se registra «ninguna enfermedad conocida» o «no toma medicación habitual» | Los tres apartados necesitan la misma lógica: lista, «refiere que no hay» y «no consta». **[Preguntar a CJ]** |
| **Alergia e intolerancia a la vez.** En la práctica un residente puede tener ambas, y los «cuatro botones» sugieren que se elige solo una | Modelar las intolerancias como elementos propios que pueden convivir con «No refiere alergias»; **[Preguntar a CJ]** |

### 4.2 Riesgo de la copia duplicada de la hoja de tratamiento y de la receta electrónica

- **[Hecho]** CJ explica que la fuente más fiable de la medicación es la receta electrónica, contrastada con lo que dicen la familia o el propio residente (que a veces toma menos o de otra forma) y que, si hay discrepancias, la receta se corrige. La ficha **no es esa fuente**: es una copia manual de segunda mano. **[Hecho]** Un cambio de pauta hecho en la receta o en la hoja de tratamiento no aparece solo en ResidApp.
- **[Opinión]** Es el riesgo más probable de inexactitud y no se elimina con tecnología. Mitigaciones: edad del dato visible por apartado (1.4), frase de cautela que remite a la fuente oficial (4.3), medicación como el apartado que más se actualiza y se activa el último, y que el contrato diga que la residencia es responsable de la exactitud y de su procedimiento de actualización. Una integración con la receta electrónica está fuera de alcance (`alcance.md:49`) y abriría una EIPD propia.
- **El informe firmado no se corrige** (`0016:9`): un error en la ficha en el momento de firmar queda impreso. Los errores posteriores se resuelven llamando al servicio receptor y dejándolo escrito en el protocolo del evento; **[Suposición]** lo confirma quien construya.

### 4.3 Texto del informe: la frase de CJ y mi propuesta

CJ aceptó: «Dato registrado en la ficha del residente; revisado el (fecha) por (perfil). Contrastar con la hoja de tratamiento oficial del centro.» **Con la decisión de no revisar, «revisado» sería falso.** Mi propuesta, que CJ debe validar porque cambia un texto ya aceptado:

> **Registrado en la ficha del residente; última modificación el (fecha) por (perfil).** Esta ficha no se revisa de forma periódica. Contrastar con la hoja de tratamiento oficial del centro y con el residente o su familia.

- **Una línea por apartado**, cada una con su fecha (no una única para toda la ficha). Se imprime el **perfil**, no el nombre de la persona: menos datos del personal en un documento que sale del centro.
- **Alergias, redactadas así** (etiqueta siempre «alergias a **medicamentos**»; ver la aclaración 1.1 de CJ):
  - Con entradas: «Alergia a: (fármaco)» / «Intolerancia o efecto adverso: (fármaco), (motivo)».
  - No refiere: «**No refiere alergias a medicamentos** (según el residente o su familia)».
  - No consta: «**No consta: información no fiable o sin confirmar.** No equivale a «sin alergias»».
  - Pendiente: «**Ficha pendiente de rellenar en este centro: no hay datos registrados.** No equivale a «sin alergias»».
- **Enfermedades y medicación: la misma lógica.** «No consta» y «pendiente» nunca se imprimen como «ninguna».
- **Pie del informe [Opinión]:** «Contiene datos de salud; uso exclusivo para la atención de esta persona.»
- Si **CJ elige imprimir igual «No refiere» y «No consta»**, lo documento como decisión suya en la EIPD; yo la desaconsejo.

## 5. Requisitos para el esquema SQL

### 5.1 Qué debe contener el diseño

Proponer nombres definitivos es cosa de quien construya; lo siguiente son requisitos, no un esquema cerrado. El siguiente script libre es el 0094 (el último en `database/scripts/` es el `0093`; **[A verificar]** al construir).

1. **Una cabecera de versión por apartado y guardado**, y **una fila por elemento** (alergia, enfermedad, fármaco). Cabecera: `id`, `centro_id`, `residente_id`, apartado (`ALERGIAS`, `ENFERMEDADES`, `MEDICACION`), número de versión, estado del apartado, fuente opcional (lista cerrada), motivo del cambio (lista cerrada), cuenta, perfil, fecha y hora. Elementos: tipo, texto corto y los campos propios del apartado, con límites de longitud y `CHECK` de no vacío. **Sin columnas de reserva para «una futura base de datos de fármacos»** (sería construir de más): una fila por elemento ya permite migrarla después.
2. **Sin valor por defecto ni fila automática.** Apartado sin fila = «pendiente».
3. **Solo inserción.** Disparador `INSTEAD OF UPDATE, DELETE` como `TR_id_immutable` (`0016:56-57`); en `0091`, INSERT sí y `DENY` en UPDATE y DELETE, y declaración de la tabla para que `PermisosAplicacionTests` no falle (`0008-multicentro-y-grupos.md:114-115`). Con una rama para el expurgo como las demás tablas del catálogo (`expurgo-y-supresion.md:111`).
4. **Unicidad y concurrencia:** única `(residente_id, apartado, numero_version)`; el guardado inserta la versión *n+1* solo si la vigente es la *n* que vio quien edita (si no, conflicto visible).
5. **Coherencia estado y elementos** (`CHECK` o disparador): «con entradas» exige al menos un elemento; «no refiere», «no consta» y «refiere que no hay» exigen cero. Alergias: las intolerancias pueden convivir con «No refiere alergias» (a confirmar con CJ).
6. **Año** de enfermedades e intervenciones: opcional, con rango razonable (no futuro). «Año desconocido» es una respuesta válida.
7. **Seguridad por filas:** `centro_id`, clave compuesta con `residentes` y política por filas; `CoberturaRlsTests` obliga a cubrirla (`0008:104`).
8. **Anulación por error** (datos de otro residente, versión equivocada): **otra fila**, no un UPDATE (la tabla es inmutable). La versión anulada deja de verse en pantallas ordinarias y no puede salir en un PDF (art. 32 LOPDGDD, apartado 1.5).
9. **Informe de derivación:** el `contenido_json` guarda, por apartado, el número y el `id` de la versión, su fecha y perfil, el estado y los elementos; entra en la huella como el resto (`0016:39-43`). No hace falta columna nueva en `informes_derivacion`.
10. **Permisos de perfil:** uno o dos permisos nuevos en `ProfilePermissions` y en el disparador de catálogo, apagados por defecto; Auxiliar, Administración, Plataforma y Familia **no** pueden tenerlos.
11. **Auditoría:** acciones nuevas en `ClinicalTraceability.ActionCodes` y, si procede, en `CK_idem_action` (`0016:24-29`), con etiqueta en español; solo identificadores.
12. **Catálogo de expurgo** y, cuando exista, **consulta y exportación** de la ficha con sus versiones para el derecho de acceso (art. 15 RGPD; Llei 21/2000 art. 13; `expurgo-y-supresion.md:185`).
13. **Fuera del censo exportable** (ADR 0010: la exportación no lleva nada clínico).

### 5.2 Qué NO se debe guardar

- Antecedentes **familiares** ni datos de terceros; nombres de quien aportó el dato (solo categoría).
- Cumplimiento o incumplimiento terapéutico («no se toma la pastilla»), dicho por un familiar o no: dato de tercero y juicio subjetivo (CTMDC; Llei 21/2000 art. 13.2).
- **Todo lo que sea administración o prescripción:** registro de dosis dadas, horarios con avisos, vía, fecha de inicio o fin, prescriptor, motivo de la prescripción, stock, alertas o cruces (ADR 0011:68; ADR 0015:141-143).
- Documentos adjuntos, fotos de recetas o de informes, número de tarjeta sanitaria u otros identificadores que no hagan falta; informes del médico de cabecera.
- Texto libre largo, observaciones abiertas o narrativa clínica.
- **Nada procedente de un tratamiento con IA** (apartado 6).

### 5.3 Condiciones previas

Clave: **C** = antes de construir; **I** = imprescindible antes de datos reales; **P** = antes de producción; **R** = puede esperar.

| N.º | Condición | Quién | Cuándo |
|---|---|---|---|
| 1 | Orlando acepta el cambio de «alergias primero» a los tres apartados y el orden de activación; CJ responde las aclaraciones (1.1, 1.2) y las preguntas del apartado 8 | Orlando, CJ | C |
| 2 | Corregir alcance, ADR 0011 y ADR 0015 (nota o ADR) y decidir si la medicación va en la base o en el módulo «Medicamentos» | Orlando con CJ | C (antes de activar la medicación) |
| 3 | Base jurídica y condición del art. 9 por escrito; ¿centro sanitario?; ¿DPO designado?; AEPD o APDCAT; norma con rango de ley para la parte social | Abogado y DPO real | I |
| 4 | Información a residentes y representantes (con Urgencias como destinatario), información al personal, registro de actividades, EIPD general con anexo, contrato de encargado con la categoría nueva | Residencia y su DPO; ResidApp aporta el anexo técnico | I |
| 5 | Procedimiento de la residencia: entrega del PDF (canal, aparato, qué hacer con la copia descargada), y **procedimiento de actualización de la ficha** cuando cambia la receta o la hoja de tratamiento | La residencia | I |
| 6 | Decisión sobre auditar las lecturas del personal asistencial | DPO real | I |
| 7 | Decisión sobre bloqueo de versiones anteriores (art. 32 LOPDGDD) y plazo de la ficha | DPO real | I |
| 8 | Datos de prueba, semillas, capturas y ejemplos del Manual **solo inventados** (el repositorio es público) | Orlando y quien construya | I |
| 9 | Prueba que impida que `Infrastructure/IA` referencie los tipos de la ficha; ningún campo admite dictado | Quien construya | C |
| 10 | Tras restaurar una copia de seguridad, avisar al centro: una ficha puede volver a una versión antigua (`entorno-produccion-lectura-dpo.md:98-103`) | Orlando | P |
| 11 | Cualificación regulatoria escrita antes de cualquier función de alerta (apartado 6) | Experto regulatorio | R (hasta que se plantee) |

## 6. Bloqueo futuro de prescripción por alergia

No entra ahora. **Anoto lo que abre** para que no se descubra tarde:

1. **Producto sanitario [Validar].** El Reglamento (UE) 2017/745, anexo VIII, regla 11: «los programas informáticos destinados a proporcionar información que se utiliza para tomar decisiones con fines terapéuticos o de diagnóstico se clasifican en la clase IIa», salvo impactos mayores (`mdr.txt:2560-2562`). La guía MDCG 2019-11 deja fuera el software que no hace nada más que almacenar, archivar, comunicar o buscar de forma simple (`mdcg-2019-11.txt:500-503`), y pone como ejemplo de software que sí es producto sanitario un módulo de una historia clínica que ayuda a ajustar dosis o avisa de interacciones (`mdcg-2019-11.txt:469-474, 989-990`). **[Opinión]** La ficha **tal como se propone ahora** (guardar, mostrar e imprimir) queda en la parte de «almacenar y comunicar»; **cruzar una alergia con una prescripción es hacer algo más** y lo acerca a producto sanitario, con marcado CE y evaluación de un organismo notificado. Hay que calificarlo por escrito antes de plantearlo.
2. **Art. 22 RGPD.** Es un derecho a no ser objeto de decisiones basadas «únicamente en el tratamiento automatizado» que afecten significativamente (`rgpd.txt:268`); el apartado 4 prohíbe esas decisiones con datos de salud salvo consentimiento explícito o interés público esencial (`rgpd.txt:274`). Un **bloqueo duro** (el médico no puede prescribir) se acerca a esa figura; una **alerta que el médico puede saltarse dejando un motivo escrito** es apoyo a la decisión con intervención humana real. **[Opinión]** Diseñarlo como alerta con anulación motivada, no como bloqueo.
3. **«Prescribir» está fuera de alcance** (`alcance.md:39`). El bloqueo exige que ResidApp tenga prescripciones, es decir, reabrir la prescripción y la administración de medicación.
4. **Base de datos de fármacos.** Elegir fuente y licencia; preferible una copia local versionada y no una consulta externa por cada tecla (así no sale nada a terceros; una consulta con solo el nombre del fármaco, sin el residente, no es un dato personal del residente, pero sí lleva metadatos de cuenta e IP). El «grupo farmacológico» exige un criterio clínico de reacciones cruzadas; **un catálogo desactualizado produce falsos negativos**. Quién responde de ese catálogo es una decisión de producto con consecuencias de seguridad del paciente.
5. **IA.** Si el emparejamiento nombre-fármaco o la «recomendación de alternativas» usaran un modelo, aplica el ADR 0011 (nuevo ADR, contrato, EIPD) y el Reglamento de IA: un sistema de IA que sea un producto sanitario sujeto a evaluación de terceros es de alto riesgo (art. 6.1, `reglamento-ia.txt:223-225`). Si son reglas deterministas, el Reglamento de IA no aplica, pero sí el producto sanitario (punto 1).
6. **Espacio Europeo de Datos de Salud.** La ficha completa se parece mucho a una «historia clínica resumida del paciente», que el Reglamento (UE) 2025/327 define con alergias, problemas de salud, procedimientos médicos y medicamentos (anexo I, punto 1, apartados 4, 7, 10 y 12; `ehds.txt:1192-1205`), categoría prioritaria del art. 14.1.a (`ehds.txt:294`) aplicable a los sistemas de historia clínica electrónica **desde el 26 de marzo de 2029** (art. 105, `ehds.txt:1137`). Un «sistema HCE» es el que permite almacenar, editar o visualizar esos datos y «está destinado por el fabricante» a los prestadores de asistencia sanitaria (art. 2.2.k, `ehds.txt:209`). **[A verificar]** Si ResidApp, con la ficha, pasa a ser un sistema HCE sujeto a marcado y requisitos de interoperabilidad, y si la residencia es «prestador de asistencia sanitaria». Es una **pregunta nueva** respecto a mi lectura anterior, que lo dejaba como a verificar sin urgencia: ahora tiene más peso.
7. **Nuevos requisitos de la ficha de alergias si hubiera bloqueo:** codificación (principio activo o grupo), versionado del catálogo, quién puede anular una alerta y con qué motivo, auditoría de cada anulación y de cada alerta saltada, y una EIPD propia.
8. **Dictado.** El ADR 0015 declara el módulo incompatible con el dictado hasta que exista un ADR nuevo (líneas 141-143).

## 7. Qué de mi recomendación anterior queda superado y qué sigue vigente

| Mi recomendación del 2026-11-11 | Estado | Comentario |
|---|---|---|
| «Ficha solo con alergias a medicamentos ahora; las tres cosas más adelante y por fases» | **Superada en el alcance** | CJ ha elegido la opción C (las tres). Mantengo el **orden de activación** (alergias, enfermedades, medicación), no el alcance |
| «No ampliar sin leer» y «enfermedades con tope de elementos» | **Retirada en lo esencial** | Acepto la lista completa de enfermedades e intervenciones (apartado 1.3); sigue vigente que sean elementos cortos, sin antecedentes familiares ni narrativa |
| «Fecha de revisión y aviso de revisión caducada impreso en el PDF» | **Superada** | CJ no quiere revisión ni caducidad. Se sustituye por la fecha y el perfil de la **última modificación** por apartado (1.4) |
| «Revisión como acto de solo añadir (‘revisado sin cambios’)» | **Superada** | Sin revisión periódica, no existe ese acto. Queda como recomendable el recordatorio opcional al cerrar un evento con derivación |
| «Tres estados por apartado, nunca vacío por defecto» | **Vigente y ampliada** | Y se añade «pendiente» (sin fila) como cuarto estado de sistema (apartado 1.4) |
| «`No documentado` nunca se imprime como “ninguna”» | **Vigente** | CJ pensaba que, si no hay alergia documentada, se asume que no la hay; mi criterio y la aclaración 1.1 siguen siendo imprimir distinto |
| «Versionado de solo añadir con autor, perfil, fecha y motivo» | **Vigente** | Es la mitigación central |
| «Procedencia honesta en el PDF; no “dato automático” sin más» | **Vigente, con texto nuevo** | La frase aceptada por CJ debe cambiar (4.3) |
| «Fuente de una lista cerrada» | **Vigente, opcional** | CJ no quiere un método obligatorio; la fuente es un campo opcional por categoría |
| «No bloquear la derivación por una ficha ausente» | **Vigente y confirmada por CJ** | |
| «Sin Auxiliar, sin Administración, sin familia; Dirección con finalidad y auditoría» | **Vigente** | |
| «Permisos de edición apagados por defecto» | **Vigente** | |
| «Nunca a un proveedor de IA; sin dictado de la ficha» | **Vigente** | Y se añade: tampoco IA para extraer datos de informes o fotos de recetas al ingreso (es la petición más probable y la excluye el ADR 0011) |
| «No hay alertas ni cruces» | **Vigente** | El bloqueo futuro es otra decisión con otra EIPD (apartado 6) |
| «EIPD: anexo de la general, no una propia» | **Vigente, con dos riesgos nuevos** | Exactitud y seguridad del paciente; sensibilidad por inferencia (apartado 3) |
| «Medicación: la más arriesgada, germen de “Medicamentos”» | **Vigente y más urgente** | Ahora es una decisión de CJ; ver ADR 0015 y la decisión de «base o módulo» |
| «Servicios de urgencias como destinatarios en la información» | **Vigente y pendiente** | Hoy no figuran en la política (`Privacy.cshtml:93-100`) |
| «Quién es la fuente de verdad lo decide el responsable clínico, no el software» | **Vigente** | CJ lo ha respondido: la receta electrónica, contrastada; sin método rígido |

## 8. A verificar o decidir

### Para CJ (clínicas; no las decide el DPO)

1. **Aclaración 1.1 (contestada por CJ el 2026-11-15):** qué imprime el informe en «No refiere» frente a «No consta». Mi posición: distintos. CJ eligió distintos.
2. **Aclaración 1.2 (contestada por CJ el 2026-11-15):** si Medicina también puede cambiar la ficha. Si sí, con permiso propio y sin estado «validado». CJ dijo que sí, también Medicina.
3. **¿Una alergia y una intolerancia pueden convivir?** («No refiere alergias, pero sí intolerancia a X»). Afecta al modelo de datos.
4. **¿Cómo se anota «ninguna enfermedad conocida» y «no toma medicación habitual»?** (lista, «refiere que no hay», «no consta»).
5. **¿Solo alergias a medicamentos, o también látex, alimentos o contrastes?** Si solo medicamentos, la etiqueta lo dice siempre.
6. **¿Qué es «medicación habitual»?** ¿Entran las pautas «si precisa», los suplementos, los parches, las inhalaciones, las insulinas? ¿Hay que anotar cuando el residente toma menos que lo prescrito?
7. **¿Acepta la nueva frase del informe** (4.3) en lugar de la que aprobó?
8. **¿Quiere el recordatorio opcional** al cerrar un evento con derivación?
9. **¿Lleva reacción breve la alergia**, como la intolerancia?

### Para Orlando

1. **Aceptar el cambio del alcance** (de «alergias primero» a los tres apartados) y **el orden de activación** (medicación la última).
2. **Medicación: ¿en la base o en el módulo «Medicamentos»?** (afecta al precio, ADR 0015).
3. **Corregir** `alcance.md`, ADR 0011:75-77, ADR 0015 y `Privacy.cshtml:48, 54, 93-100` antes de activar la medicación.
4. **Decidir el tratamiento de «obligatorio»**: pendiente y visible, nunca bloqueante (mi propuesta).
5. **Si hay una residencia piloto**: tipo de centro (público, concertado o privado) para fijar AEPD o APDCAT y ENS.

### Para el DPO real o un abogado [Validar]

1. Base y condición del art. 9, incluida la norma con rango de ley para la parte social; ¿centro sanitario?; ¿DPO obligatorio?
2. ¿Es la ficha parte de la historia clínica del centro o un documento auxiliar? Afecta a custodia, acceso del residente y plazo.
3. Plazo de conservación de la ficha y de sus versiones (15 años desde la baja por defecto).
4. Bloqueo de versiones anteriores (art. 32 LOPDGDD) y alcance del bloqueo.
5. Auditar o no las lecturas del personal asistencial.
6. Si hay diagnósticos de especial sensibilidad que exijan un trato distinto en el PDF.
7. Si ResidApp con esta ficha puede ser un «sistema HCE» del Reglamento 2025/327 y si la residencia es «prestador de asistencia sanitaria».
8. Texto de la cláusula de ingreso y de la política.
9. Reparto de responsabilidad sobre la exactitud: lo que firma la residencia en el contrato.

### A verificar (fuente oficial)

1. Texto literal de la Llei 21/2000 en el DOGC (la base local es el consolidado del BOE en castellano).
2. Plazo exacto que corresponde a «hojas de tratamiento» y antecedentes (arts. 12.4, 12.6 y 12.7 de la Llei 21/2000; recomendaciones de la CTMDC).
3. Cómo encaja la lectura de la AEPD de julio de 2026 sobre trazabilidad de accesos (no la he vuelto a leer en esta consulta).
4. Calificación regulatoria de una futura función de alerta (producto sanitario).
5. Si existe un modelo oficial en Cataluña de documento de traslado desde residencias y qué datos pide.

**Resumen para decidir hoy.** Orlando: aceptar los tres apartados, activar la medicación la última, decidir si va en la base o en «Medicamentos» y corregir las promesas públicas antes de activarla. CJ: validar la nueva frase del informe, decidir qué imprime «No refiere» frente a «No consta», aclarar los puntos 3 a 6 y si Medicina edita. DPO o abogado: base del art. 9, centro sanitario, plazo, bloqueo de versiones, auditoría de lecturas y posible clasificación como sistema HCE. Nada de esto se usa con residentes reales hasta cumplir las condiciones del apartado 5.3.

Este documento es un borrador del agente `dpo-residapp`: lo valida una persona antes de usarse con datos reales.
