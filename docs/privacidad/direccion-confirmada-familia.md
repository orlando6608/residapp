# Casilla «Dirección confirmada con la familia»: lectura de privacidad y condiciones de construcción

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-02. Segunda lectura experta, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL), **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado.

Complementa, sin repetirlos, `docs/privacidad/aviso-cita-por-correo.md` (apartado 2.1, «Verificar la dirección», y punto 1 de «A verificar»), `docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md` (apartados 2.4 y 7) y `docs/privacidad/antes-de-datos-reales.md` (apartado 7). **No modifico ninguno**; el apartado 9 lista lo que habría que tocar.

Términos, en una frase cada uno: **marca** es la casilla «Dirección confirmada con la familia»; **encargada** es quien trata datos por cuenta de otro (ResidApp, por cuenta de la residencia); **disparador** es una regla de la base de datos que se ejecuta sola al cambiar una fila; **auditoría** es el registro de quién hizo qué y cuándo (`eventos_auditoria`); **oposición** es el derecho a pedir que se deje de usar un dato para un fin concreto.

## Conclusión

**Admisible con condiciones.** Para un aviso genérico (sin nombre, centro, fecha ni motivo), confirmar la dirección «de palabra» y dejar una marca interna es una medida razonable y proporcionada; la verificación por correo (código) no es necesaria para este texto. El riesgo más importante **no es la marca, es lo que la marca no prueba ni garantiza**:

1. **La marca prueba que alguien leyó la dirección a una persona, no que esa persona controle el buzón ni que Administración la leyera de verdad.** Vale solo mientras el correo sea genérico. No vale para un correo con contenido (nombre del residente o del centro) ni para el código de acceso (pieza 3 del ADR 0012), que exigirán verificación técnica (apartado 2, a).
2. **Que se pueda marcar por inercia, o que se pierda sin que nadie lo note.** Si Administración marca sin leer, la medida es decorativa; si olvida marcar, la familia cree que recibe avisos y no llegan (una propuesta caduca a las 48 horas, `aviso-cita-por-correo.md`, línea 18). Se mitiga con el diseño de la pantalla y con transparencia hacia la plantilla, **no con vigilancia** (apartado 7).
3. **Que la invariante dependa de que cada camino de código se acuerde de desmarcar.** Hoy hay **tres** caminos que escriben `familiares.correo` (apartado 1). Debe garantizarla la base de datos, no solo el código.

Se puede construir **ya, con datos de prueba y el envío apagado**. **Antes de la primera dirección real** hace falta lo del apartado 6 (sobre todo: la persona experta confirma que «de palabra» basta, persona de referencia con suplente, procedimiento escrito e información a la familia y a la plantilla).

## 1. Qué hace hoy el código [Hecho]

