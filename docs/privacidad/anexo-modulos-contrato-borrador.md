# Anexo de módulos del contrato entre ResidApp y la Residencia: borrador para revisión jurídica

**BORRADOR DE TRABAJO. NO ES UN DICTAMEN JURÍDICO NI UN CONTRATO LISTO PARA FIRMAR.** Fecha: 2026-11-09. Lo ha redactado el agente `dpo-residapp` (segunda lectura experta, no DPO designado ni abogado) a petición de Orlando. Lo debe revisar, corregir y, en su caso, adoptar un **abogado o un DPO real** antes de ofrecerlo a ninguna residencia. Hasta entonces no se firma, no se envía a un centro y no se activa ningún módulo con residentes reales sobre la base de este texto.

Etiquetas: **[Hecho]** verificado en el repositorio o en fuente oficial (con archivo y línea o URL); **[Propuesta]** lo que propone el ADR 0015 o este borrador; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no se ha comprobado en fuente oficial; **[Opinión]** criterio del agente, discutible. Las partes en `[CORCHETES]` son campos en blanco. Este borrador no contiene datos personales reales, importes ni plazos inventados.

Base: ADR 0015 (`docs/decisiones-arquitectura/0015-modulos-opcionales.md`, estado «propuesta») y la lectura previa `docs/privacidad/modulos-opcionales-lectura-dpo.md`. Decisiones de Orlando del 2026-11-09 que este borrador da por dadas (no se discuten aquí): cada módulo es una compra de la Residencia, definida en este anexo, con pago diferenciado; los importes no van en este documento ni en la aplicación; lo que no es módulo es base; si un módulo se apaga o vence, lo registrado queda en solo lectura; el anexo es, en principio, la instrucción documentada del centro; Plataforma ejecuta la activación y no se enciende un módulo con residentes reales sin anexo firmado.

---

## 0. Qué es este documento y qué le falta

### 0.1 Qué es

Un **anexo** pensado para colgar de un contrato marco entre ResidApp (proveedor y **encargada del tratamiento**) y una Residencia (cliente y **responsable del tratamiento**). Sirve para tres cosas a la vez:

1. Dejar escrito **qué módulos opcionales ha comprado** la Residencia, con su ficha de privacidad.
2. Ser la **instrucción documentada** de la Residencia a ResidApp para activar cada módulo (RGPD art. 28.3.a: la encargada «tratará los datos personales únicamente siguiendo instrucciones documentadas del responsable»; leído en el BOE el 2026-11-09).
3. Fijar **qué pasa al apagar, vencer o terminar** un módulo.

Dos términos que se usan en todo el texto:

- **Encargada / responsable.** La Residencia decide para qué y cómo se tratan los datos de sus residentes, familias y personal (es la *responsable*). ResidApp los trata por cuenta de la Residencia y siguiendo sus instrucciones (es la *encargada*). Si ResidApp decidiera por su cuenta fines o medios, pasaría a ser responsable de ese tratamiento (RGPD art. 28.10, BOE 2026-11-09). Ese es el motivo de que activar un módulo requiera una instrucción escrita de la Residencia.
- **Plataforma.** El perfil con el que el personal de ResidApp opera la aplicación. No ve residentes ni contenido clínico y no lee la auditoría de otros centros (`docs/privacidad/antes-de-datos-reales.md:136-138`). Sí ve y escribe la estructura de cada centro y, según el ADR 0015, vería qué módulos tiene cada uno.

### 0.2 Estado: qué existe y qué no (comprobado en el repositorio el 2026-11-09)

| Pieza | Estado | Dónde se comprueba |
|---|---|---|
| **Sociedad titular de ResidApp** | **No existe.** Es la dueña futura del repositorio y de la cuenta de Azure; el ADR que la prevé está aplazado hasta que exista. Hasta entonces no hay parte contratante por el lado de ResidApp. | `docs/decisiones-arquitectura/0014-entorno-produccion.md:6, 57, 298`; `docs/privacidad/antes-de-datos-reales.md:108` (el titular se decide «antes del primer centro real») |
| **Contrato marco con las residencias** | **No existe** en el repositorio. Solo hay menciones a que falta (búsqueda hecha con Grep en `docs/` y Glob de ficheros con «contrato», «encargado», «dpa» y «anexo»: ningún borrador de contrato). | `docs/privacidad/politica-privacidad-portal.md:268, 412, 450` («No existe ninguno firmado»); `docs/tareas/futuro/pendientes-landing.md:35` |
| **Plantilla de contrato de encargado (art. 28)** | **No existe.** Lo único escrito son listas de lo que debe contener. | `docs/privacidad/expurgo-y-supresion.md:40, 186, 435`; `docs/privacidad/entorno-produccion-lectura-dpo.md:69, 248`; `docs/privacidad/aviso-cita-por-correo.md:150, 168` |
| **Anexo de subencargados** (Azure, correo, Mistral) | No existe. El ADR 0015 pide que Mistral conste como **condicional** («solo con el módulo Dictado por voz»). | `docs/decisiones-arquitectura/0015-modulos-opcionales.md:97`; `modulos-opcionales-lectura-dpo.md:188` |
| **Interruptor de módulos por centro** (tabla `modulos_centro`, script 0092, catálogo, comprobación) | **No existe.** El ADR 0015 es una propuesta, «solo documentación; no hay código ni SQL». | `0015-modulos-opcionales.md:3, 19` |
| **Dictado por voz hoy** | Existe, pero se enciende **para todos los centros o para ninguno**, con la clave global `IA__Mistral__ApiKey`; sin clave, 404. | `0015-modulos-opcionales.md:20-22`; `src/ResidApp.Web/Controllers/AuxiliarController.Dictado.cs:29` (según el ADR) |
| **Evaluación de impacto (EIPD), contrato con Mistral adaptado a datos de salud, retención cero** | No hechos. «Sin contrato adaptado a datos de salud ni evaluación de impacto: por eso el dictado sigue apagado.» No hay datos reales en ningún entorno. | `docs/seguridad/dictado-por-voz-dossier-dpo.md:119, 142-144` |
| **DPO o abogado reales** | No hay. «Aún no existe» y se busca «antes del primer centro real». | `antes-de-datos-reales.md:111` |
| **Exportación de datos asistenciales** | No existe. La exportación de hoy es solo estructura y censo de residentes activos. | `docs/decisiones-arquitectura/0010-importacion-exportacion-ficheros.md:38, 72`; `0015-modulos-opcionales.md:33` |

**Consecuencia para quien revise** [Opinión]: este anexo describe un diseño **propuesto**, no un sistema que ya funcione. Mientras no exista el interruptor por centro, firmar este anexo como si «el centro tuviera activado el dictado» sería inexacto, porque hoy la activación es global. La cláusula 5.5 recoge esa condición.

### 0.3 Qué se necesita para poder firmarlo

1. Que exista la parte contratante por el lado de ResidApp (sociedad titular o persona física; condiciona el contrato de encargado: `antes-de-datos-reales.md:108`).
2. Que exista el contrato marco y el contrato de encargado (art. 28) al que este anexo se refiere, con su anexo de subencargados.
3. Que un abogado o DPO real confirme que este anexo vale como instrucción documentada (ADR 0015, puerta 3).
4. Que esté construido el interruptor por centro, o que la primera activación se haga con otro procedimiento que el abogado considere equivalente (cláusula 5.5).
5. Que se cumplan las condiciones de la ficha de cada módulo (apartado 4).

