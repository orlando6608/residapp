# Antecedentes y enfermedades, medicación habitual y alergias a medicamentos en el informe de derivación a Urgencias: lectura de protección de datos

**BORRADOR. Lo valida una persona antes de usarlo con datos reales: el DPO real o un abogado en lo jurídico, y el médico responsable en lo clínico.** Fecha: 2026-11-11. Segunda lectura experta del agente `dpo-residapp`, no un dictamen. Es un análisis previo a una decisión de producto: no se ha construido nada, no modifica código, ADR ni otros documentos, y no contiene datos de personas reales.

Etiquetas: **[Hecho]** verificado en el repositorio (archivo y línea) o en una fuente oficial (URL y fecha); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, el médico responsable, el DPO real o un abogado; **[A verificar]** no confirmado en fuente oficial. Las páginas web las leí con una herramienta que devuelve resúmenes con citas cortas, no el texto literal, y no pudo abrir ningún PDF: lo marco donde importa y lo repito al final.

Palabras que se repiten, definidas una vez:

- **Dato de salud:** dato que revela algo del estado de salud de una persona. El RGPD lo llama «categoría especial» y parte de prohibir su uso salvo excepciones.
- **Responsable:** quien decide para qué y cómo se usan los datos (aquí, la residencia). **Encargado:** quien los trata por cuenta del responsable y solo según sus instrucciones (aquí, ResidApp). **Subencargado:** empresa que trabaja para ResidApp, que trabaja para la residencia.
- **DPO:** delegado de protección de datos, la persona experta e independiente que asesora al centro. **EIPD:** evaluación de impacto, un análisis por escrito, hecho antes de empezar, de los riesgos para las personas y de cómo se reducen. **Registro de actividades:** la lista que una organización debe llevar de para qué usa datos personales, de quién, quién los ve y cuánto los guarda.
- **Minimización:** guardar solo lo necesario para el fin. **Exactitud:** que los datos sean ciertos y estén al día. **Fuente de verdad:** el documento o sistema que manda cuando dos listas no coinciden. **Versionado:** cada cambio crea una versión nueva y la anterior se conserva.
- **Ámbito:** las unidades y residentes a los que da acceso el perfil de una persona. **Seguridad por filas:** regla de la base de datos que impide que una consulta devuelva datos de otro centro. **Auditoría:** registro, que nadie puede borrar ni editar, de quién hizo qué y cuándo. **Disparador:** regla automática de la base de datos que impide modificar o borrar ciertas filas. **Huella:** resumen matemático de un documento; si alguien lo cambia, deja de coincidir.

## 1. Resumen y recomendación

**Conclusión: admisible con condiciones.** No veo una prohibición legal: son datos de salud que la residencia ya trata para atender a la persona, y entregarlos al servicio que la atiende en una urgencia es parte de esa atención. El riesgo más importante no es la privacidad sino la **exactitud**: una alergia o una medicación desactualizada, impresa en un informe urgente firmado y que ya no se puede corregir, es a la vez un riesgo clínico y un incumplimiento del principio de exactitud (art. 5.1.d RGPD).

**Recomendación [Opinión]: C ahora, con A mejorado para el resto; B por fases y solo si se cumplen las condiciones del apartado 9.**

| | **A.** Texto libre por derivación (como hoy) | **B.** Ficha con enfermedades, medicación y alergias | **C.** Ficha solo de alergias a medicamentos |
|---|---|---|---|
| Dato nuevo guardado | Ninguno: el texto ya existe (hasta 4.000 caracteres) dentro del informe firmado | Tres apartados vivos por residente, versionados | Un apartado vivo por residente, versionado |
| Minimización | La mejor: no hay copia viva fuera del informe. Riesgo: un campo sin ayuda invita a pegar de más | La peor de las tres: lista permanente de diagnósticos y fármacos (los fármacos delatan diagnósticos) | Buena: poco volumen y el menos sensible de los tres |
| Exactitud | El profesional mira la fuente cada vez, pero no hay fecha de revisión ni versión, y un informe sin alergias no distingue «no tiene» de «no se anotó» | La más expuesta: lo más volátil (la medicación) acabaría impreso como «dato automático» | Buena: cambia poco; con tres estados y fecha de revisión se controla |
| Encaje con el proyecto | Sin cambios | Choca hoy con el alcance, el ADR 0011, la política de privacidad y el bloqueo de «Medicamentos» | Encaja: no es medicación ni administración |
| Veredicto | Admisible; mejorarlo con una ayuda en el campo | Admisible con condiciones; por fases y no el primero | **Recomendada como primer paso** |

**A mejorado:** el mismo campo de texto libre, con una ayuda que pida medicación, enfermedades y alergias, que diga «escribe solo lo necesario para la atención urgente, sin nombres de familiares» y que pida escribir «no consta» cuando no se sepa. No crea ningún dato nuevo. Si el PO necesita estructura para medicación y enfermedades sin ficha viva, hay una variante intermedia: tres campos separados dentro de la propia derivación (los mismos datos que hoy, sin copia fuera del informe).

**Por qué C primero y no B de entrada:**

1. Las alergias a medicamentos son el dato más crítico en una urgencia, cambian poco y son pocas líneas: la ficha más barata de mantener al día y la menos arriesgada.
2. La **medicación habitual** es lo más volátil (cambia con cada revisión, alta o ajuste), lo que más daño hace si está desactualizado y el germen del módulo «Medicamentos», que el proyecto tiene bloqueado hasta que el PO reabra el alcance (`docs/producto/alcance.md:45`; ADR 0011:75-77; ADR 0015:141-143; `anexo-modulos-contrato-borrador.md:238-249`).
3. Una lista de **enfermedades** tiende a crecer hasta reproducir la historia clínica y a revelar más de lo que una urgencia necesita; ResidApp no es la historia clínica oficial (`alcance.md:38`; `docs/producto/vision-general.md:7`).
4. Con B, ResidApp guardaría una **segunda copia** de lo que el centro ya tiene en su hoja de tratamiento, su receta electrónica o sus informes. Dos listas que pueden discrepar son un riesgo de seguridad del paciente, no solo de privacidad.
5. No hay datos reales ni producción: nada urge. B construido sin reglas clínicas escritas queda mal por defecto y luego cuesta arreglarlo.

**Lo que se pierde con C y A mejorado:** el profesional sigue escribiendo a mano la medicación y las enfermedades en cada derivación, bajo la presión de una urgencia. Es el precio de no tener una segunda lista que mantener.

