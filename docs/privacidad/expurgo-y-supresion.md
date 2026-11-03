# Expurgo, plazo de conservación y supresión de datos de personas

**BORRADOR del agente `dpo-residapp`. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-03. Segunda lectura experta, no un dictamen.

**Qué es un expurgo:** el borrado controlado de la documentación clínica cuando vence su plazo de conservación (en catalán, «esporgada»). **Qué es anonimizar:** quitar de un dato lo que identifica a la persona para que ya no se pueda saber de quién es. **Qué es seudonimizar:** cambiar lo que identifica por un código, de modo que con información aparte se pueda volver a la persona; sigue siendo dato personal (RGPD, considerando 26). **Qué es bloquear:** guardar el dato apartado, sin que nadie lo use ni lo vea, solo para ponerlo a disposición de un juez o de una autoridad durante el plazo en que pueden exigirse responsabilidades (LOPDGDD, art. 32).

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL); **[Suposición]** mía; **[Decidir]** lo fija una persona concreta; **[A verificar]** no he podido confirmarlo en fuente oficial. Las fuentes web que uso me devuelven resúmenes automáticos, no el texto literal: lo marco donde importa.

## Conclusión

1. **15 años desde la baja como plazo por defecto: admisible con condiciones.** Es el plazo que mejor protege la disponibilidad de la historia clínica, y el que menos minimiza. Se sostiene porque casi todo lo que guarda ResidApp es «curso clínico» (el seguimiento diario), para el que la Comissió Tècnica recomienda 15 años, y porque el centro conserva la facultad de acortarlo dentro de lo que la ley permita. Las condiciones: que lo confirme el DPO real, que una persona resuelva si el art. 12 de la Llei 21/2000 alcanza a una residencia (sigue abierto), y que el producto no prometa un plazo único para lo que no es historia clínica.
2. **Anonimizar a un familiar: el diseño es válido en lo esencial, con un desacuerdo importante y varias correcciones.** El desacuerdo: «sin guardar el dato anterior» choca con la obligación de **bloquear** al suprimir (LOPDGDD, art. 32). Eso lo decide el DPO; hasta entonces no recomiendo construirlo como irreversible sin esa decisión por escrito (apartado 3.3, punto 1). Las correcciones: la cuenta compartida entre centros, los textos libres inmutables que el diseño no cita, la marca de confirmación del correo, el motivo del cierre de sesiones y los accesos que deben rechazar a un familiar anonimizado.
3. **Lo que más urge no es el expurgo clínico.** El primer vencimiento real de un plazo de 15 años desde la baja llegaría 15 años después de la primera baja real. Lo que puede exigirse desde el primer día es: la supresión de un familiar, el cierre de una cuenta de personal, y **decidir el plazo de los registros que hoy no se pueden borrar nunca** (`eventos_acceso` con IP, `sesiones`, `eventos_auditoria`). Sin plazo para esos tres, el art. 5.1.e RGPD no se cumple (apartado 2.8).
4. **La frase «como ya hace 0075» era imprecisa y el precedente correcto es otro.** `0075` no sustituye ningún texto. El precedente que sirve para dejar pasar una modificación en una tabla protegida con `INSTEAD OF` es `TR_dht_guard` de `0079` (apartados 2.3 y 5.2).

---

# PARTE A. ANÁLISIS Y DISEÑO

## 1. Quince años desde la baja como plazo por defecto

### 1.1 Qué dice la ley, y qué he podido confirmar

- **[Hecho, leído por el solicitante en el PDF del BOE, 2026-11-03]** Art. 12 de la Llei 21/2000, redacción de la Ley 16/2010 (BOE-A-2010-10215): el apartado 4 obliga a conservar, con los datos de identificación, **como mínimo 15 años desde el alta de cada proceso asistencial**, los consentimientos informados, los informes de alta, los informes quirúrgicos y el registro de parto, la anestesia, las exploraciones complementarias, la necropsia y la anatomía patológica. El apartado 6 dice que el resto «puede destruirse» a los 5 años. El 7 manda conservar según los criterios de la Comissió Tècnica lo relevante a efectos asistenciales (incluidas las voluntades anticipadas), epidemiológicos, de investigación u organización, y a efectos judiciales. El 8 atribuye la decisión a la dirección médica del centro sanitario, a propuesta del facultativo y previo informe de la unidad de gestión de la historia clínica. El 9 hace responsable de la destrucción correcta a quien custodia.
- **[Hecho, por mi cuenta]** Hoy he leído la página consolidada `https://www.boe.es/buscar/act.php?id=BOE-A-2001-2353` a través de un resumen automático: coincide en los apartados 4, 6, 7, 8 y 9; dice que el artículo se actualizó por última vez el 10/06/2010 y la página el 28/11/2025, y que solo lo modificó la Ley 16/2010. Un resultado de búsqueda de fuentes secundarias hablaba de «10 años» y de «20 años tras la muerte del paciente»; parecen venir de la redacción anterior a 2010 y **no los uso**.
- **[Hecho, dato del solicitante]** El cuadro de la Comissió Tècnica de 07-04-2017 (canalsalut.gencat.cat) recomienda 15 años para el «full de curs clínic», pide un registro de historias eliminadas y habla de «centres, serveis i establiments sanitaris». **[A verificar]** No pude abrirlo: el PDF no se deja leer como texto con mis herramientas.
- **[Hecho, resumen automático]** Ley 41/2002, art. 17 (BOE, página «actualizada 01/03/2023»): mínimo de 5 años desde el alta de cada proceso asistencial, también por razones judiciales, epidemiológicas, de investigación u organización del SNS. Uso solo ese dato.
- **[A verificar, sigue abierto]** Si una residencia cuenta como «centro sanitario» a efectos del art. 12 de la Llei 21/2000 y de la Ley 41/2002. Indicio, no prueba: una guía técnica de la Diputació de Barcelona para residencias de gente mayor (resultado de búsqueda, no abierta) remite a la Llei 21/2000 para la custodia de la historia clínica del residente. La Llei 12/2007 de servicios sociales no la he encontrado como fuente de un plazo de conservación del expediente asistencial. **Lo resuelve un abogado con el tipo de centro piloto delante.**

### 1.2 Valoración

| Aspecto | Valoración |
|---|---|
| **Base** | **[Suposición]** Lo que ResidApp guarda de cada residente es sobre todo curso clínico (cierres cotidianos, eventos, valoraciones, indicaciones, constantes) más el informe de derivación a Urgencias, que se parece a un informe de alta. Para el curso clínico la ley solo da un permiso de destruir a los 5 años y la Comissió Tècnica recomienda 15; para los documentos del apartado 4 el mínimo es 15. Un plazo único de 15 años evita destruir antes de tiempo algo que la ley manda conservar. |
| **Por qué es «conservador»** | Lo es en el sentido de **riesgo legal por destruir demasiado pronto**. No lo es en el sentido de **minimización** (art. 5.1.e RGPD: «no más tiempo del necesario»). Se justifica porque la necesidad la fija una obligación legal y una recomendación técnica oficial, no la comodidad. Debe quedar escrito así en el registro de actividades. |
| **Contar desde la baja, no desde el alta de cada proceso** | La ley cuenta desde el alta de **cada proceso asistencial**; cada episodio de ResidApp se cierra antes de la baja del residente. Contar desde la baja da siempre una fecha igual o posterior: **más** conservación que la mínima, nunca menos. |
| **Límite 1: no hay expurgo durante la estancia** | Con este plazo, los episodios antiguos de un residente que lleva 20 años en el centro no se destruyen nunca mientras viva allí. Es el precio de la sencillez. **[Decidir]** DPO: aceptarlo, o pedir expurgo por episodio más adelante. |
| **Límite 2: un solo plazo para todo lo clínico** | ResidApp no separa «documentos de 15 años» de «documentos de 5». Si el centro decide acortar, solo puede hacerlo si antes se etiqueta cada tabla con su categoría legal (apartado 2.2). Acortar todo a 5 años destruiría documentos del apartado 4 antes de tiempo. |
| **Límite 3: conservar no es poder consultar** | **[Hecho]** Un residente dado de baja queda `INACTIVE` y la autorización lo trata como «no activo» (`0041_baja_y_suspension_residente.sql:5-6`); la lista de Administración es de solo lectura y sin datos clínicos (`SqlResidentStatusDirectory.cs:9-12`). **[A verificar]** Si hoy alguien puede leer lo clínico de un residente dado de baja. Si nadie puede, el derecho de acceso (art. 15 RGPD; Ley 41/2002) se atiende hoy sin vía en la aplicación: hace falta una consulta o exportación **antes de producción**. |
| **Límite 4: la custodia es del centro** | La ley atribuye la custodia a la dirección del centro (art. 12.1 y 12.9). ResidApp guarda una copia como encargada. Al terminar el contrato, el centro debe recibir la historia en un formato utilizable y ResidApp borrar su copia (art. 28.3.g RGPD). Es contenido del contrato de encargado. |
| **Límite 5: el plazo no es una orden de borrar** | «Vencido» significa que el centro **puede y, pasado un tiempo razonable, debe** decidir (art. 5.1.e). Una lista de vencidos que nadie revisa es un riesgo. Hace falta persona responsable y revisión periódica por escrito. |

