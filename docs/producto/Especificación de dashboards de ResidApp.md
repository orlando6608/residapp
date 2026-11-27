# Especificación de dashboards de ResidApp

Nov 10, 2026 · @Orlando Santos

Estado: **propuesta revisada el 2026-11-10** contra las decisiones ya tomadas (ADR 0006, 0008 y 0015, lectura del DPO de módulos, guía de diseño y directrices PWA). Lo que contradecía alguna de ellas se ha quitado o rebajado; el apartado «Qué se descartó y por qué» lo recoge.

**Construido (2026-11-27): la base común y Administración (fases 1 y 2).** Lo demás sigue sin construir. Al construirlas, Orlando decidió, y esto manda sobre lo que diga más abajo:

1. **Dos sitios.** Los avisos siguen en «Requiere atención» del Inicio de Administración, que gana los dos nuevos; las cifras y los gráficos van en una página aparte, «Panel de gestión» (`/Administracion/Panel`), con su tarjeta en «Inicio», al lado de la de «Administración» (Orlando, 2026-11-27).
2. **Sin caché de servidor** hasta medir que una pantalla va lenta (las condiciones del apartado «Datos, histórico y rendimiento» se aplicarán si se añade).
3. **Sin datos anteriores no se compara:** si al empezar el periodo no había ningún residente registrado, se lee «Sin datos anteriores para comparar» (un censo importado diría que han entrado todos).
4. **Una baja anulada con una reactivación sigue contando**, igual que la reactivación como alta, y la pantalla lo dice.

Los criterios de lo construido están en `docs\historias-usuarios\administracion.md`, historia 12. Decisiones de construcción: tramos por día, semana de lunes a domingo o mes natural; las tarjetas de citas y comunicados no llevan variación; «Comunicados» usa la cifra del Inicio (publicados en los últimos 7 días); la evolución de autorizaciones es la de los residentes que hoy están en el ámbito; la rosquilla lleva el porcentaje en el centro y el texto completo al lado; un Centro de Día no ve ocupación ni plazas (ADR 0017). Quedan pendientes de la fase 2 la frase del registro de actividades del centro y la de «estadística interna» de su cláusula (lectura del DPO, apartado 1), que valida una persona, y medir los tiempos con el volumen de un centro grande.

## Objetivo y alcance

Los dashboards dan a los perfiles de gestión una vista única, con avisos arriba, cifras con su variación y gráficos sencillos, construida solo con datos que ResidApp ya registra y calculándolo en el momento de abrir la pantalla.

Hoy cada perfil tiene un Inicio con tarjetas de cifras, Administración tiene el bloque «Requiere atención» y Dirección / Coordinación Clínica tiene contadores por unidad, indicadores y calidad de proceso. El Manual (revisado para la v0.3.0) ya anuncia en «Próximamente» un panel completo de gestión administrativa. Este documento no sustituye esas pantallas: las resume en un solo cuadro y enlaza a ellas.

**Primera fase:** Administración (base), avisos nuevos de Dirección / Coordinación Clínica (base) y Plataforma (funcionalidad del proveedor, no módulo).

**Fuera de alcance:**

- Auxiliar y Familia, que no necesitan un cuadro de mando. El Inicio de Auxiliar ya es su lista del día (basal vigente o pendiente y «Cerrado hoy» o «Pendiente» por residente), así que no se le añade nada.
- Medicina y Enfermería, que quedan para una fase posterior como panel de turno (ver «Fases»).
- **Los gráficos, tendencias y comparativas de Dirección**, que son un módulo opcional de pago (candidato «Dashboard de Dirección» del ADR 0015) y no se diseñan aquí; ver «Dirección».
- Una vista conjunta de varios centros.

## Principios de diseño comunes

Todos los dashboards siguen la misma estructura de tres capas, de arriba abajo, y reutilizan las reglas visuales que la app ya aplica en los Inicios (`docs\bocetos-pantallas\guia-diseno-sistema-visual.md`, vinculante).

1. **Avisos destacados.** Lo que requiere acción, en un orden fijo de más a menos urgente (el orden no se reajusta según las cifras). Cada línea lleva una cifra y un enlace a la pantalla donde se resuelve, como ya hace «Requiere atención» de Administración. Si no hay nada, dice «No hay nada pendiente».
2. **Indicadores clave.** Entre cuatro y seis cifras grandes, cada una con su variación respecto al periodo anterior y, si es un porcentaje, con su numerador y denominador («X de Y»).
3. **Gráficos.** Evolución en el tiempo y comparación por unidad o por edificio. Un gráfico **no enlaza** a los casos que lo componen.

**Reglas que se mantienen** (Manual, «Apariencia», y guía de diseño):

- El color nunca va solo: el texto de cada tarjeta dice lo mismo que el color (rojo urgente, ámbar pendiente).
- Una cifra a cero nunca sale en rojo.
- Modo claro, oscuro y automático; al imprimir o guardar como PDF, siempre claro, con el encabezado de marca de la guía (sección 4). **Imprimir o guardar como PDF es una exportación de hecho:** se aplica la misma cifra mínima que en pantalla y el papel lleva la frase «datos agregados, no reidentificar».
- En el móvil, las tablas pasan a tarjetas, sin desplazamiento lateral.
- En los cuadros de gestión no salen nombres de residentes ni texto de comunicados o solicitudes: solo cifras.
- Sin cifras ni gráficos por trabajador (no hay rankings: `alcance.md`, línea 47).

**Reglas nuevas:**

