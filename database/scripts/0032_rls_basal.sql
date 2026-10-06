/*
 * Seguridad por filas por centro, tanda 1: las diez tablas del basal del residente (ADR 0008, incremento G1). Amplía la política
 * creada en 0030 con el mismo predicado; las reglas (deja pasar a db_owner, sin salto de PLATAFORMA) son las de ese script.
 */

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_areas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_areas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_areas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_barthel,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_barthel AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_barthel AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_barthel_items,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_barthel_items AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_borrador_barthel_items AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_areas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_areas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_areas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_barthel,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_barthel AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_barthel AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_barthel_items,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_barthel_items AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_version_barthel_items AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_vigentes_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_vigentes_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_vigentes_residente AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_sustituciones,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_sustituciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.basales_sustituciones AFTER UPDATE;
GO