---

## 1. Cómo se lee este anexo

- **Cuerpo del anexo** (apartados 2, 3 y 5 a 8): reglas comunes a todos los módulos.
- **Fichas de privacidad** (apartado 4): una por módulo. La ficha dice qué datos trata el módulo, qué terceros intervienen, qué debe tener hecho la Residencia antes y qué se conserva al apagarlo. Cada ficha tiene **versión**. Lo que la Residencia contrata es «el módulo X en la versión N de su ficha».
- **Lo que no es módulo es base**: todo lo que ya existe en ResidApp y no figura en la tabla del apartado 3, incluida la exportación de estructura y censo y el correo (que es una función de la plataforma, no un módulo; ADR 0015, líneas 41-44). Un módulo apagado o vencido nunca quita funciones base.
- **Importes y periodicidad**: no figuran en este anexo. Se rellenan en la oferta o en el contrato, que es donde se factura. **La aplicación no guarda precios**; solo guarda la referencia de este anexo, su versión y la vigencia (ADR 0015, punto 4 y puerta 9).

---

## 2. Datos del contrato y de las partes (campos a rellenar)

| Campo | Valor |
|---|---|
| Contrato marco al que se adhiere este anexo (título, fecha, referencia) | `[CONTRATO MARCO]` **[Decidir: aún no existe]** |
| Contrato de encargado del tratamiento (art. 28) vigente | `[CONTRATO DE ENCARGADO]` **[Decidir: aún no existe]** |
| **Residencia (responsable del tratamiento)**: razón social, NIF, domicilio, centro/s | `[RESIDENCIA]`, `[NIF]`, `[DOMICILIO]`, `[CENTROS AFECTADOS]` |
| Persona que firma por la Residencia y su poder (cargo, escritura o acuerdo que lo acredita) | `[FIRMANTE RESIDENCIA]`, `[PODER]` |
| Persona autorizada por la Residencia para pedir cambios de módulos (cláusula 5.3), con suplente | `[PERSONA AUTORIZADA]`, `[SUPLENTE]` |
| DPO de la Residencia, si lo tiene | `[DPO RESIDENCIA]` |
| **ResidApp (encargada del tratamiento)**: razón social o titular, NIF, domicilio | `[RESIDAPP TITULAR]`, `[NIF]`, `[DOMICILIO]` **[Decidir: persona física o sociedad; no existe sociedad titular]** |
| Persona que firma por ResidApp | `[FIRMANTE RESIDAPP]` |
| Autoridad de control competente | `[AUTORIDAD]` (AEPD o APDCAT) **[A verificar]** según titularidad pública, concertada o privada del centro y el ámbito de la Llei 32/2010; no se ha podido confirmar para el centro piloto (`modulos-opcionales-lectura-dpo.md`, «A verificar») |
| **Versión de este anexo** | `[VERSIÓN DEL ANEXO]` (ejemplo de formato: «Anexo de módulos, versión 1»). El esquema de numeración lo fija quien redacte el contrato |
| Fecha de la versión | `[FECHA]` |
| **Vigencia general del anexo** | Desde `[FECHA INICIO]` hasta `[FECHA FIN o «duración del contrato marco»]` **[Decidir]**. La vigencia de cada módulo (apartado 3) puede ser distinta y no puede superar la del anexo |
| Renovación | `[¿Renovación expresa o tácita?]` **[Decidir]**. Ver pregunta 6 del apartado 9: una renovación tácita del contrato comercial no debería renovar sin más la instrucción de tratamiento |

---

## 3. Módulos contratados

Un módulo solo está contratado si su fila está rellenada y firmada. Una fila vacía significa «no contratado».

| Código del catálogo | Nombre | Versión de la ficha aceptada | Vigente desde | Vigente hasta | Precio y periodicidad | Centro/s a los que aplica |
|---|---|---|---|---|---|---|
| `DICTADO_VOZ` | Dictado por voz | `[VERSIÓN FICHA]` (apartado 4.1) | `[FECHA]` | `[FECHA]` | **Ver oferta `[REFERENCIA OFERTA]`** (no se fija aquí) | `[CENTRO/S]` |
| *(futuro)* `[CÓDIGO]` | Dashboard de Dirección | No contratable todavía (apartado 4.3) | — | — | — | — |
| *(futuro)* `[CÓDIGO]` | Medicamentos | **Bloqueado** (apartado 4.4) | — | — | — | — |

Notas para esta tabla:

1. **Código estable.** El código (`DICTADO_VOZ`) es el identificador del catálogo de la aplicación y no cambia. El nombre en pantalla sí puede cambiar (ADR 0015, punto 1).
2. **Qué guarda la aplicación de esta fila** [Propuesta, ADR 0015 punto 2 y 4]: centro, módulo, estado (`ACTIVE` o `DISABLED`), referencia de este anexo, su versión, la versión de la ficha aceptada, `vigente_desde` y `vigente_hasta`. No guarda el importe.
3. **Pasada la fecha «Vigente hasta», el módulo pasa solo a solo lectura**, sin que nadie tenga que hacer nada (cláusula 6.3). **[Decidir]** cómo se interpreta el último día (hasta las 24:00 de qué zona horaria) y cómo se comunica el vencimiento próximo.
4. **Módulos futuros.** El catálogo prevé más módulos, pero hoy solo existe `DICTADO_VOZ`. Cualquier módulo nuevo se añade **por una versión nueva de este anexo o por un anexo adicional**, con su propia ficha y su propia firma. Ningún módulo se entiende contratado por estar en el catálogo de la aplicación, por «venir incluido» ni por una prueba gratuita (cláusula 5.1).
5. **Dependencias** entre módulos (un módulo que necesita otro): la regla de apagado en cascada no está decidida (ADR 0015, punto 5 y puerta 4). **[Decidir]** cuando exista el primer módulo con dependencias; hoy no aplica.

---

## 4. Fichas de privacidad por módulo

### 4.1 Ficha `DICTADO_VOZ` · Dictado por voz · versión de la ficha `[VERSIÓN FICHA]`

> **Estado de la ficha:** borrador. Su contenido sale de `docs/seguridad/dictado-por-voz-dossier-dpo.md` (2026-10-23, pendiente de valoración del DPO real), del ADR 0009, del ADR 0011 y del ADR 0015. Los datos sobre el contrato de Mistral son un resumen de páginas públicas, **no de un contrato firmado**; hay que leer los textos originales (dossier, apartado 4 y cierre).

**a) Finalidad.** Que una auxiliar, en la pantalla «Registrar cambio», pueda **contar hablando** lo ocurrido a un residente en lugar de teclearlo. El audio se convierte en texto y el sistema propone qué casillas marcar y qué notas escribir. Es una **sugerencia**: la auxiliar revisa cada opción sugerida una a una («Está bien» o «Quitar») y confirma el registro como siempre (dossier:11-21; ADR 0009:26-43). El dictado no registra ni decide nada clínico por sí solo. Es la **única** función de inteligencia artificial aprobada en ResidApp (ADR 0011:38).

