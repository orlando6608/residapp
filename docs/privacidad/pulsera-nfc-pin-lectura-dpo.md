# Pulsera NFC con PIN (I5 del ADR 0007): lectura de protección de datos y seguridad

> **Decisiones de Orlando sobre esta lectura (2026-11-20, tomadas de una en una):**
> 1. **B corregida.** Antes de la pulsera se construyen D0, D1 y D2 del ADR 0016 (tablas, clave del aparato y registro por QR con la pantalla «Aparatos»); la pulsera solo se ofrece en tablets compartidas registradas, aunque el centro no tenga restricción general de aparatos. No se acepta la «B pura» con datos reales.
> 2. **Se descarta la alternativa FIDO2 con NFC** y se sigue con la pulsera NTAG 424 DNA (ResidApp custodia el PIN con las medidas de esta lectura). La llave FIDO2 queda anotada como opción futura.
> 3. **Solo Auxiliar**, ligada a su centro. Enfermería y Medicina, fuera de la primera versión. La lista blanca de acciones la aprueba CJ (pendiente).
> 4. **Voluntaria**, apagada por defecto en cada centro.
>
> Lo que sigue es el informe del agente `dpo-residapp`, copiado tal cual. Los documentos derivados son `docs/seguridad/pulsera-nfc-guia-implementacion.md`, `docs/seguridad/pulsera-nfc-guia-administracion.md` y `docs/seguridad/pulsera-nfc-especificacion-compra.md`.

**BORRADOR del agente `dpo-residapp`. Es una segunda lectura experta, no un dictamen. Lo valida una persona (DPO real o abogado y, para el apartado 3, un laboralista) antes de usarlo con datos reales.** Fecha: 2026-11-20. No he escrito ni modificado ningún fichero. No he recibido ni pedido datos reales.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente (con ruta y línea); **[Suposición]** mía; **[A verificar]** no lo he podido confirmar; **[Decidir]** lo fija Orlando, CJ, el DPO real, la residencia o un abogado.

**Palabras que uso, una vez y en llano.**
- *Pulsera con SDM*: el chip NTAG 424 DNA fabrica un mensaje distinto en cada lectura (un código cifrado más una firma). *SDM* significa «mensaje dinámico seguro».
- *CMAC*: esa firma. Solo quien tiene la clave del chip puede comprobarla.
- *Contador del chip*: un número que sube en cada lectura.
- *Random ID*: opción del chip para que no entregue su número de serie fijo a cualquier lector.
- *Pimienta*: un secreto que se guarda fuera de la base de datos y se mezcla con el PIN antes de guardarlo. Así, quien robe solo la base no puede probar PIN uno a uno.
- *Key Vault*: la caja fuerte de claves de Azure.
- *Alcance reducido*: una sesión que solo permite una lista corta de cosas.

---

## 0. Veredicto

**Admisible con condiciones.**
- **En desarrollo y con datos de prueba:** sí.
- **Con datos reales:** no hasta cerrar las condiciones bloqueantes del apartado 6.
- **La «opción B pura» (sin filtro de aparato) no la aceptaría con datos reales.** La B corregida sí es defendible: sin restricción general de aparatos, pero con la pulsera ofrecida solo en tablets «compartidas» registradas. Es lo mismo que ya decide el ADR 0016 (decisión 12) para el caso con restricción, aplicado también aquí (apartado 2.6).

**Riesgo más importante.** Con la pulsera sola y sin filtro de aparato, quien consiga una lectura fresca de la pulsera y averigüe el PIN puede entrar a datos de salud **desde su propio móvil, fuera de la residencia**. El PIN es corto: un PIN de 6 cifras solo se protege con el límite de intentos. Sigue un riesgo de segundo orden, el uso de los registros como control laboral (horarios y ubicación), que se evita con diseño y con papeles.

**Dos cosas que conviene saber antes de comprar nada:**
1. Para la residencia, el PIN es una pieza que ResidApp tiene que custodiar. El ADR 0007 ya lo dice (`docs/decisiones-arquitectura/0007-proveedor-identidad.md:50`: «obliga, con cualquier proveedor, a que ResidApp custodie los PIN y verifique la pulsera»). Hay una alternativa que **evita custodiar el PIN**: una llave de seguridad FIDO2 con NFC y PIN propio, que reutiliza lo ya construido en I4 (apartado 2.2). Cuesta más por unidad. Conviene decidir si se descarta **antes** de comprar el lote.
2. Sí hay hallazgos técnicos que cambian la compra: activar Random ID es irreversible, hace falta una clave compartida por centro y no se debe leer el número de serie fijo (apartado 2.9).

---

## 1. Qué he leído y cómo está la base normativa

**Del repositorio**
- ADR 0007 completo.
- ADR 0009 completo.
- ADR 0016 completo.
- `docs/privacidad/dispositivos-autorizados-lectura-dpo.md`.
- `docs/privacidad/antes-de-datos-reales.md`.
- `docs/seguridad/acceso-por-dispositivo.md` (en parte).
- Scripts `0061` y `0062`.
- `SessionSignIn.cs`, `SessionPolicy.cs`, `AccesoOptions.cs`, `ProfilePermissions.cs`.
- Búsqueda de «pulsera/NFC» en `src/` y `docs/`.

**[Hecho]** No hay código ni SQL de la pulsera. La mención más reciente en la política de privacidad dice «Pulsera NFC con PIN (I5), NFC en camas | No construido | No aparece» (`docs/privacidad/politica-privacidad-portal.md:324`). `Privacy.cshtml` no menciona la pulsera.

**Base normativa local** (`normativa-local/INDICE.md`, generado el 2026-11-14)
- La comprobación más antigua tiene 8 días. Ejecuté `tools/normativa.ps1 -Comprobar` (solo informa, no escribe): **hay versiones nuevas de 4 textos**.
- Dos de esos textos los cito: **Estatuto de los Trabajadores** (local: BOE actualizado el 2026-11-10; hay otro del 2026-11-19) y **Llei 32/2010** (local: 2026-07-13; hay otro del 2026-11-17).
- No los he actualizado porque pediste no tocar ficheros. Los artículos que cito de ellos (20, 20 bis, 34.9 y 64.5.f del ET; art. 3 de la Llei 32/2010) **no los he cotejado con las versiones nuevas**: [A verificar].
- Todos los textos de `normativa-local` son consolidados del BOE o de la UE, no la versión oficial.

**De la web (resúmenes, no texto literal salvo donde digo)**
- La nota de aplicación **NXP AN12196** (PDF de 2025-03-05). Bajé el PDF, extraje el texto y cito frases literales. No está en el catálogo; si se va a usar más, conviene añadirla a `catalogo.json` como `pdf`.
- La documentación de Chrome sobre Web NFC, y NIST SP 800-63-4. De NIST solo pude confirmar principios, no cifras (apartado 2.2).

---

## 2. Respuestas a las nueve preguntas

### 2.1 Pregunta 1: ¿es admisible? Comparación de riesgo y salvaguardas

**Comparación**

| | Contraseña (12 o más) + TOTP | Passkey (llave de acceso) | Pulsera + PIN (diseño propuesto) |
|---|---|---|---|
| Algo que se sabe | Contraseña larga [Hecho: `AccesoOptions.cs:18`] | Nada que ResidApp custodie | PIN de 4 a 6 cifras (muy débil por sí solo) |
| Algo que se tiene | El móvil con el código | El aparato, desbloqueado con huella, cara o PIN local | La pulsera (chip con clave AES) |
| Resiste a una web falsa | No (el código se puede reenviar) | **Sí**: la llave solo vale en el dominio de ResidApp | **No**: una web falsa con permiso NFC puede leer la pulsera y pedir el PIN [Suposición] |
| Qué custodia ResidApp | Resumen de la contraseña; clave TOTP **en claro** [Hecho: `0007:99`] | Solo la clave pública | **Claves AES de las pulseras + comprobante del PIN**: más material sensible |
| Bloqueo | 5 fallos, 15 min [Hecho: `AccesoOptions.cs:13-15`] | Lo da el aparato | Hay que construirlo (apartado 2.2) |
| Alcance de la sesión | Completo | Completo | **Reducido** (condición clave) |