**Tres cosas que pesan más que la letra elegida:**

1. **La exactitud** (apartado 4): sea cual sea la opción, hay que decidir quién es la fuente de verdad y quién responde de que esté al día. Eso no lo resuelve el software.
2. **La promesa pública:** la política de privacidad dice que ResidApp «no guarda la medicación de las personas residentes ni su administración» (`src/ResidApp.Web/Views/Home/Privacy.cshtml:54`). Con B esa frase deja de ser cierta; con C sigue siéndolo.
3. **Lo que falta con A, B o C:** la política no cita a los servicios de urgencias como destinatarios de los datos (`Privacy.cshtml:93-100`) aunque el informe ya sale hoy del centro; y aún no existen la EIPD, el registro de actividades completo ni el contrato de encargado (apartado 8).

### Lo que hay hoy [Hecho]

- **No existe ningún dato de medicación habitual, enfermedades ni alergias.** Búsqueda en `database/scripts/` y en `src/`: la única aparición es el motivo de cita «Medicación o tratamiento», que elige la familia al pedir una cita (`src/ResidApp.Domain/Appointments/AppointmentRequests.cs:21`; `database/scripts/0057_citas_solicitud_previa.sql:46`). Matiz: el basal ya recoge «demencia documentada», su etiología y el estadio GDS (`src/ResidApp.Domain/Baseline/Catalogs/CognitionCodes.cs:7-38`) y el informe ya los copia (`src/ResidApp.Web/Models/ReferralModels.cs:165-190`). Una enfermedad concreta ya viaja en el PDF: una lista de enfermedades no debe duplicarla ni contradecirla.
- **El informe reúne** identificación, basal, observación, valoraciones, constantes, actuaciones y evolución (`ReferralModels.cs:141-160`); el profesional escribe el motivo, la información adicional (hasta 4.000 caracteres) y las comunicaciones (`src/ResidApp.Domain/Enfermeria/EmergencyReferral.cs:14-16`). El campo «Información adicional para Urgencias» no llevaba ninguna ayuda (`src/ResidApp.Web/Views/Shared/_Derivacion.cshtml:37-41`); desde el 2026-11-13 la lleva, con las tres indicaciones del «A mejorado» (apartado de opciones).
- **El informe firmado no se puede corregir.** Un informe por evento, sin corrección ni nueva versión (`database/scripts/0016_derivacion_urgencias.sql:9, 53, 56-57`). El texto de «información adicional» queda copiado en tres sitios de la misma fila: columna, JSON y PDF (`0016:39-42`; `src/ResidApp.Infrastructure/Persistence/ReferralWriter.cs:60-74`).
- **Quién descarga el PDF:** Enfermería y Medicina (esta, solo de eventos escalados), con cada descarga auditada (`src/ResidApp.Application/Ports/IReferralReportRepository.cs:26-29`; `SqlReferralReportRepository.cs:36`); Dirección Clínica, con declaración de finalidad (`IReferralReportRepository.cs:35-39`). La aplicación no envía el PDF a nadie y no hay integraciones con sistemas externos (`alcance.md:49`).
- **No existen** una EIPD (figura como pendiente antes de datos reales: `docs/producto/roadmap.md:49`; ADR 0007:110) ni un registro de actividades completo: solo entradas parciales por función, por ejemplo `historial-tras-traslado-01-registro-actividades.md`.

## 2. Qué datos son, base jurídica, responsable y encargado

### 2.1 Categoría de datos

- **[Hecho]** Son «datos relativos a la salud»: datos de la salud física o mental de una persona, incluida la prestación de servicios sanitarios, que revelan información sobre su estado de salud (art. 4.15 RGPD; BOE, leído el 2026-11-11 por resumen). Son categoría especial del art. 9.1: el RGPD parte de que no se pueden tratar y solo lo permite si se da una de las excepciones del art. 9.2 y con garantías.
- **[Opinión]** Los tres no pesan igual. Las **alergias** son lo menos sensible. Las **enfermedades** pueden incluir salud mental, deterioro cognitivo o infecciones. La **medicación** revela diagnósticos por inferencia (el fármaco delata la enfermedad): una lista de fármacos tiene la sensibilidad de una lista de diagnósticos (`modulos-opcionales-lectura-dpo.md:163`).
- **Personas afectadas.** Residentes (mayores, a menudo con capacidad disminuida: colectivo vulnerable a efectos de las listas de EIPD); profesionales (cada cambio lleva su autoría); y **terceros incidentales** si alguien escribe «antecedentes familiares» o el nombre de un familiar. **[Opinión]** No recoger antecedentes familiares: son datos de otras personas y no hacen falta para una urgencia.

### 2.2 Base jurídica

La fija la residencia, que es la responsable. Lo que propongo para que lo contraste su DPO o su abogado:

- **Art. 6 RGPD (por qué es lícito tratarlos).** **[Suposición]** 6.1.b si el contrato de servicio con el residente incluye la atención; 6.1.c si hay obligación legal de documentación clínica (Ley 41/2002 o Llei 21/2000, si alcanzan a la residencia); 6.1.e si el centro es público. La letra d (intereses vitales) solo como apoyo en una urgencia concreta, no como base de una ficha permanente.
- **Art. 9.2 RGPD (la excepción que levanta la prohibición).** **[Suposición]** La 9.2.h: tratamiento necesario para el diagnóstico, la prestación de asistencia o tratamiento sanitario o social, o la gestión de los sistemas y servicios de asistencia sanitaria y social, sobre la base del Derecho de la Unión o de un Estado miembro; con el art. 9.3, que exige que lo traten profesionales sujetos a secreto profesional o personas bajo su responsabilidad (BOE, leído el 2026-11-11 por resumen). La 9.2.c (intereses vitales cuando la persona no puede consentir) refuerza el caso de la urgencia vital. **No se pide consentimiento**: la asistencia no se apoya en él, y pedirlo crearía la expectativa de poder retirarlo. La AEPD recuerda que el consentimiento explícito es una excepción entre otras (su página «Tus derechos en relación con tus datos de salud», modificada el 2026-11-06).
- **Norma con rango de ley. [Hecho]** La LOPDGDD (art. 9.2) exige que los tratamientos del 9.2.h fundados en el Derecho español estén amparados en una norma con rango de ley. Su disposición adicional 17.ª, apartado 1, enumera las leyes que amparan los tratamientos de salud (Ley 14/1986, Ley 31/1995, Ley 41/2002, Ley 16/2003, Ley 44/2003, Ley 14/2007, Ley 33/2011, Ley 20/2015, el texto refundido de la Ley de garantías y uso racional de los medicamentos y el de la Ley general de derechos de las personas con discapacidad). Texto consolidado del BOE, actualizado el 27/12/2025, leído hoy por resumen.
- **Cataluña. [Hecho, resumen del BOE actualizado el 28/11/2025]** La Llei 21/2000 dice, en su art. 10, que los datos clinicoasistenciales de la historia clínica incluyen los antecedentes, la descripción del problema de salud, los procedimientos y los informes de alta; en el art. 11, que los profesionales del centro implicados en el diagnóstico o el tratamiento deben tener acceso a ella, que el personal de administración solo accede a lo que su función requiere y que todo el que accede queda obligado al secreto; y en el art. 9, que los cambios y los profesionales que los hacen deben quedar registrados. La Ley 41/2002 (arts. 14 a 17; resumen del BOE actualizado el 01/03/2023) va en el mismo sentido. Los antecedentes son, por tanto, contenido propio de la historia clínica.
- **[A verificar, importante]** La lista de la disposición adicional 17.ª **no incluye** leyes de servicios sociales (Llei 12/2007, Ley 39/2006). Si la residencia es un servicio social y no un centro sanitario, para la parte de «asistencia social» del 9.2.h un abogado debe señalar la norma con rango de ley que lo ampara. Es la misma pregunta que ya está abierta sobre si una residencia cuenta como centro sanitario (`antes-de-datos-reales.md:131-132`).
- **Fines.** Asistencia y continuidad asistencial: tener a mano los datos de referencia para derivar. **No** valen otros: estadística propia de ResidApp, mejora del producto, entrenamiento de modelos, indicadores agregados sin base propia.

### 2.3 Quién es responsable y quién encargado

- **[Hecho]** La residencia es la responsable y ResidApp la encargada (ADR 0007:3-5). Azure (SQL y Web App) es subencargado. **Mistral no interviene y no debe intervenir** (apartado 7).
- Los servicios de urgencias y emergencias (hospital, SEM) que reciben el PDF pasan a ser **responsables independientes** de la copia que reciben: no son subencargados de nadie.
- Añadir una categoría de datos no cambia los papeles, pero hay que escribirla: el contrato de encargado recoge las instrucciones documentadas del responsable y el tipo de datos (art. 28.3; **[A verificar]** el texto literal). Si ResidApp usara estos datos para un fin propio (estadística, producto), pasaría a ser responsable (art. 28.10, leído en el BOE el 2026-11-09: `modulos-opcionales-lectura-dpo.md:15, 44`): el contrato debe prohibirlo.
- **ResidApp no valida el contenido clínico** (no hay catálogo de fármacos ni de diagnósticos): la responsabilidad del dato es del profesional que lo registra y de la residencia.
- **[Hecho]** Plataforma (el perfil del personal de operación de ResidApp) no ve contenido clínico (ADR 0006; `0008-multicentro-y-grupos.md:22-23`).

## 3. Minimización y proporcionalidad

### 3.1 ¿Es necesario para el fin?

El fin es la atención sanitaria urgente fuera del centro. **[Opinión]** Que una persona mayor llegue a Urgencias con una lista de lo que toma, lo que padece y si es alérgica es lo que suele esperarse de un documento de traslado: la necesidad de los tres datos es plausible. **[Decidir]** La confirma el médico responsable; no me corresponde valorar la necesidad clínica. La proporcionalidad se juega en el detalle, y se decide al **definir la ficha**, no al imprimirla: el PDF solo puede ser tan proporcionado como la ficha de la que sale.

### 3.2 Qué sí y qué no

| Apartado | Necesario para una urgencia (a confirmar por el médico) | No es necesario |
|---|---|---|
| Alergias a medicamentos | Estado (hay, no hay, no documentado), sustancia (principio activo o grupo) y, si el médico lo pide, la reacción en una línea | Alergias no medicamentosas (látex, alimentos, contrastes) salvo que el médico las pida; narrativa clínica |
| Enfermedades | Problemas de salud activos y antecedentes relevantes para atender una urgencia, con tope de elementos | Antecedentes familiares; diagnósticos antiguos sin relevancia actual; valoraciones subjetivas; lo que el basal ya recoge (demencia y su etiología) |
| Medicación habitual | Lo que toma de forma continuada: principio activo y, si el médico lo estima imprescindible, dosis y pauta | Historial de medicación pasada; motivos de prescripción en narrativa; horarios, registro de administración y alertas: eso ya es el módulo «Medicamentos» |
| Todos | Solo la **versión vigente** y la fecha de su última revisión en el PDF | El historial de versiones en el PDF; adjuntos; fotos de recetas; nombres de terceros |

**Cómo estructurarlo [Opinión].** Elementos de texto corto, con tope de longitud y de número, no un texto libre largo. **Sin autocompletado contra un catálogo externo** de fármacos o enfermedades: cada consulta enviaría lo tecleado a un tercero, que sería un destinatario o subencargado nuevo y exigiría su propio ADR (como los proveedores de IA: ADR 0011:58-62; `modulos-opcionales-lectura-dpo.md:168`). Una lista fija dentro de la propia aplicación sí es admisible.

**No ampliar sin leer.** Si el responsable clínico quiere añadir otras cosas habituales en un traslado (por ejemplo, persona de contacto o código de actuación), es otra decisión con su propia lectura; no deben colarse en esta ficha.

## 4. Exactitud

### 4.1 Por qué importa más aquí que en cualquier otro dato

El art. 5.1.d RGPD pide datos «exactos y, si fuera necesario, actualizados» y «todas las medidas razonables» para rectificar sin dilación los inexactos (BOE, leído el 2026-11-11). Tres hechos agravan el riesgo en este caso:

- **[Hecho]** El informe firmado no se corrige (`0016:9, 56-57`) y el PDF se entrega fuera del centro. Si lleva un dato viejo, el error ya ha salido. **La vista previa obligatoria es el último control** (`docs/flujos-clinicos/derivacion-urgencias.md:18`; `_Derivacion.cshtml:67-68`).
- **[Hecho]** Hoy el PDF marca las secciones como «dato automático» o «escrito por el profesional» (`src/ResidApp.Infrastructure/Pdf/ReferralReportPdfRenderer.cs:68`; `_Derivacion.cshtml:74`). Una lista persistente impresa como «dato automático» **transmite una fiabilidad que puede no tener**.
- **[Opinión]** La ficha sería una copia de otra fuente (la hoja de tratamiento, la receta electrónica, los informes). Si la copia se descuida, es peor que no tenerla.

