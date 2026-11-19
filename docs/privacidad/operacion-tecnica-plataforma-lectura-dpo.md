# «Dashboard de Plataforma: Operación técnica»: lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-19. Segunda lectura experta del agente `dpo-residapp`, no un dictamen ni un sustituto de un DPO designado (arts. 37 a 39 RGPD). Es una lectura previa: no modifica código, ADR, esquema ni la especificación. Continúa `dashboards-lectura-dpo.md`, `entorno-produccion-lectura-dpo.md` (apartado 1) y `registros-tecnicos-lectura-dpo.md`.

Lee el apartado «Dashboard de Plataforma: Operación técnica» de `docs/producto/Especificación de dashboards de ResidApp.md` (líneas 146 a 220 en el árbol de trabajo `docs-plan-monitor-plataforma`; en adelante, «la especificación»), más «Privacidad, permisos y auditoría» (245 a 264) y «Decisiones abiertas» (293 a 303).

Etiquetas: **[Hecho]** verificado en el repositorio o en un texto de `normativa-local` (con archivo y línea); **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no confirmado.

Palabras que uso, una vez: **latido** es la anotación que una tarea deja al terminar cada vuelta para demostrar que sigue viva; **firma** del error es su «huella» (tipo de excepción, número de SQL o código de dominio); **plantilla de ruta** es la dirección de la pantalla con los huecos vacíos (`/Residente/{linkId}/Citas`), no con sus valores; **cifra mínima** es el número por debajo del cual una cifra se oculta o se agrupa; **procedimiento almacenado** es una rutina guardada en la base de datos que hace una operación fija, de modo que quien la llama no toca la tabla directamente; **ámbito activo** es el par centro y perfil con el que ha entrado una persona.

## Conclusión

**Admisible con condiciones, en dos tiempos.**

- **Fase 1 (salud, latido, reglas R1 a R6): admisible.** La tabla del latido no lleva persona, centro ni mensaje; es telemetría de funcionamiento. Puede pasar a esquema con las condiciones del apartado 1.
- **Fase 2 (errores con centro, reglas R7 a R9): admisible solo si se estrecha lo que se guarda y lo que Plataforma puede leer.** La tabla de recuentos por día, firma, ruta y centro **no contiene ningún dato personal directo**, pero el centro y la ruta, juntos, pueden **singularizar** a una persona en un centro pequeño (considerando 26 del RGPD: «singularización»), y eso la acerca a un registro de actividad del personal. Mi propuesta es mantener la columna del centro (decisión de Orlando) y **limitar su uso**: centro solo para los fallos de seguridad, nunca en el cruce centro × firma × ruta que se enseña en pantalla, y un procedimiento de lectura que solo devuelva recuentos. Con eso la fase 2 queda dentro de lo defendible. Sin eso, no.

**El riesgo más importante:** que la tabla del centro se convierta, sin quererlo, en un **registro de qué pantalla falla en qué centro cada día**, legible por una sola persona de ResidApp y sin cifra mínima; en un centro con una sola Administración equivale a «lo que hizo esa persona hoy». Es una vigilancia de personal por la puerta de atrás, y la finalidad declarada («disponibilidad y seguridad») no la cubre. El segundo riesgo, de ejecución: **la hora**. «Nunca se guarda la hora» solo se cumple si la tabla no lleva ninguna columna de fecha y hora, ni fila de identidad creciente, ni versión de fila (apartado 8.2).

Dos hallazgos de coherencia que la especificación debe corregir antes de construir: las «últimas 24 h» no se pueden calcular con un contador diario (apartado 8.3) y el texto «no hay almacenamiento nuevo» ya no es cierto (apartado 9).

## Hechos del repositorio que condicionan la lectura

1. **[Hecho]** Hoy el registro técnico escribe el **centro «declarado»** por la cookie, que no se valida: `TechnicalFailureLog.cs:16-17` («el centro que declara la cookie del ámbito activo (no firmada: es lo que dice la petición, no lo verificado)»), `Program.cs:455-458` y `ActiveProfileScopeCookie.cs:11-14` («es puramente cosmético/UX»). La especificación acierta al no usarlo (línea 194). Coherencia: cuando exista el centro validado, el registro de A4 debería pasar a usarlo (`registros-tecnicos-lectura-dpo.md:86`).
2. **[Hecho]** El registro técnico actual solo observa **acceso denegado** y **fallo inesperado**; los conflictos y los datos no válidos no se registran (`TechnicalFailureLog.cs:12-13` y `36-47`). Incluir los `*_CONFLICT` en la tabla es una ampliación que A4 no hizo. Los códigos de conflicto son una lista cerrada con muchos de dominio (`ApplicationResult.cs:71-77`: `RESIDENT_TRANSFER_CONFLICT`, `FAMILY_PORTAL_ACCESS_CONFLICT`, `ACCOUNT_SUBJECT_TAKEN`, etc.).
3. **[Hecho]** En el árbol de trabajo hay **seis** tareas en segundo plano (`Program.cs:233, 235, 313, 322, 325, 328`: dos avisos a CJ, aviso de cambio, recordatorio, purga de correos y avisos sin resolver). La especificación habla de **siete** («avisos externos»): **[A verificar]** que la séptima llegue con el PR 6 de las citas externas; hoy no existe.
4. **[Hecho]** Cuando su función está apagada, las tareas **no hacen vuelta**: `ChangeNoticeWorker.cs:35-38` y `ReminderWorker.cs:30-33` hacen `continue` sin tocar la base; `AvisoNotasCjWorker.cs:28-31` igual. Un latido que solo se escribe al terminar una vuelta distinguirá mal «apagada a propósito» de «muerta» (apartado 1).
5. **[Hecho]** Las tareas ya registran **solo el tipo de excepción** (`ChangeNoticeWorker.cs:61`, `ReminderWorker.cs:59`, `EmailLogPurgeWorker.cs:51`, `AvisoNotasCjWorker.cs:52`) y los recuentos de filas (`EmailLogPurgeWorker.cs:47`). El latido solo traslada eso a una tabla.
6. **[Hecho]** No existe `/healthz` (búsqueda de `HealthChecks`, `healthz`, `MapHealth` y `UseHealth` en `src/`: sin resultados).
7. **[Hecho]** `PlataformaController` no consulta hoy nada de operación; solo centros, módulos por vencer y cuentas de varios centros (`PlataformaController.cs:27, 34, 255`).
8. **[Hecho]** El ADR 0008 deja al perfil Plataforma un «salto» **por tabla y por operación**, solo en las tablas de provisión (líneas 60 a 68), y fija la regla «ninguna consulta ni clave foránea nueva cruza centros fuera del perfil `PLATAFORMA`» (82-83). Hay precedentes de rutinas con `EXECUTE AS OWNER` para lecturas y purgas acotadas: `dbo.nombres_unidades_vinculacion` (70-71) y las purgas de `0085` y `0089` (116). Hay precedentes de tablas deliberadamente fuera de la seguridad por filas: `eventos_acceso`, `invitaciones_acceso` y `credenciales` (`dashboards-lectura-dpo.md:44`).
9. **[Hecho]** El ADR 0011 admite un solo caso de uso de IA (el dictado, línea 38), exige para abrir otro «un caso de uso que lo necesite y que las reglas no resuelvan» (93-94) y recoge el criterio de 2026-10-21: «la IA no lee datos de residentes ni redacta, resume, prioriza o hace triaje» (71).