**Lectura.** Pulsera+PIN es comparable a «tarjeta del cajero + PIN». Es aceptable porque el ataque exige tener el objeto físico, los intentos son pocos y el daño está acotado por el límite de lo que se puede hacer. No es aceptable como sustituto de la contraseña ni de la passkey con alcance completo. Tu diseño (8) lo recoge: es una vía adicional, apagable por centro, que no sustituye a nada.

**Hay un efecto agravante que el diseño no recoge.** El mensaje de la pulsera no lleva un reto del servidor. Una lectura robada con el móvil (a 4 cm, unos segundos) sigue siendo válida hasta que la pulsera se use otra vez con éxito. No caduca por tiempo [Suposición derivada del funcionamiento de SDM; AN12196 describe que cada lectura genera el mensaje en el chip]. Por eso el PIN con bloqueo es lo que sostiene el sistema, y por eso el filtro de aparato importa (apartado 2.6).

**Salvaguardas que lo hacen proporcionado.** Todas deben estar. Solo con las cuatro primeras no basta.
1. Alcance reducido en el servidor, con lista blanca (2.1.3).
2. Bloqueo de la pulsera por intentos fallidos y desbloqueo solo por Administración.
3. Solo aparatos compartidos registrados (B corregida).
4. Solo el perfil Auxiliar (2.1.2).
5. Inactividad corta (propuesta: 5 minutos; tope absoluto de 4 horas). Hoy la política de sesión es global [Hecho: `SessionPolicy.cs:32-44` y `Program.cs:120-121`, 30 min y 12 h]. Hace falta un valor por tipo de sesión.
6. Apagable por centro y apagada por defecto.
7. Registro de cada entrada y de cada fallo con el método «pulsera».

**Qué perfiles no pueden usarla nunca**

| Perfil | Motivo |
|---|---|
| **Plataforma** | Cuenta más sensible del sistema (ADR 0006). El ADR 0016 (decisión 1) la protege con la restricción siempre activa. Un PIN no puede abrirla. |
| **Administración** | Con una pulsera podría gestionar cuentas, permisos y otras pulseras: se emitiría a sí misma credenciales. |
| **Dirección Clínica** | Lee indicadores, calidad e historiales y cierra episodios (permisos `QUALITY_REVIEW`, `CLINICAL_DETAIL_READ`: `ProfilePermissions.cs:41`). |
| **Familiar** | Fuera de alcance (ADR 0009, ADR 0016 decisión 2). |

**Enfermería y Medicina.** Tu pregunta era si solo Auxiliar, como dice el ADR 0009 (`0009:23-24`: «pulsera del auxiliar»). Recomiendo **solo Auxiliar en la primera versión**:
- **[Hecho]** El perfil Auxiliar **no tiene permisos configurables** (`ProfilePermissions.cs:7, 37-43`): sus capacidades ya son estrechas. Eso hace pequeño el riesgo.
- Enfermería y Medicina sí los tienen: foto, antecedentes, citas externas, traslados y basal (`ProfilePermissions.cs:39-40`). Con una pulsera habría que vetar justo lo que usan.
- La historia clínica en Cataluña exige identificar a quien hace cada cambio. Texto literal de la Llei 21/2000, art. 9.3 (`llei-21-2000.txt:82`): «debe garantizarse que quedan registrados todos los cambios e identificados los médicos y los profesionales asistenciales que los han realizado». Una entrada con PIN corto y objeto prestable debilita la atribución, y ese debilitamiento pesa más donde hay firma.
- **[Decidir]** CJ. Si más adelante se amplía, las acciones con firma o con efectos legales se vedan siempre.

**Acciones que una sesión de pulsera nunca debe poder hacer.** Recomiendo **lista blanca**, no lista negra: solo lo que CJ apruebe para «Auxiliar de planta» (abrir la ficha operativa, registrar cambios, el cierre cotidiano, el dictado). Todo lo demás exige entrar con contraseña+TOTP o passkey. En concreto, vedado siempre:
- Mi cuenta: cambiar contraseña, passkeys, códigos de recuperación o PIN, y alta o baja de pulseras.
- Administración de cuentas, permisos, ámbitos, invitaciones y vinculaciones.
- Firmar o cerrar nada con efectos legales (basal, informe de derivación, cierre excepcional).
- Editar fichas de antecedentes (alergias, enfermedades, medicación), traslados y bajas.
- Dirección, calidad, indicadores, auditoría, historial tras traslado y declaraciones de acceso clínico.
- Exportaciones y PDF no operativos.

El ENS respalda este escalón (texto literal en `ens-rd-311-2022.txt:916`, op.acc.6.r6.1): «Se definirán aquellos puntos en los que el sistema requerirá una renovación de la autenticación del usuario, mediante identificación singular, no bastando con la sesión establecida». Solo es obligatorio si el centro está en el ámbito del ENS (ver 2.6). Las pruebas deben cubrir cada acción vedada.

**La restricción vive en el servidor.** La sesión se valida contra `dbo.sesiones` en cada petición [Hecho: ADR 0016, línea 20]. La cookie del ámbito activo **no es de fiar** (ADR 0016, línea 21: va sin cifrar ni firmar). Por tanto, el tipo de sesión (pulsera) debe guardarse en la fila de sesión y comprobarse en la autorización. Ese es el mismo cambio que pide el ADR 0016, que hoy no pasa la sesión a `SqlAuthorizationEvidenceProvider`. Hacer los dos juntos ahorra trabajo.

**La pulsera se liga a un ámbito, no a la cuenta entera.** Tu diseño (2) dice «una cuenta de personal». Corrijo: debe ligarse a **la cuenta, el centro y el perfil Auxiliar de ese centro**. Si una persona es Auxiliar en un centro y Administración en otro (cuentas en varios centros: `0007:74`), la pulsera no puede abrir nada del otro. La sesión de pulsera no elige ámbito: nace con el del alta de la pulsera. Si la cuenta pierde ese ámbito, la pulsera deja de servir.

---

### 2.2 Pregunta 2: custodia del PIN

**¿Basta un resumen con sal?** **No, por sí solo.** [Suposición, cálculo mío]
- Un PIN de 6 cifras tiene un millón de posibilidades; uno de 4 cifras, diez mil.
- Con un resumen SHA-256 corriente, un ordenador prueba un millón en un instante. Aun con una función lenta (0,1 s por intento), son unas 28 horas en un solo procesador y minutos con varios.
- Si se filtra la base de datos, todos los PIN se recuperan. Y como mucha gente reutiliza el PIN de su tarjeta bancaria, el daño va más allá de ResidApp.

**¿Hace falta pimienta?** **Sí.** Es el control principal.
- Comprobante guardado = HMAC-SHA-256 (clave secreta, sal de esa pulsera + PIN), con la clave secreta **fuera de la base de datos** (Key Vault en producción; secretos de usuario con valores de prueba en dev).
- Versionar la pimienta. Al acertar un PIN se recalcula con la pimienta vigente (el PIN solo se conoce entonces). Así se puede rotar sin pedir PIN nuevos a todos.
- **Límite honesto:** quien robe la base **y** la pimienta (por ejemplo, tomando el control del servidor) recupera todos los PIN. Por eso el límite de intentos, el alcance reducido y la B corregida deben estar aunque la pimienta exista.

**Longitud.** **6 cifras como mínimo.** El principio de NIST SP 800-63-4 que sí pude confirmar es que el bloqueo por intentos es el control de un PIN de activación; la cifra de 6 dígitos la vi en un blog, no en la norma. [A verificar]. Probabilidad de acertar con 5 intentos:
- 4 cifras: 0,05 %.
- 6 cifras: 0,0005 %.

Si CJ o Orlando quieren 4 cifras por usabilidad, solo con bloqueo a los 3 fallos [Opinión]. Además:
- Prohibir PIN triviales: cifras repetidas (000000), secuencias (123456, 654321), pares repetidos (121212) y una lista corta de los más habituales.
- No se pide «fecha de nacimiento» ni nada derivado de datos personales.
- La información al personal dice: «No uses el PIN de tu tarjeta».