Riesgo por apartado, de menos a más:

| Apartado | Cuánto cambia | Daño si está desactualizado | Observación |
|---|---|---|---|
| Alergias | Poco | Alto, pero por omisión | Se controla con estados y fecha |
| Enfermedades | Medio-bajo | Medio | Riesgo de lista larga y obsoleta |
| Medicación | Alto | Muy alto (anticoagulantes, insulina, psicofármacos) | Es la que exige más reglas y la que más se acerca a «Medicamentos» |

### 4.2 Mecanismos mínimos [Opinión]

1. **Tres estados por apartado, nunca vacío por defecto:** «consta que no hay», «hay: (lista)» y «no documentado». El PDF imprime el estado tal cual; **«no documentado» nunca se imprime como «ninguna»**. Es el criterio que ya sigue el basal: `NO_DOCUMENTADO` nunca se interpreta como normalidad (`docs/flujos-clinicos/gestion-basal-barthel.md:26, 54`).
2. **Fecha y autor de la última revisión**, separados de la fecha del último cambio. «Revisado sin cambios» es un acto que se registra (cuenta, perfil, fecha) sin crear contenido nuevo; así una ficha estable no parece abandonada. Conviene que sea un evento de solo añadir, para no conceder permiso de modificación sobre la tabla (ADR 0008:108-117).
3. **Versionado de solo añadir.** Cada cambio crea una versión con autor, perfil, fecha y motivo de una lista cerrada (por ejemplo, cambio clínico, error de transcripción, revisión tras un ingreso); nada se sobrescribe. Es el patrón del basal (`gestion-basal-barthel.md:70, 73`) y lo que pide la Llei 21/2000 (art. 9: quedan registrados los cambios y quién los hace; resumen del BOE).
4. **Fuente** de una lista cerrada (hoja de tratamiento oficial, informe médico, informe de alta hospitalaria, el propio residente, su familiar o representante, otra). Sirve para saber a quién preguntar y para tratar con cuidado lo aportado por terceros. **[A verificar]** La Comissió Tècnica en Matèria de Documentació Clínica tiene recomendaciones sobre cómo registrar en la historia clínica lo aportado por terceros y sobre la rectificación; no pude abrirlas (PDF).
5. **Aviso de revisión caducada**, visible en la ficha y en la vista previa e **impreso en el PDF**, junto con «Revisada el (fecha)». El plazo lo fija el médico, no el software.
6. **Procedencia honesta en el PDF.** No «dato automático» sin más, sino «Registrado en la ficha del residente; revisado el (fecha) por (perfil)», más una frase de cautela que redacta el médico (por ejemplo, contrastar con la hoja de tratamiento oficial). El perfil, no el nombre de quien revisa: menos datos del personal en un documento que sale del centro.
7. **No bloquear la derivación** por una ficha ausente o caducada. La regla del proyecto es que documentar no retrasa la atención (`0016:18-19`). Se avisa y se imprime el estado.
8. **Recordatorios, no reglas.** Al cerrar un evento con derivación o al volver de un ingreso, la aplicación puede preguntar «¿ha cambiado algo?». Qué dispara la revisión lo define el responsable clínico.
9. **Tras restaurar una copia de seguridad**, una ficha puede volver a una versión antigua (`entorno-produccion-lectura-dpo.md:98-103`): el procedimiento de restauración debe avisar al centro y pedir la revisión de las fichas tocadas en el tramo perdido.

### 4.3 Lo que debe decidir el responsable clínico, no el software

1. Qué es «habitual» (¿pautas «si precisa»? ¿suplementos?) y qué es alergia frente a intolerancia o efecto adverso; si entran las alergias no medicamentosas.
2. Qué enfermedades entran, con cuántas y con qué detalle, y cómo se distinguen de lo que ya recoge el basal.
3. La **fuente de verdad** y cómo se contrasta con ella.
4. Quién edita, quién revisa, cada cuánto y qué hechos obligan a revisar (reingreso, cambio de pauta, alta hospitalaria).
5. A partir de cuándo una revisión se considera caducada.
6. Qué se imprime cuando no hay dato o la revisión es antigua, y la frase de cautela.
7. Si hace falta alguna comprobación adicional al firmar (mi recomendación es no bloquear: punto 7 de 4.2).
8. Qué se hace si se descubre un error con el informe ya entregado. **[Suposición]** Llamar al servicio receptor por el canal urgente y dejar constancia en el protocolo del evento, que sigue abierto hasta el cierre (`0016:6-9`); lo confirma quien construya. El PDF firmado no cambia.

### 4.4 Lo que el software no debe hacer

- **Alertas ni cruces** (por ejemplo, avisar si una indicación nombra un fármaco al que hay alergia, o interacciones entre fármacos). Sería apoyo a la decisión clínica: lo descartó el ADR 0011:68 y podría ser producto sanitario (software con finalidad médica sujeto a regulación propia: ADR 0011:73-74; `modulos-opcionales-lectura-dpo.md:161`).
- **Deducir «sin alergias» de la ausencia de datos**, o rellenar un estado por defecto.
- **Reutilizar la ficha** en indicadores o cuadros de mando de Dirección (por ejemplo, «porcentaje de residentes polimedicados»): sería otra finalidad y entra en lo que el ADR 0011 descarta como detección de riesgos.

## 5. Acceso y trazabilidad

### 5.1 Quién ve y quién edita (propuesta [Opinión], a decidir)

La ficha es del residente, no del episodio ni de la unidad (como el basal): al trasladarse de unidad la ve el equipo nuevo según las reglas de su ámbito.

