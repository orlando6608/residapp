# Dictado por voz en ResidApp: dossier para la persona responsable de protección de datos

Fecha: 2026-10-23. Estado: **pendiente de su valoración**. Mientras no conteste, el dictado solo se usa con audios y textos inventados, nunca con residentes
reales, y está apagado por defecto.

Este documento reúne lo que hay que saber para decidir si el dictado por voz puede usarse con datos reales. No contiene consejo legal ni da por hecho que
haga falta o no una evaluación de impacto: eso lo decide usted. Cada término técnico lleva al lado una frase corriente.

## 1. Qué es y para qué sirve

En la pantalla «Registrar cambio» una auxiliar anota qué ha cambiado en un residente (cómo ha comido, si tiene dolor, una herida, etc.). Hoy lo hace tocando
casillas y escribiendo notas. El dictado le permite **contarlo hablando**:

1. Pulsa **Grabar**, cuenta lo ocurrido y pulsa **Parar**.
2. El audio viaja a un servicio externo que lo convierte en texto y propone qué casillas marcar y qué notas escribir.
3. La pantalla muestra esa propuesta como una **sugerencia**. La auxiliar tiene que revisar **una a una** cada opción sugerida («Está bien» o «Quitar») y
   hasta entonces el botón «Continuar» no avanza. Después confirma el registro como siempre.

El dictado no registra nada por sí solo ni decide nada clínico. Si no hay micrófono, si falla el servicio o si la persona prefiere no usarlo, el formulario
manual funciona igual que antes. Es la única función de inteligencia artificial aprobada en ResidApp (decisión de producto del 2026-10-23, documento
técnico `docs/decisiones-arquitectura/0011-arquitectura-ia.md`); el resumen clínico, la detección de riesgos o un asistente sobre el historial están descartados.

## 2. Qué datos salen de la aplicación y cuáles no

| Dato | ¿Sale hacia el proveedor? | Comentario |
|---|---|---|
| El audio de la grabación | **Sí** | Lleva la voz de la auxiliar y lo que dice del residente. Va al servicio de transcripción. |
| El texto de la transcripción | **Sí** | Va a un segundo modelo que lo convierte en casillas y notas sugeridas. |
| Nombre o identificador del residente | No | El residente se elige en pantalla y su nombre no se envía. |
| Centro, unidad, cuenta de la auxiliar | No | Tampoco se envían. |
| Historial, basal o cualquier otro dato del residente | No | El modelo no consulta nada de la aplicación. |

**Lo que dice la auxiliar en voz alta sí llega al proveedor.** Si pronuncia un nombre, estará en el audio y en la transcripción. Por eso la pantalla muestra
siempre el aviso «No digas nombres de personas.»; el sistema no puede impedirlo.

**Qué guarda ResidApp:** ni el audio ni el texto. Lo único que queda es una fila en el registro de auditoría (una tabla que solo admite añadir filas, no
modificarlas) que dice *quién* usó el dictado, *en qué unidad*, *sobre qué residente* y *cuándo*. Esa fila se escribe **antes** de enviar el audio: si no
puede escribirse, el audio no sale. El registro técnico de la aplicación anota el modelo usado, el tamaño del audio, la duración y la cantidad de
*tokens* (las unidades de texto que cuenta el proveedor para cobrar), pero nunca el contenido.

## 3. Quién es el proveedor y dónde procesa

- **Proveedor:** Mistral AI, empresa francesa. La aplicación solo admite proveedores de una **lista autorizada**, que hoy tiene únicamente a Mistral: si alguien
  configura otro, la aplicación no arranca. Añadir uno exigiría un nuevo documento de decisión, el contrato y una evaluación de impacto.
