# Matriz de trazabilidad funcional

**Versión:** 0.2  
**Fecha:** 2026-09-06  
**Estado:** Aprobada para la línea base funcional v1.1  
**Cadena:** requisito → permiso → pantalla → entidad → prueba  
**Fuente canónica:** Markdown  

## 1. Documentos de referencia

1. Registro de decisiones posteriores al 02/09/2026 v0.1.
2. PRD Plataforma de contacto con familias v0.5 consolidado.
3. Matriz de permisos de seis perfiles v0.2.1.
4. Wireframes canónicos: Auxiliar v0.2, Enfermería v0.3, Medicina v0.3, Portal Familiar v0.2, Administración v0.3 y Dirección/Coordinación Clínica v0.1.
5. Contrato de datos de residente y basal `0002`, pendiente de traducción a esquema físico D1/Drizzle.

## 2. Convenciones

La columna **Permiso/condición** expresa autorización funcional, no nombres definitivos del esquema técnico.

| Código | Significado |
| --- | --- |
| `BASE` | Cuenta y sesión activas, perfil activo explícito, ámbito aplicable y autorización server-side; denegación por defecto. |
| `ASIG` | `BASE` más asignación, tarea o residente vinculado, según el perfil. |
| `AUT-ACTIVA` | Autorización familiar activa para el residente. |
| `AUTOR` | La cuenta autenticada es autora del objeto y opera con el perfil exigido. |
| `ESTADO` | El objeto se encuentra en un estado que permite la operación. |
| `CONFIG` | Configuración vigente del centro habilita la función o modalidad. |
| `AUDIT` | Acceso o acción condicionado a auditoría append-only. |
| `NO` | Capacidad expresamente prohibida para los seis perfiles. |

Los códigos `RESIDENT_IDENTITY_CREATE`, `BASELINE_INITIAL_COMPLETE`, `BASELINE_REEVALUATE`, `BASELINE_DRAFT_CONTRIBUTE`, `CLINICAL_DETAIL_READ` y `PUBLICATION_EXCEPTIONAL_WITHDRAW` son los únicos permisos configurables nominalmente aprobados en la matriz v0.2. Las restantes expresiones son condiciones o capacidades funcionales que deberán traducirse al contrato técnico sin ampliar alcance.

**Entidades:** son entidades lógicas. No implican todavía aprobación del esquema D1/Drizzle.

**Pruebas:** cada identificador `TR-*` representa como mínimo una prueba positiva cuando la capacidad existe y una prueba negativa de ámbito, perfil, autoría o estado cuando proceda.

## 3. Autenticación, organización y permisos

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `AUTH-01` | `BASE` | FAM-01; ADM-30; cuenta de cada perfil | `Account`, `ProfileGrant` | `TR-AUTH-01`: cada actor entra con cuenta individual; se rechaza cuenta compartida/inactiva. |
| `AUTH-02` | `BASE` + ámbito/permiso específico | Todas las protegidas; DIR-17/18 | `Account`, `Session`, `ProfileGrant`, `ScopeGrant` | `TR-AUTH-02`: servidor ignora identidad/perfil/fecha enviados por cliente y deniega fuera de ámbito. |
| `AUTH-03` | `BASE` + sesión válida | FAM-01/18; ADM-30 | `Session`, `AccountSecurityEvent` | `TR-AUTH-03`: caducidad, revocación y bloqueo impiden la siguiente operación sin revelar cuentas. |
| `AUTH-04` | `BASE` + segundo factor | FAM-02; ADM-30; acceso profesional | `SecondFactorChallenge` | `TR-AUTH-04`: datos reales inaccesibles antes del segundo factor válido. |
| `AUTH-05` | `BASE` + perfil explícito | ADM-13/30; DIR-17 | `AccountProfile`, `ActiveProfileContext` | `TR-AUTH-05`: cuenta multirol no combina grants y cada cambio renueva contexto. |
| `AUTH-06` | `BASE` | FAM-15/18; DIR-18; todas las rutas | Recursos protegidos | `TR-AUTH-06`: manipular URL/ID/centro/unidad/residente no amplía acceso. |
| `AUTH-07` | `BASE` + `AUDIT` | ADM-28; DIR-14/18 | `PermissionGrant`, `AuditEntry` | `TR-AUTH-07`: ausencia de concesión deniega; concesiones/revocaciones dejan registro append-only. |
| `ORG-01` | Plataforma provisiona; Administración `BASE` en centro | ADM-05/12/13/29 | `Center`, `OrgUnit`, `ProfileGrant` | `TR-ORG-01`: Administración no crea centros y solo gestiona subordinados de centros autorizados. |
| `ORG-02` | `BASE` administrativo | ADM-07/13/14 | `OrgPosition`, `ShiftAssignment`, `ProfileGrant` | `TR-ORG-02`: asignar cargo o turno no concede perfil ni permiso. |
| `ORG-03` | `BASE` administrativo + validación jerárquica | ADM-05/06/29 | `Center`, `Unit`, `Building`, `Floor`, `Room`, `Bed` | `TR-ORG-03`: centro/unidad obligatorios; niveles opcionales respetan configuración y rama. |
| `ORG-04` | Administración `BASE` | ADM-14/15/16/17 | `ShiftPattern`, `ShiftOccurrence`, `ShiftException` | `TR-ORG-04`: recurrencias/excepciones se guardan y el conflicto se muestra sin decisión automática. |
| `ORG-05` | `BASE`; sin concesión clínica implícita | ADM-05/07/12/13/29; DIR-18 | `ScopeGrant`, `ProfileGrant` | `TR-ORG-05`: crear estructura/usuario/turno no permite abrir contenido clínico. |