| Perfil | Leer la ficha | Editarla | PDF con la ficha | Motivo |
|---|---|---|---|---|
| Auxiliar | No, por defecto | No | No descarga (`src/ResidApp.Application/UseCases/EmergencyReferral.cs:45`) | Minimización. Hoy lee el resumen del basal (`src/ResidApp.Application/Authorization/ResidentBaselinePolicy.cs:140-148`); la ficha no debe heredar esa regla sin decidirlo |
| Enfermería | Sí, en su ámbito | Solo con permiso propio y si el médico lo decide (por ejemplo, transcribir la medicación desde la hoja oficial) | Sí | Mismo patrón que los permisos del basal (`src/ResidApp.Domain/Accounts/ProfilePermissions.cs:28-29`) |
| Medicina | Sí, en su ámbito | Sí, con permiso | Sí, solo eventos escalados | Edita quien tiene la responsabilidad clínica |
| Dirección Clínica | Solo con permiso de lectura clínica, finalidad declarada y auditoría previa | No | Sí, con declaración de finalidad | Como el basal y la derivación (`ResidentBaselinePolicy.cs:171-189`) |
| Administración | No | No | No | No accede por defecto al historial clínico (`alcance.md:28`) |
| Plataforma | No | No | No | No ve contenido clínico (ADR 0006) |
| Familia en el portal | No | No | No | El portal no muestra medicación, basal ni derivaciones (`docs/producto/guia-por-perfil-recorrido.md:68`; `alcance.md:27`; `Privacy.cshtml:65`) |
| Mistral o cualquier IA | No | No | No | Apartado 7 |

**Privacidad por defecto (art. 25.2 RGPD: por defecto solo se tratan los datos necesarios y se limita su accesibilidad; BOE, resumen).** Los permisos de edición nacen apagados y los concede la Administración del centro por perfil, como los del basal. Activar la función no concede acceso a ningún perfil.

### 5.2 Seguridad por filas, permisos y pruebas

- **[Hecho]** La seguridad por filas aísla cada centro en la base de datos (ADR 0008:42-76) y la aplicación acota por unidad. Una tabla nueva debe llevar `centro_id`, clave compuesta con `residentes` y política por filas; `CoberturaRlsTests` obliga a cubrirla (ADR 0015:27-28).
- **[Hecho]** El usuario de la aplicación solo recibe los permisos declarados en el script `0091`, y `PermisosAplicacionTests` obliga a declarar cada tabla nueva (ADR 0008:108-117). **[Opinión]** Las versiones, de solo añadir (INSERT sí; UPDATE y DELETE no), con el disparador `INSTEAD OF` que ya usan las demás tablas inmutables (por ejemplo `informes_derivacion`: `0016:56-57`).
- **[Hecho]** No existe aún la prueba del catálogo de expurgo (la lista de todas las tablas que habría que vaciar para borrar a un residente al vencer su plazo; no hay ninguna en `tests/`). La ficha y sus versiones deben entrar en él cuando se construya (`expurgo-y-supresion.md:95, 440`).

### 5.3 Auditoría

- **Cambios y revisiones:** cada uno deja fila en la auditoría clínica, con **solo identificadores y sin el texto de la ficha** (el texto ya está en la versión). Una acción nueva exige alta en la lista cerrada y su etiqueta en español (`src/ResidApp.Application/Ports/ClinicalTraceability.cs:20-21, 24-37`).
- **Descarga del PDF:** ya se audita (`SqlReferralReportRepository.cs:36`).
- **Lecturas por el personal asistencial:** hoy la lectura del basal por Auxiliar, Enfermería y Medicina no deja registro (`src/ResidApp.Application/Authorization/RequestAuthorizationContext.cs:256-259`); solo se auditan las de Dirección Clínica y, en otra función, las del historial tras un traslado. **[A verificar, importante]** La AEPD resolvió en julio de 2026 (PD-00068-2026, criterio publicado el 7 de julio) que, como regla general, el derecho de acceso a la historia clínica incluye la identidad de quienes accedieron, con limitaciones excepcionales y motivadas; el Tribunal Supremo (sentencia 789/2026, de 24 de junio, según el resumen de la AEPD) matiza que no hay un derecho incondicionado a conocer a cada empleado si los protocolos internos acreditan que las consultas siguieron las instrucciones del responsable. **[Decidir]** DPO real: si se registrarán las lecturas de la ficha (cuenta, perfil, residente, hora, sin contenido). Es una decisión de todo el producto, no solo de la ficha. Contrapeso: más datos del personal; la finalidad debe quedar escrita (seguridad y acceso indebido, no evaluar al personal), como ya se hizo en `antes-de-datos-reales.md:58`.
- **Registros técnicos y telemetría** (los registros de funcionamiento de la aplicación): el texto de la ficha no debe aparecer en ellos ni en mensajes de excepción (`entorno-produccion-lectura-dpo.md:44-56`).

### 5.4 Qué no debe ver la familia

- La ficha, en ninguna pantalla del portal.
- Los **comunicados**: la derivación genera una actualización relevante para la familia (`derivacion-urgencias.md:32`), pero la escribe una persona y no debe rellenarse con datos de la ficha. Tampoco los avisos por correo, que no llevan nada de salud (`Privacy.cshtml:104`).
- La **familia como fuente:** puede aportar un dato (por ejemplo, una alergia); eso se registra en la ficha con su fuente y no se le devuelve por el portal.
- El **derecho de acceso** del residente o de su representante legal se atiende por el procedimiento del centro (art. 15 RGPD; Ley 41/2002; Llei 21/2000, art. 13), no por una pantalla del portal. Para eso la ficha y sus versiones tienen que poder consultarse y copiarse: la consulta y exportación del historial sigue pendiente (`expurgo-y-supresion.md:442`).
- **Exportación del censo:** hoy no lleva nada clínico (ADR 0010). La ficha no debe entrar en ella sin una decisión propia.

### 5.5 Copias del dato

La ficha viva, sus versiones, la copia dentro de cada informe firmado (columna, JSON y PDF) y las copias de seguridad. El plazo por defecto del proyecto es de 15 años desde la baja (`src/ResidApp.Domain/Residents/ResidentDischarge.cs:25`; `expurgo-y-supresion.md:20-47`), pendiente de que lo confirme una persona. Ni el borrado selectivo ni la corrección del informe firmado son posibles sin romper su huella; se tratan como el resto de la historia.

## 6. Comunicación a Urgencias

**Flujo [Hecho]:** el profesional firma el informe, lo descarga y lo entrega por sus medios; ResidApp no lo transmite (`alcance.md:49`; `src/ResidApp.Web/Controllers/EnfermeriaController.cs:831`; `MedicinaController.cs:674`).