- **Dos servicios de Mistral:** uno que pasa la voz a texto (transcripción) y otro que, con ese texto, devuelve la sugerencia con un formato fijo.
- **Dónde:** por defecto, Mistral aloja y procesa en la **Unión Europea**; existe un punto de acceso de Estados Unidos que ResidApp **no** usa. Mistral avisa de que
  algunas funciones pueden mover temporalmente datos fuera de la UE y publica la lista de subencargados en su centro de confianza (*Trust Center*). **Sin verificar**
  si alguna de ellas afecta a los dos servicios que usamos: es una de las preguntas para Mistral (apartado 4).

## 4. El contrato con el proveedor

**Cómo funciona el contrato de Mistral** (según su addendum de tratamiento de datos, en adelante *DPA*; resumen de su página pública, **hay que leer el texto original**):

- Se incorpora **«por referencia»** a los términos comerciales: forma parte del contrato desde que el cliente acepta esos términos o desde que Mistral empieza a
  tratar datos personales por su cuenta, lo que ocurra antes. El texto no describe una firma aparte, aunque en un apartado habla de «aceptar» el DPA.
- Reparto de papeles: el cliente es el **responsable** del tratamiento y Mistral es el **encargado** (el que trata los datos siguiendo las instrucciones del responsable).
- **Entrenamiento de modelos: los dos textos no dicen lo mismo.** Los términos comerciales (apartado 4.2) dicen que Mistral **no** usa los datos del cliente para entrenar,
  salvo en casos listados: ciertos ajustes de aceptar o rechazar, los comentarios («me gusta / no me gusta») que envíe un usuario, lo que prevea un pedido concreto y el uso de
  modelos en pruebas (*Labs* o *Preview*). En cambio, el resumen del DPA (apartado 2.3) dice que Mistral puede entrenar, según su política de privacidad, **salvo que el cliente
  lo haya desactivado**. Hay que pedir a Mistral que aclare cuál manda, desactivar el entrenamiento en la cuenta y comprobarlo, y confirmar que los dos modelos que usa
  ResidApp (transcripción y salida estructurada, ambos con nombre «latest», que puede apuntar a versiones nuevas) **no son** modelos de prueba (Labs o Preview).
- **Vigilancia de abusos:** hay moderación automática en la API. Con la retención cero (siguiente punto) no se aplica.
- **Retención:** el anexo del DPA dice que los datos se conservan «durante el contrato». La **retención cero** (*Zero Data Retention*, ZDR) es un modo en el que Mistral no
  conserva el contenido de las peticiones. Cubre los dos servicios que usamos, pero solo en la modalidad de pago por uso y **con la aprobación de Mistral, que puede
  rechazarla**; hay que pedirla con una justificación.
- **Subencargados** (empresas que Mistral contrata para tratar los datos): autorización general; se puede objetar por escrito en diez días y por motivos razonables; si no,
  se dan por aprobados.
- **Fuera de la UE:** puede transferir datos a países con decisión de adecuación o con cláusulas contractuales tipo.

**La pega principal:** el anexo del DPA indica «categorías especiales de datos: ninguna» y **no dice nada de datos de salud**. El dictado puede llevar datos de salud
(«no quiere comer», «tiene una herida en el talón»), aunque no vayan asociados a un nombre. Hay que pedir a Mistral que confirme por escrito que el DPA cubre esos datos, o que
lo amplíe.

**Qué habría que pedir o comprobar antes de datos reales:**

1. Confirmación escrita de que el DPA vigente cubre datos de salud.
2. Aclaración escrita sobre el entrenamiento (apartado anterior), entrenamiento desactivado en la cuenta y comprobado, y los dos modelos fuera de Labs y Preview.
3. Retención cero aprobada para los dos servicios.
4. Uso exclusivo del punto de acceso de la UE y confirmación de que los dos servicios no mueven datos fuera de la UE.
5. Lista actual de subencargados y su ubicación.
6. Versión vigente del DPA: una consulta indicaba que entró en vigor el 12 de marzo de 2026 y otra, el 27 de julio de 2026. **Sin verificar cuál rige.**