### 1.3 Cómo lo presenta ResidApp

**[Hecho]** En el árbol de trabajo que he leído, `ResidentDischarge.cs:18-25` ya fija `RetentionYears = 15` y remite a este documento; `Manual.cshtml:433` ya dice «15 años desde la baja». Siguen diciendo 5 años: `0041_baja_y_suspension_residente.sql:4` (comentario de cabecera) y `flujo-de-trabajo.html:819, 1775, 1804, 1810`. `conservar_hasta` es `DATE NOT NULL` y **no se puede modificar** (`TR_bjr_guard`, `0041:49`).

Cómo decirlo a la residencia (texto sugerido para el contrato y la política):

> ResidApp propone, por defecto, conservar la documentación clínica de cada residente 15 años desde su baja. Es un plazo prudente, no una instrucción legal. El centro, como responsable, puede fijar otro, siempre dentro de lo que la ley permita, y ResidApp lo aplicará según su instrucción por escrito. ResidApp no borra nada por sí sola: avisa de los plazos vencidos y el centro decide.

Consecuencias técnicas **[Suposición]**, para cuando se construya:

- Si el centro puede cambiar el plazo, hay que poder cambiar `conservar_hasta` de las bajas ya registradas, o calcularlo al leer a partir de un plazo por centro (como hace `0085`, cuyo plazo vive en la base y no en el código, `0085_purga_envios_correo.sql:8`). Con la columna inmutable, un cambio de plazo no llega a las bajas existentes.
- Hace falta una **retención reforzada** («legal hold»): una marca de «no expurgar» por residente cuando hay una reclamación, una inspección o una investigación abierta (el borrador `historial-tras-traslado-04` ya habla de investigación abierta). Tabla de solo inserción con alta y levantamiento.

### 1.4 Reactivación

**[Hecho]** Una baja guarda `reactivada_en`; solo se puede rellenar una vez (`0041:41-51`). A lo sumo hay una baja abierta por residente (`UX_bjr_open`, `0041:38`). Un residente reactivado vuelve a `ACTIVE`, tiene episodio y ubicación nuevos, y si vuelve a salir se crea **otra fila** de baja con su propio `conservar_hasta`. La lista de Administración ya descarta las bajas reactivadas (`SqlResidentStatusDirectory.cs:22-26`: `reactivada_en IS NULL` y `resident.estado = 'INACTIVE'`).

Regla propuesta:

1. Solo cuenta la **baja abierta** (la última, no reactivada). El `conservar_hasta` de una baja reactivada no sirve para nada.
2. El plazo de lo clínico del residente corre desde esa **última** baja, **también para los episodios de estancias anteriores**. Es más conservación de la mínima, pero evita separar datos de estancias distintas (la basal y la identidad son del residente, no del episodio).
3. Un residente `ACTIVE`, o con suspensión abierta, nunca es candidato.
4. La lista de vencidos y el futuro expurgo usan la misma consulta que el listado actual, para no divergir.

### 1.5 Lo que no es historia clínica

| Dato | Qué propongo decir | Quién decide | Notas |
|---|---|---|---|
| Comunicados a las familias (`comunicaciones_familiares`) | **[Suposición]** Mismo plazo que lo clínico, por defecto. | DPO; pregunta a CJ | No puedo separar qué comunicados llevan información clínica. Si el DPO los considera fuera de la historia, pueden tener plazo propio, pero el borrado exigiría el mismo mecanismo que lo clínico (tablas inmutables). |
| Citas y solicitudes de cita del Portal Familiar | Plazo propio, **sin cifra mía**; mientras tanto siguen al residente. | DPO | `comentario` (300) lo escribe la familia y es inmutable (`0057:30, 103-105`); `citas_cambios.motivo` (500) e `citas_solicitudes_cambios.detalle` (500) también (`0054:121, 131`; `0057:71, 81`). Pueden contener datos de salud. |
| Foto | Ya decidido: se borra al dar de baja. | Hecho | `Privacy.cshtml:132`. |
| Autorizaciones, vínculos y contacto urgente | Siguen al residente; el nombre del familiar se borra por separado (apartado 3). | DPO | Tablas inmutables, no se pueden borrar. |
| Registro de envíos de correo | Hecho: 12 meses, borrado por `dbo.purgar_envios_correo`. | Hecho | `0085:26-30`. |
| Registros de entrega de Azure Communication Services | Previsto 30 días en un Log Analytics propio de la UE; sin configurar (paso B10). | Orlando | `antes-de-datos-reales.md:104`. |
| `eventos_acceso` (con IP), `sesiones`, `eventos_auditoria` | **Sin plazo y sin vía de borrado.** Es el hueco más serio. | DPO y residencia | Apartado 2.8. |
| Copias de seguridad de Azure SQL | Por defecto 7 días de restauración a un punto en el tiempo, configurable de 1 a 35, más la retención a largo plazo si se activa. **[A verificar]** el valor real del servidor de producción. | Orlando | Microsoft Learn, «Automated backups in Azure SQL Database» (resumen de búsqueda, 2026-11-03). |
| Cuentas de personal y de familias | Personal: se suspenden, no se borran (apartado 4). Familias: se anonimiza el familiar a petición (apartado 3). | DPO | |

---

## 2. Diseño del expurgo futuro (no se construye ahora)

### 2.1 Por qué no basta con «DELETE»

**[Hecho]** Tres obstáculos distintos, que se resuelven de formas distintas:

1. **Triggers de dos tipos.** Muchas tablas usan `INSTEAD OF UPDATE, DELETE` con `THROW` incondicional (`TR_audit_immutable`, `0002:727-728`; `TR_ced_immutable`, `0075:35-36`; `TR_fac_immutable`, `0022:104-105`; `TR_rcu_immutable`, `0022:147-148`; `TR_id_immutable`, `0016:56-57`; `TR_ilf_immutable`, `0016:83-84`; `TR_ric_immutable`, `0021:44-45`; `TR_citas_cambios_immutable`, `0054:131-132`; `TR_citas_solicitudes_cambios_immutable`, `0057:81-82`). Otras usan `AFTER UPDATE` con comprobaciones y `INSTEAD OF DELETE` que lanza error (`TR_bjr_guard` y `TR_bjr_no_delete`, `0041:41-55`; `TR_acc_update_guard`, `0023:21-33`; `TR_fam_update_guard`, `0052:24-36`).
2. **Claves ajenas hacia `residentes`.** `eventos_auditoria` tiene `FK_audit_resident` (`0002:718`) y hay 24 apariciones de `REFERENCES dbo.residentes` repartidas en 15 scripts (`0002` incluido); además, las tablas hijas de los eventos llegan a `residentes` por `evento_id`. **No se puede borrar la fila de `residentes` mientras exista la auditoría**: hay que dejar una «lápida» (la fila con la identidad vaciada).
3. **Copias de la identidad fuera de `residentes`.** **[Hecho]** `residentes_identidad_correcciones` guarda nombre y fecha de nacimiento **anteriores y nuevos** de cada corrección, inmutable (`0021:16-45`). `residentes.motivo_inactivacion` (1000, `0002:102`) y `bajas_residente.motivo_texto` (500, `0041:21`) copian el texto libre de la baja. `informes_derivacion` guarda `contenido_json` y `pdf` firmados (`0016:40-42`). `intentos_llamada_familia.contacto` (200) escribe a mano a quién se llamó (`0016:69`). **Una lápida que solo vacíe `residentes.nombre_visible` dejaría el nombre en otros sitios.**

### 2.2 Principios

1. **Catálogo de expurgo.** Una lista, en el repositorio, de **cada tabla** alcanzable desde `residentes` o desde un episodio, con tres datos: qué se hace (borrar filas, vaciar columnas, conservar), categoría legal (A: documentos del art. 12.4; B: curso clínico; C: no clínico) y orden. Se genera a partir de `sys.foreign_keys` y se acompaña de **una prueba automática que falla si aparece una tabla nueva que referencia `residentes` o `eventos_asistenciales` sin entrada en el catálogo**. Esto es lo único que conviene hacer ya: cuesta poco y evita que cada vertical nuevo haga imposible el expurgo.
2. **Nada de «desactivar los triggers».** `DISABLE TRIGGER` afecta a todas las conexiones mientras dura: otro proceso podría modificar a la vez tablas que deben ser inmutables, y un fallo a medias deja la protección apagada. **No lo recomiendo.**
3. **Permiso por constancia en la misma transacción**, como se hace en `0075` y `0079` (apartado 2.3).
4. **El expurgo es por residente y de una sola vez**, dentro de una transacción. O se borra todo el catálogo de ese residente o nada.
5. **La decisión es de la persona competente del centro; la ejecución es de un procedimiento con permiso aparte**, nunca de la cuenta de la aplicación.

