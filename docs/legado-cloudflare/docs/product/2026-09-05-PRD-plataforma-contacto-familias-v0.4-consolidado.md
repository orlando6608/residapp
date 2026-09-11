# Documento de Requisitos del Producto

## Plataforma asistencial y de comunicación con familias

**Versión:** 0.4  
**Fecha:** 5 de septiembre de 2026  
**Estado:** Consolidado funcional para construir y validar el prototipo navegable  
**Ámbito inicial:** Residencias geriátricas; prototipo exclusivamente con datos ficticios  
**Propietario de producto:** Promotor del proyecto / Dirección clínica  
**Documento sustituido:** PRD v0.3, de 2 de septiembre de 2026  
**Documento asociado vigente hasta su actualización:** Matriz de permisos de seis perfiles v0.1  

> **Decisión central de la v0.4.** Se mantienen el alcance, los seis perfiles y los flujos consolidados en la v0.3. Se incorpora el contrato funcional de Residente y Basal aprobado el 5 de septiembre de 2026: nueve áreas comunes, Barthel común de diez ítems, exclusión completa de la CFS, borrador basal único, firma no transferible, inmutabilidad y rectificación mediante nueva versión vinculada. También se formalizan el provisionamiento de centros y el historial temporal de ubicación.

> **Límite de uso.** Este documento no autoriza el tratamiento de datos reales, no sustituye una evaluación de impacto relativa a la protección de datos, una validación jurídica, una auditoría de seguridad ni los protocolos asistenciales del centro. El esquema físico D1/Drizzle todavía no queda aprobado.

## Control del documento

| Versión | Fecha | Cambio principal | Estado |
| --- | --- | --- | --- |
| 0.1 | 08/08/2026 | Alcance funcional inicial | Revisión |
| 0.1-r1 | 09/08/2026 | Ficha, basal y registro cotidiano simplificado | Revisión |
| 0.1-r2 | 10/08/2026 | Auxiliar: diez áreas, opciones rápidas y circuito prioritario | Revisión |
| 0.2 | 24/08/2026 | Consolidación de Auxiliar, Enfermería y Medicina | Consolidado funcional |
| 0.3 | 02/09/2026 | Integración de seis perfiles, citas configurables, multirol, Administración y Dirección | Sustituido |
| 0.4 | 05/09/2026 | Consolidación del contrato Residente/Basal, eliminación de CFS, Barthel común, firma, rectificación, ubicación y provisionamiento | Consolidado funcional vigente |

## Jerarquía documental

1. Decisión explícita del propietario de producto documentada con fecha posterior.
2. Este PRD v0.4 para alcance, invariantes, entidades y comportamiento global.
3. Matriz de permisos vigente para autorización de acciones y recursos.
4. Wireframe vigente del perfil para detalle de pantalla e interacción.
5. `AGENTS.md` vigente para la forma de trabajar sobre el repositorio.
6. Implementación existente como evidencia del estado técnico, nunca como fuente para inventar requisitos.

Una contradicción debe hacerse visible y resolverse documentalmente. No se corregirá en silencio ni se ampliarán permisos por inferencia.

## Requisitos sustituidos por la v0.4

| Identificador | Regla anterior de v0.3 | Regla vigente de v0.4 |
| --- | --- | --- |
| `BAS-02` | Barthel y CFS | Barthel común; CFS y Pfeiffer excluidos |
| `BAS-03` | Barthel de diez ítems sin catálogo común cerrado | `BARTHEL_COMUN_V0_1`, diez respuestas, puntuaciones y total automático sobre 100 |
| `BAS-08` | CFS condicionada a licencia | Requisito retirado: no existe CFS en el alcance |
| `DER-03` | Informe con Barthel/CFS | Informe con Barthel y basal relevante, sin CFS |
| `FAM-03` | Prohibición de acceso familiar que enumeraba CFS | Se mantiene la prohibición, eliminando la referencia a CFS |
| `COR-01` | Ventana general de corrección de seis horas | Ventana limitada a cursos/notas clínicas expresamente habilitados; nunca al basal |
| `COR-02` | Rectificación genérica fuera de ventana | Rectificación según tipo de objeto; el basal siempre se rectifica mediante nueva versión vinculada |
| `ORG-01` | Administración gestiona centros y estructura | La plataforma provisiona centros; Administración gestiona la estructura subordinada autorizada |

# 1. Resumen ejecutivo

La plataforma permite que los profesionales de una residencia registren de forma breve y trazable el estado cotidiano de cada residente, documenten cambios, mantengan continuidad entre turnos, realicen valoración profesional y transformen únicamente la información aprobada en publicaciones comprensibles para familiares autorizados.

La v0.4 mantiene seis perfiles funcionales:

- **Auxiliar:** observación y registro cotidiano; aviso directo ante situaciones prioritarias.
- **Enfermería:** basal según permiso, valoración, seguimiento, continuidad, escalado, ejecución de indicaciones, derivación, cierre y comunicación familiar cuando resuelve.
- **Medicina:** basal según permiso, valoración médica, conducta, indicaciones, seguimiento, derivación, cierre y comunicación familiar cuando es el punto final.
- **Portal Familiar:** consulta exclusiva de publicaciones aprobadas y gestión estructurada de citas según configuración.
- **Administración:** estructura subordinada del centro, usuarios, roles, asignaciones, turnos, autorizaciones, programación familiar, citas y auditoría administrativa.
- **Dirección/Coordinación Clínica:** supervisión operativa, calidad de proceso e indicadores; acceso clínico detallado solo con permiso específico, en lectura y auditado.

La plataforma no diagnostica, prescribe, solicita pruebas, recomienda tratamientos, decide prioridades clínicas ni deriva automáticamente. Tampoco sustituye canales urgentes, llamadas, emergencias, historia clínica oficial o protocolos del centro.

## 1.1 Propuesta de valor

- **Centro:** continuidad, trazabilidad y configuración adaptable a su organización.
- **Profesionales:** registros rápidos, responsabilidades claras y bandejas compartidas.
- **Familia:** información comprensible en un portal privado y posibilidad de coordinar una cita sin chat clínico.
- **Dirección:** indicadores agregados y supervisión del proceso sin interferir en la actuación asistencial.

## 1.2 Resultado esperado

Prototipo navegable capaz de representar con datos ficticios el circuito completo:

`Alta administrativa -> basal profesional -> registro cotidiano o evento -> revisión de Enfermería -> seguimiento o escalado -> valoración médica e indicaciones -> derivación si procede -> cierre -> texto familiar -> aprobación -> publicación -> consulta familiar`.

También debe representar configuración organizativa, historial de ubicación, agenda, auditoría y supervisión.

# 2. Problema, oportunidad y límites

## 2.1 Problemas que se pretenden resolver