---

## 1. La tabla del latido

**Conclusión:** [Opinión] **sí basta** con la lectura actual de dashboards y de entorno de producción; no hace falta una lectura nueva para esta tabla, solo estas condiciones.

**Finalidad.** [Opinión] Es la misma que ya cubrí para «Servicio»: vigilar la disponibilidad del servicio. Una tarea muerta es una indisponibilidad (art. 32.1.b del RGPD: «capacidad de garantizar la confidencialidad, integridad, disponibilidad y resiliencia permanentes de los sistemas y servicios de tratamiento»; RGPD consolidado a 2016-05-04). No lleva centro, cuenta, residente ni mensaje: **probablemente no es un dato personal** (considerando 26: no se aplica a «información que no guarda relación con una persona física identificada o identificable»). Tampoco hay categoría especial (art. 9): no hay dato de salud. Base jurídica: no hace falta una propia para datos que no son personales; para el resto de la sección rige la misma de A4 (apartado 4).

**Condiciones y columnas:**

1. **Una fila por tarea, que se sobrescribe. Nunca historia.** [Opinión] Un histórico de vueltas con hora y recuento ya sería un registro de actividad con rastro de lo que hace el servicio (cada recordatorio enviado deja un minuto); si algún día se quiere, nueva lectura.
2. **Columnas permitidas:** nombre de la tarea (lista cerrada), última vuelta (instante), última vuelta correcta (instante), duración (milisegundos), elementos tratados (entero) y tipo de excepción. **Sin** mensaje, sin pila, sin identificadores de lo tratado, sin centro, sin nombre de usuario de la base.
3. **Tipo de excepción: sí se puede guardar, con filtro.** [Opinión] El nombre de la clase no lleva datos. Pero que el valor lo valide quien escribe: patrón de nombre de clase (letras, dígitos, punto y subrayado), longitud máxima y, si no encaja, «(omitido)». Es la misma idea que `CodePattern` en `TechnicalFailureLog.cs:27` y `65`. No guardes `InnerException` ni `Message`.
4. **«Elementos tratados»: el menos útil.** [Opinión] Con un solo centro piloto, «1 aviso tratado en esta vuelta» es un correo a una familia a un minuto concreto. No lo veo grave (se sobrescribe cada minuto y no hay centro), pero es el campo que más se parece a actividad. Si no sirve a ninguna regla de R1 a R6, no lo guardes; si lo guardas, solo la última vuelta. **[Decidir]** Orlando.
5. **Distinguir «apagada» de «muerta».** [Hecho: hechos 4 de arriba] Si la función está apagada (correo apagado, sin dirección de CJ), la tarea no hace vuelta y el latido se queda viejo: R1 saltaría siempre en dev. El latido debe poder decir «apagada por configuración» (y la regla ignorar esa tarea), o R1 será ruido y se aprenderá a ignorarla.
6. **Escribir el latido tiene un efecto colateral:** hoy, con el correo apagado, las tareas «no tocan la base» (`ChangeNoticeWorker.cs:12-14`). Un latido por minuto toca la base siempre. **[A verificar]** que no impida que una base con pausa automática se duerma (coste, no privacidad).
7. **Permisos.** [Hecho: ADR 0008, 111-117] Toda tabla nueva queda sin permiso hasta que un script la declara y `PermisosAplicacionTests` lo exige. Dos tareas (las de CJ) no tienen conexión propia a la base y escribirían con el usuario de la aplicación; las demás, con el rol `residapp_tareas`. [Opinión] Dar a `residapp_tareas` escritura sobre **solo esta tabla** no cambia su carácter (el rol salta el filtro por centro pero solo con permisos por columnas, ADR 0008, líneas 52 a 59), pero hay que listarlo en la revisión del ADR 0008 y en `tarea-correo-identidad-rls.md`.
8. **En producción las dos tareas de CJ no existen** (la carpeta `/pendientes-cj` no viaja a producción, ADR 0014, línea 158). El latido y R1 deben tolerar «tarea ausente en este entorno».

**Etiqueta de seguridad:** [Hecho] el latido no se audita en `eventos_auditoria` (no actúa ninguna persona); coherente con las tareas actuales (ADR 0012, líneas 79 a 80).

---

## 2. La tabla de errores con centro: condiciones mínimas

**Conclusión:** [Opinión] **admisible con las condiciones (a) a (e) de abajo. Sin ellas, no.** La decisión de Orlando de guardar el centro la respeto; lo que propongo es acotar **dónde** se guarda, **quién** lo lee y **qué** se enseña.

**Qué sí y qué no es esto.** [Opinión] Es una tabla de recuentos: no guarda cuenta, sesión, IP, residente ni hora. En sentido estricto, un recuento por centro, firma, ruta y día es **información agregada**. El problema no es lo que contiene, sino lo que se puede **inferir** cuando el centro es pequeño (considerando 26: hay que tener en cuenta «la singularización» y «todos los medios que razonablemente pueda utilizar» quien tenga la tabla, y aquí quien la lee es la misma persona que ve cuántas cuentas tiene cada centro en «Servicio»). Por eso la trato **como dato personal potencial**, con las cautelas de abajo. No contiene datos de salud: un código como `RESIDENT_TRANSFER_CONFLICT` sin residente ni hora no identifica a nadie.