### 2.3 Cómo se permite romper la inmutabilidad

**Patrón existente.** `0075` crea la fila de constancia `cierres_excepcionales_direccion` con `transaccion_id = CURRENT_TRANSACTION_ID()` por defecto (`0075:27`), y `TR_ea_transition_guard` solo admite el cierre excepcional si existe esa fila de **ese episodio en la misma transacción** (`0075:62-64`). Es la idea correcta: la constancia es la «llave» y solo existe dentro de esa transacción.

**Lo que `0075` no demuestra, y no sustituye texto.** Su trigger es de tipo `AFTER UPDATE`: puede aceptar o abortar un cambio de estado. Las tablas del apartado 2.1.1 son `INSTEAD OF`: el trigger **sustituye** a la operación y no hay nada que «dejar pasar»; para permitir un cambio, el trigger debe hacerlo él mismo. El precedente del repositorio es **`TR_dht_guard`** (`0079:20-37`): `INSTEAD OF UPDATE, DELETE` que rechaza todo salvo terminar la petición, y que ejecuta él mismo `UPDATE t SET terminada_en = i.terminada_en ... WITH (FORCESEEK)` (`0079:35-36`). Dentro de un `INSTEAD OF`, una sentencia sobre la misma tabla no vuelve a disparar el trigger (Microsoft Learn, «CREATE TRIGGER», resumen de búsqueda de hoy; **[A verificar]** probándolo en Azure SQL).

**Diseño propuesto** (no construido):

1. Tabla `expurgos_constancia`, solo inserción, con `transaccion_id` por defecto `CURRENT_TRANSACTION_ID()`, centro, residente (UUID), referencia de la decisión, baja y `conservar_hasta`, cuenta o usuario de base que ejecuta, versión del catálogo y recuento esperado por tabla.
2. Una función `dbo.fn_expurgo_autoriza(@centro_id, @residente_id)` que devuelve 1 si hay una constancia de **ese residente** en la **transacción actual**. El `WITH (FORCESEEK)` es necesario (lección de `0055` y `0056`).
3. En cada trigger del catálogo, una rama: `IF dbo.fn_expurgo_autoriza(...) = 1` entonces el trigger hace el `DELETE` o el `UPDATE` de columna él mismo; si no, el `THROW` de siempre. La rama solo cubre `DELETE` y, en las lápidas, el `UPDATE` de las columnas de identidad.
4. La constancia solo la inserta un procedimiento `dbo.expurgar_residente` con `EXECUTE AS OWNER`, con `EXECUTE` concedido a un rol aparte (por ejemplo `residapp_expurgo`) y **ningún permiso de `INSERT` sobre la tabla** para la aplicación ni para la tarea programada. Es el mismo reparto que `0083` (rol sin miembros) y `0085` (`EXECUTE AS OWNER`, `0085:17-19`).
5. Un solo procedimiento y un solo rol: la cuenta de la aplicación (`residapp_app`) no puede expurgar aunque se vea comprometida, porque no puede crear la constancia.

### 2.4 Orden de las operaciones

Todo dentro de una transacción con `XACT_ABORT ON`; si cualquier recuento no coincide, `THROW` y no se hace nada.

1. **Comprobar las condiciones**: residente `INACTIVE`, última baja no reactivada y `conservar_hasta` pasada, sin suspensión abierta, sin retención reforzada, y decisión registrada.
2. **Bloquear** la fila del residente (`UPDLOCK, ROWLOCK`, como en `SqlResidentFamilyRepository.cs:367-369`).
3. **Contar** las filas de cada tabla del catálogo.
4. **Insertar la constancia** con esos recuentos.
5. **Borrar de las hojas hacia el tronco**: hijos de los eventos (indicaciones, valoraciones, constantes, protocolo urgente, intentos de llamada, informes de derivación con su PDF, cierres excepcionales), eventos, cierres cotidianos, basales y borradores, traslados, declaraciones de historial, comunicados, citas y solicitudes con sus cambios, foto y consentimiento, suspensiones, correcciones de identidad, bajas.
6. **Vínculos con familiares**: autorizaciones, contacto urgente, vínculos. Los `familiares` que se quedan sin ningún vínculo pasan por la rutina del apartado 3 (iniciativa del centro).
7. **Lápida de `residentes`**: nombre y datos de identidad vaciados con marcas fijas, motivo de la baja con texto fijo, estado `INACTIVE`. `TR_res_identity_guard` (`0021:48-64`) exige una corrección de identidad que coincida; también necesita la rama de constancia.
8. **Verificar** que no queda nada del residente salvo la lápida, la auditoría y la constancia, consultando `sys.foreign_keys`; si queda algo, `THROW`.
9. **Copias fuera de la base** (apartado 2.9).

### 2.5 Registro de historias eliminadas

La Comissió Tècnica pide un registro **[Hecho, dato del solicitante; contenido exacto A verificar]**. Propuesta:

- **En ResidApp**: la propia `expurgos_constancia`, con UUID del residente, centro, fecha de la baja, plazo, fecha del expurgo, referencia de la decisión, quién ejecutó, versión del catálogo y recuento por tabla. **Sin nombre ni contenido.**
- **En el centro**: su propio libro de registro, que sí puede llevar el número de historia o la identificación que la residencia use, porque la residencia es la responsable y puede relacionarlo con su archivo en papel. Así ResidApp no tiene que guardar un nombre para demostrar que destruyó algo.
- **[Decidir]** DPO real: si el registro de ResidApp debe llevar algo más para atender a quien alegue que no se destruyó (por ejemplo, el año de nacimiento).
- La huella SHA-256 de cada PDF destruido puede quedar en la constancia como prueba de qué existió. **[Suposición]** Una huella no permite recuperar el documento; **[Decidir]** DPO si la considera dato personal.

### 2.6 Quién decide

**[Hecho, dato del solicitante]** Art. 12.8: la dirección médica del centro sanitario, a propuesta del facultativo y previo informe de la unidad de gestión de la historia clínica.

- **[A verificar]** Si el perfil «Dirección Clínica» de ResidApp es esa dirección médica en una residencia. Puede que el centro tenga un director médico, un director sin perfil clínico o ninguno. **La residencia y un abogado lo definen**; ResidApp no lo presupone.
- La decisión de expurgar un residente es una **instrucción por escrito del centro** (art. 28.3.a RGPD) que ResidApp conserva. Lo ideal es que quede en la aplicación (una propuesta por Dirección Clínica) y en un acta del centro.
- **Cuatro ojos.** Quien ejecuta el procedimiento, sea personal de ResidApp, no es quien decide. Ejecutar un borrado irreversible sobre datos de otro responsable exige que dos personas de ResidApp lo hagan y lo anoten.

### 2.7 El PDF firmado

- **[Hecho]** `informes_derivacion` guarda `contenido_json`, el PDF y sus huellas, inmutable (`0016:38-57`). El PDF lleva dentro el nombre del residente y, de quien firma, nombre, perfil y «cuenta {identificador}» (`ReferralReportPdfRenderer.cs:80-87`). No encuentro que el PDF incluya datos del familiar (`ReferralReportPdfRenderer.cs` no los menciona); **[A verificar]** con una prueba que lo confirme, y que `contenido_json` tampoco.
- **En vida del plazo no se toca**: cambiarlo invalidaría la huella y la firma.
- **Al expurgar** se borra la fila entera (PDF y JSON), porque el documento es la historia y no se puede «anonimizar sin romperlo». La constancia conserva solo la huella, si el DPO lo acepta.
- **No se hace nada con el PDF cuando se anonimiza a un familiar** ni cuando se cierra la cuenta de un firmante: son parte de la historia (apartado 4).

### 2.8 Auditoría y registros técnicos

