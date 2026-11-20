# Pulsera NFC con PIN: guía de implementación

**Estado: diseño decidido el 2026-11-20, nada construido.** Este documento guía a quien construya la pulsera (I5 del ADR 0007, pieza 3 del ADR 0009, decisión 12 del ADR 0016). Parte de las cuatro decisiones de Orlando y de la lectura del agente `dpo-residapp` (`docs/privacidad/pulsera-nfc-pin-lectura-dpo.md`, un borrador que valida una persona antes de datos reales). Antes de cada script se consulta otra vez a `dpo-residapp`, como manda `CLAUDE.md`.

## 1. Qué se construye y qué no

**Se construye** una vía de entrada rápida para el personal de planta en tablets compartidas: se acerca la pulsera a la tablet, se teclea un PIN y se abre una sesión **pequeña** (solo lo que una Auxiliar necesita en planta).

**Decisiones de Orlando (2026-11-20):**

1. **B corregida.** La pulsera solo se ofrece en tablets que Administración ha registrado como «compartidas». Por eso, antes de la pulsera se construyen **D0, D1 y D2 del ADR 0016** (apartado 3). No hace falta el filtrado general de aparatos (D3 a D5).
2. **Pulsera NTAG 424 DNA.** Se descarta la llave FIDO2 con NFC (queda como opción futura). ResidApp custodia el PIN.
3. **Solo el perfil Auxiliar**, ligada al ámbito de su centro. Nunca Plataforma, Administración, Dirección Clínica ni Familiar. Enfermería y Medicina quedan fuera de la primera versión.
4. **Voluntaria** y **apagada por defecto** en cada centro. No sustituye a la contraseña con código ni a la passkey: es una vía más.

**No se construye (y no se debe colar):** un registro de «quién acercó la pulsera, cuándo y en qué tablet» (sería un control de horarios y ubicación del personal), una pulsera de invitado o de préstamo, ni un uso como registro de jornada (Estatuto de los Trabajadores, art. 34.9: es otra finalidad, con otra base).

## 2. Palabras que se usan

- **NFC:** comunicación a pocos centímetros, la del pago sin contacto.
- **Web NFC:** la parte del navegador que deja a una página web leer una etiqueta NFC. **Solo funciona en Chrome para Android** (ADR 0009) y **solo lee**: no puede programar las claves del chip.
- **NTAG 424 DNA:** el chip de NXP elegido. A diferencia de una pegatina corriente, **fabrica un mensaje distinto en cada lectura** y lo firma, así que copiar un mensaje viejo no sirve de mucho.
- **SDM** («mensaje dinámico seguro»): esa función del chip. **CMAC:** la firma del mensaje; solo quien tiene la clave del chip puede comprobarla.
- **Contador del chip:** un número que sube en cada lectura. Sirve para rechazar un mensaje repetido.
- **Random ID:** opción del chip para que no entregue su número de serie fijo a cualquier lector. **Activarla es irreversible.**
- **Clave «meta»:** la que descifra el número de serie dentro del mensaje. Es **una por centro y entorno** (no puede ser distinta por pulsera, porque hay que descifrar antes de saber de quién es). La clave que firma sí es distinta por pulsera.
- **Pimienta:** un secreto que se mezcla con el PIN antes de guardarlo y que **vive fuera de la base de datos**. Así, quien robe solo la base no puede probar PIN uno a uno.
- **Alcance reducido:** una sesión que solo permite una lista corta de acciones (lista blanca).
- **Key Vault:** la caja fuerte de claves de Azure (hoy solo existe en la propuesta de producción, ADR 0014).

## 3. Orden de construcción (piezas pequeñas, cada una en su PR)

Cada pieza llega a `main` y a Azure dev **con la pulsera apagada en todos los centros**. Cada una lleva su script, sus pruebas y su consulta a `dpo-residapp`, `qa-revisor-residapp` y `doc-usuario-residapp`.