**b) Quién y qué datos.**

| Dato | Persona afectada | ¿Sale de ResidApp hacia Mistral? | ¿Lo guarda ResidApp? |
|---|---|---|---|
| **Audio** de la grabación: la voz de la auxiliar y lo que dice del residente (puede ser dato de salud) | Personal (voz) y residente (contenido) | **Sí** | **No** |
| **Texto de la transcripción** | Igual | **Sí** (segundo servicio de Mistral) | **No** |
| Nombre o identificador del residente | Residente | **No** (se elige en pantalla y no se envía) | — |
| Centro, unidad, cuenta de la auxiliar | Personal | **No** | — |
| Historial, datos basales u otros datos del residente | Residente | **No** (el modelo no consulta la aplicación) | — |

Fuentes: dossier:25-31; ADR 0009:50-51; ADR 0011:37 («Al proveedor no va nada que identifique al residente. Ni el audio ni la transcripción se guardan»). **[Hecho]**

Lo que sí llega a Mistral sin que ResidApp pueda impedirlo: **si la auxiliar dice un nombre en voz alta, queda en el audio y en el texto**. Por eso la pantalla muestra siempre «No digas nombres de personas.» (dossier:33-35, 110-111). Está planteado a CJ si el sistema debe avisar además cuando crea oír un nombre; no está construido.

**Lo que ResidApp sí conserva por el uso del dictado** [Hecho]:

- Una fila en la auditoría del centro (tabla de solo añadir) con **cuenta de la auxiliar, perfil, centro, unidad, residente, identificador de la operación y fecha y hora**, sin audio ni texto. Se escribe **antes** de enviar el audio; si no puede escribirse, el audio no sale (`src/ResidApp.Infrastructure/Persistence/SqlVoiceDictationAudit.cs:6-19`; dossier:36-38). **Esa fila identifica al residente y a la auxiliar**: es dato personal del centro y no se confunde con el histórico de activaciones de módulos del apartado 8, que no lleva datos de residentes.
- Un registro técnico sin contenido: modelo usado, tamaño y duración del audio y número de *tokens* (las unidades de texto que cuenta el proveedor para cobrar) (dossier:38-39).
- Los datos que la auxiliar **confirmó** en el registro, que son datos clínicos normales de la aplicación (base del producto, no del módulo).

**c) Subencargado condicional: Mistral AI.**

- **Quién es.** Empresa francesa. Dos servicios: transcripción de voz a texto (Voxtral) y generación de la sugerencia con salida de formato fijo (Mistral Small) (ADR 0009:28-32). La aplicación solo admite proveedores de una lista autorizada que hoy contiene solo a Mistral (ADR 0011:58-61).
- **Condicional.** Mistral intervendría **solo en los centros que tengan contratado y activo `DICTADO_VOZ`**. Debe constar en el anexo de subencargados del contrato de encargado con esa condición (ADR 0015:97).
- **Cadena.** Residencia (responsable) → ResidApp (encargada) → Mistral (subencargado). Exige que la Residencia **autorice por escrito** a ResidApp a contratar a Mistral (RGPD art. 28.2: autorización previa por escrito, específica o general; texto literal comprobado el 2026-11-12: si la autorización es general, «el encargado informará al responsable de cualquier cambio previsto» dándole «la oportunidad de oponerse»; con una autorización específica, como la de esta ficha, ese deber de aviso no se activa para este subencargado). Esta ficha, firmada, es la **autorización específica** de la Residencia para este subencargado y este fin **[Decidir]** si el abogado prefiere una autorización en el contrato de encargado y aquí solo una referencia. ResidApp responde ante la Residencia del cumplimiento de Mistral (art. 28.4: el encargado inicial «sigue siendo plenamente responsable»; BOE 2026-11-09).
- **Región.** Por defecto Mistral aloja y procesa en la Unión Europea; existe un punto de acceso de Estados Unidos que ResidApp **no** usa. Mistral avisa de que algunas funciones pueden mover datos temporalmente fuera de la UE: **sin verificar** si afecta a los dos servicios usados (dossier:41-48). `[REGIÓN CONFIRMADA POR ESCRITO]` **[A verificar antes de activar]**.
- **Contrato de encargado con Mistral** (el que firma ResidApp con Mistral): se incorpora por referencia a sus términos comerciales. **Problemas sin resolver** (dossier:50-84):
  1. El anexo de su DPA dice «categorías especiales de datos: ninguna» y **no menciona datos de salud**; hay que obtener confirmación escrita de que el DPA cubre datos de salud.
  2. Sus textos no dicen lo mismo sobre **entrenamiento de modelos**: hay que obtener aclaración escrita, desactivarlo en la cuenta y comprobarlo.
  3. Los dos modelos usados llevan nombre «latest» (puede apuntar a versiones nuevas): hay que confirmar que **no son modelos de prueba** (*Labs* o *Preview*).
  4. Subencargados de Mistral: autorización general; se puede objetar en diez días y por motivos razonables (dossier:66-67). Ver cláusula 7.3.
  5. Versión vigente del DPA sin determinar (dos fechas distintas en dos consultas; dossier:81).
- **Retención cero por escrito: condición bloqueante.** La retención cero (*Zero Data Retention*) es un modo en el que Mistral no conserva el contenido de las peticiones. Según el DPA, sin ella los datos se conservan «durante el contrato», y Mistral puede **rechazarla** (dossier:63-65). Como la clave de Mistral es **una sola para todos los centros**, Mistral no puede distinguir ni borrar por centro (ADR 0015:84-85). Por tanto: **`DICTADO_VOZ` no se activa en ningún centro con residentes reales mientras ResidApp no tenga la retención cero aprobada por escrito para los dos servicios**. Si Mistral la rechaza: **[Decidir]** qué se le dice a la Residencia y si hay alguna alternativa (la única mencionada en el repositorio, un modelo en servidor propio dentro de la UE, está sin estudiar: dossier:134-138). [Opinión] No activar y no ofrecer el módulo mientras tanto.

**d) Evaluación de impacto (EIPD).** El proyecto la fija como condición previa: «evaluación de impacto antes de producción» (ADR 0009:68-69; ADR 0011:60). Si es obligatoria por ley y con qué alcance (un centro, un piloto, todos) es la primera pregunta que el dossier deja al DPO real (dossier:123). El RGPD exige la evaluación cuando hay tratamiento a gran escala de categorías especiales de datos (art. 35.3.b; BOE 2026-11-09), la hace el **responsable** antes de empezar (35.1) y se revisa cuando cambia el riesgo (35.11). **La EIPD es de la Residencia**; ResidApp aporta un anexo con datos, flujos, terceros, riesgos y medidas `[ANEXO EIPD]` **[Decidir]** quién y cuándo lo redacta. [Opinión] Cada activación es un «cambio de riesgo» y la Residencia debe poder decir «mi EIPD ya cubre este módulo».