| Tabla | Qué contiene de personal | Qué se propone | Quién decide |
|---|---|---|---|
| `eventos_auditoria` | Cuenta, perfil, residente (UUID), hora; en algunas filas `justificacion` con texto libre (`0038:14`; `0077:9`) | Las filas no se borran con el residente. Cuando el residente se expurga y su lápida pierde la identidad, la fila deja de identificar al residente (**[Suposición]**: queda solo el UUID), pero sigue identificando a la persona del personal. Su plazo es el de la seguridad o el de la prueba de accesos. **Solo el texto libre de `justificacion` se sustituye por una marca fija** (opción C de `historial-tras-traslado-05`). | DPO y residencia |
| `eventos_acceso` | Cuenta, tipo, hora, **IP** (`0062:32-47`); nunca el usuario tecleado (`0062:10-11`) | Plazo propio. Opción gradual: primero poner `ip` a NULL (se pierde poco), después borrar filas. Ambas exigen modificar `TR_eventos_acceso_no_update` y `_no_delete` (`0062:49-55`) con la rama de constancia. | DPO y residencia |
| `sesiones` | Cuenta, horas, motivo de cierre (`0061:13-25`) | Plazo propio, borrado de filas cerradas por antigüedad. Requiere tocar `TR_sesiones_no_delete` (`0061:45-47`). | DPO y residencia |

- **[Decidir, la pregunta clave]** ¿La auditoría de accesos a la historia clínica forma parte de la historia (mismo plazo) o es un registro de seguridad con plazo propio? Es la pregunta abierta de `historial-tras-traslado-05`. **Sin respuesta no se puede fijar ningún plazo.**
- **No pongo cifras.** Los plazos de estos registros no salen de una norma que yo haya leído. Antes de datos reales, el DPO fija uno por tabla y la política lo dice. Construir la vía de borrado puede esperar, pero **el plazo y la fecha prevista de la vía deben figurar por escrito**.
- La auditoría no puede «bloquearse» en el sentido del art. 32 LOPDGDD: no hay ningún mecanismo que impida su visualización (la ve Administración, `AdministrativeAudit.cs:17-30`). Si el DPO exige bloqueo, hay que diseñarlo.

### 2.9 Copias de seguridad y copias fuera de la base

- **Copias de Azure SQL.** Contienen lo expurgado hasta que caduquen. **[Hecho, resumen de búsqueda]** Por defecto se conservan para restaurar a cualquier punto de los últimos 7 días (configurable de 1 a 35) y existe retención a largo plazo hasta 10 años si se activa. Nada se purga de las copias por separado. Decir esto a la persona afectada y documentarlo.
- **Restaurar una copia antigua deshace los expurgos y las anonimizaciones posteriores.** Debe existir un **registro fuera de la base** (solo identificadores y fechas) para **reaplicarlos** tras cualquier restauración. Procedimiento escrito.
- **Fuera de ResidApp.** Hojas de acceso impresas, correos, documentos en papel y exportaciones. Los trata el centro con su procedimiento.
- **Subencargados.** Azure Communication Services y su registro de entrega (30 días previstos): el envío de un correo deja la dirección allí. Art. 19 RGPD obliga a notificar a los destinatarios una supresión «salvo que sea imposible o exija un esfuerzo desproporcionado». **[A verificar]** si un encargado cuenta como «destinatario» a estos efectos.

### 2.10 Condiciones antes de construirlo

**Antes de datos reales** (barato):

1. Catálogo de expurgo y prueba que falla si falta una tabla (2.2.1).
2. Plazo escrito para `eventos_acceso`, `sesiones`, `eventos_auditoria`, con fecha prevista de la vía de borrado.
3. Copias: retención verificada y documentada (2.9).

**Antes de producción:**

4. Dictamen sobre la aplicabilidad del art. 12 a la residencia y sobre quién es la «dirección médica».
5. Plazo por categoría (A, B, C) y política del centro sobre acortar.
6. Retención reforzada, plazo configurable por centro y propietario de la lista de vencidos con revisión periódica por escrito.
7. Vía de consulta y exportación (derecho de acceso y fin de contrato).
8. Contrato de encargado con la instrucción de borrado y la devolución.
9. Contenido del registro de historias eliminadas contrastado con el texto de la Comissió Tècnica.
10. Evaluación de impacto con el expurgo y la restauración de copias incluidos.
11. Ensayo completo del procedimiento sobre una copia de datos de prueba y verificación de que ninguna tabla conserva identidad.

**Recomendable, más adelante:** expurgo por episodio durante la estancia; evaluar el borrado por destrucción de claves por residente («crypto-shredding»), que hoy no encaja con 60 tablas estructuradas y triggers que comparan columnas.

---

## 3. Anonimización de un familiar

### 3.1 Veredicto

**Admisible con condiciones.** El diseño cubre bien lo visible (datos de contacto, acceso, contacto urgente, sesiones, credenciales). Tiene un desacuerdo de fondo (art. 32 LOPDGDD), varios huecos concretos y una confusión de nombre: lo que se hace **no es anonimizar en sentido estricto** mientras exista `sujeto_externo` y el resto de vínculos (3.9).

### 3.2 Lo que valido

- **UPDATE y no DELETE**, en una transacción: correcto; `TR_fam_no_delete` (`0022:45-47`) lo exige y se conserva la integridad (`residentes_familiares`, `envios_correo`, `citas` apuntan a la fila).
- `familiares.nombre_visible` y `telefono` a una marca fija: el esquema los exige no vacíos (`CK_fam_nombre`, `CK_fam_telefono`, `0022:29-30`), así que una marca fija es la única opción.
- `correo` a NULL: permitido (`0022:31`).
- Columnas `anonimizado_en` y `anonimizado_por_cuenta_id`: correctas. **Añado:** ambas nulas o ambas con valor (`CHECK`), y un `CHECK` o trigger que, una vez puesta la marca, impida volver a cambiar nombre, teléfono o correo. `TR_fam_update_guard` (`0052:24-36`) hoy no lo impide.
- Revocar autorizaciones, quitar contacto urgente, suspender la cuenta, borrar credenciales y passkeys, anular sesiones y poner `cuentas.nombre_visible` a NULL: coherente con lo que la base permite (`cuentas.nombre_visible` admite NULL, `0023:14-18`; `credenciales` y `passkeys` no tienen triggers de borrado, `0062:15-30`, `0066:13-25`).
- Auditoría `FAMILY_MEMBER_ANONYMIZE` solo con identificadores: correcto. Una acción nueva no aparece en la pantalla de Administración hasta añadirla a la lista cerrada con su etiqueta (`AdministrativeAudit.cs:17-30`, `AuditoriaModels.cs:88-95`).
- Solo la **Administración principal** (`AdministrationScopeApplicationService.cs:15`): bien. Es una acción de impacto alto; acotarla a quien ya gestiona el centro entero es razonable.
- Para el personal, sin función y con procedimiento: de acuerdo (apartado 4).

### 3.3 Desacuerdos y correcciones, de mayor a menor gravedad

**1. Bloqueo (LOPDGDD, art. 32): el diseño borra sin guardar nada.** **[A verificar, resumen del BOE de hoy: `BOE-A-2018-16673`, actualizado el 27/12/2025]** El art. 32 obliga al responsable a **bloquear** los datos cuando los rectifica o suprime: identificarlos y reservarlos, con medidas que impidan su tratamiento incluida la visualización, y ponerlos solo a disposición de jueces, Ministerio Fiscal o administraciones competentes (la autoridad de protección de datos incluida), durante el plazo de prescripción de las responsabilidades, y destruirlos al terminar. Si el sistema no puede bloquear, dice el mismo artículo que se haga una copia segura con evidencia de autenticidad y de la fecha del bloqueo. Las autoridades pueden fijar excepciones. Un análisis de Cuatrecasas (secundario) lee que la AEPD entiende que el bloqueo excluye el borrado material.

**Lo que implica:** si anonimizamos sin dejar copia, el centro no podrá mostrar a un juez quién era la persona que figuraba (por ejemplo, para acreditar a quién se llamó, o quién era representante). Dos salidas, **que decide el DPO**:

- **(A) Irreversible**, como en el diseño, con una decisión escrita de que los datos de contacto de un familiar no generan responsabilidades que obliguen a bloquear, o con una excepción de la autoridad. Riesgo: medio.
- **(B) Con bloqueo.** Antes del `UPDATE`, copiar nombre, teléfono, correo y vínculos a una tabla `familiares_bloqueados` de solo inserción, sin lectura para ningún rol de la aplicación, consultable solo por una persona con permiso aparte, con `bloqueado_hasta` y borrado por procedimiento al vencer. Es lo que dice la ley si el DPO no ve excepción. Tiene el coste de que la «supresión» sea, durante ese plazo, un bloqueo. La información al interesado debe decirlo.

**Mi recomendación:** construir (B) salvo que el DPO real diga por escrito que (A) basta. No construir (A) sin esa respuesta, porque es irreversible. El mismo asunto afecta a la «opción C» de `historial-tras-traslado-05` (sustituir el texto de un motivo): el texto antiguo tampoco se guarda.