**Bloqueo.** [Opinión]
- Contador **por pulsera, en SQL** (no en memoria: la comprobación por IP del acceso actual vive en memoria y se reinicia con cada despliegue, `0007:73`).
- **5 fallos seguidos → pulsera bloqueada hasta que Administración la desbloquee.** Sin desbloqueo automático. Con desbloqueo cada 15 minutos, un atacante probaría 480 PIN al día.
- **Contar el intento antes de comprobar el PIN**, en la misma sentencia SQL, para que 20 peticiones simultáneas no multipliquen los intentos.
- Comparación en tiempo constante.
- **Contador separado del de la contraseña.** Si se compartieran, quien consiga leer la pulsera bloquearía la entrada con contraseña de la persona.
- Mismo mensaje para todo fallo. El ENS lo pide: op.acc.6.7 (`ens-rd-311-2022.txt:898`): «si se rechaza, no se informará del motivo del rechazo». Y op.acc.6.8 (línea 899): «bloqueando la oportunidad de acceso una vez superado tal número, y requiriendo una intervención específica para reactivar la cuenta».
- **El bloqueo estricto cuesta poco**: queda la contraseña+TOTP y la passkey. El riesgo de que alguien bloquee la pulsera de otra persona a propósito es molestia, no pérdida de acceso.

**Validar la pulsera y el PIN en una sola petición.** [Opinión] La lectura (código de la pulsera) y el PIN viajan juntos en un POST. Así:
- No se revela si la pulsera existe, está revocada o bloqueada antes de dar el PIN. El ENS (op.acc.6.7) pide algo equivalente: «Las credenciales solamente se validarán cuando se tengan todos los datos necesarios».
- No hace falta estado intermedio ni cookie nueva.

**¿Se puede evitar custodiar el PIN?**
- **PIN solo local en el aparato: no.** En una tablet compartida cada persona tendría que registrarlo en cada tablet, y una comprobación en el navegador se puede saltar. No aporta seguridad.
- **El chip NTAG 424 DNA no tiene PIN.** No se puede verificar «en la pulsera».
- **Alternativa real: llave FIDO2 con NFC y PIN (o tarjeta FIDO2).** El PIN lo comprueba la propia llave, que cuenta sus intentos y se bloquea. ResidApp no custodia PIN ni claves AES, resiste a webs falsas y reutiliza lo construido en I4 (passkeys). Contras:
  - Cuesta bastante más por unidad que un NTAG (del orden de decenas de euros frente a un euro o dos) [A verificar con presupuestos].
  - Suele venir como llavero o tarjeta, no como pulsera de silicona.
  - No he comprobado el soporte de FIDO2 por NFC en Chrome para Android con las tablets reales [A verificar].
  - Con una passkey «sin atestación» (`0007:76`) hoy no se distingue una llave itinerante de una passkey de móvil; habría que guardar si es «cross-platform».
- **[Decidir]** Orlando, antes de comprar: si descarta la alternativa por coste o formato, la custodia del PIN es inevitable y debe hacerse como arriba.

**Cómo se pone y se cambia el PIN.** [Opinión]
- Lo elige la persona en «Mi cuenta» pidiendo contraseña y código TOTP (como las passkeys: `0007:76`). **Administración nunca lo ve ni lo fija.**
- Si no, Administración podría entrar como cualquier auxiliar (y la atribución en la historia clínica dejaría de valer).
- El reinicio por Administración deja la pulsera «sin PIN» y la persona pone uno nuevo.

---

### 2.3 Pregunta 3: datos personales de la pulsera, vigilancia laboral y consulta

**Qué datos son y de quién.**
- Los de la pulsera son **datos de personal**, no de salud.
- Texto literal del RGPD, art. 4.1 (`rgpd.txt:43`, versión consolidada 2016-05-04): «se considerará persona física identificable toda persona cuya identidad pueda determinarse, directa o indirectamente, en particular mediante un identificador, como por ejemplo un nombre, un número de identificación, datos de localización, un identificador en línea…».
- El identificador del chip ligado a una cuenta, el contador, los fallos y las entradas son datos personales.

| Dato | Comentario y mínimo |
|---|---|
| Identificador del chip | Guardar un resumen con clave (HMAC) y no el número en claro [Suposición]. Se borra al revocar o al dar de baja. |
| Cuenta, centro y ámbito de la pulsera | Necesario. |
| Estado, alta, baja y motivo (lista cerrada) | Necesario. |
| Contador del chip | **Solo el último valor aceptado**, sin historial. Dice cuántas veces se leyó (incluidas lecturas ajenas): dato menor, pero es de uso. |
| Intentos fallidos y bloqueo | Necesario, con su contador. |
| **Día** del último uso (no la hora) | Misma lógica que la garantía G5 del ADR 0016 (`dispositivos-autorizados-lectura-dpo.md:124`). |
| Comprobante del PIN | Solo como se describe en 2.2. |
| Eventos de entrada y de fallo | Cuenta, hora, IP del centro y método «pulsera». Ver abajo y apartado 5.2. |

**Base jurídica.** [Decidir] DPO real; lectura mía:
- **Interés legítimo del centro** (art. 6.1.f, `rgpd.txt:87`): proteger los datos de salud que custodia, con ponderación escrita. Es la base más probable si el centro es privado.
- **Atención: si el centro es público**, el último párrafo del art. 6.1 dice (`rgpd.txt:88`): «Lo dispuesto en la letra f) del párrafo primero no será de aplicación al tratamiento realizado por las autoridades públicas en el ejercicio de sus funciones». Entonces haría falta 6.1.c (obligación legal; el art. 32 RGPD y el ENS) o 6.1.e.
- **Posible alternativa:** art. 6.1.b (ejecución del contrato laboral), porque la credencial es un medio para hacer el trabajo.
- **No consentimiento**: un trabajador no puede consentir libremente ante su empleador.
- Art. 32 RGPD (`rgpd.txt:374`): «medidas técnicas y organizativas apropiadas para garantizar un nivel de seguridad adecuado al riesgo».

**Marco laboral (textos literales de `normativa-local`).**
- **RGPD art. 88.2** (`rgpd.txt:959`): las normas laborales «incluirán medidas adecuadas y específicas para preservar la dignidad humana de los interesados así como sus intereses legítimos y sus derechos fundamentales, prestando especial atención a la transparencia del tratamiento […] y a los sistemas de supervisión en el lugar de trabajo».
- **LOPDGDD art. 87.3** (`lopdgdd.txt:702`): «Los empleadores deberán establecer criterios de utilización de los dispositivos digitales respetando en todo caso los estándares mínimos de protección de su intimidad […]. En su elaboración deberán participar los representantes de los trabajadores». **[Opinión]** La pulsera es un medio digital que el empleador pone a disposición del trabajador. El art. 87 está escrito para el contenido de los dispositivos, y la pulsera no guarda contenido, pero sus criterios de uso (custodia, no prestarla, qué pasa si se pierde) sí caen en la lógica del artículo.
- **ET art. 64.5.f** (`estatuto-trabajadores.txt:1135`): el comité de empresa tiene derecho a emitir informe previo sobre «la implantación y revisión de sistemas de organización y control del trabajo». Si la pulsera es solo una credencial, probablemente no es eso, pero **un sistema que deja «quién entró y cuándo» lo parece si se usa mal**. Lo decide el laboralista. [A verificar con la versión del 2026-11-19]
- **ET art. 20.3** (`estatuto-trabajadores.txt:365`): el control del empresario debe guardar «la consideración debida a su dignidad».
- **ET art. 34.9** (`estatuto-trabajadores.txt:500`): «La empresa garantizará el registro diario de jornada». Hay **tentación evidente** de usar la lectura de la pulsera como registro de jornada. Eso sería otra finalidad, con su propia base y su consulta, y está excluido aquí.

**Riesgo de vigilancia: dónde está realmente.**
1. **Registrar cada lectura de la pulsera** («Ana, 06:58, tablet de la planta 2») crea un registro de horarios y de ubicación dentro del centro. **No lo registres.**
2. **Registrar qué tablet se usó en cada entrada**: lo mismo, convierte la tablet en una ubicación. En tablets compartidas el «aparato» identifica un lugar de trabajo.
3. **El contador** no da hora ni lugar; el riesgo es bajo.
4. **Lo que sí se registra** (entrada con éxito, fallo con PIN, bloqueo, desbloqueo) ya es del tipo de lo que se guarda hoy (cuenta, hora, IP del centro: `Privacy.cshtml:72`, según la lectura previa; no lo he releído).

