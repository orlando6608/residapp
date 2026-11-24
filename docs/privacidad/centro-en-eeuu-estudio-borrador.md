# Centro en Miami (Florida, EE. UU.): estudio de cumplimiento de protección de datos

**BORRADOR. Lo valida una persona experta (abogado o abogada de EE. UU. con práctica en sanidad y datos, más un DPO real para la parte europea) antes de usarlo.** Fecha: 2026-11-24. Segunda lectura del agente `dpo-residapp`, no un dictamen. Origen: encargo de Orlando para poder activar la zona `America/New_York` («Miami (Florida, EE. UU.)», inactiva desde el script `0112`; `docs/producto/zonas-horarias-por-centro-estudio.md`, apartado 3.9, línea 89). Este estudio no activa nada.

**Cómo se marca lo que sé y lo que no.**
- **[Verificado en texto literal]**: leído en `normativa-local/` (RGPD, comprobación del 2026-11-20 según `normativa-local/INDICE.md`; es texto consolidado, no la versión oficial) o en una página oficial consultada hoy.
- **[Resumen web]**: leído en un resumen de una página, no en el texto literal. Sirve para orientar, no para decidir.
- **A CONFIRMAR POR PERSONA EXPERTA**: lo sé de oídas o de memoria, o no he podido comprobarlo. Esto incluye casi todo el derecho de EE. UU.: **la base normativa local no trae ninguna norma estadounidense**, y mi conocimiento de ella es de segunda mano.

## Conclusión

**No, tal y como lo plantea Orlando, no.** Poner la zona horaria de Miami en un centro **no cambia qué ley se le aplica**: la zona solo decide cómo se muestran las horas. Y la ley **no se elige por pacto**. Con un centro en Miami y residentes que viven allí:

1. **Aplicará derecho de EE. UU. y de Florida** a los datos de salud del centro (casi seguro HIPAA, y leyes de Florida sobre brechas, historia clínica y licencia del centro). Un contrato «bajo derecho español» no lo quita.
2. **El RGPD no se aplica al centro por el mero hecho de usar ResidApp.** Se aplica a **ResidApp** como encargada si ResidApp está establecida en la UE (art. 3.1), y a un centro estadounidense solo en los casos del art. 3.2 (ofrecer servicios a personas que están en la UE, o vigilar su comportamiento allí) o si el centro tuviera titular o establecimiento en la UE. Lo normal será que el RGPD cubra a ResidApp pero no al centro.
3. **Lo que sí puede hacerse** es que ResidApp aplique, por su cuenta y por contrato, un nivel de protección europeo alto, y que además cumpla lo estadounidense. Eso es **doble cumplimiento**, no «elegir España».
4. **Hay un obstáculo probable y concreto a la arquitectura actual:** una norma de Florida exige que ciertos proveedores sanitarios guarden la información del paciente «en Estados Unidos continental o sus territorios o Canadá» (§ 408.051(3), apartado 3.4 de este estudio). Hoy los datos viven en Azure en la UE (ADR 0014:19, región Spain Central; política de «solo UE/EEE», línea 64). Si esa norma alcanzara al centro, **no se podría activar el centro con los datos en la UE**. Es la primera pregunta para la persona experta.

**Riesgo más importante:** activar un centro de salud en EE. UU. sin saber si es entidad cubierta por HIPAA, de qué tipo de licencia es (residencia con enfermería o vivienda asistida) y sin acuerdo de asociado comercial, con un encargado de brechas y conservación escritos para España. **Condición:** no se activa la zona hasta cerrar el apartado 7.

La lectura previa de Orlando es **correcta en lo esencial** y la afino en dos puntos: (a) el RGPD sí puede alcanzar a ResidApp en sí misma, aunque el centro esté fuera; (b) la ley local no se «elimina» con un pacto, pero un pacto sí puede **añadir** obligaciones.

---

## 1. Qué ley aplica según quién sea quién

**Términos.** *Responsable*: quien decide para qué y cómo se usan los datos (el centro). *Encargado*: quien los trata por cuenta del responsable (ResidApp, ADR 0007). *Entidad cubierta* (HIPAA): el tipo de organización a la que la ley sanitaria federal de EE. UU. obliga directamente (proveedores sanitarios que facturan electrónicamente, aseguradoras, etc.). *Asociado comercial*: un proveedor que maneja datos de salud por cuenta de una entidad cubierta.