**2. Cuenta compartida entre centros.** **[Hecho]** Una misma cuenta puede tener un familiar en cada centro (`0052:9-11`) y la aplicación se niega a suspender o renombrar una cuenta con perfiles vigentes en otros centros (`SqlProfessionalAccountRepository.cs:125-131` para el estado; `98-104` para el nombre; error `ACCOUNT_OTHER_CENTERS`). El diseño suspende la cuenta y pone `nombre_visible` a NULL sin mirar eso. **Corrección:** si la cuenta tiene perfiles vigentes en otro centro, la anonimización de este centro **revoca solo el perfil `FAMILIAR` de este centro**, anonimiza la fila `familiares` de este centro y **no toca la cuenta global** (ni estado, ni nombre, ni credenciales). La cuenta pertenece a otra residencia también responsable; la residencia donde se pidió la supresión no puede decidir por ella. Lo que sí se debe comunicar: qué ha hecho y qué no. **[Decidir]** DPO.

**3. Textos libres inmutables que el diseño no cita.** Se suman a los dos que ya se conocen (`intentos_llamada_familia.contacto` y comunicados):

| Texto | Dónde | Quién lo escribe | Se puede tocar |
|---|---|---|---|
| `relacion` (100) | `residentes_familiares`, `0022:54` | Administración | Sí, `TR_rfa_update_guard` solo bloquea las claves; pero **no** si el vínculo está desvinculado (`0045:34-35`) |
| `desvinculado_motivo` (500) | `0045:12` | Administración | No: un vínculo desvinculado no admite ningún cambio |
| `motivo` de autorizaciones (1000) | `familiares_autorizaciones_cambios`, `0022:88` | Administración | No (`INSTEAD OF`, `0022:104-105`) |
| `motivo` de «sin contacto» (500) | `residentes_contacto_urgente`, `0050:11` | Administración | No (`0022:147-148`) |
| `comentario` (300) | `citas_solicitudes`, `0057:30` | **La propia familia** | No (`0057:103-105`) |
| `detalle` (500) y `motivo` (500) | `citas_solicitudes_cambios`, `citas_cambios` | Administración o familia | No |
| `contacto` (200) | `intentos_llamada_familia`, `0016:69` | Personal | No (`0016:83-84`) |
| Comunicados a la familia | `comunicaciones_familiares` | Personal | No |

**Consecuencia para el diseño:** al revocar autorizaciones y quitar el contacto urgente, la anonimización **escribe filas nuevas con un motivo obligatorio** (`CK_fac_motivo`, `0022:96-98`; `CK_rcu_motivo`, `0050:18-20`). Ese motivo debe ser un **texto fijo** de la aplicación («Anonimización del familiar»), nunca el motivo que escriba el usuario, porque quedará inmutable.

**4. La marca de confirmación del correo.** `correo_confirmado_en` y `correo_confirmado_por_cuenta_id` (`0084:19-26`). El trigger `TR_fam_correo_confirmado` ya las pone a NULL si el correo cambia o se quita (`0084:35-42`), pero conviene ponerlas a NULL explícitamente en el mismo `UPDATE` para no depender del orden. **[Hecho]** El trabajo de aviso por correo lee `familiares.correo` al enviar (`0083:35`); hay una ventana mínima en la que un aviso en curso puede salir después de anonimizar. Documentarlo.

**5. Cierre de sesiones.** El motivo de cierre de `sesiones` tiene lista cerrada (`LOGOUT`, `INACTIVIDAD`, `CADUCIDAD`, `CAMBIO_IDENTIDAD`, `RECUPERACION`, `CIERRE_PROPIO`; `0064:19-22`). No hay un valor para «cuenta anonimizada». **Basta con suspender la cuenta**: la sesión se rechaza en la petición siguiente porque la cuenta no está activa (`SqlSessionStore.cs:31-33`). Cerrarlas explícitamente exige añadir un motivo al `CHECK`.

**6. Guardas en los demás caminos.** Un familiar anonimizado no debe poder: editarse (`FAMILY_MEMBER_UPDATE`), vincularse a otro residente (`FAMILY_MEMBER_LINK`), recibir acceso (`GrantPortalAccessAsync` crea una cuenta nueva si `cuenta_id` es NULL, `SqlResidentFamilyRepository.cs:323-336`, así que sin guarda se crearía una cuenta para el «Familiar anonimizado»), ni **reactivar su cuenta** (`ChangeStatusAsync`, `SqlProfessionalAccountRepository.cs:139-143`, no comprueba nada de esto). También hay que anular la **invitación viva** de la cuenta (`UX_invitaciones_acceso_viva`, `0063:32`; solo se puede anular de NULL a un valor, `0063:35-50`): una invitación sin usar podría fijarle una contraseña.

**7. Contacto urgente y representante.** Quitar a un familiar que es el **único** contacto urgente deja al residente sin contacto (se inserta una fila `SIN_CONTACTO`, `SqlResidentFamilyRepository.cs:391-397`), y la derivación a Urgencias exige llamar a la familia. Quitar al representante legal deja sin constancia en ResidApp de quién representa. El aviso debe **listar** esos casos antes de confirmar, y el procedimiento debe pedir **designar sustituto antes**. **[Decidir]** DPO: si ser representante legal de un residente cuya documentación está en plazo es motivo para **denegar** la supresión (art. 17.3.b y e RGPD: obligación legal; formulación o defensa de reclamaciones).

**8. Retención reforzada.** No anonimizar mientras haya una reclamación, queja o investigación abierta que afecte a ese familiar, salvo que el DPO lo autorice por escrito.

**9. Concurrencia.** Bloquear la fila del familiar y las filas de cada residente afectado **en un orden fijo** (por id) para evitar interbloqueos, y repetir la comprobación de «contacto urgente vigente» con el mismo control de versión que usa la designación (`expectedDesignations`, `SqlResidentFamilyRepository.cs:376-380`).

### 3.4 La marca fija

- **Una sola constante** para todos los familiares (por ejemplo, `Persona anonimizada` para el nombre y `-` para el teléfono), no un código por persona: un código por persona es un seudónimo que permite seguir distinguiendo y relacionando.
- **No incluir la fecha ni el id**: ya están en `anonimizado_en` y en la auditoría.
- `CHECK` en la tabla: `anonimizado_en IS NULL OR (nombre_visible = <marca> AND telefono = <marca> AND correo IS NULL)`. Así la base garantiza el estado, no solo el código.
- Cualquier validación de formato del teléfono en lectura no debe romperse con la marca. **[A verificar]** en `FamilyMember.cs`.

### 3.5 El motivo de lista cerrada

La lista propuesta («la persona misma», «su representante», «otro motivo») registra **quién pide**, no por qué. Está bien, y no admite texto libre. Propongo cuatro valores:

| Valor | Cuándo |
|---|---|
| `PETICION_DEL_INTERESADO` | La persona misma lo pide |
| `PETICION_DE_REPRESENTANTE` | Lo pide quien la representa (hay que acreditarlo) |
| `PETICION_DE_ALLEGADOS_O_HEREDEROS` | La persona ha fallecido y lo piden allegados o herederos (LOPDGDD, art. 3: permite pedir acceso, rectificación o supresión salvo prohibición expresa; **[A verificar]** resumen del BOE de hoy) |
| `INICIATIVA_DEL_CENTRO` | Error de alta, ya no hay vínculos vigentes, o decisión del centro |

«Otro motivo» queda cubierto por `INICIATIVA_DEL_CENTRO`. **Dónde guardarlo:** `eventos_auditoria.proposito_codigo` (`NVARCHAR(32)`, nullable) **[Hecho]**: la única restricción sobre esa columna es para `CLINICAL_DETAIL_READ` (`0038:21-31`), así que no hace falta cambiar el esquema; y la auditoría no tiene lista cerrada de acciones (`0077:9`). Es un uso distinto del nombre de la columna («propósito»); alternativa equivalente: una columna `anonimizado_motivo_codigo` con `CHECK` en `familiares`. **No guardar quién es el solicitante, ni su documento, ni la fecha de la petición**: eso va al registro de derechos del centro.

### 3.6 Texto de aviso antes de confirmar

Español llano; el texto de pantalla de la versión definitiva lo validará el DPO. Versión en catalán si se pide.