- **Tres caminos escriben el correo:** alta suelta de familiar (`SqlResidentFamilyRepository.cs`, líneas 40 a 55), edición (líneas 151 a 165) y alta del residente con hasta cinco familiares (`SqlResidentRepository.cs`, líneas 99 a 109). Un cuarto camino, vincular un familiar existente a otro residente (`LinkExistingAsync`, líneas 68 a 119), **no escribe el correo pero comparte el familiar**: el correo está en `dbo.familiares`, no en el vínculo (`SqlResidentFamilyRepository.cs`, línea 41; `SqlAdministracionResidentDirectory.cs`, línea 174).
- **El correo se valida mínimamente:** una sola «@», sin espacios (`FamilyMember.cs`, líneas 143 a 148). Las etiquetas de hoy son «Correo electrónico (opcional)» en dos formularios distintos (`AdministracionModels.cs`, línea 137; `ResidentsModels.cs`, línea 107) y ninguna dice para qué se usará.
- **La edición rechaza un guardado sin cambios** (`SameAs`, `FamilyMember.cs`, líneas 11 a 16; `SqlResidentFamilyRepository.cs`, líneas 146 a 149): una acción «solo marcar» no puede pasar por el mismo método sin tocar esa regla. **La huella de versión incluye el correo** (`FamilyMember.cs`, líneas 20 y 21) y se compara antes de guardar (líneas 141 a 144): sirve para que una marca hecha sobre un formulario viejo no se aplique a una dirección nueva (apartado 2, a).
- **Auditoría del cambio de dirección: no cumple lo que CJ pidió.** Cambiar el correo (o el teléfono, o el nombre) deja una sola acción, `FAMILY_MEMBER_UPDATE` (`SqlResidentFamilyRepository.cs`, línea 164), sin distinguir qué campo cambió. No se puede saber por la auditoría si se cambió la dirección. El alta deja `FAMILY_MEMBER_CREATE` (línea 54; `SqlResidentRepository.cs`, línea 109). **Comprobado: falta** la acción del cambio de dirección (CJ 3.1; `antes-de-datos-reales.md`, línea 102).
- **Los dos avisos leen la dirección en su propia consulta:** `SqlProposalNoticeStore.cs`, líneas 51 a 69, y `SqlChangeNoticeStore.cs`, línea 41 (`RecipientSql`). Ambas reutilizan `SqlFamilyPortalReader.AccessibleLinks`. **La marca debe comprobarse en las dos.**
- **El motivo de omisión es una lista cerrada** con `CHECK` (`0081_envios_correo.sql`, líneas 36 y 37; `motivo_codigo` es `NVARCHAR(24)`, línea 27). `DIRECCION_NO_CONFIRMADA` tiene 23 caracteres: cabe. Añadirlo exige un script nuevo, no editar `0081`.
- **La tarea programada solo lee columnas concedidas** (`0083_rol_tareas_correo.sql`, línea 35: `GRANT SELECT (id, centro_id, cuenta_id, correo) ON dbo.familiares`). **La columna nueva necesita su propio `GRANT`**; sin él, el aviso de cambio fallaría o se omitiría siempre.
- **El disparador de `familiares` existe** (`TR_fam_update_guard`, `0052_portal_familiar.sql`, líneas 24 a 35) y hoy protege centro, creador, fecha de creación y cuenta del portal. No toca el correo.
- **Respuestas de CJ del 2026-11-02:** 2.1 A (de palabra, con marca, sin marca no se envía), 3.1 A (Administración atiende; persona de referencia con suplente, procedimiento escrito, constancia como incidente, cambio de dirección en la auditoría), 3.2 A (más adelante, la familia apaga los avisos desde el portal) (`aviso-citas-por-correo-cj.respuestas.json`, líneas 22 a 48). CJ dejó expresamente a la persona experta si «de palabra» basta jurídicamente (línea 26).

## 2. Respuestas a las preguntas a) a g)

### a) ¿Es suficiente y proporcionado confirmar «de palabra» con marca interna? ¿Qué debe constar?

**Sí, con condiciones, para este aviso genérico [Suposición; Decidir DPO].**

- **Por qué es proporcionado.** El RGPD pide datos exactos y medidas razonables para corregirlos (art. 5.1.d) y poder demostrar que se cumple (art. 5.2). El texto del aviso no lleva dato de salud ni nombre (`aviso-cita-por-correo.md`, apartado 2.2). El daño de un error es un aviso genérico que llega a un desconocido: es bajo. Leer la dirección en voz alta a la persona detecta la causa más frecuente del error (la transcripción de Administración) a coste casi nulo. La AEPD trata como brecha de confidencialidad enviar datos a un destinatario equivocado, pero la valoración del riesgo es lo que decide si se notifica (guía de brechas de la AEPD, junio de 2021; consultada hoy solo a través de un buscador, **versión vigente a verificar**: `https://www.aepd.es/guias/guia-brechas-seguridad.pdf`).
- **Qué no prueba.** (i) Que la persona sea la dueña del buzón (una dirección compartida, o dictada por un hijo para su madre). (ii) Que la dirección esté bien escrita **por la propia persona** al dictarla. (iii) Que Administración haya leído de verdad. Por eso **el alcance de la marca debe quedar escrito**: «vale solo para avisos genéricos; para cualquier correo con contenido, o para un código de acceso o recuperación de cuenta, no vale y hace falta verificación técnica». Debe constar en el ADR 0012 y en el comentario del campo.
- **Qué debe constar (mínimo):**
  - que la dirección está confirmada (sí o no);
  - **quién**: la cuenta de Administración que marcó (`cuenta_id`);
  - **cuándo**: fecha y hora.
  - **«A quién se le leyó» no necesita campo propio:** es el familiar de la ficha (`familiar_id`). La etiqueta debe decir «la persona a quien pertenece el correo», no «la familia» a secas, para que no se dé por buena la palabra de otro pariente. [Decidir DPO] si se admite que la confirme una persona que la familiar designa y que está presente (el caso de la abuela que se ayuda de su nieto).