- Información cotidiana dispersa o poco útil para las familias.
- Interrupciones y llamadas repetitivas dependientes de disponibilidad individual.
- Falta de claridad sobre quién observó, valoró, indicó, ejecutó, corrigió o cerró.
- Pérdida de continuidad en relevos y seguimientos abiertos.
- Exposición indebida de contenido clínico interno a familiares o perfiles administrativos.
- Dificultad para adaptar horarios, unidades, turnos y citas a centros diferentes.
- Falta de indicadores de proceso con denominador, periodo y ámbito explícitos.
- Riesgo de perder el contexto histórico al sobrescribir el basal o la ubicación del residente.

## 2.2 Fuera de alcance de la v0.4

- Sustituir la historia clínica oficial o el software integral del centro.
- Diagnóstico, prescripción, solicitud de pruebas, triaje o recomendaciones automáticas.
- Mensajería libre entre familia y profesionales.
- Contenido sanitario por email, SMS, WhatsApp o enlaces públicos.
- Notificaciones push en el piloto.
- App móvil nativa; la primera versión será web adaptable/PWA.
- Modo clínico offline con sincronización posterior.
- Administración de medicación, facturación, nóminas, fichaje o gestión laboral avanzada.
- Videollamada propia; solo puede ofrecerse como modalidad si el centro dispone del medio externo adecuado.
- Rankings nominativos de productividad o evaluaciones automáticas de mala praxis.
- Exportación masiva de historias clínicas individuales.
- Integraciones con historias clínicas externas en la primera versión.
- Flujos específicos de Centro de Día y SAAD antes de validar el modelo inicial en residencias.
- CFS y Pfeiffer dentro del estado basal.
- Esquema físico definitivo D1/Drizzle.

# 3. Objetivos y métricas del piloto

| Objetivo | Indicador | Meta inicial |
| --- | --- | --- |
| Registro viable | Tiempo mediano para cerrar un día sin cambios | <= 60 s por residente |
| Continuidad | Cambios ordinarios revisados antes del cierre diario | >= 95 % |
| Seguridad operativa | Eventos prioritarios de Auxiliar con aviso directo documentado | 100 % |
| Trazabilidad | Actuaciones firmadas con autor, perfil activo y marca temporal | 100 % |
| Relevo | Eventos abiertos visibles tras cambio de turno | 100 % |
| Comunicación | Publicaciones con aprobación profesional previa | 100 % |
| Acceso | Operaciones clínicas y familiares validadas en servidor | 100 % |
| Basal | Basales vigentes con versión, autoría, firma y catálogo identificables | 100 % |
| Usabilidad | Tareas críticas completadas sin ayuda | >= 90 % |

Estas metas son hipótesis de validación del piloto, no estándares clínicos universales ni umbrales automáticos.

# 4. Principios funcionales invariantes

- **Basal como referencia:** los cambios se comparan con la situación habitual del residente.
- **Autoría fiel:** quien observa registra; quien valora, indica, ejecuta, corrige o cierra firma su propia actuación.
- **Firma no transferible:** nadie firma un borrador basal creado por otra persona.
- **Inmutabilidad trazable:** un registro firmado no se elimina ni se sobrescribe silenciosamente.
- **Versionado temporal:** basal y ubicación conservan el estado aplicable en cada momento.
- **Separación de objetos:** observación, valoración, conducta, ejecución, publicación familiar y cita son objetos distintos.
- **Responsabilidad de equipo:** un evento abierto no pertenece permanentemente a la cuenta que inició la valoración.
- **Mínimo privilegio:** acceso limitado por centro, unidad, residente, asignación, perfil activo, autorización, permiso específico y finalidad.
- **Autorización en servidor:** ocultar botones no constituye seguridad.
- **Datos confiables:** identidad, perfil, ámbito, autoría y fecha/hora se obtienen de sesión y contexto server-side, no de campos libres del cliente.
- **Denegación por defecto:** ninguna capacidad se concede por ausencia de una prohibición explícita.
- **Vencimiento no equivale a cierre:** un objeto fuera de plazo sigue abierto y visible hasta actuación profesional.
- **Automatización no clínica:** el sistema puede programar una publicación aprobada o reservar un hueco; no redacta, interpreta ni decide clínicamente.
- **Urgencias fuera del portal:** ninguna pantalla, cita o publicación sustituye el protocolo urgente ni el contacto directo.
- **Privacidad por diseño y por defecto:** se minimizan datos, exposición, retención y permisos desde el modelo.

# 5. Roles y responsabilidades

| Perfil | Responsabilidad principal | Límite esencial |
| --- | --- | --- |
| Auxiliar | Cierre cotidiano, cambios observados y aviso directo cuando proceda | No diagnostica, valora clínicamente, modifica basal, escala a Medicina ni aprueba publicaciones |
| Enfermería | Basal según permiso, bandejas, valoración, seguimiento, escalado, derivación, indicaciones, cierre y comunicación | No altera observaciones ajenas ni gestiona autorizaciones familiares |
| Medicina | Basal según permiso, escalados, eventos propios, valoración, conducta, indicaciones, seguimiento, derivación, cierre y comunicación | No automatiza decisiones ni atribuye actos a otros |
| Familiar | Consulta publicaciones y gestiona citas habilitadas para residentes autorizados | No accede a contenido clínico interno ni elige profesional concreto |
| Administración | Estructura subordinada, identidades, ubicaciones, roles, asignaciones, turnos, autorizaciones, citas y auditoría administrativa | No provisiona centros, participa en actos clínicos ni accede por defecto al historial clínico |
| Dirección Clínica | Supervisión de proceso, indicadores e informes agregados | No interviene clínicamente desde este perfil; detalle clínico solo en lectura, con permiso y auditoría |

## 5.1 Cuentas multirol

Una cuenta puede tener uno o varios perfiles autorizados. El usuario debe seleccionar o cambiar explícitamente el perfil activo. Toda actuación conserva cuenta, perfil activo, ámbito y fecha/hora. Los permisos de perfiles distintos no se combinan. Un cargo, organigrama, turno o posición organizativa no concede permisos por sí mismo.

# 6. Alcance modular

