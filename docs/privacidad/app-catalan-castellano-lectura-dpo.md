# La aplicación entera en catalán y castellano: lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-24. Segunda lectura experta del agente `dpo-residapp`, no un dictamen. Lee `docs/tareas/alta-prioridad/pendientes-catalan.md` (apartados 1 a 8) y se apoya en las lecturas anteriores `portal-familiar-dos-idiomas.md` (2026-10-29) y `modulos-opcionales-lectura-dpo.md`. No modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado en el repositorio o en el texto de `normativa-local` (cito el archivo; la versión es la de `normativa-local/INDICE.md`, comprobada el 2026-11-20; son textos consolidados, no la versión oficial; las leyes catalanas están en castellano, versión del BOE); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar en el texto literal. No incluyo datos reales ni claves.

## Conclusión

**Admisible, y mejora la transparencia, con condiciones.** Que la aplicación hable el idioma de quien la usa ayuda a cumplir el art. 12.1 del RGPD, que exige información «inteligible y de fácil acceso, con un lenguaje claro y sencillo» (`rgpd.txt`, art. 12, apartado 1). Traducir la interfaz no crea por sí mismo ningún tratamiento nuevo de datos de residentes. Lo que sí crea son **tres cosas con efecto de privacidad** y un **riesgo de calidad del dato clínico** que la propuesta no menciona:

1. **El idioma de la cuenta del personal es un dato personal laboral nuevo**, y el de las familias ya estaba aceptado. Se trata igual (valor cerrado, nunca inferido, solo lo cambia la propia persona), pero el personal tiene además una relación laboral: el centro no debe poder usarlo para nada distinto de mostrar la pantalla (apartado 1).
2. **El idioma de un documento que sale de ResidApp no es el idioma de quien lo va a leer.** El PDF sale en el idioma de **quien lo imprime**, no del residente ni de su familia. Si un auxiliar con la cuenta en catalán imprime un documento para una familia que lee castellano, la transparencia (arts. 12 a 14) falla sin que nadie lo decida. Falta una regla de idioma por documento (apartado 5).
3. **Todo texto que el sistema escribe por su cuenta en un registro clínico o en la auditoría debe guardarse como código, no como frase traducida.** Si se guarda la frase en el idioma de quien actuó, el mismo registro queda en dos idiomas, no se puede buscar ni auditar igual, y un informe posterior mezcla lenguas sin que nadie lo haya escrito (apartado 5).
4. **El texto de privacidad y las cláusulas informativas en catalán son contenido jurídico**, no interfaz: los revisa una persona experta, no solo quien traduce la pantalla. Sigue sin decidirse cuál prevalece (apartado 4).

Lo demás (traducir con IA durante el desarrollo, correos, cookie, catálogos) se resuelve con reglas ya escritas y pocas cautelas nuevas.

**Lo más importante antes de construir:** decidir la regla de idioma por documento (apartado 5), la versión que prevalece (apartado 4) y que quede escrito que lo que sale de ResidApp hacia una persona residente o familiar **no** toma por defecto el idioma de la cuenta de quien lo saca.

## Estado verificado en el repositorio

- **[Hecho]** La aplicación fija `es-ES` aunque el servidor tenga otra cultura, y solo admite esa cultura (`src/ResidApp.Web/Program.cs:455-465`).
- **[Hecho]** Los correos ya existen por idioma `es`/`ca`; hoy el idioma lo da un ajuste de entorno, no la cuenta (`src/ResidApp.Infrastructure/Correo/CorreoOptions.cs:27-28`: `Idioma`, «mientras no exista el ajuste del centro (ADR 0013)»). Las plantillas son genéricas a propósito: solo reciben el idioma y la dirección base (`src/ResidApp.Application/Email/ChangeNoticeTemplate.cs:8`, `ProposalNoticeTemplate.cs:11`, `ReminderTemplate.cs:8`, `ExternalNoticeTemplate.cs:10`).
- **[Hecho]** El registro de envíos guarda el idioma de cada correo, limitado a `es`/`ca` (`database/scripts/0081_envios_correo.sql:22, 34`).
- **[Hecho]** La política de privacidad de la aplicación todavía dice «Agencia Española de Protección de Datos o Autoritat Catalana de Protecció de Dades, según el tipo de centro» (`src/ResidApp.Web/Views/Home/Privacy.cshtml:233`) y deja pendiente la conservación de 15 años (`:151`). Está en castellano; no hay versión catalana.
- **[Hecho]** El ADR 0013 (aceptado 2026-10-30) deja fuera la parte profesional, y el ADR 0012 sigue diciendo que la aplicación está «solo en español» (`0012-envio-correos.md:46-47`). Ambos hay que reescribir; no lo hago yo.
- **[Hecho]** Para el personal, el acceso inicial es por invitación entregada en mano, sin correo (`0007-proveedor-identidad.md:58`); el correo del personal no es dato obligatorio. El correo de las familias es opcional en el alta y choca con FAM-10 (`0007:116`, puerta abierta ya conocida).
- **[Hecho]** El ADR 0011 aprueba un único caso de uso de IA (la extracción estructurada del dictado) y dice que no se crea `IChatProvider` «porque no hay ningún caso de uso aprobado que lo use» (`0011-arquitectura-ia.md:38, 56`). Solo admite proveedores de una lista autorizada y no arranca con otro (`:58-61`).