La ley que se aplica depende de **tres cosas**: dónde está **establecido** quien trata los datos, **dónde están las personas**, y **qué actividad** es (aquí, asistencia sanitaria y social). No depende del idioma, de la zona horaria ni de lo que diga el contrato.

### Escenario A: centro estadounidense, sin establecimiento ni titular en la UE; ResidApp establecida en España

- **El centro (responsable).** El RGPD «se aplica al tratamiento de datos personales en el contexto de las actividades de un establecimiento del responsable o del encargado en la Unión, independientemente de que el tratamiento tenga lugar en la Unión o no» (art. 3.1, `rgpd.txt:34`) y, para quien no está establecido en la UE, solo si trata datos de personas «que se encuentren en la Unión» y ofrece servicios o controla su comportamiento (art. 3.2, `rgpd.txt:36-39`). Residentes que viven en Miami **no están en la Unión**. **Conclusión probable: el RGPD no obliga al centro.** [Verificado en texto literal; la aplicación al caso: A CONFIRMAR POR PERSONA EXPERTA]
- **Excepción a vigilar: familias en la UE.** Si familiares que viven en España o en otro país de la UE usan el Portal Familiar, son personas «en la Unión». Pero el art. 3.2.a exige que el centro les **ofrezca bienes o servicios** a ellas (no basta con que usen un portal), y la ofrece a los residentes. No parece que alcance, pero **A CONFIRMAR POR PERSONA EXPERTA**; en la práctica: preguntar al centro dónde viven sus familias y no ofrecer el portal a personas en la UE sin decidirlo.
- **ResidApp (encargada).** Si ResidApp tiene establecimiento en la UE, el art. 3.1 la alcanza **por su propio tratamiento**, aunque los datos sean de personas de EE. UU. Esto significa que ResidApp sigue obligada, como encargada, por el art. 28 (contrato), el art. 32 (seguridad), el art. 33.2 (avisar al responsable de una brecha), el registro de actividades del art. 30.2 y el cap. V (transferencias). La interpretación de cuánto del RGPD se aplica a un encargado de la UE cuyo responsable está fuera es de las directrices del CEPD (3/2018 sobre ámbito territorial); **no las he leído en esta sesión: A CONFIRMAR POR PERSONA EXPERTA.**
- **Quién fija esto: la titularidad de ResidApp.** La sociedad titular aún no existe (memoria del proyecto; ADR 0014, aplazado). Hay que saber dónde estará **establecida** ResidApp: si estuviera fuera de la UE, el art. 3.1 no la alcanzaría y el art. 27 (representante en la UE) solo entraría por el art. 3.2.
- **Autoridad.** Para ResidApp, la AEPD (o la APDCAT solo si encajara en el art. 3 de la Llei 32/2010, que se refiere a entidades de Cataluña: un proveedor privado con sede en Cataluña que presta a un centro de EE. UU. no parece encajar; A CONFIRMAR). Para el centro: autoridades de EE. UU. y de Florida (apartado 3).
- **Llei 21/2000, Ley 41/2002, Ley 8/2021 y Ley 39/2006.** Son normas españolas y catalanas sobre asistencia en España. **No parece que apliquen a un centro en Miami**, porque regulan la documentación clínica y el apoyo a personas de los centros sujetos al ordenamiento español. [Opinión; A CONFIRMAR POR PERSONA EXPERTA]

### Escenario B: centro con titular español (o con establecimiento en la UE) que opera en Miami

- El responsable estaría **establecido en la UE** y el art. 3.1 hace que el RGPD se aplique a su tratamiento **también en Miami y también a datos de residentes que viven allí**, «independientemente de que el tratamiento tenga lugar en la Unión o no» (`rgpd.txt:34`). Se aplicaría **a la vez** que el derecho de EE. UU. y de Florida: el centro físico en Florida debe tener licencia, notificar brechas allí, etc.
- Si la actividad se hace «en el contexto de las actividades del establecimiento» es una cuestión de hecho (¿decide y administra el titular español? ¿o es una filial independiente en EE. UU.?): **A CONFIRMAR POR PERSONA EXPERTA**. Una filial estadounidense independiente de un grupo español sería el escenario A.
- Si fuera así, la autoridad competente sería la del Estado miembro donde esté el titular (AEPD, o APDCAT si encaja en su art. 3). Y la parte de historia clínica seguiría fuera de la Llei 21/2000 (el centro no está en Cataluña).

