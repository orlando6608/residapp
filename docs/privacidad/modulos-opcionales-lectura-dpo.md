# Módulos opcionales por centro (futuro ADR 0015): lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-09. Segunda lectura experta del agente `dpo-residapp`, no un dictamen. Es una lectura previa al ADR 0015: no modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar en fuente oficial. No incluyo datos reales, secretos ni claves.

Las decisiones de Orlando del 2026-11-09 (plan por censo más módulos aparte, solo lectura al apagar, dashboard de Plataforma fuera del ADR, todo activable en desarrollo) las trato como dadas. Lo que critico es el diseño que propone.

## Conclusión

**Admisible con condiciones.** Tener módulos por centro es una mejora de privacidad respecto a hoy, porque hoy el dictado se enciende **para todos los centros a la vez** con una clave global. Lo que cambia, y es lo que hay que escribir bien en el ADR, es que **activar un módulo deja de ser un ajuste técnico y pasa a ser una decisión sobre el tratamiento de datos de un centro**. Esa decisión es del centro (el responsable), no de ResidApp (la encargada). La tabla `modulos_centro` es, en lenguaje llano, el libro donde queda escrito qué ha decidido cada centro y quién lo ha ejecutado.

Los seis puntos que cambiaría o añadiría al diseño, por gravedad:

1. **«Solo Plataforma activa» necesita una instrucción documentada del centro detrás.** Sin ella, ResidApp estaría decidiendo por su cuenta qué datos de salud van a un tercero, y el RGPD dice que una encargada que decide fines o medios pasa a ser responsable (art. 28.3.a y 28.10, leídos en el BOE el 2026-11-09). Una «prueba gratuita» o una activación comercial en un centro con datos reales entra en este caso (apartado 1).
2. **La «condición de privacidad» del catálogo no debe ser un texto que se lee: debe ser una lista de comprobación que se guarda al activar**, con la versión de la ficha del módulo que el centro aceptó. Si la ficha cambia de forma importante (por ejemplo, un subencargado nuevo), la activación ya hecha deja de ser válida hasta que el centro la acepte de nuevo (apartado 2).
3. **El centro debe poder apagar un módulo él mismo, de inmediato.** Encender requiere a Plataforma; apagar es la medida que protege. Si un centro tiene que esperar a que una sola persona de ResidApp conteste para parar un tratamiento, el control no es suyo (apartado 3).
4. **«Consultar y exportar siguen» solo es verdad si la exportación cubre los datos del módulo, y hoy no cubre nada clínico.** Un módulo no debería poder guardar datos reales mientras no tenga definida su salida (apartados 5 y 8).
5. **El interruptor «todo activo fuera de producción» tiene que ser a prueba de errores de configuración**: que no sea una fila sembrada en la base, que no dependa solo del nombre del entorno y que no pueda viajar a producción con una copia (apartado 4).
6. **Medicamentos contradice hoy el alcance del producto** y puede ser producto sanitario. Debe quedar anotado como bloqueado hasta que CJ reabra el alcance y se haga una evaluación específica (apartado 7).

## Estado actual verificado en el repositorio

- **[Hecho]** El dictado se enciende globalmente con `IA__Mistral__ApiKey`; sin ella, el endpoint responde 404 (`src/ResidApp.Infrastructure/IA/IaOptions.cs:24-25`; `src/ResidApp.Web/Controllers/AuxiliarController.Dictado.cs:29-32`). La comprobación está en el controlador, no en el caso de uso.
- **[Hecho]** El correo se enciende con ajustes de entorno de la Web App (`Correo__Activo`, `Correo__AvisoCambioCita`), no por centro (`docs/decisiones-arquitectura/0012-envio-correos.md:81`).
- **[Hecho]** La política de privacidad de la aplicación ya dice «Solo interviene si el centro tiene activado el dictado» y «Solo interviene si el centro los tiene activados» (`src/ResidApp.Web/Views/Home/Privacy.cshtml:112-113`, `156-157`). **Hoy eso no es cierto**: la activación es global. El módulo lo vuelve cierto; mientras no exista, esas frases prometen algo que no hay (la página ya está marcada como «Texto en revisión»).
- **[Hecho]** El centro del ámbito activo viaja en una cookie que el ADR 0008 declara **no fiable** (`0008-multicentro-y-grupos.md:42`); el controlador del dictado lee el centro de esa cookie (`AuxiliarController.Dictado.cs:34, 43`). La comprobación de módulo debe usar el centro ya validado contra la base, no el de la cookie.
- **[Hecho]** `centros` no tiene política de filas porque el login la lee antes de elegir ámbito (`0008:68`). Plataforma solo inserta en `eventos_auditoria` de otros centros; no lee la auditoría ajena (`0008:63-65`). Plataforma «no cambia un centro ya creado» (`0006-perfil-plataforma.md:46-48`).
- **[Hecho]** Una tabla nueva no recibe permiso alguno hasta que un script lo declara, y `PermisosAplicacionTests` lo obliga (`0008:114`; `antes-de-datos-reales.md:149-152`).
- **[Hecho]** Hay configuración por centro ya existente: `permisos_perfil` (`database/scripts/0002_renombrado_espanol_sqlserver.sql:233-253`, con histórico: se revoca, no se borra) y `citas_configuracion_centro` (`database/scripts/0054_citas_reserva_directa.sql:24`).
- **[Hecho]** La exportación de hoy es solo estructura y censo de residentes activos, con identidad administrativa; «Nada clínico» (`0010-importacion-exportacion-ficheros.md:38, 72-76`). El alcance deja fuera la «exportación masiva de historias clínicas individuales» (`docs/producto/alcance.md:48`).
- **[Hecho]** La política de la aplicación afirma que ResidApp «no guarda la medicación de las personas residentes ni su administración» (`Privacy.cshtml:54`); el alcance deja fuera la «administración de medicación» (`alcance.md:45`) y el ADR 0011 recoge que «ResidApp no guarda medicación» (`0011-arquitectura-ia.md:75-77`).

