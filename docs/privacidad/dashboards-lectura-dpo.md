# Especificación de dashboards: lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-10. Segunda lectura experta del agente `dpo-residapp`, no un dictamen. Lee `docs/producto/Especificación de dashboards de ResidApp.md` (en adelante, «la especificación»). No modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado leyendo el repositorio (archivo y línea); **[Opinión]** mi criterio, discutible; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no confirmado. No cito de memoria artículos de la LOPDGDD ni plazos: donde hace falta, queda como «a verificar».

## Conclusión

**Admisible con condiciones.** La especificación ya incorpora casi todas las cautelas de mi lectura de módulos (sin foto diaria, sin enlace del gráfico al caso, sin cifras por trabajador, calcular al abrir). Mi opinión por pregunta:

| Pregunta | Veredicto |
|---|---|
| 1. Gráficos de Administración | Admisibles. No son una finalidad nueva que exija un análisis de compatibilidad aparte; sí exigen **una línea en el registro de actividades del centro** y una frase en su información. |
| 2. Dashboard de Plataforma | Admisible **con datos de prueba ya**; con datos reales, **solo cuando estén decididos el plazo de `eventos_acceso` y la cifra mínima**. Son dos bloqueos que la especificación reconoce solo a medias. |
| 3. Sin copias + caché de 1 a 5 min | Lo primero basta. La caché es aceptable si se cumplen seis condiciones (apartado 3); la clave que propone la especificación no basta por sí sola. |
| 4. Avisos de Dirección | Aceptables como **recuentos del estado actual** que enlazan a pantallas que ya muestran los casos. **No** aceptables si derivan en nuevos cruces por unidad y periodo mientras `ISupervisionDirectory` siga sin regla de cifras pequeñas. |
| 5. Cautelas que faltan | Siete (apartado 5). La más importante: imprimir y guardar como PDF son exportación de hecho. |

## 1. Gráficos de Administración: ¿finalidad nueva?

**[Hecho]** Administración ya ve de forma nominal las bajas con su motivo y el texto de «Otro motivo» (`src/ResidApp.Web/Views/Administracion/Bajas.cshtml:57`). Los cuatro códigos de motivo son `FALLECIMIENTO`, `ALTA_VOLUNTARIA`, `TRASLADO_OTRO_CENTRO` y `OTRO` (`database/scripts/0041_baja_y_suspension_residente.sql:30`). La especificación excluye el texto libre (línea 69) y declara que el dashboard no amplía lo que cada perfil ve (línea 149).

**[Opinión] No es una finalidad nueva a efectos del art. 6.4.** Mismo actor, mismos datos, misma finalidad de fondo (gestión administrativa y de ocupación del centro), con menos detalle que la lista. El análisis de compatibilidad (vínculo entre fines, contexto, naturaleza de los datos, consecuencias, garantías) sale a favor: no hay categoría nueva y la agregación es una garantía. **[Decidir]** El DPO real lo confirma por escrito en una línea; no hace falta EIPD propia. (Art. 6.4: lo leí solo en parte el 2026-11-09, **[A verificar]** el texto completo.)

Lo que sí hay que hacer, porque el responsable es la residencia y no ResidApp:

1. **Registro de actividades del centro (art. 30.1).** La actividad «gestión administrativa de residentes» debe citar «elaboración de cifras internas de gestión (ocupación, altas y bajas, citas, autorizaciones)». Es una frase, no una ficha nueva.
2. **EIPD general del centro.** Anotar el dashboard como parte del tratamiento ya evaluado. No sustituye a las EIPD pendientes (voz, NFC).
3. **Información a residentes y familiares (art. 13).** La cláusula de gestión administrativa debe cubrir «estadística interna». **[A verificar]** que la plantilla de `docs/privacidad/` ya lo diga.
4. **Información al personal.** No hay datos del personal en los gráficos de Administración, así que no hace falta aviso específico. Sí lo hay en Plataforma y en los avisos de Dirección (apartados 2 y 4).

**Dos cautelas propias de este dashboard:**