## 4. Residente, ubicación y estado basal

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `RES-01` | `BASE` según perfil/ámbito | ADM-03/04; ENF-17/18; MED-19/20 | `Resident` | `TR-RES-01`: edad se calcula; los ID no autorizan; sexo admite solo `male/female/other/unknown`, no se infiere y no acepta texto libre. |
| `RES-02` | Administración `BASE`; Enfermería `RESIDENT_IDENTITY_CREATE`; Medicina `NO` | ADM-04; ENF-19 | `Resident`, `PermissionGrant` | `TR-RES-02`: alta administrativa deja basal pendiente; Medicina y Enfermería sin permiso reciben denegación. |
| `RES-03` | Lectura de residente autorizada | ADM-03/04; ENF-18; MED-20 | `Resident.birthDate` | `TR-RES-03`: edad cambia con fecha de consulta sin campo de edad persistido. |
| `RES-04` | Administración `BASE`; lectura según ámbito | ADM-03/05/06; ENF-17/18; MED-19/20 | `LocationInterval` | `TR-RES-04`: traslado cierra intervalo previo y abre exactamente uno vigente de forma atómica. |
| `RES-05` | Administración `BASE` + jerarquía válida | ADM-03/05/06 | `LocationInterval`, estructura organizativa | `TR-RES-05`: se rechaza ubicación cuyos niveles no pertenecen al centro/unidad seleccionados. |
| `BAS-01` | Enfermería/Medicina con permiso basal; Auxiliar lectura asignada; Dirección lectura condicionada | ENF-20; MED-21; AUX-03; DIR-05 | `BaselineVersion`, `BaselineAreaAnswer` | `TR-BAS-01`: existen exactamente nueve áreas, sin décima área ni funcionalidad duplicada. |
| `BAS-02` | Igual que `BAS-01`; CFS/Pfeiffer `NO` | ENF-20/21/22 retirada; MED-21; AUX-03; DIR-05 | `BaselineVersion`, `BarthelAssessment` | `TR-BAS-02`: Barthel está presente y CFS/Pfeiffer ausentes de UI, API, informe y permisos. |
| `BAS-03` | Borrador propio + permiso basal | ENF-21; MED-21 | `BarthelAssessment`, `BarthelItemAnswer` | `TR-BAS-03`: diez respuestas/puntuaciones persistidas producen total automático correcto sobre 100. |
| `BAS-04` | Borrador propio + permiso basal | ENF-20/23; MED-21 | `BaselineCognition`, `DocumentedSource` | `TR-BAS-04`: etiología/GDS exigen documentación, fuente y fecha; no determinada se admite. |
| `BAS-05` | Lectura según perfil; histórico no Auxiliar | ENF-18/25; MED-20/21; AUX-03; DIR-05/15 | `BaselineVersion` | `TR-BAS-05`: por defecto solo vigente; históricas archivadas y accesibles solo con permiso. |
| `BAS-06` | `BASELINE_INITIAL_COMPLETE` o `BASELINE_REEVALUATE` | ENF-20; MED-21 | `BaselineDraft.reason` | `TR-BAS-06`: solo alta, revisión programada o cambio consolidado crean versión ordinaria. |
| `BAS-07` | `BASELINE_INITIAL_COMPLETE` / `BASELINE_REEVALUATE` + ámbito | ENF-18/20; MED-20/21 | `PermissionGrant`, `BaselineDraft` | `TR-BAS-07`: mismo profesional permitido en un centro y denegado en otro sin grant. |
| `BAS-08` RETIRADO | `NO` | ENF-22 retirada | Ninguna; capacidad prohibida | `TR-BAS-08`: búsqueda estructural confirma cero activo/campo/puntuación/permiso/resumen CFS. |
| `BAS-09` | Permiso basal + `ESTADO` | ENF-20/23; MED-21 | `BaselineDraft` | `TR-BAS-09`: índice único impide segundo borrador activo; incompleto no firma. |
| `BAS-10` | `AUTOR` + mismo perfil + permiso basal | ENF-23; MED-21 | `BaselineDraft`, `BaselineSignature` | `TR-BAS-10`: creador firma; otra cuenta o perfil del mismo usuario no puede firmar. |
| `BAS-11` | `BASELINE_DRAFT_CONTRIBUTE` + ámbito | ENF-20/23; MED-21 | `BaselineContribution` | `TR-BAS-11`: aportación conserva autor y no cambia creador/derecho de firma. |
| `BAS-12` | Creador + motivo; sustitución por tercero bloqueada | ENF-20/23; MED-21 | `BaselineDraftCancellation` | `TR-BAS-12`: cancelado no reabre/firma; nuevo profesional crea borrador nuevo; actor excepcional sigue bloqueado. |
| `BAS-13` | `AUTOR` + permiso + validación completa + `ESTADO` | ENF-23; MED-21 | `BaselineVersion`, `BaselineSignature` | `TR-BAS-13`: firma congela contenido/contexto y sustituye vigente atómica e idempotentemente. |
| `BAS-14` | Firmado inmutable; rectificación bloqueada hasta definir permiso | ENF-25; MED-21/23; DIR-15 | `BaselineVersionLink` | `TR-BAS-14`: PUT/DELETE y corrección de 6 h sobre firmado se deniegan; nueva versión queda vinculada. |
| `BAS-15` | Borrador propio + permiso basal | ENF-20/23; MED-21 | `BaselineCatalogVersion`, `BaselineAreaAnswer` | `TR-BAS-15`: nueve respuestas obligatorias; `NO_DOCUMENTADO` no se normaliza; catálogo queda versionado. |
| `BAS-16` | Borrador propio + permiso basal | ENF-20; MED-21 | `MobilityBaselineAnswer` | `TR-BAS-16`: modo, ayuda técnica, humana y transferencias son distintos; `NINGUNA` difiere de `NO_DOCUMENTADO`, se rechaza `NO_APLICA` y `OTRA` exige texto. |
| `BAS-17` | Borrador propio + permiso basal | ENF-20; MED-21 | `FeedingBaselineAnswer` | `TR-BAS-17`: `NO_APLICA` en textura/líquidos solo es válido con vía exclusivamente enteral; una vía mixta exige dato oral o `NO_DOCUMENTADO`. |
| `BAS-18` | Borrador propio + permiso basal | ENF-20/23; MED-21 | `BaselineVersion`, `DocumentedSource` | `TR-BAS-18`: fuente/fecha comunes; incompatibles excluyentes; todo `OTRO/OTRA` exige texto recortado no vacío y el texto sin opción se rechaza. |
| `BAS-19` | Lectura/escritura basal autorizada | ENF-20/21/23; MED-21; AUX-03; DIR-05 | `BaselineVersion`, `BarthelAssessment` | `TR-BAS-19`: no existe total de nueve áreas; Barthel mantiene total independiente. |