- **Gráficos hechos en el servidor.** Cada gráfico se dibuja como una imagen vectorial (SVG: líneas y rectángulos descritos en texto) que genera el propio servidor al montar la página. No se añade ninguna librería de gráficos ni JavaScript nuevo, no hay `style=""` ni `<script>` en línea (la guía los prohíbe por la política de seguridad futura) y el service worker no cambia. Así sale igual en claro, oscuro e impresión, y la política de privacidad sigue siendo cierta: solo Bootstrap y jQuery locales.
- **Colores de los gráficos.** Solo los de la paleta oficial: azul de marca, verde teal y neutros. El rojo, el ámbar y el cian de las alertas no se usan como color de una serie. Cada serie lleva su etiqueta y sus valores en texto.
- **Una tabla accesible debajo de cada gráfico** con los mismos datos, y un periodo seleccionable (7 días, 30 días, 12 meses) que viaja en la dirección de la página, sin guardar nada en el navegador.
- **Un valor que la cifra mínima oculte no aparece en ningún sitio:** ni en la tabla accesible, ni en un `title`, ni en los atributos del SVG.
- **Orden fijo también en los gráficos y tablas por unidad** (por nombre o por orden de la estructura): nunca de mayor a menor, para que no se lea como un ranking de unidades.
- **Etiquetas neutras:** «intentos fallidos», no «actividad anómala» ni nada que sugiera perfilar a personas.
- **Fallo de carga no es «nada pendiente».** Si un bloque no se puede calcular, dice que no se pueden mostrar las cifras y el resto de la pantalla sigue funcionando (como ya hace el Inicio de Administración); nunca se pinta un cero ni «No hay nada pendiente».
- **Icono:** se reutiliza `indicadores` (`bar-chart-line`) de la guía; no se crea otro.

## Dashboard de Administración

Es el primero que conviene construir: casi todos sus avisos ya existen en «Requiere atención» y los datos de ocupación, familias y citas ya están en la base. Es funcionalidad **base** (no módulo). No necesita tablas nuevas.

### Avisos (de más a menos urgente)

| Aviso | Situación | Enlaza a |
| --- | --- | --- |
| Residentes sin contacto urgente ni motivo indicado | Existe hoy | Lista de residentes filtrada |
| Solicitudes de cita por gestionar, propuestas sin respuesta, citas pasadas sin cerrar | Existe hoy | Citas con la familia |
| Autorizaciones de familiares pendientes, caducadas o que caducan en 30 días | Existe hoy | Lista de residentes filtrada |
| Residentes con ubicación pendiente (sin habitación o sin cama) | Existe hoy | Lista de residentes filtrada |
| Fotos de más de 12 meses | Existe hoy | Lista de residentes filtrada |
| Citas externas que empiezan en los próximos 7 días sin transporte o acompañante confirmado | Existe hoy | Citas externas |
| Cuentas suspendidas o sin ámbito | Existe hoy | Usuarios filtrado |
| Invitaciones de acceso pendientes o caducadas (vigencia de 72 h) | Nuevo; se calcula con las fechas que ya guarda cada invitación | Usuarios |
| Bajas cuyo plazo de conservación vence o está a punto de vencer | Nuevo en el Inicio (la lista de bajas ya lo avisa) | Residentes dados de baja |

### Indicadores y gráficos

| Elemento | Qué muestra | Tipo |
| --- | --- | --- |
| Residentes activos | Número actual y variación del periodo | Cifra con tendencia |
| Ocupación actual | Plazas ocupadas sobre plazas totales de su ámbito, con el texto «92 % (83 de 90 plazas)» en el centro y en la tabla accesible | Donut (rosquilla: partes de un total) |
| Ocupación por unidad y edificio | Plazas ocupadas sobre plazas totales, por unidad y por edificio | Barras por unidad |
| Movimientos del día | Altas y bajas de hoy; en las bajas, el desglose por las cuatro categorías cerradas de motivo (incluido «Fallecimiento»), nunca el texto de «Otro motivo» | Cifras |
| Altas y bajas | Altas y bajas por mes; el motivo de baja solo con las cuatro categorías cerradas (nunca el texto escrito en «Otro motivo») | Barras agrupadas por mes |
| Citas | Confirmadas hoy y en los próximos 7 días; por semana y estado | Cifra y barras por semana |
| Autorizaciones de familiares | Activas, pendientes, caducadas y suspendidas, con su evolución a partir del historial de cambios | Barra apilada (partes de un total) |
| Comunicados a la familia | Programados y publicados esta semana | Cifra |

Una Administración con ámbito limitado no ve el total del edificio ni del centro, solo el de las unidades de su ámbito (prueba concreta: cuenta con una unidad en un edificio de dos). La evolución de autorizaciones es un dato de familiares cuya base jurídica sigue abierta; este gráfico no la resuelve. El centro, que es el responsable de los datos, anota estos gráficos en su registro de actividades y en una frase de «estadística interna» de su cláusula a residentes y familias.

La ocupación solo se calcula en unidades con habitaciones y plazas definidas; en las demás se muestra el número de residentes sin porcentaje. **A comprobar:** el historial de ubicación permite saber quién ocupaba qué plaza cada día, pero no se ha visto que las plazas tengan historial propio; si no lo tienen, el porcentaje de un día pasado se calcula con las plazas de hoy y la pantalla lo dice.

## Dashboard de Dirección / Coordinación Clínica

Dirección ya ve contadores por unidad, indicadores y calidad de proceso, y todo eso es **base**. El ADR 0015 considera el «Dashboard de Dirección» (resúmenes gráficos) un posible **módulo opcional de pago**, todavía no contratable, y deja sin decidir qué se queda en la base y qué pasa al módulo. Por eso esta especificación se limita a lo que es seguro que es base.

### Lo que entra ahora (base)

Nuevos avisos y contadores en el Inicio, con la misma forma que los de hoy, que solo muestran estado operativo (cifras y fechas de hitos), nunca observaciones, valoraciones ni indicaciones:

| Aviso | Situación | Enlaza a |
| --- | --- | --- |
| Protocolos urgentes activos | Existe hoy como contador por unidad | Supervisión de pendientes |
| Episodios prioritarios abiertos y sin valorar | Existe hoy como contador por unidad | Supervisión de pendientes |
| Seguimientos con la fecha prevista pasada | Existe hoy como contador por unidad | Supervisión de pendientes |
| Indicaciones no realizadas o con incidencia | Existe hoy como contador por unidad | Supervisión de pendientes |
| Episodios abiertos más antiguos que un umbral | Nuevo (umbral por definir con CJ) | Supervisión de pendientes |
| Hitos del proceso fuera de plazo | Nuevo en el Inicio; los plazos ya los fija Dirección con permiso | Calidad de proceso |
| Peticiones de historial tras un traslado sin atender | La pantalla existe; **a comprobar** si hay contador para el Inicio | Peticiones de historial |
| Episodios con indicaciones sin cumplir, que admiten el cierre excepcional | La acción existe en el episodio; **a comprobar** si hay contador para el Inicio | Supervisión de pendientes |

Los bloques de indicadores y de calidad de proceso solo aparecen si la cuenta tiene el permiso correspondiente. Los cálculos de «Indicadores e informe de actividad» y «Calidad de proceso» se reutilizan tal cual, para no crear cifras que los contradigan, y todo porcentaje muestra su numerador, denominador y periodo (historia DIR-08).

**Antes de añadir nada nuevo por unidad hay que revisar un problema que ya existe:** el DPO anotó que los indicadores de Dirección de hoy cuentan por unidad y mes sin ninguna regla de cifras pequeñas (`ISupervisionDirectory`, lectura parcial).

### Lo que queda bloqueado (módulo)

Barras apiladas por estado, línea de episodios nuevos y cerrados, medianas de tiempos, cumplimiento de plazos con tendencia, urgentes y derivaciones por mes e indicaciones por semana. No se construyen hasta que:

1. Orlando decida el reparto entre la base (Indicadores y Calidad) y el módulo (puerta 1 del ADR 0015).
2. Exista su ficha de privacidad y el DPO fije la **cifra mínima** (por debajo de ese número de casos una celda se oculta o se agrupa, y también la celda que permitiría deducirla restando), con CJ.
3. Se respeten las cautelas del DPO: sin enlace del gráfico a la persona, sin desglose por trabajador, pocas combinaciones de filtros, mínimos aplicados en el servidor también al exportar y sin copias derivadas.

## Dashboard de Plataforma

Plataforma es quien opera ResidApp, no el personal de un centro, no ve residentes ni contenido clínico (ADR 0006) y no es un módulo (ADR 0015). Su dashboard mide el **funcionamiento del servicio**: cuentas, invitaciones y accesos, nada asistencial. Solo usa tablas que la aplicación ya lee para Plataforma o que no están bajo seguridad por filas (la regla que impide que un centro lea datos de otro, ADR 0008), de modo que **no hay que ampliar sus permisos** (en la sección «Servicio»; la fase 2 de «Operación técnica» añade una tabla con lectura por procedimiento).

### Avisos (de más a menos urgente)

| Aviso | Situación | Enlaza a |
| --- | --- | --- |
| Invitaciones de acceso o de recuperación caducadas sin usar (vigencia de 72 h) | Nuevo; las fechas ya se guardan | Administraciones y unidades del centro |
| Centros cuya Administración aún no ha hecho su primer acceso (invitación de alta sin usar o caducada) | Nuevo | Centros |
| Cuentas bloqueadas ahora (5 intentos fallidos bloquean 15 min) | Nuevo; solo la cifra, sin nombres | Cuentas de varios centros |
| Módulos de centros que vencen en 30 días o vencidos sin renovar en los últimos 90 | Existe hoy como panel «Módulos que vencen» del listado de Centros (ADR 0015); se **sube al Inicio** de Plataforma con los mismos datos, solo metadatos de centro y módulo | Centros |

### Indicadores y gráficos

| Elemento | Qué muestra | Tipo |
| --- | --- | --- |
| Centros | Total y creados en el periodo, con el desglose por estado y por tipo de servicio (solo metadatos del centro) | Cifra con tendencia |
| Cuentas por centro | Activas, suspendidas y sin ámbito, por perfil | Barras apiladas por centro |
| Cuentas en varios centros | Número y centros que comparten | Cifra |
| Accesos | Entradas correctas y fallidas por día, del servicio entero, con selector de 7 y 30 días (sin 12 meses, para que el gráfico no condicione el plazo de conservación del registro). Con pocos usuarios, «1 entrada ese día» es una persona: mientras haya pocos centros se agrupa por semana | Línea con dos series |
| Passkeys | Cuentas de personal con passkey sobre el total, por centro. **A verificar** dónde se guardan | Barras por centro |

**Condiciones de lectura (DPO):**

- El registro de accesos guarda cuenta, IP y sesión, y **no** el centro. El dashboard solo recibe recuentos `(día, tipo, número)` de una lista cerrada de tipos «fallidos». Nunca se cruza la cuenta con sus ámbitos para sacar «accesos por centro»; si algún día se quiere, cambia el esquema de una tabla sin plazo ni borrado y pasa por el DPO. Nunca por hora.
- «Cuentas bloqueadas ahora» es un recuento que no lee ninguna otra columna de la tabla de credenciales (contiene la clave del segundo factor).
- Familia queda fuera del recuento de cuentas por perfil, salvo decisión contraria (ver «Decisiones abiertas»).
- La finalidad es vigilar la seguridad y la disponibilidad del servicio; usarlo para actividad comercial sería un fin propio de ResidApp. Debe constar en el encargo del centro, en el registro de actividades de ResidApp y en una frase de la política de privacidad para profesionales.
- Con datos de prueba se puede construir ya; **con datos reales, solo cuando estén decididos el plazo del registro de accesos y la cifra mínima.**

