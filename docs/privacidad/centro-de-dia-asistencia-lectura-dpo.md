# Centro de Día, fase 5a (asistencia y ausencias programadas): lectura del esquema

**BORRADOR del agente `dpo-residapp`. NO está validado. Lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-11-24. Segunda lectura experta, no un dictamen. Rama `fase5a-asistencia`. Es la lectura del esquema que el apartado 4.1 de `centro-de-dia-lectura-dpo.md` exige **antes del ADR y del script**. No modifica código, esquema, ADR ni otros documentos. Modelo de formato: `zona-horaria-centro-lectura-dpo.md`.

Etiquetas: **[Hecho]** verificado en el repositorio o en `normativa-local/` (con archivo y línea); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, la residencia, el DPO real o un abogado; **[A verificar]** no lo he podido confirmar. No incluyo datos reales ni comentarios de CJ copiados. Los textos legales vienen de `normativa-local/` (índice del 2026-11-20 21:41, menos de una semana; consolidados, no versión oficial; las leyes catalanas, en castellano del BOE).

Términos, en una frase cada uno. **Dato de salud**: dato que dice algo sobre la salud de una persona, incluida la atención que recibe (RGPD, art. 4.15, `rgpd.txt:57`). **De solo añadir**: tabla en la que la aplicación no puede cambiar ni borrar filas; un cambio es una fila nueva. **Seguridad por filas**: filtro de la base que hace que cada centro vea solo lo suyo. **Seudonimizado**: que se guarda un identificador y no el nombre. **Bloqueo** (LOPDGDD, art. 32): reservar un dato sin usarlo, ni verlo, salvo para jueces y autoridades, hasta que prescriba la responsabilidad.

## Conclusión

**Admisible con cambios.** El esquema propuesto es proporcionado en lo esencial (sin hora, causa cerrada, tablas propias, lectura por perfil). Lo cambio en seis puntos y señalo un **hueco de diseño que bloquea la lista de seguimiento**:

1. **Hueco que bloquea (apartado 3.1): sin «días en que se espera a la persona», «3 días seguidos» no tiene sentido.** Quien viene lunes, miércoles y viernes figuraría como ausente martes y jueves. Hay que decidir de dónde sale el calendario esperado, y ese calendario (la pauta) es un dato nuevo por persona que también necesita esta lectura.
2. **Asistencia: de solo añadir, con corrección como fila nueva** (no actualizable). Un día = una cadena de versiones; vale la última.
3. **El texto libre de «otro motivo» es el punto débil.** Se admite porque CJ lo pidió, pero con tope bajo, ayuda visible, sin saltos de línea y **sin que lo vea Administración**: Administración ve la causa cerrada, no la nota.
4. **Ausencia programada: el motivo sí revela salud** (el ingreso hospitalario es dato de salud sin duda) y Administración **no lo necesita** para su fin (que no se marque cada día y que no salga en la lista). Propongo guardar el motivo pero **leerlo solo Enfermería y Medicina**; sin texto libre en la ausencia programada.
5. **La lista de seguimiento es admisible como función administrativa**, con tres límites que la separan de la alerta de seguridad descartada (consulta, no aviso; solo días ya anotados; texto que lo dice). **No se guarda nada nuevo** al calcularla.
6. **El interruptor de centro, el tipo de servicio leído de la base y una regla de base de datos «solo en centros de Centro de Día»** como defensa en profundidad (apartado 3.4).

**El riesgo más importante** no es de confidencialidad sino de **falsa seguridad y de exactitud**: que alguien lea la lista (o su ausencia de nombres) como «vigilancia», o que un día sin anotar se interprete como «vino» o como «no vino».

---

## 1. Qué he leído y qué no

**[Hecho]** He leído `centro-de-dia-lectura-dpo.md` (apartados 1, 3.2, 3.4, 4.1, 5 y 6), ADR 0017 (en especial el punto 7: «Cualquier función nueva que dependa del tipo [...] exige ADR propio, lectura del esquema por el DPO, interruptor apagado por defecto y comprobación del tipo en el caso de uso», `0017...md:28`), ADR 0015 (líneas 31, 110 y 126), `expurgo-y-supresion.md` (apartado 1.5 y catálogo) y `citas-externas-lectura-dpo.md` (apartados 6.2 y 6.3). **No he leído el ADR 0012** (el correo no entra en 5a) **ni el script `0091`** (la matriz de permisos); lo que digo de ella viene de las lecturas anteriores.

