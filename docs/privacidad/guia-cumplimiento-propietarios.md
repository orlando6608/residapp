# Guía de cumplimiento de protección de datos para los propietarios de ResidApp

**BORRADOR del agente `dpo-residapp`, a petición de Orlando. Fecha: 2026-11-13.** Escrita para Orlando y CJ (las dos personas propietarias de la plataforma). Especificación de partida: [`../tareas/media-prioridad/pendiente-guia-cumplimiento-proteccion-datos.md`](../tareas/media-prioridad/pendiente-guia-cumplimiento-proteccion-datos.md).

> **Esta guía NO sustituye a un delegado de protección de datos (DPO) ni a un abogado.** Yo soy una segunda lectura experta hecha por una herramienta: ordeno lo que hay, cito la ley y señalo qué falta. No firmo nada, no doy un dictamen y no soy una de las figuras que exige el RGPD. **Una persona experta (abogado especializado o DPO) debe leerla, corregirla y darla por buena antes de que os fiéis de ella.** Mientras tanto, es una hoja de ruta, no una garantía de cumplimiento.
>
> **Hoy no existe la sociedad titular de ResidApp ni un DPO o abogado real** (ver el apartado 9). Eso condiciona quién firma los contratos y quién es el contacto de privacidad.

Las citas legales salen del texto literal de `normativa-local/` (índice comprobado el 2026-11-12: RGPD consolidado a 2016-05-04; LOPDGDD, BOE actualizado el 2026-10-08; LSSI, BOE 2025-12-18; ENS, BOE 2026-10-30; Llei 32/2010, BOE 2026-07-13; Llei 21/2000, BOE 2026-02-16; Ley 41/2002, BOE 2025-12-18). Son textos consolidados: sirven para consultar, no son la versión oficial. Lo que no está en esa base se dice en el apartado final.

---

## 0. Cómo leer esta guía

### Palabras que se repiten

- **Datos de salud:** información sobre la salud de una persona. El RGPD los llama «categorías especiales de datos» (art. 9) y exige más cuidado con ellos.
- **Responsable del tratamiento:** quien decide para qué y cómo se usan los datos. En ResidApp, **la residencia**.
- **Encargado del tratamiento:** quien trata los datos por cuenta del responsable y solo según sus instrucciones. En ResidApp, **ResidApp**.
- **Subencargado:** un proveedor al que el encargado recurre para parte del trabajo (Microsoft Azure, el envío de correo, Mistral para el dictado).
- **Contrato de encargado (art. 28 del RGPD):** el contrato obligatorio entre responsable y encargado que fija qué datos se tratan, para qué y con qué garantías. Sin él, el encargo de tratamiento no está en regla.
- **Instrucción documentada:** orden por escrito del responsable al encargado («activa tal módulo», «borra tales datos»).
- **Base jurídica:** el motivo legal que permite tratar unos datos (art. 6 del RGPD y, para salud, art. 9).
- **Cláusula informativa:** el texto que explica a cada persona qué se hace con sus datos (art. 13). Se da por capas: un resumen breve y el texto completo aparte.
- **Registro de actividades de tratamiento (art. 30):** la lista escrita de qué datos se tratan, para qué, quién los ve y cuánto se guardan.
- **EIPD (evaluación de impacto):** un análisis escrito de riesgos que hay que hacer **antes** de empezar cuando el tratamiento es de alto riesgo, por ejemplo datos de salud a gran escala (art. 35).
- **DPO (delegado de protección de datos):** persona o empresa independiente que vigila que se cumpla la ley y es el contacto ante la autoridad (arts. 37 a 39).
- **Autoridad de control:** el organismo que vigila y sanciona. La AEPD en general; la APDCAT en el ámbito catalán que fija la Llei 32/2010.
- **ENS (Esquema Nacional de Seguridad):** reglamento de seguridad informática que obliga al sector público y a quien le presta servicios (RD 311/2022).
- **Brecha de seguridad:** un incidente que expone, pierde o altera datos personales (un acceso indebido, un correo enviado a quien no debía, una copia perdida).
- **Bloqueo:** al suprimir un dato, reservarlo sin usarlo, solo a disposición de jueces y autoridades hasta que prescriban las responsabilidades (LOPDGDD art. 32).
- **SaaS:** programa que se ofrece como servicio por internet y se paga por uso, sin que el cliente lo instale.

### Las casillas

En el apartado 7 cada paso lleva una casilla:

- `[x] Hecho`: lo he podido comprobar en el repositorio (se dice dónde).
- `[ ] Pendiente`: hay que hacerlo y sabemos cómo.
- `[ ] Decide una persona`: lo debe resolver una persona experta (abogado o DPO) o vosotros por escrito; yo solo puedo opinar.

### Quién es quién en esta guía

| Quién | Qué hace |
|---|---|
| **Orlando y CJ** | Deciden el producto y el negocio, hacen las preguntas y cumplen lo que está en vuestra mano (ResidApp como empresa). |
| **Persona experta** (abogado o DPO) | Valida, decide lo jurídico y firma lo que hay que firmar. Apartado 4. |
| **La residencia** | Es la responsable: decide, informa a las personas, atiende los derechos y notifica brechas. ResidApp la ayuda. |
| **El agente `dpo-residapp`** | Redacta borradores y cita la ley. No decide. |

---

## 1. Lo esencial en un minuto

1. **ResidApp no es la dueña de los datos de los residentes: lo es cada residencia.** ResidApp los trata por encargo suyo y solo según sus instrucciones. Si ResidApp los usara para sus propios fines, pasaría a ser responsable de ese uso (LOPDGDD art. 33.2).
2. **Sin contrato de encargado firmado no se carga ningún dato real**, ni en un piloto. El art. 28.3 del RGPD dice que el tratamiento por el encargado «se regirá por un contrato u otro acto jurídico». Hoy **no hay plantilla de ese contrato**.
3. **Lo más lento es encontrar a la persona experta** (semanas). **Decidido por Orlando el 2026-11-13: la búsqueda empieza ya**, sin esperar a que haya un centro interesado.
4. **Lo que hay hoy son borradores** (24 archivos en esta carpeta) hechos por mí. Ninguno lo ha validado una persona. Esta guía los ordena y dice cuáles faltan.
5. **Tres documentos no tienen todavía ni borrador:** el contrato de servicio (términos del SaaS), el aviso legal y el protocolo de brechas. Más un registro de actividades y una EIPD completos (hoy solo hay piezas sueltas). Apartado 3.
6. **Dos datos que cambian mucho el camino:** **qué tipo de centro es el piloto** (público, concertado o privado; **aún no se sabe**, Orlando 2026-11-13) y **la sociedad titular** (**decidido: no se firma con ningún centro real hasta que exista**). Apartados 8 y 9.

---

## 2. Hasta dónde llega la responsabilidad de ResidApp (y cómo encaja con la del centro)

### 2.1 El reparto

Está decidido en el ADR 0007 (`../decisiones-arquitectura/0007-proveedor-identidad.md`, cabecera): **la residencia es la responsable y ResidApp la encargada**. Lo confirma el borrador de la política de privacidad ([`politica-privacidad-portal.md`](politica-privacidad-portal.md), apartado 1).