**Cadena de contratos (pregunta para usted, no afirmación):** el responsable del tratamiento de los residentes es el centro. ResidApp actuaría como su encargado, y
Mistral, como subencargado de ResidApp. Eso exigiría que el centro autorice por escrito a ResidApp a contratar a Mistral. Quién firma y a nombre de quién está sin decidir.

## 5. Medidas que ya tiene la aplicación

Cada una se comprueba con una prueba automática, salvo donde se indica.

| Medida | Prueba que la cubre |
|---|---|
| Sin clave del proveedor, el dictado está apagado (la pantalla no lo ofrece y el servidor responde «no existe») | `SinLaClaveDelProveedor_ElDictadoEstaDesactivado_Y404`, `LaPantallaDeRegistrarCambio_OfreceElDictadoSoloConClave_ConElAvisoDeNoDecirNombres` |
| Solo un proveedor autorizado; otro impide arrancar | `LosProveedoresAutorizados_SoloAdmitenMistral_YSinClaveElDictadoEstaDesactivado` |
| Solo una auxiliar con ese residente asignado puede usarlo | `Sugerir_ConUnResidenteNoAsignado_EsAccesoDenegado_SinAuditarNiLlamarAlProveedor`, `ConOtroPerfilOUnResidenteNoAsignado_Responde403_SinAuditarNiLlamarAlProveedor` |
| El uso se anota **antes** de enviar el audio; si falla la anotación, no sale nada | `Sugerir_AuditaAntesDeTranscribir_YDevuelveLaSugerenciaSaneada`, `Sugerir_SiLaAuditoriaFalla_NoSaleNadaHaciaElProveedor` |
| La fila de auditoría no lleva audio ni texto | `Sugerir_ConUnResidenteAsignado_AuditaUnaVez_YLlamaAlProveedorDespues` |
| Ni el audio ni el texto se guardan | Por diseño: la aplicación no tiene dónde guardarlos. No hay una prueba que demuestre una ausencia. |
| El registro técnico no lleva contenido | `ElRegistro_LlevaModeloDuracionYTokens_PeroNuncaElContenido` |
| El modelo no puede devolver nada fuera del catálogo del formulario; lo que devuelva se valida en el servidor | `Esquema_TieneCadaAreaConSoloSusOpciones_YNingunaTemperatura`, tres pruebas `Saneado_…` |
| Límite de usos por minuto y por cuenta | `ElLimiteSeAplicaPorCuenta_NoPorDireccionIp` |
| Protección contra envíos falsos desde otra web (token antifalsificación) | `SinTokenAntiforgery_Responde400_SinAuditarNiLlamarAlProveedor` |
| Un fallo del proveedor no se explica al usuario con detalles | `SiElProveedorFalla_Responde503_SinFiltrarDetalles` |
| Audios vacíos, demasiado grandes o que no son audio se rechazan antes de enviar nada | `Sugerir_ConUnAudioVacioGrandeOQueNoEsAudio_EsEntradaInvalida_SinAuditar` |
| Aviso «No digas nombres de personas.» siempre visible | La prueba de la pantalla citada arriba. |
| Revisión una a una de cada opción sugerida antes de continuar | **Comprobada a mano en un navegador real, sin prueba automática.** |
| Sin temperatura sugerida | `Esquema_TieneCadaAreaConSoloSusOpciones_YNingunaTemperatura` |

## 6. Riesgos conocidos y no resueltos

- **Nombres dichos en voz alta.** El aviso ayuda, pero no impide que una auxiliar diga el nombre de un residente o de un familiar. Está planteado a la responsable de producto
  (CJ) si además debe avisar el sistema cuando crea haber oído un nombre; no está construido.
- **El modelo se equivoca.** En la prueba de precisión marcó «úlcera por presión» cuando la auxiliar solo dijo «sacro enrojecido», y a veces marca de más. La mitigación es que
  cada opción sugerida se revise una a una; el riesgo es que la persona se fíe y pulse «Está bien» sin mirar.