**Finalidad cerrada (misma redacción que el ADR 0016, G2).** El dato de la pulsera sirve para decidir si se abre una sesión y para investigar incidentes de seguridad. **Nunca** para horarios, jornada, productividad, ubicación o evaluación. En el informe, ninguna lista ni ranking de lecturas por persona.

**Quién ve qué.**
- Administración ve, por pulsera: cuenta, estado, fecha de alta, **día** del último uso y si está bloqueada. No ve horas ni historial de lecturas.
- La persona ve sus últimos accesos en «Mi cuenta» (`0007:75`), con el método «pulsera». El ENS lo pide en su refuerzo: op.acc.6.r5.2 (`ens-rd-311-2022.txt:914`): «Se informará al usuario del último acceso efectuado con su identidad».
- No sé quién puede leer `eventos_acceso` más allá de esa vista: [A verificar] (no hay pantalla de Administración que yo haya visto).

**Plazo de conservación.**
- Entradas y fallos: **el que el DPO fije para toda la tabla `eventos_acceso`**. Hoy no hay plazo ni vía de borrado [Hecho: triggers `INSTEAD OF` en `0062:49-55`; `antes-de-datos-reales.md:134`]. Punto de partida de discusión (de la lectura previa, `dispositivos-autorizados-lectura-dpo.md:251`): 12 meses y poner antes la IP a NULL.
- Fila de la pulsera: al revocar o dar de baja, borrar el identificador del chip y borrar la fila a los **12 meses** [Propuesta, mismo patrón que el ADR 0016, línea 45].
- Acciones de Administración (alta, baja, desbloqueo): el plazo de la auditoría administrativa, **sin decidir**.
- Copias de seguridad: [A verificar] (defecto de 7 días, `expurgo-y-supresion.md:78`).
- **Bloqueante**: cada evento nuevo de pulsera añade filas con IP que hoy **no se pueden borrar**. Hay que construir la vía (procedimiento `EXECUTE AS OWNER`, patrón `0085`: `antes-de-datos-reales.md:160`) antes de datos reales.

**¿Asesoría laboral o consulta a la representación?** **Sí, recomendable antes de activar con datos reales.**
- Un laboralista confirma: si la pulsera es obligatoria o voluntaria, cómo se informa o consulta a la representación (LOPDGDD 87.3; ET 64.5.f), quién paga una pulsera perdida (lo prudente es que no se descuente al trabajador), el régimen disciplinario del uso indebido y el convenio aplicable [A verificar].
- **Recomendación:** que la pulsera sea **voluntaria**. Quien prefiera contraseña+TOTP o passkey sigue pudiendo. Es lo que mejor sostiene la proporcionalidad.

---

### 2.4 Pregunta 4: pérdida, robo, préstamo y suplantación

**Revocación inmediata.**
- **Quién:** Administración del centro, en la ficha de la cuenta (efecto inmediato). También **la propia persona** desde «Mi cuenta» con otra credencial (contraseña+TOTP o passkey), que es la vía si no hay nadie de Administración. Plataforma no revoca pulseras de un centro, salvo la regla de cuentas en varios centros que ya existe (`0007:74`).
- **Efecto:** la pulsera pasa a «revocada» y **se cierran las sesiones abiertas con método pulsera de esa cuenta**. Para eso basta guardar el método en la sesión; no hace falta guardar *qué pulsera* abrió cada sesión. Es más minimizado que el vínculo del ADR 0016 (decisión 10). Hay que ampliar la lista de motivos de cierre (`CK_sesiones_cierre`, `0061:22-24`, `NVARCHAR(20)`): por ejemplo `PULSERA_REVOCADA` tiene 16 caracteres. [A verificar] la lista vigente tras scripts posteriores.
- **Se revoca también** al suspender la cuenta, al perder el ámbito del centro y con la invitación de recuperación (si se sospecha robo de credenciales, una pulsera nueva puede haber sido dada de alta por el atacante).
- **El fuera de horas es organización del centro** (ADR 0016, decisión 11): la app solo ofrece la función. No diseño guardias.

**Qué queda en auditoría.** Solo identificadores y motivos de lista cerrada, sin texto libre.
- En `dbo.eventos_acceso` (autenticación): `ENTRADA_PULSERA`, `PULSERA_PIN_FALLIDO`, `PULSERA_BLOQUEADA`, `PULSERA_REVOCADA_USADA` (alguien intenta usar una pulsera revocada; con la cuenta de su dueña, como aviso de seguridad). Longitudes de tipo ≤ 32 (`0062:36`). Hay que ampliar el `CHECK` (`0062:39-41`).
- En la auditoría administrativa: alta, activación, baja (motivo: pérdida, robo, sustitución, baja del personal, sospecha), desbloqueo y reinicio de PIN.
- **No** un evento por «lectura» ni por aparato.

**Suplantación entre compañeras (la pulsera no es biométrica).** Riesgo **real y no eliminable**: en residencias es habitual compartir credenciales por prisa. Con una pulsera prestada y el PIN conocido, la entrada sale a nombre de la dueña. Mitigaciones, de más a menos eficaces:
1. **El PIN lo elige y solo lo conoce la persona** (Administración no lo ve).
2. **Sesión de alcance reducido y corta** (si una compañera la usa, hace poco y sin firmar).
3. **Pantalla que muestra siempre quién tiene la sesión** (nombre visible en la cabecera) y un botón claro «Soy otra persona / Cerrar sesión».
4. **La persona ve en «Mi cuenta» sus últimas entradas con pulsera**: así detecta una que no hizo.
5. **Alertas sencillas**: dos sesiones de pulsera de la misma cuenta a la vez en dos tablets distintas.
6. **No hay pulseras de préstamo ni «de invitado»**: se perdería la atribución. Si alguien olvida la suya, entra con contraseña+TOTP.
7. **Cierre precintado de un solo uso** en la pulsera: dificulta prestarla, pero tiene implicaciones laborales (llevar algo puesto) [Opinión, consultar al laboralista].
8. **Política escrita del centro**: no prestar la pulsera ni el PIN; qué consecuencias; cómo avisar. Es del centro, no de la app.

Residual: **media**. La atribución en la historia clínica depende de esta política.

---

### 2.5 Pregunta 5: claves del chip y proveedor

**¿Es encargado o subencargado quien codifica las pulseras?**
- **[Opinión] Probablemente ninguno de los dos**, si solo recibe chips anónimos y las claves de ResidApp, y **no recibe nombres, listas de personal ni asignaciones**. Las claves y los números de serie de chips sin asignar no son datos personales hasta que se ligan a una persona dentro de ResidApp. Entonces es un proveedor de bienes con acceso a un secreto de seguridad.
- **Sí sería encargado** (art. 28 RGPD, `rgpd.txt:324-349`) si recibiera datos del personal (por ejemplo, grabado de nombres). Entonces ResidApp, que ya es encargada de la residencia, necesitaría autorización previa por escrito del centro para recurrir a él (28.2, `rgpd.txt:328`) y trasladarle las mismas obligaciones (28.4, `rgpd.txt:343`). **Se evita no enviándole ningún dato personal.**
- Las pulseras salen anónimas, sin nombre, foto ni número de empleado. Si lleva un número visible, es de lote y no coincide con el número de serie del chip.

**Cláusulas del contrato de suministro** (no son cláusulas del art. 28 si no hay datos personales):
- Confidencialidad de las claves, y que las claves no se retienen tras el trabajo (destrucción comprobable).
- Claves distintas por lote y por centro; nunca reutilizar entre clientes.
- Entrega de claves por canal cifrado.
- No subcontratar la programación sin permiso.
- Aviso inmediato de cualquier incidente que afecte a las claves.
- Certificado de autenticidad del chip y especificación completa (apartado 2.9).

**Cambio de clave al recibir (recomendado, [Suposición] a confirmar con el fabricante).** La mejor opción es que **el proveedor no conozca las claves finales**. Programa con claves de transporte y ResidApp las cambia al recibir, con un lector USB. Así, si el proveedor las filtra, ya no valen.
- La hipótesis de Orlando (7) se confirma: «Web NFC no puede programar claves de un NTAG 424 DNA». La documentación de Chrome indica que solo cubre datos NDEF y no el acceso de bajo nivel (ISO-DEP), según resúmenes de búsqueda [A verificar con la fuente completa]. Y el cambio de clave del chip va por comandos (`ChangeKey`, `AuthenticateEV2First`, nota AN12196).
- Hace falta una **herramienta de personalización fuera de la PWA** (un PC con lector USB o una app nativa interna). Su equipo y quién lo usa forman parte de la cadena de custodia. Hoy hay una sola persona que opera ResidApp (contexto del ADR 0007).