| Tema | Lo decide y responde... | ResidApp... |
|---|---|---|
| Para qué se tratan los datos de residentes, familias y personal del centro | **La residencia** | Ejecuta. No decide fines ni medios. |
| Base jurídica (arts. 6 y 9) de cada tratamiento | La residencia | Propone texto; no la afirma. |
| Informar a residentes, representantes, familias y personal | La residencia | Facilita plantillas ([`politica-privacidad-portal.md`](politica-privacidad-portal.md)). |
| Atender derechos (acceso, rectificación, supresión…) | La residencia | La ayuda técnicamente (RGPD art. 28.3.e). |
| Hacer la EIPD y el registro de actividades del centro | La residencia | Aporta datos, flujos, terceros y medidas (art. 28.3.f). |
| Avisar a la autoridad y a los afectados de una brecha | La residencia | **Avisa a la residencia sin dilación indebida** (art. 33.2). |
| Seguridad de la plataforma (cifrado, accesos, copias, registros) | **ResidApp** (y también el centro en lo suyo: art. 32.1 obliga a los dos) | Es lo central de su encargo. |
| Qué módulos se activan en un centro | La residencia (con instrucción escrita) | Plataforma ejecuta (ver [`anexo-modulos-contrato-borrador.md`](anexo-modulos-contrato-borrador.md), cláusula 5.1). |
| Quién puede ver qué dentro del centro | La residencia (Administración del centro) | Da las herramientas. Plataforma no ve residentes ni contenido clínico. |
| Subencargados (Azure, correo, Mistral) | La residencia **autoriza**; ResidApp contrata y **sigue siendo plenamente responsable** ante ella | Art. 28.2 y 28.4. |
| Qué pasa con los datos al terminar el contrato | La residencia elige devolver o suprimir | Lo hace (art. 28.3.g; LOPDGDD art. 33.3). |

### 2.2 Lo que ResidApp debe cumplir siempre como encargada

Del art. 28.3 del RGPD: tratar solo con instrucciones documentadas; garantizar que quien opere ResidApp se ha comprometido por escrito a la **confidencialidad** (letra b); aplicar las medidas del art. 32; no usar otro encargado sin autorización previa por escrito; ayudar con los derechos y con los arts. 32 a 36; suprimir o devolver al terminar; permitir **auditorías** del centro; y avisar al responsable «inmediatamente» si cree que una instrucción infringe el RGPD. Además debe **llevar su propio registro** de lo que hace por cuenta de cada centro (art. 30.2).

Líneas rojas prácticas (de ahí salen reglas ya escritas en el proyecto): no usar datos de residentes para mejorar el producto, para estadísticas propias o para demostraciones; las demos van siempre en desarrollo con datos inventados (`anexo-modulos-contrato-borrador.md`, cláusula 5.1). Si ResidApp cobra por número de residentes, contar residentes es un uso para un fin propio: tiene que preverlo el contrato (cláusula 5.6 del anexo).

### 2.3 Lo que ResidApp trata por su cuenta (aquí **ResidApp es la responsable**)

Esto no depende de ninguna residencia. Hace falta información propia, entrada propia en el registro de actividades y plazo propio:

| Datos | Dónde está explicado | Estado |
|---|---|---|
| Cuentas del personal de Plataforma y sus aparatos autorizados (ADR 0016) | [`dispositivos-autorizados-lectura-dpo.md`](dispositivos-autorizados-lectura-dpo.md), apartado 4.2 | Sin cláusula informativa ni línea de registro |
| Contactos comerciales que lleguen por las landings (`residapp.es`, `residapp.cat`) | [`landings-y-passkeys-por-subdominio.md`](landings-y-passkeys-por-subdominio.md), apartado 2; [`antes-de-datos-reales.md`](antes-de-datos-reales.md), apartado 6 | Landings sin publicar; textos legales sin escribir |
| Correos que recibáis en vuestros buzones de contacto o soporte | Mismos documentos | Sin procedimiento; un solo punto de entrada para derechos (landings, apartado 2) |
| Datos de quienes trabajan en el proyecto en GitHub (cuenta, correo, actividad) | [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md), apartado 6 | Regla de «ningún dato personal en el repositorio»; falta registro y acuerdo con GitHub |
| Registros de seguridad de la plataforma (IP de operadores, alertas) | [`registros-tecnicos-lectura-dpo.md`](registros-tecnicos-lectura-dpo.md) | Plazos sin decidir para la IP |

### 2.4 Dos avisos sobre el reparto

- **Quién firma, hoy, por ResidApp:** nadie puede, porque la sociedad titular no existe (`anexo-modulos-contrato-borrador.md`, apartado 0.2). Es la decisión que condiciona todos los contratos (apartado 9).
- **El riesgo de hacer de responsable sin querer:** el art. 33.2 de la LOPDGDD dice que será responsable quien «en su propio nombre y sin que conste que actúa por cuenta de otro, establezca relaciones con los afectados». Por eso los correos a familias, la política de privacidad y las pantallas deben decir siempre que ResidApp actúa **por cuenta del centro**.

---

## 3. Los documentos que hacen falta

Cada fila dice para qué sirve (en llano), quién lo redacta, quién lo valida y **dónde está el borrador y en qué estado**. «Valida: persona experta» significa abogado o DPO real. Todos los borradores indican al final «A verificar o decidir» con sus puntos abiertos.