- **Canal (en persona o por teléfono): no lo guardaría.** Valor probatorio bajo: por teléfono tampoco se identifica a quien contesta, y en persona tampoco se pide documento. Añade un clic, invita a rellenarlo por inercia y es un dato más. El procedimiento escrito dice que ambas vías valen. Si la persona experta lo pide, que sea un valor cerrado de dos opciones, no texto. [Decidir DPO.]
- **Qué es excesivo:** la dirección leída (ya está en la ficha), un texto libre de lo hablado, el nombre de quien contestó al teléfono, una grabación, una firma o un documento de la familia, la IP o el dispositivo de quien marca, y cualquier campo del tipo «cuántas veces se confirmó».
- **Atar la marca a la dirección exacta que se leyó.** La casilla viaja en el mismo envío del formulario que la dirección, con la huella de versión que ya incluye el correo (`FamilyMember.cs`, líneas 20 y 21). Si otra persona de Administración cambia la dirección mientras la primera la lee, el guardado da conflicto (`FAMILY_MEMBER_CONFLICT`, líneas 141 a 144) y la marca **no** se aplica a la dirección nueva.

### b) ¿Debe caducar la marca?

**No recomiendo caducidad automática. Basta con que se quite al cambiar o borrar la dirección, más una vía para quitarla a mano [Suposición; Decidir DPO].**

- La caducidad automática crea un fallo silencioso: a los N meses los avisos dejan de salir sin que nadie lo vea, y una propuesta de cita caduca a las 48 horas (`aviso-cita-por-correo.md`, línea 18). El portal y el teléfono siguen siendo el canal, pero la medida empeoraría el servicio sin reducir un riesgo que ya está acotado por el texto genérico.
- Lo que sí invalida la marca: cambiar o borrar la dirección; una queja de la familia («me llegó un aviso y no lo esperaba»); y, **cuando exista** (hoy no existe, `aviso-cita-por-correo.md`, línea 128), un rebote duro. En esos casos Administración quita la marca a mano.
- Si la persona experta quiere una revisión periódica, que sea una **lista de «pendientes de reconfirmar»** (por ejemplo, al revisar los datos de contacto una vez al año), nunca un apagado automático.

### c) Familiar con varios residentes: ¿marca del familiar o del vínculo?

**Del familiar, una por dirección. Coincido con tu inclinación.** El correo está en `dbo.familiares` y el aviso se manda a `fam.correo` (`SqlProposalNoticeStore.cs`, línea 52; `SqlChangeNoticeStore.cs`, línea 41). Una marca por vínculo permitiría que la misma dirección estuviera confirmada para un residente y no para otro: estados contradictorios sobre un dato que es uno solo. Consecuencias que conviene tener claras:

- Cambiar la dirección desde la ficha de **cualquier** residente vinculado desmarca la confirmación para **todos**. Es lo correcto: la dirección cambió para todos. La auditoría lleva `recurso_id` = el familiar (`SqlResidentFamilyRepository.cs`, línea 163) y el residente de la ficha desde la que se actuó.
- La información a la familia (apartado 5) es la misma para todos sus residentes: se da una vez.
- Cuando exista el interruptor de la familia en el portal (CJ 3.2), será también del familiar y **separado** de la marca (apartado 7, riesgo 3).

### d) Datos mínimos del historial y plazo

**Recomiendo no crear una tabla de historial aparte.** Para este aviso basta:

1. **El estado actual en `familiares`:** dos columnas, la fecha y la cuenta que confirmó, ambas nulas si no hay confirmación. Su vida es la de la dirección: se borran solas al cambiar o quitar el correo, así que no hay nada que purgar.
2. **El historial en la auditoría**, con tres acciones nuevas que llevan solo identificadores (apartado 3, condición 4).

Razones: es lo que menos datos acumula; la auditoría ya es inmutable y ya tiene su propio plazo pendiente (`docs/seguridad`, puerta abierta); y una tabla nueva repetiría los mismos hechos con un plazo distinto. Orlando propuso un historial inmutable «como otras tablas»: **no es necesario para cumplir** y es más código y más cosas que purgar. Si se prefiere la tabla, mínimo: `familiar_id`, `centro_id`, número, acción (`CONFIRMADA` o `RETIRADA`), `cuenta_id`, momento, y **nada más** (ni dirección ni canal ni huella de la dirección: una huella seguiría señalando a la persona, como se razonó en `aviso-cita-por-correo.md`, línea 120).