## 1. Dónde y cómo se guarda el idioma de la cuenta (personal y familias)

**Respuesta a la pregunta 1 del traspaso:** es el mismo dato que ya aceptó la lectura de 2026-10-29 para familias (`portal-familiar-dos-idiomas.md`, apartado (b)), con **una diferencia**: para el personal es dato de empleado.

- **[Opinión] Dato personal: sí; categoría especial: no.** Va ligado a una cuenta identificada (RGPD art. 4.1). Sigo sin verlo como dato del art. 9 por sí mismo; en un contexto sanitario hay que tratarlo con la cautela de que no se use para deducir nada (origen, nacionalidad). La cita del TJUE que usé entonces (C-184/20) sigue **[A verificar]**.
- **Reglas que se mantienen para las dos poblaciones:** valor cerrado `es`/`ca`, vacío si no ha elegido; **no se infiere** (nombre, apellidos, IP, centro, `Accept-Language`); solo lo cambia la propia persona o Administración como rectificación; sin historial de valores anteriores; finalidad única (mostrar la pantalla, los PDF que saca esa persona y los correos); si se borra la cuenta, se va con ella.
- **Lo que añado para el personal:**
  1. **Ninguna pantalla lista el idioma por persona** ni lo exporta ni lo filtra (ni Administración, Dirección o Plataforma). El centro puede ver una cuenta, no «quién prefiere catalán». Un listado así sería un dato de plantilla sin finalidad, y en un centro pequeño señala a personas concretas. **[Opinión]**
  2. **No debe entrar en ningún control del trabajador** (informes de uso, fichajes, evaluaciones). Los derechos digitales laborales (LOPDGDD, arts. 87 a 91, `lopdgdd.txt`) no lo prohíben expresamente, pero la finalidad cerrada lo cubre: el art. 90.2 exige informar «de forma expresa, clara e inequívoca» al personal cuando hay un sistema de control; aquí no debe haberlo.
  3. **«Idioma por defecto del centro» no es dato personal [Suposición]**, pero ojo: si el centro tiene tres cuentas de personal y todas están en catalán, el ajuste del centro no añade nada. El problema aparece al **no mezclarlos**: el idioma de la cuenta gana, el del centro solo cubre a quien no eligió.
  4. **Aviso al personal.** Tiene que saber que existe el dato, para qué, que es voluntario y cómo cambiarlo (art. 13 RGPD). Va en la información al personal que adopta la residencia como empleadora. **[Decidir]** DPO real: base jurídica; **[Suposición]** interés legítimo del centro o ejecución del contrato de trabajo (art. 6.1.b y 6.1.f), sin consentimiento, porque no se pide permiso para mostrar una pantalla.
- **Dónde se guarda [Suposición, lo decide ingeniería]:** una columna de valor cerrado (con `CHECK` `es`/`ca`, como `0081`) en la cuenta del personal y en la del familiar. **No en `centros`** (esa tabla la lee el login y es el sitio del «idioma por defecto del centro», que sí es un ajuste, no un dato personal). **No en la cookie** cuando la persona está identificada.
- **Plataforma:** sigue siempre en castellano; no tiene campo. Dilo así en la ficha del tratamiento para que nadie añada un selector «por si acaso».
- **Conservación [Decidir]:** la de la cuenta. Hoy el plazo de la auditoría y de los registros técnicos sigue sin decidir (`antes-de-datos-reales.md`, puertas abiertas), y el idioma copiado en `envios_correo` entra en ese plazo. No lo repito: queda como una más de las cosas por decidir.
- **Registro de actividades (art. 30).** Al registro ya propuesto en 2026-10-29 (idioma preferido de la familia, idioma por defecto del centro, cookie técnica) hay que añadir: «idioma de la interfaz del personal». Categoría: datos identificativos del personal (no especiales). Destinatarios: nadie más allá de la propia aplicación; ver punto 1 de arriba.

## 2. Idioma de correos y avisos

- **Correos a familias (citas, recordatorios, cambios, invitación, código de acceso).** Ya están decididos por plantilla y por idioma y son genéricos (no llevan datos de la cita, del residente ni del equipo). Con la propuesta, el idioma sale de la cuenta del familiar y, si no la tiene, del centro. **[Hecho]** El correo del código lleva el mismo aviso contra fraude en los dos idiomas (`0012:46`).
- **Dato nuevo en el correo: ninguno.** Un correo en catalán no revela más que uno en castellano. Pero el **idioma de la plantilla** pasa a ser reflejo del idioma de la cuenta, y `envios_correo.idioma` guarda esa copia: misma conservación.
- **Correos al personal: no existen hoy** (alta por invitación en mano y recuperación por Administración, `0007:58-59`). Si algún día hay correo al personal (avisos, recuperación), valdrá lo mismo y exigirá su propia lectura. Mientras tanto, **no inventes un correo al personal para que el catalán «llegue a todos»**.
- **Recuperación de acceso.** Es el flujo más sensible: un mensaje de seguridad mal entendido es un riesgo de suplantación. El texto en catalán y en castellano debe decir lo mismo de **seguridad**, sin matices. Prueba obligatoria: el mismo conjunto de advertencias en las dos versiones (y que lo revise una persona del lado seguridad, no solo del lado idioma).
- **Enlace en el correo.** Sigue la regla de 2026-10-29: enlazar a `app.residapp.es` **sin parámetros** (`landings-y-passkeys-por-subdominio.md`, apartado 3); el idioma lo da la cuenta. Un enlace con `?idioma=` en un correo es una señal de suplantación para quien lo ve.
- **Avisos dentro de la aplicación.** Todo aviso del sistema (por ejemplo «cita sin cubrir») sale en el idioma de **quien lo lee**; no se almacena como frase, sino como código (apartado 5).
- **Contradicción FAM-10 / correo opcional** (puerta abierta de `0007:116`): no la resuelve esta propuesta. Si una familia no tiene correo, no recibe nada en ningún idioma y el aviso lo da la residencia por otra vía.