| # | Documento | Para qué sirve | Lo redacta | Lo valida / firma | Borrador y estado |
|---|---|---|---|---|---|
| 1 | **Política de privacidad** de la aplicación | Cuenta a residentes, familias y personal qué datos se tratan, quién los ve, cuánto se guardan y cómo ejercer derechos (art. 13) | `dpo-residapp` + vosotros (datos) | Persona experta; **la adopta cada residencia** como suya | [`politica-privacidad-portal.md`](politica-privacidad-portal.md). **Borrador publicado** en la app con el aviso «Texto en revisión» (`../../src/ResidApp.Web/Views/Home/Privacy.cshtml`, líneas 11-12) y cada dato que falta marcado «Por completar». Falta: datos del prestador y del centro, plazos, regiones, y **la versión para profesionales** (`antes-de-datos-reales.md`, apartado 9: «aún no existe») |
| 2 | **Declaración de cookies y almacenamiento en el dispositivo** (LSSI art. 22) | Dice qué se guarda en el móvil o el navegador y por qué | `dpo-residapp` | Persona experta | Va **dentro** de la política, apartado 10 (inventario en la parte B3). Para las **landings** no hay: basta decir «no usamos» si es así (`landings-y-passkeys-por-subdominio.md`, apartado 2). Puntos abiertos en el apartado 8 |
| 3 | **Cláusulas informativas** a residentes y representantes, familias y personal | Texto corto que cada centro entrega (al ingreso, al dar acceso, al contratar) | `dpo-residapp` como plantilla | Persona experta; **las entrega la residencia** | Hay **piezas, no el conjunto**: [`historial-tras-traslado-03-informacion-residentes-y-personal.md`](historial-tras-traslado-03-informacion-residentes-y-personal.md), [`aviso-cita-por-correo.md`](aviso-cita-por-correo.md), [`portal-familiar-dos-idiomas.md`](portal-familiar-dos-idiomas.md), [`direccion-confirmada-familia.md`](direccion-confirmada-familia.md), y el «texto A» para el personal en [`dispositivos-autorizados-lectura-dpo.md`](dispositivos-autorizados-lectura-dpo.md), apartado 2.4. **Falta la hoja de primera capa por población** (residente/representante, familia, profesional). **Decidido (Orlando, 2026-11-13): ResidApp ofrecerá a los centros una plantilla de información al ingreso;** la redacta la persona experta, cada centro la adapta y la entrega, y el centro sigue siendo quien informa |
| 4 | **Registro de actividades de tratamiento** (art. 30) | Lista escrita de tratamientos. **Hacen falta dos:** el del centro (art. 30.1) y el de ResidApp como encargada (art. 30.2) y como responsable de lo suyo | `dpo-residapp`; lo mantiene cada parte | Persona experta | **Solo entradas sueltas:** [`historial-tras-traslado-01-registro-actividades.md`](historial-tras-traslado-01-registro-actividades.md) y apartados «registro de actividades» de varias lecturas (p. ej. dispositivos 7.4). **No hay registro consolidado.** La excepción de menos de 250 personas **no vale** si se tratan datos de salud (art. 30.5, texto literal) |
| 5 | **EIPD** (evaluación de impacto) | Análisis de riesgos previo. La hace **el responsable (la residencia)**; ResidApp aporta un anexo (art. 35.1 y 28.3.f). El art. 35.3.b la exige en particular para «tratamiento a gran escala de las categorías especiales de datos» | La residencia con el anexo de ResidApp | Persona experta; consulta a la autoridad si queda riesgo alto (art. 36) | **Solo piezas:** [`historial-tras-traslado-02-nota-eipd.md`](historial-tras-traslado-02-nota-eipd.md), el dossier del dictado (`../seguridad/dictado-por-voz-dossier-dpo.md`), partes en [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md) y [`dispositivos-autorizados-lectura-dpo.md`](dispositivos-autorizados-lectura-dpo.md) (apartado 7.2). **No hay EIPD general.** Guía de apoyo en la base: `apdcat-guia-aipd-2025` |
| 6 | **Contrato de encargado (art. 28) con cada residencia**, con anexo de subencargados | El contrato obligatorio entre el centro y ResidApp. Lista de subencargados y ubicaciones | **Abogado** (plantilla) | Abogado; lo firman residencia y ResidApp | **No existe plantilla.** Solo listas de lo que debe contener ([`anexo-modulos-contrato-borrador.md`](anexo-modulos-contrato-borrador.md), apartado 0.2; [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md), apartado 11, «Puerta 2», punto 6) |
| 7 | **Anexo de módulos** del contrato | Qué módulos opcionales compra el centro; es la instrucción documentada para activarlos; qué pasa al apagarlos | `dpo-residapp` (hecho) | Abogado | [`anexo-modulos-contrato-borrador.md`](anexo-modulos-contrato-borrador.md) (2026-11-09). Depende del ADR 0015, que es **propuesta** y no está construido. Lectura previa: [`modulos-opcionales-lectura-dpo.md`](modulos-opcionales-lectura-dpo.md) |
| 8 | **Contrato de servicio (términos y condiciones del SaaS)** | El contrato **mercantil** con cada centro: precio, duración, soporte, nivel de servicio, responsabilidades, terminación. **Distinto** del contrato de encargado (aunque pueden ir en el mismo paquete) | **Abogado** (Derecho Digital) con vuestras decisiones comerciales | Abogado; firman las partes | **Sin borrador propio.** Hoy solo hay menciones de que falta. El anexo de módulos remite a él como «contrato marco» (`[CONTRATO MARCO]`) |
| 9 | **Aviso legal** (LSSI art. 10) | Identifica al titular del sitio. El art. 10.1 pide, «de forma permanente, fácil, directa y gratuita»: «nombre o denominación social», domicilio, «dirección de correo electrónico» y «los datos de su inscripción en el Registro Mercantil» si está inscrito | Vosotros (datos) + abogado (texto) | Abogado | **Sin borrador propio.** La política tiene los campos del prestador como «Por completar»; las landings piden «titular, NIF y domicilio» (`../tareas/futuro/pendientes-landing.md`, línea 27). **Depende de que se decida persona física o sociedad** |
| 10 | **Protocolo de brechas** (art. 33) | Pasos escritos para detectar, avisar al centro y documentar un incidente | Vosotros con `dpo-residapp` | Persona experta | **Sin borrador.** Solo hay piezas y una lista de requisitos ([`registros-tecnicos-lectura-dpo.md`](registros-tecnicos-lectura-dpo.md), punto 6; [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md), apartado 1). Apartado 6 de esta guía |
| 11 | **Procedimiento de derechos** | Qué hacer cuando alguien pide acceder, rectificar, borrar… | Vosotros con `dpo-residapp` | Persona experta | **Sin documento único.** Piezas: [`expurgo-y-supresion.md`](expurgo-y-supresion.md) (cuentas del personal en el apartado 4; anonimizar familiares en el 3) y el procedimiento del correo a familias en [`direccion-confirmada-familia.md`](direccion-confirmada-familia.md). Apartado 6 de esta guía |
| 12 | **Plazos de conservación y política de copias de seguridad** | Cuánto se guarda cada cosa y cómo se restaura | Vosotros con la persona experta | Persona experta | [`expurgo-y-supresion.md`](expurgo-y-supresion.md) (15 años desde la baja; expurgo) y [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md), apartado 3 (copias; redacción propuesta). **Plazos de IP, sesiones, auditoría y registros siguen sin decidir** |
| 13 | **Contratos con vuestros proveedores** (Microsoft, correo, Mistral, GitHub) | ResidApp debe imponerles las mismas obligaciones que firma con el centro (art. 28.4) | Abogado revisa los términos de cada proveedor | Abogado | **No hechos.** Mistral con problemas abiertos (`../seguridad/dictado-por-voz-dossier-dpo.md`, y [`anexo-modulos-contrato-borrador.md`](anexo-modulos-contrato-borrador.md), ficha 4.1.c) |
| 14 | **Documentación del ENS** (política de seguridad, declaración de aplicabilidad), **solo si aplica** | Evidencia de seguridad exigida a quien presta servicios al sector público | Responsable de seguridad | Persona experta decide si aplica | **Sin borrador.** Primero hay que saber si aplica (apartado 8) |

Los demás borradores de esta carpeta (avisos de cita por correo, landings, dashboards, medicación y alergias, registros técnicos, etc.) son lecturas por tema que alimentan estos documentos. Están listados con una línea cada uno en el apéndice.

---

## 4. Cuándo hace falta una persona experta, para qué y qué pedirle

### 4.1 Respuesta corta

**Sí, y antes de cargar el primer dato real.** El propio proyecto ya lo tiene escrito: la persona experta «aún no existe» y se deja «para antes del primer centro real», pero encontrarla «puede llevar semanas» (`antes-de-datos-reales.md`, apartado 7, último punto). Un abogado no es sustituible por mí en tres cosas: firmar o adoptar contratos, decidir los puntos abiertos del apartado 8, y responder de lo que aconseja.

### 4.2 A quién buscar

Un **especialista en Derecho Digital y en el sector sanitario** (RGPD, programas como servicio y asistencia sanitaria y social), no un abogado generalista. Si es posible, con experiencia en Cataluña (documentación clínica catalana, APDCAT) y en el ENS si algún cliente es público.

**Decidido por Orlando el 2026-11-13: no se fija de antemano si es un solo despacho o dos figuras separadas;** la primera pregunta a la persona experta es qué estructura recomienda para ResidApp.

Son **dos figuras distintas**, aunque pueden ser un mismo despacho:

- **Abogado:** valida contratos, adopta plantillas y decide los puntos jurídicos abiertos.
- **DPO:** es una figura legal con independencia. El responsable «garantizará que el delegado de protección de datos no reciba ninguna instrucción» y «no será destituido ni sancionado por desempeñar sus funciones» (RGPD art. 38.3), y sus otras funciones no pueden dar lugar a «conflicto de intereses» (art. 38.6). Puede ser de la plantilla o «en el marco de un contrato de servicios» (art. 37.6). Por eso **ni Orlando ni CJ deberían ser el DPO de ResidApp**: son quienes deciden el producto. (Pregunta 4 de la lista siguiente.)

### 4.3 Para qué fin concreto

1. **Validar** los borradores de esta carpeta y la guía (apartado 3).
2. **Preparar las plantillas** del contrato de encargado (y su anexo de subencargados) y del contrato de servicio **antes de las primeras ventas**.
3. **Decidir los puntos abiertos** del apartado 8.
4. **Ser el contacto ante la autoridad**, si se designa DPO (apartado 5).
5. Revisar la **EIPD** y el **protocolo de brechas** antes de producción.

### 4.4 Cuándo encargarlo

| Momento | Qué se le pide |
|---|---|
| **Ya** (búsqueda) | Encontrar y presupuestar. Cuanto antes, porque lleva semanas. Para el primer contacto: [`dossier-persona-experta.md`](dossier-persona-experta.md) (documento para enviar al despacho, con marcadores de contacto por rellenar) |
| **Antes de datos reales** (aunque sea piloto) | Validar los borradores del apartado 7, momento 1; resolver las preguntas 1 a 8; contrato de encargado para el piloto |
| **Antes de las primeras ventas** | Plantillas del contrato de encargado y del contrato de servicio; aviso legal; textos de las landings |
| **Antes del primer centro real** | Preguntas restantes; EIPD y registro; protocolo de brechas; DPO si procede |