**Plazo [Decidir DPO]:** el estado, mientras exista la dirección. El historial en auditoría, el que se decida para la auditoría (no más corto que el registro de envíos para que ambos se puedan cruzar). Si hubiera tabla propia: **12 meses desde que la confirmación deja de estar vigente** (cambio, retirada o borrado de la dirección), alineado con el tope de `envios_correo` (`antes-de-datos-reales.md`, línea 105). Es una propuesta de ingeniería, **no un plazo legal**; no he encontrado ninguno que lo fije. Necesitaría su vía de purga, que hoy tampoco existe para `envios_correo`.

### e) Texto de la etiqueta y de la ayuda

Tu etiqueta, «Correo para avisos de ResidApp (confirmado leyéndoselo a la persona)», mezcla el nombre del campo con la instrucción. Propongo separarlas y añadir lo que falta (para qué se usa, cómo se quita, qué pasa si no se confirma). Texto propuesto en el apartado 5.

### f) ¿Hace falta algo más antes de construirlo?

**Para construir con datos de prueba: nada bloquea.** Sí hay condiciones para encenderlo con direcciones reales (apartado 6): la persona de referencia y su suplente, el procedimiento escrito, la constancia como incidente de un aviso a un desconocido (la AEPD exige documentar la brecha aunque no se notifique, art. 33.5 RGPD), la información a la familia (art. 13) y a la plantilla, y el procedimiento para ejercer los derechos sobre la dirección.

**Derechos sobre la dirección (procedimiento corto):**

- **Rectificar:** Administración cambia la dirección; la marca se quita sola; se vuelve a confirmar leyéndosela.
- **Suprimir u oponerse («no quiero más avisos»): borrar la dirección.** No basta quitar la marca (apartado 7, riesgo 3).
- **Acceder:** Administración le dice qué dirección figura y si está confirmada.
- **Quién lo hace:** Administración, con persona de referencia y suplente (CJ 3.1). Cada acción queda en la auditoría.
- **Oposición:** con interés legítimo (6.1.f) o misión pública (6.1.e) existe y debe mencionarse de forma clara (art. 21.1 y 21.4 RGPD; el texto de 21.4 solo lo vi en una reproducción no oficial, `aviso-cita-cambio-cancelacion-por-correo.md`, línea 266: **contrastar con EUR-Lex o el BOE**).
- **Identificar a quien pide el cambio:** una petición de cambiar o quitar la dirección se atiende como cualquier cambio de datos de contacto del centro. [Decidir residencia] qué comprobación hace. El daño de un cambio fraudulento es bajo (avisos genéricos a otra dirección), pero existe, y por eso la dirección nueva también se confirma leyéndosela.

### g) Riesgos que no estaban en la propuesta

Ver apartado 7.

## 3. Condiciones que el código debe cumplir (comprobables con pruebas)

Se suman a las 16 del borrador de la propuesta y a las N1 a N14 del borrador de cambio, que siguen valiendo.