## 5. Auxiliar y registro cotidiano

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `AUX-01` | `ASIG` | AUX-01 | `CareAssignment`, `DailyRecord` | `TR-AUX-01`: solo residentes asignados y estado de cierre del turno. |
| `AUX-02` | `ASIG` + `ESTADO` | AUX-02/04/05/06 | `DailyRecord` | `TR-AUX-02`: tres acciones excluyentes y una sola transición final. |
| `AUX-03` | `ASIG` + firma | AUX-04 | `DailyRecord` | `TR-AUX-03`: Sin cambios firma/cierra y no crea tarea de Enfermería. |
| `AUX-04` | `ASIG` + motivo | AUX-05 | `DailyRecord` | `TR-AUX-04`: No valorable sin motivo falla y nunca se codifica como estabilidad. |
| `AUX-05` | `ASIG` | AUX-06/07 | `Observation`, `ObservationArea` | `TR-AUX-05`: acepta una o varias de las diez áreas exactas; cero áreas falla. |
| `AUX-06` | `ASIG` | AUX-07 | `ObservationAreaAnswer` | `TR-AUX-06`: opciones rápidas aprobadas incluyen Atragantamiento y texto según área. |
| `AUX-07` | `ASIG` | AUX-08/09 | `Observation`, `VitalMeasurement` | `TR-AUX-07`: temperatura opcional; hora de registro servidor; observación exacta no obligatoria. |
| `AUX-08` | `ASIG` | AUX-10 | `Observation.initialPriority` | `TR-AUX-08`: ordinario/prioritario enruta bandeja sin diagnóstico automático. |
| `AUX-09` | `ASIG` | AUX-10 | `PriorityReason` | `TR-AUX-09`: seis motivos prioritarios exactos producen ruta prioritaria. |
| `AUX-10` | `ASIG` + aviso documentado | AUX-11B/12 | `DirectAlertRecord` | `TR-AUX-10`: prioritario muestra protocolo y exige confirmación del aviso definida para prototipo. |
| `AUX-11` | `ASIG` + firma | AUX-11A/12 | `Observation`, `ClinicalEvent` | `TR-AUX-11`: firma conserva autoría y crea un único envío a la bandeja correcta. |
| `AUX-12` | `ASIG`; campos P1 no bloqueantes | AUX-09/11B | `ObservationTiming`, `DirectAlertRecord` | `TR-AUX-12`: “Desde cuándo” y detalle de aviso pueden omitirse en prototipo sin perder el evento. |
| `AUX-13` | `ASIG` + lectura basal vigente | AUX-03 | `BaselineVersion` | `TR-AUX-13`: ve vigente asignado; histórico, borrador, escritura, firma y rectificación se deniegan. |

