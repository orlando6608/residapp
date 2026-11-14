# Protocolo de brechas de seguridad de ResidApp

**Escrito para Orlando (persona de contacto de privacidad de ResidApp) y CJ (suplente): para usarlo mientras ocurre un incidente, sin ser jurista.**

**BORRADOR del agente `dpo-residapp`, 2026-11-13. Lo valida una persona experta (abogado o DPO) antes de usarlo con datos reales.** No es un dictamen ni sustituye a un delegado de protección de datos. Hoy no hay sociedad titular ni persona experta contratada ([`guia-cumplimiento-propietarios.md`](guia-cumplimiento-propietarios.md), apartado 9). Este documento desarrolla el apartado 6.3 de esa guía y las decisiones de Orlando del 2026-11-13 (ver abajo).

Las citas legales salen de `normativa-local/` (índice del 2026-11-12): RGPD consolidado a 2016-05-04; LOPDGDD, BOE act. 2026-10-08; Llei 32/2010, BOE act. 2026-07-13; ENS (RD 311/2022), BOE act. 2026-10-30; guía de brechas de la AEPD (PDF de junio de 2021, «guía AEPD»; las páginas son las del PDF); directrices 9/2022 del CEPD sobre notificación de brechas (v2.0, en inglés, «directrices 9/2022»; citadas por apartado) y 01/2021 del CEPD con ejemplos (v2.0, en inglés). Las páginas web de la APDCAT sobre brechas (actualizadas el 6 y el 12 de junio de 2025, consultadas el 14-11-2026) no están en la base, que solo admite PDF: se citan como «web APDCAT». Son textos consolidados: sirven para consultar, no son la versión oficial.

**Decisiones ya tomadas que este protocolo aplica (Orlando, 2026-11-13):** ResidApp avisa al centro **en un máximo de 24 horas desde que tenga constancia**; la persona de contacto de privacidad de ResidApp es **Orlando, con CJ de suplente** (CJ aún no lo ha aceptado por escrito). **Además, en esta revisión (Orlando, 2026-11-13):** las 24 horas cuentan **seguidas**, incluidas noches, fines de semana y festivos; **no se cargan datos reales hasta tener una cobertura definida** para noches y vacaciones (opción E del apartado 5.2); y el suplente **solo avisa al centro, anota y registra**, sin contener (la contención técnica es de Orlando).

---

## 0. Si estás en un incidente ahora

1. **Anota la hora** de ahora y la de la primera señal. Es lo primero.
2. **No borres nada** y no restaures ninguna copia sin leer el paso 3.
3. **Corta lo que siga filtrando** (paso 3).
4. **Avisa al centro afectado en menos de 24 horas** desde la hora anotada, **aunque no sepas todo** (paso 5 y plantilla del apartado 4). No esperes a entender el riesgo: valorarlo es cosa del centro.
5. **Avisa a CJ** (suplente) y, cuando exista, a la persona experta (paso 7).
6. **Rellena el registro de incidentes** mientras trabajas, no al final (apartado 4.2).

Palabras que se usan:

- **Brecha de seguridad:** un incidente en el que se pierden, se destruyen, se alteran, se muestran o se pueden ver datos personales sin permiso, por accidente o a propósito. El RGPD la define como «toda violación de la seguridad que ocasione la destrucción, pérdida o alteración accidental o ilícita de datos personales transmitidos, conservados o tratados de otra forma, o la comunicación o acceso no autorizados a dichos datos» (RGPD art. 4.12).
- **Constancia:** el momento en que se sabe que el incidente ha afectado a datos personales. De ahí corren los plazos (guía AEPD, p. 18 y 40).
- **Responsable / encargada:** el responsable decide para qué se usan los datos; la encargada los trata por su cuenta. En los datos de las residencias, **la residencia es la responsable y ResidApp la encargada**.
- **Autoridad de control:** el organismo que vigila la protección de datos: la AEPD o, en el ámbito catalán, la APDCAT.
- **Subencargado:** un proveedor de ResidApp que toca los datos (Azure, el envío de correo, Mistral para el dictado).

---

## 1. Qué es una brecha en ResidApp y qué no

### 1.1 Ejemplos reales de ResidApp

| Qué pasa | ¿Brecha? | Qué se ha perdido | Notas |
|---|---|---|---|
| Un correo de aviso de cita llega a alguien que no es de la familia | **Sí, pequeña.** Se documenta aunque no se notifique | Confidencialidad | El aviso de hoy es genérico, sin datos clínicos (`direccion-confirmada-familia.md`, línea 83 y apartado 6, punto 5). Si algún día el correo llevara más contenido, sube de nivel |
| Alguien usa la cuenta de un profesional (contraseña o móvil robados) | **Sí si entró en datos**; incidente hasta comprobarlo | Confidencialidad, y a veces integridad | Se comprueba en la auditoría `eventos_auditoria` y en `eventos_acceso` |
| Una copia de seguridad o una exportación se pierde o queda expuesta | **Sí** | Confidencialidad | Una exportación mezcla todos los centros: afecta a todos a la vez (`entorno-produccion-lectura-dpo.md`, apartado 3.1) |
| La auditoría muestra que un profesional abrió fichas de residentes sin motivo | **Sí** (abuso de privilegios) | Confidencialidad | Lo suele ver el centro; si lo ve ResidApp, avisa. La guía AEPD lo lista como suceso (p. 31) |
| Se filtra una clave (la de Mistral, la cadena de conexión, las claves de sesión) | **Incidente de seguridad; brecha si no puedes descartar que alguien accediera a datos** | Depende | Las claves de sesión permiten fabricar sesiones (`entorno-produccion-lectura-dpo.md`, línea 76). Cambia la clave en cualquier caso |
| Un fallo hace que un centro vea datos de otro | **Sí, grave** | Confidencialidad | Hay **un aviso por cada centro afectado**: una brecha en un encargado puede afectar de forma distinta a cada responsable (guía AEPD, p. 21) |
| La plataforma cae o se pierden datos y el personal no puede ver las fichas | **Posible brecha de disponibilidad** | Disponibilidad | La guía AEPD incluye los datos «inaccesibles de forma temporal o permanente para quien legítimamente debe poder tratarlos» (p. 32). **No hay umbral de duración en la ley**: se anota siempre y se avisa si impidió ver o usar datos clínicos (ver apartado 8) |
| Se publica en el repositorio (público) un dato de personas o una clave | **Sí si hay datos personales** (publicación no autorizada); incidente si solo es una clave | Confidencialidad | Lo que fue público se trata como conocido: borrarlo del historial no basta (`entorno-produccion-lectura-dpo.md`, apartado 6, línea 175) |
| Un proveedor (Microsoft, correo, Mistral) te avisa de un incidente suyo | **Sí, para ti**: es tu constancia | Depende | Tu aviso al centro corre desde ese momento |
| Cambios por error: un borrado, una restauración de copia que deshace supresiones | **Sí** (integridad o pérdida) | Integridad | Ver el paso 3 sobre restaurar |