- **[Opinión] Ámbito.** Una Administración con ámbito limitado a algunas unidades no debe ver totales del centro entero. La especificación lo dice (línea 142), pero «ocupación por edificio» y «residentes activos» son los puntos donde se suele escapar: el total del edificio incluye unidades fuera de ámbito. Prueba concreta: cuenta con una unidad, edificio con dos; el total debe ser el de su unidad.
- **[Opinión] Evolución de autorizaciones.** Sale del historial de cambios (línea 71). Son datos de familiares; la base jurídica y el plazo de los datos de familiares y del Portal Familiar siguen abiertos y este gráfico no los resuelve. Que no se use como argumento para fijar un plazo.

La afirmación de la línea 160, «ocultar una cifra pequeña a quien ya ve las listas no protege a nadie», es **[Opinión]** correcta para la pantalla, pero no para lo que sale de ella (apartado 5, punto 1).

## 2. Plataforma: ¿es admisible mostrar accesos y cuentas?

### Hechos verificados

- **[Hecho]** `eventos_acceso` guarda `cuenta_id`, `tipo`, `sesion_id` e `ip` (`database/scripts/0062_credenciales_y_eventos_acceso.sql:32-38`); es inmutable por disparadores (`:49-55`); es el «registro de seguridad de la autenticación» (`:9`). **No guarda el centro**. La especificación dice que guarda «fecha y tipo» (línea 138): es incompleto, porque la tabla fuente sí contiene cuenta e IP; lo que se publica son recuentos.
- **[Hecho]** El plazo de `eventos_acceso` sigue sin decidir (`docs/privacidad/antes-de-datos-reales.md:131`) y cualquier purga futura exige un procedimiento aprobado (`:160`). La tabla está sin seguridad por filas por diseño (`0062:12`; `tests/IntegrationTests/CoberturaRlsTests.cs:21`), igual que `invitaciones_acceso` y `credenciales` (`CoberturaRlsTests.cs:19, 22`). Por eso la consulta de Plataforma puede leerlas sin ampliar permisos, como dice la especificación (línea 107). El rol de la aplicación solo tiene restringidas las escrituras (`database/scripts/0091_permisos_aplicacion.sql:83`: `eventos_acceso` sin UPDATE ni DELETE); la lectura viene de `db_datareader` (`0091:9`).
- **[Hecho]** El bloqueo por intentos vive en `credenciales.bloqueada_hasta` (`0062:23`; `src/ResidApp.Infrastructure/Identity/SqlCredentialStore.cs:24`). Esa tabla contiene también el hash de contraseña y la clave TOTP en claro (`0062:18, 20`).

### Opinión por elemento

| Elemento (línea de la especificación) | Valoración |
|---|---|
| Entradas correctas y fallidas por día, global, sin cuenta ni IP (124) | **Admisible como finalidad.** Vigilar intentos de acceso y disponibilidad del servicio es seguridad (art. 32); es la razón por la que existe la tabla (`0062:9`). **Condición:** que el encargo de ResidApp lo diga («seguridad y disponibilidad del servicio»); si se usara para medir actividad con fines comerciales sería un fin propio (art. 28.10, ver lectura de módulos, apartado 9.2). **Riesgo real:** con pocos usuarios, «1 entrada ese día» es una persona. Es el caso del piloto. Ver cifra mínima abajo. |
| Cuentas bloqueadas ahora, solo recuento (115) | **Admisible.** Un número sin nombres. Que la consulta devuelva un `COUNT` y no lea ninguna otra columna de `credenciales` (hay hash y clave TOTP en claro, puerta abierta). |
| Cuentas por perfil y estado, por centro (122) | **Admisible.** `cuentas` y `ambitos_perfil` no tienen política por filas (`0008-multicentro-y-grupos.md:68`) y Plataforma ya las gestiona. Dos matices: (a) restar totales puede revelar celdas pequeñas, mismo asunto que la cifra mínima; (b) **[Decidir]** si el perfil Familia entra en el recuento: contar familias con acceso por centro roza el Portal Familiar, cuya base está abierta. Mi propuesta: solo personal. |
| Invitaciones caducadas por centro (113) | **Admisible.** `invitaciones_acceso` tiene `centro_id` (`0063_invitaciones_acceso.sql:17`) y no tiene seguridad por filas; recuento, sin nombres. |
| Passkeys por centro (125) | **Admisible**, con la salvedad que ya hace la especificación (dónde se guardan). Indicador de seguridad, no de personas. |

