# Alcance

## Alcance modular

| Módulo | Funciones incluidas |
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

## Roles y responsabilidades

| Perfil | Responsabilidad principal | Límite esencial |
| --- | --- | --- |
| Auxiliar | Cierre cotidiano, cambios observados y aviso directo cuando proceda | No diagnostica, valora clínicamente, modifica basal, escala a Medicina ni aprueba publicaciones |
| Enfermería | Basal según permiso, bandejas, valoración, seguimiento, escalado, derivación, indicaciones, cierre y comunicación | No altera observaciones ajenas ni gestiona autorizaciones familiares |
| Medicina | Basal según permiso, escalados, eventos propios, valoración, conducta, indicaciones, seguimiento, derivación, cierre y comunicación | No automatiza decisiones ni atribuye actos a otros |
| Familiar | Consulta publicaciones y gestiona citas habilitadas para residentes autorizados | No accede a contenido clínico interno ni elige profesional concreto |
| Administración | Estructura subordinada, identidades, ubicaciones, roles, asignaciones, turnos, autorizaciones, citas y auditoría administrativa | No provisiona centros, participa en actos clínicos ni accede por defecto al historial clínico |
| Dirección Clínica | Supervisión de proceso, indicadores e informes agregados | No interviene clínicamente desde este perfil; el detalle clínico solo se consulta en lectura, con permiso y auditoría |

### Cuentas multirol

Una cuenta puede tener uno o varios perfiles autorizados, pero debe seleccionar o cambiar explícitamente el perfil activo. Toda actuación conserva cuenta, perfil activo, ámbito y fecha/hora. Los permisos de perfiles distintos no se combinan. Un cargo, organigrama, turno o posición organizativa no concede permisos por sí mismo.

## Fuera de alcance

- Sustituir la historia clínica oficial o el software integral del centro.
- Diagnóstico, prescripción, solicitud de pruebas, triaje o recomendaciones automáticas.
- Mensajería libre entre familia y profesionales.
- Contenido sanitario por email, SMS, WhatsApp o enlaces públicos.
- Notificaciones push en el piloto.
- App móvil nativa; la primera versión es web adaptable/PWA.
- Modo clínico offline con sincronización posterior.
- Administración de medicación, facturación, nóminas, fichaje o gestión laboral avanzada.
- Videollamada propia; solo puede ofrecerse como modalidad si el centro dispone del medio externo adecuado.
- Rankings nominativos de productividad o evaluaciones automáticas de mala praxis.
- Exportación masiva de historias clínicas individuales.
- Integraciones con historias clínicas externas en la primera versión.
- Flujos específicos de Centro de Día y SAAD antes de validar el modelo inicial en residencias.
- CFS y Pfeiffer dentro del estado basal.

## Nota de vigencia

Este alcance funcional y estos límites de rol son independientes de la plataforma tecnológica y siguen siendo la referencia vigente para la implementación en .NET. Para saber qué módulos y bloques verticales ya están migrados, ver [roadmap.md](roadmap.md). El detalle histórico de la matriz de permisos de seis perfiles se conserva en `docs/legado-cloudflare/docs/product/`, sin que ello implique que ya exista un equivalente decidido para la nueva pila.