## 1. Pregunta a): activar un módulo cambia el tratamiento

**Respuesta corta: sí, en los tres módulos que citas.** No hace falta que sea siempre una EIPD nueva, pero sí que el centro **decida y deje constancia antes**.

| Módulo | Qué cambia para el centro | Lo que tiene que existir antes de activar |
|---|---|---|
| Dictado por voz | Nuevo **subencargado** (Mistral), nueva categoría de dato (la voz del personal, y lo que dice de la persona residente, que puede ser dato de salud), tecnología nueva | Autorización del centro a ResidApp para usar a Mistral (art. 28.2; la primera frase del apartado dice «autorización previa por escrito, específica o general»; el resto, sobre cambios y oposición, no lo he leído literal: **[A verificar]**); contrato con Mistral que cubra salud y retención cero (`docs/seguridad/dictado-por-voz-dossier-dpo.md:63-65, 70-72, 83-84`); EIPD del centro que lo incluya; información al personal y a las personas residentes |
| Dashboard de Dirección | **Finalidad nueva** para los datos que ya hay: gestión y toma de decisiones con cifras agregadas. No hay tercero nuevo | Que el centro lo incluya en su registro de actividades y en su EIPD; comprobar compatibilidad del fin (art. 6.4, leído solo en parte); garantías de agregación (apartado 6) |
| Medicamentos | **Categoría nueva de dato clínico** y riesgo para la salud, no solo para la privacidad. Hoy fuera de alcance | Apartado 7 |

**Quién decide.** [Hecho, BOE 2026-11-09] La encargada «tratará los datos personales únicamente siguiendo instrucciones documentadas del responsable» (art. 28.3.a). Quien decide es la residencia; ResidApp ejecuta. Por eso «solo Plataforma activa» es correcto como **quién aprieta el botón**, pero no como **quién decide**. [Opinión] Esto también afecta a:

- **Pruebas gratuitas o demos en un centro real.** Activar un módulo «para que lo pruebe» con residentes reales es un tratamiento real. Las demos van siempre sobre desarrollo con datos ficticios (`entorno-produccion-lectura-dpo.md:222`).
- **Activar módulos por motivos comerciales sin que el centro lo haya pedido.**

**EIPD.** [Hecho, BOE 2026-11-09] La evaluación de impacto la hace el responsable antes de empezar (art. 35.1), es obligatoria, entre otros casos, en el «tratamiento a gran escala de las categorías especiales de datos» (35.3.b) y debe revisarse cuando cambie el riesgo (35.11). Si el riesgo sigue siendo alto tras las medidas, consulta previa a la autoridad (art. 36.1). [Opinión] Cada activación es un «cambio de riesgo» a efectos del 35.11: el centro debe poder decir «mi EIPD ya cubre este módulo». ResidApp ayuda aportando, por módulo, un **anexo de EIPD** (datos, flujos, terceros, riesgos, medidas). La lista de supuestos de la AEPD (art. 35.4), comprobada el 2026-11-12 en el PDF de la base normativa local: se cumplen para ResidApp los puntos 4 (categorías especiales, art. 9.1), 7 (datos a gran escala) y 9 (sujetos vulnerables, entre ellos mayores con algún grado de discapacidad y personas que acceden a servicios sociales); la lista presume la necesidad de EIPD «en la mayoría de los casos» con dos o más criterios.

**Qué debe registrar la plataforma al activar** (propuesta para `modulos_centro` y su histórico; todo sin datos de residentes):

1. Centro y módulo.
2. Estado anterior y nuevo, y fecha y hora (UTC).
3. Cuenta y perfil de Plataforma que ejecuta (patrón de `CENTER_CREATE` y similares: `0006-perfil-plataforma.md:25-26`).
4. **Referencia documental, no texto libre**: identificador y fecha de la instrucción escrita del centro (contrato, anexo o acta). No guardar nombres de personas del centro en la base; el documento firmado queda en el archivo contractual de ResidApp.
5. **Versión de la ficha del módulo aceptada** (apartado 2) y, como casillas, las condiciones comprobadas: contrato de encargado con el anexo de subencargados actualizado, EIPD del centro actualizada, información a personas afectadas entregada, y las condiciones propias del módulo.
6. En desactivación, **motivo de una lista cerrada** (petición del centro, fin de contrato, impago, incidente de seguridad, retirada del proveedor, otro con aviso). Nada de texto libre: el texto libre acaba conteniendo datos.
7. Quién avisó al centro y por qué canal (sin la dirección).

**¿La activación es un dato personal?** [Opinión] El estado de un módulo en un centro es un dato **del centro** (una persona jurídica, normalmente), no de una persona residente ni del personal. Pero la fila **sí contiene datos personales** de quien la ejecuta (la cuenta de Plataforma) y, si se guardara, de quien firmó por el centro. Consecuencias: va al registro de actividades de ResidApp (art. 30.2, encargada) con acceso restringido, el histórico es de solo añadir, y su plazo de conservación es **el de la prueba contractual y de responsabilidad, no el clínico** (no los 15 años de la historia: `entorno-produccion-lectura-dpo.md:91`). El plazo concreto lo fija una persona experta; sigue sin decidir el de la auditoría (`antes-de-datos-reales.md:129, 131`). Si se guardara un nombre de una persona del centro, se necesitaría base y plazo propios: por eso propongo referencia y no nombre.

## 2. Crítica del catálogo en código y de la «condición de privacidad»

