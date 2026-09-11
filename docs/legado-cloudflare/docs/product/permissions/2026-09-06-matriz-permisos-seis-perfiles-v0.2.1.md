# Matriz consolidada de permisos

## Seis perfiles - RBAC/ABAC funcional

**Versión:** 0.2.1  
**Fecha:** 6 de septiembre de 2026  
**Estado:** Fuente funcional de autorización alineada con PRD v0.5  
**Documento sustituido:** Matriz de permisos de seis perfiles v0.2, de 5 de septiembre de 2026  
**Perfiles:** Auxiliar, Enfermería, Medicina, Familiar, Administración y Dirección/Coordinación Clínica  
**Datos del prototipo:** exclusivamente ficticios  

> **Alcance de v0.2.1:** actualización de referencias para la línea base v1.1. No concede, revoca ni modifica ningún permiso de v0.2; `D1-P04`–`D1-P06` permanecen bloqueadas y denegadas por defecto.

> Esta matriz describe capacidades funcionales, no visibilidad de botones. Toda operación protegida debe autorizarse en servidor combinando cuenta activa, perfil activo explícito, ámbito, relación, estado del objeto, autoría, permiso específico y finalidad cuando corresponda.

> La ausencia de una prohibición no concede permiso. Ante una condición no satisfecha o una decisión pendiente, se deniega la operación.

# 1. Cambios principales de v0.1 a v0.2

- Se elimina por completo el permiso de seleccionar o consultar CFS.
- Barthel pasa a ser un módulo común y versionado para todos los centros.
- Se separan alta administrativa, creación de borrador basal, aportación, firma, cancelación, reevaluación y rectificación.
- Solo puede existir un borrador basal activo por residente y solo puede firmarlo su creador.
- Un basal firmado es inmutable; la rectificación crea otra versión vinculada.
- La ventana de seis horas queda limitada a cursos/notas clínicas y nunca se aplica al basal.
- La ubicación se gestiona mediante historial temporal; no se sobrescribe el pasado.
- La plataforma provisiona centros y Administración gestiona su estructura subordinada.
- Se refuerzan el perfil activo, la denegación por defecto y los atributos confiables obtenidos en servidor.
- La lectura clínica de Dirección exige permiso, finalidad, ámbito y auditoría previa.

# 2. Leyenda

| Código | Significado |
| --- | --- |
| **SI** | Permitido dentro del ámbito autorizado y con las validaciones generales |
| **COND** | Permitido solo si se cumple la condición o el permiso adicional indicado |
| **LECT** | Consulta en solo lectura |
| **PROPIO** | Limitado a la propia cuenta, solicitud, cita o registro autorizado |
| **ESTADO** | Solo metadatos o estado operativo/administrativo; no contenido clínico completo |
| **AUTO** | Acción automática no clínica después de una decisión humana válida |
| **BLOQ** | Capacidad aprobada conceptualmente pero bloqueada hasta cerrar actor, permiso o procedimiento |
| **NO** | No permitido desde ese perfil |

Las expresiones combinadas, como `COND-LECT`, deben cumplir ambas partes. `BLOQ` se implementa como denegación hasta que una decisión posterior lo sustituya.

Una cuenta multirol no suma capacidades. Cada petición se evalúa con un único perfil activo y contra los grants de ese perfil.

# 3. Condiciones generales de toda autorización

Toda celda distinta de `NO` o `BLOQ` presupone:

1. Cuenta activa y no suspendida.
2. Sesión válida.
3. Perfil activo seleccionado explícitamente.
4. Grant activo para el centro.
5. Unidad y residente dentro del ámbito aplicable.
6. Estado del objeto compatible con la acción.
7. Permiso específico cuando la fila lo requiera.
8. Autorización o vínculo activo cuando intervenga un familiar.
9. Finalidad válida y auditoría cuando lo exija el recurso.

El servidor obtiene cuenta, perfil activo, centro, unidad, residente, autoría y fecha/hora desde la sesión y otras fuentes confiables. No acepta esos atributos como autoridad desde campos libres del navegador.