**Base [Suposición].** Es parte de la asistencia (continuidad asistencial): art. 9.2.h con las normas sanitarias generales, y art. 9.2.c si la persona no puede consentir y está en juego su vida o su integridad. Sin consentimiento. **[A verificar]** No encontré en lo leído un precepto que regule expresamente la entrega de un informe a *otro* centro: el art. 16 de la Ley 41/2002 y el art. 11 de la Llei 21/2000 hablan del acceso de los profesionales del propio centro. Un resumen de búsqueda atribuye a la APDCAT (dictamen CNS 51/2018) la conclusión de que las comunicaciones con finalidad asistencial no requieren consentimiento; no pude abrir el original.

**Límites que propongo:**

1. **Solo a quien atiende a la persona** (servicio de urgencias, SEM, hospital). No a la familia, ni a aseguradoras, ni a terceros.
2. **Solo lo necesario:** la versión vigente de la ficha y el informe; no el historial.
3. **Sin envío automático desde la aplicación** (no lo hay hoy). Si algún día se integra con otros sistemas, hace falta ADR, EIPD y contrato propios.
4. **Canal y soporte, regla de la residencia:** en mano o en papel, o por el canal seguro del servicio receptor; no por mensajería ni correo personales. El PDF descargado queda en la carpeta de descargas del aparato, y el proyecto admite aparatos personales de los trabajadores (ADR 0016:29; ADR 0009:56-58), donde esa copia queda fuera del control del centro. **[Decidir]** procedimiento: aparato del centro, borrar tras entregar, no reenviar.
5. **Secreto profesional:** el receptor también está obligado (art. 9.3 RGPD; Ley 41/2002, art. 16.6, resumen). Aun así, el PDF debería llevar al pie «Contiene datos de salud; uso exclusivo para la atención de esta persona» **[Opinión]**.
6. **Información a la persona o a su representante, si tiene la capacidad disminuida.** Los servicios de urgencias deben figurar como destinatarios en la información que da la residencia (art. 13.1.e RGPD). Hoy no figuran en la política de privacidad (`Privacy.cshtml:93-100`; `politica-privacidad-portal.md:111-117`).
7. **Datos que vienen de terceros** (familia, informes previos): el art. 14 RGPD pide decir las categorías de datos y la fuente (14.1.d y 14.2.f; BOE, leído hoy). **[Opinión]** Basta una frase general en la cláusula del centro.
8. **Después de entregar,** el receptor es responsable de su copia; la residencia solo responde de lo que entregó y de cómo.

## 7. Inteligencia artificial

**Confirmado: estos datos no deben enviarse nunca a un proveedor de IA.** Cuatro razones, todas del propio repositorio:

1. **El ADR 0011 lo excluye.** El único caso de uso aprobado es el dictado de «Registrar cambio» (ADR 0011:38; ADR 0009). Quedan descartados el resumen clínico, el asistente sobre el historial y la detección de riesgos (polimedicación, interacciones), y rige el criterio de que la IA no lee datos de residentes ni resume, prioriza o hace triaje (clasificar a los pacientes por gravedad) (ADR 0011:66-71). También el *tool calling* (dejar que el modelo pida datos a la aplicación) sobre la medicación (ADR 0011:75-77).
2. **No hay contrato que cubra salud.** El anexo del acuerdo de tratamiento de Mistral dice «categorías especiales: ninguna», y la retención «durante el contrato» no es retención cero (`docs/seguridad/dictado-por-voz-dossier-dpo.md:63-65, 70-72`).
3. **Minimización de la IA:** al proveedor no va nada que identifique al residente (ADR 0011:37; ADR 0009:50-51).
4. **El dictado no debe tocar la ficha:** ni el prompt de «Registrar cambio» (`src/ResidApp.Infrastructure/IA/Prompts/registrar-cambio.md`) ni su esquema deben incluir alergias, medicación o enfermedades.

**Guardas técnicas [Opinión]:** una prueba que falle si el código de `Infrastructure/IA` referencia los tipos de la ficha; ningún campo de la ficha admite dictado (una dosis mal entendida es un riesgo directo: `modulos-opcionales-lectura-dpo.md:165`); y **ningún dato real de la ficha, ni capturas, en herramientas de IA usadas para desarrollar o dar soporte**. Hoy el entorno solo tiene datos inventados; la regla debe regir en cuanto exista un dato real.

**Si algún día se quisieran resumir informes de derivación:**

- Exige lo que ya fija el ADR 0011:93-94: un caso de uso que las reglas no resuelvan, el visto bueno del PO, contrato, EIPD y un ADR nuevo. Un modelo local, sin tercero, sería el candidato natural (ADR 0011:85-86); el proveedor actual, no.
- **Regla de seguridad del paciente [Opinión]:** alergias y medicación se copian tal cual desde la ficha, nunca resumidas ni reescritas; un resumen que omita una alergia es un incidente. La IA, si acaso, tocaría solo la narrativa.
- **Marco regulatorio (texto literal comprobado el 2026-11-12 en la base normativa local; consolidados, no versión oficial):** si prioriza o clasifica pacientes, el anexo III, punto 5, letra d) del Reglamento de IA incluye «sistemas de triaje de pacientes en el contexto de la asistencia sanitaria de urgencia» entre los de alto riesgo, aplicable desde el 2 de diciembre de 2027 según el Reglamento (UE) 2026/1744 (que modifica el art. 113, párrafo tercero, letra c, del Reglamento de IA); si una clasificación de pacientes en residencia cuenta como «urgencia» es interpretación, y sin IA no aplica. Si orienta decisiones terapéuticas, podría ser producto sanitario: Reglamento (UE) 2017/745, anexo VIII, regla 11 («los programas informáticos destinados a proporcionar información que se utiliza para tomar decisiones con fines terapéuticos o de diagnóstico se clasifican en la clase IIa», con excepciones al alza); la guía MDCG 2019-11 rev.1 deja fuera el software que no realiza una acción sobre los datos más allá de almacenarlos, archivarlos, comunicarlos o buscarlos de forma simple. Cómo encaja cada función es interpretación. Además, el art. 22 RGPD protege frente a decisiones basadas solo en tratamiento automatizado.
- Revisión humana de cada salida y sin guardar las salidas.

## 8. Documentos que habría que actualizar y si hace falta una EIPD nueva