**Lo que está bien.** Un catálogo en código se revisa por pull request, deja historial y obliga a pensar la ficha de un módulo antes de venderlo. Que la dependencia entre módulos sea explícita (A necesita B) evita estados incoherentes.

**Lo que cambiaría.**

- **Ficha por módulo, versionada.** Cada módulo declara en el catálogo: finalidad; datos que trata y de quién; quién accede; terceros (y su región); retención; qué sale en la exportación; qué pasa al apagar; si exige EIPD; condiciones previas; información que debe recibir cada población (residentes y representantes, familias, personal). `modulos_centro` guarda la **versión de la ficha** aceptada. Si la ficha cambia de manera sustancial, el módulo pasa a «pendiente de nueva aceptación» y se avisa al centro. Esto cumple, en lenguaje llano, el aviso de cambios de subencargados y el art. 35.11.
- **Condiciones bloqueantes, no informativas.** [Opinión] Dos clases: las que **comprueba el sistema** (la clave del proveedor existe, el entorno no es de pruebas) y las que **declara una persona** (el centro tiene EIPD y la información entregada). Las segundas se registran como casillas con fecha y referencia; ResidApp no puede verificarlas, pero sí puede negarse a activar sin ellas y dejar constancia (art. 5.2: la responsabilidad proactiva es del responsable, pero la encargada debe poder demostrar que siguió instrucciones, art. 28.3.h).
- **Combinaciones prohibidas, además de dependencias.** Un módulo puede declarar que **no es compatible** con otro (por ejemplo, Medicamentos y el dictado, apartado 7).
- **Apagar en cascada.** Definir qué pasa si se apaga B y A lo necesitaba: o se prohíbe mientras A esté activo, o A pasa también a solo lectura. No dejar que exista el estado «A activo, B apagado».
- **Los módulos del catálogo no se anuncian como disponibles hasta que existen.** Es una condición ya fijada para las landings (`antes-de-datos-reales.md:81`): el catálogo debería ser la fuente única de qué está «disponible» y qué «próximamente».

**Dónde guardar el estado.** [Opinión] **No en una columna de `centros`.** Esa tabla está fuera de la seguridad por filas porque la lee el login (`0008:68`); poner ahí los módulos haría que la sesión de un centro pudiera leer lo contratado por los demás. Mejor `modulos_centro` con `centro_id` bajo la política por filas, con un salto de Plataforma **de solo lectura e inserción** (nunca UPDATE ni DELETE, como el resto de saltos: `0008:63-68`) y su entrada en la matriz de permisos de `0091`. Esto **modifica** la lista de saltos de Plataforma del ADR 0008 y su descripción en `antes-de-datos-reales.md` apartado 9: hay que anotarlo ahí, no solo en el 0015. Y `CoberturaRlsTests` debe cubrir la tabla. (Si el estado «actual» es una fila que cambia, el histórico va aparte y de solo añadir; si el estado se deduce del último evento, no hay UPDATE. Lo segundo encaja mejor con el patrón de tablas inmutables del proyecto: **[Suposición]**, lo decide quien diseñe el esquema.)

**La comprobación única.**

- **Dónde.** En el caso de uso (capa de aplicación), **antes de auditar y antes de llamar a cualquier tercero**, no solo en el controlador. Hoy el dictado lo comprueba el controlador (`AuxiliarController.Dictado.cs:29`); un segundo punto de entrada (otra acción, un trabajo en segundo plano, una exportación) lo saltaría sin querer.
- **Con qué centro.** El centro validado por la base, no el de la cookie (`0008:42`).
- **404 y no 403.** Está bien: no dice que el módulo existe. Para la pantalla, ocultar el botón; no mostrar «este módulo no está contratado» a quien no lo debe ver (publicidad dentro de una herramienta clínica es ruido y puede confundir al personal).
- **La prueba automática que falla si una ruta de módulo no comprueba** es la mejor idea del diseño. Cuando se escriba, que cubra también: acciones con distinto verbo (GET y POST), vistas parciales, PDF, descargas y cualquier tarea programada. Las tareas sin persona conectada (rol `residapp_tareas`, `0008:52-59`) no tienen ámbito: o no pertenecen a un módulo, o necesitan una consulta explícita por centro.
- **Sin caché que retrase un apagado.** Si se guarda en memoria «el centro X tiene el módulo Y», un apagado urgente (apartado 3) debe aplicarse en segundos. [Suposición] Con una consulta por petición sobre una tabla pequeña y con índice, no hace falta caché.
- **La orden de apagado gana a la operación en curso.** Si el módulo se apaga mientras alguien tiene una operación abierta, se rechaza al guardar, no solo al abrir la pantalla.

## 3. Pregunta c): Plataforma gestiona la lista de módulos de cada centro

**¿Amplía lo que Plataforma ve?** Poco: ve **qué módulos tiene cada centro**, que es información comercial y contractual, no de residentes. Lo que sí amplía es lo que **puede hacer**: controlar si los datos de un centro van a un tercero (Mistral). Esa capacidad es más sensible que ver una lista.

- **[Hecho]** Plataforma no ve ni lee la auditoría de otros centros (`0008:63-65`). El diseño debe mantenerlo: el evento de activación se **inserta** en la auditoría del centro afectado, y el centro lo lee en la suya.
- **Lo que no debe llegar a Plataforma por este camino:** uso por centro desglosado por persona (cuántos dictados hace cada auxiliar), contenido, nombres. Si hay contadores de uso (para cobrar o para el dashboard de Plataforma), que sean **por centro y por módulo, sin cuenta**, y que la ficha del módulo y el contrato digan que existen. Un contador por persona sería control del trabajador, no facturación.