### 1.2 Qué NO es una brecha (pero se anota igual)

Un incidente que **no toca datos personales** no es brecha: un error 500 sin datos, un despliegue que falla, un intento de ataque que no llega a materializarse, un correo con virus que no se abrió (guía AEPD, p. 8). Se gestiona como incidente de seguridad, **se anota** en el registro y se deja escrito cómo se comprobó que no había datos personales. La guía AEPD dice que hay que determinar «si han llegado a afectar a datos personales» (p. 8). **Ante la duda, trátalo como brecha** hasta comprobarlo.

---

## 2. Dos casos distintos: de quién son los datos

| | **Caso A: datos que ResidApp trata POR ENCARGO de una residencia** | **Caso B: datos que ResidApp trata COMO RESPONSABLE** |
|---|---|---|
| Qué datos | Residentes, familias y profesionales **de un centro**; lo que hay en su base; los registros técnicos con IP del personal de un centro | Cuentas del personal de Plataforma y sus aparatos autorizados; contactos que lleguen por las landings y por vuestros buzones; datos de quienes trabajan en el proyecto en GitHub; registros de seguridad con IP de operadores (`guia-cumplimiento-propietarios.md`, apartado 2.3) |
| Quién es el responsable | **La residencia** | **ResidApp** |
| Qué hace ResidApp | **Avisa al centro sin dilación indebida y en un máximo de 24 h** desde su constancia (RGPD art. 33.2; plazo propio decidido; la guía AEPD pide que no supere las 72 h, p. 19) | **Notifica ella misma a la autoridad** sin dilación y, de ser posible, en un máximo de 72 h desde su constancia, salvo que sea improbable un riesgo para las personas (RGPD art. 33.1) |
| Quién notifica a la autoridad | **El centro.** ResidApp solo lo hace si el contrato lo prevé por escrito (guía AEPD, p. 18 y 21) | ResidApp |
| Quién avisa a las personas afectadas (alto riesgo) | **El centro**, «sin dilación indebida», en lenguaje claro (RGPD art. 34.1 y 34.2). ResidApp solo si el contrato se lo encarga (guía AEPD, p. 27) | ResidApp |
| Quién documenta | El centro **y** ResidApp (su propio registro) | ResidApp (RGPD art. 33.5) |
| Autoridad | La del **centro**: ver 2.1 | Ver 2.2 |

**Regla práctica:** si los datos los cargó o los usa un centro, es el **caso A**. Si son datos de ResidApp como empresa (vuestros contactos, vuestro personal), es el **caso B**. Si una brecha toca los dos tipos, se aplican **los dos** procedimientos.

**ResidApp no se pone en contacto por su cuenta con residentes ni familias para contarles una brecha del caso A.** La LOPDGDD (art. 33.2) dice que quien «en su propio nombre y sin que conste que actúa por cuenta de otro, establezca relaciones con los afectados» pasa a ser responsable. Habla el centro.

**Sanciones que conviene tener presentes** (LOPDGDD): no avisar el encargado al responsable de una brecha es infracción grave (art. 73.q); no documentar una brecha es infracción leve (art. 74.n, que remite al art. 33.5 del RGPD).

### 2.1 Autoridad en el caso A (la del centro)