### 4.5 Preguntas preparadas para la persona experta (por orden de importancia)

Cada una indica dónde está el contexto. Las 1 a 8 bloquean datos reales; las siguientes, el primer centro real o las ventas.

1. **¿Una residencia cuenta como «centro sanitario»?** De la respuesta dependen: el plazo de 15 años de la Llei 21/2000, art. 12 («La responsabilidad de custodiar la historia clínica recae en la dirección de los centros sanitarios»; la Ley 41/2002, art. 3, define centro sanitario como «el conjunto organizado de profesionales, instalaciones y medios técnicos que realiza actividades y presta servicios para cuidar la salud»), si el centro **debe tener DPO** (LOPDGDD art. 34.1.l: «los centros sanitarios legalmente obligados al mantenimiento de las historias clínicas») y quién es la «dirección médica» que decide el expurgo. Contexto: `expurgo-y-supresion.md`, apartado 1.
2. **¿Qué autoridad corresponde (AEPD o APDCAT) según el tipo de centro piloto?** El art. 3 de la Llei 32/2010 incluye a las instituciones y entes públicos, a las entidades privadas con capital, ingresos o dirección mayoritariamente públicos, y a «las demás entidades de derecho privado que prestan servicios públicos mediante cualquier forma de gestión directa o indirecta, si se trata de ficheros y tratamientos vinculados a la prestación de dichos servicios». **Necesito de vosotros el tipo de centro** (apartado 8). ¿Y cuál es la autoridad de ResidApp como encargada y como responsable de lo suyo?
3. **¿Aplica el ENS?** El RD 311/2022, art. 2.1, lo aplica a «todo el sector público»; el art. 2.3, a las entidades privadas que presten servicios o provean soluciones al sector público «para el ejercicio por estas de sus competencias y potestades administrativas». ¿Un centro concertado entra por el art. 2.3? ¿Y ResidApp, como proveedora?
4. **¿Necesita ResidApp un DPO propio?** El art. 37.1.c del RGPD obliga también **al encargado** cuando sus «actividades principales» consistan en tratamiento «a gran escala» de categorías especiales; si no, puede ser voluntario (LOPDGDD art. 34.2). Y, si se designa, ¿quién puede serlo sin conflicto de intereses?
5. **¿Persona física o sociedad como titular?** Con qué figura se firma el piloto mientras la sociedad no existe, con qué responsabilidad personal y qué hay que cambiar después (cesión de contratos). Esto condiciona el aviso legal y todos los contratos.
6. **Bloqueo frente a borrado al suprimir datos de un familiar.** El art. 32 de la LOPDGDD obliga a «bloquear los datos» cuando se rectifican o suprimen, y su apartado 4 admite un «copiado seguro» con evidencia si el sistema no permite el bloqueo. Hoy anonimizar **borra sin copia bloqueada**. **No se anonimiza a una familia real hasta tener la respuesta por escrito** (`antes-de-datos-reales.md`, apartado 8; `expurgo-y-supresion.md`, apartado 3).
7. **¿Vale el anexo de módulos como «instrucción documentada»?** Y la tensión entre baja comercial y derecho del centro a dejar de tratar (`anexo-modulos-contrato-borrador.md`, apartado 9, preguntas 1 a 3).
8. **Base jurídica y EIPD.** Base (arts. 6 y 9) de los tratamientos del centro y de los accesorios (familias, personal, aparatos, registros); si la EIPD es una por centro, una general o una por módulo; si procede consulta previa a la autoridad (art. 36).
9. **Contrato de encargado:** cláusulas, plazo máximo propio para avisar de una brecha, autorización de subencargados (Azure, ACS, Mistral), auditoría del centro, y **qué se promete al terminar**: hoy **no existe devolución completa** de la historia clínica (`anexo-modulos-contrato-borrador.md`, cláusula 6.6). ¿Es aceptable firmar con datos reales sin ella?
10. **Dictado por voz (Mistral):** retención cero, DPA que cubra datos de salud, entrenamiento, región, si la voz es dato biométrico aquí, EIPD (`../seguridad/dictado-por-voz-dossier-dpo.md`).
11. **Plazos que faltan:** IP (`eventos_acceso`), `sesiones`, auditoría, registros de seguridad, copias, `envios_correo` (12 meses como tope, a confirmar), motivo de las lecturas del historial tras un traslado.
12. **Aviso de cita por correo a familias:** base, si aplica la LSSI, información a la familia, el significado de «sin confirmar» en un aviso no resuelto (`aviso-cita-por-correo.md`, `direccion-confirmada-familia.md`).
13. **Cookies y almacenamiento:** exención del art. 22.2 LSSI para la clave del aparato (ADR 0016) y tope de caducidad de la cookie de idioma; si hace falta banner.
14. **Aspectos laborales** (consultar a un laboralista): aparatos autorizados ligados a la cuenta del trabajador, móviles personales con micrófono para el dictado, información a la plantilla y a su representación (`dispositivos-autorizados-lectura-dpo.md`, apartado 2).
15. **Quién ejerce los derechos de un residente con capacidad disminuida** (representante, apoyos de la Ley 8/2021, familiares) y cómo se acredita; acceso a la historia clínica por representación.
16. **Idioma:** si el Codi de consum exige la información en catalán en una web B2B y si los derechos lingüísticos obligan al centro a atender en catalán (`portal-familiar-dos-idiomas.md`).
17. **Módulos futuros:** si «Medicamentos» o un dashboard con ayuda a la decisión serían producto sanitario (Reglamento 2017/745, regla 11) y qué exige el Reglamento de IA al dictado (`modulos-opcionales-lectura-dpo.md`, apartados 6 y 7).
18. **Repositorio:** cuándo privatizar y qué hacer con lo ya expuesto mientras fue público (`entorno-produccion-lectura-dpo.md`, apartado 6).
19. **Responsabilidad económica, seguro, límites, jurisdicción, confidencialidad, propiedad intelectual, nivel de servicio** y **obligaciones al publicar precios** en una web B2B (`anexo-modulos-contrato-borrador.md`, apartado 9.15; `antes-de-datos-reales.md`, apartado 6).

---

## 5. Los nombramientos y figuras que exige la ley

| Figura | ¿Obligatoria? | A quién se comunica y cuándo | Quién debería ser |
|---|---|---|---|
| **DPO del centro** | **Sí, si el centro es** autoridad u organismo público (RGPD art. 37.1.a), si su actividad principal es tratamiento a gran escala de categorías especiales (37.1.c), **o** si es un «centro sanitario legalmente obligado al mantenimiento de las historias clínicas» (LOPDGDD art. 34.1.l). Si no, es voluntaria (art. 34.2) | «En el plazo de diez días» a la AEPD o, en su caso, a la autoridad autonómica, tanto la designación como el cese (LOPDGDD art. 34.3). Además hay que **publicar** sus datos de contacto y comunicarlos a la autoridad (RGPD art. 37.7) | **La residencia.** ResidApp le facilita información; no lo elige por ella. Que el centro sea «sanitario» es la pregunta 1 |
| **DPO de ResidApp** | Posible: el art. 37.1.c alcanza al encargado. Si no se exige, voluntario | Igual: 10 días a la AEPD o autoridad que corresponda | Persona externa con independencia (apartado 4.2). **Decide la persona experta** |
| **Persona de contacto de privacidad de ResidApp** (no es un DPO) | No como figura legal, pero el art. 33.3.b del RGPD pide en una brecha «el nombre y los datos de contacto del delegado de protección de datos **o de otro punto de contacto**» | Se publica en el aviso legal y en la política | **Decidido (Orlando, 2026-11-13): Orlando, con CJ de suplente.** Falta que CJ lo acepte por escrito (un mensaje basta) y que conste en el aviso legal |
| **Responsable de seguridad (ENS)** | **Solo si aplica el ENS.** La política de seguridad debe identificar a los responsables de la información, del servicio, de la seguridad y del sistema (RD 311/2022, arts. 11 y 13) | Lo designa la organización en su política. Si ResidApp es proveedora de un centro público, el art. 13.5 pide un «POC (Punto o Persona de Contacto) para la seguridad» | **El responsable de seguridad ha de ser distinto del responsable del sistema y sin dependencia jerárquica** (art. 13.3), salvo ausencia justificada de recursos con medidas compensatorias. Hoy una sola persona opera ResidApp: no se cumple sin esas medidas. El ADR 0007 ya dice que Orlando «hace de responsable de Seguridad», pero **no es un nombramiento ENS** |
| **Personas de contacto y suplente por centro para avisos de brecha** | Contractual, no legal | Se fija en el contrato de encargado | Una persona y su suplente por centro, y una de ResidApp con suplente |
| **Compromiso de confidencialidad de quien opere ResidApp** | Sí (art. 28.3.b) | Por escrito, antes de dar acceso | Orlando, CJ y cualquier persona futura con acceso; hoy **no consta** |
| **Persona de referencia y suplente para el aviso de correo a familias** | Condición del proyecto | Procedimiento escrito del centro | Administración del centro (`direccion-confirmada-familia.md`, apartado 6) |
| **Persona autorizada y suplente para pedir cambios de módulos** | Contractual | En el anexo de módulos | La residencia (`anexo-modulos-contrato-borrador.md`, apartado 2) |
| **Dirección médica que decide el expurgo** | Depende de la pregunta 1 | — | La residencia |