| Módulo | Funciones incluidas en v0.4 |
| --- | --- |
| Acceso | Cuentas individuales, sesión, recuperación, bloqueo y segundo factor según política |
| Organización | Provisionamiento de centros; edificios y plantas opcionales; unidades, habitaciones y plazas/camas configurables |
| Identidades | Usuarios multirol, cargos separados, asignaciones y estados de cuenta |
| Residentes | Identidad, historial de ubicación, basal vigente/versionado, Barthel común y cognición documentada |
| Registro cotidiano | Sin cambios, no valorable y cambio observado |
| Eventos | Ordinarios/prioritarios, bandejas, valoración, seguimiento, continuidad y cierre |
| Medicina | Escalados, conducta, indicaciones, seguimiento médico y continuidad |
| Derivación | Módulo común, vista previa, firma y PDF vinculado al evento |
| Publicaciones | Texto separado, aprobación humana, programación, corrección y retirada excepcional |
| Portal Familiar | Publicaciones publicadas, autorizaciones activas y citas según modalidad del centro |
| Administración | Familias, autorizaciones, contacto urgente, publicaciones, turnos, citas y auditoría |
| Dirección Clínica | Supervisión operativa, detalle clínico condicionado, indicadores e informes agregados |

# 7. Modelo funcional y estados

## 7.1 Entidades principales

| Entidad | Finalidad | Regla clave |
| --- | --- | --- |
| Centro | Aislamiento organizativo | Provisionado por la plataforma; no por Administración del centro |
| Estructura / unidad | Organización subordinada | Centro y unidad obligatorios; niveles restantes según configuración |
| Usuario / perfil activo | Identidad y capacidad | Cuenta individual y cambio explícito de perfil |
| Grant / permiso | Autorización | Denegación por defecto y ámbito verificable en servidor |
| Asignación / turno | Ámbito operativo | La planificación no sustituye autorización de seguridad |
| Residente | Sujeto de atención | Identificador opaco y no secuencial; la edad se calcula desde la fecha de nacimiento |
| Historial de ubicación | Fuente de ubicación | Un único intervalo vigente; los cambios no sobrescriben el pasado |
| Estado basal / versión | Referencia habitual | Un borrador y una versión vigente como máximo por residente |
| Aportación basal | Colaboración identificada | No transfiere autoría ni firma del borrador |
| Barthel | Medición funcional | Diez respuestas, puntuaciones y total automático vinculados a una versión basal |
| Registro cotidiano | Resultado breve | Sin cambios, no valorable o cambio enviado |
| Evento | Cambio o hecho relevante | Puede originarlo Auxiliar, Enfermería o Medicina |
| Valoración / actuación | Aportación profesional | No modifica observaciones previas |
| Seguimiento / continuidad | Trabajo pendiente | Equipo responsable, fecha o criterio y autoría individual |
| Indicación médica | Tarea a Enfermería | Lectura y realización son hitos distintos |
| Derivación | Documento externo | Módulo común de Enfermería y Medicina |
| Familiar / autorización | Vínculo y acceso | Solo autorización activa concede acceso al portal |
| Publicación familiar | Contenido externo aprobado | Independiente del evento clínico interno |
| Agenda / franja / cita | Coordinación estructurada | Modo definido por centro; reserva transaccional |
| Auditoría | Trazabilidad | Append-only y no editable por usuarios ordinarios |

## 7.2 Estados consolidados

| Objeto | Estados |
| --- | --- |
| Registro cotidiano | `BORRADOR -> FIRMADO_SIN_CAMBIOS / FIRMADO_NO_VALORABLE / CAMBIO_ENVIADO` |
| Evento Enfermería | `PENDIENTE -> EN_REVISION -> EN_SEGUIMIENTO / ESCALADO_MEDICINA / PROTOCOLO_URGENTE -> CERRADO_ENFERMERIA` |
| Evento Medicina | `PENDIENTE_MEDICA -> EN_VALORACION -> INDICACIONES / SEGUIMIENTO / PROTOCOLO_URGENTE -> CERRADO_MEDICINA` |
| Vencimiento | Pendiente fuera de plazo; nunca cierre u ocultación automática |
| Continuidad | Pendiente de recepción por turno entrante / pendiente de continuidad médica |
| Indicación | `PENDIENTE_LECTURA -> LEIDA -> REALIZADA / NO_REALIZADA_CON_INCIDENCIA` |
| Basal | `PENDIENTE -> BORRADOR -> FIRMADO_VIGENTE -> HISTORICO` |
| Borrador basal cancelado | `BORRADOR -> CANCELADO_CON_MOTIVO`; nunca puede firmarse después |
| Rectificación basal | Nueva versión vinculada que sigue el ciclo de borrador y firma |
| Ubicación | Intervalo vigente sin fecha final -> intervalo cerrado + nuevo intervalo vigente |
| Autorización familiar | `PENDIENTE -> ACTIVA -> SUSPENDIDA / REVOCADA / CADUCADA` |
| Publicación | `BORRADOR -> APROBADA -> PREPARADA -> PUBLICADA / CORRECTORA / RETIRADA_EXCEPCIONAL` |
| Reserva directa | `DISPONIBLE -> CONFIRMADA -> FINALIZADA / REPROGRAMADA / CANCELADA` |
| Solicitud previa | `PENDIENTE_GESTION -> PROPUESTA_CENTRO -> CONFIRMADA / SOLICITUD_CAMBIO / CANCELADA -> FINALIZADA` |

# 8. Requisitos funcionales

## 8.1 Autenticación, organización y permisos

- **AUTH-01 P0.** Cuenta individual para cada profesional, administrador, directivo y familiar.
- **AUTH-02 P0.** Toda operación protegida se autoriza en servidor comprobando cuenta activa, perfil activo explícito, centro, unidad, residente o ámbito aplicable, asignación y permiso específico. El servidor obtiene identidad, perfil, ámbito, autoría y fecha/hora desde fuentes confiables.
- **AUTH-03 P0.** Sesiones con caducidad, revocación, bloqueo y recuperación segura; errores que no revelen la existencia de cuentas.
- **AUTH-04 P0.** Segundo factor antes de utilizar datos reales, según política validada.
- **AUTH-05 P0.** Una cuenta multirol exige selección explícita de perfil; los permisos no se combinan implícitamente.
- **AUTH-06 P0.** Conocer o manipular un identificador, URL, unidad, residente o perfil no concede acceso.
- **AUTH-07 P0.** La política de autorización es denegar por defecto y toda concesión o revocación sensible queda auditada.
- **ORG-01 P0.** La plataforma provisiona cada centro. Administración gestiona únicamente estructura subordinada, usuarios, perfiles, asignaciones y estados dentro de los centros autorizados.
- **ORG-02 P0.** Cargo organizativo, organigrama, turno y permiso de seguridad son conceptos separados.
- **ORG-03 P0.** Centro y unidad son obligatorios. Edificio y planta son opcionales. Habitación y plaza/cama se utilizan según la configuración del centro.
- **ORG-04 P1.** Turnos con fechas múltiples, recurrencias y excepciones; los solapamientos se muestran y Administración decide.
- **ORG-05 P0.** Crear o gestionar estructura no concede por sí mismo acceso a residentes ni contenido clínico.

## 8.2 Residente, ubicación y estado basal