1. **D1. La invariante la garantiza la base de datos.** (i) La marca solo puede existir si hay correo (`CHECK`: sin correo, sin confirmación). (ii) Un `UPDATE` que cambie el correo (de un valor a otro, a nulo o de nulo a un valor) deja la marca sin efecto, **salvo que ese mismo `UPDATE` la ponga de forma deliberada con una fecha nueva**; el valor por defecto ante cualquier duda es «sin confirmar». Forma sugerida: dos columnas (`correo_confirmado_en`, `correo_confirmado_por_cuenta_id`, ambas nulas o ambas con valor) y un disparador junto a `TR_fam_update_guard` (`0052`, líneas 24 a 35). Alternativa equivalente: un contador de versión del correo que sube con cada cambio, con la confirmación válida solo si coincide. Pruebas, una por camino que escribe el correo: alta suelta, edición, alta del residente con familiares y, además, un `UPDATE` directo sobre la tabla (el disparador debe actuar aunque el código se olvide).
2. **D2. La marca la pone solo una acción de Administración, atada a la dirección que leyó.** Casilla **desmarcada por defecto**, nunca precargada, visible solo si hay correo. Viaja en el mismo envío que la dirección y con la huella de versión que ya incluye el correo. Una acción «solo marcar o desmarcar» pasa por su propio método (no por el guardado de la ficha, que rechaza guardados sin cambios, `SqlResidentFamilyRepository.cs`, líneas 146 a 149) y también exige la huella de versión. **No existe una acción de «confirmar todas»** ni de marcado masivo. Prueba: marca con huella vieja, conflicto, no se aplica.
3. **D3. Ningún script, migración, importación ni dato inicial pone la marca.** Los familiares que ya tienen correo guardado (datos de prueba hoy) quedan sin marca. Prueba o revisión: el script que añade las columnas las deja nulas.
4. **D4. Auditoría con solo identificadores.** Acciones nuevas (nombres sugeridos): `FAMILY_EMAIL_CONFIRM`, `FAMILY_EMAIL_UNCONFIRM` y `FAMILY_EMAIL_CHANGE` (solo cuando el correo se añade, cambia o se borra; **se mantiene** `FAMILY_MEMBER_UPDATE`). Llevan cuenta, perfil, centro, residente de la ficha, `recurso_id` = el familiar y momento. **Ni la dirección, ni la anterior, ni la nueva.** También en el alta con dirección y marca en el mismo guardado (`SqlResidentFamilyRepository.cs`, línea 54; `SqlResidentRepository.cs`, línea 109). Prueba: las tres acciones se escriben en cada camino y la tabla no contiene la dirección. [Suposición] no hay lista cerrada de acciones de auditoría; comprobarlo al escribir el script.
5. **D5. Comprobación al enviar, en las dos consultas y en una sola lectura.** Dirección y marca se leen en la **misma** consulta (`SqlProposalNoticeStore.cs`, líneas 51 a 69; `SqlChangeNoticeStore.cs`, línea 41), para que no cambien entre una lectura y otra. Orden de motivos: `SIN_ACCESO`, `SIN_CORREO`, `DIRECCION_NO_CONFIRMADA`. Sin marca: `OMITIDO / DIRECCION_NO_CONFIRMADA` en `envios_correo` y no sale nada. Script nuevo: ampliar `CK_envios_correo_motivo` (`0081`, líneas 36 y 37) y conceder `SELECT` de la columna nueva a `residapp_tareas` (`0083`, línea 35). Pruebas: una por aviso (propuesta y cambio) con dirección sin marca, con marca, y con marca quitada entre reservar y enviar.
6. **D6. Confirmar después no dispara envíos atrasados.** Un aviso omitido por falta de marca consume su clave única (`0081`, línea 44) y **no se envía más tarde** al confirmar la dirección. Es lo previsible para Administración y evita correos viejos que ya no tienen sentido. [Decidir Orlando.] Prueba: omitir, confirmar, comprobar que no sale nada.
7. **D7. La marca no sustituye a otras comprobaciones.** El envío exige además `AccessibleLinks`, el interruptor general y el del aviso (apartados 2.5 y N10 de los otros borradores). Prueba: marca puesta con residente de baja, no se envía.
8. **D8. Sin dirección en registros.** `envios_correo`, la auditoría y los logs no llevan la dirección (condiciones 12 y 13 de la propuesta). El nuevo motivo es un valor cerrado.
9. **D9. Alcance escrito.** En el ADR 0012 y en un comentario junto a las columnas: «esta marca autoriza solo avisos genéricos; un correo con contenido o un código de acceso exige verificación técnica».
10. **D10. Ningún informe sobre personas.** No se construye lista, ranking ni indicador por cuenta de «quién confirma cuántas» ni «cuánto tarda». La auditoría se consulta de forma reactiva (incidente) por quien el procedimiento indique.

## 4. Lo que se puede construir ya y lo que debe validar una persona

**Ya, con datos de prueba y el envío apagado:** las dos columnas y el disparador (D1), el `CHECK`, el motivo nuevo y el `GRANT` (D5), la acción de marcar y desmarcar (D2), las tres acciones de auditoría (D4), la etiqueta y la ayuda en pantalla con los textos del apartado 5 (marcados como borrador), el estado «correo sin confirmar» en la ficha y las pruebas.

**Antes de cualquier dirección real (lo valida una persona):** apartado 6.

## 5. Textos propuestos

Sin jerga. Los corchetes se quitan hasta que exista lo que nombran. Si se pide, lo doy también en catalán (la pantalla de Administración hoy es solo en castellano).

**Etiqueta del campo (ficha del familiar y alta del residente; ambas, porque es donde se recoge el dato: `AdministracionModels.cs`, línea 137; `ResidentsModels.cs`, línea 107):**
«Correo para avisos de ResidApp (opcional)»

**Casilla (solo visible si hay correo escrito; desmarcada):**
«Dirección confirmada con la familia: se la he leído a la persona a quien pertenece y me ha dicho que es correcta»