- **AEPD** con carácter general (centro privado con su único establecimiento en España; guía AEPD, p. 19).
- **APDCAT** si el centro entra en el ámbito del art. 3 de la Llei 32/2010: instituciones públicas, Administración de la Generalitat, entes locales y entidades vinculadas o dependientes (letras a a d); entidades privadas controladas por esos entes por capital, ingresos o dirección (letra e); y **«las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios»** (letra f). La guía AEPD dice que, en el sector público de Cataluña, se notifica a la APDCAT (p. 20).
- **Cómo se notifica a la APDCAT** (web APDCAT, trámite «Notificació de violacions de la seguretat de les dades personals»): sin tasa; las entidades del sector público integradas en EACAT lo hacen por EACAT y las demás por la sede electrónica de la APDCAT; hay un trámite aparte para la notificación complementaria. La APDCAT entiende que hay constancia cuando existe **certeza** de que se ha producido y un conocimiento suficiente de su naturaleza y alcance; la mera sospecha no obliga a notificar, aunque, si la brecha puede tener un gran impacto, **recomienda contactarla en cuanto haya indicios** (web APDCAT). Es una redacción algo más exigente que la del CEPD («cierto grado de certeza», directrices 9/2022, apartado 31): ante la duda, la más temprana.
- **Lo que se sabe hoy:** el tipo de centro del piloto **no se conoce** (público, concertado o privado; `guia-cumplimiento-propietarios.md`, apartado 1, punto 6). Una residencia concertada podría entrar por la letra f: **lo decide la persona experta** con los datos del centro. Cada centro lo fija en su ficha de contactos (apartado 5.3).
- **Si el centro es público**, además, el ENS (RD 311/2022): las entidades del sector público notifican al CCN los incidentes «que tengan un impacto significativo» (art. 33.2) y tienen que disponer de procedimientos de gestión de incidentes (art. 25.1; el art. 25.2 añade mecanismos de detección, criterios de clasificación, análisis y resolución, cauces de comunicación y un registro de actuaciones). **Para ResidApp como proveedora** (leídos los arts. 2.3, 13.5 y 33.7 del RD 311/2022): el ENS le alcanza solo si presta servicios o soluciones al sector público «para el ejercicio por estas de sus competencias y potestades administrativas» (art. 2.3: **lo decide la persona experta**). Si le alcanza: debe designar un **punto de contacto de seguridad** (POC) que canalice las comunicaciones de seguridad y la gestión de incidentes del servicio (art. 13.5), y debe notificar los incidentes que le afecten al **INCIBE-CERT** (art. 33.7), que lo pone en conocimiento del CCN-CERT. El art. 33.7 no fija plazo ni forma: remite, sin perjuicio, a los arts. 9 a 11 del RD 43/2021 (plataforma de notificación de ciberincidentes), que **no están en la base**.

### 2.2 Autoridad en el caso B (ResidApp)

- **AEPD**, salvo que la persona experta diga otra cosa. ResidApp es una empresa privada y no consta que entre en ninguna letra del art. 3 de la Llei 32/2010 (guía AEPD, p. 19 y 20). **A verificar** cuando exista la sociedad y su domicilio.
- **Cómo se notifica:** por el formulario de la sede electrónica de la AEPD, con **certificado electrónico reconocido o Cl@ve permanente** (guía AEPD, p. 23). Quien vaya a notificar debe **tenerlo preparado antes** de que haga falta. Se puede hacer una notificación **inicial** con lo que se sepa y completarla después con una «modificación» (guía AEPD, p. 19 y 29). **Hoy la sociedad no existe:** quién notifica y con qué certificado es una decisión abierta (apartado 8).

---

## 3. Los pasos, en orden

### 3.0 Cuadro de tiempos

| Hito | Quién | Cuándo | De dónde sale |
|---|---|---|---|
| **Constancia (hora cero)** | Quien lo detecte | Se anota **en el momento** | RGPD art. 33.1; guía AEPD, p. 40 |
| Contener | Orlando (o CJ) | **De inmediato**, sin esperar a entenderlo todo | Sentido común y art. 32 |
| Valoración provisional | Orlando y CJ | Lo antes posible, **sin retrasar el aviso** | La guía AEPD (nota 14, p. 18) recoge que no hace falta que el encargado evalúe el riesgo antes de avisar |
| **Aviso al centro (caso A)** | Orlando o CJ | **Máximo 24 h desde la hora cero** | Decisión de Orlando 2026-11-13; RGPD art. 33.2 |
| Aviso complementario al centro | Orlando o CJ | «Sin dilación indebida» en cuanto haya datos nuevos | RGPD art. 33.4 |
| Aviso a la persona experta | Orlando o CJ | `[plazo: decide Orlando con la persona experta]` | Apartado 3.7 |
| **Notificación a la autoridad (caso B)** | ResidApp | **Sin dilación y, de ser posible, 72 h** desde la hora cero | RGPD art. 33.1 |
| Notificación a la autoridad (caso A) | **El centro** | Sin dilación y, de ser posible, 72 h desde que **el centro** tenga constancia | RGPD art. 33.1. Las directrices 9/2022 (apartado 44) dicen que, **en principio, el centro tiene constancia cuando el encargado se lo comunica**; la persona experta lo confirma (apartado 8) |
| Aviso a las personas afectadas (alto riesgo) | **El centro** (caso A) o ResidApp (caso B) | «Sin dilación indebida» | RGPD art. 34.1 |
| Registro del incidente | ResidApp | **Desde el minuto uno**; se cierra al final | RGPD art. 33.5 |

**Las horas corren sin parar:** noches, fines de semana y festivos cuentan (guía AEPD, p. 17 y 18, sobre las 72 h). Para las 24 h de ResidApp, lo mismo: **corren seguidas** (decidido por Orlando el 2026-11-13; la redacción final del contrato la revisa la persona experta). Que el plazo acabe de noche, en fin de semana o en vacaciones **no lo suspende**: la guía AEPD lo reconoce como motivo de retraso que, aun así, no exime de responsabilidad (p. 40, nota 35).

### 3.1 Detectar y anotar la hora

1. Anota la **hora y la zona horaria** de la **primera señal** (alerta de Azure, aviso de un centro, queja de una familia, aviso de un proveedor, algo que has visto tú).
2. Anota la **hora cero (constancia)**: la hora en que tú o CJ sabéis, o sospecháis con fundamento, que **han podido verse afectados datos personales**. **Propuesta [Decidir con la persona experta]:** si dudas, usa la hora **más temprana**. Una alerta de métricas, por sí sola, no dice si hay datos personales: la hora cero es la de cuando alguien la lee y entiende que puede haberlos.
3. **Si te avisa una familia o un profesional directamente:** anótalo, no le des detalles de otros centros ni de otras personas, y dile que lo comunicas al centro.
4. **Si te avisa el centro:** el centro tiene su propio reloj. Ayúdale (RGPD art. 28.3.f) y conserva los registros que le sirvan de prueba.
5. Abre el registro de incidentes (apartado 4.2) y avisa a CJ.

