# Especificación de dashboards de ResidApp

Nov 10, 2026 · @Orlando Santos

Estado: **propuesta revisada el 2026-11-10** contra las decisiones ya tomadas (ADR 0006, 0008 y 0015, lectura del DPO de módulos, guía de diseño y directrices PWA). Lo que contradecía alguna de ellas se ha quitado o rebajado; el apartado «Qué se descartó y por qué» lo recoge. Nada de esto está construido.

## Objetivo y alcance

Los dashboards dan a los perfiles de gestión una vista única, con avisos arriba, cifras con su variación y gráficos sencillos, construida solo con datos que ResidApp ya registra y calculándolo en el momento de abrir la pantalla.

Hoy cada perfil tiene un Inicio con tarjetas de cifras, Administración tiene el bloque «Requiere atención» y Dirección / Coordinación Clínica tiene contadores por unidad, indicadores y calidad de proceso. El Manual (revisado para la v0.3.0) ya anuncia en «Próximamente» un panel completo de gestión administrativa. Este documento no sustituye esas pantallas: las resume en un solo cuadro y enlaza a ellas.

**Primera fase:** Administración (base), avisos nuevos de Dirección / Coordinación Clínica (base) y Plataforma (funcionalidad del proveedor, no módulo).

**Fuera de alcance:**

- Auxiliar y Familia, que no necesitan un cuadro de mando.
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

Los bloques de indicadores y de calidad de proceso solo aparecen si la cuenta tiene el permiso correspondiente. Los cálculos de «Indicadores e informe de actividad» y «Calidad de proceso» se reutilizan tal cual, para no crear cifras que los contradigan, y todo porcentaje muestra su numerador, denominador y periodo (historia DIR-08).

**Antes de añadir nada nuevo por unidad hay que revisar un problema que ya existe:** el DPO anotó que los indicadores de Dirección de hoy cuentan por unidad y mes sin ninguna regla de cifras pequeñas (`ISupervisionDirectory`, lectura parcial).

### Lo que queda bloqueado (módulo)

Barras apiladas por estado, línea de episodios nuevos y cerrados, medianas de tiempos, cumplimiento de plazos con tendencia, urgentes y derivaciones por mes e indicaciones por semana. No se construyen hasta que:

1. Orlando decida el reparto entre la base (Indicadores y Calidad) y el módulo (puerta 1 del ADR 0015).
2. Exista su ficha de privacidad y el DPO fije la **cifra mínima** (por debajo de ese número de casos una celda se oculta o se agrupa, y también la celda que permitiría deducirla restando), con CJ.
3. Se respeten las cautelas del DPO: sin enlace del gráfico a la persona, sin desglose por trabajador, pocas combinaciones de filtros, mínimos aplicados en el servidor también al exportar y sin copias derivadas.

## Dashboard de Plataforma

Plataforma es quien opera ResidApp, no el personal de un centro, no ve residentes ni contenido clínico (ADR 0006) y no es un módulo (ADR 0015). Su dashboard mide el **funcionamiento del servicio**: cuentas, invitaciones y accesos, nada asistencial. Solo usa tablas que la aplicación ya lee para Plataforma o que no están bajo seguridad por filas (la regla que impide que un centro lea datos de otro, ADR 0008), de modo que **no hay que ampliar sus permisos**.

### Avisos (de más a menos urgente)

| Aviso | Situación | Enlaza a |
| --- | --- | --- |
| Invitaciones de acceso o de recuperación caducadas sin usar (vigencia de 72 h) | Nuevo; las fechas ya se guardan | Administraciones y unidades del centro |
| Centros cuya Administración aún no ha hecho su primer acceso (invitación de alta sin usar o caducada) | Nuevo | Centros |
| Cuentas bloqueadas ahora (5 intentos fallidos bloquean 15 min) | Nuevo; solo la cifra, sin nombres | Cuentas de varios centros |

### Indicadores y gráficos

| Elemento | Qué muestra | Tipo |
| --- | --- | --- |
| Centros | Total y creados en el periodo | Cifra con tendencia |
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

