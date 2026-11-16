# ADR 0015 — Módulos opcionales por centro: catálogo, activación por Plataforma y qué pasa al apagar uno

Estado: **aceptada en lo técnico para los incrementos 1 a 4 y 6** (2026-11-16, tras la segunda lectura de `dpo-residapp`; el uso con un centro real queda condicionado a la lista de `docs/privacidad/antes-de-datos-reales.md`, apartado 13, como en el ADR 0016). Propuesta del 2026-11-09. Los incrementos 1 y 2 (script `0098`), el 3 (el dictado como módulo `DICTADO_VOZ`) y el 4 (pantalla de Plataforma; script `0099`) están construidos; el 6, no.
**Decidido por Orlando el 2026-11-16:** (1) alcance: incrementos 1 a 4 y 6, **sin el 5** (pantalla de solo lectura de Administración); (2) **solo Plataforma enciende y apaga, a petición del centro** (el anexo fija en cuánto tiempo se atiende), sin apagado de emergencia global (para el dictado, quitar la clave `IA__Mistral__ApiKey` ya lo corta); (3) al vencer la vigencia el módulo **pasa solo a solo lectura**, calculado al consultar y sin tarea programada, y la pantalla de Plataforma enseñará los vencimientos próximos; (4) la **política de privacidad** se corrige con el incremento 6, no antes.
**Decidido por Orlando el 2026-11-09:** habrá **varios módulos opcionales** que se ofrecen aparte y
influyen en el precio; la **exportación pasa a ser base**; el **dashboard de Plataforma no es un módulo** (es una funcionalidad de Plataforma); si un centro deja de tener un módulo, lo ya
registrado queda en **solo lectura**; en el entorno de desarrollo se podrán activar todos los módulos. **Cada módulo es una decisión de compra de la Residencia: se define en el contrato (en un anexo de módulos) y supone un pago diferenciado.** Todo lo demás está sin decidir salvo lo marcado como **[Hecho]**.
Lectura de protección de datos previa, que este ADR recoge: `docs/privacidad/modulos-opcionales-lectura-dpo.md` (borrador del agente `dpo-residapp`; lo valida una persona antes de usarlo con datos reales). Su conclusión es
que el diseño es **admisible con condiciones**: activar un módulo es una decisión sobre el tratamiento de datos del **centro** (el responsable), y ResidApp, como encargada, solo la ejecuta (RGPD art. 28.3.a y 28.10).

Etiquetas: **[Hecho]** verificado en el repositorio; **[Propuesta]** lo que recomienda este ADR; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no se ha comprobado.

## Contexto

ResidApp se plantea como servicio por suscripción: un plan según el censo del centro y **módulos opcionales cobrados aparte** (decisión de Orlando del 2026-10-29, recogida en
`docs/tareas/futuro/pendientes-landing.md`). CJ contestó el 2026-10-30 (`docs/pendientes-cj/modulos-base-y-opcionales-cj.respuestas.json`, 16 de 16) que todo es base salvo el dictado por
voz y la exportación; Orlando ha decidido después que la exportación también es base.

Lo que existe hoy **[Hecho]**:

- **No hay módulos por centro.** Ni catálogo, ni tabla, ni comprobación. La tabla `centros` solo tiene `id`, `codigo`, `nombre_visible`, `estado` y `creado_en`.
- **El dictado por voz se enciende para todos los centros o para ninguno**, con la clave `IA__Mistral__ApiKey` de la Web App (`IaOptions.Activa`). Sin clave, el servidor responde 404
  (`AuxiliarController.Dictado.cs`, línea 29) y la sección desaparece de la pantalla. Solo está autorizado el proveedor Mistral (ADR 0011) y la configuración inválida impide arrancar.