El centro cumple **lo suyo por su lado**: ResidApp no nombra al DPO del centro ni dirige su organización (la app no regula la organización del centro; solo exige lo técnico que el centro active).

---

## 6. Atender derechos, reclamaciones y brechas

### 6.1 Peticiones de acceso, rectificación, supresión y demás derechos

**Quién responde: el centro.** El responsable es quien debe contestar (RGPD art. 12). La LOPDGDD añade que el encargado «podrá tramitar, por cuenta del responsable, las solicitudes» **solo «si así se estableciere en el contrato»** (art. 12.3) y que la prueba de haber respondido «recaerá sobre el responsable» (art. 12.4). Los derechos se pueden ejercer «directamente o por medio de representante legal o voluntario» (art. 12.1).

**Plazos** (RGPD art. 12.3 y 12.4, texto literal): respuesta «sin dilación indebida y, en cualquier caso, en el plazo de un mes» desde la recepción; prorrogable **dos meses más** si es complejo, avisando al interesado dentro del primer mes y diciendo los motivos. Si no se da curso, hay que explicar las razones y la posibilidad de reclamar ante la autoridad «a más tardar» en un mes. Es gratuito (art. 12.5), salvo solicitudes manifiestamente infundadas o excesivas.

| Paso | Quién | Cómo |
|---|---|---|
| 1. Llega la petición | Al centro o a ResidApp | Si llega a ResidApp por un dato de un centro, **se reenvía al centro el mismo día laborable** (plazo propio a fijar en el contrato; **Decide una persona**) y no se contesta de fondo salvo que el contrato lo prevea |
| 2. Identificar a quien pide | El centro | Si hay dudas razonables sobre la identidad puede pedir información adicional (RGPD art. 12.6) |
| 3. Reunir los datos | El centro, con ayuda técnica de ResidApp | Plataforma **no ve residentes ni contenido clínico**: la ayuda pasa por la Administración del centro. **Hoy no existe exportación completa de la historia clínica** (`anexo-modulos-contrato-borrador.md`, cláusula 6.6): carencia a cerrar antes de datos reales |
| 4. Decidir y responder | El centro | Dentro del mes (o 3 meses con aviso) |
| 5. Dejar constancia | El centro (y ResidApp si interviene) | Fecha, quién pidió, qué se hizo |

**Particularidades:**
- **Acceso a la historia clínica:** tiene régimen propio. La LOPDGDD (art. 12.5) remite a las leyes especiales. La Llei 21/2000, art. 13, dice que «corresponde a los centros sanitarios regular el procedimiento», que el acceso no puede ir en perjuicio de la confidencialidad de terceros ni de las «anotaciones subjetivas» de los profesionales, y que puede ejercerse «por representación, siempre que esté debidamente acreditada».
- **Supresión:** no es automática. La historia clínica tiene plazo mínimo de conservación (15 años desde el alta de cada proceso para varios documentos: Llei 21/2000, art. 12.4) y hay que **bloquear, no solo borrar** (LOPDGDD art. 32). ResidApp **no borra nada clínico por su cuenta** (`expurgo-y-supresion.md`, apartado 1.3).
- **Familiares y personal:** ver `expurgo-y-supresion.md`, apartados 3 y 4. **No se anonimiza a un familiar real hasta decisión escrita** (pregunta 6).
- **Derechos sobre datos propios de ResidApp** (apartado 2.3): responde ResidApp, con un buzón de privacidad único para `residapp.es` y `residapp.cat` (**pendiente**; `landings-y-passkeys-por-subdominio.md`, apartado 2).

### 6.2 Si alguien reclama ante la autoridad

- El interesado puede reclamar ante una autoridad de control «en particular en el Estado miembro en el que tenga su residencia habitual, lugar de trabajo o lugar de la supuesta infracción» (RGPD art. 77.1).
- **Si hay DPO**, el afectado puede dirigirse antes a él, y el DPO debe comunicar la decisión «en el plazo máximo de dos meses»; la autoridad puede incluso remitirle la reclamación para que responda «en el plazo de un mes» (LOPDGDD art. 37.1 y 37.2).
- **Quién contesta:** la reclamación va contra el responsable (el centro). **ResidApp coopera** con la autoridad si se lo pide (RGPD art. 31) y pone a disposición del centro «toda la información necesaria para demostrar el cumplimiento» (art. 28.3.h). No responde en nombre del centro sin instrucción.
- **Qué hacer en cuanto llegue algo** de una autoridad a ResidApp: avisar a la persona experta y al centro afectado el mismo día, no contestar de fondo sin consultar, y guardar el registro de actividades, los contratos y las pruebas de lo que se hizo.

### 6.3 Si hay una brecha de seguridad

Hoy **no hay protocolo escrito**. Esto es lo que dice la ley y lo que debería recoger:

| Quién | Qué | Fuente |
|---|---|---|
| **ResidApp (encargada)** | Notifica al centro «sin dilación indebida» las brechas de las que tenga conocimiento | RGPD art. 33.2. La AEPD aclara que el RGPD «no especifica un plazo concreto» para el encargado, pero la información debe enviarse sin dilación (guía de brechas de la AEPD, apartado sobre responsabilidades) |
| **El centro (responsable)** | Notifica a la autoridad de control competente «sin dilación indebida y, de ser posible, a más tardar 72 horas después de que haya tenido constancia», salvo que sea improbable que haya riesgo para las personas; si pasa de 72 horas, indica los motivos | RGPD art. 33.1 |
| **El centro** | Si hay probable «alto riesgo», lo comunica también a las personas afectadas, en lenguaje claro | RGPD art. 34.1 y 34.2 |
| **El centro** | **Documenta toda brecha**, aunque no la notifique | RGPD art. 33.5 |

Contenido mínimo de la notificación: naturaleza, categorías y número aproximado de afectados, contacto del DPO o punto de contacto, consecuencias y medidas (art. 33.3).

**Cómo se reparte el trabajo** (la guía de la AEPD admite que el responsable delegue la gestión y la notificación en el encargado «documentándose dicha delegación» en el contrato, y que la delegación de funciones «no implica delegación de responsabilidad»). **Ante qué autoridad:** la guía de la AEPD indica que, en el sector público de Cataluña, se notifica a la APDCAT; para un centro privado, la AEPD es la regla general (pregunta 2).