**[Hecho]** Lo que CJ y Orlando han decidido (resumen del encargo): sin hora de llegada ni de salida; causa cerrada si no viene; ausencia programada; la falta se marca al acabar el horario; no se llama a la familia; lista de seguimiento en pantalla de Administración con 3 días como valor por defecto, **sin validar por CJ**; sin correo ni alerta.

**No sé** (y condiciona): quién anota la asistencia en el piloto (qué perfiles), cómo sabe hoy el centro quién debe venir cada día, y si un centro puede suspender a un residente por hospitalización (ver 2.4).

## 2. El esquema, tabla por tabla

### 2.1 `asistencia_dia`: veredicto y cambios

| Campo propuesto | Veredicto | Cambio o razón |
|---|---|---|
| `centro_id`, `residente_id` | Bien | Clave compuesta con el residente (que el residente sea del centro lo garantiza la base, no la aplicación) |
| Fecha local del centro | Bien, con precisión | Guardar como **fecha** (sin hora) calculada con la zona del centro (`zonas_centro`, F1 de zonas horarias), **no** con la del servidor ni la del dispositivo. No puede ser futura ni anterior al alta del residente ni posterior a su baja, y solo se anota para un residente activo |
| Estado `ASISTIO` / `NO_ASISTIO` | Bien | Dos valores y nada más. **No añadir «tarde», «salió antes» ni «parcial»**: serían horas por la puerta de atrás (CJ: sin hora) |
| Causa `AVISO_FAMILIA` / `SIN_AVISO` / `OTRO` | Bien | `CHECK`: causa **obligatoria con `NO_ASISTIO` y prohibida con `ASISTIO`**. Ninguna causa clínica en la lista |
| Nota corta, solo con `OTRO` | Cambia (ver 3.2) | Tope bajo en la base, sin saltos de línea, solo con `OTRO`, **no legible por Administración** |
| Cuenta y perfil de quien anota | Bien | Seudonimizados; `CHECK` de los perfiles que pueden anotar (lista cerrada, la fija CJ). La cuenta es dato personal del trabajador: al registro de actividades de ResidApp (art. 30.2) con acceso restringido |
| Momento (`anotado_en`) | **Mantener, pero con una regla** | Es el momento **en que se anotó**, no la hora de llegada. Pero si se anota al llegar, se parece a una hora de entrada y contradice la decisión de CJ. **Ninguna pantalla, informe ni exportación lo muestra como hora de llegada**; solo se ve en el historial de correcciones, para quien lo corrige. Existe por responsabilidad proactiva (art. 5.2 RGPD) |
| Inmutable o actualizable | **Solo añadir, con versiones** | Ver 2.2 |
| Domicilio, transporte, comida, dieta, incidencias | **No** | Fuera de 5a (ya lo fija el apartado 1 de la lectura general) |

**Qué no se añade**: ni foto, ni huella, ni NFC, ni pulsera para fichar (identificador del art. 9.1 y ADR 0009: otra lectura), ni geolocalización, ni observaciones libres del día.

### 2.2 Inmutable con corrección: qué recomiendo y por qué

**Recomiendo solo añadir con versión** (`numero` 1, 2, 3… por residente y día; vale la de mayor número; índice único `(centro_id, residente_id, fecha, numero)`).

- **Exactitud (art. 5.1.d RGPD):** «se adoptarán todas las medidas razonables para que se supriman o rectifiquen sin dilación los datos personales que sean inexactos» (`rgpd.txt:76`). Un auxiliar se equivoca de persona o de casilla: debe poder corregir el mismo día.
- **Responsabilidad proactiva (art. 5.2, `rgpd.txt:79`):** lo anotado en primer lugar y su corrección, con quién y cuándo, es la prueba de lo que se hizo. Con una fila actualizable se perdería.
- **Coherencia con el proyecto:** es el patrón de las citas externas, las zonas y los comunicados (publicación inmutable, correctora vinculada). Y la base **no puede garantizar** que solo se corrija lo anotado si el permiso es de `UPDATE`.
- **Sin texto libre en la corrección.** Una versión nueva repite estado, causa y nota. Que `numero >= 2` ya significa «corrección». **[Decidir]** CJ: quién corrige (propongo los mismos perfiles que anotan; **Administración no anota ni corrige** la asistencia, la lee) y hasta cuántos días atrás (el tope lo fija CJ; pasado el tope, solo un perfil superior). Una corrección de Administración sobre un día que no presenció debilita la prueba.
- **Cómo se lee la última versión:** una consulta única que devuelve la fila de mayor `numero`. Ningún cálculo (ni la lista de seguimiento) puede leer versiones antiguas.
- **Un día sin fila no es «vino» ni «no vino»:** es «sin anotar», y así se muestra («Sin anotar»), nunca vacío ni en verde.