- **El correo** se enciende también de forma global (`Correo__Activo`, ADR 0012). No es un módulo: es una función de la plataforma.
- **Configuración por centro que ya existe**, pero que no son módulos: `permisos_perfil` (qué permisos concede Administración a cada perfil, `ProfilePermissions`) y `citas_configuracion_centro`.
  Un permiso decide **quién dentro del centro** puede hacer algo; un módulo decide **si el centro tiene esa función contratada**.
- **Plataforma** (ADR 0006) ya es quien opera ResidApp: vive en el centro reservado `PLATAFORMA`, no ve residentes ni contenido clínico y ya escribe en otros centros (alta de centros,
  suspensión de cuentas multicentro) con auditoría sin datos. El script de este ADR es el **0098** (el 0092 a 0097 se ocuparon antes de construirlo).
- **La seguridad por filas** (ADR 0008, script 0030) cubre las tablas de datos por `centro_id`; `CoberturaRlsTests` obliga a que cada tabla nueva esté cubierta o figure como excepción con motivo.
- **Dirección Clínica ya tiene** las pantallas Indicadores y Calidad de proceso, detrás del permiso `QUALITY_REVIEW` (`DireccionController`). Son base hoy.
- **La comprobación del dictado está en el controlador**, no en el caso de uso, y el centro de la cookie del ámbito activo no es de fiar (ADR 0008, punto 5). Un módulo por centro debe
  comprobarse con el centro verificado, no con la cookie.
- **La política de privacidad ya promete lo que aún no existe:** `Privacy.cshtml` (apartado 6 y la lista de proveedores) dice que el dictado y el correo funcionan «solo si el centro lo activa», y hoy
  se encienden por entorno, no por centro.
- **La exportación actual es solo de estructura y censo**, con límites de 5 MB y 2.000 filas (ADR 0010). No devuelve datos asistenciales.
- **El alcance excluye hoy la administración de medicación** (`docs/producto/alcance.md`, línea 45): un módulo de Medicamentos que la gestione lo contradice y solo CJ puede reabrirlo. La ficha informativa de alergias, enfermedades y medicación habitual **no** es ese módulo: va en la base (decisión de Orlando, 2026-11-15; lectura del DPO en `docs/privacidad/ficha-alergias-enfermedades-medicacion-lectura-dpo.md`).

Lo que se quiere poder hacer: ofrecer una función a un centro y no a otro, cobrarla, activarla sin tocar el código ni los ajustes de la Web App, y que un módulo nuevo nazca ya con su
interruptor, sin que alguien tenga que acordarse de ponerlo en cada pantalla.

## Qué es base y qué es módulo

- **Base:** lo tiene todo centro. Es todo lo que ya existe, incluida la exportación y el correo.
- **Módulo opcional:** una función que un centro puede no tener contratada. Hoy existe uno: **Dictado por voz**. Candidatos de Orlando, sin diseñar: **Dashboard de Dirección** (resúmenes
  gráficos agregados del estado de una parte de la app) y, más adelante, **Medicamentos**.
- **No es un módulo:** el dashboard de Plataforma (funcionalidad del proveedor para vigilar el desempeño de la app; se propondrá aparte), el correo, ni los permisos de perfil.
- **[Decidir]** Cómo se reparte entre las pantallas de Indicadores y Calidad (base hoy) y el futuro Dashboard de Dirección: si lo que ya existe sigue siendo base y el módulo añade gráficos nuevos,
  o si parte de lo actual pasa al módulo. No se decide aquí.

## Decisión **[Propuesta]**

1. **Catálogo de módulos en código**, con el mismo estilo que `ProfilePermissions`. Cada módulo declara:
   - su **código** estable (por ejemplo `DICTADO_VOZ`) y su nombre en pantalla;
   - de qué **otros módulos depende** (un dashboard no tiene sentido sin los datos que resume);
   - la **condición previa de privacidad** que debe cumplirse antes de activarlo en un centro real (por ejemplo, el contrato con el proveedor y la evaluación de impacto del dictado), con
     enlace al documento;
   - su **regla al apagarse**, que hoy es única: solo lectura.
   La condición previa es una **ficha de privacidad versionada** por módulo, no una frase: qué datos trata, qué proveedor interviene, qué contrato y qué evaluación de impacto exige y qué
   se conserva al apagarlo.