> **Anonimizar a este familiar**
>
> Vas a borrar de ResidApp los datos de contacto de **[nombre]**. **Esto no se puede deshacer.**
>
> **Qué pasará** (todo a la vez; si algo falla, no se hace nada):
> - Se borran su nombre, su teléfono y su correo. Su ficha pasa a llamarse «Persona anonimizada».
> - Pierde el acceso al Portal Familiar: se revocan sus autorizaciones y se borran sus claves. No podrá volver a entrar.
> - Deja de ser contacto urgente. **[Si es el único contacto de algún residente]** Estos residentes se quedarán sin contacto urgente: [lista]. Designa a otra persona antes de seguir.
> - **[Si es representante legal]** Consta como representante de: [lista]. ResidApp ya no sabrá quién les representa.
> - **[Si tiene cuenta en otro centro]** Su cuenta sigue activa en otro centro; aquí solo se le quita el acceso.
>
> **Qué no cambia:**
> - Lo que el personal escribió a mano (por ejemplo, a quién se llamó en una derivación, o un comunicado) y los informes ya firmados: forman parte de la historia del residente y se conservan.
> - Los registros de entradas y de actividad de su cuenta: sirven para la seguridad y no se pueden borrar. Quedan sin su nombre.
> - Lo que esté en papel, en correos o en tu propio archivo: lo tratas tú según el procedimiento de tu centro.
> - **[Si se opta por el bloqueo (B)]** Una copia de sus datos queda apartada, sin que nadie pueda verla, solo para ponerla a disposición de un juez o una autoridad durante [plazo], y después se destruye.
>
> **Antes de confirmar:**
> - Comprueba la identidad de quien lo pide.
> - ¿Quién lo pide? ( ) La propia persona  ( ) Su representante  ( ) Sus allegados o herederos  ( ) El centro
> - No escribas nombres ni motivos: la lista es todo lo que se guarda.
> - [ ] He comprobado la identidad y entiendo que no se puede deshacer.
> - Recuerda: tu centro debe contestar por escrito en el plazo de un mes (ampliable) y anotar la petición en su registro de derechos.
>
> [Anonimizar] [Cancelar]

Mensaje al terminar: «Familiar anonimizado. Anótalo en el registro de derechos de tu centro.» Sin nombre en el mensaje: los mensajes de una pantalla a la siguiente viajan en la cookie temporal de la aplicación (`politica-privacidad-portal.md`, B3.1, fila TempData).

### 3.7 `sujeto_externo` (el identificador de acceso)

- **[Hecho]** No se puede cambiar: `TR_acc_update_guard` (`0023:21-33`, línea 26). Lo elige quien da el acceso y admite letras, dígitos y `. _ - @` (`ProfessionalAccount.cs:18-19, 41-42`); es decir, **puede ser un nombre, un correo o un teléfono**. Aparece solo en `cuentas` y en la hoja de acceso impresa; no se guarda en `eventos_acceso` (`0062:10-11`) ni en la auditoría. En los PDF solo sale el de quien firma, personal (`ReferralReportPdfRenderer.cs:81-83`). La seguridad por filas lo compara por valor (`0030_rls_centro.sql:41`), así que un cambio futuro debe respetarlo.
- **Por tanto, para un familiar, `sujeto_externo` es el último dato que lo identifica de forma directa** si el identificador elegido tenía su nombre o su correo.
- **Recomendación, en este orden:**
  1. **Prevención (la que resuelve el problema para todos los casos nuevos).** Que ResidApp **genere** el identificador de acceso de un familiar (un código aleatorio, sin nombre, correo ni teléfono) en lugar de aceptar texto libre. Es un cambio de código en el alta del acceso del familiar; lo decide Orlando. Con eso, `sujeto_externo` deja de identificar y la anonimización puede dejarlo.
  2. **Sustitución controlada** solo si el DPO lo exige para identificadores ya existentes: ampliar `TR_acc_update_guard` con la rama de constancia en la misma transacción (el patrón del apartado 2.3), de modo que el identificador de una cuenta de familiar anonimizada pueda cambiarse **una sola vez** por un valor aleatorio nuevo. No tiene riesgo de impersonación: la cuenta queda suspendida, sin credenciales y sin sesiones, y el valor nuevo no se reutiliza.
  3. **Aceptar y documentar** si todos los identificadores existentes se generaron sin datos personales.
- **Hoja de acceso impresa:** lleva el identificador y el QR de invitación. Hay que destruirla; es del centro.

### 3.8 Qué hacer con las copias de texto libre

1. **Prevención.** Formularios con lista cerrada o textos cortos; avisos de «no escribas nombres». La opción D de `historial-tras-traslado-05` va en esa línea.
2. **Información al interesado.** Decirle exactamente qué se conserva y por qué (el texto de aviso lo recoge).
3. **Base para conservarlo.** Lo que forma parte de la historia clínica (llamadas de derivación, comunicados) se conserva por obligación legal y por defensa de reclamaciones (art. 17.3.b y e RGPD); y queda **bloqueado** o dentro del plazo de la historia.
4. **Al expurgo del residente** (apartado 2), esos textos se borran con él. Es el momento natural.
5. **Lo que escribió la propia familia** (`citas_solicitudes.comentario`): es el caso más delicado, porque es un dato que ella misma aportó y no es historia clínica. **[Decidir]** DPO: si merece una excepción de borrado de ese campo (rama de constancia en `TR_citas_solicitudes_update_guard`).

### 3.9 ¿Anonimizar basta para atender el art. 17 RGPD?

**No por sí solo.** Cubre la copia «viva» en ResidApp. Para atender la supresión de un familiar hace falta además:

1. **Decidir el bloqueo** (3.3.1) y decir al interesado qué queda bloqueado.
2. **Lo que se conserva por obligación legal** y por qué (art. 17.3.b y e): los textos libres, los informes firmados y la auditoría.
3. **Seguimiento de lo que sigue identificando:** `sujeto_externo` (3.7); `cuenta_id` en `citas`, `citas_solicitudes`, `eventos_acceso` (con IP) y `eventos_auditoria`; el vínculo con el residente (`relacion`, referente, representante). Mientras exista `cuenta_id` y un identificador que apunte a la persona, los datos son **seudonimizados**, no anónimos (considerando 26). Si el identificador es aleatorio y se han borrado las credenciales y el nombre, la persona deja de ser identificable por quien no tenga información externa; **[Suposición]** puede considerarse prácticamente anónimo, salvo por el vínculo con un residente concreto («hija de X»), que alguien que conozca a la familia podría reconstruir. Por eso la comunicación al interesado y la política no deben decir «anonimizado» sin matices, sino «hemos borrado su nombre, teléfono y correo y cerrado su acceso; conservamos, sin su nombre, los registros de [..] por [..]».
4. **Subencargados y copias** (2.9): ACS, Log Analytics, copias de Azure SQL, y copia de seguridad por restauración.
5. **Fuera de ResidApp:** papel, correos, hojas de acceso.
6. **Responder por escrito** en el plazo de un mes (art. 12.3 RGPD, ampliable), anotarlo en el registro de derechos del centro y dejar la **evidencia fuera de la base** (para reaplicar tras restaurar).

---

## 4. Procedimiento escrito para cuentas del personal

**Principio.** No hay función de anonimización para el personal, de acuerdo, por tres razones verificadas: (1) `sujeto_externo` no puede cambiar (`0023:26`) y el nombre y la cuenta del firmante están impresos en los informes de derivación firmados (`ReferralReportPdfRenderer.cs:81-86`); (2) `eventos_auditoria`, `eventos_acceso` y `sesiones` no se pueden borrar ni modificar (`0002:727-728`; `0062:49-55`; `0061:45-47`) y señalan a la cuenta; (3) la identidad de quien intervino en la historia es parte de lo que la historia debe poder acreditar durante el plazo clínico. **[A verificar]** el artículo concreto de la Ley 41/2002 y de la Llei 21/2000 que lo exige. Una petición de supresión de personal se atiende con el procedimiento de abajo y, para lo que la ley obliga a conservar, con la explicación del art. 17.3.b y e.

### 4.1 Cuándo se aplica

La persona deja el centro, cambia de funciones, o pide la limitación o supresión de sus datos. El **momento** (el mismo día de la baja laboral, o antes en caso de baja disciplinaria) lo fija el centro. **[Decidir]** Residencia.

### 4.2 Pasos para la Administración del centro (lo que hace la aplicación hoy)