**Alternativa aceptable (más simple, algo menos estricta):** una fila por día actualizable más una tabla de historial. La descarto: hace falta un permiso `UPDATE` y un disparador que escriba el historial; el patrón de solo añadir ya existe y las pruebas de la matriz de permisos lo conocen.

### 2.3 `ausencias_programadas`: veredicto y cambios

| Campo | Veredicto | Cambio o razón |
|---|---|---|
| `centro_id`, `residente_id` | Bien | Igual que arriba |
| `desde`, `hasta` | Cambia | **`hasta` obligatorio**, `desde <= hasta`, tope de duración fijado por CJ (sin cifra mía). Una ausencia sin fin saca a la persona de la lista de seguimiento para siempre y nadie lo verá. Una ausencia más larga se registra renovándola (versión nueva) |
| Motivo `INGRESO_HOSPITALARIO` / `VACACIONES` / `OTRO` | Cambia (ver 3.3) | Se guarda, pero **solo lo leen Enfermería y Medicina**. **Sin texto libre** |
| Quién la registra | Cambia | Propongo **Administración y Enfermería** (lo normal es que la familia llame al centro). Administración puede elegir el motivo al registrar (es quien recibe la llamada), pero la pantalla no se lo vuelve a mostrar |
| Anulable con constancia | Bien | Como filas nuevas de solo añadir: `ANULADA` (error) y `ACORTADA` (la persona vuelve antes), con cuenta, perfil y momento, **motivo de lista cerrada, sin texto**. Un cambio de fechas = anular y registrar otra |
| Solapes | **Añadir** | La base rechaza dos ausencias vigentes del mismo residente que se solapan |
| Baja del residente | **Añadir** | Al dar de baja, las ausencias vigentes se cierran en la misma transacción, con causa fija `BAJA_RESIDENTE` (patrón de las citas externas, `citas-externas-lectura-dpo.md`, apartado 6.3) |

**Prioridad entre ambas tablas.** **[Opinión]** Una ausencia programada **no crea filas de `NO_ASISTIO`**: es una regla de lectura. Si un día cubierto tiene una fila de `asistencia_dia`, **manda la fila** (la realidad gana a lo previsto; si la persona vino, vino). Así no se inflan las cifras ni se marcan días que nadie vivió.

### 2.4 Cosas que el esquema no debe duplicar

- **Suspensión del residente por hospitalización.** La suspensión que ya existe (`citas-externas-lectura-dpo.md:242`: «hospitalización, ausencia temporal») y `INGRESO_HOSPITALARIO` cuentan lo mismo de dos maneras. **[Decidir]** CJ y Orlando: o el Centro de Día no usa la suspensión, o un residente suspendido no sale en la asistencia ni en la lista, y la ausencia programada es solo para lo que no se suspende. Sin esa decisión habrá dos fuentes de verdad.
- **«Días contratados» o pauta.** Lo menciono en 3.1: es lo que falta.

---

## 3. Respuestas a tus preguntas

### 3.1 Pregunta 2 (primero, porque condiciona el resto): la lista de seguimiento

**¿Es admisible?** **Sí, como función administrativa**, y es coherente con lo que CJ describió (Administración contacta con la familia cuando faltan 2 o 3 días seguidos). No es un tratamiento nuevo de datos: es una consulta sobre lo ya anotado, para un perfil que ya lo lee (asistencia es gestión; criterio de la Llei 21/2000, art. 11.4: el personal de administración y gestión «puede acceder sólo a los datos de la historia clínica relacionados con las mencionadas funciones», `llei-21-2000.txt:117`).