2. **Tabla `modulos_centro`** (script 0098) **bajo la seguridad por filas, no en `centros`** (que está exenta). **[Hecho 2026-11-16]** Es **una sola tabla de solo añadir**: cada fila es un cambio (alta,
   renovación o baja) y el estado vigente de un módulo en un centro es el de su último cambio (el de mayor `numero`). Como Plataforma no hace UPDATE ni DELETE, «apagar» es insertar una fila
   `DISABLED` (así no hay una tabla de estado y otra de historial que puedan discrepar; disparadores `INSTEAD OF UPDATE, DELETE` y secuencia sin huecos, como `administraciones_principales_cambios`).
   Cada fila guarda, **sin datos de residentes ni texto libre**: centro, módulo, estado, **vigencia** (`vigente_desde` y `vigente_hasta`, obligatorias al activar: el contrato fija la duración,
   RGPD art. 28.3; renovar es una fila nueva, nunca editar fechas), **referencia y versión del anexo**, **versión de la ficha** que el centro aceptó y los **códigos** de las condiciones comprobadas (no su texto),
   un motivo de baja de lista cerrada, **cuándo recibió Plataforma la petición del centro, por qué canal (lista cerrada) y con qué referencia opaca** (sin esas tres marcas no se probaría el plazo de apagado que
   fije el anexo), y la cuenta y el perfil que ejecuta con la fecha. Si la ficha cambia de forma sustancial, el centro la vuelve a aceptar (se activa otra vez con la versión nueva). Un módulo que nunca se ha activado no tiene fila.
   **La referencia de la petición del centro la asigna ResidApp, no se teclea** (decidido por Orlando, 2026-11-16, tras una primera versión en la que la tecleaba Plataforma y no se repetía por centro y módulo): es el código del módulo y el número del cambio (`DICTADO_VOZ-0003`), único por construcción por centro y módulo, y la pantalla la devuelve en el aviso para que Plataforma la anote en su respuesta al centro. Así no puede colarse un nombre en un historial que no se puede corregir. El índice único `UX_mc_referencia` del script `0099` se queda como segunda barrera; las filas anteriores a este cambio conservan la referencia tecleada.
   **Renovar** es una alta nueva que **no puede empezar en el futuro ni nacer ya vencida** (la tabla es inmutable y un año mal tecleado no se podría corregir; la hora de la solicitud no puede ser de hace más de 366 días) (como manda el último cambio, una renovación con `vigente_desde` futura apagaría hoy un módulo aún vigente): se registra el día en que empieza o antes.
   **Apagar se puede siempre**, también en un centro inactivo; activar, solo en un centro activo. «Hoy» es el **día de Madrid** (decidido por Orlando, 2026-11-16) y `vigente_hasta` vale hasta el final de ese día.
   La fila lleva datos personales seudonimizados del operador y se conserva lo que exija la prueba contractual, no los 15 años clínicos (plazo sin decidir; criterio de partida en `antes-de-datos-reales.md`, apartado 13).
   Plataforma entra con un salto de **lectura e inserción**, nunca UPDATE ni DELETE, como ya hace con `unidades` (ADR 0008, actualización 2026-11-04): eso **modifica el ADR 0008** (Plataforma lee e inserta
   en `modulos_centro` de cualquier centro, solo con su cuenta y su perfil; la Administración del centro no inserta) y añade la tabla a la matriz de permisos del script 0091 (solo INSERT, con DENY de UPDATE y DELETE).
   `CoberturaRlsTests` queda en verde.