### Escenario C: ResidApp como encargada desde España/UE, con el centro como entidad cubierta de HIPAA

Es el escenario A visto desde el lado estadounidense: el centro, si es **entidad cubierta**, está obligado por HIPAA a firmar con ResidApp un **acuerdo de asociado comercial** (BAA) y ResidApp, al manejar datos de salud por su cuenta, pasa a ser **asociada comercial**. Que una asociada establecida fuera de EE. UU. quede directamente obligada, y en qué medida la haría efectiva la autoridad estadounidense, es **A CONFIRMAR POR PERSONA EXPERTA**. [Resumen web: la regla de HIPAA define como asociado comercial a quien «crea, recibe, mantiene o transmite información de salud protegida» por cuenta de la entidad cubierta (45 CFR 160.103, consultado en eCFR)]

### Cuadro

| Escenario | ¿Aplica RGPD al centro? | ¿Aplica RGPD a ResidApp? | ¿Aplica derecho de EE. UU./Florida? |
|---|---|---|---|
| A. Centro estadounidense, sin vínculo UE | Probablemente no (art. 3.2 salvo familias en la UE) | Sí, si ResidApp está establecida en la UE (art. 3.1) | Sí, al centro; a ResidApp como asociada comercial si el centro es entidad cubierta |
| B. Titular español en Miami | Sí (art. 3.1), a la vez que lo local | Sí | Sí |
| C. Lo mismo que A, vista desde ResidApp | — | Sí | BAA y obligaciones de asociado comercial |

## 2. Transferencias internacionales (cap. V del RGPD)

**En palabras sencillas.** El RGPD prohíbe sacar datos personales de la UE hacia un país sin esa protección, salvo que haya una decisión de la Comisión sobre ese país o garantías adecuadas. Se llama **transferencia** a ese paso.

- **Regla general:** «Solo se realizarán transferencias… si… el responsable y el encargado del tratamiento cumplen las condiciones establecidas en el presente capítulo» (art. 44, `rgpd.txt:538`). Vías: decisión de adecuación (art. 45), garantías adecuadas como cláusulas tipo o normas corporativas vinculantes (art. 46, `rgpd.txt:553-561`) y excepciones puntuales (art. 49, `rgpd.txt:590`). [Verificado en texto literal]
- **Hechos del repo.** Datos en Azure en la UE (ADR 0014:19, 64). El dictado usa Mistral (Francia; el anexo de módulos, línea 136, ya avisa de que algunas funciones podrían mover datos fuera de la UE y que está **sin verificar**). Microsoft tiene matriz en EE. UU. (`dictado-proveedor-mistral-vs-azure-lectura-dpo.md:98`).
- **¿Hay transferencia cuando el personal del centro en Miami entra desde EE. UU. a datos alojados en la UE?** Es lo más probable que sí: cuando una encargada de la UE pone los datos a disposición del responsable ubicado en un país tercero, las directrices del CEPD 05/2021 lo tratan como transferencia. **No las he leído en esta sesión: A CONFIRMAR POR PERSONA EXPERTA.** Si lo es, y si ResidApp está sometida al RGPD (escenario A), ResidApp necesitaría una vía del cap. V para ese flujo. Las candidatas serían las cláusulas contractuales tipo de la Comisión (su módulo de encargado a responsable es el pertinente; **A CONFIRMAR** que existe y su texto) o, si el centro estuviera adherido al Marco de Privacidad de Datos UE-EE. UU., esa decisión de adecuación (art. 45). Un hospital o residencia pequeña probablemente no está adherida: **A CONFIRMAR**.
- **Estado del Marco UE-EE. UU.** [Resumen web, consultado hoy] El Tribunal General desestimó en septiembre de 2025 el recurso de Latombe y este ha apelado ante el Tribunal de Justicia (C-703/25 P, presentado el 31-10-2025); no he encontrado sentencia del Tribunal de Justicia, así que **sigue pendiente: A CONFIRMAR su estado actual**. Conviene no apoyar un centro de salud en una vía que puede caer.
- **El art. 49 (consentimiento, ejecución de un contrato…)** es para casos puntuales, no para un flujo diario y estructural: no es la vía para el acceso del personal.
- **Qué haría falta, en lista:** (1) decidir quién es exportador e importador; (2) la vía del cap. V elegida; (3) evaluación del riesgo del acceso de las autoridades de EE. UU. al dato (medidas complementarias: cifrado, claves, minimización); (4) registrarlo en el registro de actividades de ResidApp (art. 30) y en la EIPD; (5) informar al centro. Todo con la persona experta.
- **Lo contrario: ¿los datos pueden estar en EE. UU.?** Si el centro exigiera alojar en EE. UU. (apartado 3.4), ResidApp tendría que usar una región de EE. UU. de Azure y ya sería **una transferencia de la UE a EE. UU.** de forma estructural, con todas las consecuencias. Hoy la política de Azure de ResidApp prevé solo UE/EEE (ADR 0014:64): habría que decidir si se monta **un despliegue separado** para EE. UU. (apartado 7).