**No se muestra**, en esta versión: residentes activos ni registros por día; nombres de cuenta ni direcciones IP; correos enviados, omitidos o fallidos; errores de la aplicación (ver «Decisiones abiertas»). La apertura de este dashboard no se anota en la auditoría, porque solo enseña agregados sin datos personales; si el DPO lo pide, se haría con la protección contra envíos duplicados que exigen las directrices PWA (apartado 4).

## Datos, histórico y rendimiento

**No hay almacenamiento nuevo.** Todo se calcula al abrir la pantalla; las tendencias salen de datos que ya llevan fecha:

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
| Plataforma | Cifras de servicio por centro: centros, cuentas, invitaciones y arranque; accesos del servicio entero | Residentes, contenido clínico, cifras asistenciales, nombres de cuenta, IP, auditoría de otros centros |
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
4. **Plataforma.** Con las cifras de servicio de arriba. Se prueba con los dos centros de pruebas y la cuenta de Plataforma de pruebas.
5. **Panel de turno para Medicina y Enfermería (fase posterior).** No es un cuadro de gestión sino una lista accionable del turno: eventos prioritarios abiertos, escalados recibidos (Medicina), indicaciones sin leer (Enfermería), seguimientos con la fecha pasada, protocolos urgentes activos y residentes sin basal. Casi sin gráficos y sin cifras por profesional, para que no se lea como un ranking de personas.

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
| Librería de gráficos propia | Sin precedente en la app; la guía prohíbe `style` y `script` en línea; la política de privacidad afirma que solo hay Bootstrap y jQuery |

## Decisiones abiertas

- [ ] **Reparto entre la base y el módulo de Dirección.** Qué se queda en Indicadores y Calidad y qué pasa al módulo (puerta 1 del ADR 0015). Bloquea los gráficos de Dirección.
- [ ] **Cifra mínima.** Número y tratamiento (ocultar o agrupar, y la celda complementaria), con el DPO y CJ. Obligatoria para Plataforma y para el módulo de Dirección.
- [ ] **Plazo y borrado del registro de accesos** (incluida la IP). Sin él, Plataforma no usa datos reales (DPO). Está pendiente en `docs\privacidad\antes-de-datos-reales.md`.
- [ ] **Familia en el recuento de cuentas de Plataforma.** Por defecto queda fuera.
- [ ] **Umbrales de aviso.** Antigüedad de un episodio abierto para avisar a Dirección, y plazo para dar por pendiente una reevaluación de basal (CJ).
- [ ] **Uso por módulo en Plataforma.** El DPO admite contadores de uso por centro y por módulo, sin cuenta, si la ficha del módulo y el contrato los mencionan; decidir si se quieren cuando haya más de un módulo.
- [ ] **Correos y errores en Plataforma.** Hoy no se muestran. Hacerlo exigiría un procedimiento de base de datos que devuelva solo recuentos (cambia el ADR 0008 y necesita al DPO) o mirar la telemetría de Azure fuera de la app. La retención de esos registros ya está pendiente con el DPO.
- [ ] **Vista consolidada.** Las cuentas con varios centros ven cada centro por separado; una vista conjunta sería un módulo distinto, decisión de producto y de privacidad aparte (ADR 0008, lectura del DPO).

## Referencias

- `docs\decisiones-arquitectura\0006-perfil-plataforma.md`, `0008-multicentro-y-grupos.md`, `0015-modulos-opcionales.md`.
- `docs\privacidad\modulos-opcionales-lectura-dpo.md` (apartado 6, cautelas de agregación) y `anexo-modulos-contrato-borrador.md` (4.3).
- `docs\privacidad\dashboards-lectura-dpo.md`: lectura del DPO sobre esta especificación (borrador; la valida una persona antes de usarlo con datos reales).
- `docs\bocetos-pantallas\guia-diseno-sistema-visual.md` y `docs\decisiones-arquitectura\directrices-pwa-movil.md`.
- `docs\producto\alcance.md` y `docs\historias-usuarios\direccion-coordinacion-clinica.md` (DIR-08, DIR-09).