# 4. Acceso, identidad y ámbito

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Acceder a la aplicación | SI | SI | SI | SI | SI | SI |
| Gestionar seguridad de su cuenta | PROPIO | PROPIO | PROPIO | PROPIO | PROPIO | PROPIO |
| Cambiar explícitamente de perfil multirol | COND cuenta multirol | COND cuenta multirol | COND cuenta multirol | NO | COND cuenta multirol | COND cuenta multirol |
| Consultar perfil activo, ámbito y permisos propios | LECT | LECT | LECT | LECT vínculo | LECT | LECT |
| Ver residentes de su ámbito | LECT asignados | LECT | LECT | LECT vinculados | LECT administrativa | LECT según ámbito |
| Acceder a otro centro o unidad sin grant | NO | NO | NO | NO | NO | NO |
| Acceder a residente fuera del ámbito vigente | NO | NO | NO | NO | NO | NO |
| Combinar permisos de varios perfiles en una operación | NO | NO | NO | NO | NO | NO |
| Provisionar un centro | NO | NO | NO | NO | NO | NO |
| Gestionar estructura subordinada del centro | NO | NO | NO | NO | SI ámbito | NO |
| Gestionar organigrama o cargos | NO | NO | NO | NO | SI ámbito | NO |
| Crear, activar o suspender cuentas | NO | NO | NO | NO | SI ámbito | NO |
| Asignar varios perfiles y sus ámbitos | NO | NO | NO | NO | SI ámbito | NO |
| Conceder o revocar permisos configurables | NO | NO | NO | NO | COND política centro | NO |
| Planificar turnos | NO | NO | NO | NO | SI | ESTADO |

**Provisionamiento:** la creación del centro corresponde a un actor de plataforma fuera de estos seis perfiles. Su identidad, autorización y procedimiento siguen pendientes antes del esquema físico.

# 5. Residente, ubicación y estado basal

## 5.1 Identidad y ubicación administrativa

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Ver identidad mínima del residente | LECT asignado | LECT ámbito | LECT ámbito | LECT vinculado | LECT administrativa | LECT ámbito |
| Crear identidad administrativa | NO | COND `RESIDENT_IDENTITY_CREATE` | NO | NO | SI | NO |
| Modificar identidad administrativa | NO | NO | NO | NO | SI | NO |
| Ver ubicación vigente | LECT asignado | LECT ámbito | LECT ámbito | LECT mínima vinculada | LECT administrativa | LECT ámbito |
| Ver historial administrativo de ubicación | NO | LECT necesaria para contexto | LECT necesaria para contexto | NO | LECT | COND-LECT clínica/operativa |
| Cambiar ubicación abriendo un nuevo intervalo | NO | NO | NO | NO | SI | NO |
| Editar o borrar un intervalo histórico de ubicación | NO | NO | NO | NO | NO | NO |
| Inactivar o reactivar residente | NO | NO | NO | NO | BLOQ procedimiento pendiente | NO |

Cambiar ubicación debe cerrar el intervalo vigente y abrir el nuevo en una única operación. La ubicación actual es una proyección del historial, no un campo administrativo paralelo editable.

## 5.2 Consulta y creación del basal

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Ver basal vigente | LECT asignado | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Ver historial de versiones basales | NO | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Crear borrador basal inicial | NO | COND `BASELINE_INITIAL_COMPLETE` | COND `BASELINE_INITIAL_COMPLETE` | NO | NO | NO |
| Crear borrador de reevaluación | NO | COND `BASELINE_REEVALUATE` | COND `BASELINE_REEVALUATE` | NO | NO | NO |
| Completar nueve áreas basales | NO | COND borrador propio y permiso | COND borrador propio y permiso | NO | NO | NO |
| Completar Barthel común | NO | COND borrador propio y permiso | COND borrador propio y permiso | NO | NO | NO |
| Registrar cognición/etiología/GDS documentados | NO | COND borrador propio y permiso | COND borrador propio y permiso | NO | NO | NO |
| Registrar CFS | NO | NO | NO | NO | NO | NO |
| Crear un segundo borrador activo para el mismo residente | NO | NO | NO | NO | NO | NO |

`BASELINE_INITIAL_COMPLETE` y `BASELINE_REEVALUATE` son permisos diferentes. El centro decide qué profesionales de Enfermería y Medicina reciben cada uno.