**Ayuda bajo el campo:**
«Este correo solo sirve para mandar a esa persona avisos de ResidApp: que tiene una propuesta de cita pendiente[, que el centro ha cambiado o cancelado una cita que reservó, o un recordatorio]. Los avisos no dicen el nombre del residente, ni el centro, ni la fecha, ni el motivo: solo que entre en ResidApp. Es voluntario: puede pedir que se quite cuando quiera y entonces lo verá todo al entrar en ResidApp.
Antes de marcar la casilla, cuéntele esto, léale la dirección despacio (letra a letra si hace falta) y espere a que le diga que es correcta. Si no ha podido hacerlo, deje la casilla sin marcar: es correcto y no pasa nada, simplemente no se enviará ningún aviso a esa dirección. Si cambia o borra la dirección, la confirmación se quita y hay que volver a confirmarla. Si alguien le pide que no le manden más avisos, borre la dirección. El correo no sustituye al teléfono: si algo es urgente, llame.»

**Mensaje en la ficha cuando hay correo y no hay marca:**
«Correo sin confirmar: ResidApp no enviará avisos a esta dirección.»

**Mensaje en la ficha cuando hay marca:**
«Dirección confirmada el [fecha] por [nombre de la cuenta].» [Decidir Orlando si se enseña el nombre o solo «Administración»; para el propio personal es información suya, pero ver apartado 7, riesgo 2.]

**Guion para decir a la persona (es la forma práctica de cumplir el art. 13 RGPD en el momento de recoger el dato; la residencia lo adopta o lo cambia, y lo imprime con el procedimiento):**
«Si nos da su correo, le mandaremos avisos de ResidApp cuando haya algo que mirar sobre sus citas. Los avisos no dicen el nombre de su familiar, ni la fecha, ni el motivo: solo que entre en ResidApp. Es voluntario y puede pedir que se lo quitemos cuando quiera; si lo quitamos, lo verá todo al entrar en ResidApp. Para estar seguros, voy a leerle la dirección: [letra a letra]. ¿Es correcta?»

**Línea para la hoja de procedimiento sobre el correo equivocado (CJ 3.1):** «Si una persona dice que ha recibido un aviso que no esperaba: pida disculpas, borre o corrija la dirección, anote fecha y motivo, y avise a [persona de referencia] para dejar constancia como incidente.»

La información completa a la familia (segunda capa, base jurídica, oposición, conservación, reclamación ante [AUTORIDAD]) sigue la de `aviso-cita-cambio-cancelacion-por-correo.md`, apartado 7, con una frase más: «Por seguridad, el centro le lee la dirección para confirmarla; hasta entonces no se le mandan avisos.»

## 6. Qué debe validar una persona antes de datos reales

**DPO real o abogado:**
1. Que confirmar «de palabra» con marca interna es suficiente para este aviso genérico (CJ lo dejó a su criterio, `aviso-citas-por-correo-cj.respuestas.json`, línea 26) y que no basta para un correo con contenido ni para el código de acceso.
2. Si se admite que confirme una persona que la familiar designa y está presente.
3. La conservación: estado (mientras exista la dirección) y auditoría (el plazo de la auditoría); si hubiera tabla, 12 meses desde que deja de ser vigente. Y que la vía de purga, que ya es imprescindible para `envios_correo`, cubra cualquier tabla nueva.
4. Cómo se informa a la plantilla (apartado 7, riesgo 2) y si el registro de actividades de la residencia debe describir la marca como tratamiento de datos del personal.
5. Si el aviso a un desconocido sin datos queda como incidente documentado (art. 33.5) sin notificar a la autoridad, o si algún caso exige notificarlo.

**Residencia:**
1. Persona de referencia con suplente para dudas, quejas y bajas de dirección (CJ 3.1).
2. Procedimiento escrito de una página: dar de alta y confirmar, cambiar, quitar, correo a un desconocido y ejercicio de derechos.
3. Qué comprueba Administración cuando alguien pide cambiar o quitar una dirección por teléfono.
4. Cómo se les dice a las familias que ya tenían correo guardado que no recibirán avisos hasta confirmarlo.

**CJ:** nada nuevo; la decisión de 2.1 A queda recogida tal cual. Confirmar, si quiere, que apagar los avisos desde el portal (3.2) será un interruptor de la familia, distinto de esta marca.