## 3. Lo que probablemente aplica en EE. UU. y Florida

**Aviso importante.** Esta sección es una lista de **dónde mirar**, no un análisis. **Todo lo que no lleve la marca «[Verificado]» o «[Resumen web]» es A CONFIRMAR POR PERSONA EXPERTA.**

### 3.1 HIPAA (ley federal)

- **Qué es.** Ley federal de 1996 con reglas de **privacidad** (qué se puede hacer con la información de salud y qué derechos tienen las personas), **seguridad** (protección de la información electrónica) y **notificación de brechas**. Las reglas están en el título 45 del Código de Regulaciones Federales (CFR), partes 160 y 164.
- **Quién es «entidad cubierta».** Incluye a «un proveedor de atención sanitaria que transmite cualquier información de salud en forma electrónica en relación con una transacción» y define como proveedor a «cualquier otra persona u organización que presta, factura o cobra por atención sanitaria en el curso normal de su actividad» [Resumen web, 45 CFR 160.103 en eCFR]. Una residencia que **factura electrónicamente** (por ejemplo a Medicare o Medicaid) normalmente lo es. Una residencia **solo de pago privado, que no factura electrónicamente**, podría no serlo. **A CONFIRMAR POR PERSONA EXPERTA**, es lo primero que hay que saber.
- **Acuerdo de asociado comercial.** Si el centro es entidad cubierta, ResidApp necesita un BAA con él, y el centro con ella. Lo exige HIPAA; **no existe en el repo** (la plantilla de contrato del art. 28 ni siquiera existe: `anexo-modulos-contrato-borrador.md:34`). Los subencargados que toquen datos (Microsoft, Mistral) necesitarían su propio BAA con ResidApp. **A CONFIRMAR** si Mistral ofrece uno; hoy probablemente no, lo que podría excluir el dictado para ese centro.
- **Brechas.** Los asociados comerciales deben notificar a la entidad cubierta «sin demora injustificada y, en todo caso, a más tardar 60 días naturales después del descubrimiento» (45 CFR 164.410(b)) [Resumen web]. Para la entidad cubierta con respecto a las personas, creo que el límite es también de 60 días y que hay aviso al Departamento de Salud y Servicios Humanos (HHS) y, en ciertos tamaños, a los medios: **A CONFIRMAR (no verificado)**.
- **Derechos de las personas.** HIPAA da derecho de **acceso** a su historia, de **rectificación**, de **recibir un listado de comunicaciones** a terceros y de recibir un **aviso de prácticas de privacidad**. **No hay en HIPAA un derecho de supresión como el del art. 17 RGPD** (lo que tiene que borrarse lo fija la ley de conservación). **A CONFIRMAR.**
- **Documentación.** HIPAA obliga a conservar seis años la documentación de cumplimiento (políticas, formación, registros de brechas), **no la historia clínica** [Resumen web: 45 CFR 164.316 y 164.530(j)]. La conservación de la historia la fija la ley estatal.
- **Prevalencia sobre la ley estatal.** HIPAA se aplica como mínimo y, en general, no desplaza una ley estatal más protectora. **A CONFIRMAR**.
- **Seguridad.** Exige análisis de riesgos, controles de acceso, auditoría y cifrado «direccionable». ResidApp ya hace parte (registro de accesos, sesión, TOTP) pero **no se ha medido contra la regla de seguridad**. **A CONFIRMAR** con una evaluación formal.

### 3.2 Leyes de Florida sobre datos y brechas

