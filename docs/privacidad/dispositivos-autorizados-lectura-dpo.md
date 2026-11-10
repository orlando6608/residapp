# Dispositivos autorizados por residencia: lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado, y para el apartado 2 también un laboralista) antes de usarlo con datos reales.** Fecha: 2026-11-10 (actualizado el mismo día con las decisiones cerradas por Orlando: el aparato se identifica solo por su clave, sin cookie de aparato; caducidad por no uso en el servidor; petición de 7 días; varios centros; mínimo de dos aparatos de Administración; y la disponibilidad en urgencias, que la app no resuelve: decisión 11 del ADR 0016). Segunda lectura experta del agente `dpo-residapp`, no un dictamen. No modifica código, ADR ni otros documentos del proyecto.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL); **[Suposición]** mía; **[Opinión]** mi criterio, discutible; **[Decidido]** lo ha cerrado Orlando (el DPO real puede revisarlo); **[Decidir]** lo fija Orlando, CJ, el DPO real, la residencia o un abogado y sigue abierto; **[A verificar]** no lo he podido confirmar en fuente oficial. No incluyo datos reales, secretos ni claves.

**Palabras que uso, una vez y en llano.** *Aparato*: el móvil, la tablet o el ordenador desde el que alguien abre ResidApp (así lo llama el proyecto). *Cookie*: un pequeño fichero que el navegador guarda en el aparato (el diseño **ya no usa una cookie para reconocer aparatos**; la palabra aparece por las cookies que la aplicación ya tiene). *Resumen SHA-256* (o *hash*): lo que queda de un número después de pasarlo por una trituradora de un solo sentido; sirve para reconocerlo, pero no para recuperarlo. *Par de claves*: dos números matemáticamente unidos, uno **privado** (queda en el aparato y sirve para firmar) y otro **público** (se entrega al servidor y solo sirve para comprobar firmas; con él no se puede firmar). *Clave no extraíble*: el navegador permite usar la clave privada para firmar, pero no entrega su contenido ni a la página. *Reto*: un número que el servidor manda en cada entrada para que el aparato lo firme y demuestre que tiene la clave privada. *IndexedDB*: un pequeño almacén de datos que el navegador guarda en el aparato, distinto de las cookies. *BYOD* («trae tu propio dispositivo»): que el trabajador use su móvil o tablet privados para el trabajo. *Encargada*: la empresa que trata datos por cuenta de otra y solo según sus instrucciones (ResidApp, para las residencias). *EIPD*: la evaluación de impacto en protección de datos, un estudio de riesgos que el responsable hace antes de empezar un tratamiento delicado. *Registro de actividades* (RAT): la lista, que obliga a llevar el RGPD, de qué datos se tratan, para qué y hasta cuándo.

**Punto de partida.** El diseño del 2026-11-10 solo existe en los encargos que recibo y, desde que lo leí, en el borrador `docs/decisiones-arquitectura/0016-dispositivos-autorizados.md` (líneas 36-47: decisiones 8 a 12 y propuestas). Busqué en todo el repositorio «dispositivos autorizados», `dispositivos_autorizados` y «aparato autorizado»: **no hay nada** en `src/` ni en `database/` (búsqueda hecha antes de existir el ADR). La primera lectura («dispositivo ligado al centro»: admisible como medida del art. 32 RGPD si es mínima, informada y de finalidad limitada) **tampoco está en `docs/privacidad/`**; la tomo tal como me la resumes. Lo que digo del código y de las tablas se refiere a lo que existe hoy; lo del diseño nuevo es lectura sobre el papel.

---

## Conclusión

**Admisible con condiciones.** Pedir que una cuenta del personal solo abra sesión desde un aparato que la Administración conoce es una medida de seguridad razonable para un tratamiento de datos de salud (art. 32 RGPD). No añade datos de salud: añade datos **de los trabajadores** (qué aparatos usan y cuándo los usan por última vez). Eso es lo que hay que cuidar. Mis condiciones, de mayor a menor gravedad:

1. **Aparato personal = BYOD y riesgo de control laboral.** El vínculo «cuenta con aparato privado» es el único dato nuevo que dice algo de la vida del trabajador. Es admisible si es **voluntario, con alternativa real, de finalidad cerrada, sin ver el contenido del aparato, sin historial de uso por aparato y con información previa** (apartado 2). Sin esas garantías no lo activaría.
2. **Los registros nuevos caen en una tabla que hoy no se puede borrar y no tiene plazo.** `eventos_acceso` guarda IP, es inmutable y su plazo sigue sin decidir (`database/scripts/0062_credenciales_y_eventos_acceso.sql:32-55`; `docs/privacidad/expurgo-y-supresion.md:158`). Añadir eventos de aparatos sin resolver eso agranda un hueco que ya bloquea los datos reales (apartado 6).
3. **Activar, desactivar y «registrar un aparato si el centro pierde todos» son decisiones del centro que ejecuta Plataforma.** Sin instrucción documentada detrás, ResidApp decidiría ella los medios de seguridad de un responsable ajeno (art. 28.3.a, leído en el BOE el 2026-11-09; `docs/privacidad/modulos-opcionales-lectura-dpo.md:44`). Y es la capacidad más delicada del diseño: abrir la puerta de un centro a un aparato nuevo (apartado 4).
4. **Identificar al aparato solo por una clave mejora la seguridad y simplifica la privacidad (una cookie menos), pero añade un identificador estable y un almacenamiento en el aparato.** La clave pública, unida a una cuenta o a un centro, es dato personal; hay que dar una clave nueva por registro y por centro, borrarla al revocar y declarar el almacenamiento. Ese almacenamiento (IndexedDB) **sigue necesitando la excepción del art. 22.2 LSSI**, y no es seguro que la AEPD la acepte tal cual (apartado 5).
5. **Las peticiones pendientes durante 7 días (decidido) son defendibles como tope**; el borrado de la petición no borra el apunte de `eventos_acceso` (apartado 3).
6. **Cinco textos del proyecto dejarían de ser ciertos** al construirlo (política de privacidad de la aplicación, inventario de almacenamiento del navegador, manual, guía «Acceso y sesión» y `acceso-por-dispositivo.md`). Apartado 7.5.

---

## Estado actual verificado en el repositorio

- **[Hecho]** No existe código ni SQL de este diseño (búsqueda hecha en `src/` y `database/`). Sí existe el borrador de ADR 0016, que recoge las decisiones de Orlando (lo cito por líneas).
- **[Hecho]** `eventos_acceso` guarda `ocurrido_en`, `cuenta_id`, `tipo`, `sesion_id` e `ip` (`0062:32-38`), con lista cerrada de tipos en un `CHECK` (`0062:39-41`; la lista se amplió en scripts posteriores, **[A verificar]** la vigente), y dos disparadores `INSTEAD OF` que impiden modificar y borrar (`0062:49-55`). Nunca guarda lo tecleado (`0062:10-11`). Sin seguridad por filas, porque se lee antes de que haya ámbito (`0062:12`).
- **[Hecho]** La política de la aplicación ya informa de que se registra cada entrada con IP y cada sesión con IP (`src/ResidApp.Web/Views/Home/Privacy.cshtml:72-73`) y de que el registro «sirve para la seguridad y para detectar accesos indebidos» y que «ResidApp no incluye listas ni clasificaciones de personal por rendimiento» (`Privacy.cshtml:78`).
- **[Hecho]** La tabla de cookies lista siete cookies propias con duración de sesión del navegador o de minutos a una hora, más las de formularios y mensajes (`Privacy.cshtml:166-177`; `docs/privacidad/politica-privacidad-portal.md:205-217`, inventario B3 en las líneas 328-350). Como el diseño ya no usa cookie de aparato, **esa tabla no necesita fila nueva** y la afirmación de que todas son técnicas y no piden consentimiento (`Privacy.cshtml:164`) no cambia.
- **[Hecho]** **Hoy la aplicación no usa IndexedDB**, y los documentos lo dicen: «La aplicación no usa "almacenamiento de sesión" ni bases de datos locales del navegador» (`politica-privacidad-portal.md:226`) y el inventario B3 anota «`sessionStorage`, `indexedDB`, `document.cookie`: No hay ninguno» (`politica-privacidad-portal.md:349`). Las dos frases dejarían de ser ciertas. El resto del almacenamiento que no son cookies es `residapp-tema` (almacenamiento local) y la copia `residapp-shell-v9` (`Privacy.cshtml:179-184`).
- **[Hecho]** El ADR 0007 fija sesión de 30 minutos de inactividad y 12 horas absolutas (`0007-proveedor-identidad.md:69`); una persona ve y cierra sus propias sesiones en «Mi cuenta» (`0007:73`), un precedente útil para el apartado 2; la invitación de recuperación cierra sesiones y borra passkeys (`0007:72, 74`); Plataforma se recupera por SQL con `database/operaciones/invitar_cuenta.sql` (`0007:105-106`; `0006-perfil-plataforma.md:36-38`).
- **[Hecho]** Una *passkey* ya guarda su clave secreta en el aparato y «nunca llega a ResidApp» (`Privacy.cshtml:185`; `0007:78-80`): es un precedente de explicar a la persona una clave que se queda en su aparato.
- **[Hecho]** Reglas de una cuenta en varios centros: ninguna Administración la suspende ni le entrega invitaciones; lo hace Plataforma (decididas por Orlando el 2026-10-23, pendientes del DPO: `0007:72`).
- **[Hecho]** Plataforma vive en el centro reservado `PLATAFORMA`, sin unidades ni residentes, y «no ve residentes ni contenido clínico» (`0006:14-20`). Su salto de seguridad por filas se limita a unas tablas y operaciones, nunca UPDATE ni DELETE (`0008-multicentro-y-grupos.md:60-71`). Para mirar sin abrir una tabla se usa un procedimiento `EXECUTE AS OWNER` (`0008:69-71`, patrón `nombres_unidades_vinculacion`).
- **[Hecho]** `docs/seguridad/acceso-por-dispositivo.md` es un borrador de análisis que dice: «Acceso desde casa del personal: los documentos no dicen si está permitido ni restringido» (línea 50), y que la passkey «encaja mal» en aparatos compartidos (líneas 46-47).
- **[Hecho]** Precedente de purga con plazo fijo: el registro de envíos de correo se borra a los 12 meses con un procedimiento `EXECUTE AS OWNER` (`0085`; descrito en `expurgo-y-supresion.md:75`). Toda purga futura de `eventos_acceso`, `sesiones` y auditoría debe seguir ese patrón (`antes-de-datos-reales.md:160`).