### 3.2 Pregunta de arranque: ¿caso A, caso B o los dos?

Usa la tabla del apartado 2. Para el caso A, apunta **qué centros** (la tabla de centros y módulos ayuda: `anexo-modulos-contrato-borrador.md`, cláusula 7.4).

### 3.3 Contener

**Qué se puede apagar ya** (solo lo que exista; lo que no, queda como `[ ]` hasta construirse):

| Qué | Cómo | Ojo |
|---|---|---|
| Una cuenta de personal comprometida | **Suspender** la cuenta desde su ficha (Administración del centro). Si trabaja en varios centros, lo hace Plataforma (`Manual.cshtml`, líneas 636 a 639) | Suspender no borra el historial de lo que hizo: déjalo así |
| Una autorización de familia | **Suspender** o revocar (`Manual.cshtml`, línea 513) | Revocar es definitivo |
| El aviso de cita por correo | Apagar `Correo__Activo` en los ajustes de la Web App (`0012-envio-correos.md`, línea 81) | Comprueba que el ajuste ha quedado aplicado |
| El dictado por voz | Quitar `IA:Mistral:ApiKey`: sin ella el dictado queda desactivado (`0011-arquitectura-ia.md`, línea 6) | Anota la hora y si hubo audios enviados |
| Un módulo para todos los centros | «Parada de emergencia» de Plataforma (`anexo-modulos-contrato-borrador.md`, línea 296) | **Propuesta: depende del ADR 0015, sin construir** |
| Una clave o secreto filtrado | Crear uno nuevo y anular el antiguo `[procedimiento por clave: a escribir]` | Anota qué clave y a qué daba acceso, **no su valor** |
| Las sesiones abiertas | Cerrar todas las sesiones (`entorno-produccion-lectura-dpo.md`, línea 109) | **A verificar** cómo se hace hoy: no consta una pantalla |
| Algo publicado en el repositorio | Retirarlo **y** cambiar las claves afectadas | Ya pudo copiarse |

**Qué NO hacer:**

- **No borres** registros, correos, cuentas ni datos «para limpiar»: son la prueba y lo que exige el art. 33.5.
- **No restaures una copia sin pensar.** Restaurar **deshace** expurgos, supresiones y revocaciones y **acorta la auditoría inmutable** (`entorno-produccion-lectura-dpo.md`, apartado 3.2, líneas 100 a 109). Si hay que restaurar: siempre en una base **nueva**, comprobar, reaplicar el registro externo de supresiones y revocaciones, cerrar sesiones, avisar al centro y anotar.
- **No investigues leyendo historias clínicas.** Cuenta y comprueba por recuentos y claves, no por contenido.
- **No copies datos de las personas afectadas** al registro, a mensajes, a capturas ni a este repositorio. Guarda **lo que prueba qué pasó** (horas, números, nombres de recursos), no los datos (la guía AEPD lo pide así, p. 44).
- **No lo comentes por chat o por correo con nombres de afectados.**

### 3.4 Valorar

Respóndete esto y apúntalo. **Esto no decide si avisas al centro:** el aviso del caso A sale **siempre** (art. 33.2 no lo condiciona al riesgo). La valoración sirve para priorizar y para **ayudar al centro**, que es quien decide si hay riesgo y si hay alto riesgo.

1. **¿Qué se ha perdido?** Confidencialidad (alguien lo vio), disponibilidad (no se puede acceder) o integridad (se cambió) (guía AEPD, p. 32).
2. **¿Qué datos?** ¿Datos de salud, de contacto, claves o credenciales? ¿Estaban cifrados y la clave sigue segura? Si es así, reduce mucho el riesgo (guía AEPD, p. 32).
3. **¿De qué centros?** Un aviso por cada uno (guía AEPD, p. 21).
4. **¿Cuántas personas, aproximadamente?** Por colectivo: residentes, familias, profesionales. Si no lo sabes, di el total del tratamiento afectado (guía AEPD, p. 36).
5. **¿A propósito o por accidente?** ¿Origen en ResidApp, un subencargado, el centro o un tercero? (guía AEPD, p. 29 y 30).
6. **¿Sigue ocurriendo?** ¿Se ha recuperado ya?

**Orientación (la decide el centro, no tú).** Los factores de la guía AEPD son: tipo de brecha, carácter sensible y volumen, facilidad de identificar a las personas, gravedad de las consecuencias, características del responsable y número de afectados (p. 17). En datos de salud, la guía recoge que «se considerará probable» que haya daños (p. 17, nota 12). Estas personas son, además, mayores y a menudo con capacidad disminuida; la guía cita como agravante el colectivo especialmente vulnerable (p. 36) **[Opinión: aplica a residentes; lo confirma la persona experta]**.

| Pista | Lectura provisional |
|---|---|
| Datos de salud vistos, sacados o publicados fuera del centro; credenciales; varios centros | **Probable riesgo alto**: avisa pronto y di que lo ves así |
| Datos de salud vistos por un profesional del propio centro sin motivo | **Riesgo**, a valorar por el centro |
| Un correo genérico sin datos clínicos a una persona ajena | **Riesgo bajo**; se documenta |
| Datos cifrados, sin acceso de nadie, recuperado con copia sin pérdida | **Riesgo bajo o improbable**; se documenta, y se avisa igual al centro si afecta a su responsabilidad |