La tabla de centros es la pieza principal: una fila por centro con su estado de arranque, sus cuentas y sus invitaciones pendientes. Va en **orden fijo** (por nombre o fecha de alta) y con una etiqueta de estado en texto; no se reordena según lo que requiera atención.

**No se muestra**, en esta versión: residentes activos ni registros por día; nombres de cuenta ni direcciones IP; correos enviados, omitidos o fallidos; errores de la aplicación (los errores técnicos van en «Operación técnica», fase 2, más abajo). La apertura de este dashboard no se anota en la auditoría, porque solo enseña agregados sin datos personales; si el DPO lo pide, se haría con la protección contra envíos duplicados que exigen las directrices PWA (apartado 4).

## Dashboard de Plataforma: Operación técnica

Estado: **plan del 2026-11-19, con las decisiones de Orlando y la primera lectura del DPO (`docs\privacidad\operacion-tecnica-plataforma-lectura-dpo.md`, borrador sin validar por una persona experta); nada construido.** Lo ya descrito arriba pasa a llamarse **«Servicio»** (cuentas, invitaciones, accesos). Esta es la segunda sección, **«Operación técnica»**: dos pestañas bajo la misma ruta y la misma autorización de Plataforma. Plataforma es **solo en español** (el ADR 0013 solo obliga al Portal Familiar y a las pantallas de entrada).

**Finalidad:** vigilar la disponibilidad y el buen funcionamiento del servicio. No mide a personas ni a centros con fines comerciales.

### Reparto entre Azure y la app (decisión 1)

- **Azure** vigila la infraestructura: errores 500, tiempos de respuesta, base de datos, correo y costes, con un panel y alertas en el portal. Es lo que Azure ya sabe y no cuesta código. **La app no lee Azure Monitor ni lleva una credencial de Azure**: sería una superficie nueva en la cuenta más delicada.
- **La app** muestra solo lo que Azure no puede saber: si sus tareas en segundo plano están corriendo, si la clave del dictado está puesta y qué códigos de error de dominio se repiten.

### Qué muestra

| Bloque | Contenido | Origen |
| --- | --- | --- |
| Salud | Base de datos (ok o fallo y milisegundos de una consulta mínima); correo (activo o apagado, con o sin cadena de conexión); dictado (clave global puesta o no). Correo y dictado solo miran la configuración: **no llaman al proveedor** (gastaría cuota y enviaría tráfico a un tercero en cada refresco). No hay bloque de «almacenamiento de ficheros»: las fotos y los logos están en SQL. | Comprobación al abrir la pantalla. Además, un `/healthz` mínimo, sin detalles, para el health check de App Service |
| Tareas en segundo plano | Las tareas (hoy **siete**: aviso de cambio, recordatorio de cita, avisos externos de citas, avisos sin resolver, purga de correos y los dos avisos de CJ): última vuelta, última vuelta correcta, duración y tipo de excepción, o el estado «apagada por configuración». **Sin «elementos tratados»**: con un solo centro piloto, un recuento de 1 equivale a un correo a una familia en un minuto concreto | Tabla nueva, **una fila por tarea** que se actualiza y sin histórico, sin centro ni datos personales ni mensajes (decisión 2) |
| Errores (fase 2) | Hoy y últimos 7 días naturales (el contador es diario: no se pueden calcular 24 h) por firma, plantilla de ruta y centro (el centro solo en acceso denegado y fallo inesperado), con su contador | Tabla nueva de recuentos por día (decisión 3) |
| Versión | Versión desplegada y hora de arranque del proceso | Ya existe |
| Enlace | Al panel de Azure | Un ajuste con la dirección |

### Recomendaciones: reglas deterministas, sin IA (decisión 7)

Un catálogo de reglas en código, revisado por PR como los prompts. Cada regla tiene un código, una condición sobre cifras ya calculadas, un **texto fijo en español** que dice qué hacer y un enlace. Los umbrales van en configuración y no se inventan: se arranca con un valor provisional y se ajusta tras una semana de datos reales. Un test por regla. Van en **orden fijo** (por el de la tabla, no por gravedad), el color nunca va solo y una cifra a cero no sale en rojo.

| Regla | Cuándo salta | Qué recomienda | Fase |
| --- | --- | --- | --- |
| R1. Tarea sin latido | Pasa más de 3 veces su intervalo desde su última vuelta. Una tarea «apagada por configuración» (hoy no hace vuelta) no cuenta; si no, saltaría siempre en desarrollo | Comprobar «Siempre activo» en la Web App | 1 |
| R2. Recordatorio sin vuelta en franja | Hoy no hay vuelta del recordatorio entre las 10:00 y las 18:59 (hora de Madrid) | Igual, y avisa de que ese día se pierden recordatorios sin error visible | 1 |
| R3. Tarea sin identidad propia | La conexión de la tarea no es del rol `residapp_tareas` (hoy solo lo escribe el registro) | Dar el rol al usuario de la tarea | 1 |
| R4. Última vuelta en error | Termina en excepción, con su tipo | Explicación fija por tipo de excepción | 1 |
| R5. Dictado sin clave | Algún centro tiene activo el módulo de dictado y la clave global falta | Poner la clave o apagar el módulo en ese centro | 1 |
| R6. Base lenta | Los milisegundos superan un umbral (se fija viendo antes el valor normal) | Mirar el panel de Azure | 1 |
| R7. Firma de error nueva | Una firma que no estaba en los 7 días anteriores | Mirar el registro de Azure con esa firma | 2 |
| R8. Errores concentrados en un centro | Un centro suma al menos **5 errores** (mínimo absoluto, provisional, en configuración) y más de un porcentaje de los errores de hoy. El denominador solo cuenta errores con centro (los de seguridad e inesperados) | Contactar con ese centro. El texto **no cita el art. 33 RGPD**: un pico de errores no es una brecha (CEPD 9/2022, párrafo 44) | 2 |
| R9. Conflictos de concurrencia repetidos, **global y sin centro** | El mismo código `*_CONFLICT` varias veces en la misma ruta, en todo el servicio | Señal de diseño del producto (por ejemplo, avisar mejor a quien edita), no de cómo trabaja un centro | 2 |