**Dónde guardar las claves y quién accede.**
- **Fuera de la base de datos, del repositorio y de los registros.**
- **Producción:** Key Vault de la UE, con identidad gestionada, roles mínimos, borrado suave y protección contra purga, y registro de accesos. [Hecho] Key Vault solo aparece en la propuesta de producción (ADR 0014, líneas 66 y 136). [A verificar] su replicación y región (`entorno-produccion-lectura-dpo.md:78`).
- **Desarrollo:** `dotnet user-secrets` o ajustes del entorno con **claves de prueba**, nunca reutilizadas en producción.
- **Límite honesto:** la aplicación necesita la clave **en memoria** para comprobar cada firma (Key Vault estándar no calcula el CMAC por nosotros) [Suposición, a verificar]. Key Vault protege el reposo y los permisos, no la memoria del servidor. Si se compromete el servidor, se pueden fabricar lecturas válidas.
- **Quién accede:** solo la persona que opera Plataforma, con registro. Con un único operador no hay doble control (limitación a declarar).
- **Separar** la clave de la pimienta del PIN y las claves de cada pulsera.

**Rotación.** La rotación de las claves de las pulseras equivale a **cambiar las pulseras**, porque el cambio de clave del chip exige una herramienta nativa, no se hace por la PWA. Por tanto: [Opinión]
- Compra un 15 % de reserva por lote.
- Guarda un identificador de versión de clave (`kid`) para convivir con dos versiones durante un cambio.
- Rotación ordinaria al reponer pulseras y rotación de emergencia ante sospecha. No hay rotación periódica por calendario.

---

### 2.6 Pregunta 6: restricción de aparato (ADR 0016) y la opción B

**Riesgo adicional sin filtro de aparato.**
- **Cualquier navegador Chrome en Android puede leer la pulsera y pedir el PIN.** Una lectura fresca por proximidad (más el PIN, por observación o por probabilidad) permite entrar desde un móvil ajeno **fuera de la residencia**. La contraseña+TOTP ya permite eso a quien la tenga, pero la pulsera añade un camino con un secreto mucho más corto.
- Si se roban las claves del servidor, el atacante podría fabricar lecturas desde Internet sin tener ninguna pulsera. Lo único que lo frenaría es no reconocer su aparato.
- **El ENS ayuda a ver el criterio**: op.acc.6.r1.1 (`ens-rd-311-2022.txt:902`) admite la contraseña sola solo cuando se accede «desde zonas controladas y sin atravesar zonas no controladas»; y op.acc.6.r8.1 (línea 921): «Para el acceso desde o a través de zonas no controladas se requerirá un doble factor de autenticación: R2, R3 o R4». Pulsera+PIN no es R2, R3 ni R4. [A verificar] con las guías CCN-STIC si el centro está en el ámbito del ENS (público o con prestación pública: art. 2 del RD 311/2022, `ens-rd-311-2022.txt:68-70`). Esto apoya que la pulsera solo valga desde aparatos de la residencia.

**Recomendación: la «B corregida».** Sí, **exigir que la entrada con pulsera solo se ofrezca desde aparatos marcados como «compartidos» por Administración, aunque el centro no tenga restricción general**. No hace falta que todo el ADR 0016 esté construido. Con ese orden:
1. **D0** (tablas), **D1** (clave del aparato) y **D2** (registro por QR y pantalla «Aparatos»), tal como los define el ADR 0016, líneas 59-61. Con ellos, la pulsera comprueba «¿este aparato es compartido y está registrado?».
2. Después, la pulsera (I5).
3. Más adelante, **D3 a D5** (comprobación al elegir el centro, petición pendiente, interruptor por centro). El resto de aparatos queda sin tocar.
- Así **no se rehace nada**: la pulsera ya usa la regla de la decisión 12 del ADR 0016 y la restricción general se añade después. Eso es lo que quería la opción B.
- **No** crea peticiones pendientes (decisión 12): en un aparato desconocido se entra con contraseña o passkey.

**Si Orlando mantiene la B pura**, sin D0-D2:
- Solo con datos falsos en dev, nunca con datos reales.
- Apagada por defecto en todos los centros y activable por centro con instrucción documentada.
- Compensación parcial: una lista opcional de la IP pública del centro por la que se acepta la pulsera. [Opinión] Es frágil (IP cambiante, wifi de visitas con la misma salida) y no sustituye a registrar el aparato.
- Registrar en la EIPD el riesgo aceptado, con responsable y fecha.
- Dejar **un único punto de decisión** en el código («¿se ofrece la pulsera desde este aparato?»). Hoy responde «sí»; después lo responde la tabla de aparatos.

---

### 2.7 Pregunta 7: información, registro de actividades, EIPD y política

**Al personal (texto P, borrador en plano).** Lo adopta la residencia como suyo. Catalán si se pide. Corchetes los rellena el centro.

> **Pulsera con PIN para entrar en ResidApp**
>
> **[RESIDENCIA]** ofrece, **si tú quieres**, una pulsera para entrar más rápido en las tablets compartidas de planta. Quien decide y responde de esta medida es **[RESIDENCIA]**; ResidApp, que presta la aplicación, trabaja siguiendo sus instrucciones.
>
> **Cómo funciona.** Acercas la pulsera a la tablet y escribes tu PIN de 6 cifras, que solo conoces tú. Con ella se abre una sesión **con menos permisos que una entrada normal** (solo lo necesario para tu trabajo de planta) y se cierra enseguida si no la usas. Para otras tareas, entras como siempre.
>
> **Qué se guarda.** Un identificador de la pulsera ligado a tu cuenta; su estado (activa, bloqueada, retirada) y las fechas; **el día** de su último uso; los intentos fallidos; y una versión protegida de tu PIN (nadie, ni ResidApp, puede leerlo). Cada entrada y cada fallo quedan anotados con tu cuenta, la hora, la dirección IP de la residencia y que fue con pulsera.
>
> **Qué no se hace.** No se registra cada vez que acercas la pulsera, ni en qué tablet. **No se usa para controlar tu horario, tu jornada, dónde estás ni tu rendimiento.**
>
> **Qué tienes que hacer.** No prestes tu pulsera ni tu PIN. Si la pierdes, avisa a **[persona o cargo]** o retírala tú en «Mi cuenta».
>
> **Para qué.** Para proteger los datos de salud de los residentes. Base jurídica: **[el DPO confirma]**.
>
> **Cuánto tiempo.** **[plazos]**. Cuando se retira la pulsera, se borra su identificador y la fila a los **[12 meses]**.
>
> **Tus derechos.** Acceso, rectificación, supresión y los demás ante **[contacto]**; reclamación ante **[AEPD o APDCAT]**.

**A residentes y familias.** No son titulares de estos datos y la medida no trata datos suyos, así que **no hace falta una cláusula propia**. Una línea en la información general del centro es suficiente y honesta: «El acceso del personal a la aplicación se protege con contraseña y código del móvil, llave de acceso o, en tablets compartidas, pulsera con PIN, y se registra».

**Registro de actividades (art. 30).**
- **Residencia (30.1):** una línea nueva «Acceso del personal con pulsera NFC y PIN» (o ampliar la de «gestión de accesos» si existe). Contenido: finalidad (seguridad del acceso), personas (personal), datos (apartado 2.3), destinatarios (Administración del centro; ResidApp como encargada; Azure), plazos, medidas.
- **ResidApp como encargada (30.2):** una línea por cada centro que la active.
- **No hay línea de ResidApp como responsable**: Plataforma queda fuera de la pulsera.

**EIPD.** **Anexo** a la EIPD general del acceso, no una EIPD aparte. [Hecho] La política ya anuncia que hace falta una «EIPD previa a producción: voz y salud, acceso con NFC y pulsera, Portal Familiar» (`politica-privacidad-portal.md:404 y 465`). Razones para que la pulsera conste expresamente: la lista de la AEPD incluye «el procesamiento de identificadores únicos que permitan la identificación de usuarios de servicios de la sociedad de la información» como parte de la observación sistemática (`aepd-lista-eipd.txt:59-64`, punto 3) y «el uso de categorías especiales de datos» (punto 4, líneas 65-69), y aquí los dos se combinan. Contenido del anexo: finalidad cerrada, datos, ponderación del interés legítimo, riesgos del apartado 5, medidas del apartado 6, riesgo residual.

