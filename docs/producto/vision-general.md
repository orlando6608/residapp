# Visión general

## Qué es ResidApp

ResidApp es la plataforma que permite a los profesionales de una residencia registrar de forma breve y trazable el estado cotidiano de cada residente, documentar cambios, mantener la continuidad entre turnos, realizar valoración profesional y transformar únicamente la información aprobada en publicaciones comprensibles para familiares autorizados. Su ámbito inicial son las residencias geriátricas.

La plataforma **no** diagnostica, prescribe, solicita pruebas, recomienda tratamientos, decide prioridades clínicas ni deriva automáticamente. Tampoco sustituye canales urgentes, llamadas, emergencias, la historia clínica oficial ni los protocolos asistenciales del centro.

## El problema que resuelve

ResidApp existe para atender problemas concretos observados en la operación diaria de una residencia:

- Información cotidiana dispersa o poco útil para las familias.
- Interrupciones y llamadas repetitivas dependientes de la disponibilidad individual de cada profesional.
- Falta de claridad sobre quién observó, valoró, indicó, ejecutó, corrigió o cerró cada actuación.
- Pérdida de continuidad en los relevos y en los seguimientos abiertos entre turnos.
- Exposición indebida de contenido clínico interno a familiares o a perfiles administrativos que no deben acceder a él.
- Dificultad para adaptar horarios, unidades, turnos y citas a las particularidades de cada centro.
- Falta de indicadores de proceso con denominador, periodo y ámbito explícitos.
- Riesgo de perder el contexto histórico al sobrescribir el estado basal o la ubicación del residente.

## Propuesta de valor

- **Para el centro:** continuidad, trazabilidad y una configuración adaptable a su propia organización.
- **Para los profesionales:** registros rápidos, responsabilidades claras y bandejas de trabajo compartidas.
- **Para la familia:** información comprensible en un portal privado y la posibilidad de coordinar una cita sin necesidad de un chat clínico.
- **Para la dirección:** indicadores agregados y supervisión del proceso, sin interferir en la actuación asistencial.

## El circuito funcional completo

El resultado esperado de la plataforma es representar, de principio a fin, el siguiente circuito:

**Alta administrativa → basal profesional → registro cotidiano o evento → revisión de Enfermería → seguimiento o escalado → valoración médica e indicaciones → derivación si procede → cierre → texto familiar → aprobación → publicación → consulta familiar.**

Junto a este circuito, la plataforma también debe representar la configuración organizativa del centro, el historial de ubicación de cada residente, la agenda de citas, la auditoría de acciones sensibles y la supervisión operativa por parte de dirección.

## Perfiles que intervienen

En este circuito participan seis perfiles funcionales: **Auxiliar**, **Enfermería**, **Medicina**, **Portal Familiar**, **Administración** y **Dirección/Coordinación Clínica**. Sus responsabilidades y límites detallados se documentan en [alcance.md](alcance.md); aquí solo se enumeran para dar contexto al circuito descrito arriba.

## Nota de procedencia

El contenido de este documento adapta el PRD del prototipo legado (`docs/legado-cloudflare/docs/product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md`) y permanece vigente sin cambios pese al cambio de plataforma tecnológica hacia el monolito ASP.NET Core / SQL Server. Para el enfoque técnico de esa migración, ver `docs/decisiones-arquitectura/instrucciones-migracion-net10.md`; para el estado de avance, ver [roadmap.md](roadmap.md).