## 6. Enfermería

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `ENF-01` | `BASE` + unidad | ENF-01/02/03/08/13/14 | `ClinicalWorkQueue` | `TR-ENF-01`: cinco bandejas comparten elementos por unidad y excluyen fuera de ámbito. |
| `ENF-02` | `BASE` + estado | ENF-01/02/03/08/24 | `ClinicalEvent` | `TR-ENF-02`: cerrado sale de bandeja; abierto vencido permanece y figura en Historial al cerrar. |
| `ENF-03` | `BASE` + unidad + concurrencia | ENF-04 | `AssessmentWorkSession` | `TR-ENF-03`: iniciar registra actor/hora; otro turno puede continuar sin propiedad permanente. |
| `ENF-04` | Lectura clínica en ámbito | ENF-04/18/24 | `Observation`, `BaselineVersion`, `TimelineEntry` | `TR-ENF-04`: observación no editable; basal vigente visible; timeline bajo permiso. |
| `ENF-05` | Enfermería `BASE` + evento | ENF-05 | `NursingAssessment`, `VitalMeasurement` | `TR-ENF-05`: guarda campos y constantes opcionales con unidades, sin exigirlas. |
| `ENF-06` | Enfermería `BASE` + evento abierto | ENF-06/07A/07B/10/11 | `ClinicalEventTransition` | `TR-ENF-06`: cada salida válida cambia estado una vez y ninguna se decide automáticamente. |
| `ENF-07` | Enfermería `BASE` | ENF-07B/08 | `NursingFollowUp`, `FollowUpEntry` | `TR-ENF-07`: exige fecha/criterio y cada actuación conserva autoría. |
| `ENF-08` | Enfermería `BASE` + unidad | ENF-09 | `ShiftHandover` | `TR-ENF-08`: transferencia explícita no oculta el evento aunque falte recepción. |
| `ENF-09` | Enfermería `BASE` | ENF-10; MED-02/03 | `MedicalEscalation` | `TR-ENF-09`: transmite fuentes completas sin resumen/decisión diagnóstica automática. |
| `ENF-10` | Enfermería ejecución; Medicina emisión | ENF-13; MED-07/08/09 | `MedicalInstruction`, `InstructionEvent` | `TR-ENF-10`: lectura no marca realización; no realizada exige incidencia. |
| `ENF-11` | Enfermería `BASE` + residente | ENF-16 | `Observation`, `ClinicalEvent` | `TR-ENF-11`: evento propio conserva autoría de Enfermería y no suplanta Auxiliar. |
| `ENF-12` | Enfermería cierra + audiencia autorizada | ENF-07A/14/15 | `FamilyPublication` | `TR-ENF-12`: al cerrar decide comunicación y aprobación separada; sin audiencia no publica. |
| `ENF-13` | `BASELINE_INITIAL_COMPLETE`, `BASELINE_REEVALUATE` o `BASELINE_DRAFT_CONTRIBUTE`; alta separada | ENF-18/19/20/21/23/25 | Entidades basales | `TR-ENF-13`: permiso basal aplicable habilita su operación, pero no ENF-19 sin `RESIDENT_IDENTITY_CREATE`. |