**Política de privacidad y documentos que dejarán de ser ciertos al construirlo:**
- `src/ResidApp.Web/Views/Home/Privacy.cshtml` y `docs/privacidad/politica-privacidad-portal.md` (línea 324 dice «No construido»): añadir los datos de la pulsera en «Personal del centro».
- **Cookies:** si el diseño no usa una cookie de estado entre la lectura y el PIN (lo evito leyendo y comprobando en una sola petición), la tabla de cookies **no cambia**. Si se usa una cookie, debe añadirse al inventario. [A verificar] al construir.
- Web NFC pide un permiso del navegador, pero no guarda datos en el aparato: no es almacenamiento en el sentido del art. 22.2 LSSI. [Suposición]
- `docs/seguridad/acceso-por-dispositivo.md` (líneas 23-28 y 53-58), el Manual, la guía «Acceso y sesión», ADR 0007 (fila I5) y ADR 0016 (decisión 12): pasar por el agente `doc-usuario-residapp` al construir.

**Contrato de encargado (apartado 6).** Instrucciones del centro a ResidApp: activar o desactivar la pulsera por escrito, tratamiento solo para esa finalidad, plazos, ayuda para derechos, aviso de brechas y cooperación en la auditoría (art. 28.3, `rgpd.txt:329-342`).

---

### 2.8 Pregunta 8: condiciones previas bloqueantes

Ver apartado 6 (separadas en «código y diseño» y «documentación y validación humana»).

### 2.9 Pregunta 9: especificación de compra de pulseras

**Hallazgos técnicos que condicionan la compra** (fuente: NXP AN12196, PDF de 2025-03-05, texto literal):
- **Random ID.** Su finalidad, según la nota: «This feature is used to retain end consumer privacy, avoid tracking and to meet latest GDPR regulations». Con el número de serie fijo de 7 bytes cualquier lector, sin autenticarse, puede leer un identificador estable de la persona. **Activarlo es irreversible**: «Enabling Random ID feature is irreversible process - meaning that it cannot be disabled once it is enabled». Hay que activarlo desde el primer día y comprobar que el proveedor no lo entrega ya activado o con claves que no son nuestras.
- **Número de serie cifrado en el mensaje.** Con el número de serie y el contador cifrados dentro del mensaje, la clave que los descifra **no puede ser distinta por pulsera** (hay que descifrar antes de saber quién es): «In this case, KSDMMetaRead key shall not be UID diversified and high attention on secure storage on system level of this key is required». Consecuencia: **una clave «meta» compartida por centro** (y por entorno), y la clave de comprobación de la firma, sí distinta por pulsera, derivada del número de serie. Quien robe la clave meta puede **ver** de qué pulsera se trata, pero no fabricar lecturas válidas; quien robe la clave maestra de derivación puede clonarlas todas.
- **Límite de lecturas del chip.** AN12196: «This feature can be a potential risk for DoS attacks». **No se activa.**
- **Contador.** Debe exigirse estrictamente creciente, y solo se avanza si la entrada fue correcta. [Suposición] Una lectura robada sigue valiendo hasta la siguiente entrada correcta (apartado 2.1).
- **Escritura.** Cualquier móvil con Web NFC podría reescribir una pulsera con un mensaje malicioso si el fichero es escribible sin clave. Pedir que la escritura del mensaje esté protegida por clave. [A verificar] los derechos de acceso que trae por defecto.

**Requisitos de la compra**

| Requisito | Motivo |
|---|---|
| Chip **NXP NTAG 424 DNA** original, con certificado de autenticidad. No NTAG 213/215/216 ni MIFARE Classic | Solo el 424 DNA tiene SDM con clave. Hay chips falsificados. [Suposición] |
| Entrega **sin datos escritos por el proveedor** (sin URL de su dominio ni servicio de validación propio) y con claves de transporte o por defecto | Que el proveedor no conozca las claves finales y no meta un tercero en la lectura |
| **Random ID activado por nosotros** al personalizar | Irreversible; evita seguimiento por terceros (arriba) |
| **Sin número de serie en claro dentro del mensaje**, solo cifrado. **Sin dato del centro en la URL** (ruta genérica) | Un tercero que lea el mensaje no debe saber de qué centro es |
| Escritura del mensaje **protegida por clave** | Evita que otro móvil lo reescriba |
| Sin nombre, foto, número de empleado ni código que coincida con nada del sistema | Que el proveedor no reciba ni imprima datos personales |
| Claves de lote por centro y por entorno; certificado de destrucción de las claves de transporte | Cláusulas del apartado 2.5 |
| Pulsera de silicona, con cierre que se pueda **cambiar sin herramientas** (o precintado de un solo uso: ver 2.4, a consultar) y resistente a gel hidroalcohólico y lavado | Uso diario en planta (no es de privacidad) |
| **Lote de prueba de 5 a 10 unidades, antes del lote real**, para probar con las tablets reales: lectura con la pulsera puesta, con guantes, con humedad, y con la opción Random ID. **Las tablets deben tener NFC** y ser Android con Chrome (muchas baratas no lo tienen; el iPad queda fuera: `0009:54-58`) | Evitar comprar lo que no se pueda leer |
| Cantidades: personal con pulsera + **15 % de reserva**; lote numerado; **custodia del lote bajo llave con registro de quién lo recibe**; cuenta de existencias | Si se pierde una caja sin asignar, las pulseras no sirven mientras no se den de alta (el alta exige a Administración y a la persona, apartado 5.1) |
| Declaración del fabricante sobre materiales (silicona en contacto con piel) | Seguridad del producto |

**El PIN no va en la pulsera.** El chip no lo guarda.

**El mensaje que lee el chip lo recibe también un móvil que no use Web NFC.** Si la URL del mensaje apunta a ResidApp, Android puede abrirla en el navegador o aparecerle a un iPhone como aviso. Por tanto, la dirección abierta con GET debe ser inocua (una página estática, sin comprobar ni registrar nada). La comprobación va por POST desde la página. [Suposición, a probar con aparatos]. La alternativa es un tipo de registro NDEF que el sistema no abra solo; hay que probarlo con el chip configurado.

---

## 3. Corrección al diseño propuesto

| N.º | Propuesta | Mi lectura |
|---|---|---|
| (1) | SDM con CMAC, clave AES por pulsera | De acuerdo. Matices: Random ID y número de serie cifrado exigen clave meta compartida por centro (2.9); el servidor guarda el identificador como resumen con clave. |
| (2) | Una cuenta de personal | **Corregir:** ligada al ámbito Auxiliar de un centro, no a la cuenta entera. Solo Auxiliar al principio (2.1). |
| (3) | PIN 4-6, resumen con sal | **Corregir:** 6 cifras, pimienta fuera de la base, bloqueo por pulsera hasta Administración, intento contado antes de comprobar, PIN elegido por la persona (2.2). |
| (4) | Alcance reducido e inactividad corta | De acuerdo, con **lista blanca** en el servidor y tipo de sesión guardado en `sesiones`. Hoy la política de sesión es global. |
| (5) | Cada lectura y cada entrada en `eventos_acceso` | **Corregir:** no registrar «lecturas» ni el aparato; solo entradas, fallos, bloqueos y uso de una pulsera revocada (2.3). |
| (6) | Claves en Key Vault o cifradas | De acuerdo en producción. En dev: secretos de usuario con claves de prueba. Key Vault no resuelve la memoria del servidor. |
| (7) | Pulseras codificadas por el proveedor o por ResidApp | Preferible: el proveedor no conoce las claves finales y ResidApp las cambia al recibir. La hipótesis sobre Web NFC se confirma (2.5). |
| (8) | Vía adicional, apagable por centro | De acuerdo. Apagada por defecto. Cerrar las sesiones de pulsera al apagarla. |

---

## 4. Riesgos y gravedad