### (a) Cómo la lee Plataforma sin ampliar el ADR 0008 más de lo imprescindible

Propuesta, de menor a mayor cambio sobre el ADR 0008:

1. **La tabla no lleva seguridad por filas**, como `eventos_acceso`, `invitaciones_acceso` y `credenciales` (`dashboards-lectura-dpo.md:44`; `CoberturaRlsTests.cs:19-22`). Se añade a la lista «de diseño» de esa prueba, con su motivo. No es una tabla clínica ni de datos de un centro; es telemetría de servicio con una columna de centro. **No** hace falta ningún salto nuevo de Plataforma por tabla ni por operación (ADR 0008, 60-68): la regla «nada cruza centros fuera de Plataforma» (82-83) se sigue cumpliendo porque solo Plataforma la lee.
2. **La aplicación no toca la tabla directamente**: ni `SELECT`, ni `INSERT`, ni `UPDATE`. Solo dos procedimientos almacenados con `EXECUTE AS OWNER`, siguiendo el modelo de `0085`, `0089` y `nombres_unidades_vinculacion`. Es la forma coherente con `0091` (todo lo no concedido va con `DENY`, que gana a `db_datareader`; ADR 0008, 113-114), porque **`db_datareader` hoy le daría lectura de toda la tabla a cualquier conexión de la aplicación**.
   - **Escritura** (`registrar_errores`): recibe un lote de recuentos ya agrupados por la aplicación y suma al contador del día. **Deduce el centro dentro de la base a partir del identificador del ámbito activo y del sujeto**, con la misma lógica de `seg.fn_centro_del_ambito` (ADR 0008, línea 45), y **descarta cualquier centro que le pase la aplicación**. Así «validado por la base» es literal, no «el que dice el código de la aplicación»; y una cookie manipulada no puede atribuir errores a otro centro. Sin ámbito (tarea o antes de entrar), centro vacío.
   - **Lectura** (`leer_errores_plataforma`): comprueba **dentro de la base** que el sujeto tiene un ámbito vigente `PLATAFORMA` (el mismo predicado que usan `0088` y `0090`, ADR 0008, líneas 65-66), y devuelve **solo marginales**: (i) por firma y plantilla de ruta, **sin centro**; (ii) por centro, **solo el total** (para R8). **Nunca devuelve el cruce centro × firma × ruta.** El cruce existe en la tabla (hace falta para sumar), pero no sale de la base.
3. **Cambio sobre el ADR 0008:** una línea nueva en la lista de tablas fuera de la seguridad por filas y una regla («la tabla de errores nunca lleva cuenta, sesión, IP, identificador de residente, vínculo o invitación, ni hora; solo la lee Plataforma, por procedimiento, y solo devuelve recuentos»). No hay otro cambio.
4. **Escribir al fallar es peligroso:** si cae la base, escribir cada error en la base multiplica el problema (cada petición fallida intenta otra escritura). [Opinión] La aplicación debe **agrupar en memoria** (por minuto) y volcar por lotes, con un tope de combinaciones distintas por día (el resto va a una fila «(otros)»). Pierde algún recuento si el proceso se cae; es aceptable, porque el detalle sigue en el registro técnico de A4 y en Azure. Si la base no responde, se descarta el lote (y queda un aviso en el registro técnico, sin datos).
5. **El identificador del ámbito activo se captura al autorizar, no al fallar.** [Opinión] En la ruta de error no se vuelve a preguntar a la base «¿de qué centro es esta petición?» (si la base cae, falla). La autorización ya revalida el ámbito en cada petición (`ActiveProfileScopeCookie.cs:11-14`); lo que se guarda es el identificador del ámbito ya aceptado, y es el procedimiento de escritura el que lo traduce a centro.

**Auditoría de la lectura.** [Opinión] Con la variante de marginales (sin cruce), **no hace falta auditar la apertura** (mismo criterio que la línea 144 de la especificación). Si algún día se enseña el cruce centro × firma × ruta, sí: una anotación por sesión y por pantalla, con protección contra duplicados.

### (b) Plazo de conservación

Ver apartado 5: **30 días**, con borrado en el mismo procedimiento de escritura.

### (c) ¿Cifra mínima si un centro tiene muy pocos errores?

**[Opinión] Una cifra mínima clásica (ocultar «1» y «2») choca con la finalidad**: un único error raro en un centro es justo lo que el operador quiere ver (R7). Ocultarlo la vaciaría. Lo que protege de verdad es **no enseñar el cruce y limitar el centro a lo que lo necesita**:

1. **Sin cruce en pantalla** (apartado (a).2). Por centro, solo el total; por firma y ruta, sin centro. Así «qué pantalla falla» y «en qué centro» no se pueden unir a la vista.
2. **R8 con mínimo absoluto.** [Hecho: especificación, línea 180] R8 salta con «más de un porcentaje de los errores de 24 h». Con uno o dos centros (el piloto) el porcentaje siempre es alto y R8 saltaría por un solo error. R8 debe exigir **un mínimo de errores en el día** antes de nombrar al centro. **[Decidir]** el número: propongo **5** como valor provisional (no sale de ninguna norma; es un orden de magnitud que evita nombrar a un centro por uno o dos errores) y que el DPO real lo ajuste, en coherencia con la cifra mínima general que sigue pendiente (`dashboards-lectura-dpo.md:116`).
3. **El centro solo para los fallos de seguridad.** Guardar centro cuando la firma es acceso denegado o fallo inesperado (lo que A4 ya registra y lo que puede ser una brecha). **No guardarlo para los conflictos** (`*_CONFLICT`): no son un incidente y no se avisa a ningún responsable por ellos. [Opinión] Es minimización (art. 5.1.c: «adecuados, pertinentes y limitados a lo necesario»): el motivo de Orlando (alcanzar al centro, art. 33) no alcanza a los conflictos de edición. Esto reduce además lo más cercano a «actividad de personas» (dos personas editando a la vez la misma pantalla).
4. **Centro reservado `PLATAFORMA`.** Los errores del propio operador (centro reservado, ADR 0006, línea 17) no cuentan como centro para R8 ni salen en la lista de centros.
5. **Rutas por perfil.** [Opinión] Dado que Plataforma ya sabe cuántas cuentas hay por centro y perfil, una ruta propia de Administración, en un centro con una sola Administración, atribuye de hecho el fallo a una persona. Otra razón para el punto 1: la ruta nunca se enseña junto al centro.