---

## 1. Datos nuevos, finalidad y base jurídica

### 1.1 Quién es quién

| Situación | Responsable | Encargada | Personas afectadas |
|---|---|---|---|
| Aparatos de un centro (Administración, Enfermería, Medicina, Auxiliares…) | La residencia | ResidApp | Los trabajadores del centro (y quien administre el centro) |
| Aparatos del centro reservado `PLATAFORMA` | **ResidApp** (aquí no trata datos por cuenta de nadie: es su propio personal) | — | Las personas que operan ResidApp |
| Familiares | Fuera de esta medida [Decidido] | — | — |

**[Opinión]** El segundo caso importa: para Plataforma ResidApp **es responsable**, no encargada, y necesita su propia cláusula informativa, su línea en el registro de actividades (art. 30.1) y su plazo (apartado 4).

### 1.2 Qué se guarda y cuánto dice de la persona

| Dato | De quién | Para qué | Comentario y mínimo que propongo |
|---|---|---|---|
| **Clave pública del aparato** (el aparato se identifica por ella o por su huella) | Del aparato (y, si es personal, de su dueño) | Reconocer el aparato y comprobar la firma del reto en cada entrada | **Dato nuevo y único identificador del aparato.** Apartado 1.6. |
| Tipo (compartido o personal) | Del aparato | Decidir a quién sirve | Sin objeción. |
| Nombre del aparato | Quien lo registra / Administración | Que la Administración reconozca cuál es | **Es texto libre**: la gente acaba escribiendo «Móvil de Marta» o un teléfono. Ver 2.3 (garantía G9). |
| Etiqueta del aparato («Android, Chrome») | Del aparato | Ayudar a reconocerlo al aprobar | **[Opinión]** Guardar **solo la etiqueta ya reducida**, no la cadena completa que envía el navegador (`User-Agent`): esa cadena, con el resto, sirve para reconocer un aparato en concreto. |
| Centro | Del aparato | Ámbito de validez | Sin objeción. |
| Cuenta (solo si es personal) | Del trabajador | Atar el aparato privado a una persona | **El dato delicado.** Apartado 2. |
| Quién lo aprobó y cuándo (una cuenta de Administración) | De quien aprueba | Poder demostrar quién autorizó qué (art. 5.2) | Sin objeción; es un dato de actividad del administrador, con el mismo tratamiento que la auditoría administrativa. |
| Alta, revocación | Del aparato | Ciclo de vida | Sin objeción. |
| Último uso, **sin IP** | De quien lo usa | Caducar aparatos sin uso: compartido 1 año, personal 90 días; cada uso renueva [Decidido] | **[Opinión]** Guardar **solo el día**, no la hora (apartado 2.3, G5): el día basta para aplicar los plazos de 90 días y 1 año. Guardar la hora de cada uso de un móvil privado es un registro de horarios de trabajador. |
| Petición pendiente: cuenta, etiqueta, fecha, **clave pública** y código | Del trabajador | Que la Administración apruebe | Dato temporal (apartado 3). |
| Eventos nuevos en `eventos_acceso` | Del trabajador (cuenta, **IP**, hora) | Seguridad y prueba | Plazo sin decidir (apartado 6). |

**Lo que no se guarda y debe seguir sin guardarse** **[Opinión, a fijar como garantía]**: IP del aparato en la tabla de aparatos; modelo, número de serie o identificadores de hardware; ubicación; lista de aplicaciones o ficheros; versión exacta del sistema; **la clave privada (no sale del aparato y ResidApp no puede obtenerla)**; los retos y las firmas de cada entrada (se comprueban y se descartan; no van a `eventos_acceso` ni a la auditoría); el código de la petición en la auditoría. En el aparato se guarda una cosa: la clave.

### 1.3 Finalidad

**Una sola:** decidir si desde ese aparato se puede abrir sesión con esa cuenta, para reducir el riesgo de que alguien con credenciales robadas entre desde un aparato desconocido y vea datos de salud. Medida de seguridad del tratamiento de datos de residentes (art. 32, apartados 1 y 2, leídos hoy en el BOE en resumen: «medidas técnicas y organizativas apropiadas para garantizar un nivel de seguridad adecuado al riesgo»). Se debe declarar **cerrada**: no sirve para controlar horarios, productividad, desplazamientos ni conducta (garantía G2). Un uso distinto sería otra finalidad que habría que justificar aparte (art. 5.1.b y 6.4, **[A verificar]** literal).

### 1.4 Base jurídica

- **[Opinión]** Para el centro, el **interés legítimo** del responsable (art. 6.1.f: «necesario para la satisfacción de intereses legítimos perseguidos por el responsable», leído hoy en el BOE) en proteger los datos de salud que custodia, con una **ponderación escrita** (apartado 7.3). Alternativa que el DPO puede preferir: obligación legal en cuanto el art. 32 RGPD (y el ENS, si el centro es público o concertado: RD 311/2022) impone medidas de seguridad (art. 6.1.c). **[Decidir]** DPO.
- **No consentimiento.** Un trabajador no puede consentir libremente ante su empleador; si el diseño lo pidiera, no sería válido y daría una falsa sensación de legalidad. **[Opinión]** El carácter voluntario del BYOD (G1) no es consentimiento en sentido jurídico: es una garantía de proporcionalidad.
- **Artículo 9.** Esta medida **no trata datos de salud**: no hace falta condición del art. 9.2 para sus datos. Los datos de salud a los que protege siguen con la base que ya tengan. (No he releído el art. 9 hoy.)
- **Para ResidApp en Plataforma:** también interés legítimo (seguridad de la plataforma que opera), con ponderación propia; y la relación con su personal según su figura laboral o mercantil **[Decidir]**.

### 1.5 Quién decide activar la restricción

Es del **centro**, no de ResidApp (apartado 4.3). Lo que ejecuta Plataforma debe quedar respaldado por una referencia documental (no un nombre) y anotado en la auditoría del centro, igual que propuse para los módulos (`modulos-opcionales-lectura-dpo.md:51-61`).

### 1.6 La clave del aparato como único identificador [Decidido por Orlando, 2026-11-10]