| Pieza | Qué entrega |
|---|---|
| **P0 = D0 del ADR 0016** | Esquema de aparatos: tablas `aparatos`, `peticiones_aparato`, `sesiones_aparato`, seguridad por filas, interruptor del centro como histórico de solo INSERT. Sin pantallas. |
| **P1 = D1** | Clave del aparato: el navegador crea una clave que no se puede extraer y firma un reto del servidor (`wwwroot/js/aparatos.js`). |
| **P2 = D2** | Registro de un aparato por QR de un solo uso y pantalla «Aparatos» de Administración. Aquí se marca una tablet como **compartida**. |
| **P3** | Esquema de la pulsera (script nuevo): tabla de pulseras, tipo de sesión, eventos nuevos, interruptor del centro. Sin pantallas. |
| **P4** | Núcleo del servidor: comprobar el mensaje del chip, el PIN con su secreto y bloqueo, y abrir la sesión de alcance reducido. Con un «chip de pruebas» en software (como `SoftwareAuthenticator` en las passkeys). |
| **P5** | Sesión de alcance reducido: lista blanca en el servidor, inactividad corta, botón «Soy otra persona». |
| **P6** | Pantallas: «Mi cuenta» (activar, elegir PIN, quitar), Administración (alta, baja, desbloqueo), entrada en la tablet con Web NFC (JavaScript). |
| **P7** | Documentación de usuario, política de privacidad, procedimiento del centro y prueba con las tablets y pulseras reales. |

**Antes de cualquier pieza con datos reales:** las condiciones bloqueantes del apartado 9.

## 4. Datos que se guardan (propuesta, no decidida)

Mínimo posible. Nombres de columna provisionales; se fijan al escribir el script.

| Dato | Cómo | Para qué |
|---|---|---|
| Cuenta, centro y ámbito de perfil | referencias | La pulsera abre **ese ámbito**, no toda la cuenta. Si la cuenta lo pierde, la pulsera deja de servir. |
| Identificador del chip | **resumen con clave (HMAC)**, no el número en claro | Reconocer la pulsera sin guardar su número. Se borra al revocar. |
| Estado | activa, pendiente de activar, bloqueada, revocada | |
| Alta, baja y motivo de baja | fechas y lista cerrada (pérdida, robo, sustitución, baja del personal, sospecha) | Sin texto libre. |
| Último contador aceptado | **solo el último**, sin historial | Rechazar mensajes repetidos. |
| **Día** del último uso | solo el día, no la hora | Que Administración vea si se usa, sin crear un registro de horarios. |
| Intentos fallidos seguidos | contador **por pulsera, en SQL** | Bloqueo. |
| PIN | comprobante (HMAC con pimienta y sal de la pulsera), versión de la pimienta | Nunca el PIN. |
| Versión de clave (`kid`) | número | Convivir con dos lotes de claves. |

**Fila de la pulsera:** al revocar se borra el identificador del chip; la fila se borra a los 12 meses (propuesta, mismo patrón que el ADR 0016). **Sesiones:** una columna con el método de entrada (contraseña, passkey o pulsera), sin guardar *qué* pulsera.

## 5. Cómo funciona una entrada (de la tablet al servidor)

1. **La tablet abre una página inocua.** El mensaje del chip es una dirección web. Si un móvil cualquiera la abre, solo debe ver una página estática que no comprueba ni registra nada. **La comprobación va por POST**, nunca por la dirección.
2. **Web NFC lee el mensaje** cuando la persona acerca la pulsera (solo con la página en primer plano). La persona teclea su PIN.
3. **Una sola petición POST** lleva la lectura y el PIN. Así no se revela si la pulsera existe, está revocada o bloqueada antes de dar el PIN. Sin cookie nueva.
4. **El servidor:** (a) comprueba que el centro tiene la pulsera activada y que el aparato es una tablet compartida registrada; (b) descifra con la clave meta del centro el número de serie y el contador; (c) deriva la clave de firma de esa pulsera y comprueba la firma; (d) busca la pulsera por el resumen del número; (e) **cuenta el intento antes de comprobar el PIN**, en la misma sentencia SQL (para que 20 peticiones simultáneas no multipliquen los intentos); (f) compara el PIN **en tiempo constante**; (g) exige un contador **mayor** que el último y **solo lo avanza si la entrada fue correcta**; (h) abre la sesión de pulsera con el ámbito del alta.
5. **Cualquier fallo da el mismo mensaje** (no se dice si fue la pulsera, el PIN o el bloqueo).
6. **Cinco fallos seguidos bloquean la pulsera** hasta que Administración la desbloquee. No hay desbloqueo automático. El contador es **distinto del de la contraseña**: si no, quien lea una pulsera podría bloquear la entrada con contraseña de otra persona.

**Limitación que hay que conocer:** el mensaje no lleva un reto del servidor, así que una lectura robada con un móvil sigue valiendo hasta la siguiente entrada correcta. Lo sostienen el PIN con bloqueo y que la pulsera solo se ofrezca en tablets registradas.

## 6. El PIN