## 3. Pantallas de entrada compartidas y cookie de idioma

**Respuesta a la pregunta 2.** El diseño del ADR 0013, punto 4, sirve también para el personal, con cuatro precisiones:

1. **Orden de prioridad por quién es la persona:** una vez identificada, **gana el idioma de la cuenta**; antes, selector pulsado, parámetro `?idioma=` para esa visita, castellano. No se usa el idioma del centro antes de identificarse (no se sabe el centro). **[Hecho en el ADR; lo suscribo.]**
2. **La cookie solo se guarda cuando la persona pulsa el selector en la aplicación.** Es la condición que hace más clara la exención del art. 22.2 de la LSSI. **[Hecho]** El texto literal de esa exención está en `lssi.txt` (art. 22.2) y la guía de la AEPD en `aepd-guia-cookies.txt`; la aplicación al caso, **[A verificar]** con abogado, y el tope de caducidad sigue sin cifra.
3. **La cookie no distingue personal de familia.** Una pantalla de entrada común no debe mostrar nada que diga quién eres antes de identificarte (ADR 0007, ya recogido). Al ser el mismo selector para todos, la cookie solo guarda `es` o `ca`.
4. **Dispositivo compartido.** El personal comparte tablets y móviles (ADR 0009). La cookie de idioma persistente en un dispositivo común hace que una persona vea la pantalla de entrada en el idioma de la anterior. Es molesto, no es una brecha, pero **tras entrar manda la cuenta** y el selector debe estar visible. Si se guarda en el dispositivo, que dure poco y que cerrar sesión no la deje con datos de la persona. **[Opinión]**
5. **Caché y service worker.** Lo ya escrito vale: `Vary: Cookie` o sin caché para respuestas que cambian con el idioma, y la página `Offline` no lee idioma ni cookies. **[Añado]** El `sw.js` guarda en caché recursos del dispositivo: si se guardan pantallas por idioma, que una persona que cierra sesión no deje en caché una pantalla con datos propios (ya vale la regla de `directrices-pwa-movil.md`, líneas 42-43 citadas en 2026-10-29; **[A verificar]** que los textos nuevos no cambian esa regla).

## 4. Información al residente y a la familia, y textos legales bilingües

### 4.1 Qué obliga y a quién

- **El deber de informar es de la residencia (responsable).** Art. 12.1 RGPD: la información debe darse de forma «inteligible y de fácil acceso, con un lenguaje claro y sencillo». No fija un idioma concreto (el texto no lo menciona: no he encontrado la palabra «idioma» o «lengua» en `rgpd.txt` salvo en el considerando de lenguaje claro y sencillo y en el art. 34.2, comunicación de brechas «en un lenguaje claro y sencillo»). **[Hecho]**
- **Sanidad.** Ley 41/2002, art. 4.2: la información clínica «se comunicará al paciente de forma comprensible y adecuada a sus necesidades» (`ley-41-2002.txt`). Art. 5.2: el paciente será informado «de modo adecuado a sus posibilidades de comprensión», incluso en caso de incapacidad, y también su representante. **[Hecho]**
- **Servicios sociales (Cataluña).** Llei 12/2007, art. 10.f: derecho a «disponer de las ayudas y los apoyos necesarios para comprender la información que les sea dada si tienen dificultades derivadas del desconocimiento de la lengua o si tienen alguna discapacidad». Y art. 10.a y 10.b: información «suficiente y comprensible», por escrito y de palabra, «en lenguaje comprensible y accesible» (`llei-12-2007.txt`, versión BOE consolidada 2026-11-18). **[Hecho]** Esto es una pista clara de que **la información debe poder recibirse en la lengua que se entienda**; no he encontrado en la base local un precepto que obligue a la residencia a ofrecer **todo** en catalán.
- **Derechos lingüísticos en Cataluña (pregunta 6 del traspaso).** La base local no contiene la normativa lingüística de Cataluña (Estatut, Llei 1/1998 de política lingüística, Codi de consum de Catalunya). **[A verificar]** con abogado: si obliga a una residencia privada, concertada o pública a atender y a rotular en catalán, y si una cláusula informativa debe estar disponible en catalán. [Opinión] En la práctica, ofrecer **ambos idiomas** quita el problema, y la propuesta lo hace; el peso jurídico de la petición baja, pero sigue siendo útil saber si es una obligación o una mejora. No lo afirmes en un documento de venta hasta saberlo.
- **Residentes con capacidad disminuida (Ley 8/2021).** La información va al residente de forma adecuada y, si hay apoyo o representante, también a esa persona. **[Opinión]** La app no puede resolver con un selector de idioma quién entiende qué: **el idioma del documento lo elige la persona que informa**, con el residente o su representante delante (apartado 5).