### (d) Qué debe y no debe contener la firma y la plantilla de ruta

**Firma (tipo, número de SQL o código):**

- **Sí:** nombre del tipo de excepción (patrón de letras, dígitos, punto y subrayado, longitud máxima); número de error de SQL Server; código de dominio **solo si encaja entero** con `^[A-Z][A-Z0-9_]{2,80}$` (el mismo filtro de `TechnicalFailureLog.cs:27` y `65`; `SafeCode`). Lo que no encaje cae en «(otro)».
- **No:** el mensaje de la excepción (un mensaje de SQL por clave duplicada puede llevar el valor, `entorno-produccion-lectura-dpo.md:56`), la pila, la excepción interna, parámetros, el estado de ninguna petición.
- **Lista cerrada.** [Opinión] Para que la cardinalidad no crezca sin control (y un atacante autenticado no pueda inundar la tabla), las firmas admitidas salen de las listas que el código ya conoce; lo desconocido va a «(otro)».
- **Códigos con contenido semántico.** [Opinión] Algunos códigos describen un hecho (`FAMILY_ANONYMIZATION_*`, bajas, traslados). Sin residente ni hora ni (para conflictos) centro no identifican a nadie; con centro, un código raro en un centro con pocas familias sí podría. Otra razón para el punto (c).3.

**Plantilla de ruta:**

- **[Opinión] Deriva de los metadatos del punto final ya resuelto**, no del texto de la dirección. En ASP.NET, el patrón de la ruta del punto final (o el controlador y la acción de la acción coincidente) es texto fijo del código; el valor de la petición no se mezcla. Es el mismo principio de `entorno-produccion-lectura-dpo.md:49` («plantilla de ruta, no el valor»).
- **Sin punto final coincidente** (rutas inexistentes, escaneos): «(sin ruta)». **Nunca** se cae a la dirección real de la petición. Esta es la fuga más fácil: un `Request.Path` «por si acaso».
- **Lista cerrada de plantillas** construida al arrancar a partir de las rutas registradas; cualquier otra cosa va a «(otra)». Esto, además, acota la cardinalidad.
- **Casos concretos.** [Hecho] La ruta `/Acceso/Invitacion/{token}` lleva el token en la dirección (`entorno-produccion-lectura-dpo.md:41`; ADR 0014, líneas 150 a 152): la plantilla es `Acceso/Invitacion/{id}` y no es sensible; **la dirección real nunca debe llegar a esta tabla**. Lo mismo con `linkId`, `citaId`, identificadores de residente y episodio (`entorno-produccion-lectura-dpo.md:41`: 86 usos de `asp-route-id`). **Sin cadena de consulta, sin cabecera `Host` ni `Referer`.**
- **Prueba obligatoria** (para el PR que lo construya): una petición con un GUID real y un token real que provoque un error **no deja ninguna fila con patrón GUID, ni `Invitacion/` seguido de valor, ni cadena de consulta**.

### (e) El aviso R8 «errores concentrados en un centro»

**[Opinión] Plantea tres problemas.**

1. **Redacción engañosa.** [Hecho: especificación, línea 180] El texto recomendado cita el «plazo propio del contrato, art. 33 RGPD». Un pico de errores **no es una brecha de seguridad**: el art. 33.1 habla de violación de seguridad de datos personales; el reloj de 72 horas del responsable corre desde que «haya tenido constancia» de ella. Y, según el CEPD, «el encargado no necesita evaluar el riesgo antes de avisar» pero sí **establecer que ha habido una brecha** (directrices 9/2022, párrafo 44, líneas 458 a 462 del texto local). Avisar a un centro «porque tiene más errores» como si fuera una brecha crea ruido, y un aviso de brecha falso consume el reloj del centro. R8 debe decir: «comprueba si es un fallo propio de ese centro (configuración o datos) y, si sospechas acceso no autorizado a datos, aplica el protocolo de brechas». El reloj contractual lo abre un **incidente confirmado**, no R8.
2. **Estadística con un centro es inútil.** Con uno o dos centros, el porcentaje es siempre alto. Ver (c).2.
3. **Atribución falsa.** Los errores sin centro (apartado 3) cuentan en el total pero no en ningún centro; hay que definir **si el denominador los incluye**. Propuesta: el porcentaje se calcula **solo entre errores con centro**, y el aviso muestra a su lado el recuento «sin centro» para no dar una falsa sensación de reparto.

---

## 3. Errores sin centro (fuera de petición o antes de entrar)

**[Opinión] Es aceptable, y es lo correcto.** Las tareas trabajan sobre todos los centros y, si fallan, no hay motivo para atribuirlas a uno; y antes de entrar no hay ámbito verificado. El **centro vacío es el valor honesto**, no un defecto.

Condiciones:

1. **No rellenarlo por otra vía.** [Opinión] No intentar adivinar el centro con la IP, el agente de usuario, la hora o un cruce con `eventos_acceso` o con el registro de A4: eso recrearía el registro de «accesos por centro» que `dashboards-lectura-dpo.md:61` pide rechazar en revisión («sería, indirectamente, un registro de presencia del personal por centro»).
2. **Verlo y nombrarlo.** La pantalla debe enseñar el recuento «sin centro» como una fila más, no omitirlo; el aviso R8 lo tiene en cuenta (apartado 2, e.3).
3. **No es prueba de ausencia.** Si la base cae, el error no se puede guardar (especificación, línea 195); si el centro es desconocido, no se atribuye. Ni la ausencia de filas ni el reparto prueban que un centro **no** está afectado. La obligación de avisar al centro de una brecha no depende de esta tabla.
4. **Un fallo de una tarea con el centro conocido en el código** (por ejemplo, una fila de un centro concreto): tampoco se atribuye. Una tarea que falla es del servicio.

---

## 4. La frase de finalidad