- **6 cifras** como mínimo, **lo elige la persona** en «Mi cuenta» pidiendo contraseña y código del móvil. **Administración nunca lo ve ni lo fija.** Su reinicio deja la pulsera «sin PIN» para que la persona ponga uno nuevo.
- Se prohíben los triviales: cifras repetidas, secuencias (123456, 654321), pares repetidos (121212) y una lista corta de los más habituales; nada derivado de datos personales.
- Comprobante = HMAC-SHA-256 con la **pimienta** (fuera de la base) y la **sal** de esa pulsera. La pimienta lleva versión: al acertar un PIN se recalcula con la vigente, así se puede rotar sin pedir PIN nuevos.
- **Límite honesto:** quien robe la base **y** la pimienta recupera todos los PIN. Por eso el bloqueo, el alcance reducido y las tablets registradas se mantienen aunque haya pimienta.
- El PIN **no puede aparecer** en ningún registro, evento ni mensaje de error (hay una prueba para eso).

## 7. La sesión de pulsera

- **Tipo de sesión en la fila de `dbo.sesiones`**, y la lista blanca se comprueba **en el servidor**. La cookie del ámbito activo **no es de fiar**: va sin cifrar ni firmar (ADR 0016, contexto). Esto es el mismo cambio que pide D3 del ADR 0016 (hoy `SqlAuthorizationEvidenceProvider` no recibe la sesión): conviene hacerlos juntos o dejar un único punto de decisión.
- **Política de sesión por tipo:** hoy es global (30 minutos de inactividad y 12 horas de tope). Para la pulsera, propuesta de 5 minutos de inactividad y 4 horas de tope; el valor definitivo es de CJ y del centro (punto abierto 3 del ADR 0007).
- **Lista blanca:** solo lo que CJ apruebe para «Auxiliar de planta» (abrir la ficha operativa, registrar cambios, el cierre cotidiano, el dictado). **Siempre vedado:** «Mi cuenta» (contraseña, passkeys, PIN, alta y baja de pulseras), administrar cuentas, permisos, ámbitos e invitaciones, firmar o cerrar nada con efectos legales, editar antecedentes de salud, traslados y bajas, Dirección, calidad, auditoría y declaraciones de acceso clínico, y exportaciones. Cada acción vedada devuelve lo mismo que «sin permiso» y exige entrar con contraseña con código o passkey.
- **Siempre a la vista:** el nombre de quien tiene la sesión y un botón claro «Soy otra persona / Cerrar sesión». Entrar con pulsera cierra la sesión que hubiera abierta en esa tablet.
- **Apagar la pulsera en un centro** cierra sus sesiones de pulsera.

## 8. Eventos, revocación y claves

**Eventos (lista cerrada, sin texto libre):** en `dbo.eventos_acceso`: `ENTRADA_PULSERA`, `PULSERA_PIN_FALLIDO`, `PULSERA_BLOQUEADA`, `PULSERA_REVOCADA_USADA`. En la auditoría administrativa: alta, activación, baja, desbloqueo y reinicio del PIN. **No** hay evento de lectura ni de aparato. Hay que ampliar el `CHECK` de `eventos_acceso` (longitud del tipo ≤ 32) y la lista de motivos de cierre de sesión (`CK_sesiones_cierre`, ≤ 20 caracteres; por ejemplo `PULSERA_REVOCADA`).

**Revocación inmediata:** por Administración (ficha de la cuenta) y por la propia persona desde «Mi cuenta» con contraseña con código o passkey. Cierra las sesiones con método pulsera de esa cuenta. También al suspender la cuenta, al perder el ámbito del centro y con la invitación de recuperación. Plataforma no revoca pulseras de un centro. **La pulsera de préstamo no existe:** quien olvida la suya entra con contraseña con código.

**Claves.** Una clave meta por centro y entorno, una clave maestra de derivación para las claves de firma, y la pimienta del PIN, **separadas**. Fuera de la base de datos, del repositorio y de los registros: **Key Vault de la UE en producción**, `dotnet user-secrets` o ajustes del entorno con **claves de prueba** en desarrollo (nunca reutilizadas en producción). La aplicación necesita las claves en memoria para comprobar cada firma: Key Vault protege el reposo y los permisos, no la memoria del servidor. **Rotar las claves de las pulseras equivale a cambiar las pulseras**; por eso hay una versión de clave (`kid`) y un 15 % de reserva en cada lote.

