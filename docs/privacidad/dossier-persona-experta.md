# ResidApp: dossier de primer contacto para despachos y delegados de protección de datos

**Escrito para:** un despacho de abogados (Derecho Digital y sector sanitario) o un delegado de protección de datos externo que no conoce ResidApp y debe decidir si acepta un encargo y con qué presupuesto. No es un documento comercial: es la información mínima para que pueda valorarlo.

Fecha: 13 de noviembre de 2026. Versión: primer contacto.

---

## 1. Qué es ResidApp y en qué momento está

- **Qué es.** Una aplicación web (instalable en móvil) para residencias de personas mayores. Sirve para el registro asistencial diario (constantes, observaciones, citas, derivaciones a urgencias, historial de cada residente) y para la comunicación con las familias a través de un portal propio. Lo usan auxiliares, enfermería, medicina, dirección y administración del centro; los familiares acceden al portal.
- **Qué datos trata.** Datos de salud de los residentes (categoría especial del artículo 9 del RGPD), y datos identificativos y de contacto de familiares y de profesionales. Algunas funciones opcionales (dictado por voz con un proveedor de IA, avisos de cita por correo a familias) están **apagadas por defecto** y no se activarían sin decisión previa.
- **Reparto de papeles (decidido).** **La residencia es la responsable del tratamiento y ResidApp es la encargada** (artículo 28 del RGPD). ResidApp no decide fines ni medios, y no usa los datos de los residentes para fines propios. Por su cuenta, ResidApp solo trataría datos de contacto comerciales, cuentas de su propio personal técnico y registros de seguridad de la plataforma.
- **Subencargados previstos.** Microsoft Azure (alojamiento y base de datos; Azure Communication Services para el correo) y, solo si el centro activa el dictado, Mistral AI (transcripción y apoyo a la redacción). Hoy el entorno está en una región de España de Azure; para producción se prevé una región de la UE/EEE, **a confirmar**.
- **Momento actual.** Preproducción. **No hay ningún centro real ni ningún dato real:** todo se ha probado con datos inventados. **La sociedad titular de ResidApp todavía no está constituida**; el equipo promotor ha decidido no firmar con ningún centro real hasta que exista. Se prevé un piloto con una residencia de Cataluña cuyo tipo (pública, concertada o privada) **aún no está determinado**.
- **Equipo.** Dos personas promotoras, sin formación jurídica; una con experiencia clínica en el sector y otra técnica. Una sola persona opera la plataforma. **No existe hoy delegado de protección de datos ni asesor jurídico de ResidApp.**

## 2. Qué se le pide, y en qué orden

Existen más de veinte **borradores** (política de privacidad, notas de registro de actividades y de evaluación de impacto, plazos de conservación, un anexo de módulos para el contrato, lecturas por tema sobre producción, dispositivos, correo y dictado). Los ha redactado un asistente de IA con acceso al texto de las normas; **ninguna persona con independencia los ha revisado**. Parten de una guía de cumplimiento interna que ordena lo pendiente y que podemos facilitarle.

| Orden | Qué se le pide | Cuándo |
|---|---|---|
| 0 | **Recomendar la estructura:** un solo despacho que asuma abogado y DPO, o dos figuras separadas. Es decisión pendiente y es nuestra primera pregunta. | Primera respuesta |
| 1 | **Validar** los borradores existentes y señalar qué corrige o descarta. | Antes de cualquier dato real, aunque sea un piloto |
| 2 | **Preparar las plantillas** del contrato de encargado del tratamiento (con su anexo de subencargados) y del contrato de servicio (condiciones mercantiles del SaaS), más el aviso legal. | Antes de las primeras ventas |
| 3 | **Decidir los puntos abiertos** (apartado 3) y fijar, con nosotros, el registro de actividades, la evaluación de impacto, el protocolo de brechas y el procedimiento de derechos. | Antes del primer centro real |
| 4 | **Actuar como delegado de protección de datos de ResidApp y contacto ante la autoridad**, si usted considera que procede designarlo. | Si procede |