### 4.2 Política de privacidad y cláusulas en catalán

- **No son interfaz, son contenido jurídico.** Los textos de privacidad, cookies, aviso legal y cláusulas informativas **los redacta o adopta el responsable (la residencia)**; ResidApp aporta la plantilla. La versión catalana no la puede validar solo quien traduce los botones. **[Decidir]** Quién revisa el catalán jurídico (hoy CJ y el traductor externo del ADR 0012 revisan el catalán de pantallas y correos; el DPO real o un abogado debe validar que el texto catalán dice exactamente lo mismo que el castellano).
- **Cuál prevalece.** La norma que he podido leer no lo dice. **[Decidir]** Orlando, con validación jurídica: propongo que **las dos versiones sean oficiales y equivalentes**, con una cláusula «en caso de discrepancia entre versiones, prevalece [la versión castellana / la versión catalana]» y que las discrepancias reales se traten como un fallo de mantenimiento. [Opinión] Lo más prudente para un centro catalán es **no dejar que prevalezca el castellano por defecto** sin saber si hay obligación de ofrecer la versión catalana como oficial. La fórmula concreta es de abogado. El Codi de consum de Catalunya (art. 128-1, citado ya en `antes-de-datos-reales.md`) **[A verificar]** si se aplica a una web B2B.
- **Qué debe llevar cada documento legal** para que se pueda mantener: versión, fecha, idioma, y un identificador común a las dos lenguas («misma versión 2.1 en es y ca»). La prueba que sugería el traspaso («los dos ficheros tienen las mismas claves») sirve para pantallas; para textos legales hace falta además **una revisión conjunta** cada vez que cambia uno. **[Opinión]**
- **Aceptaciones y consentimientos.** Si ResidApp deja constancia de que alguien aceptó una política, una invitación o un consentimiento, guarda **qué versión** y **en qué idioma** se mostró. No hace falta guardar el texto entero, pero sin la versión y el idioma no se puede demostrar qué se aceptó (art. 5.2 y 7.1 RGPD: «el responsable deberá ser capaz de demostrar», `rgpd.txt`, **[A verificar]** el texto exacto del 7.1 antes de citarlo). Hoy no he verificado qué aceptaciones guarda la aplicación: **[A verificar en el código]**.
- **Consentimiento informado clínico (Ley 41/2002, art. 8).** Debe darse «recibida la información prevista en el artículo 4» (`ley-41-2002.txt`). Si algún día ResidApp genera un documento de consentimiento, el residente o su representante firma **el idioma que entiende**. El PDF debe poder generarse en el idioma de esa persona, no en el de quien lo imprime (apartado 5).

### 4.3 Información al personal

El personal debe saber en qué idioma ve la aplicación, que puede cambiarlo, y que lo que escribe o dicta **no se traduce** (ya lo dice el traspaso, apartado 1). Hay un matiz que conviene escribir: **el dictado en catalán** exige que el modelo de voz lo admita. **[A verificar]** que Voxtral transcribe catalán con calidad suficiente y que el esquema estructurado del ADR 0011 no pierda valores clínicos por el idioma. Hoy el dictado se aprobó sin revisar el idioma de entrada (`0011-arquitectura-ia.md`; el puerto `ISpeechTranscriber` recibe `language`, línea 46). Si se activa en catalán, es un cambio del caso de uso aprobado y pasa por el DPO y por CJ.

## 5. PDF, impresión y documentos clínicos en dos idiomas

Aquí está el riesgo que más pesa y que la propuesta no ha visto.