## 5.3 Autoría, aportaciones, firma y cancelación basal

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Editar el contenido de su borrador basal activo | NO | COND creador + permiso vigente | COND creador + permiso vigente | NO | NO | NO |
| Editar directamente el borrador creado por otro | NO | NO | NO | NO | NO | NO |
| Añadir una aportación identificada a borrador ajeno | NO | COND `BASELINE_DRAFT_CONTRIBUTE` | COND `BASELINE_DRAFT_CONTRIBUTE` | NO | NO | NO |
| Modificar o borrar una aportación ajena | NO | NO | NO | NO | NO | NO |
| Firmar su propio borrador basal | NO | COND creador + permiso + validación | COND creador + permiso + validación | NO | NO | NO |
| Firmar borrador creado por otro profesional | NO | NO | NO | NO | NO | NO |
| Transferir autoría o derecho de firma | NO | NO | NO | NO | NO | NO |
| Cancelar voluntariamente su borrador antes de firma | NO | COND creador + motivo | COND creador + motivo | NO | NO | NO |
| Cancelar borrador por indisponibilidad del creador | NO | BLOQ actor/procedimiento pendiente | BLOQ actor/procedimiento pendiente | NO | NO | NO |
| Reabrir o firmar un borrador cancelado | NO | NO | NO | NO | NO | NO |

La aportación conserva autoría propia y no modifica la propiedad del borrador. El permiso y actor capaces de cancelar por indisponibilidad del creador deben cerrarse antes del esquema físico.

## 5.4 Vigencia, inmutabilidad y rectificación basal

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Activar como vigente al firmar | NO | COND creador + validación completa | COND creador + validación completa | NO | NO | NO |
| Mantener dos versiones firmadas vigentes | NO | NO | NO | NO | NO | NO |
| Editar o borrar basal firmado | NO | NO | NO | NO | NO | NO |
| Aplicar ventana de seis horas al basal | NO | NO | NO | NO | NO | NO |
| Iniciar rectificación de basal firmado | NO | BLOQ actor/alcance pendiente | BLOQ actor/alcance pendiente | NO | NO | NO |
| Firmar rectificación creada por sí mismo | NO | COND cuando se habilite + creador | COND cuando se habilite + creador | NO | NO | NO |
| Consultar vínculo entre original y rectificación | NO | LECT ámbito | LECT ámbito | NO | NO | COND-LECT auditada |

Toda rectificación basal crea una nueva versión vinculada. Nunca autoriza un `UPDATE` del original. Hasta definir quién puede iniciarla y sobre qué versiones, la creación de rectificaciones permanece bloqueada por defecto.

# 6. Registro cotidiano y eventos

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Registrar Sin cambios / No valorable | SI | COND tarea habilitada | NO por defecto | NO | NO | NO |
| Registrar cambio o evento observado | SI | SI | SI | NO | NO | NO |
| Documentar aviso directo prioritario propio | SI | SI si procede | SI si procede | NO | NO | NO |
| Ver observación original | LECT ámbito | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Editar observación ajena | NO | NO | NO | NO | NO | NO |
| Valorar evento | NO | SI | SI en escalado/propio | NO | NO | NO |
| Registrar constantes | Temperatura opcional | SI | SI | NO | NO | NO |
| Iniciar seguimiento de Enfermería | NO | SI | NO | NO | NO | NO |
| Escalar a Medicina | NO | SI | NO | NO | NO | NO |
| Emitir indicación médica | NO | NO | SI | NO | NO | NO |
| Confirmar lectura de indicación | NO | SI | LECT | NO | NO | ESTADO |
| Registrar realización/no realización | NO | SI | LECT | NO | NO | ESTADO |
| Crear seguimiento médico | NO | NO | SI | NO | NO | NO |
| Transferir continuidad de turno | NO | SI | SI | NO | NO | ESTADO |
| Cerrar evento | NO | SI si resuelve | SI si resuelve | NO | NO | NO |
| Ver eventos vencidos | ESTADO limitado | SI | SI | NO | NO | ESTADO |