## 7. Medicina

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `MED-01` | Medicina `BASE` + ámbito | MED-01 | `ClinicalWorkQueue` | `TR-MED-01`: inicio contiene solo módulos y elementos autorizados. |
| `MED-02` | Medicina `BASE` + escalado | MED-02/03 | `MedicalEscalation` | `TR-MED-02`: muestra motivo/constantes/actuaciones/tiempo sin diagnóstico automático. |
| `MED-03` | Lectura clínica en ámbito | MED-03/24 | `Observation`, `NursingAssessment`, `TimelineEntry` | `TR-MED-03`: fuentes de Enfermería inmutables; timeline plegada y autorizada. |
| `MED-04` | Medicina `BASE` + evento | MED-04/05 | `MedicalAssessment`, `VitalMeasurement` | `TR-MED-04`: registra exploración/valoración/constantes opcionales/actuaciones con autoría. |
| `MED-05` | Medicina `BASE` + evento abierto | MED-06/07/10/13/15 | `MedicalPlan`, `ClinicalEventTransition` | `TR-MED-05`: cierre/seguimiento/urgente son decisiones explícitas y excluyentes. |
| `MED-06` | Medicina `BASE` | MED-07 | `MedicalInstruction` | `TR-MED-06`: exige texto y fecha/criterio; no existe prioridad automática. |
| `MED-07` | Medicina `BASE` + juicio profesional | MED-10/11 | `MedicalFollowUp` | `TR-MED-07`: resultado pendiente mantiene evento abierto sin inferencia del sistema. |
| `MED-08` | Medicina `BASE` | MED-09/11 | `MedicalFollowUp` | `TR-MED-08`: vencido sigue visible hasta transición válida. |
| `MED-09` | Medicina `BASE` + fin de turno | MED-12 | `ShiftHandover`, `MedicalFollowUp` | `TR-MED-09`: permite transferencia o próxima revisión y conserva trazabilidad. |
| `MED-10` | Medicina `BASE` + cierre | MED-15; ENF-24 | `ClinicalEvent` | `TR-MED-10`: cierre médico es único y no solicita segundo cierre enfermero. |
| `MED-11` | Medicina `BASE` + residente | MED-18 | `Observation`, `ClinicalEvent` | `TR-MED-11`: evento médico continúa en Medicina sin reenvío artificial. |
| `MED-12` | Medicina cierra + audiencia autorizada | MED-16/17 | `FamilyPublication` | `TR-MED-12`: cierre médico decide y aprueba comunicación separada. |
| `MED-13` | `BASELINE_INITIAL_COMPLETE`, `BASELINE_REEVALUATE` o `BASELINE_DRAFT_CONTRIBUTE`; alta `NO` | MED-19/20/21 | Entidades basales | `TR-MED-13`: cada operación del módulo común exige su permiso; alta administrativa siempre denegada a Medicina. |

## 8. Derivación a Urgencias

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `DER-01` | Enfermería/Medicina `BASE` + competencia | ENF-11/12; MED-13/14 | `EmergencyReferral` | `TR-DER-01`: ambos perfiles usan el mismo contrato funcional y validaciones. |
| `DER-02` | Autor competente + vista previa | ENF-12; MED-14 | `ReferralReportDraft`, `SignedDocument` | `TR-DER-02`: firma bloqueada sin vista previa; editar informe no altera fuentes. |
| `DER-03` | Lectura clínica y firma competente | ENF-12; MED-14 | `ReferralReportSnapshot` | `TR-DER-03`: contiene campos aprobados/Barthel y no contiene CFS. |
| `DER-04` | Interno clínico; informe externo excluye | ENF-11/12; MED-13/14 | `ExternalContactEvent` | `TR-DER-04`: contactos/horas existen en trazabilidad, no en PDF externo. |
| `DER-05` | Firma competente; descarga auditada en real | ENF-12/24; MED-14/22 | `SignedDocument`, `DocumentAccessAudit` | `TR-DER-05`: PDF inmutable vinculado al evento; impresión/descarga genera auditoría. |
| `DER-06` | Derivación + comunicación autorizada | ENF-14/15; MED-16/17; FAM-10 | `FamilyPublication`, `UrgentContactAttempt` | `TR-DER-06`: crea relevante e intento telefónico sin bloquear atención. |

## 9. Publicaciones y Portal Familiar

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `FAM-01` | `AUT-ACTIVA` | FAM-03/15/17; ADM-08/10/11 | `FamilyPerson`, `ResidentAuthorization` | `TR-FAM-01`: persona y autorización son distintas; solo Activa concede acceso. |
| `FAM-02` | `AUT-ACTIVA` + audiencia + publicada | FAM-04/05/06/08/09 | `FamilyPublication`, `PublicationAudience` | `TR-FAM-02`: borrador/aprobada no publicada/otra audiencia no se devuelve. |
| `FAM-03` | `NO` para contenido interno | FAM-04/06/15/18 | Entidades clínicas protegidas | `TR-FAM-03`: API/URL nunca exponen basal, escalas, notas, derivación o timeline. |
| `FAM-04` | Administración `BASE` configura; familia lee | ADM-18/19; FAM-04/05/08/09 | `PublicationSchedule` | `TR-FAM-04`: diaria/semanal/solo relevantes respeta día y hora configurados. |
| `FAM-05` | Profesional cierra/aprueba + `ESTADO`; programación automática | ENF-15; MED-17; ADM-20; FAM-06/08 | `FamilyPublication` | `TR-FAM-05`: sin aprobación no publica; al horario publica contenido existente una vez. |
| `FAM-06` | Profesional autorizado + aprobación | ENF-14/15; MED-16/17; FAM-09 | `FamilyPublication` | `TR-FAM-06`: relevante solo por decisión humana y audiencia válida. |
| `FAM-07` | `AUT-ACTIVA` + ventana de visibilidad | FAM-05 | `PublicationVisibilityPolicy` | `TR-FAM-07`: ordinaria desaparece a 30 días y relevante a 6 meses sin borrado interno. |
| `FAM-08` | Original inmutable; correctora por profesional autorizado | FAM-06/11; ENF-15; MED-17 | `PublicationCorrectionLink` | `TR-FAM-08`: original no cambia y correctora enlaza al original. |
| `FAM-09` | Administración `PUBLICATION_EXCEPTIONAL_WITHDRAW` + motivo + `AUDIT` | ADM-27/28; FAM-12 | `PublicationWithdrawal`, `AuditEntry` | `TR-FAM-09`: causa/permiso obligatorios; portal retira y original interno persiste. |
| `FAM-10` | Capacidades no habilitadas | FAM-04/06/13/14 | `FamilyPortalCapability` | `TR-FAM-10`: no hay email/SMS/WhatsApp/push/chat/comentarios/descarga específica. |
| `FAM-11` | `AUT-ACTIVA` | FAM-07/18 | Estado de presentación | `TR-FAM-11`: vacío usa texto neutral y fallo técnico no simula “todo bien”. |
| `FAM-12` | Publicación visible | FAM-06/08/09/10/11 | `FamilyPublication.visibleAuthorLabel` | `TR-FAM-12`: siempre muestra “Equipo asistencial del centro”, no autor individual. |

