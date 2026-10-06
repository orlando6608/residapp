/*
 * Seguridad por filas por centro, tanda 4b: las tablas de la autorización (ADR 0008, incremento G1). Los permisos del ámbito, las
 * restricciones de residente por ámbito, los episodios del residente en el centro y sus intervalos de ubicación. La autorización de
 * cada petición las lee, siempre con el ámbito activo y su centro (la política no cambia ningún resultado, y sin ámbito activo no
 * se ve ninguna fila: la autorización deniega). Amplía la política de 0030 con el mismo predicado y las mismas reglas. Con esto
 * quedan cubiertas todas las tablas de datos salvo las de provisión que escribe PLATAFORMA.
 */

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.permisos_perfil,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.permisos_perfil AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.permisos_perfil AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ambitos_perfil_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ambitos_perfil_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ambitos_perfil_residente AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.episodios_residente_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.episodios_residente_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.episodios_residente_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.intervalos_ubicacion_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.intervalos_ubicacion_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.intervalos_ubicacion_residente AFTER UPDATE;
GO