| N.º | Riesgo | Gravedad sin medidas | Residual con las medidas |
|---|---|---|---|
| R1 | Entrada desde un móvil ajeno, fuera del centro, con lectura robada + PIN (sin filtro de aparato) | **Alta** | Baja (con aparato compartido registrado); **Alta** con la B pura |
| R2 | PIN débil guardado solo con sal: fuga de la base = todos los PIN (y reutilizados en bancos) | **Alta** | Media (pimienta); Alta si quien roba tiene también el secreto |
| R3 | Suplantación por préstamo de pulsera y PIN: atribución falsa en la historia clínica (Llei 21/2000, art. 9.3) | **Alta** | Media (no se elimina; política del centro) |
| R4 | Uso de los registros como control laboral: jornada (ET 34.9), horarios, ubicación por tablet | **Alta** | Media-Baja (finalidad cerrada, sin lecturas ni aparato, información, laboralista) |
| R5 | Claves AES o clave maestra comprometidas (proveedor, repositorio, registros, memoria): clonación de pulseras | Media | Media-Baja (cambio de clave al recibir, clave por centro, Key Vault; rotación = cambiar pulseras) |
| R6 | Seguimiento de la persona por terceros con el número de serie fijo | Media | Baja con Random ID desde el primer día |
| R7 | Bloqueo a propósito de la pulsera de otra persona (molestia) | Baja | Baja (queda la contraseña y la passkey) |
| R8 | Sesión de alcance reducido mal aplicada (cookie de ámbito no fiable) | **Alta** si falla | Baja con comprobación en servidor y pruebas |
| R9 | Reutilización de una lectura robada (el mensaje no caduca por tiempo) | Media | Baja con PIN + bloqueo + aparato compartido |
| R10 | Web falsa que lee la pulsera y pide el PIN | Media | Baja en tablets compartidas dedicadas; no resiste como la passkey |
| R11 | `eventos_acceso` sin plazo ni vía de borrado: más filas con IP | Media | Baja cuando se construya la purga |
| R12 | Falta de información o de consulta a la representación del personal; obligatoriedad | Media | Baja (voluntaria, texto P, laboralista) |
| R13 | Un móvil ajeno abre el mensaje del chip en el navegador (GET con efectos) | Baja | Baja (página inocua) |
| R14 | Tablets sin NFC o con Chrome distinto | Baja (operativo) | Baja (lote de prueba) |

---

## 5. Notas técnicas de apoyo

### 5.1 Alta, activación y baja (propuesta)
1. **Administración** asocia una pulsera a una cuenta (estado «pendiente de activar»). Su identificador se guarda como resumen.
2. **La persona** la activa en «Mi cuenta» pidiendo contraseña+TOTP: acerca la pulsera y elige su PIN. Administración no lo ve.
3. **Sustitución:** revocar la antigua (cierra sesiones) y dar de alta la nueva. El PIN se vuelve a elegir.
4. **Baja:** al salir el personal, al suspender la cuenta, al perder el ámbito y con la invitación de recuperación. Se borra el identificador del chip.
5. **Interruptor por centro:** apagado por defecto. Quién lo cambia: **[Decidir]**. Para ser coherente con el ADR 0016 (decisión 1) y el 0015, Plataforma lo cambia solo con instrucción documentada del centro y queda en auditoría. Al apagar, se cierran las sesiones de pulsera.

### 5.2 Eventos (lista cerrada, sin texto libre)
- `ENTRADA_PULSERA`, `PULSERA_PIN_FALLIDO`, `PULSERA_BLOQUEADA`, `PULSERA_REVOCADA_USADA` en `eventos_acceso`.
- Alta, activación, baja, desbloqueo y reinicio del PIN en la auditoría administrativa, con identificadores.
- Sin evento de lectura ni de aparato.

### 5.3 Sesión
- Columna con el método de entrada en `sesiones`; inactividad y tope absoluto por método; el cierre al revocar usa un motivo nuevo de ≤ 20 caracteres.
- La entrada con pulsera cierra la sesión que hubiera abierta en esa tablet [Hecho: `SessionSignIn.cs:19, 35-43`], algo útil y a tener en cuenta en las pruebas.
- Un botón visible «Soy otra persona» y el nombre siempre a la vista.

### 5.4 Pruebas
- Lista blanca (cada acción vedada devuelve lo mismo que sin permiso).
- Reutilización del contador, PIN erróneo, bloqueo, revocación, apagado del centro, aislamiento entre centros.
- **Que el PIN no aparece en ningún registro ni evento.**
- Web NFC no se puede probar en un navegador sin cabeza: prueba manual en las tablets reales (skill `probar-en-navegador` para lo que se pueda).

---

## 6. Condiciones

### 6.1 De código y diseño

**Antes de construir (en dev, con datos de prueba):**
1. Decidir B corregida frente a B pura (2.6) y alinear el orden con D0 a D2 del ADR 0016.
2. Decidir si se descarta la alternativa FIDO2 con NFC (2.2).
3. Solo perfil Auxiliar, ligada al ámbito; lista blanca definida por CJ (2.1).
4. Tipo de sesión guardado en `sesiones` y comprobación de la lista blanca en el servidor (no en la cookie del ámbito).
5. Inactividad corta y tope absoluto por tipo de sesión; botón «Soy otra persona».
6. PIN: 6 cifras, lista de PIN prohibidos, lo elige la persona en «Mi cuenta», pimienta versionada fuera de la base, sal por pulsera, contador de fallos en SQL por pulsera, 5 fallos = bloqueo hasta Administración, intento contado antes de comprobar, comparación en tiempo constante, mensaje único.
7. Una sola petición POST con la lectura y el PIN; dirección GET inocua; contador estrictamente creciente que solo avanza con éxito; sin límite de lecturas del chip.
8. Chip: Random ID, número de serie solo cifrado, sin datos del centro en la URL, escritura protegida por clave, clave meta compartida por centro y clave de firma derivada por pulsera, claves por centro y por entorno, versión de clave.
9. Claves fuera de la base y del repositorio y sin aparecer en registros; en dev, valores de prueba.
10. Eventos y datos mínimos del apartado 2.3 (sin lecturas, sin aparato, solo último contador y día de uso).
11. Revocación: Administración y la propia persona; cierra sesiones con método pulsera; también al suspender, perder ámbito o recuperar la cuenta.
12. Interruptor por centro apagado por defecto; al apagar se cierran sesiones.
13. Cada acción vedada exige entrar de nuevo con contraseña+TOTP o passkey.

**Antes de datos reales (además):**
14. Vía de borrado y plazo de `eventos_acceso` (bloqueante).
15. Producción con Key Vault (ADR 0014) y claves de producción distintas de las de dev; ninguna pulsera con claves de dev en producción.
16. Probado con las tablets reales del centro (NFC, Chrome, guantes, humedad, la pulsera puesta).
17. Documentación de usuario, política de privacidad, Manual y guía «Acceso y sesión» actualizados.

### 6.2 De documentación y validación humana
1. **DPO real o abogado:** validar esta lectura; la base jurídica (6.1.f, 6.1.c/e o 6.1.b); la autoridad de control; que no se usa consentimiento; la ponderación.
2. **Laboralista:** voluntariedad; consulta o información a la representación (LOPDGDD 87.3; ET 64.5.f); custodia y coste de la pulsera perdida; régimen disciplinario; exclusión del registro de jornada; convenio aplicable.
3. **Plazo y vía de borrado** de `eventos_acceso`.
4. **Texto P** en castellano y catalán, entregado **antes** de activar; si el centro está en el ENS, constancia de que el usuario conoce sus obligaciones (op.acc.6.2).
5. **Registro de actividades** (centro y ResidApp como encargada) y **anexo de la EIPD** con la ponderación y los riesgos de la sección 4.
6. **Contrato de encargado:** las instrucciones de 2.7.
7. **Contrato de suministro** de pulseras con las cláusulas de 2.5 y la especificación de 2.9. Sin datos personales hacia el proveedor.
8. **Procedimiento del centro:** pérdida y robo, no prestar, desbloqueo, altas y bajas (incluido personal externo y de ETT), custodia e inventario del lote. La app no regula la organización del centro; el procedimiento es suyo.
9. **CJ:** la lista blanca de acciones; confirmar que Enfermería y Medicina quedan fuera de la primera versión.
10. **Verificar con el fabricante** (NXP o el distribuidor) los puntos marcados [A verificar] del apartado 7.
11. Si el centro es **público o concertado**: ENS (op.acc.6, y las guías CCN-STIC sobre factores).
12. **Autoridad de control.** Según el art. 3 de la Llei 32/2010 (`llei-32-2010.txt:25-38`), la APDCAT cubre los tratamientos de instituciones públicas, la Generalitat, los entes locales, entidades vinculadas a ellos y, en el apartado f), a «las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios». Un centro **privado no concertado** corresponde a la AEPD. Para uno **concertado**, [A verificar]: falta saber la titularidad y si tiene plazas concertadas (ya constaba como abierto: `antes-de-datos-reales.md:227`).