**e) Base jurídica.** La fija la Residencia (RGPD art. 6 y 9). ResidApp no la decide ni la afirma. Cuestión abierta para el DPO real: cuál es la condición que levanta la prohibición de tratar datos de salud y quién la documenta (dossier:124). `[BASE JURÍDICA DEL CENTRO]` **[Decidir]**.

**f) Información a las personas afectadas (responsabilidad de la Residencia).** Personal: su voz se envía a un tercero, se usa para transcribir y no para identificarle (dossier:116). Residentes y representantes: se emplea esa tecnología. Familias: según proceda. ResidApp facilita texto-base `[TEXTO DE INFORMACIÓN]`. La voz como dato biométrico: duda abierta del dossier (:129), sin identificación de personas en el uso previsto.

**g) Quién puede usarlo dentro del centro.** Hoy, una auxiliar con ese residente asignado (el perfil queda fijado a «AUXILIAR» en la auditoría; dossier:94; `SqlVoiceDictationAudit.cs:19`). Activar el módulo **no concede permisos a ningún perfil**: eso sigue siendo de la Administración del centro (ADR 0015:76-77).

**h) Quién puede activar y apagar.**
- Activar: **Plataforma** (ResidApp), solo con este anexo firmado, la ficha aceptada y las condiciones de la lista siguiente.
- Apagar: Plataforma, y **[Decidir]** el propio centro (cláusula 6.2).

**i) Qué se conserva al apagarlo.**
- En ResidApp: **nada propio del módulo**, porque no guarda audio ni transcripción. Lo ya confirmado en los registros es historia clínica normal y sigue como siempre; las filas de auditoría del dictado se conservan con la auditoría (plazo sin decidir: `antes-de-datos-reales.md:129-131`). No hay «solo lectura del dictado» que mantener: simplemente desaparece el botón (ADR 0015:85; `modulos-opcionales-lectura-dpo.md:138`).
- En Mistral: con retención cero, nada. Sin ella (caso que esta ficha excluye como condición bloqueante), **apagar el módulo de un centro no borra nada en Mistral** hasta que termine el contrato de ResidApp con Mistral (lectura previa, 5.2).
- Desde el apagado, la aplicación **no debe enviar nada** a Mistral para ese centro, aunque la clave global siga puesta. Debería haber una prueba automática («módulo apagado, clave puesta, ninguna llamada de red»): **[Propuesta]**, aún no existe.

**j) Qué debe comprobar la Residencia antes de la activación (casillas).** Las firma la persona autorizada; ResidApp no puede verificarlas y solo puede negarse a activar sin ellas y dejar constancia.

- [ ] Este anexo está firmado por quien tiene poder y la fila de `DICTADO_VOZ` está rellenada (instrucción documentada).
- [ ] Existe contrato de encargado vigente con ResidApp y su anexo de subencargados incluye a Mistral como **condicional al módulo**.
- [ ] La Residencia autoriza por escrito a ResidApp a usar a Mistral para este fin (cláusula 4.1.c).
- [ ] La EIPD de la Residencia cubre el dictado (o su DPO ha dejado por escrito que no hace falta y por qué).
- [ ] La Residencia ha identificado su base jurídica (arts. 6 y 9) y ha actualizado su registro de actividades (art. 30).
- [ ] El personal sabe que su voz se envía a un tercero y para qué; residentes, representantes y familias han recibido la información que corresponda.
- [ ] El personal conoce el aviso «No digas nombres de personas.» y hay un procedimiento si se dice un nombre.
- [ ] Se ha revisado el uso de móviles personales con micrófono (ADR 0009:56-58) frente a la normativa laboral y de derechos digitales **[A verificar]** artículos de la LOPDGDD aplicables.
- [ ] Hay persona de contacto y suplente para avisos de incidentes (cláusula 7.4).
- [ ] La Administración sabe que activar el módulo no concede permisos y ha decidido qué perfiles lo usan.

**Condiciones que ResidApp debe cumplir antes de activar (bloqueantes)**, independientes de la Residencia:

- [ ] Retención cero aprobada por escrito por Mistral para los dos servicios.
- [ ] Confirmación escrita de que el DPA de Mistral cubre datos de salud.
- [ ] Aclaración escrita sobre entrenamiento, entrenamiento desactivado y comprobado; modelos fuera de *Labs* y *Preview*.
- [ ] Región UE confirmada por escrito para los dos servicios; lista actual de subencargados de Mistral.
- [ ] Versión vigente del DPA identificada.
- [ ] Interruptor por centro construido, o procedimiento equivalente aceptado por el abogado (cláusula 5.5).
- [ ] Política de privacidad corregida: sus frases «solo si el centro lo activa» solo son ciertas cuando exista el interruptor por centro (`src/ResidApp.Web/Views/Home/Privacy.cshtml:112-113, 156-157`; ADR 0015:96-97).
- Comprueba el sistema: la clave de Mistral existe (interruptor maestro), el entorno no es de pruebas y el módulo está vigente para el centro.

**k) Medidas ya existentes** (dossier, apartado 5; cada una con prueba automática salvo la indicada):

- Sin clave del proveedor el dictado está apagado (pantalla y servidor).
- Solo proveedor autorizado: otro impide arrancar la aplicación.
- Solo una auxiliar con ese residente asignado puede usarlo.
- El uso se anota **antes** de enviar el audio; si falla la anotación, no sale nada.
- La anotación no lleva audio ni texto; el registro técnico no lleva contenido.
- El modelo no puede devolver nada fuera del catálogo del formulario y lo devuelto se valida en el servidor.
- Límite de usos por minuto y por cuenta; protección contra envíos falsos desde otra web; los fallos del proveedor no se explican con detalle.
- Audios vacíos, demasiado grandes o que no son audio se rechazan antes de enviar nada.
- Aviso «No digas nombres de personas.» siempre visible.
- Revisión una a una de cada opción sugerida antes de continuar: **comprobada a mano en un navegador real, sin prueba automática** (dossier:105).

**l) Riesgos conocidos y no resueltos** (dossier, apartado 6): nombres dichos en voz alta; el modelo se equivoca y la persona puede fiarse sin mirar; precisión medida solo con voz sintética limpia, con resultados esperados aún no validados por la responsable clínica; la voz de la auxiliar es dato personal suyo; tercero nuevo en el tratamiento; posible procesado fuera de la UE sin verificar.

### 4.2 Plantilla de ficha de privacidad para un módulo nuevo

Copiar para cada módulo nuevo. Una ficha incompleta no se firma ni se activa.