**¿Basta con auditoría sin datos?** Para el qué, sí. Para el por qué, no: hace falta la referencia documental y la versión de la ficha (apartado 1). Hay una limitación conocida: con una sola persona operando, no hay «cuatro ojos» (`entorno-produccion-lectura-dpo.md:145`). La compensación es que **el centro ve la activación** y puede protestar.

**¿Debe el centro ver qué módulos tiene y su historial?** **Sí.** Razones: el centro necesita saberlo para su registro de actividades (art. 30.1), para informar a su personal y a las personas residentes (arts. 13 y 14) y para comprobar que ResidApp sigue sus instrucciones (art. 28.3.h). Propuesta [Opinión]:

- Una pantalla de solo lectura para Administración con los módulos, su estado, desde cuándo, y el historial (por la auditoría existente, categoría nueva «Módulos»).
- En el historial, el actor como «Plataforma de ResidApp», sin nombre; el nombre de la cuenta lo conoce quien administre y se da a quien tenga derecho a pedirlo (el DPO, una inspección). Menos datos personales de la persona operadora expuestos a todo el personal del centro.
- Aviso a la Administración principal (al abrir sesión o por la vía acordada) cuando cambia un módulo: «Se ha activado X en tu centro el [fecha]. Si no lo has pedido, avísanos».

**Quién apaga.** [Opinión, contraria al diseño propuesto] La Administración principal del centro debería poder **desactivar** un módulo ella misma (pasa a solo lectura) y que el sistema lo registre. Activar o reactivar sigue siendo cosa de Plataforma. Razón: parar un tratamiento es un derecho del responsable que no debe depender de que una persona de ResidApp conteste. También **Plataforma** debe poder apagar un módulo **para todos los centros** de golpe (retirar la clave del proveedor), como interruptor de emergencia ante un incidente del proveedor, y avisar a los centros «sin dilación indebida» (art. 33.2, leído el 2026-11-08; ver `entorno-produccion-lectura-dpo.md:68`).

**Apagar por impago.** [Opinión] Nunca debe impedir **consultar ni exportar** lo ya registrado. El centro tiene que poder cumplir sus 15 años y atender derechos de las personas con independencia de si pagó. El solo lectura ya lo da; conviene escribirlo en el ADR y en el contrato.

## 4. Entorno de desarrollo: «todos los módulos activos»

Decisión de Orlando, dada. Mis cautelas son de implementación:

- **[Opinión]** El interruptor debe ser un **ajuste de ejecución**, **no filas sembradas** en `modulos_centro`. Las semillas ya han dado problemas de separación (`entorno-produccion-lectura-dpo.md:206-207, 217`: el despliegue aplica semillas siempre; se pide que se nieguen fuera de desarrollo). Si el «todo activo» es una semilla, una copia o un despliegue mal configurado lo lleva a producción como datos reales del libro de activaciones.
- **Falla cerrado, con una señal doble.** Que el interruptor solo valga si el entorno **no** es producción **y** hay una marca de entorno de desarrollo en la base (la que ya propone `entorno-produccion-lectura-dpo.md:217`). Que un entorno desconocido (por ejemplo, sin la variable puesta) se trate como producción.
- En producción, que la aplicación **no arranque** si el ajuste está puesto (como `IaOptions.Validate` impide arrancar con un proveedor no autorizado: `IaOptions.cs:27-36`).
- **Aviso visible en desarrollo** («entorno de pruebas, todos los módulos activos, no introduzca datos reales»). Ya se propone el rótulo y el `noindex` para `dev.` (`entorno-produccion-lectura-dpo.md:215`).
- **El dictado en desarrollo envía audio a Mistral de verdad.** Con «todo activo», cualquiera que pruebe dicta contra un tercero. Regla ya escrita: la clave de Mistral de dev solo con voces de prueba (`entorno-produccion-lectura-dpo.md:215`). Mantenla.
- **La clave global como interruptor maestro** (dictado): bien como segunda llave. Dos matices: (i) sin clave no hay dictado en ningún centro aunque lo hayan contratado: documentarlo como disponibilidad, no como fallo; (ii) con clave pero con el módulo apagado, la aplicación **no debe enviar nada** a Mistral. Prueba: «módulo apagado, clave puesta, ninguna llamada de red» (parecida a la actual `SinLaClaveDelProveedor_ElDictadoEstaDesactivado_Y404`, `dictado-por-voz-dossier-dpo.md:92`).

## 5. Pregunta b): solo lectura al apagar, derechos y conservación

### 5.1 ¿Basta para acceso, portabilidad y 15 años?