**Plazo propio de ResidApp.** Como el reloj de 72 horas corre para el centro, ResidApp debe poder avisarle **mucho antes**. El contrato debe fijar un plazo máximo propio: **propuesta decidida por Orlando el 2026-11-13, 24 horas** desde que ResidApp tenga constancia (la redacción final la revisa la persona experta; `entorno-produccion-lectura-dpo.md`, apartado 1) y la persona de contacto y suplente por centro. **A verificar por la persona experta:** desde cuándo se entiende que el centro «ha tenido constancia» cuando el aviso llega de ResidApp (el art. 33.1 no lo define).

**Brechas pequeñas también:** un aviso por correo que llega a un desconocido es una brecha que hay que **documentar** (`direccion-confirmada-familia.md`, apartado 6).

**Qué hay hoy y qué falta.** Hay (en el código): registro técnico de fallos y accesos denegados sin datos personales (`../../src/ResidApp.Infrastructure/Registro/TechnicalFailureLog.cs`) y, según el relevo, alertas por correo con titular y suplente en Azure dev (no comprobable desde el repositorio). **Falta:** el protocolo escrito, la lista de contactos por centro, el plazo de aviso, la plantilla del art. 33.3 y un simulacro (`registros-tecnicos-lectura-dpo.md`, punto 6).

---

## 7. Guía rectora de pasos

Orden pensado para dos personas que no son juristas. Cuando una casilla dice «persona experta», no la cerréis sin su visto bueno por escrito.

### 7.1 Momento 1. Antes de usar datos reales (en un piloto también)

**Idea de fondo:** hoy la base de Azure es de desarrollo y solo tiene datos inventados (`antes-de-datos-reales.md`, cabecera). Esta lista es lo que debe estar cerrado antes de que entre **un solo dato real**. La lista detallada por función es [`antes-de-datos-reales.md`](antes-de-datos-reales.md); esta guía la ordena, no la sustituye.

**Lo ya hecho (verificable en el repositorio):**

- [x] **Hecho** — Borradores de protección de datos redactados: 24 archivos en esta carpeta (apéndice).
- [x] **Hecho** — Base normativa local con el texto literal de las leyes y guías (`normativa-local/INDICE.md`, `catalogo.json`, `tools/normativa.ps1`).
- [x] **Hecho** — Borrador de la política de privacidad publicado en la app con el aviso «Texto en revisión» y la advertencia de que solo se usan datos inventados (`../../src/ResidApp.Web/Views/Home/Privacy.cshtml`, líneas 11-12).
- [x] **Hecho** — Seguridad técnica construida y probada con datos de prueba: seguridad por filas en la auditoría y la estructura de centro (`../../database/scripts/0088_rls_eventos_auditoria.sql`, `0090_rls_estructura_centro.sql`), permisos mínimos del usuario de la app (`0091_permisos_aplicacion.sql`) y registro técnico de fallos (`TechnicalFailureLog.cs`).
- [x] **Hecho** — El correo y el dictado por voz **salen apagados por defecto** (el correo con `Correo:Activo`, comentario en `../../src/ResidApp.Web/Program.cs`, línea 273; el dictado sin clave de Mistral queda desactivado, ADR 0011; `appsettings.json` no trae esa clave).

**Lo pendiente:**

- [ ] **Pendiente** — **Empezar la búsqueda de la persona experta** (abogado y DPO, en uno o en dos despachos según lo que recomiende; apartado 4.2). Quién: Orlando y CJ. **Decidido: empezar ya**, porque lleva semanas.
- [ ] **Decide una persona** — **Tipo de centro del piloto** (público, concertado o privado): **aún no se sabe** (Orlando, 2026-11-13). Cuando se sepa, lo valora la persona experta (preguntas 1 a 3).
- [x] **Hecho (decisión)** — **Titular del piloto:** no se firma con ningún centro real hasta que exista la sociedad (Orlando, 2026-11-13). El piloto sigue con datos inventados. La pregunta 5 sigue para el abogado: cómo se constituye y qué firma cada parte.
- [ ] **Pendiente** — **Validación de los borradores por la persona experta** (`antes-de-datos-reales.md`, apartado 1): registro de actividades y EIPD, información a residentes y personal, procedimiento de revisión de lecturas del historial, plazos de conservación.
- [ ] **Decide una persona** — **Decisiones escritas** sobre: si la residencia es centro sanitario; AEPD o APDCAT; ENS; **bloqueo frente a borrado de familiares** (hasta entonces no se anonimiza a ninguna familia real: script 0087).
- [ ] **Pendiente** — **Contrato de encargado firmado con el centro piloto**, aunque sea un piloto (RGPD art. 28.3). Plantilla: preparada por el abogado.
- [ ] **Pendiente** — **Rellenar los datos que faltan en la política** (identidad y contacto del prestador, datos del centro y su DPO, autoridad, plazos, regiones de Azure y Mistral) y decidir si cada centro la adopta como plantilla o con un bloque «Datos de tu centro» (`antes-de-datos-reales.md`, apartado 6, última viñeta de landings).
- [ ] **Pendiente** — **Registro de actividades mínimo** (centro y ResidApp) y **EIPD del piloto** con el anexo de ResidApp.
- [ ] **Pendiente** — **Hoja de primera capa** (plantilla de información al ingreso que ofrece ResidApp, decidido el 2026-11-13) para residentes y representantes, familias y personal (apartado 3, fila 3), entregada por el centro.
- [ ] **Pendiente** — **Protocolo de brechas** con persona de contacto y suplente por centro y plazo propio de aviso de 24 horas (apartado 6.3). Falta que CJ acepte por escrito ser suplente de contacto de ResidApp.
- [ ] **Pendiente** — **Procedimiento de derechos** (apartado 6.1).
- [ ] **Decide una persona** — **Plazos de conservación** de `eventos_acceso` (guarda IP), `sesiones`, auditoría y registros de seguridad (`antes-de-datos-reales.md`, apartado 8).
- [ ] **Pendiente** — **Compromiso de confidencialidad por escrito** de quien opere ResidApp (RGPD art. 28.3.b).
- [ ] **Pendiente** — **Salida de datos al terminar el contrato**: definir cómo se devuelve la historia clínica (hoy no existe devolución completa; `anexo-modulos-contrato-borrador.md`, cláusula 6.6).
- [ ] **Pendiente** — **Mantener apagado** lo que no está cerrado: dictado (Mistral), aviso por correo, lectura de historial tras traslado, ficha de alergias (no construida). Cada uno tiene sus condiciones en `antes-de-datos-reales.md` (apartados 1, 7, 11) y en el anexo de módulos (4.1).

### 7.2 Momento 2. Antes del primer centro real

- [ ] **Decide una persona** — **Existe la sociedad titular** (o se decide por escrito que sigue una persona física) y se completa el **aviso legal** (nombre, domicilio, correo y registro mercantil: LSSI art. 10.1.a y b). Es previo al contrato de encargado y al pie de los correos (`antes-de-datos-reales.md`, apartado 7).
- [ ] **Pendiente** — **Plantillas del contrato de encargado y del contrato de servicio** preparadas por el abogado **antes de las primeras ventas**.
- [ ] **Pendiente** — **Anexo de módulos** revisado por el abogado y, si se ofrece el dictado, **retención cero** de Mistral por escrito, entre otras condiciones bloqueantes (anexo, ficha 4.1). El **interruptor por centro** (ADR 0015) debe existir o tener un procedimiento equivalente que acepte el abogado; hoy es una propuesta sin construir.
- [ ] **Pendiente** — **EIPD del centro** con su anexo de ResidApp, **registro de actividades** del centro y de ResidApp al día, y consulta previa a la autoridad si la EIPD deja riesgo alto (art. 36).
- [ ] **Pendiente** — **DPO del centro** designado si procede y comunicado a la autoridad en 10 días (apartado 5).
- [ ] **Pendiente** — **Documentación del ENS**, solo si la persona experta dice que aplica (responsable de seguridad, política de seguridad, declaración de aplicabilidad).
- [ ] **Pendiente** — **Política y cláusulas validadas** y entregadas por el centro a residentes, representantes, familias y personal; **política para profesionales** redactada (debe decir que el operador de ResidApp ve la estructura y las Administraciones, no la actividad asistencial: `antes-de-datos-reales.md`, apartado 9).
- [ ] **Pendiente** — **Landings** (`residapp.es`, `.cat`): política, aviso legal y cookies validados en castellano y catalán, sin recursos de terceros (`antes-de-datos-reales.md`, apartado 6).
- [ ] **Pendiente** — **Segunda persona** para operaciones irreversibles en producción, o decisión escrita de que esas operaciones no se hacen (`entorno-produccion-lectura-dpo.md`, apartado 4).
- [ ] **Pendiente** — **Simulacro de brecha y de restauración** con acta sin datos (`entorno-produccion-lectura-dpo.md`, apartados 3.2 y 11).
- [ ] **Pendiente** — **Contratos y condiciones de vuestros proveedores** (Microsoft, correo, Mistral, GitHub) revisados por el abogado (apartado 3, fila 13).
- [ ] **Decide una persona** — **Repositorio privado** o decisión escrita en contra (aplazado: ADR 0014; `entorno-produccion-lectura-dpo.md`, apartado 6).
- [ ] **Pendiente** — **Licencia de la librería de PDF**: hacer el cambio a QuestPDF o decidir entre quedarse o comprar licencia comercial (`antes-de-datos-reales.md`, apartado 12). No es de protección de datos, pero está en la misma lista de «antes del primer centro».