1. **Idioma del documento ≠ idioma de quien lo saca.** El traspaso dice que «todo lo que se imprima o salga en PDF sale en el idioma elegido en la aplicación» (apartado 1). Para un informe interno del personal, bien. Para **cualquier documento que se entrega a una persona residente, a un representante o a una familia** (informes para acceso, exportación de historia, consentimientos, hojas de cuidados), esa regla puede entregar un papel en un idioma que el destinatario no lee, sin que nadie lo haya decidido. **[Opinión]** Regla propuesta: **en esos documentos, el idioma se elige al generar el PDF** (selector visible, con el idioma de la cuenta como valor inicial) y **queda impreso en el propio documento** (una línea: «Documento en [idioma], plantilla v. X»). **[Decidir]** Orlando, con CJ y el DPO real.
2. **No añadas un «idioma preferido» al residente para resolverlo.** Sería un dato nuevo de una persona vulnerable, sin finalidad clara y con riesgo de inferencia. La conversación con la persona o su representante basta. **[Opinión]**
3. **El contenido clínico no se traduce.** Lo que escribió el personal en castellano sale en castellano aunque la plantilla esté en catalán (CJ lo acepta: 3.3 del traspaso). Es un documento legalmente de **una sola fuente**: el contenido es el original. Lo que se traduce son las etiquetas, los títulos y los textos fijos. **No hace falta aviso de idioma mixto por transparencia hacia la persona**, pero sí por **exactitud** si el documento sale del centro (informe a un tercero, a otro centro, para un juez): una línea en el pie: «Las notas del personal figuran en el idioma en que se escribieron». **[Opinión]** Respuesta a la pregunta 4 del traspaso: **no es obligatorio por el RGPD, es conveniente** y barato.
4. **Textos que el sistema inserta en el registro clínico o en la auditoría: guardarlos como código, no como frase.** Ejemplo: si al aprobar algo el sistema escribe una anotación automática, no se guarda «Cambio aprobado» o «Canvi aprovat» según quien actuó, sino un código que la pantalla y el PDF traducen al leerlo. Si no, el mismo hecho queda en dos idiomas en la base, las búsquedas fallan y la auditoría pierde valor probatorio. **[Hecho en el espíritu del diseño:** `envios_correo` ya guarda códigos de plantilla y de motivo, no frases (`0081_envios_correo.sql`)]. **[A verificar en el código]** qué mensajes de `ApplicationResult` y de los controladores acaban guardados en base.
5. **Reimpresión de un documento antiguo.** Un PDF generado hoy de un registro de hace tres años: las etiquetas salen en el idioma de hoy, el contenido original no cambia. Está bien. Lo que no puede ocurrir es que la **versión de la plantilla** cambie un título o una definición clínica de forma que el documento reimpreso diga otra cosa distinta de la que se firmó. Por eso la plantilla lleva versión y el glosario no reescribe valores clínicos (traspaso, apartado 8). **[Opinión]**
6. **Fechas y números.** `ca-ES` frente a `es-ES` cambia los textos («19 de novembre de 2026») y puede cambiar separadores decimales. **[A verificar]** que ningún valor clínico (peso, glucemia, dosis, escalas) cambia de coma/punto según el idioma al mostrarse, al guardarse o al importarse. **Un error de formato en un valor clínico es un riesgo de seguridad del paciente, no solo de privacidad** (art. 5.1.d RGPD, exactitud). Regla: el valor se guarda siempre en formato invariante; solo la presentación cambia.
7. **Metadatos del PDF.** Que el idioma del documento figure en las propiedades del PDF (accesibilidad) y que el pie con marca y versión no revele nada más.
8. **Sin traducción automática en el servidor.** Los PDF no llaman a ningún traductor externo; las plantillas son ficheros del repositorio. Es la única opción compatible con el ADR 0011.

## 6. Traducir con traductor externo o con IA: ¿se transfieren datos?

**Respuesta a la pregunta 5.**

- **Los textos de la interfaz no llevan datos personales, siempre que se traduzcan las plantillas, no las pantallas con datos.** [Hecho] Los ficheros fuente están en el repositorio, que es **público** (`CLAUDE.md`: «es público»), así que no contienen datos personales de nadie. Lo que va a traducirse son frases con marcadores (`{nombre}`, `{fecha}`), no datos.
- **ADR 0011.** Solo regula las funciones **del producto** con IA (el dictado es el único caso aprobado, `0011:38`), no las herramientas con las que se desarrolla. Traducir **durante el desarrollo** textos fijos con una herramienta de IA **no choca** con el ADR 0011 y no es una función de ResidApp. **[Opinión]** Pero la regla del criterio del proyecto (la IA solo si no basta lo determinista) se cumple: es trabajo de desarrollo, no de producto.
- **Lo que no debe hacerse nunca, con IA ni con traductor humano:**
  1. **Pegar capturas, textos de pantallas o ejemplos con datos reales o de prueba que sean reales** («para dar contexto»). Las pantallas de Azure dev llevan datos de prueba; aun así, el hábito es peligroso. Se traduce **la clave y su frase**, nunca la pantalla.
  2. **Pegar las respuestas de CJ** (`respuestas-cj-local/`) para dar contexto. No son datos personales de residentes, pero el traspaso ya dice que no se copian al repositorio, y tampoco a un tercero.
  3. **Pegar el Manual con capturas.** Si el Manual lleva imágenes con datos (aunque sean ficticios), no van al traductor.
  4. **Pasar a un traductor externo textos clínicos de ejemplo** reales («para que quede natural»). Los ejemplos del glosario se inventan.
- **Traductor externo humano.** El ADR 0012 prevé ingeniería más traductor externo (`0012:45`). **[Decidir]** Contrato de confidencialidad con el traductor (no procesa datos personales, pero sí información interna del producto) y que trabaje sobre ficheros de texto con marcadores, no sobre la aplicación en marcha ni con acceso a Azure. No es subencargado si no tiene acceso a datos personales; **si alguna vez ve datos reales, sí lo sería** y habría que cumplir el art. 28. **[Suposición]**.
- **Herramienta de IA para traducir durante el desarrollo.** Aunque no haya datos personales, **no uses herramientas que reutilicen lo enviado para entrenar** si hay texto de producto aún no público (la propia hoja de ruta). **[Opinión]** Verifica en las condiciones de la herramienta. Lo escrito en el repositorio público ya es público.
- **Traducción en caliente (en producción) de lo que escribe el personal: prohibida** por el criterio actual del ADR 0011 (no hay caso de uso aprobado; el traspaso ya lo dice en el apartado 8). Se mantiene. Añadir un día un traductor automático sería un caso de uso nuevo con subencargado, EIPD y revisión del Reglamento de IA.
- **Reglamento de IA.** Para el uso de IA como herramienta de desarrollo sobre textos fijos no veo obligación propia del producto. Para una función futura de traducción de notas, el análisis sería nuevo (el de ADR 0011 y `modulos-opcionales-lectura-dpo.md`). **[A verificar]** el consolidado (`reglamento-ia.txt`) antes de afirmarlo en ningún documento externo.