- **Conservación (15 años).** [Hecho en lo ya leído] El plazo por defecto de ResidApp es de 15 años desde la baja; el texto del art. 12 de la Llei 21/2000 (en la redacción de la Llei 16/2010) se leyó en el BOE el 2026-11-03 (`antes-de-datos-reales.md:115-117`) y la custodia es del **centro**; ResidApp guarda una copia como encargada (`expurgo-y-supresion.md:40`). Apagar un módulo **no debe borrar nada clínico**: eso es compatible con la conservación. Búsqueda de hoy: la Llei 2/2024 modifica la Llei 21/2000 (en el art. 8, según lo que he podido leer) y no he podido confirmar que toque el art. 12: **[A verificar]** en el texto consolidado del Portal Jurídic. Sigue abierto si una residencia cuenta como «centro sanitario» a efectos de ese artículo.
- **Acceso (art. 15).** El solo lectura permite **consultar**, pero el derecho de la persona (o su representante) es a **una copia**, y se atiende por el centro, no por la pantalla del personal. [Opinión] Basta si el centro puede sacar una copia de los datos del módulo. Eso exige que la exportación incluya los datos del módulo. **Hoy no ocurre con nada clínico** (`0010:38`; `alcance.md:48`). La vía de consulta y exportación de la historia ya figura como pendiente (`expurgo-y-supresion.md:185, 442`, fila 18). El acceso a la historia en Cataluña lo regula el art. 13 de la Llei 21/2000 (texto comprobado el 2026-11-12): el paciente tiene derecho a acceder a la documentación de la historia clínica y «a obtener una copia de los datos que figuran en ella», y corresponde a los centros sanitarios regular el procedimiento; el acceso no puede ir en perjuicio de la confidencialidad de terceros ni de las anotaciones subjetivas de los profesionales (art. 13.2). El art. 15.3 del RGPD confirma la copia «en un formato electrónico de uso común».
- **Portabilidad (art. 20).** [Hecho, BOE 2026-11-09] Es el derecho a recibir los datos «en un formato estructurado, de uso común y lectura mecánica» y a transmitirlos a otro responsable. [Opinión, a decidir por el DPO] En atención sanitaria y social la base suele ser una obligación o una función asistencial y no el consentimiento, y el art. 20 solo juega con consentimiento o contrato; por eso probablemente **no sea el derecho decisivo**. Pero **el fin de contrato sí lo es**: [Hecho] la encargada, «a elección del responsable, suprimirá o devolverá todos los datos personales» al terminar el servicio y suprimirá las copias, salvo obligación legal de conservarlas (art. 28.3.g). Para devolver hace falta una exportación completa. Hoy no existe.

**Condición que propongo para el ADR:** [Opinión] **un módulo no puede guardar datos de personas reales hasta que su ficha defina su salida** (qué fichero, con qué datos, quién lo puede pedir, cómo se audita). Es lo que hace verdadera la frase «consultar y exportar siguen».

### 5.2 Mistral y las transcripciones del dictado

- **[Hecho]** ResidApp no guarda ni el audio ni la transcripción (`0009-nfc-voz-acceso-fisico.md:51`; `0011:37`; `dictado-por-voz-dossier-dpo.md:36-39`). Lo que queda: una fila de auditoría de uso (`VOICE_DICTATION_USED`) y los datos del registro que la auxiliar **confirmó** (que son datos normales, base del producto). Por tanto, **al apagar el dictado no hay datos propios del módulo que poner en solo lectura**: lo registrado ya es historia clínica normal, y las filas de auditoría se conservan con la auditoría.
- **Lo que podría quedar fuera, en el proveedor.** [Hecho] El anexo del DPA de Mistral dice que los datos se conservan «durante el contrato» y la retención cero requiere aprobación suya (`dictado-por-voz-dossier-dpo.md:63-65`). Con una **clave única para todos los centros**, Mistral **no puede distinguir ni borrar por centro**. Consecuencias [Opinión]:
  - La retención cero aprobada por escrito es **condición bloqueante** del módulo, no una mejora. Sin ella, no se activa en ningún centro con datos reales.
  - Si no hay retención cero, apagar el módulo de un centro **no borra nada** en Mistral y la ficha debe decirlo; el borrado total solo llega cuando termine el contrato de ResidApp con Mistral. Eso se explica al centro antes de activar.
  - Si algún día un centro necesitara una clave propia (para separar uso o facturación), eso es un cambio de arquitectura y de contrato, con ADR y revisión.
- **No hace falta borrar ni dejar de conservar nada en ResidApp al apagar.** Lo que sí puede borrarse es cualquier cosa **solo técnica** del módulo (claves, ajustes, plantillas), cuando el centro termine su contrato, no antes.

### 5.3 Dos efectos del solo lectura que conviene escribir

- **Datos clínicos que dejan de actualizarse.** En un módulo clínico (Medicamentos), un listado «en solo lectura» puede estar desfasado y alguien puede fiarse de él. Exactitud (art. 5.1.d, no leído literal): la pantalla debe decir «Módulo desactivado el [fecha]: no se actualiza», y el apagado de un módulo clínico debe ir precedido de un aviso a Dirección clínica y de una copia imprimible del estado vigente.
- **Módulos que no tienen datos propios** (el dashboard solo calcula): no hay «solo lectura», simplemente desaparece la pantalla. Anótalo en la ficha para no inventar un estado que no tiene sentido.

## 6. Cautelas de agregación del dashboard de Dirección (pregunta d)

Solo orientación; se diseña aparte. Primero, un hecho incómodo: **[Hecho]** los indicadores de Dirección que ya son base cuentan por unidad y mes sin ninguna regla visible de cifras pequeñas (`src/ResidApp.Application/Ports/ISupervisionDirectory.cs:162-199`; solo he leído esa parte, **[A verificar]** el resto). Ya se había avisado en `historial-tras-traslado-06-nota-retroactiva-0075-0076.md:32`. El problema existe hoy, con o sin módulo; el módulo lo agranda porque añade más gráficos y más desgloses.