La guía AEPD indica que se comunica a los afectados cuando la severidad es alta o muy alta, o media con probabilidad alta de que el daño se materialice (p. 38 y 39). La decide el centro.

### 3.5 Avisar al centro (caso A)

1. **A quién:** a la **persona de contacto del centro y a su suplente**, a la vez (apartado 5.3). Si no responden, prueba por el segundo canal y llama.
2. **Cuándo:** **antes de 24 h desde la hora cero.** Si todavía no lo sabes todo, envía un **aviso inicial** y completa después: el RGPD lo permite expresamente («la información se facilitará de manera gradual sin dilación indebida», art. 33.4).
3. **Qué decir:** el **contenido mínimo del art. 33.3** (el centro lo necesita para notificar): a) naturaleza, categorías y número aproximado de personas y de registros afectados; b) tu contacto; c) posibles consecuencias; d) medidas adoptadas o propuestas. Usa la plantilla del apartado 4.1.
4. **Qué NO decir:** ningún nombre ni dato de afectados, ni en el asunto ni en el cuerpo ni en adjuntos.
5. **Pide acuse de recibo** y anótalo. Sin acuse, insiste por el otro canal.
6. **Después:** ofrece ayuda técnica y mantén al centro al día. Un aviso por cada centro afectado.
7. **No notifiques a la autoridad por el centro** salvo que el contrato lo prevea por escrito (apartado 8).

### 3.6 Documentar (siempre)

Rellena el registro del apartado 4.2 **durante** el incidente. Incluye hechos, efectos y medidas correctivas (RGPD art. 33.5). **No pongas datos de las personas afectadas** en él (guía AEPD, p. 44). Se documenta **toda** brecha, también las que no se notifican y también las pequeñas.

### 3.7 Avisar a la persona experta

**Cuándo (propuesta):** en cuanto hayas avisado al centro, y **antes** si ocurre cualquiera de estos casos: afecta a datos de salud; afecta a más de un centro; es del caso B; dudas si es brecha o no; llega algo de una autoridad (en la guía, apartado 6.2: avisar a la persona experta y al centro **el mismo día**); un medio de comunicación lo publica.

**Hoy no hay persona experta contratada.** Hasta que la haya, es una laguna: decidid Orlando y CJ juntos y dejad escrito por qué (apartado 8).

### 3.8 Cerrar y aprender

Cierra el incidente cuando: está contenido; el centro ha recibido el aviso y ha dado acuse; se han tomado las medidas; y el registro está completo. Después:

1. ¿Cuál fue la **causa**? ¿Cómo se detectó? ¿Qué falló o faltó?
2. ¿Qué cambio evita que se repita? (arreglo de código, hallazgo en `docs/seguridad/hallazgos.md`, ADR, alerta nueva).
3. ¿Hay que actualizar la **EIPD**, el **registro de actividades**, el contrato o este protocolo?
4. ¿Hay que avisar a un subencargado o a un proveedor?
5. Anota las **lecciones** con responsable y fecha.

---

## 4. Plantillas

### 4.1 Aviso al centro (caso A)

> **Para** `[CONTACTO DEL CENTRO]` **y** `[SUPLENTE DEL CENTRO]`. **De** `[CONTACTO DE PRIVACIDAD DE RESIDAPP]`. **Copia** `[SUPLENTE DE RESIDAPP]`.
>
> **Asunto:** `[ResidApp] Aviso de incidente de seguridad que puede afectar a datos de [RESIDENCIA] (ref. [INC-AAAA-NN])`
>
> Te escribo como encargada del tratamiento de `[RESIDENCIA]`, conforme al art. 33.2 del RGPD y a la cláusula `[ ]` del contrato de encargado. Este aviso es **`[inicial / complementario]`**.
>
> **1. Qué ha pasado.** `[Descripción breve y factual, sin nombres ni datos de personas]`.
> **2. Cuándo.** Inicio `[fecha y hora, o «no se sabe»]`. Constancia de ResidApp: `[fecha y hora y zona horaria]`. Estado: `[sigue ocurriendo / contenido / recuperado]`.
> **3. Qué datos y a cuántas personas** (aproximado): categorías `[ ]` (¿datos de salud? `[sí / no / no se sabe]`); colectivos `[residentes / familias / profesionales]`; número aproximado de personas `[ ]`; de registros `[ ]`. ¿Datos cifrados? `[ ]`.
> **4. Posibles consecuencias.** `[ ]`.
> **5. Medidas ya tomadas y propuestas.** `[ ]`.
> **6. Lo que todavía no sabemos** y cuándo volveremos a escribirte: `[ ]`.
> **7. Lo que necesitamos de vosotros:** confirmar la recepción respondiendo a este correo; decirnos quién del centro lleva el asunto; indicarnos si queréis nuestra ayuda técnica.
> **8. Contacto de ResidApp** para ampliar información: `[CONTACTO DE PRIVACIDAD DE RESIDAPP: nombre, correo y teléfono]`.
>
> Este aviso **no contiene datos de las personas afectadas**. La notificación a la autoridad de control y, en su caso, a las personas afectadas corresponde a `[RESIDENCIA]` como responsable del tratamiento.

Envíalo a las dos personas del centro **por el canal acordado** y apunta la hora de envío y la del acuse en el registro. Cuando llegue algo nuevo, manda un aviso **complementario** con el mismo número de referencia.

### 4.2 Registro de incidentes

Guárdalo **fuera del repositorio** (es público) y con acceso solo a Orlando y CJ. `[DÓNDE Y CUÁNTO TIEMPO: decidir]`. **Sin datos de las personas afectadas.** Una ficha por incidente.