1. **Suspender la cuenta.** Usuarios, ficha de la cuenta, «Suspender» (acción `ACCOUNT_SUSPEND`, `SqlProfessionalAccountRepository.cs:139-143`). Surte efecto en la petición siguiente: la sesión se rechaza si la cuenta no está activa (`SqlSessionStore.cs:31-33`). Si la persona tiene perfiles vigentes en otro centro, la aplicación no deja suspender (`ACCOUNT_OTHER_CENTERS`, `SqlProfessionalAccountRepository.cs:125-131`): en ese caso se hace solo el paso 2 y se avisa a Plataforma.
2. **Revocar cada perfil** del centro y, si procede, sus unidades (`PROFILE_SCOPE_REVOKE`, `SqlProfessionalAccountRepository.cs:191-208`). Si la persona no tiene ya ningún perfil vigente, no puede actuar en el centro aunque se reactive la cuenta.
3. **Anular la invitación pendiente** de la cuenta, si la hay («Anular la invitación», `Manual.cshtml:608-609`): sin ello, quien la tenga podría fijar una contraseña (aunque la cuenta suspendida no entre).
4. **No cambiar el nombre ni el identificador.** Siguen en los informes firmados y en la auditoría. Cambiar el nombre además está bloqueado si la cuenta tiene otro centro (`SqlProfessionalAccountRepository.cs:98-104`).
5. **Pedir a ResidApp** el paso técnico (apartado 4.3) y **anotar** la petición en el registro de derechos o de incidencias del centro: fecha, quién la pidió, qué se hizo, enlace a la auditoría.
6. Si la persona vuelve a trabajar: reactivar la cuenta, volver a conceder el perfil y crear una invitación (de alta o de recuperación, según tenga o no credenciales, `SqlAccessInvitationRepository.cs:25, 147`).

### 4.3 Pasos para quien mantiene la base (ResidApp)

**[Hecho]** La aplicación no ofrece borrar las passkeys de otra persona: solo las borra la propia persona (`CuentaController.cs:272`) o al usar una invitación de recuperación (`AccesoController.cs:186`). Con la cuenta suspendida y sin perfiles son inservibles, pero siguen almacenadas y, junto con la clave TOTP, que está **en claro** (`0062:7-8`), son material sensible. Por eso este paso técnico:

1. **Comprobar la instrucción por escrito del centro** (art. 28.3.a RGPD) y quién la firma.
2. **Comprobar** que la cuenta está suspendida y sin perfiles vigentes en ninguna parte (para no cortar a la persona en otro centro).
3. **Ejecutar, dentro de una transacción, un script revisado y guardado en el repositorio** (no a mano), con dos personas presentes y sin pegar claves en el chat:
   ```sql
   -- BORRADOR, sin ejecutar. Probar antes en local.
   -- @cuenta: id de la cuenta (nunca el nombre ni el identificador de acceso).
   BEGIN TRANSACTION;
   DELETE FROM dbo.passkeys           WHERE cuenta_id = @cuenta;
   DELETE FROM dbo.credenciales       WHERE cuenta_id = @cuenta;   -- hash de contraseña, clave TOTP y códigos de recuperación
   UPDATE dbo.invitaciones_acceso SET anulada_en = SYSUTCDATETIME()
    WHERE cuenta_id = @cuenta AND usada_en IS NULL AND anulada_en IS NULL;
   -- Comprobar los recuentos de cada sentencia; si no cuadran, ROLLBACK.
   COMMIT TRANSACTION;
   ```
   Las tres tablas lo permiten: `passkeys` y `credenciales` no tienen triggers de borrado (`0066:13-25`, `0062:15-30`) y `invitaciones_acceso` solo admite anular de NULL a un valor (`0063:35-50`).
4. **No tocar** `cuentas.sujeto_externo` ni `cuentas.nombre_visible`, ni `sesiones`, `eventos_acceso`, `eventos_auditoria`.
5. **Registrar fuera de la base** (solo identificadores): fecha, cuenta, quién pidió, quién ejecutó, recuentos. Sirve para reaplicar tras restaurar una copia.
6. **Comprobar** que la cuenta ya no puede entrar (intento controlado en entorno de pruebas, nunca con la persona real).

### 4.4 Límites que hay que decir a la persona

- Su **nombre y su identificador de acceso** seguirán constando en los informes de derivación que firmó y en los registros de actividad (solo se pueden bloquear, no borrar) mientras dure la documentación clínica a la que están asociados.
- Su **IP de entrada** queda en los registros de seguridad hasta que se fije su plazo (apartado 2.8).
- Quién puede **ver** esos registros es el personal con permiso de auditoría: no hay un bloqueo en el sentido del art. 32 LOPDGDD.
- Tiene derecho a acceso y a reclamar ante la autoridad de control (AEPD o APDCAT, **[A verificar]** según el tipo de centro). Los derechos digitales laborales y la información a la plantilla los revisa la asesoría laboral (**[A verificar]** artículos).

---

## 5. Cambios que provoca en otros documentos (no los he modificado)

### 5.1 `docs/privacidad/politica-privacidad-portal.md` y `Privacy.cshtml`

| Dónde | Qué cambia |
|---|---|
| **Parte A, apartado 8, fila «Historia de cuidados…»** (línea 172) | Sustituir «los plazos legales… desde el alta de cada proceso» por: «15 años desde la baja de la persona. Es el plazo que propone ResidApp por defecto; el centro puede fijar otro dentro de lo que la ley permita». Sigue pendiente la confirmación del DPO. Aclarar en una frase que «no se borra nada solo». |
| **Apartado 8, fila «Cuentas del personal y de las familias»** (línea 173) y `Privacy.cshtml:141` | Separar: personal, «se suspenden y no se borran mientras figuren en documentos clínicos firmados»; familias, «a petición se borran el nombre, el teléfono y el correo y se cierra el acceso». |
| **Apartado 8, filas de entradas con IP, auditoría y sesiones** (líneas 174 a 176) | Siguen «sin decidir», pero la nota debe decir que **hoy no se pueden borrar** y que el plazo y la vía están pendientes (2.8). |
| **Apartado 8, filas nuevas** | «Citas y solicitudes de cita», «comunicados a las familias» y, si se confirma, «copias de seguridad: 7 días por defecto de Azure **[A verificar]**». |
| **Apartado 12, «Supresión»** (línea 257) y `Privacy.cshtml:206` | Hoy dice que lo que la ley obliga a conservar «se **bloquea** (se reservan sin usarlos) mientras dure la obligación». **Es impreciso:** conservar por obligación legal y bloquear son cosas distintas. El bloqueo del art. 32 LOPDGDD es el que se aplica **tras** suprimir o rectificar, durante el plazo de las responsabilidades. Redactar así: «Si lo pides, el centro borra tu nombre, teléfono y correo y cierra tu acceso. Lo que la ley manda conservar (la documentación clínica y los registros de seguridad) se conserva mientras dure la obligación. Si ya habíamos corregido o borrado un dato, puede quedar guardado aparte, sin que nadie lo use, solo para un juez o una autoridad, durante [plazo]». |
| **Apartado 12, residentes** | Añadir: «La historia clínica no se borra a petición mientras dure su plazo legal; al terminar, el centro decide si destruirla y deja constancia en su registro». |
| **B1, riesgos 2 y 3** (líneas 299-300) | El riesgo 2 pasa de «la ley parece decir otra cosa» a «plazo por defecto de 15 años decidido por Orlando el 2026-11-03; falta que una persona confirme que una residencia cuenta como centro sanitario». El riesgo 3 queda atendido por este documento para familias y personal; sigue abierto para los registros técnicos. |
| **B4.1** (línea 371) | Quitar «**[A verificar]** el texto literal»: el texto literal del art. 12 se leyó en el PDF del BOE-A-2010-10215 el 2026-11-03. Mantener «[A verificar] si aplica a una residencia». |
| **B4.2** (línea 372) | «Ni el bloqueo ni la supresión… tienen hoy procedimiento»: sustituir por la referencia a los apartados 3 y 4 de este documento y mantener abierto el bloqueo (3.3.1). |
| **D8** (línea 439) | «Supresión y bloqueo… procedimiento y, si hace falta, cambio de código»: decidido por Orlando en parte (familia: función; personal: procedimiento). Queda: la decisión del DPO sobre el bloqueo y la generación automática del identificador de acceso del familiar. |
| **J1** (línea 446) | Cerrada la parte «texto literal». Abierta: aplicabilidad a residencias. |
| **J-nueva** | Art. 32 LOPDGDD y la excepción del art. 17.3; quién es la dirección médica; plazos de los registros técnicos. |

### 5.2 `docs/privacidad/antes-de-datos-reales.md`