**[Opinión] La frase actual no basta para esta sección; añade un matiz y una prohibición explícita.** «Vigilar la seguridad y la disponibilidad del servicio» cubre «Servicio» (accesos), pero Operación técnica añade: (1) **diagnóstico de fallos de funcionamiento** (buen funcionamiento, no solo disponibilidad ni seguridad); (2) **atribución a un centro** para avisarle; (3) **recomendaciones automáticas**. Y la propia especificación indica un límite (línea 139: «usarlo para actividad comercial sería un fin propio de ResidApp»). Sin una prohibición escrita, cualquiera de las tres puede derivar hacia uso propio de ResidApp.

**Por qué importa.** [Hecho] Art. 28.3.a: el encargado «tratará los datos personales únicamente siguiendo instrucciones documentadas del responsable». Art. 28.10: «si un encargado del tratamiento infringe el presente Reglamento al determinar los fines y medios del tratamiento, será considerado responsable». LOPDGDD art. 33.2: tiene la consideración de responsable «quien figurando como encargado utilizase los datos para sus propias finalidades». Aquí hay datos personales potenciales; si ResidApp usa estos recuentos para decidir qué construir o cómo facturar, dejaría de ser encargada para ese uso. Lo que sí es de ResidApp, como cualquier proveedor, es asegurar el servicio (art. 32.1 obliga también al encargado), pero **eso conviene que lo diga el centro en el encargo**, no que lo suponga ResidApp.

**Redacción propuesta** (para el anexo del contrato de encargado; la residencia la adopta como suya; campos entre corchetes):

> **Vigilancia técnica del servicio.** [RESIDENCIA] encarga a ResidApp vigilar la disponibilidad, la seguridad y el buen funcionamiento técnico del servicio. Para ello, ResidApp (a) comprueba que las tareas automáticas de la aplicación se ejecutan y (b) cuenta cada día los errores técnicos de la aplicación por tipo de error y pantalla y, cuando el error pueda afectar a la seguridad de los datos, por centro. Estos recuentos no incluyen nombres, identificadores de residentes ni familiares, ni la hora, y los ve solo el equipo técnico de ResidApp. ResidApp los usa **únicamente** para detectar y corregir fallos y para avisar al centro afectado. ResidApp **no** los usa para medir, comparar o evaluar la actividad de personas, equipos o centros, ni con fines comerciales, ni los entrega al centro como medida del trabajo de su personal. Se borran a los [N] días.

**Frase para el registro de actividades de ResidApp (art. 30.2.b y d):**

> Vigilancia técnica del servicio por cuenta de cada centro: latido de las tareas automáticas y recuentos diarios de errores técnicos por tipo, pantalla y, para fallos de seguridad, centro. Sin datos de personas, residentes ni hora. Acceso: equipo de Plataforma. Plazo: [N] días. Medidas: lectura por procedimiento que solo devuelve recuentos; sin cruce de centro con pantalla; sin identificadores.

**Frase para la política de privacidad para profesionales** (lenguaje llano; solo si se construye tal cual):

> Para que la aplicación funcione y para poder avisarte si algo falla, ResidApp cuenta cada día cuántos errores técnicos ocurren, de qué tipo y en qué pantalla (y, si el error puede afectar a la seguridad de los datos, en qué centro). Estos recuentos no llevan tu nombre, ni el de ningún residente ni familiar, ni la hora; solo los ve el equipo técnico de ResidApp; se borran a los [N] días y no se usan para evaluar tu trabajo.

**Nota.** [Opinión] Si la tabla fuese plenamente anónima, la frase para el personal no sería obligatoria; la recomiendo igualmente por transparencia (art. 5.1.a) y porque el riesgo de singularización en un centro pequeño no se puede descartar.

**R9 y la finalidad.** [Opinión] «Revisar si dos personas editan a la vez la misma pantalla» es una señal de diseño de producto. Aceptable **solo** si se calcula con recuentos globales, sin centro (apartado 2, c.3): sin centro ni persona, es información agregada, y el artículo 28.10 no se activa. Con centro, no.

---

## 5. Retención y alineación con el resto

**Propuesta (tabla de errores): 30 días** [Decidir: DPO real y Orlando], con el borrado dentro del procedimiento de escritura (al insertar el primer recuento de un día se borran los de más de 30 días); sin proceso nocturno nuevo.

Razones:

1. **Necesidad.** [Opinión] La pantalla enseña «hoy» y 7 días, y R7 compara con 7 días anteriores: la necesidad real son **14 días**. 30 deja margen para una revisión mensual de tendencias sin guardar más.
2. **Una tabla derivada no vive más que su fuente detallada.** [Hecho] El registro técnico de A4 se conserva **90 días** (`registros-tecnicos-lectura-dpo.md:85`, decisión de Orlando 2026-11-12). Los recuentos son una vista agregada de lo que A4 detalla; nunca deben durar más que el detalle. 30 ≤ 90 cumple esto.
3. **Si el DPO real prefiere un solo plazo** (90, como A4), es defendible, porque la tabla lleva menos información que A4; yo prefiero 30 por minimización (art. 5.1.e: «no más tiempo del necesario»).
4. **Borrado real.** [Hecho] Esta tabla se puede actualizar y borrar (es un contador); no tiene la inmutabilidad de `eventos_acceso` (`0062:49-55`). El borrado es una instrucción corriente. Las copias de seguridad la conservan hasta que vencen (política de copias, `entorno-produccion-lectura-dpo.md:107`).
5. **Latido:** sin plazo (se sobrescribe).

**Alineación con `eventos_acceso`.** [Hecho] El plazo de `eventos_acceso` (con la IP) **sigue sin decidir** (`expurgo-y-supresion.md:79, 160`; `antes-de-datos-reales.md:134`) y bloquea datos reales en la sección «Servicio» (especificación, línea 140). **La tabla de errores no lleva IP, sesión ni cuenta, así que su plazo es independiente y no debe esperar a esa decisión; pero tampoco debe usarse como argumento para fijar el de `eventos_acceso`.** Dos plazos distintos, no uno por cada sitio donde aparece un dato personal: la IP vive solo en `eventos_acceso` (y en los registros HTTP de Azure, que están apagados, `registros-tecnicos-lectura-dpo.md:83`).

---