## 10. Citas familiares

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `CIT-01` | Administración `BASE`; `CONFIG` única | ADM-21; FAM-13 | `AppointmentServiceConfig` | `TR-CIT-01`: servicio desactivado o exactamente un modo `DIRECT`/`REQUEST`. |
| `CIT-02` | `CONFIG` desde servidor + `AUT-ACTIVA` | FAM-13/14 | `AppointmentServiceConfig` | `TR-CIT-02`: portal muestra un solo flujo y no acepta modo manipulado por cliente. |
| `CIT-03` | `AUT-ACTIVA` + residente vinculado | FAM-13/14/15 | `Appointment`, `AppointmentRequest` | `TR-CIT-03`: revocación impide crear/leer/cambiar/cancelar desde siguiente petición. |
| `CIT-04` | `AUT-ACTIVA` + `CONFIG` | FAM-13; ADM-21/22 | `CareTeam`, `AppointmentModality` | `TR-CIT-04`: elige equipo/modalidad habilitada, nunca profesional concreto. |
| `CIT-05` | Acceso a citas | FAM-13/14 | Contenido de interfaz | `TR-CIT-05`: aviso no urgente visible en todas las etapas de ambos modos. |
| `CIT-D01` | `AUT-ACTIVA` + `CONFIG=DIRECT` | FAM-13; ADM-22/23 | `AppointmentSlot` | `TR-CIT-D01`: solo devuelve huecos libres del equipo/modalidad elegidos. |
| `CIT-D02` | `AUT-ACTIVA` + hueco disponible | FAM-13; ADM-24 | `Appointment` | `TR-CIT-D02`: revisar/confirmar crea cita confirmada sin propuesta previa. |
| `CIT-D03` | Igual que D02 + transacción/idempotencia | FAM-13; ADM-24 | `AppointmentSlotReservation` | `TR-CIT-D03`: concurrencia y doble pulsación crean como máximo una reserva por hueco. |
| `CIT-D04` | Hueco ya ocupado | FAM-13 | `AppointmentSlot` | `TR-CIT-D04`: conflicto no crea cita y devuelve alternativas actuales. |
| `CIT-D05` | `AUT-ACTIVA` + cita propia | FAM-14; ADM-25/26 | `AppointmentTransition` | `TR-CIT-D05`: reprogramar usa hueco libre; cancelar confirma y audita. |
| `CIT-S01` | `AUT-ACTIVA` + `CONFIG=REQUEST` | FAM-13 | `AppointmentRequest` | `TR-CIT-S01`: valida equipo/modalidad/motivo/disponibilidad; comentario es opcional. |
| `CIT-S02` | Administración `BASE` | ADM-23/25/26; FAM-14 | `AppointmentProposal` | `TR-CIT-S02`: solicitud queda pendiente; propuesta permite asignación interna. |
| `CIT-S03` | `AUT-ACTIVA` + solicitud propia | FAM-14; ADM-25/26 | `AppointmentRequestTransition` | `TR-CIT-S03`: aceptar/cambiar/cancelar son transiciones válidas sin chat. |
| `CIT-S04` | `AUT-ACTIVA` + propuesta vigente | FAM-14; ADM-26 | `Appointment`, `AppointmentProposal` | `TR-CIT-S04`: solo aceptación crea cita confirmada; propuesta sola no. |
| `CIT-C01` | Administración `BASE` | ADM-21/22 | `AppointmentServiceConfig`, `AvailabilityRule` | `TR-CIT-C01`: equipos/modalidades/franjas/duración/antelación/horizonte/bloqueos persisten. |
| `CIT-C02` | Administración `BASE` + configuración versionada | ADM-21/26; FAM-13/14 | `AppointmentServiceConfigVersion` | `TR-CIT-C02`: cambio de modo rige nuevas operaciones y conserva citas previas. |
| `CIT-C03` | Actor autorizado + hora servidor | FAM-14; ADM-24/25/26/28 | `AppointmentAuditEvent` | `TR-CIT-C03`: todas las transiciones conservan actor y fecha/hora confiable. |