**Lo que no hay todavía** y habría que crear o completar: contrato de encargado, contrato de servicio, aviso legal, protocolo de brechas, procedimiento único de derechos, política de privacidad para profesionales, hoja informativa breve de ingreso por tipo de persona (residente o representante, familiar, profesional), registro de actividades y evaluación de impacto completos (hoy solo hay piezas) y, solo si aplica, documentación del Esquema Nacional de Seguridad.

**Decisiones internas ya tomadas** (revisables por usted): no se firma con un centro real hasta que exista la sociedad; ResidApp ofrecerá a los centros una plantilla informativa de ingreso (que cada centro adapta y entrega); aviso de ResidApp al centro ante una brecha en un máximo propuesto de 24 horas desde que tenga constancia. **Propuesta que necesita su confirmación (no es una decisión tomada):** conservación por defecto de la historia clínica de 15 años desde la baja de la persona.

## 3. Las ocho preguntas que bloquean el uso de datos reales

Citamos de textos consolidados (no oficiales) consultados el 12 de noviembre de 2026. Las leyes catalanas las hemos leído en su versión castellana del BOE. Siglas: **RGPD** (Reglamento (UE) 2016/679), **LOPDGDD** (Ley Orgánica 3/2018), **AEPD** (Agencia Española de Protección de Datos), **APDCAT** (Autoridad Catalana de Protección de Datos), **EIPD** (evaluación de impacto), **DPO** (delegado de protección de datos), **ENS** (Esquema Nacional de Seguridad, Real Decreto 311/2022).

1. **¿Es una residencia «centro sanitario»?** La Llei 21/2000 (art. 12.1) atribuye la custodia de la historia clínica a «la dirección de los centros sanitarios» y fija quince años desde el alta de cada proceso para ciertos documentos (art. 12.4); la Ley 41/2002 (art. 3) define centro sanitario como el conjunto organizado que presta servicios «para cuidar la salud»; y la LOPDGDD (art. 34.1.l) obliga a designar DPO a «los centros sanitarios legalmente obligados al mantenimiento de las historias clínicas». De la respuesta dependen el plazo de conservación, si la residencia debe tener DPO y quién decide el expurgo («dirección médica»). Ha de tenerse en cuenta también la normativa catalana de servicios sociales (Llei 12/2007).
2. **¿AEPD o APDCAT?** El art. 3 de la Llei 32/2010 abarca, entre otros, a las instituciones públicas, a las entidades privadas con capital, ingresos o dirección mayoritariamente públicos (letra e) y a «las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta» respecto de los tratamientos vinculados a ese servicio (letra f). Necesitamos que nos indique cómo encaja un centro público, concertado o privado, y cuál sería la autoridad de ResidApp como encargada y como responsable de sus propios tratamientos.
3. **¿Aplica el ENS?** El RD 311/2022 se aplica a todo el sector público (art. 2.1) y a entidades privadas que, por relación contractual, presten servicios o provean soluciones al sector público «para el ejercicio por estas de sus competencias y potestades administrativas» (art. 2.3). ¿Entra un centro concertado por esa vía? ¿Y ResidApp como proveedora? Hoy una sola persona opera el sistema, lo que dificulta la separación de responsables que prevé el art. 13.3.
4. **¿Necesita ResidApp un DPO propio?** El art. 37.1.c del RGPD alcanza también al encargado cuando su actividad principal consista en tratamiento a gran escala de categorías especiales; si no, es voluntario (LOPDGDD, art. 34.2). Si se designa, ¿quién puede serlo sin conflicto de intereses (art. 38.6) y cuándo se comunica a la autoridad (LOPDGDD, art. 34.3, diez días)?
5. **¿Persona física o sociedad como titular?** Con qué figura debería firmarse un piloto mientras la sociedad no exista, con qué responsabilidad personal para los promotores y qué habría que cambiar después (cesión de contratos). Condiciona el aviso legal (LSSI, art. 10.1: nombre o denominación social, domicilio, correo electrónico y datos registrales) y todos los contratos.
6. **Bloqueo o borrado al suprimir datos de un familiar.** El art. 32 de la LOPDGDD obliga a «bloquear los datos» al rectificar o suprimir, y su apartado 4 admite un «copiado seguro» con evidencia digital si el sistema no permite el bloqueo. Hoy la función de anonimizar a un familiar **borra sin conservar copia bloqueada**. No se usará con familias reales hasta que usted conteste por escrito si basta con borrar o hay que bloquear.
7. **Anexo de módulos como instrucción documentada.** El centro activa módulos opcionales (con efecto en precio y en tratamientos). ¿Sirve el anexo contractual como «instrucción documentada» del art. 28.3.a del RGPD? ¿Cómo se concilia la baja comercial de un módulo con el derecho del centro a que cese el tratamiento?
8. **Base jurídica y EIPD.** Bases de los arts. 6 y 9 del RGPD para los tratamientos del centro y para los accesorios (portal de familias, cuentas y dispositivos del personal, registros de acceso); si la EIPD (art. 35, y art. 35.3.b para datos de categorías especiales a gran escala) debe ser una por centro, una general o una por módulo, y si procede consulta previa a la autoridad (art. 36).