```
FICHA DE PRIVACIDAD · [CÓDIGO] · [NOMBRE] · versión [N] · fecha [FECHA]
Estado: [borrador / revisada por (DPO real o abogado) el (fecha)]

a) Finalidad (qué hace y para qué, en lenguaje llano)
b) Datos tratados: de quién, cuáles, categorías especiales sí/no, qué sale de ResidApp, qué guarda ResidApp
c) Quién accede dentro del centro (perfiles) y si se concede algún permiso (por defecto, ninguno)
d) Terceros / subencargados: nombre, función, región, contrato, retención, ¿condicional al módulo?
e) Base jurídica propuesta (la confirma la Residencia) y compatibilidad con la finalidad original (art. 6.4) si es una finalidad nueva
f) Evaluación de impacto: ¿exigida? ¿qué aporta ResidApp? ¿qué debe hacer la Residencia?
g) Información a personas afectadas: residentes y representantes / familias / personal
h) Conservación: qué datos nuevos se generan, plazo y quién decide
i) Salida de los datos: consulta y copia. Qué fichero, qué datos, quién lo pide, cómo se audita.
   REGLA: un módulo no guarda datos reales de residentes hasta que esta salida existe (ADR 0015, punto 3)
j) Qué pasa al apagar / vencer: solo lectura (qué se ve, qué se exporta) o no aplica (módulo sin datos propios)
k) Dependencias e incompatibilidades con otros módulos
l) Quién puede activar y quién puede apagar
m) Casillas que comprueba la Residencia antes de activar
n) Condiciones que comprueba el sistema y condiciones bloqueantes de ResidApp
o) Medidas de seguridad y sus pruebas
p) Riesgos conocidos no resueltos
q) Cambios respecto a la versión anterior (y si son sustanciales, cláusula 7)
```

### 4.3 Nota de bloqueo: Dashboard de Dirección (no contratable todavía)

Resumen de `modulos-opcionales-lectura-dpo.md`, apartado 6, y ADR 0015:138-140. **No existe como módulo; no se contrata con este anexo.** Candidato de Orlando: resúmenes gráficos agregados del estado de una parte de la app, para Dirección. Antes de ofrecerlo se necesita su propia ficha y, como mínimo:

- Definir qué es base (los indicadores y la calidad de proceso de Dirección Clínica, que ya existen) y qué añade el módulo (ADR 0015:45-46, **[Decidir]**).
- Cifra mínima de agregación con **supresión complementaria** para que no se pueda deducir una celda restando; la cifra N la fijan el DPO y CJ. No se fija aquí.
- Sin salto del gráfico a la persona; sin desglose por trabajador; combinaciones de filtros limitadas; los mínimos se aplican en el servidor también al exportar; sin copias derivadas.
- Es una **finalidad nueva** para datos que ya existen: compatibilidad con la finalidad original (art. 6.4) y base (posiblemente art. 9.2.h, texto completo comprobado el 2026-11-12: gestión de sistemas y servicios de asistencia sanitaria y social «sobre la base del Derecho de la Unión o de los Estados miembros o en virtud de un contrato con un profesional sanitario», y con el art. 9.3: lo trata un profesional sujeto a secreto profesional u otra persona sujeta también a secreto; el personal no sanitario con acceso debe estar sujeto a esa obligación).
- Hecho incómodo ya existente: los indicadores de Dirección que son base cuentan por unidad y mes sin ninguna regla de cifras pequeñas visible en el código leído (`src/ResidApp.Application/Ports/ISupervisionDirectory.cs:162-199`, lectura parcial); conviene revisarlo aparte, con o sin módulo.
- El dashboard de Plataforma **no** es este módulo.

### 4.4 Nota de bloqueo: Medicamentos (BLOQUEADO)

Resumen de `modulos-opcionales-lectura-dpo.md`, apartado 7, y ADR 0015:141-143. **No se contrata, no se ofrece y no se activa en ningún centro con datos reales mientras no se cumpla todo lo siguiente:**

1. CJ reabre el alcance: hoy la administración de medicación está fuera de alcance (`docs/producto/alcance.md:45`), la política de privacidad dice que ResidApp no la guarda (`Privacy.cshtml:54`) y el ADR 0011 lo repite (líneas 75-77 y 66, «solo CJ puede reabrirlo»).
2. Se define qué es: lista de lo que toma, registro de cada administración, o apoyo a la decisión (alertas, dosis). Este último puede ser **producto sanitario** (Reglamento (UE) 2017/745) **[A verificar]** con texto oficial y un especialista; la primera versión no debería aconsejar ni alertar [Opinión].
3. Se decide la **única fuente de verdad** frente a las indicaciones de Medicina y los sistemas oficiales.
4. Evaluación de impacto específica, que incluya seguridad del paciente (no solo privacidad).
5. Acceso por necesidad y auditado; **no visible en el Portal Familiar** (la lista de fármacos revela diagnósticos).
6. **Incompatible con el dictado** hasta un ADR nuevo con revisión clínica.
7. Cualquier tercero (bases de fármacos, receta electrónica) es un destinatario o subencargado nuevo y requiere ADR.
8. Un listado desactualizado en solo lectura es un riesgo clínico: al apagarlo, aviso a Dirección clínica y copia imprimible del estado vigente.

---

## 5. Instrucciones del centro: contratación, activación y cambios

**5.1 La contratación es una instrucción.** Al firmar la fila de un módulo, la Residencia **instruye por escrito** a ResidApp para que active ese módulo para el centro o centros indicados, en la versión de la ficha indicada y durante la vigencia indicada, y para que trate los datos como describe la ficha (RGPD art. 28.3.a). Fuera de esa instrucción, ResidApp no activa ningún módulo con datos reales: ni por prueba gratuita, ni por motivos comerciales, ni «para que lo pruebe». Las demostraciones se hacen siempre en el entorno de desarrollo con datos ficticios (`entorno-produccion-lectura-dpo.md:222`).

**5.2 Quién ejecuta.** Plataforma ejecuta la activación. Activar un módulo **no concede permisos a ningún perfil**.

**5.3 Quién puede pedir cambios.** Solo la **persona autorizada** del apartado 2 (o su suplente), por `[CANAL ACORDADO]` y dejando constancia escrita. `[¿Debe el cambio ir firmado por quien firmó el anexo?]` **[Decidir]**. ResidApp no atiende peticiones de cambio de módulos de otras personas del centro (Administración, Dirección, personal asistencial) salvo que la persona autorizada lo haya delegado por escrito.

**5.4 Qué registra ResidApp en cada activación y desactivación** [Propuesta, ADR 0015 punto 2 y 4, y `modulos-opcionales-lectura-dpo.md`, apartado 1]. Todo **sin datos de residentes**:

- centro y módulo;
- estado anterior y nuevo, y fecha y hora;
- cuenta y perfil que ejecuta (Plataforma, o el centro si se decide que puede apagar);
- **referencia documental** a esta instrucción (anexo, versión y fecha); no texto libre ni nombres de personas del centro;
- versión de la ficha aceptada y las casillas de condiciones comprobadas;
- en desactivación, un **motivo de una lista cerrada** (petición del centro, fin de contrato, impago, incidente de seguridad, retirada del proveedor, otro con aviso);
- auditoría con acciones del tipo `CENTER_MODULE_ENABLE` y `CENTER_MODULE_DISABLE`.

El documento firmado queda en el archivo contractual de ResidApp, no en la base de datos.

**5.5 Condición previa técnica.** Hasta que exista el interruptor por centro (tabla `modulos_centro`, ADR 0015), el dictado se enciende para todos los centros a la vez con la clave global. Mientras tanto: `[PROCEDIMIENTO ALTERNATIVO]` **[Decidir]**. [Opinión] Ninguna activación real antes de que exista el interruptor y de que la política de privacidad diga la verdad.