## 11. Administración

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `ADM-01` | Administración `BASE` | ADM-01 | Panel administrativo | `TR-ADM-01`: panel contiene asuntos administrativos y cero contenido asistencial. |
| `ADM-02` | Administración `BASE` | ADM-02/03/04/05/06 | `Resident`, `LocationInterval` | `TR-ADM-02`: gestiona identidad/ubicación sin recuperar basal o historia clínica. |
| `ADM-03` | Administración `BASE` | ADM-08/09/10/11 | `FamilyPerson`, `ResidentAuthorization`, `UrgentContactDesignation` | `TR-ADM-03`: alta de familiar no activa acceso; designación y estado se trazan. |
| `ADM-04` | Administración `BASE` + ámbito | ADM-12/13 | `Account`, `AccountProfile`, `ScopeGrant` | `TR-ADM-04`: crea/activa/suspende y asigna varios perfiles sin combinarlos. |
| `ADM-05` | Administración `BASE` | ADM-18/19 | `PublicationSchedule` | `TR-ADM-05`: configura frecuencia/horario, pero endpoints de redacción/aprobación deniegan. |
| `ADM-06` | Administración `BASE`; texto no visible por defecto | ADM-20 | `FamilyPublication.status` | `TR-ADM-06`: ve estado/técnica sin texto completo ni acceso clínico. |
| `ADM-07` | Administración `BASE` | ADM-21/22/23/24/25/26 | Entidades de citas | `TR-ADM-07`: gestiona ambos modos según `CIT-*` sin mezclarlos. |
| `ADM-08` | `PUBLICATION_EXCEPTIONAL_WITHDRAW` + motivo + `AUDIT` | ADM-27/28 | `PublicationWithdrawal` | `TR-ADM-08`: sin permiso o causa válida se deniega la retirada. |
| `ADM-09` | Administración `BASE` + `AUDIT` | ADM-28 | `AuditEntry` | `TR-ADM-09`: auditoría administrativa cubre categorías aprobadas y respeta ámbito. |
| `ADM-10` | Basal/historia clínica `NO` | ADM-01/03/20/28 | Entidades clínicas protegidas | `TR-ADM-10`: UI/API no entregan basal ni texto clínico; no modifica registros profesionales. |
| `ADM-11` | Administración `BASE` en centro provisionado + `AUDIT` | ADM-05/06/29 | Estructura organizativa | `TR-ADM-11`: gestiona subordinados; crear/reorganizar no concede clínica ni crea centro. |

## 12. Dirección / Coordinación Clínica

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `DIR-01` | Perfil Dirección explícito | DIR-01/17; ADM-30 | `AccountProfile`, `ActiveProfileContext` | `TR-DIR-01`: perfil separado no hereda Administración, Enfermería o Medicina. |
| `DIR-02` | Dirección `BASE` + ámbito | DIR-01/02/03/09/12/13 | `SupervisionProjection` | `TR-DIR-02`: supervisa estados aprobados sin acciones clínicas. |
| `DIR-03` | Dirección `BASE` + necesidad operativa | DIR-03/04 | `SupervisionCase` | `TR-DIR-03`: identifica episodio cuando procede sin cargar notas completas. |
| `DIR-04` | `CLINICAL_DETAIL_READ` + finalidad + ámbito + `AUDIT` | DIR-05/06/07/14/15/18 | Entidades clínicas protegidas | `TR-DIR-04`: auditoría se escribe antes de devolver detalle; sin condición se deniega. |
| `DIR-05` | Escritura clínica `NO` | DIR-04/05/18 | `ClinicalEvent` y derivados | `TR-DIR-05`: valorar/indicar/ejecutar/escalar/corregir/cerrar devuelve denegación. |
| `DIR-06` | `CLINICAL_DETAIL_READ` + `AUDIT` | DIR-12 | `EmergencyReferral`, `SignedDocument` | `TR-DIR-06`: puede leer autorizado, nunca generar o firmar. |
| `DIR-07` | Dirección lectura de estado; edición `NO` | DIR-13 | `FamilyPublication.status` | `TR-DIR-07`: ve estado sin redactar/modificar/aprobar. |
| `DIR-08` | Dirección `BASE` + ámbito/periodo | DIR-08/09/10/11 | `AggregateIndicator` | `TR-DIR-08`: indicador incluye denominador y no emite predicción/juicio clínico. |
| `DIR-09` | `NO` para ranking individual | DIR-08/09/10/16 | `AggregateIndicator` | `TR-DIR-09`: consultas/reportes rechazan desglose nominativo de productividad. |
| `DIR-10` | Dirección `BASE`; historias masivas `NO` | DIR-16 | `AggregateReport` | `TR-DIR-10`: exporta agregado autorizado y bloquea historias individuales en masa. |
| `DIR-11` | `CLINICAL_DETAIL_READ` + finalidad + ámbito + `AUDIT` | DIR-05/06/15 | `BaselineVersion`, `AuditEntry` | `TR-DIR-11`: vigente/histórico solo lectura; cada acceso auditado; CFS ausente. |