- **RES-01 P0.** Ficha con nombre, fecha de nacimiento/edad calculada, sexo documentado, unidad y ubicación aplicable. Los identificadores son opacos y no secuenciales y no se usan como autorización.
- **RES-02 P0.** Administración realiza el alta por defecto y deja el basal pendiente. Enfermería solo puede crear la identidad si el centro le concede permiso adicional. Medicina no recibe permiso de alta administrativa en esta versión.
- **RES-03 P0.** La edad no se almacena como fuente independiente: se calcula desde la fecha de nacimiento y la fecha de consulta.
- **RES-04 P0.** La ubicación actual deriva del único intervalo vigente del historial. Cada traslado cierra el intervalo anterior y abre otro sin sobrescribir el pasado.
- **RES-05 P0.** Todo intervalo de ubicación debe mantener coherencia de centro, unidad y niveles subordinados configurados.
- **BAS-01 P0.** El basal contiene exactamente nueve áreas: movilidad, alimentación, continencia, aseo/higiene, cognición, comunicación, conducta, sueño y ayudas habituales.
- **BAS-02 P0.** El basal incorpora el Índice de Barthel común. CFS y Pfeiffer quedan excluidos por completo.
- **BAS-03 P0.** Barthel utiliza `BARTHEL_COMUN_V0_1`, con diez ítems seleccionables, conservación de respuestas y puntuaciones y cálculo automático validado del total sobre 100.
- **BAS-04 P0.** Cognición permite: sin deterioro conocido o documentado, deterioro cognitivo leve, demencia o situación no determinada. Etiología y GDS 1-7 solo se registran si constan documentados, con fuente y fecha.
- **BAS-05 P0.** Solo el basal vigente aparece por defecto; las versiones previas se archivan y conservan su relación temporal con eventos.
- **BAS-06 P0.** Los motivos ordinarios de nueva versión son alta, revisión programada o cambio funcional consolidado.
- **BAS-07 P0.** Enfermería y Medicina solo pueden completar o reevaluar el basal si el centro les concede el permiso correspondiente y el residente está dentro de su ámbito.
- **BAS-08 RETIRADO.** La anterior regla sobre licencia de CFS queda retirada. No se incorporará CFS como activo, campo, puntuación, permiso, resumen o contenido de informe.
- **BAS-09 P0.** Solo puede existir un borrador basal activo por residente. Puede estar incompleto, pero no activarse como vigente hasta superar todas las validaciones de firma.
- **BAS-10 P0.** Solo firma el borrador la cuenta que lo creó, actuando con el mismo perfil profesional autorizado. La firma no se transfiere.
- **BAS-11 P0.** Otros profesionales autorizados pueden añadir aportaciones identificadas y auditadas sin convertirse en autores del borrador ni adquirir capacidad de firma.
- **BAS-12 P0.** Si la persona creadora no puede finalizar, el borrador se cancela con motivo trazable y otro profesional crea uno nuevo.
- **BAS-13 P0.** Al firmar se congelan contenido, autoría, perfil firmante, fecha/hora, versión del catálogo y contenedores organizativos aplicables. La nueva versión pasa a vigente y la anterior a histórica en una sola operación lógica.
- **BAS-14 P0.** Un basal firmado es inmutable. Un error se corrige mediante una nueva versión de rectificación vinculada al original, con motivo, autoría y fecha. La ventana de seis horas nunca se aplica al basal.
- **BAS-15 P0.** Las nueve áreas usan un único catálogo basal versionado. Deben estar respondidas para firmar, admiten observaciones cuando proceda y `NO_DOCUMENTADO` nunca significa normalidad.
- **BAS-16 P0.** Movilidad separa modo habitual, ayuda técnica, ayuda humana y transferencias. Las ayudas técnicas de movilidad no se duplican en Ayudas habituales.
- **BAS-17 P0.** Alimentación distingue al menos textura normal, troceada, triturada y puré; líquidos sin espesante, néctar, miel, pudín o no documentado.
- **BAS-18 P0.** Fuente y fecha pertenecen a la versión basal común. Las opciones incompatibles se modelan como excluyentes. `OTRO` exige descripción cuando sin ella el dato no resulte interpretable.
- **BAS-19 P0.** Las nueve áreas no generan una puntuación conjunta. Barthel conserva su puntuación independiente.

## 8.3 Auxiliar: registro cotidiano y eventos

- **AUX-01 P0.** La pantalla principal muestra residentes asignados y el estado de su cierre cotidiano.
- **AUX-02 P0.** Acciones mutuamente excluyentes: Sin cambios, No valorable y Registrar cambio.
- **AUX-03 P0.** Sin cambios se firma y cierra sin tarea para Enfermería.
- **AUX-04 P0.** No valorable exige motivo y nunca equivale a estabilidad clínica.
- **AUX-05 P0.** Registrar cambio admite una o varias de diez áreas: alimentación/hidratación; movilidad/funcionalidad; ánimo/conducta; dolor/malestar; heces/diuresis; sueño; lesiones en piel; participación/relación social; incidencias/caídas; estado de conciencia.
- **AUX-06 P0.** Las opciones rápidas siguen el wireframe; se utiliza Atragantamiento y se admite texto opcional o libre según área.
- **AUX-07 P0.** Temperatura opcional; fecha/hora del registro automática; no se exige hora exacta de observación.
- **AUX-08 P0.** La clasificación inicial ordinaria/prioritaria organiza la bandeja y no diagnostica.
- **AUX-09 P0.** Situaciones prioritarias iniciales: alteración de conciencia/estado general, caída/lesión/traumatismo, fiebre/sospecha de infección, dolor nuevo/intenso, dificultad respiratoria y déficit neurológico nuevo.
- **AUX-10 P0.** Un evento prioritario recuerda aplicar el protocolo del centro y exige documentar el aviso directo definido para el prototipo.
- **AUX-11 P0.** Firmar conserva autoría y envía a la bandeja correspondiente de Enfermería.
- **AUX-12 P1.** El campo Desde cuándo en estado de conciencia y la carga de persona/canal/hora del aviso se validarán en usabilidad sin bloquear el prototipo.
- **AUX-13 P0.** Auxiliar solo consulta el basal vigente de residentes asignados; no crea, aporta, firma, rectifica ni consulta versiones históricas.

## 8.4 Enfermería