**5.6 Facturación y censo.** Si el precio depende del número de residentes, contar residentes con los datos del centro es un uso de esos datos para un fin de ResidApp (facturar). `[El censo lo declara la Residencia / ResidApp cuenta residentes activos sin identificar a nadie]` **[Decidir]**; [Opinión] preferible que lo declare el centro o que el contrato lo prevea expresamente como instrucción. **Contadores de uso**: solo por centro y por módulo, **nunca por persona**; si existen, la ficha del módulo debe decirlo (lectura previa, apartado 3 y 9.2). Importes y periodicidad: fuera de este anexo (apartado 1).

---

## 6. Alta, baja y vencimiento

### 6.1 Alta

El módulo pasa a «activo» cuando concurren: anexo firmado con la fila del módulo, ficha aceptada en su versión, casillas de la ficha comprobadas, condiciones bloqueantes de ResidApp cumplidas y fecha de inicio llegada. Un módulo que nunca se ha activado no tiene fila en la aplicación (ADR 0015:60-61). No se activa un módulo si falta uno del que depende.

### 6.2 Baja a petición y parada de emergencia

> **[Decidir] Tensión que debe resolver el abogado o el DPO real.** La baja **comercial** de un módulo sigue el contrato (aviso, fin del periodo pagado). Pero la Residencia, como responsable, **no puede quedar sin poder dejar de tratar datos cuando quiera**: puede dar una instrucción nueva en cualquier momento y la encargada debe seguirla (art. 28.3.a). Hay que conciliar ambas cosas (ADR 0015, puerta 2).

**Propuesta del agente [Opinión], para discusión:** separar dos ejes que no se mezclan.

| Eje | Regla propuesta |
|---|---|
| **Apagado técnico (dejar de tratar)** | La Residencia puede ordenar apagar el módulo **cuando quiera, sin esperar al fin del periodo pagado**. Preferible que **la Administración principal del centro pueda apagarlo ella misma** desde la aplicación (el módulo pasa a solo lectura y queda registrado), sin depender de que ResidApp conteste. Plataforma **solo puede encender** (con este anexo) y **no puede apagar por motivos comerciales** salvo vencimiento (6.3) o impago (6.4). Si el centro no puede apagarlo solo, Plataforma lo hace en `[PLAZO]` **[Decidir]** desde la petición de la persona autorizada. |
| **Efecto económico** | Lo que pase con el precio del periodo ya comprometido (se debe, se reembolsa, se prorratea) lo fija el contrato `[CLÁUSULA ECONÓMICA]` **[Decidir]**. Apagar no extingue por sí solo la obligación de pagar el periodo pagado, pero tampoco puede condicionarse a pagar o a esperar. |

**Parada de emergencia por Plataforma.** Plataforma puede apagar un módulo para **todos los centros** a la vez (por ejemplo, retirando la clave del proveedor) ante un incidente del proveedor, la retirada de la retención cero, una orden de autoridad u otra causa de seguridad, y avisa a los centros afectados sin dilación indebida (relacionado con RGPD art. 33.2; leído el 2026-11-08). `[¿Efecto económico de una parada de emergencia?]` **[Decidir]**.

**Alternativas que debe valorar el abogado:** (B) que el apagado a petición solo surta efecto al fin del periodo pagado, lo que limita una instrucción del responsable y puede no ser válido en cuanto impida cumplir el RGPD **[A verificar]**; (C) apagado a petición ejecutado solo por Plataforma en un plazo fijado, con parada de emergencia. [Opinión] La opción propuesta (apagado técnico libre, efecto económico por contrato) es la que menos riesgo jurídico plantea; el coste es que el control pasa al centro y puede reducir ingresos previstos.

### 6.3 Vencimiento

Pasada la fecha «Vigente hasta» de un módulo, **pasa solo a solo lectura** sin intervención de nadie (ADR 0015:73-75). Hasta esa fecha sigue activo. `[Aviso previo al centro de que el módulo vence: plazo y canal]` **[Decidir]**. La renovación es un acto expreso: nueva fila o nueva versión del anexo (ver pregunta 6, apartado 9). Si el módulo se reactiva tras un tiempo o cambió la ficha, vuelven a pedirse las casillas (lectura previa, 9.10).

### 6.4 Impago

Apagar por impago **nunca** impide consultar ni exportar lo ya registrado: el módulo pasa a solo lectura, no a «sin acceso». El centro tiene que poder cumplir sus 15 años de conservación y atender derechos de las personas con independencia de si pagó (`modulos-opcionales-lectura-dpo.md`, apartado 3; ADR 0015:110-111). `[Aviso previo y plazos de impago]` **[Decidir]**.

### 6.5 Qué significa «solo lectura»

En lenguaje llano: **lo ya registrado se puede ver; no se puede crear ni editar nada nuevo del módulo**. Se ocultan los botones de crear y editar y, además, el servidor rechaza esos intentos si alguien entra por la dirección directa (ADR 0015:67-69).

- **Consultar:** siempre posible, con los permisos de perfil de siempre.
- **Exportar:** posible **solo si la exportación cubre esos datos**. **Hoy la exportación solo cubre estructura y censo de residentes activos** (ADR 0010:38, 72). Este anexo **no promete** exportar datos asistenciales ni de módulos: esa salida **no existe**. Regla [Propuesta]: un módulo no guarda datos reales de residentes hasta que tenga definida su salida (consulta y copia); `DICTADO_VOZ` no tiene datos propios que exportar.
- **Módulos sin datos propios** (el dictado; un dashboard que solo calcula): no hay estado de «solo lectura»; el módulo simplemente deja de ofrecerse.
- La pantalla de un módulo clínico en solo lectura debe decir «Módulo desactivado el [fecha]: no se actualiza» (lectura previa, 5.3).

### 6.6 Fin del contrato y devolución o supresión de datos

RGPD art. 28.3.g: a elección del responsable, la encargada **suprimirá o devolverá** todos los datos personales al terminar el servicio y suprimirá las copias, salvo que una norma obligue a conservarlos (BOE 2026-11-09).

- **La Residencia elige** `[devolución / supresión / ambas]` **[Decidir]** por escrito antes del fin, en `[PLAZO]`.
- **Lo que hoy se puede devolver:** el fichero de estructura y censo de residentes activos, con los límites de la exportación actual (5 MB, 2.000 filas, 60 columnas por fichero; ADR 0010:92). **No existe devolución completa de la historia clínica ni de datos de módulos.** Es una carencia a cerrar **antes de firmar con datos reales** (`expurgo-y-supresion.md:40, 186`): formato, quién la pide, cómo se audita y con qué garantías (los límites actuales no sirven para una devolución completa). **[Decidir]**.
- **Plataforma no puede exportar datos de un centro** (no ve residentes: `antes-de-datos-reales.md:136-138`). Quien quiera hacer la devolución de fin de contrato no debe «arreglarlo» dando a Plataforma acceso clínico.
- **Conservación de 15 años y supresión.** ResidApp propone, por defecto, conservar la documentación clínica de cada residente **15 años desde su baja**. No es una instrucción legal: es un plazo prudente basado en la Llei 21/2000, art. 12, en la redacción de la Llei 16/2010 (leído en el BOE el 2026-11-03: `antes-de-datos-reales.md:115-117`; `expurgo-y-supresion.md:34-36, 49`). La **custodia es de la Residencia**; ResidApp guarda una copia como encargada. El centro, como responsable, puede fijar otro plazo dentro de lo que la ley permita y ResidApp lo aplicará por instrucción escrita; **ResidApp no borra nada clínico por su cuenta**. **[A verificar]** efecto de la Llei 2/2024 sobre la Llei 21/2000 y si una residencia cuenta como «centro sanitario» a efectos de ese artículo (`antes-de-datos-reales.md:131-132`). Apagar o vencer un módulo **no borra nada clínico**.
- **Bloqueo frente a borrado.** Al suprimir, el art. 32 de la LOPDGDD parece exigir **bloquear** los datos y no borrarlos materialmente (`antes-de-datos-reales.md:121-123`; se leyó en resumen, **[A verificar]** texto literal). Debe decidirlo la persona experta antes de suprimir datos reales.
- **Datos de los registros de activación** (apartado 8): plazo propio, el de la prueba contractual y de responsabilidad, no los 15 años clínicos **[Decidir]** (ADR 0015, puerta 5).