3. **Tres estados para el usuario**, calculados por una única consulta («¿tiene este centro este módulo?»):
   - **No disponible** (sin fila): la tarjeta o el botón no se ven y el servidor responde 404, igual que el dictado hoy sin clave.
   - **Activo:** funciona entero.
   - **Solo lectura** (módulo apagado o vencido con datos ya registrados; **el dictado no tiene datos propios, así que para él solo lectura equivale a no disponible**, y el catálogo lo declara con `HasOwnData`): se ocultan crear y editar, y siguen la consulta y la exportación de lo registrado. **Decidido por Orlando.** Los intentos
     de crear o editar por URL directa se rechazan en el servidor con un mensaje claro, no solo se ocultan en pantalla. Cuidado (DPO): «exportar» solo es cierto si la exportación cubre los
     datos del módulo, y hoy solo cubre estructura y censo. Por eso **un módulo no guarda datos reales de residentes hasta que tenga definida su salida** (consulta y copia), y el fin de
     contrato (RGPD art. 28.3.g) exigirá una exportación completa que hoy no existe.
4. **Quién enciende, quién apaga y con qué respaldo.** El módulo nace de una **compra recogida en el contrato con la Residencia** (anexo de módulos, con pago diferenciado). Ese anexo es,
   en principio, la **instrucción documentada del centro** (el responsable de los datos) que pide el DPO, y Plataforma solo la ejecuta: **no se enciende un módulo con residentes reales
   sin contrato firmado que lo incluya** (una prueba gratuita o una activación comercial sin él sería un tratamiento sin instrucción). Cada fila de `modulos_centro` guarda la **referencia del
   anexo y su versión** y una **vigencia** (`vigente_desde`, `vigente_hasta`); **pasada la vigencia, el módulo pasa solo a solo lectura**, sin que nadie tenga que acordarse. **[Decidir]** que
   un abogado o el DPO real confirme que el anexo vale como instrucción. Cada cambio queda en la auditoría sin datos (por
   ejemplo `CENTER_MODULE_ENABLE` y `CENTER_MODULE_DISABLE`, con el centro y el código del módulo). Activar un módulo **no concede permisos a ningún perfil**: eso sigue siendo de la Administración
   del centro. Además:
   - **[Decidido por Orlando, 2026-11-16, con condiciones]** **Solo Plataforma enciende y apaga, a petición del centro y en el plazo que fije el anexo.** El centro no apaga solo y no hay
     apagado de emergencia global (el DPO había propuesto lo contrario; su lectura posterior lo da por superado). Como el riesgo pasa al retraso (cada hora entre la petición y el apagado es tratar
     contra una instrucción, art. 28.3.a), el historial guarda cuándo llegó la petición, por qué canal y con qué referencia; el apagado tiene **efecto inmediato en el servidor** (una consulta por
     petición, sin caché, y se rechaza también lo que estaba abierto al guardar) y hace falta un **suplente de Plataforma** y un procedimiento escrito. Queda para un abogado confirmar que este reparto
     concilia la baja comercial con el art. 28.3.a.
   - **El centro ve sus cambios de módulos en su auditoría** (`CENTER_MODULE_ENABLE` y `CENTER_MODULE_DISABLE`, con el nombre del módulo y sin datos de residentes); **no hay pantalla de módulos para la
     Administración** (incremento 5 descartado), así que Plataforma avisa por escrito a la persona autorizada del centro en cada alta, baja y vencimiento próximo. Plataforma sigue sin leer la auditoría ajena (ADR 0008).
5. **Dependencias:** no se activa un módulo si falta alguno de los que requiere. Apagar uno que otro necesita lo deja a ese otro en solo lectura o sin datos útiles: la regla exacta se fija
   cuando exista el primer módulo con dependencias.