| Campo | Contenido |
|---|---|
| Referencia | `INC-AAAA-NN` |
| Hora de la primera señal | Fecha, hora y zona horaria |
| **Hora de constancia (hora cero)** | Fecha, hora y zona horaria |
| Quién lo detectó y cómo | Alerta de Azure / centro / familia / proveedor / auditoría / tercero / otro |
| Caso | A (por encargo) / B (como responsable) / ambos |
| Qué pasó | Hechos, sin datos de personas |
| Cuándo empezó y estado | Fecha o «no se sabe»; sigue / contenido / recuperado |
| Dimensión | Confidencialidad / disponibilidad / integridad |
| Origen e intención | ResidApp / subencargado / centro / tercero; accidental o intencionado |
| Datos afectados | Categorías; si hay datos de salud; si estaban cifrados |
| Centros afectados | Lista (sin datos de contacto de personas) |
| Número aproximado | Residentes `[ ]`, familias `[ ]`, profesionales `[ ]`; registros `[ ]` |
| Riesgo | Valoración provisional de ResidApp; **decisión del centro**, con fecha |
| Medidas de contención | Qué, quién y a qué hora |
| A quién se avisó y cuándo | Centro (cada uno): hora de envío, rol del destinatario, hora del acuse. Persona experta: cuándo. Autoridad: quién notificó, cuándo, nº de registro. Personas afectadas: lo decide el centro, cuándo y cómo |
| Estado | Abierto / cerrado |
| Causa y lecciones | Causa; cambio propuesto; responsable y fecha |
| Fecha de cierre | |

---

## 5. Quién hace qué cuando no estás tú

### 5.1 Reparto

| Persona | Papel |
|---|---|
| **Orlando** | Titular: contacto de privacidad de ResidApp; recibe las alertas; contiene y avisa |
| **CJ** | Suplente (**falta su aceptación por escrito**; un mensaje basta; la pregunta 2.1 de su formulario de la guía la recoge). Si Orlando no está: anota la hora cero, avisa al centro con la plantilla, abre el registro, avisa a Orlando y a la persona experta. **No contiene** (decidido por Orlando el 2026-11-13: la contención técnica es de Orlando); si hay que cortar algo técnico y Orlando no está, se avisa al centro de que sigue abierto |
| **Persona experta** | Pendiente de contratar: valida este protocolo y asesora ante cada incidente |
| **Contacto del centro y suplente** | El centro recibe el aviso y decide sobre la autoridad y las personas afectadas |
| **Subencargados** (Azure, correo, Mistral) | Deben avisar a ResidApp de sus incidentes: **a pedir en sus condiciones** `[A verificar]` |

**Una limitación a resolver.** Hoy lo que consta es que el suplente **aún no tiene ni acceso de lectura a los registros** (`hallazgos.md`, línea 331: el rol de lectura del almacén solo lo tiene Orlando). El suplente puede **avisar**; no puede **contener**. **Decidido por Orlando el 2026-11-13:** se queda así (solo avisa y registra). Consecuencia: si Orlando no está localizable, una brecha en curso no se puede cortar; por eso importa la cobertura del apartado 5.2.

**Si Orlando es parte del incidente** (su cuenta o su equipo): lo gestiona CJ y queda anotado en el registro.

### 5.2 Noches y vacaciones: decisión abierta

**Hoy no hay cobertura definida.** Una sola persona opera ResidApp (`entorno-produccion-lectura-dpo.md`, línea 72; `guia-cumplimiento-propietarios.md`, apartado 9). Que el plazo acabe fuera de horario **no te exime** (guía AEPD, p. 40, nota 35). **Decidido por Orlando el 2026-11-13: opción E, no se cargan datos reales hasta tener una cobertura definida** (hoy no hay centros reales, así que no hay brecha del caso A). Las opciones A a D se deciden con la persona experta antes del primer centro real:

| Opción | Qué supone | Límite |
|---|---|---|
| **A. Horario de mejor esfuerzo, dicho por escrito al centro** | Se escribe en el contrato que ResidApp revisa alertas en `[horario]` | El centro puede no aceptarlo; el plazo legal del centro no se mueve |
| **B. Alertas que despiertan** (teléfono o llamada, no solo correo) para titular y suplente, con compromiso de respuesta | Hoy las alertas son solo por correo | Cuesta tiempo de las dos personas y no cubre vacaciones de ambos |
| **C. Vacaciones escalonadas** | Nunca se ausentan Orlando y CJ a la vez | Exige que el suplente pueda contener, no solo avisar |
| **D. Un servicio o persona externa de guardia, por contrato** | Cobertura real | Coste; acceso a datos que hay que justificar (confidencialidad, art. 28.3.b) |
| **E. No cargar datos reales hasta tener una cobertura** | Es la opción más segura | Retrasa el piloto |

### 5.3 Contactos (rellenar fuera del repositorio)

El repositorio es público: **aquí solo van los huecos.** La lista real vive en `[LUGAR PRIVADO]`, con acceso a Orlando y CJ, y se comprueba cada `[ ]`.

**Por centro** (una fila por centro):

| Centro | Contacto de privacidad del centro | Suplente | Canal 1 | Canal 2 (teléfono) | Última comprobación | Autoridad del centro |
|---|---|---|---|---|---|---|
| `[RESIDENCIA]` | `[ ]` | `[ ]` | `[ ]` | `[ ]` | `[fecha]` | `[AEPD / APDCAT: según 2.1]` |

**De ResidApp:**