- **ENF-01 P0.** Bandejas compartidas por unidad: prioritarios, ordinarios, seguimientos, indicaciones y comunicaciones pendientes.
- **ENF-02 P0.** Cerrados salen de bandejas y pasan a Historial; abiertos vencidos permanecen visibles.
- **ENF-03 P0.** Empezar valoración registra profesional y hora, protege concurrencia y no crea propiedad permanente.
- **ENF-04 P0.** Observación original no editable, basal vigente y línea temporal bajo demanda.
- **ENF-05 P0.** Valoración con hallazgos, actuaciones, comunicaciones, resultado y constantes opcionales: T, PA, FC, FR, SpO2, aire ambiente/oxigenoterapia, flujo de O2, glucemia y otra con nombre/valor/unidad.
- **ENF-06 P0.** Decisiones: cerrar, seguimiento, escalar a Medicina o activar/documentar protocolo urgente.
- **ENF-07 P0.** Seguimientos compartidos con fecha o criterio; cada actuación conserva autoría.
- **ENF-08 P0.** Transferencia explícita entre turnos; el evento sigue visible aunque la recepción no se confirme.
- **ENF-09 P0.** Escalado transmite observación, basal, valoración, constantes, actuaciones y motivo, sin decisión automática.
- **ENF-10 P0.** Indicaciones médicas: lectura y realización/no realización con incidencia son hitos distintos.
- **ENF-11 P0.** Enfermería puede registrar un evento observado directamente sin alterar el rol de Auxiliar.
- **ENF-12 P0.** Si Enfermería cierra, decide si existe comunicación y puede aprobar el texto familiar separado.
- **ENF-13 P0.** Cuando dispone de permiso basal, Enfermería utiliza el módulo común definido por `BAS-01` a `BAS-19`; la creación de identidad continúa siendo un permiso diferente.

## 8.5 Medicina

- **MED-01 P0.** Inicio con escalados, seguimientos, indicaciones/incidencias, comunicaciones, residentes e historial autorizados.
- **MED-02 P0.** El escalado muestra motivo, constantes, actuaciones, tiempo y estado, sin resumen diagnóstico automático.
- **MED-03 P0.** Observación y valoración de Enfermería permanecen no editables; línea temporal bajo demanda.
- **MED-04 P0.** Valoración médica con hallazgos/exploración, valoración, constantes opcionales y actuaciones.
- **MED-05 P0.** Conducta: actuaciones, indicaciones a Enfermería y salida mediante cierre, seguimiento o protocolo urgente.
- **MED-06 P0.** Indicación con texto, fecha prevista o criterio e información adicional, sin selector automático de prioridad.
- **MED-07 P0.** Si un resultado es necesario para decidir el desenlace, Medicina mantiene el evento en seguimiento; el sistema no decide qué resultado es necesario.
- **MED-08 P0.** Un seguimiento vencido no caduca ni desaparece.
- **MED-09 P0.** Al finalizar el turno: transferencia al equipo entrante o seguimiento para próxima revisión.
- **MED-10 P0.** Si Medicina cierra, Enfermería no realiza un segundo cierre.
- **MED-11 P0.** Medicina puede registrar eventos propios sin reenviárselos.
- **MED-12 P0.** Si Medicina es el punto final, decide y aprueba la comunicación familiar.
- **MED-13 P0.** Cuando dispone de permiso basal, Medicina utiliza el módulo común definido por `BAS-01` a `BAS-19`; no recibe por ello permiso de alta administrativa.

## 8.6 Derivación a Urgencias

- **DER-01 P0.** Enfermería y Medicina reutilizan un único módulo funcional.
- **DER-02 P0.** Vista previa obligatoria antes de firma; editar el informe no altera registros de origen.
- **DER-03 P0.** Contenido: identificación/centro, basal relevante, Barthel, cognición/comunicación, motivo y observación, valoraciones, constantes, oxigenoterapia, actuaciones, evolución y firmante. La referencia anterior a CFS queda retirada.
- **DER-04 P0.** Servicios contactados y horas permanecen en trazabilidad interna, no como bloque del informe externo.
- **DER-05 P0.** PDF firmado vinculado al evento e Historial; impresión/descarga auditada en entorno real.
- **DER-06 P0.** Toda derivación genera una Actualización relevante y exige documentar el intento de llamada al contacto familiar designado, sin retrasar la atención.

## 8.7 Publicaciones y Portal Familiar

- **FAM-01 P0.** Familiar y autorización son objetos distintos; solo autorización Activa permite acceso.
- **FAM-02 P0.** La familia consulta exclusivamente publicaciones aprobadas y publicadas para su audiencia.
- **FAM-03 P0.** No accede a observaciones, valoraciones, constantes, basal, Barthel, GDS, indicaciones, seguimientos, derivaciones, historial ni línea temporal interna. La referencia anterior a CFS queda retirada.
- **FAM-04 P0.** Modalidad ordinaria por residente: diaria, semanal o solo actualizaciones relevantes. El centro configura día/hora.
- **FAM-05 P0.** La aprobación siempre es humana. Una publicación ordinaria aprobada se hace visible automáticamente al llegar el momento configurado; el sistema no inventa contenido ausente.
- **FAM-06 P0.** Una actualización relevante puede publicarse tras aprobación y la relevancia la decide el profesional.
- **FAM-07 P0.** Visibilidad del portal: publicaciones ordinarias 30 días y relevantes 6 meses. Esto no define la conservación jurídica interna.
- **FAM-08 P0.** Una publicación publicada es inmutable; un error ordinario se corrige con una publicación correctora vinculada.
- **FAM-09 P0.** Retirada excepcional solo por privacidad, seguridad, audiencia/residente incorrectos o causa equivalente, con permiso, motivo y auditoría; el original permanece internamente.
- **FAM-10 P0.** Sin email, SMS, WhatsApp ni push en el piloto. Sin chat, comentarios o botón específico de descarga.
- **FAM-11 P0.** La ausencia de publicaciones no se presenta como Todo bien, estabilidad o ausencia de incidencias.
- **FAM-12 P0.** El profesional visible es Equipo asistencial del centro.

## 8.8 Citas familiares configurables

- **CIT-01 P0.** El centro puede desactivar el servicio o activarlo con una única modalidad vigente: Reserva directa o Solicitud previa.
- **CIT-02 P0.** El Portal Familiar consulta la configuración en servidor y muestra solo el flujo aplicable. No ofrece ambos flujos a la vez.
- **CIT-03 P0.** Solo un familiar con autorización Activa para el residente puede iniciar, consultar, modificar o cancelar su cita.
- **CIT-04 P0.** El familiar elige Equipo médico o Enfermería y una modalidad habilitada: presencial, telefónica o videoconferencia. Nunca elige profesional concreto.
- **CIT-05 P0.** Todas las pantallas muestran que las citas no son un canal urgente.

### Reserva directa