#### Repaso técnico con el equipo de desarrollo (hacedlo antes de firmar con el primer cliente)

Una conversación con quien programa, con estas tres preguntas. Todo está planteado en [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md) y los ADR [0014](../decisiones-arquitectura/0014-entorno-produccion.md), [0015](../decisiones-arquitectura/0015-modulos-opcionales.md) y [0016](../decisiones-arquitectura/0016-dispositivos-autorizados.md). **Hoy producción no existe y el ADR 0014 está aplazado hasta que exista la sociedad.**

1. **¿Dónde se alojan los datos?**
   - [ ] Región elegida dentro de la UE/EEE y escrita en el contrato; política de Azure de regiones permitidas; qué pasa con la copia geográfica (el ADR avisa de que Spain Central parece no tener región par: a verificar).
   - [ ] Qué servicios de Microsoft intervienen y en qué ubicación (Web App, SQL, Key Vault, almacenamiento, correo) para el anexo de subencargados.
   - [ ] Nada de Front Door ni WAF global sin decisión expresa (está excluido del EU Data Boundary según el ADR 0007).
2. **¿Cómo se hacen y se guardan las copias de seguridad?**
   - [ ] Cuántos días se conservan las copias (la lectura propone cifra corta y escrita: **no** 15 años) y cómo se restaura **sin deshacer** borrados y revocaciones de acceso (`entorno-produccion-lectura-dpo.md`, apartado 3).
   - [ ] Registro externo de supresiones y revocaciones, y primer simulacro de restauración.
3. **¿Cómo se gestionan las contraseñas y los accesos?**
   - [ ] **Clave TOTP en claro** en `dbo.credenciales` (puerta abierta conocida del ADR 0007) y el resto de secretos: dónde viven y quién los ve.
   - [ ] Acceso del personal de ResidApp a producción: cuentas separadas, sin permisos permanentes de propietario, cuenta de emergencia y alertas (apartado 4 de la lectura).
   - [ ] La conexión de tareas `ResidAppTareas` **usa hoy contraseña** (decidido así): cómo se custodia.
   - [ ] Acceso de las **familias**: segundo factor por población (pregunta 3.2 de CJ) y la contradicción FAM-10 frente al correo opcional.
   - [ ] **Nada de datos reales en dev ni en portátiles**; `/pendientes-cj` fuera del paquete de producción; sin semillas de datos de prueba en producción.

### 7.3 Momento 3. De forma continua

- [ ] **Revisión periódica** de política, registro de actividades, EIPD, contratos y plazos de conservación (la persona experta fija la frecuencia).
- [ ] **Cambios de ley:** poner al día la base normativa (`pwsh -NoProfile -File tools/normativa.ps1`; comprobar si la última comprobación tiene más de una semana) y seguir lo que viene: Espacio Europeo de Datos de Salud y Reglamento de IA (calendarios a verificar), y los productos sanitarios si hay módulos de apoyo a la decisión.
- [ ] **Antes de crear o cambiar** un ADR, un esquema SQL, una integración con un tercero o una función de IA: consultar a `dpo-residapp` (regla del proyecto, `CLAUDE.md`), y pasar lo relevante a la persona experta.
- [ ] **Módulo nuevo:** ficha de privacidad (anexo de módulos, plantilla 4.2), EIPD actualizada, instrucción del centro y firma antes de activarlo.
- [ ] **Proveedor o subencargado nuevo, o cambio de región:** autorización previa del centro y aviso con antelación (Mistral da diez días para objetar; el aviso al centro debe ser más corto: anexo, cláusula 7.3).
- [ ] **Brechas e incidentes:** documentar cada una y revisar el protocolo después.
- [ ] **Accesos y registros:** revisar quién tiene acceso de operador y las alertas; revisar los registros de lecturas del historial (`historial-tras-traslado-04-procedimiento-revision.md`).
- [ ] **Plazos:** vigilar vencimientos de conservación (el primero llega «2041 como pronto»: `antes-de-datos-reales.md`, apartado 8) y la purga de `envios_correo`.
- [ ] **Formación y confidencialidad** de quien opere ResidApp y recordatorio a los centros de la suya.
- [ ] **Derechos y reclamaciones:** llevar un registro simple de peticiones y de cómo se resolvieron.
- [ ] **Cada versión menor de ResidApp:** revisar que la política y las cláusulas dicen lo que la app hace de verdad.

---

## 8. Lo que solo puede decidir una persona experta

Estos puntos **no se resuelven leyendo esta guía ni este repositorio**. Cada uno ya tiene su contexto y su pregunta en el apartado 4.5.

| Punto abierto | Pregunta | Por qué lo decide una persona | Dónde está |
|---|---|---|---|
| ¿La residencia es «centro sanitario»? | 1 | Depende de qué servicios presta y de la norma catalana; arrastra el DPO obligatorio y el plazo de 15 años | `expurgo-y-supresion.md`, apartado 1 |
| AEPD o APDCAT, según el tipo de centro piloto | 2 | Depende de la titularidad pública, concertada o privada y de si presta un servicio público (Llei 32/2010, art. 3). **Necesito el tipo de centro** | `politica-privacidad-portal.md`, A verificar |
| ENS según el centro piloto | 3 | Depende del sector público y de los conciertos (RD 311/2022, art. 2) | `anexo-modulos-contrato-borrador.md`, apartado 9.13 |
| **Bloqueo frente a borrado de familiares** | 6 | El art. 32 de la LOPDGDD obliga al bloqueo; un análisis de despacho entiende que la AEPD lo excluye del borrado material. **No se anonimiza a una familia real hasta decidir** | `antes-de-datos-reales.md`, apartado 8 |
| **Exención del art. 22.2 de la LSSI para la clave del aparato** (ADR 0016) | 13 | El art. 22.2 exime el almacenamiento «estrictamente necesario» para un servicio «expresamente solicitado»; si la clave del aparato lo es, lo decide la persona experta. El CEPD no analiza exenciones y la restricción la impone el centro | `dispositivos-autorizados-lectura-dpo.md`, apartado 5 |
| **Tope de caducidad de la cookie de idioma** | 13 | Ni el art. 22.2 de la LSSI ni la guía de cookies de la AEPD dan una cifra para las cookies exentas | `portal-familiar-dos-idiomas.md`; `antes-de-datos-reales.md`, apartado 6 |
| Cómo se constituye la sociedad y qué firma cada parte | 5 | Cuestión mercantil y de responsabilidad personal. Orlando decidió el 2026-11-13 esperar a la sociedad antes de firmar con un centro real | Apartado 9 |