| Quién | Dato |
|---|---|
| Contacto de privacidad (titular) | `[ ]` |
| Suplente | `[ ]` |
| Persona experta | `[ ]` |
| Incidentes de Microsoft (Azure) | `[ ]` |
| Incidentes del proveedor de correo | `[ ]` |
| Incidentes de Mistral | `[ ]` |

---

## 6. Lo que ya existe en el sistema que ayuda a detectar, y lo que falta

### 6.1 Existe

| Qué | Dónde | Qué cubre |
|---|---|---|
| **Registro técnico de fallos `ResidApp.Fallos`** | `TechnicalFailureLog.cs`, líneas 34 a 48; conectado en `Program.cs`, línea 430 | Un **acceso denegado** (aviso): código, método de origen, identificador de sesión y centro declarado. Un **fallo inesperado** (error): tipo de excepción, número y estado de SQL, código y método. **Nunca** escribe cuenta, residente, nombre ni el mensaje de la excepción salvo que sea un código en mayúsculas (líneas 14 a 17). No registra conflictos ni datos no válidos (líneas 12 y 13) |
| **Auditoría funcional** `eventos_auditoria` | `hallazgos.md`, línea 319 | Inmutable; cubre acciones clínicas y lecturas sensibles. Sirve para saber quién miró a quién |
| **Registro de accesos** `eventos_acceso` | `registros-tecnicos-lectura-dpo.md`, tabla (a) | Inicios de sesión, con IP |
| **En Azure dev** (2026-11-12, no comprobable desde el código) | `hallazgos.md`, línea 331 | Salida de la aplicación guardada 90 días con candado de borrado; **tres alertas por métricas** (errores 5xx, respuestas 403 y conexiones fallidas a SQL) hacia un grupo de acciones con dos receptores (titular y suplente, correos verificados); auditoría de SQL de inicios de sesión fallidos, 90 días |

### 6.2 Falta

- **Producción:** nada de lo anterior existe en producción, porque no hay entorno de producción (ADR 0014, aplazado).
- **Las alertas por métricas no ven todo:** muchas denegaciones salen como respuesta 200 o redirección y **solo constan en el registro, sin alerta** (`hallazgos.md`, línea 331, límite 1).
- **No se guardan los registros de peticiones HTTP** (decisión por la ruta con identificadores y el token de invitación): para investigar quién pidió qué desde qué IP **no hay rastro** hoy (`registros-tecnicos-lectura-dpo.md`, punto 1).
- **Las alertas llegan por correo:** no despiertan a nadie de noche (apartado 5.2).
- **El suplente no tiene acceso de lectura** a los registros.
- **Alertas propuestas y no hechas:** cambios de roles, de administrador de SQL, reglas de firewall, uso de la cuenta de emergencia, consulta de secretos (`entorno-produccion-lectura-dpo.md`, línea 144, propuesta).
- **Nada detecta un correo mal dirigido** salvo la queja de la familia y, cuando exista, el rebote (`direccion-confirmada-familia.md`, línea 172).
- **Falta una alerta sobre accesos anómalos en la auditoría** (un profesional abriendo muchas fichas fuera de su unidad). No consta que exista.
- **Falta** el contrato de encargado con el plazo de 24 h, la lista de contactos real, la aceptación escrita de CJ y la persona experta.

---

## 7. Simulacro

**Cuándo:** antes de cargar datos reales (`entorno-produccion-lectura-dpo.md`, apartado 11: «Protocolo de brechas con plazo de aviso a cada centro y contacto de guardia; simulacro hecho»). **Cada cuánto después: `[Decidir]`.** El simulacro de **restauración de copias** es otro, con su propia acta (`entorno-produccion-lectura-dpo.md`, apartado 3.2).

**Cómo (ejercicio de mesa, con datos inventados, sin tocar producción):**

1. Orlando y CJ eligen un escenario ficticio de la tabla 1.1 (por ejemplo: «un correo de aviso llegó a una persona ajena» o «se publicó una clave»). Si falta alguien, el ejercicio no vale.
2. Uno hace de quien detecta; el otro, de suplente. Se **cronometra** desde la hora cero.
3. Se ejecuta el protocolo: anotar la hora cero, contener (**simulando**: no se apaga nada real), valorar, redactar el aviso con la plantilla, rellenar el registro.
4. Se comprueba que: **la alerta llega a los dos** (alerta de prueba del grupo de acciones); el suplente **encuentra** este documento y la lista de contactos sin ayuda; el aviso sale **bien dentro de las 24 h**; los canales de los centros funcionan. **Solo con un centro que lo haya aceptado:** una llamada de prueba diciendo «esto es un simulacro».
5. Se redacta el acta.

**Acta (sin datos de personas), una ficha:**

| Campo | Contenido |
|---|---|
| Fecha | |
| Participantes | Orlando, CJ y, si hubo, otros (por rol) |
| Escenario | Título, sin datos |
| Horas | Hora cero; aviso listo; aviso enviado |
| Qué salió bien | |
| Qué falló o faltó | |
| Cambios acordados | Qué, quién y cuándo |
| Dónde se guarda | `[LUGAR PRIVADO]` |

---

## 8. A verificar o decidir

**Para la persona experta (jurídico):**