### 6.7 Subencargados al apagar o terminar

- Apagar `DICTADO_VOZ` en un centro: ResidApp **deja de enviar** audio o texto a Mistral para ese centro desde el momento del apagado. Con retención cero, no queda nada en Mistral; sin ella (excluida como condición del módulo), no se borra nada en Mistral hasta el fin del contrato de ResidApp con Mistral.
- Fin del contrato con la Residencia: ResidApp comunica a sus subencargados `[Azure, correo, Mistral si procede]` la supresión o devolución que proceda, y acredita a petición que lo ha hecho `[MEDIO DE PRUEBA]` **[Decidir]**.
- Si Mistral deja de prestar el servicio o cambia sus condiciones, véase 7.3.

---

## 7. Cambios de la ficha de un módulo

**7.1 Cambios no sustanciales** (aclaraciones, redacción, mejoras de seguridad sin cambio de datos ni terceros): ResidApp avisa a la persona autorizada `[PLAZO Y CANAL]` **[Decidir]** y aplica la nueva versión de la ficha.

**7.2 Cambios sustanciales** requieren **nueva aceptación expresa** de la Residencia. Se consideran sustanciales, como mínimo (lista abierta; la cierra el abogado o DPO real):
- un subencargado nuevo o un cambio de subencargado;
- un cambio de región o de garantías de transferencia;
- una categoría nueva de dato o de personas afectadas;
- una finalidad nueva;
- un cambio de retención, o la pérdida de la retención cero;
- un cambio de lo que se envía fuera de ResidApp.

Efecto: el módulo pasa a «pendiente de nueva aceptación» y se avisa al centro (lectura previa, apartado 2). **[Decidir]** qué ocurre hasta que la acepte: (a) sigue funcionando en la versión anterior de la ficha mientras sea posible; (b) pasa a solo lectura a los `[PLAZO]` si no la acepta. [Opinión] Si el cambio es forzado por el proveedor (por ejemplo, Mistral cambia sus subencargados) y el centro no lo acepta, la salida es el apagado a solo lectura en `[PLAZO]`, sin penalización.

**7.3 Subencargados de un subencargado.** Mistral puede añadir subencargados por autorización general y da diez días para objetar (dossier:66-67; resumen de su página pública, **[A verificar]** en el texto original). ResidApp se compromete a trasladar a la Residencia cada aviso de Mistral en `[PLAZO MENOR QUE EL DE MISTRAL]` **[Decidir]** para que el centro pueda objetar a tiempo. Si la Residencia objeta y Mistral no cede, el remedio es apagar el módulo en ese centro (6.2). Art. 28.2 y 28.4 del RGPD.

**7.4 Incidentes.** Si un incidente afecta a un módulo o a su tercero, ResidApp avisa a la persona de contacto del centro afectado «sin dilación indebida» (RGPD art. 33.2) y en un plazo propio más corto que el legal `[PLAZO MÁXIMO DE AVISO]` **[Decidir]** (`entorno-produccion-lectura-dpo.md:69`). La tabla de módulos por centro permite saber **a qué centros afecta**. La notificación a la autoridad la hace la Residencia como responsable `[AUTORIDAD]`.

---

## 8. Responsabilidades y auditoría

**8.1 La Residencia (responsable)** decide la finalidad y la base jurídica; hace o actualiza su EIPD y su registro de actividades; informa a residentes, representantes, familias y personal; concede los permisos de perfil; entrega el censo si se pacta así; y atiende los derechos de las personas con ayuda de ResidApp (art. 28.3.e, letra comprobada el 2026-11-12: el encargado asiste al responsable «para que este pueda cumplir con su obligación de responder a las solicitudes» de derechos del capítulo III).

**8.2 ResidApp (encargada)** ejecuta solo lo instruido; aplica las medidas de seguridad del art. 32; mantiene su registro de tratamientos por cuenta del responsable (art. 30.2: cada módulo es una línea nueva con sus subencargados); avisa de incidentes y de cambios; y permite la auditoría (art. 28.3.h). El alcance de la auditoría del centro `[FORMA, PLAZO, COSTE]` **[Decidir]**.

**8.3 Qué ve el centro.** La Residencia ve **sus módulos, su estado, desde cuándo, la versión de ficha aceptada y su historial** de activaciones y desactivaciones, en una pantalla de solo lectura para la Administración y en su auditoría (ADR 0015:80, incremento 5). En el historial, el actor figura como «Plataforma de ResidApp», sin nombre; el nombre de la cuenta se da a quien tenga derecho a pedirlo (el DPO, una inspección) (lectura previa, apartado 3). **[Propuesta]**: aviso a la Administración principal cuando cambia un módulo.

**8.4 Qué ve Plataforma.** Plataforma ve **qué módulos tiene cada centro** (información comercial y contractual), pero **no lee la auditoría de otros centros** (`antes-de-datos-reales.md:137-138`) ni datos de residentes ni contenido clínico. El evento de activación se **inserta** en la auditoría del centro afectado, y el centro lo lee en la suya. Los contadores de uso, si existen, no son por persona (5.6).

**8.5 Limitación conocida.** Con una sola persona operando Plataforma no hay «cuatro ojos» (otra persona que confirme) (`entorno-produccion-lectura-dpo.md:16`). La compensación es que **el centro ve cada activación** y puede protestar.

**8.6 Conservación del histórico** de activaciones, aceptaciones de ficha y casillas: plazo de prueba contractual y de responsabilidad `[PLAZO]` **[Decidir]**; el histórico es de solo añadir (no se modifica ni se borra salvo transcurrido el plazo). La fila contiene datos personales de quien la ejecuta (cuenta de Plataforma), por lo que figura en el registro de actividades de ResidApp con acceso restringido.

**8.7 Reparto de responsabilidad económica y de seguro** (quién responde de una infracción por culpa del subencargado, topes, seguro de responsabilidad): `[CLÁUSULA]` **[Decidir]**; es materia exclusivamente del abogado.

---

## 9. Cuestiones para el abogado o DPO real, por orden de importancia