**Sin IA, por ahora.** El botón «Análisis ejecutivo con IA» se descarta de momento: el único proveedor autorizado es Mistral (Claude exigiría contrato, EIPD y un ADR), el único caso de uso aprobado es el dictado (ADR 0011) y un resumen de cinco hallazgos aporta poco sobre los textos de las reglas. Si se reabre: con Mistral, solo en Plataforma (no lleva datos personales), solo sobre la salida de las reglas y nunca sobre la base, voluntario, auditado sin contenido y con un ADR nuevo. El DPO añade tres condiciones: que antes se cumpla la primera puerta del ADR 0011 («un caso que las reglas no resuelvan», que hoy falla); acotar el criterio de 2026-10-21 (el ADR 0011 dice que la IA «no… resume, prioriza»); y que los campos enviados vayan en una lista cerrada. No se reabre mientras no se cumpla la primera.

### Entregas

- **Fase 1:** salud, latido de tareas, versión, enlace a Azure, `/healthz` y reglas R1 a R6. Necesita un script SQL nuevo (la tabla del latido; el último aplicado en `main` es el `0105` de las citas externas, así que será el `0106` o el primero libre al construirlo) y la consulta previa a `dpo-residapp`.
- **Fase 2:** tabla de errores con centro y reglas R7 a R9. Necesita un **ADR nuevo** que cambie el ADR 0008 (una tabla con centro que lee Plataforma) y al DPO.
- El PR 6 de las citas externas y el hito 0.5.0 ya están cerrados en `main`; falta que Orlando decida cuándo empezar.

### Errores con centro: condiciones (decisión 3 y lectura del DPO)

Condiciones que fijan la decisión de Orlando y la lectura del DPO:

- Se guarda solo **día, firma, plantilla de ruta, centro y contador**: la firma es el tipo de excepción, el número de SQL o el código de dominio, con lista cerrada y «(otro)» para lo demás. Nunca el mensaje, el cuerpo, la cadena de consulta ni un identificador de residente, de vínculo o de invitación. **Ni la hora**: ninguna columna de fecha con hora, ni identidad creciente (conservaría el orden de aparición), ni `rowversion` ni tabla temporal.
- La plantilla de ruta sale de los **metadatos del punto final**, nunca de la dirección real de la petición; sin coincidencia pone «(sin ruta)». Prueba con GUID y token de invitación reales.
- El centro **no sale de la cookie del ámbito activo** (hoy no se valida contra la base): es el que deduce la base a partir del ámbito, no lo recibe la aplicación. **Solo lo llevan el acceso denegado y el fallo inesperado**; los conflictos de concurrencia (`*_CONFLICT`) no llevan centro, porque son un hecho de dos personas trabajando a la vez y, con centro, pantalla y día, podrían señalar a personas concretas de un centro pequeño. Fuera de una petición (las tareas) o antes de entrar, queda vacío; es aceptable, no se rellena con IP, hora ni cruces con el registro de accesos, y la pantalla enseña «sin centro».
- La tabla queda **fuera de la seguridad por filas**, como `eventos_acceso`. La aplicación solo la toca con dos procedimientos `EXECUTE AS OWNER`: uno de escritura (que además borra lo que pase de **30 días**) y otro de lectura que comprueba el ámbito `PLATAFORMA`. Hace falta un ADR nuevo.
- Volcado por lotes desde la memoria, con tope de combinaciones por día; un fallo del volcado no afecta a la petición y, si la base no responde, los errores del lote se descartan. Para eso queda el registro de A4.
- Motivo de querer el centro (Orlando): acotar al máximo la causa de un fallo y alcanzar rápido a la residencia afectada, dentro del plazo propio del contrato.

**Decisiones de Orlando frente a la recomendación del DPO (2026-11-19).** El DPO propuso (1) centro solo para acceso denegado y fallo inesperado; (2) que la lectura devuelva solo totales por separado, nunca el cruce centro × firma × ruta; y (3) R9 solo global. Orlando, tras revisar el riesgo en lenguaje llano, **aceptó (1) y (3)** y **se aparta en (2)**: Plataforma ve el **cruce completo, sin mínimo**, para esos dos tipos de error, porque lo más valioso para hallar la causa es la primera aparición de un fallo raro en un centro concreto, y un mínimo la ocultaría. El riesgo que queda: con centro, pantalla y día, un fallo inesperado con un solo caso puede señalar de forma indirecta a una persona de un centro pequeño (considerando 26 del RGPD). Lo acotan que los conflictos de concurrencia (lo que dos personas hicieron a la vez) no llevan centro, los 30 días, la ausencia de hora y la cláusula que prohíbe usarlo para evaluar al personal. **El DPO real o un abogado debe validar expresamente este cruce antes de usarlo con datos reales.** El mínimo de 5 solo se aplica a R8.

### Azure: alertas y panel (decisiones 4, 5, 6 y 9)

No se construye en el repositorio: es un documento con los comandos y los umbrales, que aplica Orlando. Todo es de **desarrollo**; producción queda en el ADR 0014. Solo métricas de plataforma, sin Log Analytics (que tiene coste y una retención por decidir).