**¿Roza la alerta «no ha llegado»?** Roza **si** se construye con cualquiera de estos rasgos; la separa de ella que **no** los tenga:

| Rasgo | Alerta de seguridad (descartada) | Lista de seguimiento (admisible) |
|---|---|---|
| Cuándo actúa | A la hora de llegada, el mismo día | Después de cerrados los días, sobre días ya anotados |
| Cómo llega | Aviso o correo a alguien | **Nadie recibe nada**; quien la abre la ve |
| Qué cuenta | Una persona que no ha llegado hoy | Varios días completos seguidos |
| Qué promete | «Si no llega, avisaremos» | «Esto ayuda a Administración a decidir a quién llamar» |
| Dónde | Entrada, correo, móvil | Una pantalla de Administración |

**Condiciones (todas):**

1. **Sin correo, sin notificación, sin sonido, sin color de alarma y sin cifra roja en el inicio.** **[Decidir]** Orlando: si hay un contador en el inicio de Administración (como el de otras bandejas), con texto neutro («pendiente de revisar»). Mi inclinación: **sin contador**, solo la pantalla; un contador es un empujón que acerca la lista a una alerta.
2. **Se calcula, no se guarda.** Ninguna tabla de seguimiento, ningún «visto», ningún «ya llamé». Si Administración pide anotar el contacto con la familia, es **un dato nuevo** (y un texto libre con riesgo) y necesita su propia lectura; **no entra en 5a**.
3. **Cuenta solo `NO_ASISTIO` anotado (última versión), nunca días «sin anotar».** Un día sin anotar **rompe la cadena** y se muestra aparte como «Días sin anotar», para que no parezca que todo está bien. Contar días sin anotar como ausencias llenaría la lista de falsos avisos cada vez que alguien olvidara anotar.
4. **Excluye** los días cubiertos por una ausencia programada vigente y a los residentes suspendidos o de baja.
5. **Qué muestra por persona:** identificador visible del residente (nombre como en el resto de las pantallas de Administración), último día que vino, **nº de días seguidos**, y la **causa cerrada** de esos días (`AVISO_FAMILIA`, `SIN_AVISO`, `OTRO`). **No** la nota, **no** el motivo de ninguna ausencia programada, **no** ninguna hora.
6. **No se imprime ni se exporta**, y sin datos en el título de la pestaña, la dirección ni un `aria-label`; `Cache-Control: no-store` (como las citas externas).
7. **Ningún lenguaje clínico ni de riesgo** («riesgo», «posible incidencia»): no es una función con finalidad médica. **[A verificar]** que no entra en el Reglamento (UE) 2017/745 (no he localizado en `mdr.txt` la definición para citarla; el apartado 11, punto 6, de `antes-de-datos-reales.md` ya fija el criterio «sin alertas ni cruces clínicos»). Sin IA ni tendencias (ADR 0011).
8. **El umbral de 3 es un valor de producto sin validar**: no se afirma en ninguna pantalla ni documento que 3 días sea un criterio clínico o de seguridad. Si es configurable por centro, los **topes mínimo y máximo van en la base** (valores de CJ, no míos), y el cambio guarda la cuenta (como `citas_externas_correo_centro`).

**Texto propuesto (pantalla de Administración):**

> **Personas que llevan varios días sin venir**
> Esta lista te ayuda a decidir a quién llamar. Muestra a las personas que, según lo anotado, no han venido los últimos [3] días en que se las esperaba, sin una ausencia programada.
> No es un aviso de seguridad: la aplicación no avisa a nadie ni llama a las familias. Que una persona no aparezca aquí no significa que esté bien: puede que el día no se haya anotado. Qué hacer con cada caso lo decide el centro según su procedimiento.

**El hueco que la bloquea (esto es lo importante).** «3 días seguidos» necesita saber **qué días se esperaba** a cada persona. La propuesta no tiene pauta. Sin ella hay tres salidas:

| Opción | Efecto | Mi valoración |
|---|---|---|
| **A. Pauta mínima por persona**: qué días de la semana la esperan (siete casillas; sin jornada ni horas) | La lista cuenta solo días esperados; el auxiliar anota solo a quien se espera | **La recomiendo.** Es un dato nuevo: revela un calendario y, por inferencia, necesidad de atención diurna (apartado 1 de la lectura general: «fija un calendario»). Va en esta misma lectura y ADR: mismo acceso que la asistencia, de solo añadir con versión, sin motivo ni texto |
| B. Contar días naturales | Quien viene tres días a la semana sale en la lista cada semana | Descartada: ruido que la hace inútil y enseña a ignorarla |
| C. Contar las últimas N anotaciones de la persona, sea cual sea el día | Sin calendario, pero depende de que el auxiliar anote **solo** a quien se espera | Frágil: si anota a todos cada día, B por la puerta de atrás |

**[Decidir]** Orlando y CJ: A, B o C. Si es A, las preguntas de privacidad de la pauta son: seguridad por filas, solo añadir, lectura como la asistencia, plazo propio sin cifra mía, y que **ninguna pantalla enseñe la pauta a la familia** ni al correo.

### 3.2 Pregunta 1: el texto libre de «otro motivo»

**Qué recomiendo:**

1. **Texto solo con `OTRO`**, opcional, y la base rechaza lo demás: con `AVISO_FAMILIA` o `SIN_AVISO`, la nota debe ser `NULL`.
2. **Tope corto en la base de datos.** El precedente del proyecto son 80 a 150 caracteres en las citas externas y 100 en las fichas (`centro-de-dia-lectura-dpo.md`, apartado 3.2). **[Decidir]** ingeniería con CJ; yo bajaría hasta lo que permita escribir «otro» (por ejemplo, «cambio de domicilio»). Sin saltos de línea, sin `textarea`.
3. **Ayuda visible** junto al campo: «Escribe solo el motivo en pocas palabras. No escribas diagnósticos, enfermedades, nombres de otras personas ni lo que ha contado la familia sobre la salud.»
4. **Quién la lee: Enfermería, Medicina y el Auxiliar asignado. Administración no** (ve «Otro motivo», sin texto). Razón: Administración necesita saber que hay una falta y por qué categoría para decidir si llama; **no necesita la nota** para ese fin. Art. 25.2 RGPD: «por defecto, solo sean objeto de tratamiento los datos personales que sean necesarios [...] Esta obligación se aplicará a la cantidad de datos personales recogidos, a la extensión de su tratamiento, a su plazo de conservación y a su accesibilidad» (`rgpd.txt:310`). El filtro lo hace el servidor, y una prueba por reflexión fija qué propiedades llegan a cada vista.
5. **No sale en la lista de seguimiento, ni en agregados, ni en la auditoría.**
6. **Lo que lleva por dentro.** «Otro» es el sitio donde acabará «ingresada», «gripe» o «lo que dijo la hija»: **es dato de salud y a veces información de un tercero** (la CTMDC pide cautela con lo que aporta la familia, `ctmdc-informacio-tercers.txt:77-92`). La ayuda reduce el riesgo, no lo elimina; por eso la restricción de lectura y el plazo (3.4).
7. **La corrección de una nota ya escrita** es una versión nueva. La anterior sigue en la base: **para borrarla hace falta el procedimiento de purga** (apartado 3.4). Es otro motivo para el tope bajo.

**[Decidir]** CJ: si de verdad hace falta el hueco. **Mi mejor opción desde la minimización es no tenerlo** (que «otro» baste). Lo admito porque CJ lo pidió y con estas garantías.

### 3.3 Pregunta 1 (cont.): el motivo de la ausencia programada y quién lo ve

**¿Revela salud?**

- **`INGRESO_HOSPITALARIO`: sí, sin discusión.** Es una prestación sanitaria recibida por esa persona (art. 4.15, `rgpd.txt:57`).
- **`VACACIONES`: no es salud**, es un dato de un **tercero** (la familia) y de rutina (la vivienda o la familia fuera de casa). Riesgo menor, pero no es necesario para el fin.
- **`OTRO`:** sin texto, no revela nada.
- **El hecho de tener una ausencia programada**, en este contexto, ya es dato de la persona atendida (como la propia asistencia), pero no es más que lo que Administración ve en la asistencia.