**Qué es.** El aparato crea en el navegador un par de claves. La privada es no extraíble y queda en IndexedDB del aparato; el servidor guarda la pública y, en cada entrada, el aparato firma un reto. **No hay cookie de aparato**: el aparato se identifica por su clave pública (la envía, o envía su huella, al intentar entrar). Resultado: el «carné» del aparato no se puede copiar a otro navegador, cosa que sí ocurriría con una cookie (quien la copia se hace pasar por el aparato). **[Opinión]** Es una mejora de seguridad clara, un buen ejemplo de «protección desde el diseño» (art. 25.1, leído el 2026-11-09), y **una decisión acertada también para la privacidad**: un identificador y un almacenamiento en vez de dos, una cookie persistente menos que justificar. Limitaciones que Orlando ya señala y que comparto: si se borran los datos del sitio, el aparato deja de ser conocido y hay que registrarlo de nuevo; y un script malicioso dentro de la página abierta podría pedir firmas mientras está abierta (la clave no se copia, pero se puede usar). Es una capa más, no una garantía (G13). Sin JavaScript no hay clave (la PWA ya lo necesita para otras cosas).

**¿Es dato personal?** **[Opinión, a confirmar por el DPO]** Sí, **en este contexto**. Una clave pública, por sí sola, es un número que no dice nada. Pero es **única por navegador**, estable y se guarda junto a una cuenta (aparato personal) o a un centro (aparato compartido): sirve para singularizar y reconocer a esa persona o aparato, que es lo que hace un «identificador en línea». El RGPD, art. 4.1, y el considerando 30 lo prevén **[A verificar]**: no los he releído hoy; el considerando 26 (lo identificable con medios razonables sigue siendo personal) lo leí el 2026-11-09. Trátese como dato personal a todos los efectos (registro, plazo, información, derechos).

**¿Cambia algo para el vínculo cuenta-aparato personal?** Sí, en cuatro cosas, sin cambiar la conclusión:
1. **El vínculo es más sólido y más estable.** Mientras la clave exista identifica sin duda a ese navegador. Eso afianza el valor probatorio del «último uso» y por eso la garantía G5 (solo el día, sin historial) pasa a ser más importante, no menos.
2. **Una clave nueva por registro y por centro.** Nunca reutilizar la misma clave para dos centros ni para un segundo registro del mismo aparato: una clave común sería un identificador que cruza centros (a ojos de quien administre la base) y entre registros (a ojos del tiempo). Como la clave es el único identificador, esta regla es aún más necesaria.
3. **Solo para el servidor.** La clave pública no se muestra a la Administración (no le sirve para reconocer el aparato; para eso está el nombre y la etiqueta), no va a `eventos_acceso` ni a la auditoría, ni a registros técnicos, ni a exportaciones. Los eventos se refieren al aparato por su identificador interno.
4. **Menos copiable, no menos delicada.** Que la clave privada no salga del aparato reduce el riesgo para el trabajador (nadie puede suplantar su aparato desde otro) y es un argumento de ponderación a favor (apartado 7.3). Lo que cambia para el control laboral es **nada**: ResidApp sigue sin ver nada del aparato; solo recibe una clave pública y firmas.

---

## 2. El vínculo cuenta-aparato personal y el control laboral

### 2.1 Por qué es el punto delicado

Un aparato **compartido** dice «la tablet de la planta 2 es del centro». Uno **personal** dice «esta cuenta usa este móvil privado». De ahí se deducen cosas sobre la vida del trabajador: que tiene un móvil de cierta marca, que lo usa fuera de la residencia, cuándo lo usó por última vez. En manos de un empleador, un listado así puede acabar sirviendo para preguntar «por qué entraste el domingo» o presionar para que el trabajador ponga su teléfono a disposición del trabajo.

### 2.2 Marco legal (lectura de hoy)

- **[Hecho, resumen de la herramienta, no literal; BOE consolidado, última actualización 27/12/2025]** LOPDGDD, art. 87: derecho a la intimidad en el uso de dispositivos digitales **que le proporciona el empleador**; el empleador solo accede a su contenido para controlar el cumplimiento de obligaciones laborales o garantizar la integridad del aparato, con criterios de uso fijados con participación de los representantes de los trabajadores. Art. 88: desconexión digital. Art. 89: videovigilancia y sonidos. Art. 90: geolocalización. Art. 91: garantías adicionales por convenio colectivo.
- **[Opinión]** El art. 87 está escrito para aparatos **del empleador**. En un móvil privado no es aplicable al pie de la letra, pero **su razón** (intimidad, proporcionalidad, participación de los representantes) pesa más, no menos, cuando el aparato es del trabajador. El art. 90 (geolocalización) no se aplica porque ResidApp no geolocaliza; conviene que siga así (la IP de `eventos_acceso` da una idea aproximada de dónde estaba la conexión: no usarla para eso, G2).
- **[A verificar]** Estatuto de los Trabajadores, art. 20 bis (intimidad en el entorno digital y desconexión): solo lo he visto en resultados de búsqueda de fuentes secundarias, no en el BOE. Y el convenio colectivo aplicable (no lo he visto), el deber del empleador de proporcionar los medios de trabajo y los límites a usar el móvil propio sin compensación: **lo resuelve un laboralista**.
- **[A verificar]** RGPD, art. 88 (tratamiento en el ámbito laboral): no pude leerlo en el BOE hoy.

### 2.3 Garantías concretas

**[Opinión]**, para el diseño y para el contrato. G1 a G6 son las que yo exigiría antes de activar; G7 a G13 las completan.

| N.º | Garantía | Cómo se concreta |
|---|---|---|
| **G1** | **Voluntario y con alternativa real** | Nadie está obligado a registrar un móvil propio. El centro debe poder ofrecer aparatos compartidos suficientes (si no, «voluntario» es ficticio). La Administración no registra como personal un aparato que el trabajador no haya pedido registrar. No registrar un aparato propio no tiene consecuencia laboral. |
| **G2** | **Finalidad cerrada** | Escrito en el contrato y en la política: la lista de aparatos solo sirve para decidir si se abre sesión. No se usa para horarios, productividad, conducta, ubicación ni evaluación. Excepción: investigar un incidente de seguridad concreto, con anotación y por quien el centro designe. No hay informes ni gráficos de uso por trabajador (coherente con el alcance: «rankings nominativos de productividad» fuera, `modulos-opcionales-lectura-dpo.md:148`). |
| **G3** | **Lo que ve la Administración, y nada más** | Cuenta, nombre del aparato, tipo, etiqueta, fecha de alta y **día** del último uso. No ve IP, no ve un historial de usos de ese aparato, no ve la cadena `User-Agent`, ni modelo ni ubicación, ni la clave pública. |
| **G4** | **ResidApp no ve ni gestiona el aparato** | En el aparato solo hay una clave que firma retos. No hay aplicación que instalar, ni gestión del móvil (MDM), ni acceso a ficheros, aplicaciones, fotos o contactos. La clave privada no sale del aparato. Debe decirse así a los trabajadores. |
| **G5** | **Sin rastro de horarios en los aparatos personales** | «Último uso» = día. Los eventos de ciclo de vida (petición, aprobación, rechazo, revocación, caducidad) sí llevan el aparato. **[Decidir]** Que las **entradas normales** (`ENTRADA`) **no** lleven el identificador del aparato personal: hoy ya consta cuenta, hora e IP de cada entrada (`Privacy.cshtml:72`), pero añadir «y desde qué móvil privado» convertiría la tabla en un historial de uso de móviles privados. Contrapeso de seguridad: sin el identificador, investigar un incidente es menos fino. Lo decide el DPO con la Administración. El ADR 0016 ya lo recoge como propuesta (línea 46) y guarda el vínculo sesión-aparato en una tabla aparte que se purga al cerrar la sesión (línea 38), precisamente para no crear ese historial. |
| **G6** | **La persona ve y quita sus aparatos personales** | En «Mi cuenta», junto a sus sesiones (`0007:73`): lista de sus aparatos personales y botón «Quitar». No puede aprobar (solo la Administración), pero sí retirar. Al quitar desde ese mismo aparato, la aplicación borra también su clave local (apartado 6). |
| **G7** | **Quién ve la lista** | La lista de aparatos es **solo de la Administración** del centro, con su permiso configurable. Plataforma no lee nombres ni cuentas de los aparatos de otros centros (apartado 4.3). |
| **G8** | **Al salir el trabajador** | Si la cuenta se suspende o pierde el ámbito en el centro, sus aparatos personales pasan a revocados (el diseño ya dice que «dejan de servir»; conviene que además se **registre** la revocación y su motivo de lista cerrada). Si la cuenta se recupera por sospecha de robo (invitación de recuperación, `0007:72`), **revocar también sus aparatos personales**: quien robó credenciales pudo registrar uno. |
| **G9** | **Nombre del aparato** | Texto corto con tope de longitud, ayuda visible («nombre del aparato, no de la persona: por ejemplo "Tablet planta 2" o "Móvil personal"»), propuesta automática y posibilidad de que la Administración lo cambie. Al revocar, **vaciar el nombre** (apartado 6). |
| **G10** | **Desconexión digital** | La petición de aprobación no obliga a nadie a responder fuera de su horario; no se envían avisos al trabajador ni a la Administración fuera de la aplicación (sin correo ni SMS). Sin penalización por no registrar. |
| **G11** | **Información previa y política del centro** | Antes de activar, el centro informa a los trabajadores (texto A) y fija una política de uso de móvil propio, consultada con los representantes de los trabajadores si procede **[A verificar]** por el laboralista (por analogía con el art. 87.3). |
| **G12** | **Higiene** | Los aparatos sin uso caducan **en el servidor**: compartido tras 1 año, personal tras 90 días, y cada uso renueva [Decidido]. La Administración revisa la lista periódicamente, por escrito, cada [periodo]. |
| **G13** | **Lo que no es esta medida** | No sustituye a la contraseña, al segundo paso ni a la passkey; es una capa más. Decirlo al centro: reduce un riesgo, no lo elimina. Con la clave, no hay nada que copiar a otro navegador para hacerse pasar por el aparato; pero quien controle el aparato registrado (o un script dentro de la página abierta en él) sigue pudiendo usarlo. |