| Alertas | Estado |
| --- | --- |
| Errores 5xx (≥ 5), accesos denegados 403 (≥ 20) y conexiones fallidas de SQL (≥ 5), ventana de 5 min, grupo `ag-residapp-dev-alertas` | Existen (A4) |
| Presupuestos `PresupuestoResidApp` (10 €/mes; avisos al 50 %, 80 % previsto y 95 %) y `AlertaResidApp20` (20 €/mes, 100 % real) | Existen; se dejan como están. El gasto normal ronda los 16 €/mes (dato del relevo del 2026-11-03, a reverificar) |
| Salud de la web: health check de App Service, tiempo de respuesta, CPU y memoria del plan | Nuevo |
| Base de datos: CPU o DTU sostenido, bloqueos y espacio usado | Nuevo |
| Correo: métrica de estado de entrega de ACS con resultado «Failure» (el total, no el rebote por destinatario) | Nuevo |

**Panel:** un Dashboard del portal de Azure, compartido, con cuatro bloques (ventana de 24 h, ampliable a 7 d): Web (peticiones, 5xx, 403, tiempo de respuesta, health check, CPU y memoria), Base de datos (CPU o DTU, conexiones fallidas, bloqueos, espacio), Correo (solicitudes y estado de entrega) y Coste del mes. Se exporta como JSON. Si el suplente ha de verlo, necesita un permiso de lectura mínimo sobre el grupo de recursos (hoy solo tiene lectura de blobs).

### Lectura del DPO (2026-11-19) y lo que queda

Las cinco preguntas están respondidas en `docs\privacidad\operacion-tecnica-plataforma-lectura-dpo.md` (borrador de una segunda lectura, no de un DPO designado):

1. **Latido:** admisible sin lectura nueva, con una fila por tarea y sin histórico.
2. **Errores con centro:** admisible solo con las condiciones de arriba; la cifra mínima clásica choca con la finalidad (R7 quiere ver el error raro), así que la protección es el mínimo absoluto de 5 y que no haya cruce libre. Las decisiones de Orlando que se apartan de esto están señaladas arriba.
3. **Centro vacío:** aceptable.
4. **Frase de finalidad:** la actual **no basta**. Hay que añadir el diagnóstico de fallos, la atribución a un centro y una prohibición explícita de usar estos recuentos para medir personas, equipos o centros ni con fines comerciales (art. 28.3.a y 28.10 RGPD; art. 33.2 LOPDGDD). Tres redacciones propuestas (encargo del centro, registro de actividades y política para profesionales) están en el apartado 4 del borrador del DPO; y lo decidido coincide con ellas en que el centro solo va en fallos de seguridad e inesperados; se ajustaron el 2026-11-19 en lo único que difería («sin cruce de centro con pantalla»), porque Plataforma sí ve el cruce. Siguen siendo una propuesta que valida una persona experta.
5. **Retención:** **30 días** (decidido por Orlando; lo valida el DPO real), nunca más que los 90 de A4, y sin relación con el plazo aún sin decidir de `eventos_acceso` (esta tabla no lleva IP).

Quedan abiertos: la redacción final de la cláusula y quién la firma por la residencia; la validación del DPO real del cruce completo sin mínimo; y el mínimo de 5 de R8, que no sale de ninguna norma (con CJ, junto con la cifra mínima general).

Quedan **fuera** de este apartado el reparto de costes y el margen por centro, el DAU/WAU por centro, los correos por centro, y leer el coste de Azure por su API: ver «Costes y consumo en Plataforma» en las decisiones abiertas.

## Criterio de aceptación común

Los contadores de un dashboard coinciden con las bandejas o listas filtradas a las que enlazan, para el mismo centro y el mismo ámbito. Cada bloque nuevo lleva una prueba que lo comprueba, porque los cálculos se reutilizan y no se duplican.

## Datos, histórico y rendimiento

**No hay almacenamiento nuevo, salvo las dos tablas de «Operación técnica»** (latido de las tareas y recuentos de errores), sin datos personales directos. Todo lo demás se calcula al abrir la pantalla; las tendencias salen de datos que ya llevan fecha:

- Altas y bajas, historial de ubicación (permite calcular la ocupación en cualquier día), episodios, hitos del proceso, citas y comunicados.
- Autorizaciones de familiares: la vigente es el último cambio de cada vínculo, así que el historial de cambios da su evolución.
- Invitaciones: cada una guarda cuándo se emitió, cuándo caduca y cuándo se usó o se anuló.
- Accesos: el registro de accesos guarda fecha, tipo (entrada, contraseña fallida, bloqueo), cuenta, IP y sesión; el dashboard solo publica recuentos.

Reglas:

- **Consultas siempre filtradas** por centro y por las unidades del ámbito de la cuenta, con la misma regla de acceso que ya usan las listas. La única consulta que cruza centros es la de Plataforma y devuelve recuentos.
- **Caché corta** en la memoria del servidor, para que abrir el Inicio no recalcule todo cada vez. Hoy la app no tiene ninguna caché de servidor y sus páginas llevan `NoStore`, así que es un componente nuevo. Condiciones del DPO:
  1. La autorización se evalúa siempre **antes** de mirar la caché y nunca se cachea; si no, una cuenta suspendida o con un permiso revocado seguiría viendo cifras hasta cinco minutos.
  2. La clave incluye el centro validado por la base de datos (no el de la cookie), el conjunto de unidades, el periodo y el permiso del bloque.
  3. Solo se guardan recuentos: ni listas ni SVG montados por cuenta.
  4. Memoria del propio proceso; una caché compartida exigiría un ADR nuevo.
  5. Se mantiene `NoStore` en las páginas y no se guarda nada en el navegador ni en el service worker.
  6. Los valores no se escriben en registros ni en telemetría.
  7. De 1 a 5 minutos, pero **1 minuto** para los avisos que disparan una acción (por ejemplo, cuentas bloqueadas).