**¿Debe verlo Administración?** **No lo necesita.** Su fin es doble: que los auxiliares no marquen cada día y que la lista de seguimiento no la incluya. Ambos se cumplen con «ausencia programada del [desde] al [hasta]». El motivo sirve a la parte asistencial (una persona que vuelve de un ingreso necesita que Enfermería lo sepa). Criterio de la Llei 21/2000, art. 11.4 (arriba) y art. 25.2 RGPD.

**Qué recomiendo:**

1. **Se guarda el motivo** (lista cerrada, sin texto). Se prefiere a «guardar solo ausencia programada sin motivo» porque, **si no se guarda, Enfermería pierde la información** y volvería a apuntarla en papel o en un texto libre peor. **[Decidir]** CJ: si Enfermería la necesita. Si dice que no, el campo desaparece (la mejor minimización).
2. **Lo leen Enfermería y Medicina.** Administración y Auxiliar ven «Ausencia programada» y las fechas. Dirección ve cifras (3.5).
3. **Quien la registra** (Administración o Enfermería) puede elegir el motivo al hacerlo; si es Administración, **no lo ve después**.
4. **Nunca en auditoría, correo, ni en el aviso a la familia.**
5. **El motivo `INGRESO_HOSPITALARIO` no se comparte con la familia.** La familia no ve la asistencia en 5a.

### 3.4 Pregunta 3: conservación, borrar o bloquear, catálogo de expurgo y permisos

**Plazos (sin cifra mía; lo que se anota como pendiente):**

| Dato | Qué propongo anotar | Quién decide |
|---|---|---|
| Asistencia (día, estado, causa cerrada, cuenta, momento) | Plazo propio, **no** los 15 años clínicos por defecto. Criterio de partida: mientras sea necesario para justificar la prestación y atender reclamaciones, más el plazo de prescripción que interese (LOPDGDD arts. 72 a 74; `centro-de-dia-lectura-dpo.md`, apartado 3.4). Es **gestión**, no un documento de los del art. 12.4 de la Llei 21/2000 (`llei-21-2000.txt:124`: consentimientos, informes de alta, etc.) **[A verificar]** y que un Centro de Día sea «centro sanitario» a efectos de ese artículo | DPO real y centro |
| Nota de «otro motivo» | Plazo **más corto** que la asistencia (es texto libre con más riesgo de salud) **[Opinión]**. Si en la misma fila, la purga la pone a `NULL` con el procedimiento aprobado | DPO real |
| Pauta (si se elige la opción A) | Mientras dure el servicio; versiones anteriores, plazo propio | DPO real |
| Ausencias programadas y su historial | Plazo propio, **más corto que lo clínico**; el motivo `INGRESO_HOSPITALARIO` es dato de salud | DPO real |
| Lista de seguimiento | **No hay plazo: no se guarda** | Hecho |
| Interruptor del centro (cuentas que lo cambiaron) | Con el historial de módulos y zonas, sin decidir | DPO real |

**Proporcionalidad:** art. 5.1.e, «no más tiempo del necesario» (`rgpd.txt:77`). Una fila por persona y día (unas 250 al año con cinco días por semana, **[Suposición]**) durante 15 años no se justifica.

**Borrar o bloquear.** Sigue pendiente la decisión general del DPO real (`antes-de-datos-reales.md`, apartado 8). El texto literal: «El responsable del tratamiento estará obligado a bloquear los datos cuando proceda a su rectificación o supresión» y el bloqueo es «la identificación y reserva de los mismos [...] para impedir su tratamiento, incluyendo su visualización» salvo jueces, Ministerio Fiscal y Administraciones competentes, «solo por el plazo de prescripción de las mismas» (LOPDGDD, art. 32.1 y 32.2, `lopdgdd.txt:246-247`). **Recomiendo que el diseño admita bloquear** (que una purga pueda dejar los datos fuera de toda pantalla, sin destruirlos) y **no cierre el borrado**: el catálogo de expurgo debe declarar para cada tabla nueva si se puede bloquear, borrar o ambas. Una **corrección** de una fila (versión nueva) **no es** una supresión: la versión anterior se bloquea o se purga con el mismo procedimiento.