### 2.4 Texto A: información a los trabajadores (plantilla que el centro adopta como suya)

Borrador en español llano, **sin cookie, solo la clave del aparato**. Catalán si se pide. Los corchetes los rellena el centro.

> **Aparatos autorizados para entrar en ResidApp**
>
> **[RESIDENCIA]** ha decidido que, para proteger los datos de salud de los residentes, solo se pueda entrar en ResidApp desde aparatos que la Administración del centro conozca y haya autorizado. Quien decide esta medida y responde de ella es **[RESIDENCIA]**; ResidApp, que ofrece la aplicación, trabaja siguiendo sus instrucciones.
>
> **Qué se guarda.** Si usas un aparato nuevo, la primera vez no se abrirá sesión: se guardará una petición con tu cuenta, un nombre corto del aparato (por ejemplo, "Android, Chrome") y la fecha, y la Administración la revisará. Cuando se autorice, se guardará: el nombre que le pongas al aparato, si es del centro (lo puede usar cualquier compañero) o tuyo (solo lo usas tú), quién lo autorizó y cuándo, la fecha de alta, **el día** del último uso y, si lo quitan, la fecha.
>
> **Qué se pone en tu aparato.** El navegador guarda una sola cosa técnica, solo para reconocer el aparato y por seguridad: una *clave de seguridad*, parecida a las llaves de acceso (passkeys). Esa clave tiene dos mitades: la **privada** se queda en tu aparato, no se puede copiar ni sacar y **ResidApp nunca la ve**; la **pública** se guarda en ResidApp y solo sirve para comprobar que el aparato sigue siendo el mismo cuando entras. No lleva tus datos, y es distinta en cada centro y en cada registro. No usamos ninguna cookie para esto.
>
> **Qué no se guarda ni se ve.** ResidApp no puede ver lo que hay en tu móvil (ni fotos, ni mensajes, ni aplicaciones), no sabe dónde estás y no guarda el modelo ni el número de serie. La lista de aparatos **no se usa** para controlar tus horarios, tu trabajo ni tu rendimiento. Las entradas en ResidApp se anotan, como siempre, con tu cuenta, la hora y la dirección IP desde la que entras, para la seguridad.
>
> **Es voluntario.** Nadie te obliga a registrar tu móvil. Si prefieres no hacerlo, usa los aparatos del centro. No tendrá consecuencia alguna.
>
> **Si borras los datos del navegador.** Si borras "los datos del sitio" o usas el modo privado o cambias de navegador, el aparato vuelve a ser desconocido y tendrás que pedir la autorización otra vez. Cada navegador de un mismo móvil cuenta como un aparato distinto.
>
> **Para qué.** Para evitar que alguien con tu contraseña entre desde un aparato desconocido. La base jurídica es el interés del centro en proteger los datos de salud que custodia **[el DPO confirma]**.
>
> **Cuánto tiempo.** Las peticiones sin atender se borran a los **7 días**. Un aparato del centro deja de valer tras **1 año** sin usarse y uno personal tras **90 días**; cada vez que lo usas, el plazo vuelve a empezar. Cuando se quita un aparato, se borran su nombre y la clave pública que ResidApp guardaba, y se conserva el resto (quién lo autorizó y cuándo) **[12 meses]**; los registros de entradas y de seguridad se conservan **[plazo]**. La clave privada la guarda tu navegador: ResidApp no puede borrarla desde fuera; al quitar el aparato desde "Mi cuenta" en ese mismo aparato, o al volver a abrir ResidApp en él, la aplicación la borra; si pierdes o vendes el móvil, borra los datos del sitio desde los ajustes del navegador.
>
> **Tus derechos.** Puedes ver y quitar tus aparatos personales en "Mi cuenta", y pedir al centro acceso, rectificación o supresión de tus datos en **[contacto del DPO o de la residencia]**. Si no estás de acuerdo, puedes reclamar ante **[AEPD / APDCAT]**. Si dejas de trabajar en el centro, tus aparatos personales se retiran.

**Texto B: pantalla al pedir autorización** (sale después de poner bien las credenciales en un aparato desconocido):

> No podemos abrir tu sesión desde este aparato porque la Administración todavía no lo conoce. Hemos enviado una petición con tu cuenta, el tipo de aparato ("Android, Chrome") y la fecha, y este navegador ha guardado una clave de seguridad que no sale de él. **Tu código es [código].** Díselo a la Administración de tu centro, en persona o por un medio que ya uséis, para que lo autorice. La petición se borra sola a los 7 días. Si no has sido tú, avisa al centro. [Más información]

**Texto C: ayuda en el formulario de aprobación:** «Escribe el nombre **del aparato, no de la persona** (por ejemplo, "Tablet planta 2"). No pongas nombres, teléfonos ni datos personales. Comprueba antes que el código que te ha dicho la persona coincide con el de la petición y que la persona es quien dice ser.»

---

## 3. Las peticiones pendientes como dato temporal

**Qué son.** Una fila con cuenta, etiqueta, fecha, clave pública y el código. Existe para que la Administración vea quién ha intentado entrar desde un aparato nuevo.

**Finalidad y base.** Misma que el resto (apartado 1). La persona **ya sabe** que existe: se lo dice la pantalla (texto B), que es además la primera capa de información en el momento justo (art. 13).

**Plazo.** **[Decidido]** 7 días (decisión de Orlando). **[Opinión]** Es razonable como **tope**: cubre turnos de fin de semana, vacaciones y la ausencia de un administrador, y la fila es mínima (sin IP, sin datos de salud). **Alternativa, no pendiente:** 72 horas, que alinearía el plazo con el de las invitaciones (`0007:71`) y reduciría el tiempo en que un atacante puede intentar engañar a la Administración con una petición viva; solo tendría sentido si ningún turno deja a la Administración sin atender más de tres días.