## 7. Catálogos en base de datos (tipos de comunicado, equipos)

**Respuesta a la pregunta 7.**

- **Tipos de comunicado (catálogo del sistema).** Son nombres genéricos, no datos de nadie. Guardar **el código** y traducir al mostrar. Que el nombre en catalán no cambie el significado, y que las estadísticas y los filtros usen el código, no el nombre.
- **Equipos.** **[Suposición, A verificar]** Si el nombre de un equipo lo escribe cada centro («Planta 2», «Equipo A»), **no se traduce**: es un dato del centro, no del sistema, y puede contener nombres de personas (por ejemplo, «Equipo de María»). Esa posibilidad es la cautela: **un nombre libre puede ser un dato personal**. Los nombres de equipo no deben viajar a un traductor ni aparecer en los correos (ya no aparecen: las plantillas son genéricas). Si el equipo es un tipo del sistema (por ejemplo «Enfermería»), se trata como un tipo de comunicado.
- **Enums de `<select>`.** El valor guardado es un código estable; la etiqueta se traduce. **Nunca guardar la etiqueta traducida.** [Hecho en la práctica del proyecto: `.ToCode()`, por ejemplo `HealthBackgroundModels.cs:118`.]
- **Ordenación y búsqueda.** El orden de los listados hoy usa `es-ES` (`GuiaModels.cs:72, 203`; `Position.cs:65`). En catalán la ordenación y la comparación sin acentos pueden dar resultados distintos al buscar a una persona. Una búsqueda que no encuentra «Núria» o «Josep Mª» por un fallo de cultura **es un riesgo de seguridad asistencial** (se puede duplicar una ficha). **[Opinión]** Búsquedas de personas sin distinguir mayúsculas ni acentos, **igual en los dos idiomas**, y probado.

## 8. Registro de actividades, EIPD y documentos que se ven afectados

- **Registro de actividades (art. 30).** Las líneas nuevas son pequeñas, **sin cambio de finalidad ni de categorías de datos de salud**:
  1. Idioma de la interfaz del personal (apartado 1). Idioma por defecto del centro. Cookie técnica de idioma.
  2. Sin subencargado nuevo (la traducción es de desarrollo, no de ejecución).
  3. Actualizar el anexo de subencargados **no hace falta** por este cambio.
- **EIPD.** **[Opinión]** No exige una EIPD propia: no cambia datos, fines, destinatarios ni riesgos de los tratamientos de salud ya evaluados. Sí exige **revisar la EIPD ya prevista** (voz y salud; acceso con NFC; las de `antes-de-datos-reales.md`) para comprobar que ninguna depende del idioma. Los dos puntos del idioma que sí tocan el riesgo (el PDF entregado a un tercero y el dictado en catalán) se anotan en la EIPD del centro como medidas, no como tratamiento nuevo (art. 35.11 RGPD: revisar «cuando cambie el riesgo»; el texto exacto está en `rgpd.txt`).
- **Política de privacidad de ResidApp** (`Privacy.cshtml`): hoy solo en castellano y con puntos `@Pend` abiertos (por ejemplo `:151`, `:233`). **No traducirla al catalán antes de que el texto castellano esté validado**: se traduciría un borrador y quedaría doble el trabajo de revisión. **[Opinión]**
- **Información a familias, a representantes y al personal:** en los dos idiomas, con la misma versión (apartado 4).
- **Contrato de encargado:** ResidApp no deja de ser encargada. Que la residencia pueda fijar el idioma por defecto es **una instrucción documentada del responsable** (art. 28.3.a). Basta una cláusula o un parámetro de configuración por escrito.

## 9. Autoridad de control

**Respuesta a la pregunta 8.** **[Hecho]** El art. 3 de la Llei 32/2010 (`llei-32-2010.txt`, versión BOE actualizada 2026-11-17) fija el ámbito de la APDCAT. Para una residencia son relevantes estos casos:

- Las entidades públicas (letras a, b, c, d).
- «e) Las entidades de derecho privado que cumplan, como mínimo, uno de los tres requisitos [...]: capital mayoritariamente público; **ingresos presupuestarios provenientes mayoritariamente de dichos entes públicos**; o directivos designados mayoritariamente por ellos».
- «f) Las demás entidades de derecho privado que **prestan servicios públicos mediante cualquier forma de gestión directa o indirecta**, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios».