**Cuestiones para fases posteriores** (sin urgencia para el primer contacto): contenido del contrato de encargado y retorno de la historia clínica al terminar (hoy no existe exportación completa, art. 28.3.g del RGPD); dictado por voz con proveedor de IA (retención cero, entrenamiento, región, posible dato biométrico); plazos de conservación de registros de acceso, sesiones y auditoría; cookies y almacenamiento en el dispositivo (LSSI, art. 22.2, exención de lo «estrictamente necesario»); aspectos laborales (necesitarán un laboralista); ejercicio de derechos por residentes con capacidad disminuida (Ley 8/2021); idioma (castellano y catalán); posible calificación como producto sanitario de futuros módulos (Reglamento (UE) 2017/745, regla 11) y obligaciones del Reglamento de IA; responsabilidad, seguro y condiciones de un contrato B2B.

## 4. Qué presupuesto e información le pedimos

Le agradeceríamos que incluyera:

1. **Estructura recomendada** (un despacho o dos figuras) y, en su caso, quién sería nuestro interlocutor.
2. **Alcance y precio por fases:** (a) validación de borradores y respuesta a las ocho preguntas; (b) plantillas de contrato de encargado, contrato de servicio y aviso legal; (c) registro de actividades, EIPD, protocolo de brechas y procedimiento de derechos; (d) DPO externo, si procede (cuota y alcance, y qué incluye ser contacto ante la autoridad). Indique qué es tarifa cerrada y qué es por horas.
3. **Plazos** estimados por fase y su disponibilidad actual. No fijamos fecha de piloto; la búsqueda se hace con antelación porque sabemos que el proceso lleva semanas.
4. **Experiencia:** en sector sanitario o sociosanitario, en programas como servicio (SaaS) con proveedores tecnológicos, **en Cataluña y ante la APDCAT**, y en el ENS. Referencias que pueda compartir sin romper su confidencialidad.
5. **Conflicto de intereses:** si actuara a la vez como abogado de ResidApp y como su DPO, cómo garantizaría la independencia que exige el art. 38.3 y 38.6 del RGPD; y si presta servicios a residencias u otros clientes que pudieran ser clientes de ResidApp.
6. **Forma de trabajo y facturación:** canal, revisiones, entregables, modo de facturar y condiciones de continuidad.

## 5. Qué no se comparte todavía

- **Ningún dato personal real** (de residentes, familiares o profesionales) ni acceso a un entorno de producción: no existe producción.
- **Sí podemos enseñarle**, bajo la confidencialidad que usted proponga: la documentación y los borradores, la guía interna de cumplimiento y una demostración en directo con datos inventados.

## 6. Contacto

Persona de contacto: **[NOMBRE]**
Correo: **[CORREO]**
Teléfono: **[TELÉFONO]**
Respuesta o presupuesto, por favor, hasta el: **[FECHA]**

*Este dossier no contiene datos personales. Los borradores citados son internos, no están validados y no deben entenderse como asesoramiento jurídico.*