Condiciones que propongo:
1. **El plazo lo aplica la lectura, no solo el borrado.** Una petición caducada **no se muestra ni se aprueba** aunque la tarea de limpieza no haya corrido (fallar cerrado). Para borrar hace falta una tarea programada con procedimiento `EXECUTE AS OWNER`, como `0085`; la tarea y su usuario (`ResidAppTareas`) aún están abiertos en el plan de despliegue.
2. **Al resolverse, se borra la petición.** Aprobada: pasa a ser un aparato. Rechazada: se borra, con su evento.
3. **Una petición viva por cuenta y aparato, y un tope bajo por cuenta**, para que alguien con credenciales robadas no inunde la bandeja de la Administración.
4. **Código de un solo uso, que caduca con la petición**, guardado como resumen y no en claro **[Suposición]**.
5. **Comprobación de identidad fuera de la pantalla.** La etiqueta «Android, Chrome» no identifica a nadie: el código solo vale si la Administración conoce a la persona. Debe estar escrito en el procedimiento de aprobación (texto C). Riesgo residual: un atacante con contraseña y código del móvil puede crear la petición y llamar haciéndose pasar por el trabajador. Es ingeniería social, y la medida la dificulta, no la elimina.
6. **Lo que NO desaparece a los 7 días.** El evento «petición creada» en `eventos_acceso` (cuenta, hora, IP) **no se puede borrar** hoy (`0062:49-55`). La información al trabajador no debe prometer que «se borra todo». Y la clave privada del aparato rechazado o caducado sigue en su navegador hasta que la aplicación la borre en la siguiente visita (apartado 6).
7. **La pantalla confirma que las credenciales eran correctas.** Es lo que obliga a informar a la persona, y es una pista para un atacante (le confirma una contraseña y un código válidos). **[Opinión]** Riesgo aceptable, porque ya hacía falta contraseña y segundo paso; solo conviene saberlo.
8. **Varios centros [Decidido]:** el aparato se comprueba **al elegir el centro activo** y la petición va a la Administración de ese centro (apartado 7.1, punto 2).

---

## 4. Plataforma dentro

### 4.1 Lo que se entiende bien

Dejar a Plataforma dentro es **más seguro** que dejarla fuera: es la cuenta más sensible del sistema (`0006:44-45`). Que sus aparatos los apruebe otra cuenta de Plataforma («nadie aprueba el suyo») es un buen reparto, y la primera y la recuperación por SQL encajan con cómo ya nacen sus cuentas (`0006:30-38`).

### 4.2 ResidApp responsable de los datos de su personal

Aquí no hay residencia: los aparatos son de quienes operan ResidApp. Hace falta una **cláusula informativa propia** (el texto A adaptado: «ResidApp es la responsable»), una línea en el registro de actividades como **responsable** (art. 30.1) y un plazo. Los datos son los mismos que en el apartado 1; no hay datos de salud (el centro reservado no tiene residentes: `0006:19-20`). Riesgo para las personas: bajo, siempre que sea una sola o pocas personas conocidas. **[A verificar]** qué relación jurídica tienen con ResidApp quienes operen (hoy no existe la sociedad titular: `anexo-modulos-contrato-borrador.md:30`) para saber quién firma la cláusula.

### 4.3 Dos puntos que me preocupan

**a) Un aparato de Plataforma puede ser la puerta de varios centros.** Una cuenta de Plataforma vale para provisionar centros y crear Administradores (`0006:44-45`). El control por aparato es especialmente valioso aquí (y la clave que no se puede copiar lo es más). A cambio, **la recuperación por SQL** es una puerta que se abre con el acceso a la base: conviene que el script (a) **escriba un evento** de tipo propio (no una fila manual silenciosa), (b) lo ejecute con la misma disciplina que `invitar_cuenta.sql` (sin pegar claves en conversaciones), (c) deje un apunte **fuera de la base** (fecha, cuenta, quién) como los demás procedimientos manuales (`expurgo-y-supresion.md:372`). Y es un límite que hay que declarar: quien administra la base puede saltarse cualquier medida (`0008:48-51`).

**b) Plataforma registrando un aparato o desactivando la restricción en un centro ajeno.** Es lo más sensible del diseño. **[Opinión]**
- **Solo con instrucción documentada del centro** y comprobando por un canal ya conocido que quien la pide es la Administración principal. Una petición urgente por teléfono es justo lo que usaría quien quiera colarse. Sin instrucción, ResidApp estaría decidiendo ella (art. 28.3.a y 28.10, `modulos-opcionales-lectura-dpo.md:15`).
- **Mejor generar un enlace o QR de un solo uso (forma A) y entregárselo a la Administración del centro** que registrar ella un aparato. Plataforma no «toca» el aparato del centro: lo hace la Administración, abriendo el enlace en su aparato (y es en ese aparato donde se crea la clave). El ADR 0016 lo recoge (líneas 37 y 47).
- **Desactivar la restricción** debilita la seguridad de un centro entero. Propongo: queda en la **auditoría del centro** con actor «Plataforma de ResidApp» (sin nombre), **aviso a la Administración principal** en su siguiente entrada, y reactivación fácil en cuanto el centro tenga aparatos. **[Decidir]** si caduca sola a los [N] días.
- **Salto de seguridad por filas.** Para insertar en la tabla de aparatos de otro centro Plataforma necesita un salto nuevo, que **modifica** la lista de `0008:60-71` y de `antes-de-datos-reales.md`, apartado 9. Que sea solo **INSERT** (nunca UPDATE ni DELETE, como todos), y que **no lea** nombres ni cuentas de los aparatos: para saber «este centro se ha quedado con menos de dos aparatos de Administración» basta un recuento (procedimiento `EXECUTE AS OWNER`, patrón de `0008:69-71`). Mismo criterio que apliqué a los módulos: no ampliar lo que ve Plataforma de los centros.
- La acción debe constar con el mismo tratamiento que `CENTER_CREATE` (`0006:25-26`): acción nueva de lista cerrada, sin texto libre.

**c) Evitar que el centro se quede sin acceso [Decidido].** Para activar la restricción hacen falta **dos aparatos de Administración aprobados**, y con la restricción activa **no se puede revocar el último**. **[Decidido, ADR 0016, decisión 9]** Un aparato de Administración tampoco caduca por no uso mientras haga falta para el mínimo de dos. **[Opinión]** Buena decisión. Queda un hueco que conviene escribir: **borrar los datos del sitio** en un aparato de Administración lo deja sin él aunque nadie lo haya revocado, y si pasa en los dos, la recuperación acaba en Plataforma (apartado 4.3 b).

---

## 5. El almacenamiento de la clave en el aparato (LSSI, art. 22.2)

Con la decisión de no usar cookie de aparato, **desaparece la valoración de una cookie persistente de un año** (y con ella la cuestión de que las cookies de autenticación exceptuadas lo son «únicamente de sesión», más abajo). **Queda el almacenamiento de la clave en IndexedDB, que sigue necesitando la excepción del art. 22.2.**

### 5.1 Qué dice la norma y qué he podido leer

- **[Hecho, BOE consolidado de la Ley 34/2002, última actualización 23/01/2025 según la herramienta; fragmentos entrecomillados]** El art. 22.2 permite «utilizar dispositivos de almacenamiento y recuperación de datos» en «equipos terminales de los destinatarios» «a condición de que los mismos hayan dado su consentimiento» tras información clara y completa. Y no impide el acceso técnico «al solo fin de efectuar la transmisión de una comunicación» o, «en la medida que resulte estrictamente necesario, para la prestación de un servicio de la sociedad de la información expresamente solicitado por el destinatario».
- **[Hecho, fuente secundaria]** La guía de la AEPD (versiones de 2013, 2019-2020 y 2023) trae, según resúmenes de terceros, una lista de cookies exceptuadas que incluye «autenticación o identificación de usuario (**únicamente de sesión**)» y «**seguridad del usuario**», apoyada en el dictamen 04/2012 del Grupo del artículo 29. **No pude abrir la guía de 2023** (el PDF se descarga en binario y no hay herramienta para leerlo) ni el dictamen. **[A verificar]** qué dice sobre duración y sobre tecnologías distintas de la cookie.

### 5.2 ¿Queda dentro del art. 22.2 y qué se excepciona?

1. **Sí queda dentro.** El texto verificado habla de «dispositivos de almacenamiento y recuperación de datos» en el equipo del usuario, sin nombrar la cookie: lo cubre igual. Las Directrices 2/2023 del CEPD sobre el alcance técnico del art. 5.3 de la Directiva de privacidad electrónica (versión 2.0, adoptada el 7 de octubre de 2024 según resultados de búsqueda; no las he abierto) extienden el mismo régimen al almacenamiento local, por lo que me han dado a entender las fuentes secundarias; **no he podido confirmar sus ejemplos sobre IndexedDB** y, según esas mismas fuentes, **no analizan las excepciones**. **[A verificar]**.
2. **Y también el acto de recuperarla:** cada entrada lee la clave (para firmar) y envía su identificador; es «recuperación de datos» del equipo terminal.
3. **Excepción: «estrictamente necesario … servicio expresamente solicitado».** **[Opinión]** El argumento es defendible y **más fuerte que el de una cookie**: la clave privada no sale del aparato, no se envía a nadie, no lleva datos y no sirve para seguir a la persona entre sitios; solo prueba que el aparato es el mismo. Se entra en ResidApp (servicio pedido) y el centro, por su deber de seguridad, lo protege con esta capa. La excepción protege el fin de seguridad, no la comodidad: si algún día se usara para otra cosa (analítica, recordar preferencias, saber qué aparatos tiene alguien), dejaría de valer. Es un argumento razonable, **no seguro**.