- **Ley de Protección de Información de Florida (§ 501.171 del Estatuto de Florida).** [Verificado en la página oficial `flsenate.gov`, edición 2025, hoy]
  - Cuenta como «información personal» «cualquier información sobre los antecedentes médicos, la condición mental o física de una persona» unida a su nombre. Es decir: datos de salud de un residente entran.
  - Las entidades deben tomar «medidas razonables para proteger y asegurar los datos en forma electrónica» (apartado 2) y eliminar con seguridad los registros que ya no conserven (apartado 8).
  - **Plazos:** aviso a las personas afectadas «a más tardar 30 días después de la determinación de la brecha o de motivos para creer que ocurrió» (4.a); aviso al Departamento de Asuntos Legales si afecta a **500 o más personas en Florida** (3.a); y el **agente de un tercero** (aquí, ResidApp) debe avisar a la entidad en **10 días** (6.a). Con prórroga posible solo con causa.
  - Un aviso dado conforme a las reglas del regulador federal de la entidad se considera cumplimiento del aviso a las personas (4.g), sin nombrar HIPAA. Cómo se combinan ambas: **A CONFIRMAR**.
  - [Cuidado: lo anterior viene de un extracto leído a través de una herramienta de lectura de páginas; la persona experta debe leerlo en el texto vigente.]
- **Almacenamiento en EE. UU. (§ 408.051(3)).** [Verificado en la página oficial, edición 2025, a través de la misma herramienta] Dice que, además de las reglas federales de HIPAA, **un proveedor sanitario que utilice tecnología de historia clínica electrónica certificada** debe asegurar que **toda la información del paciente almacenada fuera de sus instalaciones, incluso en la nube o por un tercero subcontratado, «se mantiene físicamente en Estados Unidos continental o sus territorios o Canadá»**. **Preguntas clave:** ¿el centro es «proveedor sanitario» según ese artículo? ¿ResidApp es «tecnología de historia clínica electrónica certificada»? (no está certificada que yo sepa: **A CONFIRMAR POR PERSONA EXPERTA**). Si se aplicara, **la base de datos de ResidApp en la UE no sería admisible para ese centro**.
- **Licencias y conservación en el centro.** [Resumen web]
  - **Residencias con enfermería** («nursing homes») se rigen en Florida por el capítulo 400, parte II del Estatuto, y su reglamento. El Código Administrativo de Florida (regla 59A-4.118) dice que «los registros médicos deben conservarse durante un período de cinco años desde la fecha del alta» (tres años tras la mayoría de edad en menores). **Un plazo muy inferior a los 15 años de ResidApp.** El tipo de residencia, la **vivienda asistida** (capítulo 429) y otros tipos tienen **reglas distintas que no he podido leer: A CONFIRMAR POR PERSONA EXPERTA**.
  - Los derechos de los residentes, la confidencialidad de sus registros y los protocolos de la Agencia para la Administración de Atención Sanitaria (AHCA, el regulador de licencias) son también del derecho de Florida y del federal de residencias (Medicare/Medicaid). **No verificado.**
- **Otras a mirar (todas A CONFIRMAR):** ley de privacidad de consumidor de Florida (su alcance según el tamaño y su relación con HIPAA); normas de Florida sobre la confidencialidad de la historia por profesionales (ley 456); consentimiento y representantes de salud en Florida (tutela y sustitutos sanitarios), que sustituyen en la práctica a la Ley 8/2021; reglas de tele-salud y voz o biometría si se usara; leyes estatales o federales sobre IA en sanidad, si hubiera; normas sobre registros de abuso o maltrato de ancianos y obligaciones de comunicación.

### 3.3 Qué no aplica pero puede inducir a error

- ENS (RD 311/2022): es para el sector público español; no aplica a un centro de Florida.
- Llei 32/2010 y APDCAT: solo para entidades de Cataluña; no alcanzan a un centro de Miami (art. 3, `llei-32-2010.txt:25`; A CONFIRMAR).

## 4. Qué asume hoy ResidApp de derecho español y cómo encaja en Miami

Resumen: casi todos los textos jurídicos de `docs/privacidad/` están escritos para un centro de **Cataluña** con RGPD, LOPDGDD y Llei 21/2000. Para Miami **hay que reescribirlos**; no basta con traducirlos.