- **Rendimiento:** medir los tiempos con el volumen de un centro grande antes de dar por buena cada consulta.
- **Si un gráfico necesita un dato que no se puede reconstruir, ese gráfico se aplaza.** No se crea una tabla de «fotos» diarias: el DPO pide calcular en la consulta, porque una copia guardada es un almacén nuevo con su plazo, su borrado y sus mínimos, y un proceso nocturno que lea todos los centros no encaja con el ADR 0008.

## Privacidad, permisos y auditoría

Los dashboards no amplían lo que cada perfil puede ver: solo lo resumen. Cada bloque se calcula con los mismos permisos y el mismo ámbito que su pantalla de origen.

| Perfil | Qué puede ver el dashboard | Qué no ve |
| --- | --- | --- |
| Plataforma | Cifras de servicio por centro: centros, cuentas, invitaciones y arranque; accesos del servicio entero; latido de las tareas; recuentos diarios de errores técnicos por firma y ruta, y por centro solo en acceso denegado y fallo inesperado (fase 2, con las condiciones de «Operación técnica») | Residentes, contenido clínico, cifras asistenciales, nombres de cuenta, IP, auditoría de otros centros |
| Administración | Cifras administrativas de su centro y de las unidades de su ámbito | Contenido clínico; nombres en los cuadros de gestión |
| Dirección / Coordinación Clínica | Estado operativo y fechas de hitos por unidad; indicadores y calidad de proceso solo con su permiso | Observaciones, valoraciones, indicaciones y fotos; la lectura clínica detallada sigue siendo la consulta auditada del basal |

Reglas de implementación:

- Ningún cuadro de un centro puede leer datos de otro; una cuenta con perfiles en varios centros ve el dashboard del centro y perfil con los que ha entrado, nunca una suma.
- La **cifra mínima** (qué hacer con una cifra de muy pocos casos) es obligatoria en cualquier vista cuyo perfil no tenga ya acceso a los casos concretos (Plataforma y el futuro módulo de Dirección). Administración y Dirección ya ven las listas nominales de su ámbito, así que ocultarles una cifra pequeña de su propio centro no protege a nadie. El número lo fijan el DPO y CJ.
- Sin cifras ni gráficos por trabajador, por cuenta ni por turno con una sola persona.
- Los dashboards de gestión no tienen botón de exportación propio, pero imprimir o guardar como PDF sí cuenta como exportación: el mínimo se aplica en el servidor antes de dibujar (nunca en el navegador) y el papel lleva la marca de la guía y «datos agregados, no reidentificar».
- Los avisos de Dirección son recuentos del estado actual que enlazan a pantallas que ya muestran los casos; **no** se admiten cruces nuevos de unidad × periodo hasta tener la cifra mínima, ni atribución a personas o a turnos con una sola persona. Se informa al personal de que Dirección ve avisos de estado por unidad.
- Las pruebas de rendimiento se hacen solo con datos sintéticos.
- En «centros sin primer acceso» se muestra el centro, nunca el nombre de la persona.
- Abrir los cuadros de Administración y Dirección deja constancia solo donde hoy ya se audita la consulta equivalente.

## Fases y orden de implementación

1. **Base común (sin SQL nuevo).** Estructura de tres capas, componentes de tarjeta, aviso y gráfico SVG con tabla accesible, y selector de periodo. Se da por terminada cuando se ve igual en claro, oscuro, móvil de 375 px e impresión.
2. **Administración.** Avisos existentes más invitaciones y bajas por vencer; ocupación, altas y bajas, citas y autorizaciones. Se prueba con Los Almendros y Los Olmos. Se añaden sus historias de usuario a `docs\historias-usuarios\administracion.md`.
3. **Dirección / Coordinación Clínica (solo base).** Los dos avisos nuevos, tras decidir el umbral con CJ y revisar la ausencia de cifra mínima en los indicadores de hoy. El módulo gráfico es una propuesta aparte.
4. **Plataforma.** Con las cifras de servicio de arriba («Servicio»). Se prueba con los dos centros de pruebas y la cuenta de Plataforma de pruebas. La sección «Operación técnica» tiene sus propias dos fases (ver su apartado) y empieza cuando Orlando lo decida (el PR 6 de las citas externas y el hito 0.5.0 ya están cerrados).
5. **Panel de turno para Medicina y Enfermería (fase posterior).** No es un cuadro de gestión sino una lista accionable del turno: eventos prioritarios abiertos, escalados recibidos (Medicina), indicaciones sin leer (Enfermería), seguimientos con la fecha pasada, protocolos urgentes activos y residentes sin basal. Casi sin gráficos y sin cifras por profesional, para que no se lea como un ranking de personas. Borrador de zonas: *Enfermería*, avisos de hitos, bandeja prioritaria (cambios prioritarios), protocolos urgentes, seguimientos pendientes y transferidos entre turnos, indicaciones pendientes o sin leer, episodios que se pueden cerrar, eventos pendientes de valoración, escalados enviados a Medicina y comunicados pendientes de publicar; *Medicina*, solo los escalados a Medicina y los eventos médicos propios (no todos los de Enfermería), protocolos urgentes, seguimientos pendientes, programados y vencidos, e indicaciones sin leer, con incidencia o en seguimiento. Ninguna acción cambia de dueño: Medicina no realiza indicaciones en nombre de Enfermería. Cada zona se contrasta con su pantalla y su permiso actuales antes de construirla.

Cada fase actualiza el Manual, la Guía por perfil y el Flujo de trabajo con el agente que ya mantiene la documentación (`doc-usuario-residapp`), y se revisa con `qa-revisor-residapp` antes de darla por cerrada. Antes de la fase 2 se consulta a `dpo-residapp` sobre la finalidad nueva de los gráficos de Administración.

## Qué se descartó y por qué