### 5.3 Qué cambia al no haber cookie y qué sigue igual

1. **Sin caducidad propia.** Una cookie tiene fecha de caducidad; IndexedDB **no la tiene**: dura hasta que se borran los datos del sitio. La validez del aparato **vive en el servidor** (caducidad por no uso: compartido 1 año, personal 90 días, cada uso renueva [Decidido]), y la aplicación **borra su clave local** cuando sabe que el aparato está revocado, caducado o con la petición rechazada (apartado 6). En la información hay que decir que el almacén persiste hasta que se revoque o se borren los datos del sitio. Como tampoco es «únicamente de sesión», **comparte con la antigua cookie el punto débil** de la lista de 2013: la duda de duración se desplaza de «¿cabe una cookie de un año?» a «¿cabe un almacén sin caducidad, con la caducidad aplicada por el servidor?». **[A verificar]** en la guía de 2023.
2. **Es legible desde la página, no está protegida como `HttpOnly`.** La clave no se puede extraer, pero cualquier script de la página puede **pedir firmas** mientras está abierta (limitación señalada por Orlando). Mitigación documentable: ningún script de terceros en la aplicación (hoy ninguno en la parte autenticada: `Privacy.cshtml:160`; las letras de Google de dos páginas estáticas son un caso aparte ya anotado), una política de seguridad de contenido (CSP) estricta, y firmar un reto ligado a esa entrada. **[A verificar]** con quien construya.
3. **Un solo uso:** decidir si este aparato es conocido. Prohibido cualquier otro. Una clave por registro y centro, sin datos de la persona (apartado 1.6).
4. **Información y tablas.** Hay que añadirla a la sección «Almacenamiento en el navegador que no son cookies» (`Privacy.cshtml:179-184`; `politica-privacidad-portal.md:219-226`) con nombre del almacén, para qué, cuándo se crea y cuánto dura (hasta que se revoque o se borren los datos del sitio), y **corregir** las dos frases que hoy dicen que no hay IndexedDB (`politica-privacidad-portal.md:226, 349`). La tabla de cookies no cambia.
5. **No hay banner de consentimiento** porque se invoca la excepción. Si el DPO o la AEPD no la aceptan, la salida no es un banner (pediría consentimiento a un trabajador por una medida del empleador) sino **rediseñar**.
6. **Aviso a los trabajadores** de que si borran los datos del sitio o navegan en modo privado, el aparato vuelve a ser desconocido y tendrán que pedir autorización otra vez (texto A).
7. **Dependencias técnicas.** Sin JavaScript no hay clave. Algunos navegadores limitan o borran el almacenamiento de sitios no usados durante un tiempo (en particular Safari en iPhone y iPad, por mi conocimiento general; **no lo he comprobado**) y las aplicaciones instaladas en la pantalla de inicio suelen quedar fuera de esa regla. Efecto: más re-registros (más peticiones, más eventos con IP), sobre todo con el plazo de 90 días de los aparatos personales. Merece una prueba con aparatos reales antes de aceptar la medida con iPhone.
8. Anotarlo en la EIPD con la justificación de la exención (apartado 7.2).

---

## 6. Plazos de conservación sugeridos

**Aviso.** No hay en la norma que he leído un plazo para esto. Lo que sigue son **propuestas mías, razonadas**, salvo lo marcado [Decidido]. Parto del precedente interno de 12 meses para los registros de envío de correo (`expurgo-y-supresion.md:75`), que tampoco viene de una norma.

| Dato | Plazo | Razón | Estado |
|---|---|---|---|
| Petición pendiente (con su clave pública y código) | **7 días** como máximo; se borra al resolverse | Dato de trámite | [Decidido] por Orlando. Alternativa de 72 h (apartado 3) |
| Aparato activo (fila y clave pública) | Caduca **sin uso**: compartido **1 año**, personal **90 días**; cada uso renueva; aplicado en el servidor | Aparato olvidado = superficie de ataque; minimización (art. 5.1.c y e, leídos hoy) | [Decidido] por Orlando |
| Aparato revocado o caducado | **Al revocar o caducar: vaciar el nombre y borrar la clave pública. Borrar la fila a los 12 meses** | El nombre es lo que más fácil lleva datos personales; la clave pública es lo que permite singularizar el aparato y ya no sirve; el resto (tipo, centro, cuenta, quién aprobó, fechas) sirve para probar quién autorizó qué y para investigar incidentes (art. 5.2) | Propuesta; **[Decidir]**. El ADR 0016 (línea 44) ya recoge vaciar nombre y clave y borrar la fila a los 12 meses como propuesta. Contrapartida: sin la clave, un aparato revocado que vuelve a intentarlo se ve como desconocido y no como «revocado»; si se quisiera esa alarma, habría que guardar su huella, con su plazo |
| Vínculo sesión-aparato (ADR 0016, decisión 10) | Se purga al cerrarse o caducar la sesión | Para cerrar sesiones al revocar sin dejar historial de qué móvil usó cada persona (G5) | [Decidido] |
| **Clave privada en el navegador del trabajador** | **ResidApp no puede borrarla desde fuera.** La aplicación la borra (a) al quitar el aparato desde «Mi cuenta» en ese mismo aparato, (b) en la siguiente visita desde un navegador cuyo aparato conste revocado, caducado o con la petición rechazada, y (c) el trabajador, borrando los datos del sitio | Que no queden claves vivas sin dueño en aparatos privados | Propuesta; hay que decirlo en la información (texto A) |
| Retos y firmas de cada entrada | Se comprueban y se descartan; el reto caduca en minutos y es de un solo uso | No son datos que conservar | Propuesta |
| Eventos de aparatos en `eventos_acceso` | **El plazo que el DPO fije para toda la tabla**; punto de partida de discusión: 12 meses y, antes, poner la IP a NULL (`expurgo-y-supresion.md:158`) | Mismo almacén, mismo problema: sin vía de borrado hoy | **Bloquea datos reales** (`antes-de-datos-reales.md:131`) |
| Acciones de la Administración (aprobar, revocar, desactivar) en la auditoría | El plazo de la auditoría administrativa | Prueba de quién decidió | Sin decidir |
| Copias de seguridad de Azure SQL | Por defecto 7 días de restauración, configurable | Contienen lo ya borrado (incluidas claves públicas) hasta que caducan | **[A verificar]** el valor real (`expurgo-y-supresion.md:78`) |
| Aparatos de Plataforma | Los mismos plazos | ResidApp responsable | — |

**Derechos del trabajador.** El acceso se atiende con su lista de aparatos personales (nombre, tipo, etiqueta, fechas); la clave pública no hace falta enseñarla, pero existe y se cuenta entre sus datos. La supresión, con el vaciado inmediato del nombre y la clave pública y el borrado de la fila **salvo** lo que se conserve por seguridad y prueba (los eventos). La clave privada **no está en poder de ResidApp**: no es dato que ResidApp pueda dar ni borrar; el trabajador la controla. Tiene que decirse en la información y apuntarse en el registro de derechos del centro.

**Dependencia técnica.** Cualquier borrado de `eventos_acceso` exige tocar sus disparadores y hacerlo con un procedimiento aprobado (`antes-de-datos-reales.md:160`). **Hasta que exista esa vía no debería activarse la medida con datos reales**, porque cada aparato nuevo añade filas con cuenta e IP que no se podrán borrar. Además, la tabla de aparatos necesita columnas que admitan NULL en el nombre y la clave pública para poder vaciarlos al revocar o caducar, y una purga a los 12 meses con procedimiento `EXECUTE AS OWNER` (patrón `0085`); y lo mismo para aplicar la caducidad por no uso.

---

## 7. Qué hay que añadir a los documentos de cumplimiento

### 7.1 Otros hallazgos del diseño (por gravedad)