- **CIT-D01 P0.** El familiar ve exclusivamente huecos realmente disponibles para el equipo y modalidad elegidos.
- **CIT-D02 P0.** Selecciona fecha/hora, revisa y confirma; el resultado es Cita confirmada sin propuesta administrativa previa.
- **CIT-D03 P0.** La confirmación es transaccional e idempotente. Dos usuarios no pueden reservar el mismo hueco.
- **CIT-D04 P0.** Si el hueco se ocupa durante la confirmación, no se crea la cita y se ofrecen alternativas disponibles.
- **CIT-D05 P0.** Reprogramar vuelve a los huecos disponibles; cancelar exige confirmación y deja trazabilidad.

### Solicitud previa

- **CIT-S01 P0.** El familiar envía una solicitud estructurada con equipo, modalidad, motivo categorizado, comentario breve opcional y disponibilidad aproximada.
- **CIT-S02 P0.** La solicitud queda Pendiente de gestión. Administración propone fecha/hora y puede asignar internamente un profesional.
- **CIT-S03 P0.** El familiar puede aceptar, solicitar otro horario o cancelar; no se habilita chat.
- **CIT-S04 P0.** Solo la aceptación de la propuesta produce Cita confirmada.

### Configuración y continuidad

- **CIT-C01 P0.** Administración configura equipos, modalidades, franjas recurrentes, duración, antelación mínima, horizonte máximo y bloqueos.
- **CIT-C02 P0.** Cambiar la modalidad del centro afecta a nuevas operaciones y no reescribe solicitudes o citas existentes.
- **CIT-C03 P0.** Toda creación, propuesta, confirmación, reprogramación, cancelación y finalización conserva actor y fecha/hora.

## 8.9 Administración

- **ADM-01 P0.** Pantalla inicial con asuntos administrativos, sin eventos, constantes, seguimientos ni indicaciones.
- **ADM-02 P0.** Gestiona identidad e historial de ubicación del residente sin mostrar información clínica.
- **ADM-03 P0.** Gestiona familiares, cuentas, autorizaciones y contacto urgente designado.
- **ADM-04 P0.** Crea, activa, suspende y asigna usuarios; puede asignar múltiples perfiles a una misma cuenta.
- **ADM-05 P0.** Configura frecuencia y horarios de publicaciones; no redacta ni aprueba contenido.
- **ADM-06 P0.** Consulta estado administrativo/técnico de publicaciones sin texto completo por defecto.
- **ADM-07 P0.** Configura y gestiona el servicio de citas conforme a `CIT-01` a `CIT-C03`.
- **ADM-08 P0.** Puede realizar retirada excepcional solo con permiso específico.
- **ADM-09 P0.** Auditoría administrativa de usuarios, perfiles, asignaciones, autorizaciones, frecuencia, contacto urgente, horarios, citas, retiradas y seguridad.
- **ADM-10 P0.** No accede al basal ni, por defecto, al historial clínico; no modifica registros profesionales.
- **ADM-11 P0.** Gestiona la estructura subordinada de centros ya provisionados. Crear, renombrar o reorganizar estructura exige ámbito y auditoría y no concede acceso clínico.

## 8.10 Dirección/Coordinación Clínica

- **DIR-01 P0.** Perfil separado de Administración y de los perfiles asistenciales.
- **DIR-02 P0.** Supervisión operativa de cierres pendientes, eventos abiertos/vencidos, seguimientos, continuidad, indicaciones con incidencia, derivaciones y estado familiar.
- **DIR-03 P0.** La supervisión operativa puede identificar residente y episodio cuando sea necesario, sin abrir automáticamente notas completas.
- **DIR-04 P0.** El contenido clínico detallado y la línea temporal requieren permiso específico de supervisión clínica, son solo lectura y cada acceso queda auditado antes de entregar el contenido.
- **DIR-05 P0.** Dirección detecta pendientes, pero no valora, indica, ejecuta, escala, corrige ni cierra desde este perfil.
- **DIR-06 P0.** Puede consultar derivaciones e informes si tiene permiso clínico; no generarlos ni firmarlos.
- **DIR-07 P0.** Supervisa estado de publicaciones; no redacta, modifica ni aprueba.
- **DIR-08 P0.** Indicadores agregados por ámbito y periodo, con denominador cuando corresponda, sin predicción clínica ni juicio automático.
- **DIR-09 P0.** No rankings nominativos ni tablas de productividad individual.
- **DIR-10 P0.** Informes agregados de actividad; no exportación masiva de historias individuales.
- **DIR-11 P0.** La consulta del basal vigente o histórico requiere permiso clínico específico, finalidad válida, ámbito de residente y auditoría append-only; siempre es de solo lectura.

## 8.11 Historial, correcciones y auditoría

- **HIS-01 P0.** Eventos cerrados salen de bandejas y permanecen en Historial según permisos.
- **HIS-02 P0.** Línea temporal completa plegada por defecto y accesible solo con autorización específica.
- **HIS-03 P0.** Eventos conservan referencia al basal y a la ubicación aplicables cuando ocurrieron, sin reconstruir el pasado desde valores actuales.
- **COR-01 P0.** La ventana ordinaria de seis horas, configurable y limitada al autor, solo se aplica a cursos o notas clínicas que la política identifique expresamente. No se aplica al basal, publicaciones ya publicadas ni otros objetos definidos como inmutables.
- **COR-02 P0.** Fuera de la ventana aplicable se añade una rectificación trazable según el tipo de objeto; original, motivo, autor y fecha permanecen. En el basal, toda rectificación crea una nueva versión vinculada con independencia del tiempo transcurrido.
- **AUD-01 P0.** Accesos clínicos detallados de Dirección, retiradas, autorizaciones, cambios de permisos y acciones sensibles quedan auditados.
- **AUD-02 P0.** La auditoría no se usa como ranking de trabajadores ni expone texto clínico a perfiles no autorizados.
- **AUD-03 P0.** Los registros de auditoría son append-only para usuarios ordinarios y conservan cuenta, perfil activo, ámbito, acción, recurso y fecha/hora confiable.

# 9. Reglas de validación y concurrencia

- Sin cambios, No valorable y Registrar cambio son mutuamente excluyentes.
- No valorable exige motivo; Registrar cambio exige al menos un área.
- Una valoración profesional nunca modifica la observación original.
- Un seguimiento exige fecha o criterio y equipo responsable.
- Confirmar lectura de una indicación no la marca como realizada.
- Una publicación no se aprueba sin audiencia autorizada.
- Solo una autorización Activa permite consulta familiar.
- Un usuario multirol actúa únicamente con el perfil activo.
- Doble pulsación debe ser idempotente.
- Una reserva directa exige exclusión transaccional del hueco.
- Cambiar URL o identificadores nunca amplía acceso.
- Un fallo técnico nunca se presenta como ausencia de información clínica.
- Solo puede existir un borrador basal activo por residente.
- Solo la cuenta creadora puede firmar su borrador basal.
- Un borrador cancelado no puede reabrirse ni firmarse.
- Solo puede existir una versión basal vigente por residente.
- Firma y sustitución de la versión vigente deben ejecutarse de forma atómica e idempotente.
- El total de Barthel debe coincidir con las diez respuestas y puntuaciones guardadas.
- Solo puede existir un intervalo de ubicación vigente por residente.
- Un cambio de ubicación cierra el intervalo anterior y abre el nuevo de forma atómica.
- La revocación de cuenta, grant, permiso o autorización se aplica a las nuevas peticiones aunque exista una sesión abierta.