| Documento | Qué cambia | Con C | Con B |
|---|---|---|---|
| `docs/producto/alcance.md` (líneas 10, 14, 38-39, 45) | Incluir la ficha en Residentes y Derivación. Aclarar que registra lo que documenta el profesional y no genera diagnósticos, alertas ni cruces | Sí | Sí, y aclarar que la lista informativa de medicación habitual no es «administración de medicación» (la línea 45 sigue) |
| `docs/flujos-clinicos/derivacion-urgencias.md:25` y un flujo de la ficha | Sección nueva del informe; quién edita, revisa y cuándo | Sí | Sí |
| ADR 0011:75-77 | «ResidApp no guarda medicación» | Sin cambio | Deja de ser cierto: nota o ADR que lo corrija. La reapertura de lo descartado la reserva el ADR 0011:66 al PO |
| ADR 0015:34, 141-143; `anexo-modulos-contrato-borrador.md:238-249`; `modulos-opcionales-lectura-dpo.md:156-170` | Frontera de «Medicamentos» | Sin cambio | Definir si la lista informativa va en la base y «Medicamentos» queda como pautas, administración y apoyo a la decisión; o si B es el primer escalón de ese módulo y hereda su bloqueo |
| `Privacy.cshtml:41-42, 48, 93-100` y `politica-privacidad-portal.md:59, 111-117` | Añadir «alergias a medicamentos» a los datos de salud y los servicios de urgencias como destinatarios (hoy faltan) | Sí | Sí |
| `Privacy.cshtml:54`; `politica-privacidad-portal.md:67, 462` | La frase «no guarda la medicación» | Sigue siendo cierta | Hay que cambiarla |
| `docs/privacidad/antes-de-datos-reales.md` | Entrada nueva con las condiciones (la aplica Orlando o la sesión principal) | Sí | Sí |
| Registro de actividades | No hay documento único: ampliar la entrada «historia asistencial» o crear una (modelo: `historial-tras-traslado-01`) con categorías, destinatarios (urgencias), plazo y medidas | Sí | Sí |
| EIPD general | Incluir el tratamiento | Sí | Sí, con un anexo de seguridad del paciente |
| Contrato de encargado y su anexo | Nueva categoría de datos; prohibición de uso propio | Sí | Sí |
| Información a residentes, representantes y personal | Cláusula y comunicación interna (quién edita, que queda registrado, que no sirve para evaluar) | Sí | Sí |
| `expurgo-y-supresion.md` (filas 16 y 18 de su tabla) | Tablas nuevas en el catálogo de expurgo y en la consulta y exportación | Sí | Sí |
| Manual, Guía por perfil, pantalla de derivación | Consultar al agente `doc-usuario-residapp`: `Manual.cshtml:1537`, `Guia/enfermeria.json:409`, `Guia/medicina.json:264`, `_Derivacion.cshtml:13-18` | Sí | Sí |

**Texto propuesto para la política, solo si se aprueba [Opinión], en lenguaje llano.**

- Con C: «Entre los datos de salud, el centro anota si la persona tiene alergias a medicamentos y a cuáles. Cuando el centro deriva a la persona a un servicio de urgencias, el informe de derivación, con estos datos, se entrega al servicio sanitario que la atiende.»
- Con B (sustituye `Privacy.cshtml:54`): «El centro anota la lista de enfermedades y de medicación habitual de la persona, y sus alergias a medicamentos, para entregarlas a Urgencias si hace falta. ResidApp no guarda la administración de la medicación (qué se da y cuándo).»

**¿EIPD nueva o basta una actualización?** Dos hechos: (1) **no hay ninguna EIPD que actualizar todavía** (`roadmap.md:49`), y la general es obligatoria de todos modos (datos de salud de personas vulnerables, a gran escala; la lista de la AEPD del art. 35.4, según un resumen de búsqueda, recoge las categorías especiales, la gran escala y los colectivos vulnerables en sus puntos 4, 7 y 9; **[A verificar]** en el PDF); (2) lo que añade este tratamiento cabe como **anexo de la EIPD general**, no como una propia. El responsable es la residencia; ResidApp aporta el anexo técnico.

- **A mejorado y C:** basta incluirlo en la EIPD general (un anexo corto).
- **B:** no una EIPD aparte, pero sí un anexo con análisis real de dos riesgos: **exactitud y seguridad del paciente** (apartado 4) y **datos sensibles por inferencia** (apartado 2). Misma lógica que la nota de `historial-tras-traslado-02-nota-eipd.md:11-17`.
- **EIPD propia, o rehacer el anexo,** si cambia algo de esto: dosis, horarios o administración; alertas o cruces; IA; acceso de Auxiliar o de la familia; envío automático o integración con sistemas externos; catálogos externos de fármacos; uso agregado de la ficha. Casi todos nos llevan al módulo «Medicamentos» (ADR 0015).
- **[Suposición]** La consulta previa a la autoridad (art. 36) no parece necesaria si se aplican las medidas; lo decide el DPO.

## 9. Decisiones que debe tomar una persona

Clave de «cuándo»: **C** = antes de construir; **I** = imprescindible antes de datos reales; **P** = antes de producción; **R** = puede esperar.

| N.º | Decisión | Quién | Cuándo |
|---|---|---|---|
| 1 | **Elegir la opción y fijar el alcance por escrito:** C ahora con A mejorado y B por fases, o B de entrada; si la ficha va en la base o en el futuro módulo «Medicamentos» (afecta al precio); que la lista informativa no es administración de medicación y que no hay alertas ni cruces. Con B, corregir la frase «no guarda medicación» (ADR 0011:75-77; `Privacy.cshtml:54`) | Orlando y el PO (el ADR 0011:66 reserva al PO reabrir el alcance) | C |
| 2 | **Reglas clínicas y de acceso:** las del apartado 4.3 (qué entra, fuente de verdad, quién edita y revisa, cada cuánto, qué dice el PDF si falta o caduca) y las del 5.1 (sin Auxiliar, sin Administración, sin familia; permisos de edición apagados por defecto) | Médico responsable (el PO y el responsable clínico de la residencia piloto), con Orlando | C |
| 3 | **Base y condición del art. 9 por escrito,** con la norma con rango de ley para la parte de servicios sociales; si la residencia es «centro sanitario» a efectos de la Ley 41/2002 y la Llei 21/2000; si hay que designar DPO (LOPDGDD art. 34.1.l: centros sanitarios obligados a mantener historias clínicas); AEPD o APDCAT. Y si se registran las lecturas del personal asistencial (criterio de la AEPD de julio de 2026) | Abogado y DPO real, con la asesoría laboral para las lecturas | I |
| 4 | **Documentos de la residencia:** información a residentes, representantes y personal (con los servicios de urgencias como destinatarios), registro de actividades, EIPD general y categoría en el contrato de encargado | La residencia y su DPO; Orlando para `Privacy.cshtml`; ResidApp aporta el anexo técnico | I |
| 5 | **Procedimiento del centro para entregar el PDF:** canal, quién, aparato, qué hacer con la copia descargada y qué hacer si se descubre un error | La residencia | I |
| 6 | **Al construir:** consulta y copia de la ficha y sus versiones (derecho de acceso y fin de contrato), entrada en el catálogo de expurgo y datos de prueba solo inventados en semillas, pruebas y capturas (el repositorio es público) | Orlando y quien construya | I |
| 7 | **Plazo de conservación** de la ficha y de sus versiones (15 años desde la baja por defecto) | DPO real | P |
| 8 | **Puede esperar:** IA y resúmenes (exigen un ADR nuevo); integración con sistemas oficiales; catálogos externos; el módulo «Medicamentos»; el análisis del Reglamento de IA, del producto sanitario y del Espacio Europeo de Datos de Salud, salvo que cambie el alcance; una lista de fichas sin revisar para Dirección | Orlando, PO y DPO, cuando toque | R |