1. **Disponibilidad y urgencias [Decidido, ADR 0016, decisión 11].** La disponibilidad del acceso en una urgencia es organización del centro y queda fuera de lo que decide la app, que solo exige que el aparato esté registrado y autorizado.
2. **Varios centros [Decidido].** El aparato se comprueba **al elegir el centro activo** (no en el login, que es antes de elegir centro: `0008:42-47`) y la petición va a la Administración de ese centro. **[Opinión]** Bien. Falta escribir: la persona solo ve y pide para centros en los que tiene ámbito; nada de otro centro es visible (patrón de `0007:72`); una cuenta con ámbito en dos centros necesita **una clave y una aprobación por centro** (apartado 1.6); y qué ve el centro sin restricción (nada).
3. **Revocar un aparato en uso [Decidido, ADR 0016, decisión 10].** Una tabla aparte vincula cada sesión con el aparato que demostró en cada centro; al revocar se cierran esas sesiones; el aparato no se guarda en `sesiones` ni en `eventos_acceso` (G5). **[Opinión]** Bien: evita crear un historial. Hay que purgar esa tabla al cerrarse la sesión (previsto) y que no entre en exportaciones.
4. **La pulsera NFC con PIN (I5) [Decidido, ADR 0016, decisión 12].** En un centro con restricción, solo en aparatos compartidos ya autorizados y no crea peticiones pendientes; en un aparato desconocido hay que entrar con contraseña o passkey. **[Opinión]** Coherente: un PIN es más débil y no debe servir para dar de alta aparatos.
5. **Seguridad por filas.** La lectura del aparato ocurre antes de que haya ámbito activo (como `cuentas`, `credenciales`, `sesiones`: `0062:12`). Si la tabla va con seguridad por filas, la comprobación inicial debe hacerse con un procedimiento acotado, no abriendo la lectura. Es una decisión de diseño con consecuencias de privacidad: **[A verificar]** con quien lo construya.
6. **Lista cerrada de eventos y de acciones.** Los tipos nuevos de `eventos_acceso` hay que añadirlos al `CHECK` (`0062:39-41`), y las acciones de la Administración a la lista cerrada de la auditoría con etiqueta (`expurgo-y-supresion.md:208`). Sin texto libre.
7. **Familiares fuera [Decidido].** Lo veo razonable (móvil propio en casa, sin vínculo laboral). Anótalo como **riesgo residual aceptado**: un familiar con credenciales robadas no encuentra esta capa. Y comprobar que una cuenta con perfil de personal y de familiar no deje un hueco.
8. **Promesa de la medida.** Al centro hay que presentarla como «reducción de riesgo», no como «solo entran aparatos seguros». No garantiza que el aparato esté libre de virus ni bien configurado, ni impide que un script dentro de la página abierta use la clave (5.3).

### 7.2 EIPD

- **[Opinión]** No hace falta una EIPD aparte: esta medida se incorpora como **sección** en la que ya está prevista para el acceso (ADR 0007, 0009: `0007:110`) y en la del centro. Contenido: finalidad cerrada, datos, flujos, la clave y su exención del art. 22.2, la ponderación del interés legítimo, riesgos para el trabajador (control laboral), medidas (G1 a G13) y riesgo residual.
- Aunque el módulo no vigila conducta, los **trabajadores** son una población en desequilibrio de poder con su empleador, y la tabla de eventos tiene IP. **[A verificar]** si la lista de la AEPD (art. 35.4) o los criterios del CEPD (WP248) incluyen la monitorización de empleados como supuesto que exija EIPD: no pude leerlos hoy. Si el DPO lo estima, la sección se vuelve EIPD independiente.

### 7.3 Ponderación del interés legítimo

El DPO necesita una hoja con tres columnas: interés del centro (proteger datos de salud), necesidad (¿hay un medio menos intrusivo? p. ej. solo contraseña + segundo paso + passkey; la medida añade una capa extra, con una clave que no se copia) y efecto sobre el trabajador (G1-G13). Redacción y resultado: **[Decidir]** DPO.

### 7.4 Registro de actividades (art. 30)

| Quién | Qué añadir |
|---|---|
| **Residencia** (art. 30.1) | Línea nueva «Control de aparatos autorizados». Responsable; finalidad (seguridad del acceso); personas (trabajadores); categorías de datos (apartado 1.2, con la clave pública); destinatarios (Administración del centro; ResidApp como encargada; Azure); operaciones (petición, aprobación, rechazo, revocación, caducidad); plazos (apartado 6: 7 días, 1 año / 90 días sin uso, 12 meses tras revocar); medidas (G1-G13, clave privada no extraíble, mínimo de dos aparatos de Administración). |
| **ResidApp como encargada** (art. 30.2) | Una línea por cada responsable, de lo que hace por su cuenta, incluidas las acciones de Plataforma sobre aparatos de un centro. |
| **ResidApp como responsable** (art. 30.1) | Línea para los aparatos de Plataforma (apartado 4.2). |

El art. 30.2 y 28.3 los leí en el BOE el 2026-11-09 (`modulos-opcionales-lectura-dpo.md:225`). No he releído hoy el 30.1.

### 7.5 Cláusula informativa y política de privacidad

- **Texto A** (sin cookie, solo la clave del aparato) para trabajadores, y su adaptación para el personal de Plataforma (ResidApp responsable).
- **Política de la aplicación** (cuando se construya; no la toco): `Privacy.cshtml:70-73` (añadir los datos del apartado 1.2, con la clave pública, a «Personal del centro»); `:78` (finalidad: ahora incluye autorizar aparatos y sigue sin haber clasificaciones por rendimiento); `:164-177` (la tabla de cookies **no cambia**: no hay cookie nueva); `:179-184` (nueva fila en «almacenamiento que no son cookies» para la clave del aparato); `:185` (borrar los datos del sitio obliga a pedir autorización otra vez; la clave privada nunca llega a ResidApp, como la passkey). Lo mismo en `politica-privacidad-portal.md:219-226` y el inventario B3 (`:328-350`), **donde `:226` y `:349` hay que corregir** («no usa bases de datos locales del navegador», «indexedDB: no hay ninguno»).
- **Documentación de usuario.** Manual, guía «Acceso y sesión» y `acceso-por-dispositivo.md:50` (que hoy dice que no hay restricción) deberán cambiar al construirlo; lo decide el proceso de `doc-usuario-residapp`.

### 7.6 Contrato de encargado y anexos

Añadir como **instrucciones** del centro a ResidApp (no como módulo, **[Suposición]**: es medida base de seguridad, no un módulo de pago; confirmar en el ADR 0015):
1. Activar o desactivar la restricción, y registrar un aparato en una emergencia, **solo por escrito** del centro (referencia documental) y con aviso a su Administración principal.
2. ResidApp trata esos datos (incluida la clave pública) **solo** para esa medida; no los usa para otro fin ni los comunica.
3. Sin subencargado nuevo (Azure ya lo es: **[A verificar]** que la clave y las tablas no añaden ninguno).
4. Plazos de conservación y borrado al terminar el contrato (art. 28.3.g: leído el 2026-11-09).
5. Ayuda al centro para atender derechos (acceso a la lista de aparatos de una cuenta) y notificar brechas «sin dilación indebida».
6. Cooperación en la auditoría de la medida y en la EIPD.

---

## 8. A verificar o decidir

### 8.1 Ya decidido por Orlando (el DPO real puede revisarlo)

1. **Mecanismo:** el aparato se identifica solo por su clave pública y firma un reto en cada entrada; **no hay cookie de aparato**. La clave privada es no extraíble y se guarda en IndexedDB.
2. **Caducidad por no uso en el servidor:** compartido 1 año, personal 90 días; cada uso renueva.
3. **Petición pendiente:** 7 días (con código obligatorio que se muestra en pantalla). La alternativa de 72 horas queda como observación, no como pendiente.
4. **Varios centros:** el aparato se comprueba al elegir el centro activo; la petición va a la Administración de ese centro.
5. **Mínimo:** dos aparatos de Administración aprobados para activar la restricción; no se puede revocar el último con la restricción activa; un aparato de Administración no caduca por no uso mientras haga falta para el mínimo (ADR 0016, decisión 9).
6. **Diseño de base:** restricción activable por centro desde Plataforma; Plataforma dentro, con restricción siempre activa y aprobación por otra cuenta de Plataforma (el primero y la recuperación, por SQL); aparato compartido o personal; registro por «pedir y aprobar» o por QR/enlace de un solo uso; familiares fuera.
7. **Urgencias y disponibilidad (ADR 0016, decisión 11):** la app no se ocupa de ello; con la restricción activa solo exige que el aparato esté registrado y autorizado. No hay acceso de emergencia sin aprobación ni clave de emergencia compartida.
8. **Sesión y pulsera:** vínculo sesión-aparato en tabla aparte que se purga al cerrar la sesión (decisión 10); pulsera con PIN solo en aparatos compartidos ya autorizados y sin crear peticiones (decisión 12).