Mi opinión va marcada como tal.

1. **¿Vale este anexo como instrucción documentada (art. 28.3.a)?** Incluye: si basta con que la fila esté firmada, si debe firmarlo quien tiene poder, qué más debe decir (módulo, versión de la ficha, vigencia, centro), y si una instrucción por anexo es compatible con una renovación tácita del contrato marco. [Opinión] Sí puede valer si es específica, escrita, firmada por quien tiene poder y vinculada a cada módulo y versión; lo debe confirmar un jurista.
2. **Baja comercial frente al derecho a dejar de tratar (6.2).** ¿Es válida una cláusula que sujete el apagado técnico al fin del periodo pagado? ¿Se puede apagar de inmediato manteniendo el pago? [Opinión] Separar apagado técnico (libre) y efecto económico (contrato).
3. **¿Puede el centro apagar él solo y Plataforma solo encender, más apagado de emergencia global?** Qué plazo debe tener Plataforma si el centro no puede hacerlo solo. [Opinión] Sí, es la medida que protege; el riesgo es solo comercial.
4. **Retención cero con Mistral como condición bloqueante (4.1.c).** ¿Hay alternativa aceptable si Mistral la rechaza? ¿Basta el DPA estándar de Mistral con confirmación escrita sobre datos de salud? [Opinión] No activar sin retención cero.
5. **Cadena de autorizaciones y responsabilidad (Residencia → ResidApp → Mistral).** ¿Es suficiente la autorización específica en la ficha, o se necesita en el contrato de encargado? ¿Quién firma y a nombre de quién (no hay sociedad titular)? ¿Cómo se concilia con la autorización general de subencargados que ofrece Mistral?
6. **Renovación y vigencia.** ¿La vigencia por módulo (`vigente_hasta`) es compatible con el modelo de suscripción? [Opinión] La instrucción de tratamiento no debería renovarse sin una confirmación visible del centro, aunque la renovación comercial sea tácita; si no, el módulo pasaría a solo lectura contra la voluntad real de ambas partes.
7. **Devolución y supresión al terminar (6.6).** Formato y garantías; qué se promete mientras la exportación completa no exista; si es aceptable firmar con datos reales sin ella. [Opinión] No firmar con datos reales sin esa salida definida.
8. **Bloqueo frente a borrado (art. 32 LOPDGDD)** y su efecto en la supresión al terminar. Y los 15 años: si una residencia cuenta como «centro sanitario» a efectos de la Llei 21/2000, art. 12, y efecto de la Llei 2/2024.
9. **Facturación por censo (5.6).** Cómo cuenta ResidApp residentes sin usar los datos para un fin propio (art. 28.10). Contadores de uso por centro y módulo, nunca por persona.
10. **Base jurídica del dictado** (arts. 6 y 9), EIPD obligatoria o no, y compatibilidad con la normativa laboral y de derechos digitales por el uso de voz del personal y de móviles personales. ¿La voz es dato biométrico aquí?
11. **Plazos que faltan:** aviso de cambios, aviso de incidente, respuesta a petición de apagado, aviso de vencimiento, conservación del histórico de activaciones y de la auditoría (esta última sin decidir: `antes-de-datos-reales.md:129-131`).
12. **Cambios sustanciales de ficha (7.2):** lista cerrada y qué ocurre mientras el centro no acepta.
13. **Autoridad de control (AEPD o APDCAT)** para el centro piloto y qué cambia en el contrato; régimen del Esquema Nacional de Seguridad (RD 311/2022, art. 2.1: «todo el sector público»; si el centro es privado concertado, solo aplica si presta el servicio a la Administración para el ejercicio de sus competencias, art. 2.3, y lo decide el abogado; ResidApp, como proveedora de un centro público, queda alcanzada por ese art. 2.3).
14. **Corregir ya la política de privacidad** (frases «solo si el centro lo activa») o construir primero el interruptor (ADR 0015, puerta 8).
15. **Cuestiones de redacción contractual** que quedan fuera de mi lectura: límites de responsabilidad, seguro, jurisdicción, confidencialidad, propiedad intelectual, nivel de servicio.

---

## A verificar o decidir (lista consolidada)

**Para escribir o rellenar (campos en blanco):** partes, poder, contrato marco, contrato de encargado, persona autorizada y suplente, DPO de la Residencia, versión y vigencia del anexo, fila del módulo (fechas, referencia de oferta, centros), versión de la ficha, canal de comunicación, plazos de aviso, plazo de apagado, plazo de devolución, plazo de conservación del histórico, forma de la auditoría, cláusula económica.

**Para decidir:** persona física o sociedad titular; si el centro apaga solo; efecto económico de apagar, de la parada de emergencia y del impago; renovación expresa o tácita; cómo se cuenta el censo; qué hacer si Mistral rechaza la retención cero; procedimiento alternativo hasta que exista el interruptor por centro; reparto entre Indicadores y Calidad (base) y el futuro Dashboard (módulo); regla de apagado en cascada.

**Para verificar en fuente oficial:**
- Texto literal del resto del art. 28.2 RGPD (aviso de cambios y oposición), de las letras b a f del art. 28.3, y de los arts. 25.2, 15.3, 9.2.h completo, 5.1.d.
- Art. 32 LOPDGDD (bloqueo) en texto literal.
- Llei 21/2000, arts. 12 y 13, y efecto de la Llei 2/2024 (Portal Jurídic de Catalunya).
- Si una residencia es «centro sanitario» a efectos del art. 12.
- AEPD o APDCAT para el centro piloto (Llei 32/2010).
- Si aplica el Esquema Nacional de Seguridad (RD 311/2022).
- LOPDGDD, derechos digitales en el ámbito laboral aplicables al dictado y a los móviles personales.
- Reglamento (UE) 2017/745 y guía MDCG 2019-11 sobre software, para Medicamentos.
- Texto original y versión vigente del DPA de Mistral, su retención cero, entrenamiento, región, subencargados y modelos «latest».
- Orientaciones de la AEPD y del grupo del art. 29 sobre anonimización y agregación, para el Dashboard.

**Hechos verificados para este borrador:** lectura de los ADR 0009, 0010, 0011 y 0015, del dossier del dictado, de `modulos-opcionales-lectura-dpo.md`, de `antes-de-datos-reales.md` (líneas 100-144), de `expurgo-y-supresion.md` (líneas 30-49) y de `SqlVoiceDictationAudit.cs`; RGPD art. 28 (apartados 1 a 4 y 10) en el BOE, `https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807`, el 2026-11-09 (en resumen automático para los apartados 1, 2 y 4; las letras a, g y h del apartado 3 en la lectura previa del agente el mismo día). Los arts. 30.2, 33.2, 35 y 36 proceden de las lecturas previas citadas en `modulos-opcionales-lectura-dpo.md`.

**Qué debe validar un humano:** todo. En particular, que el anexo vale como instrucción documentada, la solución a la tensión de la cláusula 6.2, la cadena de subencargado con Mistral, el tratamiento de los datos al terminar el contrato y los plazos. **Yo no soy DPO designado ni abogado.** Si se pide, puedo preparar la versión en catalán una vez fijado el texto en castellano.