## 6. Comprobación de salud del correo y del dictado, y `/healthz`

**Correo y dictado leen solo la configuración y no llaman al proveedor: sin objeción.** [Opinión] Es lo correcto: llamarlos gastaría cuota y mandaría tráfico a un tercero por cada refresco (es lo que dice la especificación, línea 161), y en el caso del dictado, cualquier llamada de prueba con audio sería un tratamiento nuevo con Mistral (subencargado). Condiciones menores:

1. **No mostrar nada de la clave ni de la cadena**: ni prefijo, ni longitud, ni nombre del remitente. Solo «puesta / no puesta» y «activo / apagado».
2. **«Correo apagado» es un estado normal en dev** (ADR 0012, línea 82 y `entorno-produccion-lectura-dpo.md:81`): no debe salir como alarma.
3. **No registrar** los valores en el registro técnico ni en la telemetría (mismo criterio que la caché, especificación, línea 241, condición 6).
4. **R5** (módulo de dictado activo en algún centro y clave global ausente) usa la tabla de módulos por centro que Plataforma ya lee (`PlataformaController.cs:34`): sin objeción.

**`/healthz` mínimo para el health check de App Service.** Sin objeción **si**:

1. **Sin detalle.** Solo un código de estado y cuerpo vacío o fijo: sin versión, sin nombre de componentes, sin mensaje de excepción, sin tiempos. Es una puerta anónima a Internet.
2. **Solo vida del proceso, no de la base.** [Opinión] Si el punto final consulta la base, cualquiera puede lanzar consultas a base de pedir `/healthz`; y si la base se queda lenta, App Service puede dar la instancia por mala y reiniciarla, agravando el incidente. La base se comprueba en la pantalla de Plataforma, no en `/healthz`. **[A verificar]** el comportamiento de App Service cuando el health check falla con una sola instancia.
3. **Exento de todo lo demás:** sin autenticación (hay que declararlo, porque la aplicación autoriza por defecto: **[A verificar]** la política por defecto), sin cookies, **fuera del recuento de errores** (que no se cuente a sí mismo) y con los límites de peticiones por IP que ya existen. **[A verificar]** que `AllowedHosts` (hoy `*`; en producción, el dominio, `entorno-produccion-lectura-dpo.md:158`) no lo rechaza cuando lo llama App Service.
4. **Sin telemetría de su cabecera ni de la IP de quien lo llama**: es una IP de Azure, pero el principio es que no se registre.

---

## 7. IA: «análisis con IA» solo sobre la salida de las reglas

**Confirmo, con tres correcciones.** [Opinión] La condición de la especificación (línea 183: «con Mistral, solo en Plataforma (no lleva datos personales), solo sobre la salida de las reglas y nunca sobre la base, voluntario, auditado sin contenido y con un ADR nuevo») es correcta como **mínimo**, pero falta:

1. **La primera puerta del ADR 0011 no se cumple hoy.** [Hecho] «Abrir una puerta exige, en este orden: un caso de uso que lo necesite y que las reglas no resuelvan» (ADR 0011, líneas 93-94). Un resumen de cinco hallazgos de texto fijo lo resuelven las reglas; la propia especificación lo reconoce («aporta poco»). Mientras eso sea cierto, no se abre; el ADR nuevo debe demostrar lo contrario.
2. **El criterio del 2026-10-21 exige redacción.** [Hecho] El ADR 0011 (línea 71) dice «la IA no lee datos de residentes ni redacta, resume, prioriza o hace triaje». Un análisis que «resume y prioriza» hallazgos choca con la letra aunque no sea clínico. El ADR nuevo debe acotar ese criterio («en el ámbito clínico») o la excepción debe constar.
3. **Lo que se envía debe estar fijado por lista, no por intención.** [Opinión] «Sin datos de residentes» no basta como garantía: debe haber una **lista cerrada de campos** que componen la petición (código de regla, recuentos, nombre de la regla, **sin** nombre de centro o con un alias, **sin** firmas con códigos de dominio libres, **sin** plantillas de ruta con valores), y una prueba que falle si se añade otro campo. El nombre del centro es un dato del cliente (no personal) pero confidencial; no hay necesidad de enviarlo.

Además, condiciones que ya serían habituales y conviene dejar escritas: contrato de encargado con Mistral y región (ADR 0011, línea 60; dossier del dictado, `entorno-produccion-lectura-dpo.md:81`); una nota breve en la EIPD; sin retención del contenido por el proveedor; el texto generado se muestra marcado como generado por IA (**[A verificar]** art. 50 del Reglamento de IA, consolidado a 2026-07-27: el marcado legible por máquina es obligación del proveedor, apartado 2; que una persona sepa que habla con IA es evidente aquí, apartado 1); **alfabetización en IA** (art. 4: «adoptarán medidas para apoyar la promoción de la alfabetización en materia de IA de su personal»; el texto consolidado local lo recoge en esos términos); el botón desactivado por centro de configuración. No se envía nada si el módulo de IA no está activo para Plataforma.

**Opinión de fondo:** no lo reabriría. Añade un tratamiento nuevo con un tercero para resumir cinco frases fijas.

---

## 8. Otros riesgos no contemplados

1. **Vigilancia del personal por la puerta de atrás (el que Orlando señala).** [Opinión] Confirmado: es el riesgo principal. Mitigaciones ya propuestas: sin cruce en pantalla, centro solo para fallos de seguridad, mínimo absoluto en R8, R9 global, fin explícitamente excluido en el encargo. Añado una **cláusula negativa hacia el centro**: si un centro pide «qué errores ha tenido mi personal» o «quién provocó esto», ResidApp no lo responde con esta tabla (no lo tiene), y el contrato lo dice. Para el centro, el control laboral de su personal es suyo (LOPDGDD art. 87, derecho a la intimidad en el ámbito laboral, **[A verificar]** su alcance con un laboralista); ResidApp no debe ser su instrumento.
2. **«Nunca la hora, solo el día»: confirmado como requisito, con condiciones técnicas** [Decidir y verificar]:
   - **Ninguna columna de instante** en la tabla de errores (nada de `creado_en`, `actualizado_en`, `datetime` ni `rowversion`). Una columna de «última actualización» revela la hora del último error del día; una de «creado en», la del primero.
   - **Clave natural compuesta** (día, firma, ruta, centro), **sin identidad creciente**: una columna de identidad conserva el **orden** en que aparecen las combinaciones dentro del día.
   - **Sin tabla temporal ni «versionado del sistema»**, que guardarían las versiones con hora.
   - **El «día»** se fija en una sola zona (propongo la del servidor, `Europe/Madrid`, que la aplicación ya obliga a verificar: `Program.cs:461`) y se documenta.
   - **El volcado por lotes** se hace sin conservar el instante del lote.
   - **Prueba:** que el esquema de la tabla no contenga ninguna columna de tipo fecha con hora.