**Lectura para el piloto [Suposición]:**
- **Residencia privada pura** (sin plazas concertadas ni financiación pública mayoritaria): **AEPD**.
- **Residencia concertada o con plazas públicas** (gestión indirecta de un servicio público): **APDCAT, al menos para los tratamientos vinculados a esas plazas** (letra f), y entera si los ingresos son mayoritariamente públicos (letra e). Un centro mixto puede quedar con **dos autoridades según el tratamiento**. **[Decidir / A verificar con abogado]** Cada caso concreto.
- **Residencia pública o de un ente local:** **APDCAT**.

**Qué cambia con el catalán:** la propuesta **no cambia la autoridad**. Lo único: el texto de privacidad en catalán debe nombrar a la autoridad **correcta de ese centro** (no «AEPD o APDCAT»), y una persona que lee en catalán debe poder encontrar cómo reclamar. Para ResidApp como encargada: si algún cliente es del ámbito APDCAT, esa autoridad también puede dirigirse a ella. **Pide a Orlando el tipo de titularidad y de financiación del centro piloto**: es un dato que falta desde 2026-10-29. Si es **concertado**, el Esquema Nacional de Seguridad (RD 311/2022) también aplica en la parte pública (`ens-rd-311-2022.txt`; la definición de sector público está en `ley-40-2015.txt`, art. 2; **[A verificar]** en cada caso).

## 10. Riesgos que no estaban en la propuesta

1. **Dos textos clínicos de un mismo hecho.** Ver apartado 5, punto 4: guardar códigos, no frases.
2. **Formato numérico en catalán** (coma decimal, separador de miles): un valor clínico mal leído o mal importado. Ver apartado 5, punto 6.
3. **Búsqueda de personas** distinta según el idioma (apartado 7).
4. **Errores de traducción en seguridad.** Un mensaje de bloqueo, de recuperación o de error de permisos mal traducido puede dar una pista a quien ataca (por ejemplo, decir en un idioma «usuario o contraseña incorrectos» y en otro «la cuenta no existe»). **Los mensajes de error de acceso deben decir lo mismo en los dos idiomas, incluidos los ambiguos a propósito.** **[Opinión]** Prueba: comparar las claves de entrada y de acceso.
5. **Mensajes de error que muestran datos.** Algunas capas devuelven textos con nombres o fechas. Al pasar a claves con marcadores, comprobar que no se inserta más de lo que se insertaba antes.
6. **Traducción incompleta = mezcla de idiomas.** Una pantalla que mezcla idiomas puede confundir en seguridad clínica. La regla del traspaso («áreas completas, no pantallas a medias») es correcta; añade que **el respaldo a castellano por clave debe verse en pruebas** (que una clave sin traducir se note, no se oculte).
7. **El Manual y la guía como documentación del producto.** Sirven de apoyo para entender la aplicación. Si un manual en catalán dice algo distinto del de castellano en un punto de privacidad o de seguridad (por ejemplo, quién ve el historial), la discrepancia es un incumplimiento de transparencia. Mismo control de versión en las dos lenguas.
8. **Repositorio público.** Los glosarios, las respuestas de CJ y los comentarios con nombres de personas del centro no van al repositorio. El glosario es de términos, no de personas.
9. **Centro de Día y otros servicios.** Si el catalán se hace base para todos los centros, no se activa por centro. No hay riesgo propio, pero la **política** del centro debe declarar en qué idiomas está disponible.
10. **Accesibilidad y lectura fácil.** Para residentes con capacidad disminuida, la Ley 8/2021 y la Ley 41/2002 piden comunicación adecuada a sus posibilidades. Un documento en dos idiomas no resuelve eso; son preguntas distintas. No presentarlo como «accesible» sin más.

## Decisiones que necesita Orlando (de una en una)

1. **Idioma del documento que sale hacia residentes, representantes y familias:** ¿por defecto el de la cuenta de quien lo saca, o se elige siempre al generarlo? Mi propuesta: elegir al generar, con el de la cuenta como valor inicial, e imprimir el idioma y la versión en el documento (apartado 5).
2. **Versión que prevalece** si el texto legal discrepa: castellano, catalán, o las dos equivalentes. Mi propuesta: las dos oficiales; la regla exacta la fija la persona experta (apartado 4.2). Sigue abierto desde el ADR 0013.
3. **Dónde se elige el idioma del personal** («Mi cuenta» con el del centro por defecto) y quién fija el idioma por defecto del centro: la residencia, como responsable, por escrito. Que ninguna pantalla lo liste (apartado 1).
4. **Quién revisa el catalán jurídico** (política, cookies, cláusulas), distinto de quien revisa pantallas y manual (apartado 4.2).
5. **Traductor externo:** si lo hay y cómo se le da acceso (solo ficheros de texto; sin acceso a Azure ni a datos) (apartado 6). Y confirmar la regla de «nunca pegar pantallas ni capturas».
6. **Tipo de titularidad y financiación del centro piloto** (privado, concertado, público): decide la autoridad y el Esquema Nacional de Seguridad (apartado 9).
7. **Dictado en catalán:** ¿se activa o el dictado sigue solo en castellano en la primera versión? Cambia el caso de uso aprobado (apartado 4.3).

## Qué debe validar una persona experta (DPO real o abogado), por orden de importancia