**Purga:** las tablas son de solo añadir; purgarlas exige el procedimiento con permisos del dueño que apruebe el DPO (`expurgo-y-supresion.md`, apartado 2.3; no quitar protecciones: `antes-de-datos-reales.md:165`). **No se construye en 5a**, pero el **catálogo de expurgo** las lista desde el primer script, con la categoría **no clínica** y su plazo «sin decidir» (`expurgo-y-supresion.md`, línea 95: la prueba falla si falta una tabla), y `CoberturaRlsTests` exige su seguridad por filas o justificar la excepción.

**Matriz de permisos de `0091`:** cada tabla nueva con `GRANT SELECT, INSERT` y `DENY UPDATE, DELETE` a `residapp_aplicacion`; **nada** para `residapp_tareas` (la lista es una consulta de la web, no una tarea de fondo; si algún día se envía algo, será otro script con su lectura). La prueba de la matriz debe conocer las tablas nuevas.

**Seguridad por filas y tipo de servicio:** además del filtro por centro, **una regla de base de datos** (disparador o `CHECK` con función) que **rechace inserciones en un centro que no sea `CENTRO_DE_DIA`**. Es la defensa en profundidad que la lectura general pidió para 5a a 5d (apartado 6.5, cautela 1: `dbo.centros` no tiene seguridad por filas y el tipo no debe usarse como frontera de seguridad; esta regla complementa, no sustituye, el filtro por centro). Y el **interruptor del centro**: tabla de solo añadir con la cuenta de quien lo cambia, como `citas_externas_correo_centro`; **apagado por defecto** (art. 25.2); apagarlo **no oculta ni borra lo ya anotado** (derecho de acceso; ADR 0015, alternativa descartada, línea 126). **[Decidir]** Orlando: si es un módulo del ADR 0015 (con anexo de contrato) o una función base apagada, como pregunta 4 de la lectura general. La comprobación (tipo leído **de la base** y interruptor) va **en el caso de uso, nunca en la cookie del ámbito** (ADR 0017, punto 7; ADR 0015, línea 31) y con una prueba por reflexión que falle si una acción de 5a no la hace.

### 3.5 Lectura por perfil (tu tabla 3.2 aplicada)

| Dato | Administración | Enfermería / Medicina | Auxiliar (asignados) | Dirección | Familia |
|---|---|---|---|---|---|
| Asistencia (estado, causa cerrada) | Sí, **lee**; no anota ni corrige | Sí | Sí (anota y corrige) | Solo cifras por día, sin nombres | **No** |
| Nota de «otro motivo» | **No** | Sí | Sí, la que ve de su gente | No | No |
| Ausencia programada (fechas) | Sí (la registra) | Sí (la registra) | Sí, sin motivo | Cifras | No |
| **Motivo** de la ausencia programada | **No** | **Sí** | **No** | No | No |
| Lista de seguimiento | **Sí** | No (no es su función) | No | No | No |
| Pauta (si hay) | Sí | Sí | Sí | No | No |

- **Medicina y Enfermería en la lista de seguimiento: no.** Es una herramienta de Administración (decisión de Orlando); si Enfermería la necesitara, ya ve la asistencia.
- **Dirección con cifras:** el ADR 0015 (línea 161) ya anota la cautela de **cifra mínima con supresión complementaria** para que no se pueda deducir una celda restando. En un centro pequeño, una cifra «1» es una persona. **[Decidir]** DPO y CJ el valor mínimo; no lo invento.
- **La familia no ve la asistencia** (no hay parte todavía). Cuando exista (5b), será otra lectura: la asistencia **no se vuelca sola** al portal.
- **Ninguna de estas pantallas** lleva datos en el título de la pestaña, la dirección ni un `aria-label`.

### 3.6 Pregunta 4: auditoría solo con identificadores

**Sí, solo identificadores:** cuenta, perfil, centro, residente (identificador, no nombre), recurso (identificador de la fila) y un código de acción cerrado. Acciones propuestas, a título orientativo: anotar asistencia, corregir asistencia, registrar ausencia programada, anular o acortar ausencia, abrir la lista de seguimiento, cambiar el interruptor del centro.

**Nunca en la auditoría:** el estado (vino o no), la causa, la nota, el motivo, las fechas de la ausencia, la pauta, el número de la lista ni quién aparece en ella. Un registro que dijera «Fulana: ingreso hospitalario» sería un duplicado del dato de salud en una tabla con otro plazo y otros lectores. **Abrir la lista** se registra como acción sin cifras ni nombres (quién y cuándo), porque muestra a varias personas a la vez; no se registra cada fila. El plazo de la auditoría sigue sin decidir (`antes-de-datos-reales.md`).