| Pieza | Qué asume hoy | Con un centro en Miami | Acción |
|---|---|---|---|
| **Conservación de 15 años** (`antes-de-datos-reales.md:119-121`, apartado 8; `expurgo-y-supresion.md`) | 15 años desde la baja, por el art. 12 de la Llei 21/2000 y la CTMDC (cuadro de 2017). Se discute aún si una residencia es «centro sanitario» a esos efectos | La Llei 21/2000 **no** aplica. Florida fija otro plazo (5 años desde el alta para residencias con enfermería, regla 59A-4.118 [Resumen web]; otros tipos: **A CONFIRMAR**). Lo fija la persona experta, y puede ser **más largo** por reclamaciones (prescripción de demandas en Florida) | Plazo por centro, no un valor único. Hoy el plazo es el mismo para todos. Que el expurgo tenga el plazo como parámetro del centro: cambio de ingeniería **antes** de activar, si se quisiera acortar |
| **Textos informativos** (`politica-privacidad-portal.md`, líneas 396 y 422; cláusulas por capas) | Responsable = residencia; art. 6 y 9 RGPD; DA 17.ª LOPDGDD; Llei 21/2000; AEPD/APDCAT; «solo en castellano» | No sirven. HIPAA exige a la entidad cubierta un **aviso de prácticas de privacidad** propio; Florida puede exigir otros avisos. Hay que escribirlos con un abogado de EE. UU. Si el centro se rige también por el RGPD (escenario B), deben cumplirse **los dos** | Documento nuevo por escenario; no reutilizar el actual |
| **Derechos de supresión** (`procedimiento-derechos-borrador.md`, apartado 2; `expurgo-y-supresion.md`) | Acceso, rectificación, supresión, oposición, limitación y portabilidad del RGPD, con plazos del art. 12; límite de la historia clínica | En HIPAA los derechos son otros (acceso, rectificación, listado de comunicaciones; no supresión) y los plazos, otros. El procedimiento de ResidApp (reenviar al centro en el caso A) **sirve como esqueleto** pero con otro cuadro de derechos y plazos | Una versión del procedimiento para EE. UU. con la persona experta |
| **Contrato de encargado** (art. 28 RGPD; plantilla inexistente: `anexo-modulos-contrato-borrador.md:34`) | Derecho español, encargado de art. 28 | Hace falta, **además**, un **BAA** de HIPAA si el centro es entidad cubierta, con cláusulas propias (uso permitido, salvaguardas, notificación de brechas, devolución o destrucción, subcontratistas). Se pueden **fusionar** en un solo contrato; la ley del contrato y el foro hay que decidirlos con la persona experta | Un solo contrato con anexo EE. UU. |
| **Brechas** (`protocolo-brechas-borrador.md:9, 61, 100-105`) | ResidApp avisa al centro en **24 h seguidas**; el centro notifica a la AEPD/APDCAT en 72 h | **El aviso de 24 h es más estricto que los límites de EE. UU.** (Florida: 10 días para el agente; HIPAA: 60 días como máximo para el asociado [Resumen web]) y, por tanto, **compatible**. En cambio, quién notifica y a quién cambia: el centro avisaría a las personas y al Departamento de Asuntos Legales de Florida (30 días) y, si HIPAA, al HHS (**A CONFIRMAR**); no a la AEPD | Revisar plantillas y contactos; mantener las 24 h; la cobertura nocturna con otra zona horaria: ResidApp opera de noche en España cuando hay día en Miami, y viceversa |
| **Idioma** | Castellano (ADR 0013: catalán no construido) | Probablemente **castellano e inglés**. El personal y las familias de Miami pueden requerir **inglés** en los avisos legales | Decisión de producto: textos legales en inglés **antes** de activar |
| **Quién decide por el residente** | Ley 8/2021 y Ley 39/2006 (apoyos, representante) | En Florida: tutela, apoderados y sustitutos de salud, con otras reglas (**A CONFIRMAR**) | Revisar el modelo de «representante» |
| **Autoridad y delegado** | AEPD o APDCAT; DPO (arts. 37 a 39) | Para el centro: HHS y la autoridad de Florida; el DPO europeo no es la figura estadounidense (un «responsable de privacidad» y otro «de seguridad» en HIPAA, **A CONFIRMAR**) | Nombrar quién lo asume en el centro |
| **Voz y dictado (Mistral)** | EIPD en curso; encargado Mistral | Probablemente **desactivado** para Miami hasta confirmar si Mistral firma un BAA y si puede procesar en la UE sin transferencia mala | Dejar el módulo apagado en ese centro |
| **Cookies y sesión (LSSI art. 22)** | Ley española | Aplica a la **web de ResidApp** si está establecida en España; otras leyes estatales de EE. UU. sobre cookies, **A CONFIRMAR** | — |
| **Zona horaria** | `zonas_centro` | Solo cambia cómo se ven las horas; los **plazos legales** (30 días, 24 h) se cuentan en horas reales. El cálculo de «hoy» ya sale de la zona | Comprobar que ningún plazo legal depende de «hoy» sin convertirse a UTC (ya lo comprueba `zona-horaria-centro-lectura-dpo.md`, apartado 4) |