3. **«Últimas 24 h» no se puede calcular con un contador diario.** [Hecho: especificación, líneas 163 y 180] La tabla guarda el día; no hay forma de saber qué fue en las últimas 24 horas. La pantalla debe decir «hoy» (día en curso, incompleto) y «últimos 7 días naturales», y R8 usar el día natural.
4. **Reidentificación por combinación centro, ruta y hora.** [Opinión] Con la hora fuera, queda centro + ruta + día. Es el riesgo (c) de arriba, tratado con las cautelas.
5. **Inundación y falsificación de la tabla.** [Opinión] Un usuario autenticado puede provocar errores a propósito. Impacto bajo (inflar contadores); mitigado con lista cerrada de firmas y rutas, fila «(otros)» y tope de combinaciones por día. La falsificación del **centro** se evita deduciéndolo en la base (apartado 2, a.2).
6. **Error al escribir el error.** [Hecho: `ApplicationResult.cs:158-168`] El observador ya ignora sus propias excepciones; el volcado por lotes debe hacer lo mismo.
7. **`ResidAppTareas` y los latidos de dos tareas sin conexión propia** (las de CJ): ver apartado 1.7.
8. **Coherencia con A4.** [Opinión] Si la tabla usa el centro validado y el registro técnico de A4 sigue con el «declarado», habrá dos verdades. Alinear A4 cuando exista el contexto validado.
9. **Alertas de Azure** (decisiones 4 y 5 de Orlando): los avisos por correo del grupo `ag-residapp-dev-alertas` llegan a personas con nombre; sus direcciones son datos personales de profesionales y **no se anotan en el repositorio (público)**, como ya decidió Orlando (`registros-tecnicos-lectura-dpo.md:87`). Las métricas de plataforma no llevan IP ni ruta (`registros-tecnicos-lectura-dpo.md:13`).
10. **Producción.** [Opinión] Todo lo anterior vale con datos de prueba ya. Con datos reales, además del ADR nuevo y del DPO real: encargo con la cláusula del apartado 4, registro de actividades, anexo de la EIPD y la política con la frase de profesionales. No se tocan los plazos de `eventos_acceso`.

---

## 9. Choques con la especificación (propuestas; no las aplico)

1. **Línea 224, «No hay almacenamiento nuevo».** Ya no es cierto: hay **dos tablas nuevas** (latido y errores). Cambiar a «salvo las dos tablas de Operación técnica, sin historia y sin datos personales directos».
2. **Línea 114, «no hay que ampliar sus permisos».** Cierto en «Servicio»; en la fase 2 se añade la tabla con lectura por procedimiento.
3. **Línea 144, «No se muestra… errores de la aplicación».** Corregir con una remisión a Operación técnica y a esta lectura.
4. **Línea 251 (tabla de privacidad), fila Plataforma.** Añadir «recuentos de errores técnicos por pantalla y, para fallos de seguridad, por centro».
5. **Líneas 163 y 180: «Últimas 24 h».** Sustituir por «hoy y últimos 7 días naturales».
6. **Línea 162, «sin centro»** para el latido: correcto. Añadir «una fila por tarea y sin histórico» y «apagada por configuración» como estado.
7. **Línea 162, siete tareas.** Aclarar cuáles existen hoy (seis) y que la séptima llega con el PR 6.
8. **Línea 163 y 180: centro solo para fallos de seguridad; nada de centro para los conflictos (R9).**
9. **Línea 180, R8:** mínimo absoluto, denominador con centro, y la redacción sin «art. 33».
10. **Línea 183, IA:** añadir las tres correcciones del apartado 7.
11. **Línea 193:** añadir «ni la hora (ninguna columna de fecha con hora, ni identidad creciente)» y la prueba de rutas.
12. **Línea 195:** añadir «y los errores del lote se descartan si la base no responde».
13. **Líneas 212 a 218:** convertir las cinco preguntas en «respondidas por la lectura del 2026-11-19» con enlace a este documento.
14. **Línea 302 («La retención… sigue pendiente»):** proponer 30 días y dejarlo en «a decidir por el DPO real».
15. **Frase de finalidad (línea 139):** completar con la redacción del apartado 4 para Operación técnica.

---

## Lista de comprobación para el PR que lo construya

**Fase 1 (latido):**
- [ ] Una fila por tarea, sobrescrita; sin histórico; sin centro, mensaje ni identificadores.
- [ ] Tipo de excepción validado por patrón y longitud; «(omitido)» si no encaja.
- [ ] «Apagada por configuración» y «ausente en este entorno» como estados; R1 los respeta.
- [ ] Permisos declarados en el script y en `PermisosAplicacionTests`; `residapp_tareas` solo sobre esta tabla.
- [ ] `/healthz` sin detalle, anónimo, fuera de los recuentos y que no toca la base.
- [ ] Salud del correo y del dictado: solo configuración; sin valores.

**Fase 2 (errores):**
- [ ] ADR nuevo: la tabla queda fuera de la seguridad por filas, con acceso solo por procedimientos, sin cruce de centro y ruta.
- [ ] Escritura por procedimiento que **deduce el centro en la base** a partir del ámbito; la aplicación no pasa centro.
- [ ] Lectura por procedimiento que comprueba el ámbito `PLATAFORMA` vigente y devuelve solo marginales.
- [ ] Tabla sin ninguna columna de fecha con hora, sin identidad creciente, sin tabla temporal; día en `Europe/Madrid`.
- [ ] Plantilla de ruta de los metadatos del punto final, con lista cerrada; «(sin ruta)» sin coincidencia. Prueba con GUID y token reales.
- [ ] Firma con filtro de patrón y lista cerrada; «(otro)» para lo demás; sin mensaje.
- [ ] Centro solo para acceso denegado y fallo inesperado; nulo para conflictos y para el centro reservado.
- [ ] Volcado por lotes en memoria con tope de combinaciones por día; el fallo de volcado no afecta a la petición.
- [ ] Borrado a los [N] días dentro del procedimiento de escritura; prueba de que no queda ninguna fila más antigua.
- [ ] R8 con mínimo absoluto y texto sin referencia automática al art. 33; R9 global sin centro.
- [ ] Encargo con la cláusula del apartado 4; registro de actividades de ResidApp; anexo de la EIPD; frase de política para profesionales.

