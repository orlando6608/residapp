# Centro de Día como segunda modalidad de centro: traspaso

**Estado a 2026-11-21: fase 0 hecha (alcance, lectura del DPO y preguntas a CJ). Nada construido: ni esquema, ni ADR, ni código.** Espera a que CJ conteste `docs/pendientes-cj/centro-de-dia-parte-diario-cj.html` (18 preguntas en 5 temas). Las fases 1 a 4 se pueden empezar sin esa respuesta; la 5 no.

Un Centro de Día es un servicio al que las personas van por la mañana y del que vuelven a casa por la tarde: sin camas ni turno de noche, y con una familia que espera saber cada día cómo ha ido (cierre de tarde de 17:00 a 18:00). Hasta ahora estaba fuera de alcance; ya no lo está, salvo su parte diario (ver `docs/producto/alcance.md`). SAAD (atención a domicilio) sigue fuera.

## 1. Decisiones de Orlando (2026-11-19 y 2026-11-21, de una en una)

- **El tipo de servicio es una columna `tipo_de_servicio` en `centros`** (`RESIDENCIA` por defecto, `CENTRO_DE_DIA`), fijada por Plataforma al crear el centro. **No es un módulo del ADR 0015.** Se llama así y no «modalidad» porque «modalidad» ya significa modalidad de cita y reserva directa/solicitud previa, y «tipo» en `docs/privacidad/` es la titularidad (pública, concertada o privada).
- **Inmutable**, pero Plataforma puede **corregir un error de alta solo si el centro no tiene ningún residente**, con motivo de lista cerrada y auditoría sin texto libre (patrón de `zonas_centro`, script `0109`). Con datos, se crea un centro nuevo. El DPO recomienda que la inmutabilidad la imponga un disparador en la base de datos, no solo la pantalla.
- **«Residente» se mantiene** como término del producto. En la fase 3 solo cambian cama, habitación, residencia y turno; no las ~760 líneas de «residente» ni la Guía y el Manual de golpe.
- **Una entidad con residencia y centro de día son dos centros distintos**, cada uno con su tipo. El traslado que existe solo vale entre unidades del mismo centro (`docs/flujos-clinicos/gestion-basal-barthel.md`, `D1-P04`), así que pasar a alguien de un servicio a otro sería un alta nueva en el otro centro, sin historial compartido: **asunto abierto** para el DPO y para CJ.
- **El parte diario es un circuito propio**, un registro nuevo y separado. No reutiliza el registro del Auxiliar ni `comunicaciones_familiares` (el DPO halló una contradicción con `docs/flujos-clinicos/`: un día normal no tiene evento y `comunicaciones_familiares.evento_id` es `NOT NULL` y único).
- **El parte y el correo diario son base, apagados por defecto** en cada centro.
- **Correo diario: opt-in y sin contenido** («Tienes un aviso nuevo en ResidApp»; ni nombre, ni centro, ni si hubo incidencia), solo a quien lo active.
- **La alerta «no ha llegado» queda descartada.** Es una función de seguridad; si se pidiera, es otro ADR.
- **La fase 5 se divide en 5a asistencia y horas, 5b parte a la familia, 5c correo, 5d quién recoge y transporte**, cada una con su lectura del DPO sobre el esquema.
- **Titularidad:** el piloto sería privado sin plazas concertadas, pero el producto debe contemplar el caso habitual en España y Cataluña: plazas **mixtas** (concertadas y privadas en un mismo centro). Es un resumen de Orlando, sin verificar.
- **Persona experta (DPO real o abogado):** se busca cuando CJ haya contestado, para llevarle un caso concreto.

## 2. Lectura del DPO (borrador sin validar): `docs/privacidad/centro-de-dia-lectura-dpo.md`

Fases 1 a 4 admisibles con condiciones; la fase 5, no tal como se planteó. Lo que más pesa:

- El parte no puede llevar lesiones, sospechas, nombres de otros usuarios ni diagnósticos (la persona vuelve cada tarde a casa de quien lo recibe): lo prepara un profesional, con listas cerradas y como mucho una línea libre, con interruptor por usuario y, si es posible, por familiar. Sin IA (el ADR 0011 solo aprueba el dictado).
- Tabla propia y plazo de conservación propio: 250 partes al año durante 15 años, inmutables y con texto libre, no es proporcionado.
- Base jurídica del art. 9.2.h en un servicio social sin profesional sanitario contratante: la debe validar una persona experta.
- Autoridad de control y ENS: con centros concertados es más probable la APDCAT y el ENS; con **plazas mixtas** no se pueden descartar. Hay que releer los apartados 2.5 y 4.3 con ese caso.
- Dietas por religión o creencias: otra categoría del art. 9.1. Quien recoge a la persona no dio sus datos: hay que informarle (art. 14 RGPD).
- `tipo_de_servicio` no es dato personal.

## 3. Qué cambió respecto al análisis inicial (2026-11-19)

- El siguiente script libre era `0112` el 2026-11-21 (usados hasta `0111`): confirma con `git fetch`. El siguiente ADR es el `0017`.
- Las **zonas horarias por centro** ya están construidas (F0 a F3): ya no hay una sola zona. La franja de correo (10:00 a 18:59) usa la zona de cada centro, pero las horas siguen fijas en `ReminderService.cs`, `ExternalNoticeService.cs` y los procedimientos del `0110`. Moverlas a configuración por centro es un script nuevo que sustituye esos procedimientos, no tocar `0092` ni `0105`.
- El alta de centro (`NuevoCentro.cshtml`) ya tiene un selector de zona, corregible después solo por Plataforma, y los centros anteriores quedan en Madrid: es el modelo para el tipo de servicio.
- **Catalán:** CJ ya está de acuerdo (`pendientes-catalan.md`). La fase 3 debe esperar a cómo se implemente (ficheros de traducción por área) y usar el mismo mecanismo, no condicionales repartidos por las vistas.