## 5. Preguntas para la persona experta (abogado o abogada de EE. UU., ordenadas por importancia)

1. ¿El centro es **entidad cubierta** de HIPAA? ¿Qué tipo de licencia tiene en Florida (residencia con enfermería, vivienda asistida u otro) y quién la regula? ¿Factura electrónicamente a Medicare, Medicaid o seguros?
2. **§ 408.051(3):** ¿alcanza al centro y a ResidApp (tecnología «certificada»)? ¿Cabe guardar los datos en la UE? ¿Haría falta un despliegue de ResidApp en EE. UU. o Canadá?
3. ¿Cómo debe ser el **BAA** y qué exige a los subencargados (Microsoft, Mistral)? ¿Es compatible usar Mistral para el dictado?
4. **Conservación:** el plazo mínimo y máximo de la historia clínica y de los demás registros (accesos, auditoría) para ese tipo de centro, incluida la prescripción de demandas por negligencia en Florida.
5. **Brechas:** cómo se combinan los 30 días de Florida, los 60 días de HIPAA y el aviso de 10 días del agente; quién notifica y a quién (HHS, Departamento de Asuntos Legales, medios, personas).
6. **Derechos de las personas** (acceso, rectificación, listado de comunicaciones) y plazos; qué pasa con una petición de supresión.
7. **Representantes:** quién puede decidir y recibir información por un residente sin capacidad en Florida (tutor, apoderado, sustituto sanitario) y cómo cambia el Portal Familiar.
8. **Personal:** ¿los datos de los profesionales están protegidos por normas laborales o de privacidad específicas en Florida? ¿Se pueden registrar accesos y sesiones tal como hoy?
9. **Otras leyes** de privacidad del consumidor, de IA en sanidad, de voz o biometría, o de abuso de ancianos que afecten.
10. **Ley del contrato y foro:** qué se puede pactar (derecho español para el contrato entre ResidApp y el centro) sin chocar con normas imperativas locales; seguro de responsabilidad.
11. **Asociado comercial fuera de EE. UU.:** cómo afecta a ResidApp ser directamente supervisada por HHS.

### Preguntas para el DPO real europeo

1. ¿Aplica el RGPD a ResidApp como encargada con un responsable de EE. UU.? ¿Qué obligaciones exactas (arts. 28, 30.2, 32, 33.2) y cómo se combinan con HIPAA?
2. El acceso del personal de Miami a datos alojados en la UE, ¿es transferencia? ¿Qué vía del cap. V (cláusulas tipo, Marco UE-EE. UU.) y qué evaluación del riesgo?
3. Familias en la UE usando el Portal Familiar: art. 3.2.
4. Si ResidApp no estuviera establecida en la UE, art. 3 y art. 27.
5. ¿La EIPD y el registro de actividades (art. 30) deben recoger este centro? ¿Hay que cambiar el aviso de transferencias?
6. Autoridad competente para ResidApp (AEPD/APDCAT) según su domicilio.

## 6. Decisiones que debe tomar Orlando

(Una a una; Orlando decide, la persona experta valida.)

1. **¿Se quiere de verdad un centro en EE. UU. ahora?** Es un salto de mercado y de riesgo (otra ley, otro idioma, otro huso horario de soporte, otro seguro). Alternativa: aparcar hasta tener la sociedad titular y un asesor estadounidense contratado.
2. **¿Dónde se alojan los datos de ese centro?** UE (con transferencia y quizá incompatible con Florida) o región de EE. UU. (despliegue separado, con su ADR, su EIPD y su coste). Esta decisión depende de la pregunta 2 del apartado 5.
3. **¿Un contrato único o uno por jurisdicción?** Contrato de encargado (art. 28) más BAA.
4. **¿Quién firma por ResidApp, desde qué país?** La titularidad de ResidApp condiciona la aplicación del RGPD y la capacidad de responder ante HHS.
5. **¿Se apaga el dictado con Mistral para ese centro?** Mientras no haya BAA ni transferencia resuelta.
6. **¿Idioma de los textos legales?** Inglés y castellano.
7. **¿Plazo de conservación por centro?** Cambio de ingeniería previo al primer centro de EE. UU.
8. **¿Cobertura 24 h?** Ya hay una decisión abierta de cobertura nocturna (`protocolo-brechas-borrador.md`, apartado 5.2): con Miami, la persona de guardia de España vive 6 horas por delante; revisar.
9. **¿Quién actúa como responsable de privacidad y de seguridad en el centro?** Es del centro; ResidApp lo pide, no lo hace.