# 7. Derivación, historial, correcciones y trazabilidad

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Activar/documentar protocolo urgente | Aviso según protocolo | SI | SI | NO | NO | NO |
| Generar informe de derivación | NO | SI | SI | NO | NO | NO |
| Firmar informe de derivación | NO | COND competencia propia | COND competencia propia | NO | NO | NO |
| Ver informe de derivación | NO | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Ver Historial clínico | NO general | LECT ámbito | LECT ámbito | NO | NO | COND-LECT según nivel y auditada |
| Ver línea temporal completa | NO | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Modificar línea temporal | NO | NO | NO | NO | NO | NO |
| Corregir curso/nota clínica propia dentro de ventana | COND objeto habilitado | COND objeto habilitado | COND objeto habilitado | NO | NO | NO |
| Rectificar curso/nota clínica propia fuera de ventana | COND política clínica | COND política clínica | COND política clínica | NO | NO | NO |
| Corregir registro administrativo propio | NO | NO | NO | NO | COND según tipo de objeto | NO |
| Consultar correcciones/rectificaciones clínicas | NO | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Consultar auditoría administrativa | NO | NO | NO | NO | COND ámbito | NO |
| Consultar trazabilidad clínica | NO | LECT ámbito | LECT ámbito | NO | NO | COND-LECT clínica auditada |
| Exportar masivamente historias individuales | NO | NO | NO | NO | NO | NO |

La ventana ordinaria de seis horas se aplica únicamente a cursos/notas clínicas expresamente habilitados, es configurable y se limita al autor. No se extiende por analogía a otros objetos.

# 8. Publicaciones y acceso familiar

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Decidir si un cierre genera comunicación | NO | SI si cierra | SI si cierra | NO | NO | NO |
| Redactar texto familiar | NO | SI si cierra | SI si cierra | NO | NO | NO |
| Aprobar publicación | NO | SI si cierra | SI si cierra | NO | NO | NO |
| Programar publicación aprobada | NO | AUTO por regla | AUTO por regla | NO | Configura horario | ESTADO |
| Ver texto completo antes/después de publicar | NO | LECT ámbito | LECT ámbito | Solo publicada | NO por defecto | COND-LECT clínica auditada |
| Ver estado administrativo de publicación | NO | SI | SI | Solo visible | ESTADO | ESTADO |
| Configurar frecuencia por residente | NO | NO | NO | NO | SI | NO |
| Configurar día/hora de publicación | NO | NO | NO | NO | SI | NO |
| Crear publicación correctora | NO | COND profesional autorizado | COND profesional autorizado | NO | NO | NO |
| Retirada excepcional | NO | NO | NO | NO | COND permiso específico | NO |
| Ver publicaciones familiares publicadas | NO | LECT | LECT | SI autorizadas | ESTADO | ESTADO o COND-LECT clínica |
| Gestionar familiar/contacto | NO | LECT audiencia | LECT audiencia | PROPIO limitado | SI | NO |
| Crear/activar/suspender/revocar autorización | NO | NO | NO | NO | SI | NO |
| Designar contacto urgente | NO | LECT | LECT | NO | SI | NO |
| Documentar intento de llamada en derivación | NO | SI si interviene | SI si interviene | NO | ESTADO | ESTADO |

Familiar solo ve contenido publicado para una autorización activa y su audiencia. No accede al basal, Barthel, GDS, observaciones, valoraciones, constantes, indicaciones, seguimientos, derivaciones, historial ni línea temporal interna.

# 9. Citas familiares

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Activar/desactivar servicio de citas | NO | NO | NO | NO | SI | NO |
| Elegir Reserva directa / Solicitud previa | NO | NO | NO | NO | SI | NO |
| Configurar equipos/modalidades/franjas | NO | NO | NO | NO | SI | NO |
| Ver huecos libres | NO | COND agenda interna | COND agenda interna | COND modo directo | SI | NO |
| Reservar directamente un hueco | NO | NO | NO | PROPIO modo directo | SI manual | NO |
| Enviar solicitud previa | NO | NO | NO | PROPIO modo solicitud | SI manual | NO |
| Proponer fecha/hora | NO | NO | NO | NO | SI | NO |
| Aceptar propuesta | NO | NO | NO | PROPIO | COND gestión | NO |
| Solicitar cambio de horario | NO | NO | NO | PROPIO | SI gestiona | NO |
| Reprogramar cita directa | NO | NO | NO | PROPIO | SI | NO |
| Cancelar cita/solicitud | NO | NO | NO | PROPIO | SI | NO |
| Finalizar cita | NO | COND si existe agenda profesional | COND si existe agenda profesional | NO | SI administrativa | NO |
| Asignar profesional concreto internamente | NO | NO | NO | NO | SI | NO |
| Elegir profesional concreto desde portal | NO | NO | NO | NO | NO | NO |
| Escribir chat clínico en una cita | NO | NO | NO | NO | NO | NO |
| Consultar auditoría de citas | NO | NO | NO | PROPIO estado | SI | ESTADO agregado |