**Orlando:** D6 (confirmar no dispara envíos atrasados); si se enseña el nombre de la cuenta en la ficha; si la casilla va también en el alta del residente o solo en la ficha después.

## 7. Riesgos que no estaban en la propuesta, por orden

1. **Marcar sin leer, o no marcar y que nadie lo note.** Los dos fallos cuestan poco al centro y lo pagan las familias. Mitigaciones **sin vigilar a la plantilla:**
   - **Diseño:** casilla desmarcada, solo visible con correo, frase en primera persona y pasado («se la he leído… y me ha dicho que es correcta»), en la misma pantalla que la dirección, sin acción masiva. Marcar es una declaración, y la frase se lo dice.
   - **Incentivos:** dejar la casilla sin marcar cuando no se ha podido leer es lo **correcto**. Si algún día Dirección mide «% de direcciones confirmadas», se premia marcar sin leer. No se construye ese indicador (D10).
   - **Transparencia:** decir a la plantilla que la cuenta y la hora de cada marca se guardan, **para qué** (corregir un error y responder a un incidente), **quién puede consultarlo** y **cuánto tiempo**, y que no se usa para evaluar rendimiento. Ver riesgo 2.
   - **Visibilidad del fallo contrario:** el estado «Correo sin confirmar: no se enviarán avisos» en la ficha, y una línea en las pantallas de proponer y de cambiar cita («el aviso por correo no se envió: dirección sin confirmar», sin dirección ni nombre; el borrador de la propuesta ya lo contemplaba como opcional, `aviso-cita-por-correo.md`, apartado 2.4, condición 15). Así Administración sabe que debe llamar.
   - **Detección posterior:** las quejas de las familias (canal 3.1) y, cuando exista, el rebote duro.
2. **La marca es un dato del personal.** Cuenta y hora de quién marcó son datos de profesionales. Hay que informarles (art. 13) y limitar el uso: reactivo, ante un incidente, con acceso restringido. La base probable es el interés legítimo de la residencia en poder demostrar su procedimiento (5.2) **[Suposición; Decidir DPO]**. Los derechos digitales de los trabajadores de la LOPDGDD pueden ser relevantes si se quisiera usar la marca para controlar a personas; **artículos a verificar**, no los cito de memoria. No hay que enseñar la lista de quién marcó a nadie fuera de ese procedimiento.
3. **Quitar la marca no es lo mismo que oponerse, y reactivarla podría saltarse la voluntad de la familia.** Si una familia dice «no quiero más avisos» y Administración solo quita la marca, la dirección sigue ahí y otra persona de Administración podría volver a confirmarla sin saber por qué se quitó. **Solución:** para «no quiero más avisos», se **borra la dirección** (único estado inequívoco). Cuando se construya el interruptor del portal (CJ 3.2), será **otro estado**, propio de la familia; el envío exigirá los dos (marca **y** interruptor encendido), y **Administración no podrá encender lo que la familia apagó**. El correo no tiene otro uso hoy (solo lo leen los dos avisos y la ficha: `SqlProposalNoticeStore.cs` línea 52, `SqlChangeNoticeStore.cs` línea 41, `SqlAdministracionResidentDirectory.cs` línea 174), así que borrarlo no pierde nada.
4. **Una marca que se queda en una dirección nueva** (formulario viejo, dos personas editando, un camino de código que se olvida). Cubierto por D1 y D2.
5. **Falsa sensación de verificación.** La marca no prueba la propiedad del buzón. Cubierto por D9 y el apartado 2, a).
6. **Alta del residente con correo.** El alta registra hasta cinco familiares con su correo (`SqlResidentRepository.cs`, líneas 95 a 116). Si no lleva casilla, esos correos quedan sin marca y hay que confirmarlos después en la ficha; es seguro pero fácil de olvidar. [Decidir Orlando] si la casilla va también en el alta (misma regla y mismo texto) o solo en la ficha. Si va solo en la ficha, la ayuda del alta debe decir que se confirma después.
7. **Quién puede marcar.** Hoy lo decide quien puede editar la ficha del familiar. Conviene que lo haga el mismo perfil que atiende a las familias (Administración, CJ 2.1 y 3.1). [A comprobar] qué perfiles tienen hoy el permiso de editar familiares.
8. **Direcciones puestas por Administración sin que la familia lo sepa.** Con la marca obligatoria, Administración no puede dar de alta un aviso sin hablar con la persona; es una ventaja de la medida, pero solo si el procedimiento exige hablar con ella y no con otro pariente.
9. **Cambios por teléfono de una dirección.** Ver apartado 2, f).
10. **Pieza 3 (código de acceso).** El borrador del icono ya anticipaba que un DPO real probablemente pida una dirección **verificada** si el correo lleva nombre o logotipo del centro (`aviso-cita-icono-en-correo.md`, línea 149). Esta marca no se debe reutilizar como si fuese esa verificación (D9).