### Condiciones antes de datos reales

1. **Plazo de `eventos_acceso`.** **[Decidir]** El gráfico no puede condicionarlo. El selector de «12 meses» (línea 43) promete un histórico que quizá el DPO acorte. **[Opinión]** Para accesos, ofrecer 7 y 30 días y nada más; si el plazo es menor, el gráfico se adapta.
2. **Cifra mínima.** La especificación la declara «obligatoria» para Plataforma (línea 160) y la deja abierta (línea 192), pero el dashboard de Plataforma de la fase 4 no la aplica. **[Opinión]** O se decide antes de la fase 4 con datos reales, o accesos se agrupa por semana mientras haya pocos centros. Con datos de prueba, sin condición.
3. **Granularidad.** Por día y nunca por hora ni por franja (la hora de entrada es el turno de una persona). No añadir «por centro» a los accesos cruzando `cuenta_id` con `ambitos_perfil`: sería, indirectamente, un registro de presencia del personal por centro. La especificación ya dice que el registro no guarda el centro (línea 124); que se rechace en revisión cualquier consulta que haga ese cruce.
4. **Contrato del puerto.** La consulta que sirva a Plataforma debe devolver solo `(día, tipo, recuento)`, de modo que un cambio posterior no pueda mostrar cuenta o IP sin cambiar su tipo de resultado. **[Opinión]** Es mejor garantía que «no se muestra».
5. **Qué cuenta como «fallida».** Fijar la lista cerrada de tipos (por ejemplo `CONTRASENA_FALLIDA`, `SEGUNDO_FACTOR_FALLIDO`, `PASSKEY_FALLIDA`, `INVITACION_NO_VALIDA`, `BLOQUEO`). **[A verificar]** la lista vigente: el `CHECK` se ha ampliado en varios scripts (`0063` a `0067`). Los intentos con usuario inexistente (`cuenta_id` NULL, `0062:10-11`) cuentan igual, sin mirar la IP.
6. **Información al personal.** La política de privacidad de la app (`src/ResidApp.Web/Views/Home/Privacy.cshtml`) no menciona estadísticas ni cifras agregadas (búsqueda de «estadístic», «agregad» y «gráfic» sin resultados relevantes). **[Opinión]** Añadir en la sección de profesionales: «Con los registros de acceso se calculan cifras globales de funcionamiento y seguridad, sin nombres». El texto sigue en revisión y lo valida el DPO.
7. **Registro de actividades de ResidApp (art. 30.2).** Añadir «monitorización de seguridad y disponibilidad» entre los tratamientos de la encargada.
8. **No auditar la apertura (línea 129).** **[Opinión]** Conforme: el dashboard solo muestra recuentos. Reservo la objeción para el día en que se añada cualquier lista (cuentas con muchos fallos, descartada en la línea 184): eso sí exigiría traza y base propia.

## 3. Sin copias derivadas y caché de servidor

**Sin copias derivadas: basta y es lo correcto.** Evita un almacén nuevo con plazo, borrado y mínimos propios (lectura de módulos, apartado 6.7). Consecuencia que debe constar: al calcular todo al abrir, tras un expurgo o una supresión los gráficos cambian; es aceptable.

**[Hecho]** Hoy no hay caché de servidor: la búsqueda de `IMemoryCache`, `AddMemoryCache`, `OutputCache` y `ResponseCache` en `src/` solo devuelve `NoStore` en `HomeController.cs:53, 59` y un comentario en `SecurityHeaders.cs:40` («nada se queda en la caché de un equipo compartido»). La caché es un componente nuevo.

**Caché de 1 a 5 minutos: aceptable con seis condiciones.**