1. Regla de idioma por documento hacia residentes y familias, y si la línea de pie «las notas figuran en el idioma en que se escribieron» basta (apartado 5).
2. Versión que prevalece y redacción de la cláusula de discrepancia; si la normativa lingüística catalana obliga a la residencia a ofrecer la información en catalán (apartados 4.1 y 4.2).
3. Texto catalán de la política de privacidad, aviso legal, cookies y cláusulas informativas: que dice lo mismo que el castellano.
4. Base jurídica y información al personal sobre el idioma de la cuenta (apartado 1).
5. Autoridad de control según el centro piloto (apartado 9).
6. Cookie de idioma: caducidad y exención (apartado 3).
7. Que el traductor externo y las herramientas de IA de desarrollo no ven datos personales y su régimen contractual (apartado 6).

## A verificar o decidir

- **[A verificar]** Normativa lingüística catalana (Estatut, Llei 1/1998, Codi de consum, art. 128-1) y su efecto sobre una residencia. No está en `normativa-local`; la búsqueda en web debe hacerla una persona con fuente oficial (DOGC, Portal Jurídic) o añadirse al catálogo.
- **[A verificar]** Art. 7.1 RGPD (demostrar el consentimiento) y art. 35.11: leer el texto literal antes de citarlos en un documento externo.
- **[A verificar]** Si Voxtral transcribe catalán con calidad suficiente (apartado 4.3).
- **[A verificar en el código]** Qué textos de `ApplicationResult`, controladores y auditoría se guardan en base como frase y no como código (apartado 5.4); y qué aceptaciones guarda hoy la aplicación (apartado 4.2).
- **[A verificar en el código]** Que los valores clínicos no cambian de formato con la cultura (apartado 5.6) y que la búsqueda de personas funciona igual en los dos idiomas (apartado 7).
- **[A verificar]** Si los nombres de equipo son libres por centro (apartado 7).
- **[A verificar]** Que `sw.js` y la caché no guardan pantallas con datos por idioma tras cerrar sesión.
- **[A verificar]** TJUE C-184/20 (abierto desde 2026-10-29). Tope de caducidad de la cookie de idioma (abierto).
- **[A verificar]** Plazos de conservación del idioma copiado en `envios_correo` y de la auditoría (sin decidir, `antes-de-datos-reales.md`).

**Yo no soy DPO designado ni abogado.** Todo lo anterior lo valida una persona antes de usarlo.

## Fuentes consultadas el 2026-11-24

Repositorio (rama `docs-catalan-lectura-dpo`): `docs/tareas/alta-prioridad/pendientes-catalan.md`, `docs/decisiones-arquitectura/0007`, `0011`, `0012`, `0013`, `docs/privacidad/portal-familiar-dos-idiomas.md`, `modulos-opcionales-lectura-dpo.md`, `antes-de-datos-reales.md`, `src/ResidApp.Web/Program.cs`, `Views/Home/Privacy.cshtml`, `src/ResidApp.Application/Email/*Template.cs`, `CorreoOptions.cs`, `database/scripts/0081_envios_correo.sql`.

Base normativa local (`normativa-local/`, comprobada el 2026-11-20; textos consolidados, no versión oficial; leyes catalanas en castellano por el BOE): `rgpd.txt` (art. 12.1, 34.2), `lopdgdd.txt` (arts. 11, 87 a 91), `ley-41-2002.txt` (arts. 4, 5, 8), `llei-12-2007.txt` (art. 10), `llei-32-2010.txt` (art. 3), `lssi.txt`, `aepd-guia-cookies.txt`. No usé web.

## Decisiones de Orlando

- **1. Idioma de los documentos hacia residentes y familias (2026-11-24):** se elige al generarlo (castellano o catalán, con el del centro como valor por defecto) y queda impreso en el propio documento. No se crea un «idioma del residente». Pendiente de validación por persona experta.
- **2. Prevalencia del texto legal (2026-11-24):** las dos versiones son oficiales y se mantienen idénticas; la fórmula exacta la fija un abogado antes de usarlo con datos reales.
- **3. Idioma del personal y del centro (2026-11-24):** cada persona elige el suyo en su perfil; si no lo ha elegido, se usa el del centro, que fija el administrador del centro. Ninguna pantalla lista ni exporta el idioma de otra persona.
- **4. Revisión del catalán jurídico (2026-11-24):** la persona experta (abogado o DPO real) que valide el castellano revisa también el catalán jurídico, o lo hace un jurista catalán. CJ revisa solo las pantallas.
- **5. Traductor externo (2026-11-24):** no por ahora. Traduce ingeniería sobre ficheros de texto y CJ revisa con gente del centro. Si más adelante hay traductor externo, solo con ficheros de texto, sin acceso a Azure ni a datos reales.
- **6. Titularidad y financiación del centro piloto (2026-11-24):** se desconoce todavía; se pregunta a CJ. No bloquea construir el catalán, pero sí decide la autoridad de control (AEPD o APDCAT).
- **7. Dictado en catalán (2026-11-24):** fuera de la primera versión; sigue en castellano. Se añade después, tras probar Voxtral con voz real en catalán y actualizar el ADR 0011 con el DPO.
