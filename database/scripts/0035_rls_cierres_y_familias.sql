/*
 * Seguridad por filas por centro, tanda 4a: cierres cotidianos, idempotencia y familias (ADR 0008, incremento G1). Los tres cierres
 * cotidianos del Auxiliar (residente, cambios de área y opciones), las operaciones de idempotencia, el contacto urgente, los familiares
 * del residente, las correcciones de identidad y el historial de autorizaciones de familiares. Amplía la política de 0030 con el
 * mismo predicado y las mismas reglas. Ninguna interviene en la autorización de cada petición: esas cuatro tablas van en la tanda 4b.
 */

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_residente AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_cambio_areas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_cambio_areas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_cambio_areas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_cambio_area_opciones,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_cambio_area_opciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_cotidianos_cambio_area_opciones AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.operaciones_idempotencia,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.operaciones_idempotencia AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.operaciones_idempotencia AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_contacto_urgente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_contacto_urgente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_contacto_urgente AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_familiares,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_familiares AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_familiares AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_identidad_correcciones,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_identidad_correcciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes_identidad_correcciones AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.familiares_autorizaciones_cambios,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.familiares_autorizaciones_cambios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.familiares_autorizaciones_cambios AFTER UPDATE;
GO