1. **La autorización se evalúa antes de consultar la caché y nunca se cachea.** La clave de la especificación (centro, perfil, ámbito; línea 143) no cubre una cuenta suspendida, un permiso revocado en `permisos_perfil` ni un ámbito cambiado en los últimos minutos. Sin esto, una cuenta suspendida seguiría viendo cifras hasta cinco minutos.
2. **Clave completa.** Centro **validado por la base, no por la cookie** (`0008-multicentro-y-grupos.md:42`), perfil, conjunto de unidades del ámbito, periodo y, en Dirección, el permiso del bloque.
3. **Solo recuentos.** La caché guarda números, nunca listas, nombres o texto, ni el SVG o HTML ya montado por cuenta.
4. **En memoria del proceso, no distribuida.** Si la Web App se escala a varias instancias y se pasa a una caché compartida (un servicio gestionado), es un componente nuevo con su región y su entrada en el registro: ADR y mi revisión antes.
5. **Sin caché de respuesta HTTP** en navegador ni proxys (se mantiene `NoStore`) y nada en el service worker (ya lo dice la línea 143).
6. **Sin registrar los valores** en logs ni telemetría.

**[Opinión]** Un desfase de hasta cinco minutos no es problema de exactitud (art. 5.1.d) en agregados de gestión. Sí lo es en un aviso que dispare una acción (cuentas bloqueadas); para ese caso, 1 minuto.

## 4. Avisos nuevos de Dirección

**[Hecho]** Los indicadores actuales cuentan por unidad y por mes sin ninguna regla de cifras pequeñas: `SupervisionIndicatorRules.Aggregate` y `Count` aplican el recuento directo a cada unidad y a cada mes (`src/ResidApp.Application/Ports/ISupervisionDirectory.cs:176-185, 188-217`). Releído hoy en esas líneas; no he leído el resto del archivo. El problema se anotó antes (`docs/privacidad/historial-tras-traslado-06-nota-retroactiva-0075-0076.md:32`).

**[Hecho, según documento]** La consulta de Calidad de proceso se audita y la lista de hitos ya no lleva el nombre del residente (`antes-de-datos-reales.md:45, 63`). **[A verificar]** en el código, que no lo he leído.

**[Opinión] Los dos avisos nuevos son aceptables si cumplen esto:**

- **Son un recuento del estado actual**, no una serie por periodo. Un aviso «hay N episodios antiguos» enlaza a una pantalla que ya muestra los casos con sus permisos. La cifra mínima no protege a Dirección de sí misma (línea 160), y por eso es razonable no aplicarla a un aviso.
- **No generan ningún cruce nuevo por unidad y periodo** (unidad × mes × tipo). Eso sí agranda el problema de `ISupervisionDirectory` y debe esperar a que se decida la cifra mínima (línea 192), como ya dice la especificación (línea 95).
- **No se atribuyen a una persona ni a un turno con una sola persona** (línea 161). Un aviso de «episodios sin valorar» es, para quien trabaja en la unidad, una medida de su actividad. **[Opinión]** Debe constar en la información al personal que Dirección ve avisos de estado por unidad. **[A verificar]** con un abogado laboral qué otros trámites exigen los derechos digitales en el ámbito laboral de la LOPDGDD (numeración sin confirmar).
- **El umbral de «episodio antiguo» lo fija CJ antes de construir** (líneas 90 y 193). Sin umbral no hay aviso.
- **«Sin cifra mínima» se anota como excepción justificada**, no como silencio: una línea en el registro del centro («avisos operativos a Dirección sobre casos que Dirección ya puede consultar»).

**[Decidir]** El hecho incómodo debe quedar resuelto **antes de datos reales**, aunque los avisos puedan salir antes: las cifras por unidad y mes siguen ahí con o sin avisos. El arreglo es una regla de cifras pequeñas con supresión de la celda complementaria (lectura de módulos, apartado 6.2); el número lo fijan el DPO y CJ.

## 5. Cautelas que faltan