1. **Frontera con la base.** Definir exactamente qué ve Dirección en la base (los indicadores de hoy) y qué añade el módulo. Si el módulo solo «pone bonito» lo que ya había, el centro sin módulo no pierde nada de privacidad; si añade cruces nuevos, se evalúan como finalidad nueva.
2. **Cifra mínima.** Suprimir o agrupar toda celda por debajo de un mínimo (N). **No fijo N**: lo decide el DPO con CJ según el tamaño de las unidades (en una residencia de unos cien residentes, una unidad de 20 puede identificar a una persona con una sola úlcera). Ojo: con un mínimo no basta suprimir una celda si el total y las demás permiten **restarla**; hay que suprimir también una complementaria o agrupar. [Opinión] Referencias a leer: el dictamen del grupo del art. 29 sobre técnicas de anonimización y las orientaciones de la AEPD sobre anonimización: **[A verificar]** en fuente oficial. Y el considerando 26 (leído en el BOE hoy): lo anónimo es lo que no permite identificar con los medios razonables; un agregado por residente no lo es (ya lo apliqué en `portal-familiar-dos-idiomas.md:52-54`).
3. **Combinaciones de filtros limitadas.** Unidad × mes × tipo de evento × turno puede reducir a una persona. Limitar cuántos ejes se cruzan y no ofrecer rangos de fechas de un día.
4. **Sin salto a la persona.** Un gráfico no enlaza a «ver los casos». Si Dirección necesita mirar un caso, usa las pantallas de siempre, con sus permisos y su auditoría. Un enlace desde el gráfico sería una puerta trasera sin la trazabilidad de la pantalla individual.
5. **Sin desglose por trabajador.** Ni por cuenta, ni por turno con una sola persona. Lo fija el alcance: «rankings nominativos de productividad» fuera (`alcance.md:47`) y las decisiones de `0079` («sin ranking por cuenta»: `antes-de-datos-reales.md:63`). Si se desglosa por turno o planta, mismo mínimo.
6. **Exportación de los gráficos.** Una imagen o un PDF sale del control de acceso. Reglas [Opinión]: el mínimo se aplica **en el servidor, antes de dibujar**, nunca en el navegador; el fichero no lleva datos de detalle debajo; se audita como `EXPORT_RUN` (patrón de `0010:76`); lleva marca de «datos agregados, no reidentificar»; con identidad de marca (PDF). Y no se envía por correo desde la aplicación.
7. **No guardar copias derivadas.** Calcular en la consulta. Si se guardan instantáneas para ver tendencias, son un almacén nuevo con su plazo, su borrado (¿qué pasa si una persona residente ejerce la supresión?) y sus mínimos.
8. **Quién lo ve y registro de consulta.** Dirección con permiso explícito; que la consulta deje constancia (patrón de `0079`).
9. **Base y finalidad.** [Opinión] Gestión de los servicios asistenciales y de calidad: art. 9.2.h (texto completo comprobado el 2026-11-12: «gestión de los sistemas y servicios de asistencia sanitaria y social, sobre la base del Derecho de la Unión o de los Estados miembros o en virtud de un contrato con un profesional sanitario», con el art. 9.3: lo trata un profesional sujeto a secreto u otra persona sujeta también a secreto). El DPO fija la base y el centro informa en su cláusula.
10. **Grupos y varios centros.** El ADR 0008 ya dice que un administrador de grupo ve «solo estructura y agregados» y que queda por definir qué cifras y con qué mínimo (`0008:85-97`). El dashboard de Dirección **es de un centro**. Si algún día hay una vista de grupo, será un módulo distinto con su propia ficha.
11. **El dashboard de Plataforma** (fuera de este ADR) no debe ser un atajo para saber cifras asistenciales de un centro. [Opinión] Que mida el funcionamiento técnico (errores, tiempos, uso agregado por módulo) y no desgloses clínicos. Cuando se diseñe, pasa por esta lectura.

## 7. Pregunta e): Medicamentos, qué dejar anotado desde ya

**Anota esto en el ADR 0015 como bloqueo explícito, no como idea futura:** «Medicamentos no se activa en ningún centro con datos reales mientras no se cumpla todo lo siguiente».

1. **Reabrir el alcance.** Hoy la administración de medicación está fuera de alcance (`alcance.md:45`), la política de la aplicación dice que ResidApp no guarda medicación (`Privacy.cshtml:54`) y el ADR 0011 lo repite (`0011:75-77`). Solo CJ puede reabrirlo (`0011:66`: «decisión de producto: solo CJ puede reabrirlo»). Al reabrirlo, cambian el alcance, la política de privacidad y la información a las personas residentes.
2. **Aclarar qué es el módulo.** Tres cosas muy distintas: (a) **registro de lo que toma** la persona (lista); (b) **registro de cada administración**; (c) **apoyo a la decisión** (alertas de interacciones, dosis, cálculos). La (c) puede ser **producto sanitario** (software) con requisitos regulatorios propios: el Reglamento (UE) 2017/745 y la guía MDCG 2019-11 clasifican el software que informa decisiones terapéuticas, normalmente en clase IIa o superior (resumen de búsqueda de hoy, fuentes secundarias: **[A verificar]** con el texto oficial y un especialista). El ADR 0011 ya avisó de que un sistema que detecta riesgos clínicos «podría además ser producto sanitario» y de IA de alto riesgo (`0011:72-74`). Regla de partida [Opinión]: la primera versión **no aconseja ni alerta**; si se quiere (c), va antes a consulta regulatoria.
3. **Relación con las indicaciones de Medicina.** Una pauta de medicación es una **orden clínica**. Hay que decidir quién la crea (Medicina), quién registra la administración (Enfermería, quizá Auxiliar) y cuál es la **única fuente de verdad**: ResidApp no es la historia clínica oficial (`alcance.md:38`) y, si hay receta electrónica y sistemas públicos de salud, tener dos listas distintas es un riesgo de seguridad del paciente. La indicación de Medicina que ya existe no debe duplicarse ni contradecirse. **[Decidir]** CJ y Dirección clínica.
4. **Sensibilidad por inferencia.** Una lista de fármacos revela diagnósticos (psiquiátricos, oncológicos, infecciosos) aunque no haya diagnósticos escritos. Acceso por necesidad, no abierto a todo el que ve la ficha; lectura auditada (patrón de `0077`/`0079`); y **no visible en el Portal Familiar** salvo decisión expresa: hoy ninguna pantalla del portal muestra medicación (`docs/producto/guia-por-perfil-recorrido.md:68`, FAM-08).
5. **EIPD específica antes de activar.** Obligatoria en la práctica (categorías especiales a gran escala, art. 35.3.b), y con una parte que **no es de privacidad sino de seguridad del paciente**: dosis equivocada por dato desfasado (apartado 5.3), por error de transcripción o por confusión de residente.
6. **Sin dictado en los campos de medicación.** El ADR 0011 aprueba el dictado solo para «Registrar cambio» (`0011:38`). Que el catálogo marque Medicamentos como **incompatible con el dictado** hasta un ADR nuevo con revisión clínica: una dosis dictada y mal entendida es un riesgo directo.
7. **Corrección y versiones.** Las correcciones deben conservarse (el patrón de «versiones» y «correcciones» inmutables ya existe: `antes-de-datos-reales.md:151`). Aquí es más importante: qué se pautó y qué se corrigió es un dato clínico y de responsabilidad.
8. **Retención.** Documentación clínica: seguir los 15 años desde la baja salvo que una persona experta diga otra cosa por tipo de documento (`antes-de-datos-reales.md:115-117`).
9. **Terceros.** Si se pensara en bases de fármacos, farmacia o receta electrónica, cada una es un **destinatario o subencargado nuevo** y requiere ADR (como los proveedores de IA: `0011:58-62`).
10. **Apagado controlado.** Ver 5.3.
11. **Validación clínica y pruebas** antes de la primera persona real: la que CJ determine, con el agente de calidad.