Condición familiar: autorización Activa para el residente y servicio/modalidad habilitados por el centro. El modo vigente determina qué acciones están disponibles.

# 10. Administración y Dirección Clínica

| Acción o recurso | Auxiliar | Enfermería | Medicina | Familiar | Administración | Dirección Clínica |
| --- | --- | --- | --- | --- | --- | --- |
| Ver panel administrativo | NO | NO | NO | NO | SI | NO |
| Ver panel de supervisión clínica | NO | NO | NO | NO | NO | SI |
| Ver indicadores agregados | NO | NO | NO | NO | ESTADO operativo admin | SI |
| Ver pendientes asistenciales agregados | NO | SI unidad | SI ámbito | NO | NO | SI |
| Abrir detalle operativo de episodio | NO | SI | SI | NO | NO | SI |
| Abrir contenido clínico detallado | NO | SI | SI | NO | NO | COND-LECT auditada |
| Editar desde supervisión | NO | NO | NO | NO | NO | NO |
| Generar informes agregados | NO | NO | NO | NO | COND admin | SI |
| Crear ranking individual de profesionales | NO | NO | NO | NO | NO | NO |
| Cambiar categorías o umbrales clínicos | NO | NO | NO | NO | NO | NO |
| Actuar clínicamente desde Dirección | NO | NO | NO | NO | NO | NO |
| Cambiar desde Dirección a un perfil asistencial autorizado | NO | NO | NO | NO | NO | COND cuenta multirol; la actuación posterior ya no pertenece al perfil Dirección |

La auditoría de lectura clínica de Dirección se escribe antes de entregar el contenido y conserva cuenta, perfil, centro, unidad, residente, recurso, finalidad y fecha/hora.

# 11. Atributos ABAC obligatorios

| Atributo | Aplicación |
| --- | --- |
| Cuenta activa | Una cuenta suspendida no opera aunque conserve grants |
| Sesión válida | Caducidad o revocación impiden nuevas operaciones |
| Perfil activo | Un directivo médico cambia a Medicina para actuar clínicamente |
| Centro | Ningún perfil accede a otro centro sin grant |
| Unidad | El acceso profesional se limita a unidades autorizadas |
| Residente | Familiar solo accede a residentes vinculados; restricción individual profesional cuando proceda |
| Asignación/tarea | Cierre cotidiano limitado a residentes y turnos asignados |
| Permiso específico | Alta, basal, detalle clínico y retiradas exigen permisos independientes |
| Autoría | Firma basal limitada al creador; corrección clínica ordinaria limitada al autor |
| Estado del objeto | Solo borrador válido puede firmarse; solo publicación aprobada puede programarse |
| Versión | Catálogo basal y Barthel quedan identificados en cada versión |
| Ventana temporal | Seis horas solo en cursos/notas clínicas habilitados; nunca en basal |
| Autorización familiar | Solo Activa concede portal y citas |
| Audiencia | Una publicación se muestra únicamente a destinatarios autorizados |
| Nivel de supervisión | Dirección operativa no equivale a detalle clínico |
| Configuración del centro | `DIRECT` y `REQUEST` habilitan recorridos distintos |
| Finalidad | La lectura clínica de Dirección requiere finalidad válida |
| Ubicación vigente | El residente debe concordar con el único intervalo actual del historial |

# 12. Permisos configurables identificados

| Código funcional | Finalidad | Perfiles candidatos | Estado |
| --- | --- | --- | --- |
| `RESIDENT_IDENTITY_CREATE` | Alta administrativa excepcional por Enfermería | Enfermería | Aprobado; asignación por centro pendiente |
| `BASELINE_INITIAL_COMPLETE` | Crear/completar basal inicial | Enfermería, Medicina | Aprobado; profesionales concretos pendientes |
| `BASELINE_REEVALUATE` | Crear/completar reevaluación basal | Enfermería, Medicina | Aprobado; profesionales concretos pendientes |
| `BASELINE_DRAFT_CONTRIBUTE` | Aportar a borrador ajeno sin firmarlo | Enfermería, Medicina | Capacidad aprobada; permiso/granularidad pendientes |
| `CLINICAL_DETAIL_READ` | Lectura clínica detallada y auditada | Dirección Clínica | Aprobado; ámbito/finalidades pendientes |
| `PUBLICATION_EXCEPTIONAL_WITHDRAW` | Retirada excepcional | Administración | Aprobado; asignación concreta pendiente |