## 4. Lo que ya funciona y lo que falla

Funciona: la unidad es la raíz obligatoria (una unidad = una «sala»); habitación y plaza son opcionales (`SqlResidentRepository.cs`); un centro nuevo nace sin turnos (la noche solo existe en la semilla de pruebas, `database/seed/centros_prueba.sql`); ninguna lógica clínica consulta los turnos.

Falla (comprueba las líneas, que cambian):

- **«Ubicación pendiente» falsa:** `SqlAdministrationOverview.cs` (`LocationPending`) marca como pendiente a quien no tiene habitación aunque la unidad no tenga habitaciones. Afecta al contador del inicio, al filtro «Requiere atención» (`SqlAdministracionResidentDirectory.cs`) y a la bandeja «todo en orden».
- **Rótulo fijo «Sin habitación asignada»** en `ResidentLocationLabel.Format` (`IEnfermeriaResidentDirectory.cs`), repetido en Auxiliar, Enfermería, Medicina, Ficha de antecedentes y Administración.
- **Vocabulario:** «cama», «habitación», «residencia», «turno» (`Residents/Create.cshtml`, `ResidentsModels.cs`, menú de Administración, `CitasExternas/AjustesFamilia.cshtml`, `PortalFamiliar/CitasFuera.cshtml`, «Turno ya cerrado hoy» en `Auxiliar/Registro.cshtml`, «Transferir al turno entrante», el área «Sueño» del Auxiliar).
- **Cierre de 17:00 a 18:00:** con publicación a las 18:00 y margen de 1 h (`FamilyCommunicationSchedule.ScheduledAt`), lo preparado después de las 17:00 sale al día siguiente. Hace falta margen de 0 a 30 min: **el valor lo decide CJ**, no se inventa.
- **No existe** el parte diario, la asistencia, las comidas ni el correo de comunicados.

## 5. Fases (cada una en su PR)

| Fase | Qué | Antes de empezar |
|---|---|---|
| 0 | Alcance, lectura del DPO y preguntas a CJ. **Hecha** | |
| 1 | Columna `tipo_de_servicio` (script nuevo, enum en `NewCenter.cs`, alta en `PlataformaController`, `SqlPlatformCenterRepository.cs` y `NuevoCentro.cshtml`, disparador de inmutabilidad, corrección con el centro vacío) y ADR 0017 | Las cinco condiciones del apartado 3.1 de la lectura del DPO |
| 2 | Plazas sin fricción: «ubicación pendiente» y rótulo según el tipo; ocultar «Habitaciones» y «Cama» en el alta y el menú | Ninguna del DPO mientras solo oculte; si añade días o jornada, pasa a ser 5a |
| 3 | Vocabulario por tipo, primero lo que ve la familia y el alta | Decidir cómo se hace el catalán; el DPO lee los textos de cara a personas (`Privacy.cshtml`, correos) |
| 4 | Horario: valores por defecto según tipo (publicación a las 18:00 y margen de 0 a 30 min son una propuesta de ingeniería; los valores los fija CJ), y la franja de correo si se hace configurable | Lectura del DPO si hay correo o la franja pasa a configurable |
| 5a–5d | Asistencia y horas; parte a la familia; correo diario; quién recoge y transporte | **Respuesta de CJ**, ADR propio, resolver la contradicción con los flujos (ya decidida: circuito propio) y lectura del DPO del esquema de cada subfase |

## 6. Verificación al construir

- Unitarias: `ResidentLocationLabelTests`, `CenterLayoutTests`; añadir casos de centro de día. Una prueba de `ScheduledAt` con publicación a las 18:00 y margen 0, 30 y 60 min para un comunicado a las 17:30 (el de 60 debe pasar a mañana), como documentación del comportamiento.
- Integración: `AdministracionInicioTests` (sin «ubicación pendiente» en un centro de día con residentes sin habitación), `AdministracionHabitacionesTests`, `HorarioComunicadosTests`.
- Funcionales: `ResidentsFlowTests` (alta sin cama).
- Navegador (skill `probar-en-navegador`): un centro de prueba de día, con alta, listados de Auxiliar y Enfermería y portal familiar, en claro, oscuro y 375 px.
- Consultas previas: `dpo-residapp` (esquema y ADR), `doc-usuario-residapp` (pantallas), `qa-revisor-residapp` al cerrar el bloque.
- Al cerrar: `docs/INDICE.md` y, si cambia texto visible, el Manual, la Guía y el Flujo.

## 7. Quién espera a quién

- **CJ:** `centro-de-dia-parte-diario-cj` (18 preguntas). Cuando Orlando avise, baja las respuestas con `tools/respuestas-cj.ps1` y no rellenes lo que falte.
- **DPO (otra pasada):** releer los apartados 2.5 y 4.3 con plazas mixtas; la pregunta del paso entre servicios.
- **Persona experta:** las 11 preguntas del apartado 5 de la lectura del DPO (base jurídica, `centro sanitario` frente a servicio social, art. 14 para quien recoge, correo diario y LSSI, autoridad de control y ENS, borrar o bloquear…).
- **Fuera del alcance de este traspaso, solo se avisa:** el «hoy» del cierre del Auxiliar (`SqlAssignedResidentDirectory.cs`) cuenta el día UTC y no el del centro.