### 6.3 Antes de comprar
- **No comprar el lote real todavía.** Solo un lote de prueba (5 a 10 unidades) y solo cuando se hayan decidido los puntos 1, 2 y 8 de 6.1, y la persona que operará ResidApp tenga el lector y la herramienta de personalización.
- Antes de la compra, comprobar que las tablets del piloto tienen NFC.

---

## 7. Lo que no he podido confirmar

1. **Estatuto de los Trabajadores y Llei 32/2010:** hay versiones más recientes que las de `normativa-local`; cité las locales y no las he cotejado con las nuevas. Y no he leído el convenio colectivo aplicable.
2. **NIST SP 800-63-4:** pude confirmar el principio (el bloqueo es el control de un PIN de activación), no las cifras de longitud ni de reintentos. Lo de «6 dígitos» sale de un blog.
3. **Web NFC:** el comportamiento con el sistema (apertura de la URL fuera de la página), el permiso del navegador y la lectura con Random ID, por resúmenes de la documentación de Chrome. Hay que probarlo con las tablets.
4. **NXP AN12196:** los derechos de acceso por defecto del fichero del mensaje, qué entregan los proveedores con Random ID ya activado, y los servicios de «claves a medida» de los proveedores.
5. **FIDO2 por NFC** en Chrome para Android con el PIN: no verificado.
6. **Key Vault:** si calcula el CMAC o si la aplicación debe tener la clave en memoria (lo supongo), región y replicación.
7. **Qué acciones tiene hoy exactamente un Auxiliar:** sé que no tiene permisos configurables, pero no he recorrido los controladores. La lista blanca es de CJ.
8. **Lista vigente del `CHECK` de `eventos_acceso`** y de motivos de cierre tras scripts posteriores al `0062` y al `0061`; **quién puede leer `eventos_acceso`**.
9. **ET art. 64.5.f:** si la pulsera cae en él (lo decide el laboralista).
10. **Titularidad del centro piloto** y, por tanto, AEPD o APDCAT, y si aplica el ENS.
11. No he releído `Privacy.cshtml` (solo comprobé que no menciona la pulsera) ni `0008`, `0006` ni `hallazgos.md`.
12. Los precios de pulseras NTAG 424 DNA y de llaves FIDO2 con NFC.

---

## 8. Borrador breve de la nota para `docs/privacidad/` (sin escribir)

Nombre sugerido: `pulsera-nfc-pin-lectura-dpo.md`.

---

**BORRADOR. Lo valida una persona (DPO real o abogado; laboralista para el punto 3) antes de usarlo con datos reales.** Fecha: 2026-11-20. Segunda lectura del agente `dpo-residapp` sobre I5 (ADR 0007), pieza 3 del ADR 0009 y decisión 12 del ADR 0016.

**Conclusión.** Admisible con condiciones. En desarrollo con datos de prueba, sí. Con datos reales, no hasta cerrar lo de abajo. La «opción B pura» (sin filtro de aparato) no se acepta con datos reales: la pulsera solo se ofrece en aparatos compartidos registrados (D0 a D2 del ADR 0016), aunque el centro no tenga restricción general.

**Diseño corregido.**
1. Solo perfil Auxiliar, ligada al ámbito del centro, no a la cuenta entera. Nunca Plataforma, Administración, Dirección Clínica ni Familiar. Enfermería y Medicina, fuera de la primera versión (CJ decide).
2. Sesión de alcance reducido con lista blanca comprobada en el servidor (tipo de sesión guardado en `sesiones`), inactividad corta, tope absoluto y botón «Soy otra persona».
3. PIN de 6 cifras elegido por la persona en «Mi cuenta» (contraseña + TOTP); Administración nunca lo ve. Comprobante con pimienta fuera de la base y sal por pulsera; contador de fallos por pulsera en SQL; 5 fallos, bloqueo hasta Administración; lectura y PIN en una sola petición.
4. Chip NTAG 424 DNA con Random ID (irreversible), número de serie solo cifrado, clave de lectura de metadatos compartida por centro y clave de firma derivada por pulsera. Claves fuera de la base (Key Vault en producción; valores de prueba en dev). Cambio de clave al recibir; el proveedor no conoce las claves finales y no recibe datos personales.
5. Eventos: solo entradas, fallos, bloqueos y uso de una pulsera revocada; **sin registro de lecturas ni del aparato**. Finalidad cerrada: nunca jornada, horarios, ubicación ni rendimiento.
6. Voluntaria; apagada por defecto por centro; no sustituye a contraseña+TOTP ni a la passkey.

**Bloqueantes antes de datos reales.** Plazo y vía de borrado de `eventos_acceso`; validación del DPO real o abogado (base jurídica, autoridad AEPD o APDCAT); laboralista (voluntariedad, representación, custodia, disciplina, exclusión del registro de jornada); texto al personal entregado antes de activar; registro de actividades, anexo de la EIPD y política de privacidad actualizados; contrato de suministro de pulseras; procedimiento del centro (pérdida, no préstamo, desbloqueo); pruebas con tablets reales; Key Vault en producción.

**Alternativa a decidir antes de comprar.** Llave FIDO2 con NFC y PIN propio: evita custodiar PIN y claves AES, resiste a webs falsas y reutiliza I4; cuesta más y no es una pulsera.

**A verificar.** ET y Llei 32/2010 con las versiones nuevas del BOE; cifras de NIST; comportamiento de Web NFC y del chip configurado con aparatos reales; FIDO2 por NFC en las tablets; titularidad del centro piloto y ENS; convenio colectivo.

---

Rutas relevantes (absolutas)
- `c:\repoCJ\residapp\docs\decisiones-arquitectura\0007-proveedor-identidad.md`
- `c:\repoCJ\residapp\docs\decisiones-arquitectura\0009-nfc-voz-acceso-fisico.md`
- `c:\repoCJ\residapp\docs\decisiones-arquitectura\0016-dispositivos-autorizados.md`
- `c:\repoCJ\residapp\docs\privacidad\dispositivos-autorizados-lectura-dpo.md`
- `c:\repoCJ\residapp\docs\privacidad\antes-de-datos-reales.md`
- `c:\repoCJ\residapp\docs\privacidad\politica-privacidad-portal.md`
- `c:\repoCJ\residapp\docs\seguridad\acceso-por-dispositivo.md`
- `c:\repoCJ\residapp\database\scripts\0061_sesiones.sql`
- `c:\repoCJ\residapp\database\scripts\0062_credenciales_y_eventos_acceso.sql`
- `c:\repoCJ\residapp\src\ResidApp.Web\Security\SessionSignIn.cs`
- `c:\repoCJ\residapp\src\ResidApp.Application\Sessions\SessionPolicy.cs`
- `c:\repoCJ\residapp\src\ResidApp.Web\Security\AccesoOptions.cs`
- `c:\repoCJ\residapp\src\ResidApp.Domain\Accounts\ProfilePermissions.cs`
- `c:\repoCJ\residapp\normativa-local\` (rgpd.txt, lopdgdd.txt, estatuto-trabajadores.txt, ens-rd-311-2022.txt, llei-21-2000.txt, llei-32-2010.txt, aepd-lista-eipd.txt)

Fuentes web (resúmenes, no texto literal salvo donde se indica)
- [NXP AN12196, NTAG 424 DNA features and hints (PDF)](https://www.nxp.jp/docs/en/application-note/AN12196.pdf): frases citadas leídas del texto extraído del PDF.
- [Chrome for Developers, Web NFC](https://developer.chrome.com/docs/capabilities/nfc): por resumen de búsqueda.
- [NIST SP 800-63-4, autenticadores y contraseñas](https://pages.nist.gov/800-63-4/sp800-63b/passwords/): solo principios; cifras no confirmadas.
