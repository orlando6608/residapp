# Objetivos

## Objetivos y métricas del piloto

Las siguientes metas son hipótesis de validación del piloto, no estándares clínicos universales ni umbrales automáticos de calidad asistencial.

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

## Principios funcionales invariantes

Estos principios gobiernan cualquier implementación de la plataforma, con independencia de la tecnología que la soporte.

### Autoría y trazabilidad

- **Basal como referencia:** los cambios se comparan con la situación habitual del residente.
- **Autoría fiel:** quien observa registra; quien valora, indica, ejecuta, corrige o cierra firma su propia actuación.
- **Firma no transferible:** nadie firma un borrador basal creado por otra persona.
- **Inmutabilidad trazable:** un registro firmado no se elimina ni se sobrescribe silenciosamente.
- **Versionado temporal:** el basal y la ubicación conservan el estado aplicable en cada momento.
- **Separación de objetos:** observación, valoración, conducta, ejecución, publicación familiar y cita son objetos distintos.

### Seguridad y acceso

- **Responsabilidad de equipo:** un evento abierto no pertenece permanentemente a la cuenta que inició la valoración.
- **Mínimo privilegio:** el acceso está limitado por centro, unidad, residente, asignación, perfil activo, autorización, permiso específico y finalidad.
- **Autorización en servidor:** ocultar botones no constituye seguridad.
- **Datos confiables:** identidad, perfil, ámbito, autoría y fecha/hora se obtienen de la sesión y del contexto server-side, nunca de campos libres del cliente.
- **Denegación por defecto:** ninguna capacidad se concede por ausencia de una prohibición explícita.

### Continuidad y privacidad

- **Vencimiento no equivale a cierre:** un objeto fuera de plazo sigue abierto y visible hasta que se produce una actuación profesional.
- **Automatización no clínica:** el sistema puede programar una publicación aprobada o reservar un hueco; no redacta, interpreta ni decide clínicamente.
- **Urgencias fuera del portal:** ninguna pantalla, cita o publicación sustituye el protocolo urgente ni el contacto directo.
- **Privacidad por diseño y por defecto:** se minimizan datos, exposición, retención y permisos desde el propio modelo.

## Vigencia en la nueva arquitectura

Estos objetivos y principios son independientes de la plataforma técnica: deben cumplirse igual en el monolito ASP.NET Core / SQL Server que en el prototipo original. Por ejemplo, "autorización en servidor" y "denegación por defecto" se implementan ahora mediante Claims-Based Authorization de ASP.NET Core, pero ese detalle de implementación corresponde a `docs/decisiones-arquitectura/instrucciones-migracion-net10.md`, no a este documento. Para ver qué parte de estos principios ya está verificada por la implementación actual y qué queda pendiente de comprobar, consultar [roadmap.md](roadmap.md).