**Herramienta de personalización fuera de la PWA.** Cambiar las claves del chip, activar Random ID y proteger la escritura se hace con comandos que Web NFC no puede enviar. Hace falta un programa en un PC con **lector USB** (por ejemplo, un script en `tools\`), que es parte de la cadena de custodia. Hoy lo operaría una sola persona (limitación a declarar: no hay doble control).

**Interruptor por centro:** histórico de solo INSERT, como el del ADR 0016 (decisión de diseño de D0). Quién lo cambia: Plataforma, solo con instrucción documentada del centro y con auditoría. Apagado por defecto.

## 9. Condiciones del DPO

**Antes de construir con datos de prueba:** las decisiones del apartado 1; solo Auxiliar y ligada al ámbito; tipo de sesión en `sesiones` y lista blanca en el servidor; inactividad corta por tipo de sesión; PIN, bloqueo y pimienta como en el apartado 6; una sola petición POST y contador creciente; claves como en el apartado 8; eventos mínimos; revocación completa; interruptor apagado por defecto; cada acción vedada exige entrar de nuevo.

**Bloqueantes antes de datos reales:**
1. **Vía de borrado y plazo de `eventos_acceso`.** Hoy son inmutables y guardan la IP; la pulsera añade filas. Construir el procedimiento (patrón `EXECUTE AS OWNER` de `0085`) antes.
2. Validación de una persona (DPO real o abogado): base jurídica, autoridad de control (AEPD o APDCAT según el centro) y ponderación.
3. **Laboralista:** voluntariedad, consulta a la representación del personal (LOPDGDD, art. 87.3; Estatuto de los Trabajadores, art. 64.5.f), coste de la pulsera perdida, régimen disciplinario y exclusión expresa del registro de jornada.
4. Texto para el personal entregado **antes** de activarla (borrador en `docs/seguridad/pulsera-nfc-guia-administracion.md`), registro de actividades, anexo de la EIPD y política de privacidad actualizados.
5. Contrato de suministro de las pulseras (`docs/seguridad/pulsera-nfc-especificacion-compra.md`) y procedimiento del centro (pérdida, no prestar, desbloqueo, altas y bajas).
6. Prueba con las **tablets reales** del centro: NFC, Chrome, guantes, humedad, con la pulsera puesta.
7. Key Vault en producción y claves de producción distintas de las de desarrollo.
8. **CJ** aprueba la lista blanca de acciones.

## 10. Pruebas

- **Lista blanca:** cada acción vedada se prueba con una sesión de pulsera y devuelve lo mismo que sin permiso.
- **Contador:** un mensaje repetido se rechaza; solo avanza con una entrada correcta.
- **PIN:** erróneo, bloqueo a los cinco fallos, intento contado antes de comprobar (peticiones simultáneas), desbloqueo por Administración, PIN prohibidos, **el PIN no aparece en ningún registro ni evento**.
- **Revocación:** cierra las sesiones de pulsera; la pulsera de una cuenta suspendida, sin ámbito o recuperada ya no sirve.
- **Apagar el centro** cierra las sesiones de pulsera; **otro centro** no puede usar la pulsera (aislamiento por centro en todas las tablas nuevas).
- **Aparato no compartido o no registrado:** la pulsera no se ofrece ni se acepta.
- **Web NFC no se puede probar en un navegador sin interfaz:** prueba manual con tablets y pulseras reales; para lo que sea de la página, la skill `probar-en-navegador`.

## 11. Lo que falta verificar antes de comprar o construir

- Con el fabricante (NXP o su distribuidor): los derechos de acceso por defecto del fichero del mensaje; qué entregan los proveedores con Random ID ya activado; servicios de «claves a medida».
- Con aparatos reales: cómo se comporta Web NFC (permiso del navegador, apertura de la dirección fuera de la página, lectura con Random ID) y que las tablets del piloto tienen NFC.
- Si Key Vault calcula la firma (CMAC) o la aplicación debe tener la clave en memoria (se supone lo segundo).
- Cifras de longitud y reintentos del PIN en NIST SP 800-63-4 (solo se confirmó el principio).

## 12. Documentos que dejarán de ser ciertos al construir

`docs/privacidad/politica-privacidad-portal.md` (línea que dice «Pulsera NFC con PIN (I5): No construido») y `Views/Home/Privacy.cshtml`; `docs/seguridad/acceso-por-dispositivo.md`; el Manual; la guía «Acceso y sesión»; la Guía por perfil; ADR 0007 (fila I5) y ADR 0016 (decisión 12). Se pasan por `doc-usuario-residapp` al construir.