---

## Decisiones que quedan (resumen para Orlando)

1. Plazo de la tabla de errores: **30 días** (mi propuesta) o 90 (como A4). DPO real.
2. **Número mínimo de errores en R8** (propongo 5 como provisional). DPO real y CJ, junto con la cifra mínima general.
3. Si el centro se guarda **solo para fallos de seguridad** (mi propuesta) o también para los conflictos (decisión original).
4. Si el cruce centro × firma × ruta **sale o no de la base** (mi propuesta: no) y, en ese caso, qué ve Plataforma (marginales).
5. **Si se guarda «elementos tratados»** en el latido.
6. Redacción final de la cláusula de finalidad (apartado 4) y quién la firma por la residencia.
7. Si R9 se mantiene (global, sin centro) o se elimina.
8. **No reabrir la IA** hasta que se cumpla la primera puerta del ADR 0011.

## A verificar

- Séptima tarea («avisos externos») y su latido, con el PR 6.
- Comportamiento del health check de App Service con una sola instancia, y que no choca con `AllowedHosts` ni con la política de autorización por defecto.
- Que el latido no impida la pausa automática de una base serverless (coste).
- Inventario real de plantillas de ruta y de firmas (para fijar la lista cerrada).
- Alcance de LOPDGDD art. 87 (y, en su caso, el art. 20 bis del Estatuto de los Trabajadores) sobre cifras de actividad del personal (laboralista).
- Art. 6.4 y 28.10 del RGPD en versión completa (apartado 4 solo cita la frase clave; el 6.4 no lo he vuelto a abrir).
- Autoridad de control del centro piloto (AEPD o APDCAT; art. 3 de la Llei 32/2010) y su efecto sobre la base jurídica del apartado 4: en una residencia pública el art. 6.1.f no es aplicable a la autoridad en el ejercicio de sus funciones («Lo dispuesto en la letra f)… no será de aplicación al tratamiento realizado por las autoridades públicas en el ejercicio de sus funciones», `rgpd.txt`, línea 88).
- Si el art. 50 del Reglamento de IA consolidado (2026-07-27) cambia algo para un resumen interno.

## Qué he leído y qué no

Leído: la especificación (líneas 100 a 312); `dashboards-lectura-dpo.md`, `entorno-produccion-lectura-dpo.md` y `registros-tecnicos-lectura-dpo.md` enteros; ADR 0006 y 0008 enteros; ADR 0011 (líneas 1 a 6 y 56 a 101); ADR 0012 (por búsqueda de términos); ADR 0014 (líneas 120 a 170); `TechnicalFailureLog.cs`, `PlataformaController.cs`, `ChangeNoticeWorker.cs`, `ReminderWorker.cs`, `EmailLogPurgeWorker.cs`, `AvisoNotasCjWorker.cs` y el principio de `UnresolvedNoticeWorker.cs`; `ActiveProfileScopeCookie.cs`; `ApplicationResult.cs` (líneas 30 a 200); `Program.cs` (líneas 435 a 480, y registro de las tareas por búsqueda). Texto literal de `normativa-local`: RGPD arts. 5, 6, 28, 30, 32 y 33 y considerandos 26, 83 y 87; LOPDGDD arts. 33 y 87; directrices CEPD 9/2022 (párrafos 44 y 45); Reglamento de IA arts. 4 y 50 (consolidado a 2026-07-27, versión del índice). **No he leído:** `AvisoRespuestasCjWorker.cs`, el resto de `Program.cs`, `SqlConnectionFactory`, los scripts `0085`, `0088`, `0089`, `0090` y `0091` (cito su contenido a través del ADR 0008 y de lecturas anteriores), ni `docs/privacidad/tarea-correo-identidad-rls.md`. **No he puesto al día `normativa-local`** (la comprobación más antigua del índice es de 2026-11-12, 7 días; la consulta no depende de ninguna reforma reciente). No he consultado fuentes externas hoy.

**Soy una segunda lectura, no un DPO designado ni un abogado.** Todo lo anterior lo valida una persona antes de usarlo con datos reales.

---

## Decisiones de Orlando posteriores a esta lectura (2026-11-19; añadido por la sesión principal, no por el DPO)

Esta lectura no se ha reescrito. Orlando decidió, y la especificación ya lo recoge:

| Punto | Recomendación de esta lectura | Decisión de Orlando |
|---|---|---|
| Plazo de la tabla de errores | 30 días | **30 días** (a validar por el DPO real) |
| Mínimo absoluto de R8 | 5, provisional | **5, provisional** |
| Centro en los errores | Solo acceso denegado y fallo inesperado | **Igual** (decidido tras una primera decisión contraria que Orlando revisó) |
| Cruce centro × firma × ruta | Que no salga de la base (solo totales por separado) | **Que Plataforma vea el cruce completo, sin mínimo, solo para esos dos tipos** |
| «Elementos tratados» en el latido | No | **No** |
| R9 | Solo global, sin centro | **Igual** |
| IA | No reabrir | Sin cambios: no se construye |

**Consecuencias.** Solo el cruce se aparta de esta lectura. Orlando lo quiere porque el objetivo del registro es acotar al máximo la causa de un fallo, y lo más valioso es ver la primera aparición de un fallo raro en un centro; un mínimo la ocultaría. Por tanto: (1) el DPO real o un abogado debe validarlo expresamente antes de datos reales; (2) el mínimo de 5 queda solo para R8; (3) las tres redacciones del apartado 4 y la lista de comprobación de la fase 2 **hay que ajustarlas** solo en la parte de «sin cruce de centro y ruta» (el resto coincide con lo decidido).