## 13. Historial, correcciones y auditoría

| Requisito | Permiso/condición | Pantalla | Entidad lógica | Prueba trazable |
| --- | --- | --- | --- | --- |
| `HIS-01` | Lectura de Historial según perfil/ámbito | ENF-24; MED-22; DIR-07 | `ClinicalEvent` | `TR-HIS-01`: evento cerrado desaparece de bandeja y permanece en Historial autorizado. |
| `HIS-02` | Autorización clínica específica | ENF-04/24; MED-03/24; DIR-06 | `TimelineEntry` | `TR-HIS-02`: timeline plegada; fuera de permiso no se recupera contenido. |
| `HIS-03` | Lectura histórica autorizada | ENF-24/25; MED-22/24; DIR-06/15 | `EventContextSnapshot`, `BaselineVersion`, `LocationInterval` | `TR-HIS-03`: evento antiguo conserva basal/ubicación de su fecha pese a cambios posteriores. |
| `COR-01` | `AUTOR` + objeto habilitado + ventana configurada | ENF-25; MED-23; DIR-15 lectura | `ClinicalNoteRevisionPolicy`, `ClinicalNote` | `TR-COR-01`: autor corrige nota habilitada dentro de 6 h; basal/publicación/inmutable se deniegan. |
| `COR-02` | `AUTOR` + política por tipo; basal según permiso pendiente | ENF-25; MED-21/23; FAM-11; DIR-15 | `RectificationLink`, `PublicationCorrectionLink`, `BaselineVersionLink` | `TR-COR-02`: fuera de ventana conserva original/motivo/autor/fecha; basal siempre nueva versión. |
| `AUD-01` | `AUDIT` obligatorio | ADM-27/28; DIR-05/06/12/14/15 | `AuditEntry` | `TR-AUD-01`: detalle de Dirección, retirada, autorización y permisos generan auditoría. |
| `AUD-02` | Ámbito y minimización | ADM-28; DIR-08/14/16 | `AuditEntry`, `AggregateIndicator` | `TR-AUD-02`: auditoría no produce ranking ni expone texto clínico a no autorizados. |
| `AUD-03` | Solo servicio de auditoría escribe; usuarios `NO` | ADM-28; DIR-14 | `AuditEntry` | `TR-AUD-03`: actor no puede editar/borrar; registro conserva cuenta, perfil, ámbito, acción, recurso y hora. |

## 14. Cobertura y puertas pendientes

### 14.1 Cobertura declarada

- Requisitos funcionales trazados: **140 de 140**.
- Requisitos retirados con prueba negativa: `BAS-08`.
- Perfiles cubiertos: Auxiliar, Enfermería, Medicina, Familiar, Administración y Dirección/Coordinación Clínica.
- Pruebas trazadas: **140 identificadores únicos `TR-*`**.
- Cada requisito tiene permiso/condición, pantalla, entidad lógica y prueba.

### 14.2 Pendientes que no deben resolverse en el esquema por inferencia

1. Actor y procedimiento para cancelar un borrador basal cuando su creador no está disponible.
2. Actor, alcance y permiso para iniciar rectificación basal.
3. Profesionales concretos y granularidad de los permisos configurables por centro.
4. Efectos exactos de traslados entre centros/unidades e inactivación sobre grants y pendientes.
5. Finalidades, ámbitos y retención aplicables a `CLINICAL_DETAIL_READ`.
6. Política definitiva de corrección de cursos/notas y conservación antes de datos reales.
7. Actor técnico y procedimiento de provisionamiento de centros.
8. Diseño físico D1/Drizzle, incluidos índices, restricciones y estrategia compartida o por centro.

### 14.3 Regla de cierre

La trazabilidad queda preparada para línea base funcional cuando:

- una comprobación automática confirma los 140 requisitos del PRD v0.4;
- toda pantalla referenciada existe o está marcada expresamente como retirada;
- los permisos configurables coinciden con la matriz v0.2;
- las entidades continúan siendo lógicas hasta aprobar D1/Drizzle;
- las pruebas negativas cubren denegación por perfil, ámbito, autoría, estado, revocación y manipulación de identificadores.

## 15. Ubicación canónica propuesta

`docs/product/traceability/2026-09-05-matriz-trazabilidad-funcional-v0.1.md`

## 14. Cambios de v0.1 a v0.2

- Se conservan los 140 requisitos y las 140 pruebas trazables.
- Se actualizan `TR-RES-01`, `TR-BAS-16`, `TR-BAS-17` y `TR-BAS-18` conforme a la decisión de 6 de septiembre.
- Se actualizan las versiones documentales sin alterar permisos ni habilitar flujos diferidos.