## 8. Pregunta f): la exportación pasa a base

**Sin objeción, y es un acierto.** Un centro tiene que poder sacar sus datos siempre, con independencia de lo contratado: es lo que hace posible el art. 28.3.g, la portabilidad cuando proceda y los 15 años. Condicionar eso al pago sería dar una palanca contra la persona residente. Lo que señalo:

1. **«Exportación» hoy es solo una cosa:** estructura y censo de activos con identidad administrativa (`0010:72`). **La exportación que necesita el derecho de acceso y el fin de contrato (historia clínica, por módulo) no existe** y el alcance la deja fuera (`alcance.md:48`). Al declarar la exportación «base», el ADR 0015 debe separar: (i) la de censo (hecha) y (ii) la de datos asistenciales y de cada módulo (pendiente, con condiciones). Si no se separa, «consultar y exportar siguen» induce a pensar que está resuelto.
2. **Al ser base, el riesgo está en todos los centros.** La exportación genera un fichero con datos de personas que sale del control de la aplicación (CSV abierto en Excel, con nombre, fecha de nacimiento y sexo). Hoy solo la hace Administración, se audita antes de entregar y no se guarda el fichero (`0010:76, 80-88`). Mantén: permiso propio configurable en `permisos_perfil`; auditoría previa; sin correo ni enlaces; y, cuando incluya datos de salud, otro formato de entrega (cifrado o con caducidad) **[Decidir]**.
3. **Los límites de hoy no sirven para una devolución completa** (5 MB, 2.000 filas, 60 columnas: `0010:92`). Hace falta otro camino para el fin de contrato.
4. **Plataforma no debe poder exportar datos de un centro.** Hoy no puede (no ve residentes: `0006-perfil-plataforma.md:14-16`). Dilo en el ADR para que nadie lo «arregle» al hacer la exportación de fin de contrato.
5. **Solo lectura y exportación.** Cuando un módulo se apaga, el permiso de exportar sus datos y su auditoría no cambian.

## 9. Pregunta g): otras cautelas

1. **Base no es «sin condiciones».** El correo (Azure Communication Services) es un subencargado y es «base», pero hoy se enciende por entorno, no por centro (`0012:81`); la política ya dice «si el centro los tiene activados» (`Privacy.cshtml:156`). Mismo asunto que el dictado: o se construye el interruptor por centro para lo base que implica un tercero (patrón de `citas_configuracion_centro`), o se corrige el texto. [Opinión] Lo segundo es lo más barato hasta que haya un centro real; lo primero, antes de enviar un aviso real.
2. **Facturar por censo.** Para cobrar por número de residentes, ResidApp necesita saber **cuántos hay**. Si lo calcula contando residentes en la base, usa los datos que trata como encargada para un fin propio (facturar). [Opinión] Es un recuento sin identificar a nadie, pero conviene que el contrato lo prevea como instrucción del centro, o que el censo lo declare el centro. **[Decidir]** DPO. Lo mismo para contadores de uso de módulos (apartado 3).
3. **La política de privacidad debe ser coherente con los módulos.** Describe todos los módulos, indicando «solo si tu centro lo activa», y el bloque «Datos de tu centro» (D1 de `politica-privacidad-portal.md`) muestra cuáles están activos. El catálogo puede alimentar ese bloque. Mientras tanto, corregir las frases del dictado y el correo (ver «Estado actual»).
4. **Información antes, no después.** Activar el dictado exige que el personal sepa que su voz va a un tercero (`dictado-por-voz-dossier-dpo.md:116`), y las personas residentes y sus representantes que se emplea esa tecnología. El centro lo declara como casilla antes de activar.
5. **Registro de actividades (art. 30).** [Hecho, BOE 2026-11-09, art. 30.2 primera frase] La encargada lleva un registro de las categorías de tratamientos por cuenta de cada responsable. Cada módulo es una línea nueva, con sus subencargados. El anexo del contrato de encargado debe listar a Mistral **como condicional** («solo con el módulo Dictado»).
6. **Privacidad por defecto.** [Art. 25.2 RGPD, literal comprobado el 2026-11-12: «por defecto, solo sean objeto de tratamiento los datos personales que sean necesarios» y no sean accesibles «sin la intervención de la persona, a un número indeterminado de personas físicas»] Activar un módulo **no debe conceder** permisos a ningún perfil: la Administración del centro los concede después en `permisos_perfil`. Lo contrario es activar de golpe el acceso de todo un perfil a datos nuevos.
7. **Usuarios con ámbito en varios centros.** El estado del módulo es del **centro del ámbito activo**. Una persona con dos centros puede tener el módulo en uno y no en otro; la pantalla no debe mostrar nada del centro que no está activo.
8. **Repositorio público.** El catálogo con las condiciones de privacidad de módulos futuros es un mapa de hoja de ruta. Riesgo bajo; no escribir en él detalles de seguridad (ver `entorno-produccion-lectura-dpo.md`, apartado 6).
9. **Brechas.** Un incidente de un tercero de un módulo (Mistral) obliga a la encargada a avisar al centro «sin dilación indebida» (art. 33.2). Para eso hay que saber **a qué centros afecta**: es lo que permite avisar a los correctos. Buen argumento a favor de la tabla.
10. **Reactivar.** Reactivar un módulo apagado no debe reabrir sin más: si pasó tiempo o cambió la ficha, vuelve a pedir las casillas.