6. **El dictado conserva su interruptor maestro:** la clave global de Mistral. El módulo por centro solo se puede activar si la clave existe, y sin clave nadie lo tiene. Así el contrato y la
   región del proveedor siguen controlados en un solo sitio. **Como no hay apagado global dentro de la aplicación, quitar la clave es el único corte para todos los centros a la vez:** con la clave
   ausente, la consulta de estados trata los módulos con tercero como «no funcionan» aunque la fila diga ACTIVO, y cada módulo con tercero declara en su ficha **cómo se corta para todos y quién lo hace**
   (un módulo sin corte documentado no se activa). Falta el procedimiento escrito (quién, cuánto tarda, cómo se vuelve a poner, quién lo hace si Orlando no está). Condición bloqueante de su ficha (DPO): como la clave es única para todos los centros, Mistral no puede borrar por centro; hace
   falta la **retención cero por escrito** antes de activarlo en un centro real. ResidApp no guarda audio ni transcripción, así que al apagarlo no queda nada propio en solo lectura.
7. **Desarrollo con todo activo:** un ajuste de ejecución (por ejemplo `Modulos__ActivarTodos`), no filas sembradas. Solo vale si el entorno **declara no ser producción** y **un entorno desconocido se trata como producción**.
   **[Hecho 2026-11-16]** La marca es `ASPNETCORE_ENVIRONMENT=Development` o, en la Web App de dev, `Entorno__Tipo=Desarrollo` (`ModuleOptions.Validate`); sin ninguna de las dos, la aplicación **no arranca** con el ajuste puesto,
   igual que `IaOptions.Validate` rechaza una configuración inválida. **Queda sin hacer la marca de entorno «también en la base de datos»** de la propuesta (protegería de apuntar por error una aplicación declarada de desarrollo a la base de
   producción): se decide cuando exista el entorno de producción (ADR 0014). El ajuste aun así no salta la clave global de un módulo con tercero. Así cada módulo nuevo aparece encendido en pruebas, y las pruebas automáticas cubren también el caso «módulo apagado» (con el ajuste desactivado).
8. **Una prueba automática que obliga a poner el interruptor.** Cada acción de un módulo se marca con un atributo (por ejemplo `[RequiereModulo("DICTADO_VOZ")]`) y un test por reflexión falla
   si un controlador o una acción que pertenece a un módulo del catálogo no lo lleva, o si un módulo del catálogo no tiene ninguna acción marcada. Es el mismo estilo de `CoberturaRlsTests`:
   un módulo nuevo no puede quedar sin protección sin que nadie lo decida.
   **La comprobación real va en el caso de uso**, antes de auditar y antes de llamar a un tercero, y con el centro verificado (no el de la cookie); el atributo del controlador solo oculta y
   orienta. Hoy el dictado se comprueba en el controlador.
9. **Los importes no entran en la aplicación.** El módulo es solo el interruptor técnico. Planes, precios y topes por censo no se construyen aquí, y la landing no anuncia lo que todavía no
   existe (condición anotada en `pendientes-landing.md`). Facturar por censo implica contar residentes con datos del centro: hay que preverlo como instrucción del centro.
10. **Antes de activar nada en un centro real, la política de privacidad debe decir la verdad:** las frases «solo si el centro lo activa» de `Privacy.cshtml` son ciertas únicamente cuando exista
    este interruptor por centro. **[Decidido por Orlando, 2026-11-16]** Se corrige con el incremento 6, no antes. Cuidado: la frase del dictado se vuelve cierta con el incremento 3, pero la del **correo** (`Privacy.cshtml`, «Solo interviene
    si el centro los tiene activados») no la vuelve cierta ningún módulo (el correo es base y se enciende por entorno): el incremento 6 debe reescribirla. El anexo de subencargados lista a Mistral como condicional.

## Alternativas descartadas

- **Un interruptor suelto por módulo en los ajustes de la Web App** (como el dictado hoy): sirve para todos los centros a la vez, no por centro, y obliga a reiniciar la web para cambiarlo.
- **Usar los permisos de perfil como módulos:** mezcla «quién puede» con «qué está contratado», y los concede la Administración del centro, no quien factura.
- **Una columna por módulo en `centros`:** cada módulo nuevo sería un cambio de esquema, y no guardaría quién lo activó ni el histórico.
- **Ocultar todo al apagar un módulo:** deja a un centro sin ver su propia historia clínica durante la conservación obligatoria y choca con el derecho de acceso.
- **Reducirlo a un interruptor solo para el dictado:** vale mientras haya un módulo; se descarta porque Orlando ha confirmado que habrá más.