# 10. Requisitos no funcionales

| Área | Requisito v0.4 |
| --- | --- |
| Disponibilidad | Objetivo y SLA a definir antes de producción |
| Rendimiento | Pantallas críticas utilizables con red móvil razonable |
| Escalabilidad | Diseño multicentro con aislamiento lógico verificable |
| Compatibilidad | Navegadores modernos; tableta y ordenador prioritarios; móvil adaptable |
| Accesibilidad | Objetivo WCAG 2.2 AA |
| Resiliencia | Borradores recuperables y liberación segura de edición |
| Concurrencia | Idempotencia en firmas, versiones basales, ubicaciones, valoraciones, publicaciones y reservas |
| Integridad | Estados explícitos, referencias coherentes y restricciones contra duplicidad o cruce de ámbitos |
| Observabilidad | Errores, rendimiento y seguridad sin texto clínico completo en logs |
| Mantenibilidad | Requisitos identificados, catálogos versionados, migraciones versionadas y pruebas reproducibles |
| Portabilidad | Dominio y autorización separados de la persistencia para evitar dependencia innecesaria del proveedor |

# 11. Privacidad, seguridad y protección de datos

- El prototipo utiliza exclusivamente datos ficticios. No se incorporarán nombres, contactos, documentos, capturas o historias reales.
- Antes de datos reales: definir responsable y encargado, finalidades y bases jurídicas; realizar análisis de riesgos y, previsiblemente, EIPD; formalizar contratos; política de conservación; derechos; gestión de brechas; control de proveedores y transferencias.
- Minimización y separación entre contenido asistencial, familiar, administrativo y de supervisión.
- Cifrado en tránsito y reposo, gestión segura de secretos, copias, recuperación y registros de auditoría antes de producción.
- Revocaciones de autorización, cuenta o permisos surten efecto en nuevas peticiones aunque exista sesión abierta.
- Sin publicidad, píxeles de marketing, grabaciones de sesión ni analítica invasiva en Portal Familiar.
- Logs técnicos sin contraseñas, tokens, secretos ni texto clínico completo.
- Identificadores opacos y no secuenciales; su conocimiento no concede autorización.
- La arquitectura técnica actual del prototipo no se considera validada para producción sanitaria por este PRD.
- Este PRD no aprueba todavía el esquema D1/Drizzle, la estrategia de separación por centro ni la operación productiva.

# 12. Criterios de aceptación transversales

- Los seis perfiles respetan la matriz de permisos vigente y su ámbito.
- Administración y Dirección Clínica aparecen como perfiles separados.
- Una cuenta con varios perfiles exige cambio explícito de perfil.
- Las autorizaciones se evalúan en servidor y deniegan por defecto.
- Los tres perfiles asistenciales pueden registrar un evento observado dentro de su ámbito, conservando su perfil real.
- Eventos vencidos permanecen abiertos y visibles.
- Lectura y ejecución de indicaciones son estados distintos.
- Enfermería y Medicina reutilizan el módulo de derivación.
- Familiar solo ve publicaciones aprobadas y publicadas.
- Administración no ve contenido clínico; Dirección solo lo ve con permiso clínico de lectura auditada.
- El centro puede activar/desactivar citas y elegir Reserva directa o Solicitud previa.
- Portal Familiar muestra únicamente el flujo configurado.
- Reserva directa impide doble ocupación del hueco.
- Solicitud previa exige propuesta y aceptación antes de confirmar.
- Las citas no permiten seleccionar profesional concreto ni crear chat clínico.
- Retirada excepcional exige permiso, causa y trazabilidad.
- Las pruebas negativas confirman que manipular URL, perfil o identificador no concede acceso.
- No existe ninguna referencia funcional activa a CFS.
- Barthel muestra diez ítems y total automático y conserva la versión común del catálogo.
- Solo existe un borrador basal activo por residente.
- Un profesional distinto del creador no puede firmar el borrador basal.
- La ausencia del creador exige cancelación motivada y un nuevo borrador.
- Firmar crea una única versión vigente y archiva la anterior sin sobrescribirla.
- Rectificar un basal firmado crea otra versión vinculada y nunca habilita edición del original.
- La regla de seis horas nunca permite editar un basal firmado.
- Auxiliar solo consulta basal vigente; Familiar y Administración no acceden; Dirección requiere permiso de lectura auditada.
- La ubicación actual coincide con el único intervalo vigente del historial.
- Provisionar un centro y administrar su estructura son capacidades separadas.

# 13. Riesgos y mitigaciones

| Riesgo | Impacto | Mitigación |
| --- | --- | --- |
| Carga de registro | Baja adopción | Acciones globales, formularios breves y medición de tiempos |
| Bandejas ignoradas | Pendientes sin valorar | Vencimientos visibles y responsabilidad compartida |
| Pérdida en relevo | Discontinuidad | Transferencia explícita y visibilidad de equipo |
| Confusión con urgencias | Retraso asistencial | Avisos claros y circuito directo fuera del portal |
| Comunicación inadecuada | Ansiedad o brecha | Texto separado y aprobación humana |
| Acceso familiar incorrecto | Brecha grave | Autorización activa, audiencia y pruebas negativas |
| Acceso directivo excesivo | Exposición innecesaria | Dos niveles, solo lectura, permiso específico y auditoría |
| Doble reserva | Conflicto operativo | Transacción, unicidad e idempotencia |
| Cambio de modo de citas | Estados incoherentes | No reescribir citas existentes; transición auditada |
| Firma basal por tercero | Falsa autoría | Propiedad explícita del borrador, firma server-side y prueba negativa |
| Dos basales vigentes | Historia clínica incoherente | Unicidad y sustitución atómica |
| Edición de basal firmado | Pérdida de trazabilidad | Inmutabilidad y rectificación como nueva versión |
| Ubicación sobrescrita | Contexto histórico falso | Historial temporal como única fuente de verdad |
| Cruce entre centros | Brecha grave | Ámbito relacional, denegación por defecto y pruebas de aislamiento |
| Arquitectura prematura | Riesgo de seguridad y coste | Consolidar contratos antes del esquema físico y revisar antes de datos reales |

# 14. Decisiones pendientes y puertas de salida