## Lo que debe validar o decidir una persona (DPO real o abogado)

Por orden de importancia:

1. **[Decidir]** Que la activación de un módulo exige una instrucción documentada del centro y qué documento es (cláusula del contrato, anexo por módulo, acta). Y el contenido del anexo de subencargados condicional.
2. **[Decidir]** Si la Administración principal del centro puede apagar un módulo por sí sola (mi propuesta) y el plazo en el que Plataforma debe apagar si el centro lo pide.
3. **[Decidir]** Retención cero con Mistral como condición bloqueante del dictado; y qué se le dice al centro si Mistral la rechaza.
4. **[Decidir]** Plazo de conservación del histórico de activaciones y de los contadores de uso; relación con el plazo de la auditoría (sin decidir: `antes-de-datos-reales.md:131`).
5. **[Decidir]** Base jurídica de cada módulo (arts. 6 y 9) y compatibilidad del dashboard de Dirección con la finalidad original (art. 6.4); cifra mínima de agregación.
6. **[Decidir]** Cómo calcula ResidApp el censo para facturar sin tratar los datos del centro para un fin propio (art. 28.10).
7. **[Decidir]** Medicamentos: que no se active sin reabrir alcance (CJ), EIPD específica, consulta regulatoria sobre producto sanitario y decisión sobre fuente única de verdad con los sistemas oficiales.
8. **[Decidir]** Formato y garantías de la exportación de datos asistenciales y de fin de contrato.
9. **[Decidir]** Si hay que corregir ya el texto de la política de privacidad (frases del dictado y del correo) o construir primero el interruptor por centro.
10. **[Decidir]** Que apagar por impago sea solo solo lectura y nunca pérdida de acceso a datos clínicos (cláusula del contrato).

## A verificar (fuente oficial)

- Texto literal del resto del art. 28.2 RGPD (aviso de cambios y oposición), del art. 25.2, del art. 15.3, del art. 9.2.h completo y del art. 5.1.d.
- Lista de la AEPD del art. 35.4 (numeración y supuestos): `aepd.es`.
- Orientaciones de la AEPD y dictamen del grupo del art. 29 sobre anonimización y agregación.
- Llei 21/2000 vigente: art. 12 (conservación), art. 13 (acceso) y si la Llei 2/2024 tocó algo de ellos. Portal Jurídic de Catalunya.
- Si una residencia cuenta como centro sanitario a efectos del art. 12 de la Llei 21/2000 (ya abierto: `antes-de-datos-reales.md:132`).
- Qué autoridad de control corresponde (AEPD o APDCAT) al centro piloto. No he podido confirmar el ámbito de la APDCAT en la búsqueda de hoy; hay que mirar el artículo de ámbito de la Llei 32/2010 y el tipo de titularidad del centro.
- Reglamento (UE) 2017/745 (regla 11 del anexo VIII) y MDCG 2019-11 Rev.1; si el Reglamento de IA añade obligaciones si algún día hay IA en un módulo de medicamentos.
- LOPDGDD: artículos de derechos digitales en el ámbito laboral aplicables al dictado y a los desgloses por trabajador (la numeración, **[A verificar]**).

## Fuentes consultadas el 2026-11-09

Repositorio: archivos y líneas citados. Externas (herramienta que devuelve resúmenes automáticos, salvo que se indique):

- BOE, Reglamento (UE) 2016/679: `https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807` (lectura parcial del texto: arts. 5.2, 13.3, 20, 25.1, 28.2 primera frase, 28.3 a, g y h, 28.10, 30.2 primera frase, 35, 36.1; considerando 26; arts. 6.4 y 9.2.h solo en parte).
- AEPD, lista de tratamientos que requieren EIPD (art. 35.4): `https://www.aepd.es/documento/listas-dpia-es-35-4.pdf` (no se pudo leer el PDF; resumen de búsqueda).
- ICAB, «Llei 2/2024, de 6 de febrer, de modificació de la Llei 21/2000»: `https://www.icab.es/ca/actualitat/noticies/noticia/LLEI-2-2024-del-6-de-febrer-de-modificacio-de-la-Llei-21-2000-sobre-els-drets-dinformacio-concernent-la-salut-i-lautonomia-del-pacient-i-la-documentacio-clinica.` (solo el título; contenido por resumen de búsqueda).
- Resumen de búsqueda sobre el Reglamento (UE) 2017/745, regla 11 y MDCG 2019-11 (fuentes secundarias de consultoras; **no usar como cita**).

**Yo no soy DPO designado ni abogado.** Todo lo anterior lo valida una persona antes de usarlo.