1. **Imprimir y guardar como PDF son exportación de hecho.** La línea 34 pide impresión y PDF con marca; la línea 162 dice que no hay exportación propia. Son contradictorias. **[Opinión]** El papel o el PDF sale del control de acceso. Para Administración, con cifras sin nombres, el riesgo es bajo; para Dirección y Plataforma con cifras pequeñas, no. Propuesta: la cifra mínima se aplica también al imprimir, y el encabezado de impresión añade «datos agregados, no reidentificar» (lectura de módulos, 6.6).
2. **Rankings de unidades.** Un cuadro con una fila por unidad ordenado por «peor cumplimiento» se lee como ranking de equipos. La especificación ya fija orden fijo en Plataforma (línea 127); extenderlo a Dirección y a la tabla de ocupación.
3. **Centros con la Administración sin primer acceso (línea 114).** Identifica por centro a una persona concreta del cliente. No hay objeción, pero que la pantalla muestre el centro y no el nombre.
4. **Sin lenguaje de vigilancia.** Etiquetas del tipo «cuentas sospechosas» o «actividad anómala» sugieren perfilado de personas. Mantenerlas neutras: «intentos fallidos», «cuentas bloqueadas».
5. **Pruebas de rendimiento (línea 144).** Con datos sintéticos de volumen, nunca con una copia de datos reales.
6. **Futuro: añadir el centro a `eventos_acceso`.** Si algún día se quiere «accesos por centro» (hoy descartado), el cambio de esquema modifica la finalidad de una tabla sin plazo ni borrado; pasa por mí y por el DPO.
7. **Valor oculto.** Cuando se aplique la cifra mínima, el servidor devuelve «menos de N» y no el valor; ni el SVG ni la tabla accesible deben llevar el valor oculto (ni en `title`, ni en atributos, ni en el texto alternativo). La línea 162 («en el servidor, antes de dibujar») va en la buena dirección; añadir la tabla accesible.

## Qué debe validar o decidir una persona

Por orden de importancia:

1. **[Decidir]** Plazo y vía de borrado de `eventos_acceso` (incluida IP). Bloquea datos reales y condiciona el selector de periodo de Plataforma.
2. **[Decidir]** Cifra mínima, tratamiento de la celda complementaria y si se aplica al imprimir. DPO y CJ.
3. **[Decidir]** Que la monitorización de seguridad con `eventos_acceso` figure como instrucción del centro y en el registro de ResidApp (arts. 28.3.a y 30.2).
4. **[Decidir]** Regla de cifras pequeñas para los indicadores actuales de Dirección (`ISupervisionDirectory`), antes de datos reales.
5. **[Decidir]** Umbral de «episodio antiguo» (CJ).
6. **[Decidir]** Si el recuento de Plataforma incluye cuentas Familia.
7. **[Decidir]** Información al personal sobre los avisos de Dirección y las cifras de accesos; revisión laboral.
8. **[Decidir]** Redacción de la línea del registro de actividades y de la cláusula informativa (apartado 1).

## A verificar

- Lista vigente de tipos de `eventos_acceso` tras `0063` a `0067` y posteriores, y los que añada el ADR 0016.
- Que la auditoría de Calidad de proceso y la ausencia de nombres estén en el código, no solo en el documento.
- Art. 6.4 y 28.10 RGPD: leídos solo en parte el 2026-11-09; no los he vuelto a abrir hoy.
- Derechos digitales laborales de la LOPDGDD aplicables a cifras de actividad del personal (numeración).
- Si la APDCAT o la AEPD es la autoridad del centro piloto (abierto).
- Que los cuatro motivos de `0041` coinciden con los del dominio (`ResidentDischarge.cs`, solo leí la línea del enum de fallecimiento).

## Qué he leído y qué no

Leído: la especificación entera; `modulos-opcionales-lectura-dpo.md`; `0062` (cabecera y tabla); `0091:1-120`; `0008:58-71`; `0006` (líneas de Plataforma); `ISupervisionDirectory.cs:140-219`; `Bajas.cshtml:57`; `0041` (motivos); `CoberturaRlsTests.cs` (líneas de tres tablas); búsquedas en `Privacy.cshtml` y `src/` (caché). No leído: el código de los avisos y de «Requiere atención», el de Calidad de proceso, ni `directrices-pwa-movil.md`. No he consultado fuentes externas hoy.

**Soy una segunda lectura, no un DPO designado ni un abogado.** Todo lo anterior lo valida una persona antes de usarlo.