---

## 9. Qué cambia mientras no haya sociedad ni DPO

- **Sin sociedad titular:** no hay parte contratante por el lado de ResidApp, no se puede completar el aviso legal (LSSI art. 10) ni el pie de los correos, y el ADR 0014 (producción) está en pausa porque la suscripción de Azure y el repositorio pasarán a ser de la sociedad. **Decidido por Orlando el 2026-11-13: no se firma con ningún centro real hasta que exista la sociedad** (no hay titular provisional como persona física). Un contrato firmado a título personal obligaría a Orlando personalmente.
- **Sin DPO real:** los borradores de esta carpeta no los ha leído nadie con independencia; las puertas del DPO del ADR 0007 y del ADR 0016 siguen abiertas; no hay contacto formal ante la autoridad. **No se pasa a datos reales sin él o sin un abogado equivalente.**
- **Una sola persona opera ResidApp:** no hay «cuatro ojos» para lo irreversible, ni separación entre responsable de seguridad y del sistema (ENS art. 13.3), ni cobertura de noches y vacaciones para brechas. Hay que compensarlo por escrito.
- **Lo que sí se puede hacer ya, sin datos reales:** construir, probar con datos inventados, redactar, validar con la persona experta y decidir.

---

## Apéndice. Los borradores de esta carpeta, en una línea cada uno

| Archivo | De qué trata | Alimenta el documento nº |
|---|---|---|
| [`antes-de-datos-reales.md`](antes-de-datos-reales.md) | Lista maestra de lo pendiente antes de usar datos reales | Guía completa (momentos 1 y 2) |
| [`politica-privacidad-portal.md`](politica-privacidad-portal.md) | Política de privacidad de la app, con notas para quien valide | 1, 2 |
| [`anexo-modulos-contrato-borrador.md`](anexo-modulos-contrato-borrador.md) | Anexo de módulos del contrato | 6, 7, 8 |
| [`modulos-opcionales-lectura-dpo.md`](modulos-opcionales-lectura-dpo.md) | Lectura previa del ADR 0015 | 7 |
| [`entorno-produccion-lectura-dpo.md`](entorno-produccion-lectura-dpo.md) | Producción: telemetría, copias, accesos, repositorio, correo | 5, 10, 12, 13 |
| [`registros-tecnicos-lectura-dpo.md`](registros-tecnicos-lectura-dpo.md) | Registros técnicos y alertas (A4) | 10, 12 |
| [`dispositivos-autorizados-lectura-dpo.md`](dispositivos-autorizados-lectura-dpo.md) | Aparatos autorizados por centro y control laboral | 3, 4, 5 |
| [`expurgo-y-supresion.md`](expurgo-y-supresion.md) | Plazo de 15 años, expurgo, supresión de familiares y de personal | 11, 12 |
| [`landings-y-passkeys-por-subdominio.md`](landings-y-passkeys-por-subdominio.md) | Landings, dominios y passkeys | 1, 2, 9 |
| [`portal-familiar-dos-idiomas.md`](portal-familiar-dos-idiomas.md) | Portal Familiar en castellano y catalán | 2, 3 |
| [`aviso-cita-por-correo.md`](aviso-cita-por-correo.md), [`aviso-cita-cambio-cancelacion-por-correo.md`](aviso-cita-cambio-cancelacion-por-correo.md), [`aviso-cita-icono-en-correo.md`](aviso-cita-icono-en-correo.md) | Avisos de cita por correo a familias | 3, 10 |
| [`direccion-confirmada-familia.md`](direccion-confirmada-familia.md) | Casilla «Dirección confirmada con la familia» y su procedimiento | 3, 11 |
| [`tarea-correo-identidad-rls.md`](tarea-correo-identidad-rls.md) | Identidad propia en base de datos para la tarea del correo | Seguridad (7.2, repaso técnico) |
| [`historial-tras-traslado-01…06`](historial-tras-traslado-01-registro-actividades.md) (seis archivos) | Historial tras un traslado: registro, EIPD, información, revisión, conservación, nota 0075/0076 | 3, 4, 5, 12 (parcialmente desactualizados: `antes-de-datos-reales.md`, apartado 5) |
| [`dashboards-lectura-dpo.md`](dashboards-lectura-dpo.md) | Dashboards de Dirección | Módulos futuros (pregunta 17) |
| [`antecedentes-medicacion-alergias-lectura-dpo.md`](antecedentes-medicacion-alergias-lectura-dpo.md) | Medicación, enfermedades y alergias en el informe de derivación | Función no construida |
| [`aviso-cj-notas-lectura-dpo.md`](aviso-cj-notas-lectura-dpo.md) | Aviso a CJ de notas de ingeniería | Sin relevancia para esta guía |
| [`normativa/README.md`](normativa/README.md) | Cómo funciona la base normativa local | Todos |

---

## A verificar o decidir

**Decidido por Orlando el 2026-11-13:**
- La búsqueda de la persona experta empieza ya.
- Un solo despacho o dos figuras (abogado y DPO): lo decide la persona experta; es su primera recomendación.
- Sin titular provisional: no se firma con ningún centro real hasta que exista la sociedad.
- Persona de contacto de privacidad de ResidApp: Orlando, con CJ de suplente (falta la aceptación escrita de CJ).
- Plazo propio de aviso de brecha al centro: 24 horas (la persona experta revisa la redacción).
- ResidApp ofrece a los centros una plantilla de información al ingreso (hoja de primera capa).
- Esta guía se publica además en «Para Orlando» (`docs/pendientes-cj/orlando/`) con formulario para que CJ la lea y responda. Pendiente de construir y registrar (donde corresponda, README incluido).

**Decide una persona (vosotros o la persona experta):**
- Tipo de centro del piloto (público, concertado o privado): **aún no se sabe**. Lo necesito para cerrar las preguntas 1 a 3.
- Fecha prevista de la sociedad titular.
- Quién cubre noches y vacaciones para brechas, mientras opere una sola persona.

**Citas que no he podido verificar en `normativa-local/`** (no están en la base o no las he releído para esta guía; tratadlas como «a verificar»):
- **Codi de consum, art. 128-1** (información legal en catalán en una web B2B): no está en la base. Solo se cita de pasada a través de [`portal-familiar-dos-idiomas.md`](portal-familiar-dos-idiomas.md).
- **Ley 8/2021** (apoyos a la capacidad jurídica): está en la base, pero no la he leído para esta guía; solo la menciono como pregunta 15.
- **LOPDGDD arts. 87 a 91** (derechos digitales laborales), **Reglamento (UE) 2017/745** (regla 11), **Reglamento de IA** y **Espacio Europeo de Datos de Salud**: están en la base, pero no los he releído; los menciono solo como puntos para la persona experta, sin cifras ni fechas.
- **Guías de la AEPD y de la APDCAT** sobre EIPD y contratos de encargado (`aepd-lista-eipd`, `apdcat-guia-aipd-2025`, `aepd-directrices-contratos-encargado`, `apdcat-guia-encarregat`): están en la base y las señalo como apoyo para el abogado; no las he releído para esta guía. De la guía de brechas de la AEPD solo he leído los apartados sobre el reparto responsable-encargado y la autoridad competente.
- **Contratos o condiciones de Microsoft (Azure, ACS), de Mistral y de GitHub**, y el estado actual del **EU Data Boundary**: no están en la base. Solo cito lo que ya dicen los borradores existentes, que a su vez lo leyeron en resúmenes de páginas web.
- **Estado de Azure dev** (alertas, almacenamiento de registros): consta en el relevo del 2026-11-12 (`../prompts-ia/continuar-construccion.md`); no es comprobable desde el repositorio.
- **Desde cuándo «tiene constancia» el centro** cuando el aviso llega de ResidApp (RGPD art. 33.1): no lo define el texto leído; lo debe aclarar la persona experta.

**Qué debe validar un humano:** todo. Esta guía es un borrador de segunda lectura, no un dictamen. **Yo no soy DPO designado ni abogado.**