### 8.2 Sigue abierto para el DPO real, abogado o laboralista (por importancia)

1. **[Decidir]** Base jurídica (interés legítimo con ponderación o art. 6.1.c) y que no se usa consentimiento.
2. **[Decidir, laboralista]** Que el BYOD voluntario con alternativa es admisible en el centro piloto: convenio colectivo, obligación de proporcionar medios, consulta con representantes de los trabajadores, política de uso de móvil propio, art. 20 bis ET y arts. 87-91 LOPDGDD.
3. **[Decidir]** Plazo y vía de borrado de `eventos_acceso` (incluida IP) y de los eventos nuevos.
4. **[Decidir]** Que las entradas normales no lleven el identificador del aparato personal (G5).
5. **[Decidir]** Texto A, B y C en español y catalán, y la información al personal de Plataforma.
6. **[Decidir]** Procedimiento y garantías de Plataforma sobre aparatos de un centro (instrucción documentada, QR de un solo uso, aviso, reactivación) y su salto de seguridad por filas.
7. **[A verificar]** Exención del art. 22.2 LSSI para la **clave en IndexedDB**: guía de cookies de la AEPD 2023, dictamen 04/2012 del Grupo del artículo 29 y Directrices 2/2023 del CEPD sobre el alcance técnico del art. 5.3 de la Directiva ePrivacy (versión final de octubre de 2024), incluidos sus ejemplos de almacenamiento local y la duración de un almacén sin caducidad propia.
8. **[Decidir]** Que la clave pública es dato personal a todos los efectos, que se usa una clave por registro y centro, y que se borra al revocar o caducar (1.6, 6).
9. **[Decidir]** Conservación tras revocar o caducar (12 meses propuestos, nombre y clave borrados al momento) y si se guarda una huella para detectar un aparato revocado.
10. **[A verificar]** Si es EIPD propia o sección de la existente (7.2).
11. **[Decidir]** Contrato de encargado: las seis instrucciones de 7.6.
12. **[Decidir]** Qué autoridad corresponde (AEPD o APDCAT) según el centro piloto. **[A verificar]**; no la he podido confirmar.

### 8.3 Condiciona activarlo con datos reales

Hasta que se cumpla **todo** esto, solo en desarrollo y con datos ficticios:

- [ ] DPO real o abogado designado y validación de este documento.
- [ ] Plazo de `eventos_acceso` fijado y vía de borrado construida (hoy imposible: `0062:49-55`).
- [ ] Texto A entregado a los trabajadores **antes** de activar y política de móvil propio del centro firmada.
- [ ] Alternativa real: el centro dispone de aparatos compartidos suficientes (G1).
- [ ] Garantías G1 a G6 implementadas y probadas (no ver IP ni historial, nombre acotado, «Mi cuenta», revocación al salir, sin identificador en entradas normales si se decide).
- [ ] Una clave por registro y centro; clave pública borrada al revocar o caducar; borrado de la clave local al quitar/expirar; caducidad por no uso (1 año / 90 días) aplicada en el servidor; probado en aparatos reales (Android e iPhone).
- [ ] Dos aparatos de Administración para activar y no poder revocar el último ni dejar caducar el que haga falta para el mínimo.
- [ ] Instrucción documentada del centro para activar, y procedimiento de Plataforma escrito.
- [ ] Política de privacidad, inventario de almacenamiento del navegador (con las dos frases erróneas corregidas) y manual actualizados con la clave y los datos nuevos.
- [ ] Línea en el registro de actividades (centro, ResidApp encargada, ResidApp responsable) y sección en la EIPD con la ponderación del interés legítimo.
- [ ] Instrucciones de 7.6 en el contrato de encargado.
- [ ] Verificación en fuente oficial de la exención del art. 22.2 para el almacenamiento de la clave (punto 7 de 8.2).

---

## Qué he verificado y qué no

**Verificado hoy (2026-11-10) leyendo el repositorio:** `docs/privacidad/modulos-opcionales-lectura-dpo.md`, `expurgo-y-supresion.md` (completo), `docs/decisiones-arquitectura/0007` y `0006` (completos), `0008` (líneas 36-71), `docs/decisiones-arquitectura/0016-dispositivos-autorizados.md` (líneas 36-47, tras su última reescritura: decisiones 8 a 12 y propuestas; el resto del ADR no lo he leído), `docs/seguridad/acceso-por-dispositivo.md`, `database/scripts/0062_credenciales_y_eventos_acceso.sql`, `src/ResidApp.Web/Views/Home/Privacy.cshtml` (líneas 62-86 y 162-186), `docs/privacidad/politica-privacidad-portal.md` (líneas 195-232; las líneas 328-350 por búsqueda con `Grep`) y listados de `antes-de-datos-reales.md` y `anexo-modulos-contrato-borrador.md`. **No he abierto** el código de sesiones, cookies ni los scripts posteriores al 0062; no he buscado en `src/` el uso de IndexedDB más allá de lo que dicen los documentos.

**Verificado hoy en fuentes oficiales, a través de una herramienta que devuelve resúmenes automáticos y no el texto literal** (por eso las citas son cortas y hay que contrastarlas):
- Ley 34/2002 (LSSI), art. 22.2, texto consolidado en `https://www.boe.es/buscar/act.php?id=BOE-A-2002-13758` (fragmentos literales de los tres párrafos del apartado 2).
- LO 3/2018 (LOPDGDD), arts. 87 a 91, en `https://www.boe.es/buscar/act.php?id=BOE-A-2018-16673` (resumen del contenido, no literal).
- Reglamento (UE) 2016/679, en `https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807`: art. 5.1.c y e, 6.1.b, c y f, 13.1 y 13.2 (parcial), 32.1 y 32.2 (resumen, sin las letras).

**Leído el 2026-11-09 por una lectura anterior mía y no repetido hoy:** RGPD arts. 5.2, 25.1, 28.2 (primera frase), 28.3.a, g y h, 28.10, 30.2, 35 y 36.1 y considerando 26 (`modulos-opcionales-lectura-dpo.md:225`).

**No verificado:** art. 4.1 y considerando 30 del RGPD (que un identificador en línea es dato personal; lo afirmo por conocimiento general); art. 88 RGPD; letras del art. 32.1; art. 9; art. 6.4; art. 30.1; Estatuto de los Trabajadores, art. 20 bis (solo resultados de búsqueda de fuentes secundarias); guía de cookies de la AEPD 2023 (la herramienta descargó el PDF en binario y no pude leerlo; solo tengo resúmenes de terceros de la lista de cookies exceptuadas de la guía anterior); dictamen 04/2012 del Grupo del artículo 29; Directrices 2/2023 del CEPD (solo resultados de búsqueda: versión 2.0 de octubre de 2024; no abrí el texto ni sus ejemplos de IndexedDB); lista de la AEPD del art. 35.4 y criterios WP248; convenio colectivo aplicable; autoridad de control del centro piloto (AEPD o APDCAT). **Las afirmaciones técnicas sobre WebCrypto, IndexedDB y el comportamiento de Safari las hago por conocimiento general o por lo que me indica el encargo; no las he comprobado en documentación de los navegadores ni en el código.**

Fuentes de búsqueda consultadas hoy (secundarias, no usar como cita): resúmenes de la guía de cookies de la AEPD en `https://www.aepd.es/prensa-y-comunicacion/notas-de-prensa/aepd-actualiza-guia-cookies-para-adaptarla-a-nuevas-directrices-cepd`, `https://www.ecija.com/actualidad-insights/guia-aepd-uso-cookies/` y `https://finreg360.com/alerta/analisis-de-la-guia-sobre-el-uso-de-cookies-de-la-agencia-espanola-de-proteccion-de-datos/`; Directrices 2/2023 del CEPD: `https://www.edpb.europa.eu/our-work-tools/documents/public-consultations/2023/guidelines-22023-technical-scope-art-53-eprivacy_en` y el PDF `https://edpb.europa.eu:443/system/files/2024-10/edpb_guidelines_202302_technical_scope_art_53_eprivacydirective_v2_en_0.pdf` (no abiertos).

**Yo no soy DPO designado ni abogado.** Todo lo anterior lo valida una persona antes de usarse con datos reales.