## 7. Condiciones mínimas para activar la zona

**Antes de activar `America/New_York` para cualquier centro de verdad** (todas las que sean necesarias, no hay atajo):

1. Estudio revisado y firmado por un abogado de EE. UU. (apartado 5) **y** un DPO real europeo (apartado 5).
2. Respuesta escrita a: entidad cubierta sí/no, tipo de licencia, y § 408.051(3) (dónde pueden estar los datos).
3. Si hay entidad cubierta: **BAA** firmado con el centro y con cada subencargado que toque datos. Si no se consigue con Mistral, el dictado queda apagado.
4. Contrato de encargado (art. 28) más anexo EE. UU., con la vía del cap. V decidida si el RGPD alcanza a ResidApp.
5. Textos informativos propios del centro (aviso de prácticas de privacidad y los de Florida) en inglés y castellano; no los actuales.
6. Plazo de conservación del centro decidido por escrito y reflejado en ResidApp como parámetro del centro.
7. Protocolo de brechas y procedimiento de derechos adaptados a EE. UU. (plazos, autoridades, contactos), con cobertura 24 h real (opción E ya decidida: sin cobertura, sin datos reales).
8. Evaluación de seguridad contra la regla de seguridad de HIPAA, y la EIPD y el registro de actividades al día.
9. Confirmar que la zona solo cambia la visualización y que ningún plazo legal depende de ella (ya lo hace `zona-horaria-centro-lectura-dpo.md`).
10. **Primero un centro piloto pequeño con datos ficticios**, como con los españoles.

## 8. Respuesta a la pregunta de fondo

**«¿Puede un centro en Miami usar la zona de Miami y regirse por las leyes de protección de datos de España?»**

- **Usar la zona de Miami: sí en lo técnico**, una vez validado este estudio. Es un ajuste de visualización y no cambia ninguna ley.
- **Regirse por las leyes españolas: no, como elección.** Las normas de protección de datos de salud son de orden público: las fija dónde está el centro, dónde están las personas y quién trata los datos, no un pacto. Un centro de Miami estará sujeto a la ley de EE. UU. y de Florida (casi seguro HIPAA, y la ley de Florida sobre brechas y la de historia clínica de su tipo de centro). **El RGPD solo llegará** al centro por una conexión con la UE (art. 3), y llegará a ResidApp como encargada si está establecida en la UE. Un contrato bajo derecho español no cubre las obligaciones locales del centro.
- **Lo que sí se puede hacer** es un nivel de protección europeo por contrato, **además** de lo local, con un BAA y las vías del cap. V cuando haga falta.
- **Riesgo que puede ser bloqueante:** la norma de almacenamiento en EE. UU. de Florida (§ 408.051(3)).

## A verificar o decidir (resumen)

- Todo el derecho de EE. UU. no incluido en la base local (HIPAA, regla de privacidad y de seguridad, BAA, plazos de brechas de HIPAA, § 408.051(3) y § 501.171 de Florida, regla 59A-4.118): **A CONFIRMAR POR PERSONA EXPERTA**. Las citas de eCFR y Florida vienen de una herramienta que devuelve resúmenes; **no son texto literal verificado**.
- Estado del Marco UE-EE. UU. (apelación C-703/25 P).
- Directrices del CEPD 3/2018 y 05/2021: **no leídas**.
- Cláusulas contractuales tipo de la Comisión (módulo encargado a responsable): **no leídas**.
- Titularidad y domicilio de ResidApp.
- Si Mistral puede firmar un BAA y dónde procesa.
- Tipo de licencia del centro en Florida y dónde viven sus familias.
- Capítulo 429 de Florida (vivienda asistida) y plazos de conservación fuera de las residencias con enfermería.

**No soy DPO designado ni abogado, y menos de EE. UU.**