- **La precisión está medida solo con voz sintética limpia.** Se probaron 31 frases con jerga de planta; los resultados esperados los propuso ingeniería y **no los ha validado
  todavía la responsable clínica**. No se ha probado con ruido real, acentos reales ni móviles reales.
- **La voz de la auxiliar es un dato personal suyo.** Hay que informarla de que se envía a un tercero. Se usa para transcribir, no para identificarla.
- **Un tercero nuevo en el tratamiento.** Cualquier fallo, cambio de condiciones o incidente de Mistral afecta al dictado.
- **Procesado fuera de la UE** en alguna función de Mistral: sin verificar (apartado 3).
- **Aún no hay contrato adaptado a datos de salud ni evaluación de impacto**: por eso el dictado sigue apagado fuera de la prueba con datos inventados.

## 7. Preguntas para usted

1. ¿Hace falta una evaluación de impacto (EIPD) para este tratamiento y con qué alcance (un centro, un piloto, todos)?
2. ¿Cuál es la base jurídica y la condición que levanta la prohibición de tratar datos de salud, y quién la documenta?
3. ¿Cómo y a quién hay que informar (residentes, familias, trabajadoras y sus representantes) y con qué texto?
4. ¿Quién es el responsable, quién el encargado y quién firma o autoriza la cadena centro → ResidApp → Mistral?
5. ¿Basta el DPA estándar de Mistral o hay que pedir una ampliación escrita para datos de salud?
6. ¿Se exige la retención cero como condición? ¿Se acepta que Mistral pueda rechazarla?
7. ¿La voz debe considerarse un dato biométrico aquí? (Solo se usa para transcribir, no para identificar a nadie.)
8. ¿Cuánto tiempo se conservan las filas de auditoría del dictado?
9. ¿Se aceptaría un piloto limitado con datos reales (un centro, unas pocas auxiliares) una vez cubiertos los puntos anteriores, o solo después de la evaluación de impacto?
10. ¿Debe el sistema avisar si cree haber oído un nombre de persona, o basta el aviso fijo?

## 8. Una alternativa sin un tercero nuevo

El documento técnico prevé, como puerta abierta, **ejecutar el modelo en un servidor propio** dentro de la UE (por ejemplo, donde ya está alojada la aplicación) en lugar de
enviar el audio a Mistral. Eliminaría al tercero, aunque no necesariamente la evaluación de impacto. **Sin estudiar:** no se ha medido si acertaría igual, ni su coste, ni sus
requisitos técnicos, ni la licencia de los modelos. Solo se estudiaría si usted considera que el contrato con Mistral no es viable.

## 9. Estado

- **Hecho:** el servidor y la pantalla del dictado, probados con un proveedor simulado y, a mano, con Mistral real y audios sintéticos en un ordenador de desarrollo.
- **Espera a la responsable clínica (CJ):** el rango válido de temperatura (hoy no se sugiere ninguna), qué se revisa una a una, y la validación de las 31 frases de prueba.
- **No hay datos reales** en ningún entorno. La clave de Mistral no está puesta en el entorno de pruebas de Azure.

## 10. Fuentes (consultadas el 2026-10-23)

- [Addendum de tratamiento de datos de Mistral](https://legal.mistral.ai/terms/data-processing-addendum)
- [Retención cero (Help Center)](https://help.mistral.ai/en/articles/347612-can-i-activate-zero-data-retention-zdr) y [en la documentación](https://docs.mistral.ai/admin/monitor-comply/zero-data-retention)
- [Dónde se almacenan los datos](https://help.mistral.ai/en/articles/347629-where-do-you-store-my-data-or-my-organization-s-data)
- [Dónde encontrar el DPA](https://help.mistral.ai/en/articles/347641-where-can-i-find-your-dpa-data-processing-agreement)
- [Términos comerciales de Mistral](https://legal.mistral.ai/terms/commercial-terms-of-service/)

Los términos de un proveedor cambian. Este resumen se hizo a partir de las páginas públicas, no de un contrato firmado: **léalos de nuevo antes de decidir**.