---

## 4. Lo que cambia del diseño (resumen)

1. **Pauta mínima (o decisión equivalente)** antes de definir «3 días seguidos» (3.1). **Bloquea la lista.**
2. **Asistencia de solo añadir con versión**; «sin anotar» es un estado distinto de ambos (2.2).
3. **Nota de «otro motivo»**: tope bajo en la base, sin saltos, solo con `OTRO`, ayuda visible, **no la ve Administración** (3.2).
4. **Ausencia programada**: `hasta` obligatorio, sin solapes, motivo cerrado sin texto, **motivo leído solo por Enfermería y Medicina**, anulación y acortamiento como filas nuevas, cierre por baja (2.3, 3.3).
5. **`anotado_en` existe pero no se muestra como hora de llegada** (2.1).
6. **Lista de seguimiento**: consulta que no guarda nada, sin avisos, sin contador por defecto, solo días anotados, excluye ausencias programadas, texto que dice lo que no es (3.1).
7. **Regla de base de datos solo `CENTRO_DE_DIA`**, interruptor de solo añadir, apagado por defecto, comprobación en el caso de uso (3.4).
8. **Catálogo de expurgo y matriz de `0091`** desde el primer script, con plazo «sin decidir» y posibilidad de bloquear (3.4).

## 5. Qué debe decidir una persona

**Orlando:** pauta (A, B o C, 3.1); contador en el inicio (3.1, punto 1); interruptor como módulo o función base (3.4); suspensión por hospitalización frente a ausencia programada (2.4); si Administración anota y corrige asistencia (2.2).
**CJ:** perfiles que anotan y corrigen y hasta cuántos días atrás; si hace falta el hueco de «otro motivo» y su tope; si Enfermería necesita el motivo de la ausencia programada; tope de duración de una ausencia; si el valor 3 y los topes del umbral son razonables; valor mínimo de las cifras de Dirección.
**Persona experta (DPO real o abogado):** que la asistencia y las ausencias son gestión y no historia clínica, y su plazo; base del art. 9.2.h para estos datos en un servicio privado (`centro-de-dia-lectura-dpo.md`, apartado 6.3); borrar o bloquear; que la lista de seguimiento no es una función de seguridad ni un producto sanitario; que Administración no vea la nota ni el motivo; información a usuarios, representantes y trabajadores (arts. 12 y 13 RGPD: la asistencia, la lista y quién la ve deben constar en la política y en la cláusula); registro de actividades (art. 30) y EIPD del centro con el anexo de riesgos (los de `centro-de-dia-lectura-dpo.md`, apartado 4.2, que apliquen a 5a: falsa seguridad, retención desproporcionada, rutina conocida por quien no debe).

## 6. A verificar

- Que un Centro de Día privado sea o no «centro sanitario» a efectos del art. 12 de la Llei 21/2000.
- Que la lista de seguimiento quede fuera del Reglamento (UE) 2017/745 (no he podido citar la definición desde `mdr.txt`).
- Que la matriz de `0091` y el catálogo de expurgo admitan estas tablas con las pruebas actuales (no he leído `0091`; ingeniería).
- Que la zona horaria del centro esté siempre disponible al calcular la fecha local; los procesos sin ámbito no la leen (`zona-horaria-centro-lectura-dpo.md`, apartado 4). La asistencia la escribe una persona con sesión, pero la lista la lee otra.
- Número aproximado de filas (250 al año por persona): mi cálculo, **[Suposición]**.

**Fuentes consultadas el 2026-11-24:** `normativa-local/` (`rgpd.txt` arts. 4.15, 5, 9, 25; `lopdgdd.txt` art. 32; `llei-21-2000.txt` arts. 11 y 12), y en el repositorio `docs/privacidad/centro-de-dia-lectura-dpo.md`, `zona-horaria-centro-lectura-dpo.md`, `citas-externas-lectura-dpo.md`, `expurgo-y-supresion.md`, ADR 0015 y 0017 y `docs/tareas/alta-prioridad/pendientes-centro-de-dia.md`. **No soy DPO designado ni abogado.**