- **Comprobación de «como ya hace `0075`».** **Es imprecisa, confirmado.** `0075` no sustituye ningún texto: crea una fila de constancia propia (`cierres_excepcionales_direccion`) y un trigger `AFTER UPDATE` la usa para aceptar un **cambio de estado** (`0075:12-14, 52-66`); además la constancia la inserta la cuenta de la aplicación en su flujo normal, sin permiso aparte. Lo que `0075` aporta es el patrón «constancia en la misma transacción». **[Hecho]** En el momento de leerlo, la línea 42 ya está corregida («`0075` solo aporta el patrón de la fila de constancia en la misma transacción, no sustituye ningún texto»). Falta añadir el matiz de las tablas `INSTEAD OF`: el precedente para dejar pasar un cambio es `TR_dht_guard` (`0079:20-37`).
- **Línea 30** («los plazos legales de conservación… que el agente no pudo confirmar en fuente oficial y dejó "a verificar"»): ya no es cierta. El texto literal se leyó el 2026-11-03. Lo que sigue abierto es si una residencia cuenta como centro sanitario a efectos del art. 12.
- **Línea 86, última frase** («Los plazos de la Llei 21/2000 que el agente leyó… salieron de un resumen automático: confirmarlos con el texto literal»): ya confirmados; sustituir por la pregunta de aplicabilidad.
- **Línea 57** ya dice 15 años; correcto.
- **Sección 2, punto 5** (línea 42): añadir «apartado 2 de `expurgo-y-supresion.md`».
- **Entrada nueva** (o sección 8): «Expurgo y supresión» con las condiciones de 2.10 y las decisiones de la parte B de este documento.
- **Recordatorio para Claude/Orlando:** antes de cargar datos reales, plazo escrito de los registros técnicos y decisión del DPO sobre el bloqueo.

### 5.3 Otros textos que dicen «5 años»

`0041_baja_y_suspension_residente.sql:4` (comentario); `flujo-de-trabajo.html:819, 1775, 1804, 1810`. `Manual.cshtml:433`, `hallazgos.md:428` y `ResidentDischarge.cs` ya dicen 15 años. Para quien lo aplique, y de acuerdo con la regla de pasar por `doc-usuario-residapp`.

---

# PARTE B. A VERIFICAR O DECIDIR

Ordenado por importancia. «Cuándo»: **I** imprescindible antes de datos reales; **P** antes de producción; **R** recomendable.

| N.º | Qué | Quién decide | Cuándo |
|---|---|---|---|
| 1 | **Bloqueo (art. 32 LOPDGDD) al anonimizar a un familiar:** irreversible (A) o con copia bloqueada (B); plazo de bloqueo; excepción de la autoridad; afecta también a la «opción C» del motivo | DPO real o abogado | I |
| 2 | **Plazo de `eventos_acceso` (IP), `sesiones` y `eventos_auditoria`** y si la auditoría de accesos forma parte de la historia | DPO real y residencia | I |
| 3 | **Identificador de acceso del familiar generado por ResidApp** (sin nombre, correo ni teléfono) | Orlando | I |
| 4 | **Aplicabilidad del art. 12 de la Llei 21/2000 y de la Ley 41/2002 a una residencia;** qué es la «dirección médica» y quién custodia | Abogado, con el tipo de centro piloto | I |
| 5 | **Confirmar los 15 años desde la baja** como plazo por defecto, el de los comunicados y el de las citas | DPO real; CJ opina sobre comunicados | I |
| 6 | **Cuenta compartida entre centros:** qué hace la anonimización en el centro que la recibe (3.3.2) | DPO real | I |
| 7 | **¿Ser representante legal o contacto urgente impide la supresión?** (art. 17.3.b y e) | DPO real | I |
| 8 | **Procedimiento del personal:** momento de la suspensión, quién firma la instrucción, información a la plantilla, derechos digitales laborales | Residencia y asesoría laboral | I |
| 9 | **Texto del aviso de 3.6** y de la información al interesado, en castellano y catalán | DPO real; catalán revisado por quien lo sepa | I |
| 10 | **Copias de seguridad:** retención real de Azure (7 días por defecto, **A verificar**) y que no hay retención a largo plazo | Orlando | I |
| 11 | **Contrato de encargado:** instrucción de borrado, devolución al terminar, asistencia en derechos | DPO real o abogado | I |
| 12 | **Correcciones de la política y de `antes-de-datos-reales.md`** de 5.1 y 5.2 | Quien aplique los cambios (sesión principal u Orlando), tras el DPO | I |
| 13 | **Registro de derechos del centro** con la anotación de cada anonimización | Residencia | I |
| 14 | **Auditoría de la nueva acción** `FAMILY_MEMBER_ANONYMIZE` en la lista cerrada de acciones, con su etiqueta | Quien construya | I |
| 15 | **Guardas en los demás caminos** de un familiar anonimizado (3.3.6) y anulación de invitación viva | Quien construya | I |
| 16 | **Catálogo de expurgo y prueba que falla si falta una tabla** | Orlando (construye) | I |
| 17 | **Contenido del registro de historias eliminadas** según la Comissió Tècnica (abrir el cuadro y comprobar) | DPO real | P |
| 18 | **Consulta y exportación** del historial de un residente dado de baja (derecho de acceso y fin de contrato) | Orlando y CJ | P |
| 19 | **Retención reforzada y plazo configurable por centro;** `conservar_hasta` hoy no se puede cambiar | Orlando | P |
| 20 | **Propietario de la lista de vencidos** y frecuencia de revisión por escrito | Residencia | P |
| 21 | **Categorías legales A, B y C** de cada tabla y política de acortar | DPO real | P |
| 22 | **Perfil «Dirección Clínica» = dirección médica** a efectos del art. 12.8 | Residencia y abogado | P |
| 23 | **Cuatro ojos** en la ejecución del expurgo y de los pasos técnicos de personal | Orlando | P |
| 24 | **Retención de lo que escribe la propia familia** (`citas_solicitudes.comentario`) | DPO real | P |
| 25 | **ACS, Log Analytics** (paso B10) y art. 19 RGPD: si un encargado cuenta como destinatario | DPO real | P |
| 26 | **Autoridad de control** (AEPD o APDCAT) según el tipo de centro | Abogado | P |
| 27 | **Texto literal de los arts. 17, 19 y 5.1.e del RGPD y de los arts. 3 y 32 de la LOPDGDD:** leídos en resumen del BOE; contrastar | DPO real | P |
| 28 | **Informar a CJ** de la corrección de «5 años, el tiempo que exija la RPGD» a 15 años desde la baja, y preguntarle si los comunicados a la familia son parte de la historia | Orlando | R |
| 29 | **Expurgo por episodio** durante la estancia y borrado por destrucción de claves | DPO real, más adelante | R |
| 30 | **Prueba de que el PDF y `contenido_json` no incluyen al familiar** | Quien construya | R |

## Qué debe validar una persona

Todo lo anterior. En concreto: el desacuerdo del art. 32 (1), los plazos (2, 5), la aplicabilidad a residencias (4), la redacción de la información al interesado (9) y el contrato (11). Yo no soy DPO designado ni abogado.

## Fuentes consultadas el 2026-11-03

Repositorio: archivos y líneas citados en el texto. Fuentes externas, **todas leídas a través de la herramienta de lectura web, que devuelve resúmenes automáticos y no el texto literal**:

- BOE, Ley 21/2000 (Cataluña), texto consolidado, art. 12: `https://www.boe.es/buscar/act.php?id=BOE-A-2001-2353` (resumen automático; página actualizada el 28/11/2025; artículo actualizado el 10/06/2010). Redacción de la Ley 16/2010: `https://www.boe.es/buscar/doc.php?id=BOE-A-2010-10215`, **texto literal leído por el solicitante en el PDF del BOE el 2026-11-03**.
- BOE, Ley 41/2002, art. 17: `https://www.boe.es/buscar/act.php?id=BOE-A-2002-22188` (resumen automático; página actualizada el 01/03/2023). El resumen incluía una frase sobre datos de nacimiento y archivos definitivos que no reconozco y **no uso**.
- BOE, LO 3/2018: `https://www.boe.es/buscar/act.php?id=BOE-A-2018-16673` (arts. 3 y 32; resumen automático; actualizada el 27/12/2025). La disposición adicional 17.ª no llegó a leerse.
- BOE (DOUE), Reglamento (UE) 2016/679: `https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807` (considerando 26, arts. 4.5, 5.1.e, 17 y 19; resumen automático con frases breves).
- Comissió Tècnica en Matèria de Documentació Clínica, cuadro de plazos (07-04-2017): `https://canalsalut.gencat.cat/web/.content/_Professionals/Consells_comissions/comissio_tecnica_en_materia_de_documentacio_clinica/criteris_protocols/quadre_terminis_conservacio.pdf`. **No se pudo leer** (PDF comprimido); su contenido procede del solicitante y de un resultado de búsqueda.
- Microsoft Learn, «Automated backups in Azure SQL Database» (`https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview?view=azuresql`) y «CREATE TRIGGER» (`https://learn.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql`): resúmenes de búsqueda.
- EDPB, Directrices 01/2025 sobre seudonimización: publicadas en consulta el 17/01/2025; **no he podido confirmar que exista versión final**, así que no me apoyo en ellas. El principio de que el dato seudonimizado sigue siendo personal está en el considerando 26 del RGPD.
- Análisis de Cuatrecasas sobre plazos de conservación y bloqueo (secundario, solo orientativo).