## Consecuencias

- Un módulo nuevo se da de alta con una entrada del catálogo, sus acciones marcadas y, si lleva datos, su script; la prueba guardia impide olvidar el interruptor.
- Apagar un módulo por impago deja los datos consultables y exportables, pero no editables. Hay que redactar el contrato con los centros en ese sentido (derecho de acceso y 15 años de
  conservación, Llei 21/2000) **[Decidir]**.
- Activar un módulo en un centro real puede ser un cambio de tratamiento (nueva finalidad, nuevo encargado, nueva evaluación de impacto). La ficha del catálogo lo recuerda, pero **lo decide una persona**
  (DPO real o abogado) antes de la primera activación en producción, y el centro (responsable) es quien da la instrucción.
- Plataforma gana una pantalla y un repositorio nuevos, y amplía lo que escribe (no lo que ve): sigue sin ver residentes ni contenido clínico.
- El entorno de pruebas ya no refleja un centro con módulos apagados salvo que se desactive el ajuste. Las guías de CJ se prueban con todo activo y conviene decírselo.

## Puertas abiertas

1. **[Decidir]** Reparto entre Indicadores y Calidad (base) y el Dashboard de Dirección (módulo).
2. **[Decidido por Orlando, 2026-11-16]** Solo Plataforma apaga, a petición del centro y sin apagado de emergencia global (la propuesta del DPO era la contraria). Queda para un abogado confirmar que este reparto concilia la baja comercial con el art. 28.3.a. Con la compra por contrato,
   la baja comercial de un módulo seguiría el contrato (aviso y fin del periodo pagado), pero el responsable de los datos no puede quedar sin poder dejar de tratarlos cuando quiera: cómo se concilian
   ambas cosas lo decide un abogado o el DPO real.
3. **[Decidir]** Que el anexo de módulos del contrato valga como «instrucción documentada del centro», qué debe decir (módulo, versión de la ficha de privacidad, vigencia) y quién lo valida (DPO real o abogado).
4. **[Decidir]** Qué ocurre con las dependencias al apagar un módulo requerido por otro.
5. **[Decidir]** Plazos de conservación del histórico de activaciones (prueba contractual, no los 15 años clínicos) y de las fichas aceptadas. Criterio de partida: la duración de la activación más el plazo de prescripción que interese (LOPDGDD arts. 72 a 74); sin decidir (`antes-de-datos-reales.md`, apartado 13).
6. **[Decidir]** Retención cero por escrito con Mistral, condición para activar el dictado en un centro real.
7. **[Decidir]** Cláusula del contrato con los centros sobre el solo lectura al apagar un módulo y sobre la exportación completa al terminar el contrato (art. 28.3.g), que hoy no existe:
   la exportación actual es solo de estructura y censo.
8. **[Decidido por Orlando, 2026-11-16]** Se corrigen con el incremento 6, no antes (incluida la frase del correo, punto 10).
9. **[Decidir]** Planes, precios y topes por censo, cómo se cuenta el censo para facturar y cuándo se anuncian en la landing. La facturación de cada módulo vive fuera de la aplicación (en el
   contrato); la aplicación solo guarda la referencia del anexo y la vigencia. Para fijar precios y márgenes de los módulos de pago por uso (dictado) sirven contadores por centro y módulo,
   sin cuenta (idea de «Costes y consumo en Plataforma», `docs/producto/Especificación de dashboards de ResidApp.md`, decisiones abiertas); no hay nada construido.