| Decisión | Responsable | Momento |
| --- | --- | --- |
| Vocabulario final y campo Desde cuándo | Producto + Auxiliar/Enfermería | Pruebas de usabilidad |
| Persona/canal/hora en aviso prioritario | Producto + centro + DPO | Tras usabilidad |
| Profesionales concretos con permiso de alta o basal | Centro piloto | Antes de permisos productivos |
| Catálogo administrativo de sexo documentado | Producto + centro + DPO | Antes del esquema definitivo |
| Identificador interno y niveles obligatorios de ubicación | Producto + centro | Antes del esquema definitivo |
| Traslados e inactivación/reactivación del residente | Producto + centro | Antes del esquema definitivo |
| Ausencia de ayuda técnica complementaria | Producto + Enfermería | Antes del esquema definitivo |
| Líquidos en alimentación exclusivamente enteral | Producto + Enfermería | Antes del esquema definitivo |
| Detalle obligatorio en `OTRA_TEXTURA_ADAPTADA` y `OTRO_PRODUCTO_DE_APOYO` | Producto + Enfermería | Antes del esquema definitivo |
| Cancelación de borrador, aportaciones y rectificaciones basales | Producto + centro + DPO | Antes del esquema definitivo |
| Política de corrección de notas clínicas y conservación | Centro + DPO | Antes de datos reales |
| Finalidades y retención de accesos clínicos de Dirección | Dirección clínica + DPO | Antes de datos reales |
| Criterios de actualización familiar relevante | Dirección clínica + centro | Antes del piloto real |
| Configuración real de citas, equipos y tiempos | Centro piloto | Antes del piloto en cada centro |
| Autenticación y segundo factor productivos | Seguridad + DPO | Antes de datos reales |
| SLA, RPO, RTO, backups e infraestructura | Responsable técnico + centro | Antes de producción |
| D1/Drizzle y estrategia de base compartida o separada | Producto + responsable técnico + DPO | Después de línea base funcional |

# 15. Hoja de ruta actualizada

| Fase | Resultado | Datos |
| --- | --- | --- |
| 1. Consolidación documental | Registro de decisiones + PRD v0.4 + matriz v0.2 + wireframes corregidos + trazabilidad | Ninguno |
| 2. Línea base funcional | Declaración formal de coherencia documental | Ninguno |
| 3. Decisión de persistencia | ADR D1/Drizzle y estrategia multicentro | Ficticios |
| 4. Dominio y autorización | Estados, entidades, RBAC/ABAC y pruebas | Ficticios |
| 5. Bloques verticales | Residente/basal -> Auxiliar -> Enfermería -> Medicina -> Familia -> Administración -> Dirección | Ficticios |
| 6. Integración | Flujos end-to-end, concurrencia, auditoría y accesibilidad | Ficticios |
| 7. Preparación de piloto | EIPD, contratos, seguridad, arquitectura y operación | Sin reales hasta autorización |
| 8. Piloto real | Centro/unidad y usuarios limitados | Reales solo tras puertas de salida |

# 16. Referencias

## 16.1 Fuentes funcionales del proyecto

- PRD Plataforma contacto familias v0.3 consolidado, 02/09/2026.
- Registro de decisiones posteriores al 2 de septiembre de 2026, v0.1, 05/09/2026.
- Contrato de datos mínimo de Residente y Basal `0002`, actualizado el 05/09/2026.
- Matriz de permisos de seis perfiles v0.1, 02/09/2026.
- Wireframe funcional Auxiliar v0.2, 02/09/2026.
- Wireframe funcional Enfermería v0.1.
- Wireframe funcional Medicina v0.1, 23/08/2026.
- Wireframe funcional Portal Familiar v0.1, 27/08/2026.
- Wireframe funcional Administración v0.1, 29/08/2026.
- Wireframe funcional Dirección/Coordinación Clínica v0.1, 01/09/2026.
- `AGENTS.md` v1.2, 02/09/2026.

## 16.2 Referencias normativas y técnicas de orientación

- Reglamento (UE) 2016/679 (RGPD), incluido el principio de protección de datos desde el diseño y por defecto: <https://eur-lex.europa.eu/ES/legal-content/summary/general-data-protection-regulation-gdpr.html>
- Agencia Española de Protección de Datos, evaluación de impacto y gestión del riesgo: <https://www.aepd.es/preguntas-frecuentes/2-tus-obligaciones-como-responsable-del-tratamiento/10-evaluacion-de-impacto>
- AEPD, herramienta Evalúa-Riesgo RGPD: <https://www.aepd.es/guias-y-herramientas/herramientas/evalua-riesgo-rgpd>
- OWASP Application Security Verification Standard: <https://owasp.org/www-project-application-security-verification-standard/>
- W3C Web Content Accessibility Guidelines 2.2: <https://www.w3.org/TR/WCAG22/>

# Anexo A. Cambios principales de v0.3 a v0.4

- Se incorpora el registro de decisiones posteriores al 2 de septiembre como fuente de consolidación.
- Se elimina CFS de forma completa y se retira `BAS-08` en su formulación anterior.
- Se adopta `BARTHEL_COMUN_V0_1` para todos los centros, con diez ítems y total automático.
- Se mantiene el catálogo basal de nueve áreas y se concretan movilidad, alimentación, fuente y fecha.
- Se formaliza un único borrador basal activo por residente.
- Se limita la firma del borrador a su creador y se regulan aportaciones de terceros y cancelación motivada.
- Se define el basal firmado como inmutable y la rectificación como nueva versión vinculada.
- La ventana de seis horas queda limitada a cursos/notas clínicas y nunca se aplica al basal.
- Se define el historial de ubicación como única fuente de la ubicación actual.
- Se separa el provisionamiento de centros, realizado por la plataforma, de la administración de su estructura subordinada.
- Se refuerzan denegación por defecto, datos server-side confiables y auditoría previa a lecturas clínicas de Dirección.
- Se actualizan hoja de ruta, riesgos, criterios de aceptación y puertas previas al esquema D1/Drizzle.

# Anexo B. Identificadores nuevos de v0.4

| Grupo | Identificadores añadidos |
| --- | --- |
| Autenticación | `AUTH-06`, `AUTH-07` |
| Organización | `ORG-05` |
| Residente y ubicación | `RES-03` a `RES-05` |
| Basal | `BAS-09` a `BAS-19` |
| Auxiliar | `AUX-13` |
| Enfermería | `ENF-13` |
| Medicina | `MED-13` |
| Administración | `ADM-11` |
| Dirección | `DIR-11` |
| Historial y auditoría | `HIS-03`, `AUD-03` |

> **Advertencia final.** Este PRD define el producto y sus límites funcionales. No constituye protocolo clínico, historia clínica homologada, EIPD, asesoramiento jurídico, certificación de seguridad ni autorización para tratar datos sanitarios reales.