1. ~~Desde cuándo «tiene constancia» el centro~~ **Resuelto en lo que dicen las fuentes; la persona experta lo confirma para el contrato.** El RGPD (art. 33.1) no lo define. Las directrices 9/2022 del CEPD (que actualizan las WP250 rev.01 y las sustituyen como referencia) dicen que el responsable tiene constancia cuando tiene «un grado razonable de certeza» de que un incidente ha comprometido datos personales (apartado 31); que el encargado no tiene que evaluar el riesgo antes de avisar (apartado 44); que, «en principio», el responsable tiene constancia **cuando el encargado se lo ha comunicado** (apartado 44); y que el encargado debe avisar «sin dilación indebida», con información por fases, aunque el RGPD no fije un plazo (apartado 45). Por tanto, el reloj de 72 h del centro cuenta desde el aviso de ResidApp, **pero** el aviso de 24 h de ResidApp corre desde su propia hora cero y por eso importa cumplirlo. Quedan dos matices: la APDCAT exige «certeza» y conocimiento de naturaleza y alcance (web APDCAT), y el apartado 40 avisa de que, si el responsable no actúa a tiempo ante una alerta y luego resulta que la brecha existió, puede contar como incumplimiento. Las directrices 01/2021 traen casos prácticos (el caso 3 es un ransomware en un hospital) que **no se han leído enteros**; solo consta, del caso 9 (apartado 79), que el aviso inmediato del encargado al responsable cuenta como factor que reduce el riesgo, lo que respalda el aviso rápido de ResidApp. Una segunda pasada puede aprovecharlos para el simulacro (apartado 7).
2. **Definición de «constancia» de ResidApp** para el plazo de 24 h (apartado 3.1): ¿cuándo se considera conocida? ¿Cuenta una alerta de métricas? Mi propuesta es la hora más temprana ante la duda.
3. **Delegación para notificar a la autoridad en nombre del centro:** si ResidApp la ofrecerá en el contrato; la guía AEPD exige que conste documentada y recuerda que «la delegación de funciones no implica delegación de responsabilidad» (p. 11 y 21). La misma regla para avisar a las personas afectadas (p. 27).
4. **Plazo para avisar a las personas afectadas:** el RGPD dice «sin dilación indebida» (art. 34.1) y la guía AEPD no fija días (p. 27). Hay que decidir si el contrato fija un plazo al centro o solo uno a ResidApp para entregarle el texto.
5. **Autoridad según el tipo de centro** (apartado 2.1), sobre todo para una residencia concertada (art. 3.f de la Llei 32/2010). Y la autoridad en el caso B cuando exista la sociedad.
6. **Brecha de disponibilidad:** a partir de qué duración o impacto una caída se avisa al centro como brecha. No hay umbral en la ley.
7. **Si el ENS alcanza a ResidApp** (art. 2.3 del RD 311/2022: ¿presta «soluciones» a entidades públicas «para el ejercicio de sus competencias»?, apartado 2.1). Si sí, quedan por concretar el punto de contacto de seguridad (art. 13.5), la notificación al INCIBE-CERT (art. 33.7; plazo y forma en el RD 43/2021, que no está en la base) y si es exigible algo de las guías del CCN-CERT (no están en la base). Los arts. 13, 25 y 33 ya están leídos enteros.
8. **APDCAT:** no tiene una guía propia en PDF sobre brechas; publica páginas web (trámite, preguntas frecuentes y «Notificar les violacions de seguretat», con enlace a las WP250) que se han leído (web APDCAT, junio de 2025) y están resumidas en el apartado 2.1. Si el centro es de su ámbito, hay que usar su formulario vigente, que cambia con la sede electrónica.
9. **Redacción final de la cláusula** del contrato de encargado: plazo de 24 h, horas corridas o hábiles, canal y contactos (`anexo-modulos-contrato-borrador.md`, cláusula 7.4, `[PLAZO MÁXIMO DE AVISO]`).
10. **Dónde se guarda el registro de incidentes y cuánto tiempo.**

**Para Orlando (decisión de producto o de organización):**

1. ~~Cobertura de noches y vacaciones~~ **Decidido (2026-11-13): opción E**, sin datos reales hasta tener cobertura; elegir A a D antes del primer centro (apartado 5.2).
2. **Aceptación escrita de CJ** como suplente, y que conste en el aviso legal.
3. ~~Qué puede hacer el suplente~~ **Decidido (2026-11-13): solo avisa y registra.** Queda por decidir si lee los registros (hoy solo los lee Orlando).
4. **Canal de aviso al centro** (correo y teléfono) y que no dependa solo de la propia plataforma.
5. **Quién notifica y con qué certificado electrónico** en el caso B mientras no exista la sociedad.
6. **Cuándo avisar a la persona experta** y qué hacer hasta que haya una.
7. **Frecuencia del simulacro.**

**A verificar (hecho no confirmado):** cómo se cierran todas las sesiones hoy; procedimiento para cambiar cada clave; que los subencargados se comprometen a avisar a ResidApp de sus incidentes (y en cuánto tiempo); quién puede leer la auditoría en cada centro.

**Fuentes:** `normativa-local/`: RGPD arts. 4.12, 33 y 34; LOPDGDD arts. 33.2, 73.q, 73.r, 74.n y disposición adicional 3.ª; Llei 32/2010 art. 3; RGPD art. 36; ENS arts. 2, 13, 25, 33 y 34 (leídos enteros el 2026-11-14); `aepd-guia-brechas.txt` (leída entera); `edpb-directrices-09-2022-brechas.txt` (apartados 31 a 36, 40, 44 y 45; **leída solo en esas partes**) y `edpb-directrices-01-2021-brechas-ejemplos.txt` (**no leída**: solo se ha buscado en ella; los casos prácticos están sin revisar); páginas web de la APDCAT (no están en la base). **Lo que sigue sin estar en la base:** las WP250 originales (las sustituyen las directrices 9/2022, que las actualizan), el RD 43/2021 y las guías del CCN-CERT. El art. 33 de la LOPDGDD trata del encargado; la LOPDGDD no tiene un artículo propio sobre cómo notificar brechas (solo las infracciones).