## 8. Registro de actividades (art. 30): cambios respecto a los borradores anteriores

Parte A (residencia), fila «Datos»: añadir «Confirmación de la dirección: sí o no, cuenta de Administración que la hizo y fecha; no se guarda el canal ni la dirección leída». Fila «Seguridad»: añadir «solo se envía a direcciones confirmadas leyéndoselas a la persona; la confirmación se pierde al cambiar la dirección; cambios en la auditoría sin la dirección». Fila «Personas afectadas»: añadir «profesionales de Administración (cuenta y hora de la confirmación)». Fila «Conservación»: ver apartado 2, d). Parte B (ResidApp): sin cambios, salvo que la instrucción de la residencia incluya mantener la marca y no enviar sin ella.

## 9. Qué cambiar en otros documentos (no lo hago yo)

- `docs/privacidad/antes-de-datos-reales.md`, apartado 7, viñeta 2 (línea 102): sustituir «la casilla y la etiqueta del campo del correo siguen sin construir» por el estado real cuando se construya, y añadir un enlace a este documento; añadir al final de la lista el apartado 6 de este borrador.
- `docs/privacidad/aviso-cita-por-correo.md`, línea 231 (punto 1 de «A verificar»): la decisión humana pasó a ser la casilla; remitir a este documento.
- `docs/decisiones-arquitectura/0012-envio-correos.md`, línea 74 («Falta»): quitar la casilla cuando se construya y añadir D9 (alcance de la marca) y D6.
- Manual (`Views/Home/Manual.cshtml`), «Flujo de trabajo» y guías de pruebas de CJ: consultar al agente `doc-usuario-residapp` al cerrar el cambio (regla del proyecto).

## A verificar o decidir

1. **[Decidir DPO]** Que «de palabra» con marca interna basta para el aviso genérico; si admite la confirmación por una persona designada presente; si exige canal.
2. **[Decidir DPO]** Sin caducidad automática (propuesta) o lista de pendientes de reconfirmar.
3. **[Decidir DPO]** Conservación del estado y del historial (propuesta: estado mientras exista la dirección; historial en auditoría; si hay tabla, 12 meses desde que deja de ser vigente; **no es un plazo legal**) y la vía de purga.
4. **[Decidir DPO]** Información a la plantilla sobre la marca y base del tratamiento de sus datos; artículos de derechos digitales laborales de la LOPDGDD **a verificar**.
5. **[Decidir DPO]** Aviso a un desconocido: incidente documentado (art. 33.5) y cuándo se notifica; autoridad de control (AEPD o APDCAT) según el tipo de centro, **a verificar**.
6. **[Decidir Orlando]** D6 (confirmar no dispara envíos atrasados), si se enseña el nombre de la cuenta en la ficha, y si la casilla va en el alta del residente.
7. **[Decidir residencia]** Persona de referencia y suplente, procedimiento escrito, comprobación ante una petición de cambio por teléfono, y cómo se avisa a las familias que ya tenían correo.
8. **A comprobar al construir:** que no hay lista cerrada de acciones de auditoría; qué perfiles pueden editar familiares; que el disparador y el `CHECK` no rompen los otros caminos (alta, edición, alta del residente, vincular).
9. **A verificar:** texto oficial del art. 21 RGPD (hoy solo una reproducción no oficial) y versión vigente de la guía de brechas de la AEPD.
10. **Fuera de alcance, para más adelante:** interruptor de la familia en el portal (CJ 3.2), verificación por código (fase posterior si el piloto detecta errores), rebote duro, recordatorio y código de acceso (pieza 3).

Fuentes consultadas el 2026-11-02: repositorio (archivos y líneas citados en el texto); AEPD, «Guía para la notificación de brechas de datos personales», versión de junio de 2021 (`https://www.aepd.es/guias/guia-brechas-seguridad.pdf`), solo el resumen del buscador, sin leer el texto completo. No pude recuperar hoy el texto oficial del RGPD en EUR-Lex; los artículos del RGPD se citan por su contenido general y los marcados «a verificar» deben contrastarse en fuente oficial.