## A verificar (fuente oficial)

1. Texto literal de la Llei 21/2000 (arts. 9, 10, 11, 12 y 13) en el DOGC o el Portal Jurídic; hoy solo un resumen del BOE (actualizado el 28/11/2025). Y de la Ley 41/2002, arts. 14 a 17 (resumen del BOE actualizado el 01/03/2023).
2. Si una residencia es «centro sanitario» a efectos de esas leyes, y qué norma con rango de ley ampara el art. 9.2.h en la vertiente social (Llei 12/2007, Ley 39/2006). Si hay que designar DPO (LOPDGDD art. 34.1.l). Autoridad: AEPD o APDCAT según el tipo de centro (`politica-privacidad-portal.md:397`).
3. Dictamen CNS 51/2018 de la APDCAT (comunicaciones con finalidad asistencial sin consentimiento): solo vi un resumen de búsqueda; la web de la APDCAT ha cambiado de dominio.
4. Recomendaciones de la Comissió Tècnica en Matèria de Documentació Clínica sobre información aportada por terceros, rectificación y diseño de la historia clínica: no pude abrir los PDF.
5. Criterio de la AEPD de 2026-07-07 (PD-00068-2026) y sentencia del Tribunal Supremo 789/2026: contrastar con el texto y ver si aplican a una residencia.
6. Art. 28.3 y art. 25.2 del RGPD, literales. Lista de la AEPD del art. 35.4 (PDF).
7. Reglamento de IA: anexo III, punto 5.d, texto literal y efecto de la versión consolidada del 27/07/2026. Reglamento (UE) 2017/745, regla 11, y MDCG 2019-11: contrastar con el texto oficial.
8. Reglamento (UE) 2025/327 (Espacio Europeo de Datos de Salud, que fija reglas para los sistemas de historia clínica electrónica y el intercambio de datos de salud): categorías prioritarias y calendario, y si una ficha con alergias, medicación y problemas de salud haría de ResidApp un «sistema de historia clínica electrónica». No pude confirmarlo.
9. Si existe un modelo oficial en Cataluña de documento de derivación o traslado desde residencias y qué datos pide.
10. Los documentos del Ministerio de Sanidad sobre conciliación de la medicación (2019 y 2022), como referencia clínica y no jurídica.

## Fuentes consultadas el 2026-11-11

Repositorio: archivos y líneas citados. Externas, leídas con herramienta de resúmenes (sin texto literal completo), salvo que se indique:

- [BOE, Ley 41/2002](https://www.boe.es/buscar/act.php?id=BOE-A-2002-22188): arts. 14 a 17, página actualizada el 01/03/2023.
- [BOE, LOPDGDD](https://www.boe.es/buscar/act.php?id=BOE-A-2018-16673): arts. 9 y 34.1, disposición adicional 17.ª, actualizada el 27/12/2025.
- [BOE (DOUE), Reglamento (UE) 2016/679](https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807): arts. 4.15, 5.1, 6.1, 9, 14, 16, 17.3, 22, 25.2 y 32.1.
- [BOE, Llei 21/2000](https://www.boe.es/buscar/act.php?id=BOE-A-2001-2353): arts. 9 a 13, actualizada el 28/11/2025.
- [AEPD, Tus derechos en relación con tus datos de salud](https://www.aepd.es/areas-de-actuacion/salud/tus-derechos-en-relacion-con-tus-datos-de-salud) (modificada el 06/11/2026) y [criterio sobre trazabilidad de accesos](https://www.aepd.es/informes-y-resoluciones/criterios-juridicos-aepd/acceso-informacion-de-trazabilidad-datos-de-salud-en-relacion-reglamento-ehds) (07/07/2026); [comentario de Cuatrecasas](https://www.cuatrecasas.com/es/global/farmaceutico-sanitario/art/trazabilidad-accesos-datos-salud) (13/07/2026).
- [AEPD, lista de tratamientos que requieren EIPD](https://www.aepd.es/documento/listas-dpia-es-35-4.pdf): solo resumen de búsqueda.
- [BOE, Reglamento de IA (DOUE-L-2024-81079)](https://www.boe.es/buscar/doc.php?id=DOUE-L-2024-81079) y [EUR-Lex](https://eur-lex.europa.eu/legal-content/ES/TXT/HTML/?uri=CELEX%3A02024R1689-20240712): resumen de búsqueda del anexo III.
- [MDCG 2019-11](https://health.ec.europa.eu/document/download/b865d8e9-081a-4601-a91a-f120321c0491_en) (no se pudo abrir el PDF; resumen de búsqueda), [Reglamento (UE) 2025/327](https://boe.es/doue/2025/327/L00001-00096.pdf) (resumen de búsqueda), [Comissió Tècnica, información aportada por terceros](https://canalsalut.gencat.cat/web/.content/_Professionals/Consells_comissions/comissio_tecnica_en_materia_de_documentacio_clinica/criteris_protocols/recomanacions-registre-hc-informacio-aportada-per-tercers.pdf) y [Ministerio de Sanidad, conciliación de la medicación](https://seguridaddelpaciente.sanidad.gob.es/practicasSeguras/usoSeguroMedicamentos/docs/Documento_final-_Octubre_2022._ACCESIBLE.pdf): solo referencias.

Este documento es un borrador del agente `dpo-residapp`: lo valida una persona antes de usarse con datos reales.