Estos códigos son nombres funcionales. El contrato técnico podrá ajustar su denominación sin ampliar la capacidad descrita.

# 13. Reglas de implementación y prueba

- Denegación por defecto y control server-side en cada operación.
- Identificadores opacos y no secuenciales; nunca suficientes para autorizar.
- Consultas filtradas por ámbito antes de recuperar contenido sensible.
- Cuenta, perfil, ámbito, autoría y tiempo obtenidos de fuentes confiables.
- Cambios de perfil, permiso, autorización o cuenta efectivos en la siguiente petición.
- Acceso clínico de Dirección auditado antes de devolver contenido.
- Reservas directas con unicidad, transacción e idempotencia.
- Un solo borrador basal activo, un solo basal vigente y una sola ubicación vigente por residente.
- Firma basal limitada al creador y ejecutada de forma atómica e idempotente.
- Borradores cancelados, basales firmados e intervalos históricos no editables.
- Rectificación basal como nueva versión vinculada; nunca edición del original.
- Barthel conserva diez respuestas/puntuaciones y valida el total automático.
- Pruebas negativas para cada celda `NO`, `BLOQ` y condición relevante.
- Pruebas multirol para demostrar que no se combinan permisos.
- Pruebas de revocación con sesión ya abierta.
- Pruebas de manipulación de URL, perfil, centro, unidad, residente, versión, publicación y cita.
- Logs técnicos sin texto clínico completo, contraseñas, tokens o secretos.

# 14. Decisiones configurables o pendientes

- Profesionales concretos de Enfermería y Medicina con permiso de alta, basal inicial y reevaluación.
- Actor y procedimiento para cancelar un borrador cuando su creador no está disponible.
- Granularidad y conservación de aportaciones a borradores ajenos.
- Quién puede iniciar una rectificación basal, sobre qué versiones y con qué permiso.
- Traslados entre centros/unidades e inactivación/reactivación y su efecto sobre grants y pendientes.
- Permisos concretos de retirada excepcional.
- Unidades, residentes, finalidades y retención de supervisión clínica de Dirección.
- Equipos, modalidades, duración, antelación y horizonte de citas por centro.
- Acceso de agenda para profesionales; no se amplía hasta definir su interfaz específica.
- Política de corrección de notas clínicas y conservación antes de datos reales.
- Actor técnico y procedimiento de provisionamiento de centros.
- Soporte técnico excepcional, fuera de los seis perfiles y sujeto a procedimiento temporal, mínimo y auditado antes de producción.

# 15. Criterios de cierre de la matriz v0.2

- No existe ninguna capacidad relacionada con CFS.
- Alta administrativa y basal profesional son permisos distintos.
- Auxiliar solo consulta basal vigente asignado.
- Familiar y Administración no acceden al basal.
- Dirección accede al detalle únicamente en lectura, con permiso, ámbito, finalidad y auditoría.
- Ningún perfil puede firmar un borrador basal ajeno.
- La ventana de seis horas no afecta al basal.
- Los bloqueos pendientes se implementan como denegaciones.
- Todas las capacidades del PRD v0.5 tienen una regla de autorización o una prohibición explícita.
- Los wireframes corregidos deberán usar esta matriz sin ampliar permisos.

> **Advertencia final.** Esta matriz no sustituye la EIPD ni el diseño técnico de autorización. Es el contrato funcional mínimo que debe convertirse en políticas server-side y pruebas automatizadas. No autoriza el tratamiento de datos sanitarios reales ni aprueba el esquema D1/Drizzle.

# Anexo. Cambios de v0.2 a v0.2.1

- Se actualiza la referencia al PRD v0.5 y a la línea base v1.1.
- No cambia ninguna celda de autorización ni el catálogo de permisos.
- Traslado/reactivación, cancelación excepcional e inicio de rectificación continúan bloqueados hasta una decisión funcional posterior.