10. **[Hecho en el PR 1, 2026-11-16]** Encaje de `modulos_centro` con la seguridad por filas y con la escritura de Plataforma (cambia el ADR 0008 y la matriz del script 0091).
11. **Comprobado el 2026-11-12 contra el texto literal de la base normativa local** (consolidados, no versión oficial): art. 28.2, 25.2, 15.3 y 9.2.h (con el 9.3 y el 6.4) del RGPD; lista del art. 35.4 de la AEPD (los puntos 4, 7 y 9 se cumplen para ResidApp, y bastan dos criterios para presumir EIPD; la APDCAT adopta los mismos criterios, guía de AIPD de 2025); la Llei 2/2024 y la Ley 10/2025 solo tocan el art. 8.2 (voluntades anticipadas) de la Llei 21/2000, el art. 12 (conservación) sigue en la redacción de la Ley 16/2010 (consolidado del BOE actualizado el 2026-02-16).
    **Sigue abierto, y lo decide el DPO o un abogado:** ámbito de la APDCAT frente a la AEPD para el centro piloto. Depende de la titularidad (pública, concertada o privada), del concierto con el Departament y de si todas las plazas lo están (Llei 32/2010, art. 3; Llei 12/2007, art. 2.2).

### Cautelas anotadas para los módulos futuros

- **Dashboard de Dirección** (se diseñará aparte, orientación del DPO): cifra mínima con supresión complementaria para que no se pueda deducir una celda restando (la cifra N la fijan el DPO y CJ);
  sin salto del gráfico a la persona; sin desglose por trabajador; los mínimos se aplican también en el servidor al exportar y no hay copias derivadas. El DPO observa que los indicadores de Dirección
  que ya son base (`ISupervisionDirectory`) no muestran ninguna regla de cifras pequeñas en el código que leyó; conviene revisarlo aparte.
- **Medicamentos** (se diseñará aparte): **no se activa mientras no se cumpla todo esto.** Más allá de la ficha informativa de medicación habitual (que va en la base), contradice hoy el alcance y la política de privacidad, y solo CJ puede reabrirlo; puede ser un producto
  sanitario si ayuda a decidir tratamientos (reglamento europeo 2017/745, **a verificar**); hay que fijar la fuente única de verdad frente a las indicaciones de Medicina y los sistemas oficiales; es
  incompatible con el dictado hasta que exista un ADR nuevo; la lista de fármacos revela diagnósticos y no debe verse en el Portal Familiar; y un listado desactualizado en solo lectura es un riesgo clínico (estas cautelas ya aplican a la ficha de medicación de la base).

## Incrementos (1, 2, 3 y 4 hechos el 2026-11-16; 6 aceptado, sin construir; 5 descartado)

| # | Qué | Depende de |
|---|-----|------------|
| 1 | Catálogo en código con la ficha de privacidad versionada, tabla `modulos_centro` (0098, de solo añadir), consulta única y ajuste de desarrollo con todo activo. Sin pantalla: la activación es un caso de uso de Plataforma con su auditoría en la misma transacción, no SQL suelto (los módulos con tercero no se activan por SQL). El visto bueno del DPO real es condición para **usarlo con un centro real**, no para construirlo | — |
| 2 | Atributo `[RequiereModulo]`, comprobación en servidor y en pantalla, estado de solo lectura y prueba guardia | 1 |
| 3 | **Hecho (2026-11-16).** Mover el dictado al módulo `DICTADO_VOZ` (la clave de Mistral sigue como interruptor maestro) | 2 |
| 4 | **Hecho (2026-11-16).** Pantalla de Plataforma para activar y desactivar módulos por centro, con auditoría sin datos | 2 |
| 5 | ~~Pantalla de solo lectura para Administración~~ **descartado (Orlando, 2026-11-16)** | — |
| 6 | Documentación de usuario y de Plataforma (manual, guía por perfil) y corrección de la política de privacidad | 3 y 4 | **[Hecho 2026-11-16]** (PR 4)

El dashboard de Plataforma, el Dashboard de Dirección y Medicamentos tienen cada uno su propia propuesta y no forman parte de estos incrementos.