| Propuesta de la primera versión | Motivo |
| --- | --- |
| Gráficos, tendencias y comparativa de Dirección dentro de la base | ADR 0015: es un módulo candidato, de pago y no contratable todavía |
| Enlace de un gráfico al detalle de los casos | Cautela del DPO: sería una puerta trasera sin la trazabilidad de la pantalla individual |
| «Foto diaria» de agregados con proceso nocturno | Cautela del DPO (no guardar copias derivadas); ADR 0008 (nada cruza centros fuera de Plataforma); ADR 0014 aplazado. Los datos con fecha bastan |
| Plataforma: «centros sin arrancar» por número de residentes, edificios y plazas | ADR 0006: no ve residentes, y esas tablas están bajo seguridad por filas |
| Plataforma: correos por centro | `envios_correo` está bajo seguridad por filas y Plataforma no tiene permiso de lectura sobre ella |
| Plataforma: lista de cuentas con muchos fallos y enlace a «Soporte de accesos» | Esa pantalla no existe; el registro de accesos no guarda el centro y su plazo de conservación está sin decidir; listar cuentas es vigilancia por persona |
| Plataforma: tabla ordenada por lo que requiere atención | Guía de diseño: el orden no se reajusta según las cifras |
| Plataforma: auditar la apertura del dashboard | No hay datos personales; una lectura que escribe auditoría exige protección contra duplicados |
| Operación técnica: leer Azure Monitor desde la app | Exigiría una identidad con permiso de lectura sobre Azure dentro de la app (superficie nueva); el portal ya da el panel y las alertas (decisión 1) |
| Operación técnica: botón «Análisis ejecutivo con IA» | Claude no es proveedor autorizado y el análisis no es un caso de uso aprobado (ADR 0011); aporta poco sobre las reglas (decisión 7) |
| Operación técnica: coste y margen por centro, DAU/WAU por centro, correos por centro | Datos que no existen o están bajo seguridad por filas; costes compartidos; actividad por persona sin cifra mínima |
| Librería de gráficos propia | Sin precedente en la app; la guía prohíbe `style` y `script` en línea; la política de privacidad afirma que solo hay Bootstrap y jQuery |

## Decisiones abiertas

- [ ] **Reparto entre la base y el módulo de Dirección.** Qué se queda en Indicadores y Calidad y qué pasa al módulo (puerta 1 del ADR 0015). Bloquea los gráficos de Dirección.
- [ ] **Cifra mínima.** Número y tratamiento (ocultar o agrupar, y la celda complementaria), con el DPO y CJ. Obligatoria para Plataforma y para el módulo de Dirección.
- [ ] **Plazo y borrado del registro de accesos** (incluida la IP). Sin él, Plataforma no usa datos reales (DPO). Está pendiente en `docs\privacidad\antes-de-datos-reales.md`.
- [ ] **Familia en el recuento de cuentas de Plataforma.** Por defecto queda fuera.
- [ ] **Umbrales de aviso.** Antigüedad de un episodio abierto para avisar a Dirección, y plazo para dar por pendiente una reevaluación de basal (CJ).
- [ ] **Uso por módulo en Plataforma.** El DPO admite contadores de uso por centro y por módulo, sin cuenta, si la ficha del módulo y el contrato los mencionan; decidir si se quieren cuando haya más de un módulo.
- [ ] **Costes y consumo en Plataforma** (idea de Orlando, 2026-11-16; nada construido). Propuesta por fases: **0)** sin código, un presupuesto con alertas en Azure Cost Management y un límite de gasto en la consola de Mistral; **1)** cuando haya varios centros reales o más de un módulo de pago, contadores por centro y módulo (sin cuenta, como en «Uso por módulo») y una tarjeta de «uso estimado» (contador × tarifa configurable; no es la factura); **2)** opcional, leer el coste real de Azure por su API con una identidad de solo lectura. La factura de Azure no se puede desglosar por centro (recursos compartidos: como mucho, un prorrateo con regla explícita) y la de Mistral tampoco (clave única). Dudas: regla de prorrateo, tarifas, permiso de lectura sobre la suscripción frente al aislamiento (ADR 0014), y cifra mínima si un centro tiene una sola persona usando el dictado (sería un contador por persona de hecho). Los contadores también sirven para fijar precios de módulos (ADR 0015, decisión 9). Antes de crear esquema SQL, consultar a `dpo-residapp`.
- [x] **Errores en Plataforma.** Decidido el 2026-11-19: Azure vigila la infraestructura y la app guarda recuentos de errores por firma, día y centro (fase 2 de «Operación técnica»; exige un ADR nuevo que cambie el 0008 y al DPO). **Correos por centro** sigue sin mostrarse (`envios_correo` está bajo seguridad por filas); la métrica de fallos de entrega se mira en Azure. Retención de la tabla de errores: **30 días**, a validar por el DPO real.
- [ ] **Vista consolidada.** Las cuentas con varios centros ven cada centro por separado; una vista conjunta sería un módulo distinto, decisión de producto y de privacidad aparte (ADR 0008, lectura del DPO).

## Referencias

- `docs\decisiones-arquitectura\0006-perfil-plataforma.md`, `0008-multicentro-y-grupos.md`, `0011-arquitectura-ia.md`, `0014-entorno-produccion.md` (observabilidad), `0015-modulos-opcionales.md`.
- `docs\privacidad\modulos-opcionales-lectura-dpo.md` (apartado 6, cautelas de agregación) y `anexo-modulos-contrato-borrador.md` (4.3).
- `docs\privacidad\dashboards-lectura-dpo.md`: lectura del DPO sobre esta especificación (borrador; la valida una persona antes de usarlo con datos reales).
- `docs\bocetos-pantallas\guia-diseno-sistema-visual.md` y `docs\decisiones-arquitectura\directrices-pwa-movil.md`.
- `docs\producto\alcance.md` y `docs\historias-usuarios\direccion-coordinacion-clinica.md` (DIR-08, DIR-09).
