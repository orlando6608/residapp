/*
 * Seguridad por filas por centro, tanda 3: estructura y planificación del centro (ADR 0008, incremento G1). Edificios, plantas,
 * habitaciones y plazas; equipos y sus miembros; catálogo de turnos y su planificación; rangos de referencia de constantes y su
 * historial. Amplía la política de 0030 con el mismo predicado y las mismas reglas. Las unidades no entran: las escribe PLATAFORMA
 * al dar de alta un centro (tablas de provisión).
 */

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.edificios,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.edificios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.edificios AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plantas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plantas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plantas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.habitaciones,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.habitaciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.habitaciones AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.equipos,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.equipos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.equipos AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.equipos_miembros,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.equipos_miembros AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.equipos_miembros AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.turnos_catalogo,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.turnos_catalogo AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.turnos_catalogo AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.planificacion_turnos,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.planificacion_turnos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.planificacion_turnos AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.rangos_referencia_constantes,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.rangos_referencia_constantes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.rangos_referencia_constantes AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.rangos_referencia_constantes_historial,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.rangos_referencia_constantes_historial AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.rangos_referencia_constantes_historial AFTER UPDATE;
GO
