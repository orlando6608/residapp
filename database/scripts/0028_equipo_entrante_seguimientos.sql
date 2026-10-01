/*
 * Equipos en los seguimientos de Enfermería y Medicina: una transferencia (ENF-09 y su equivalente de Medicina) ya no escribe el
 * «equipo o turno entrante» a mano, sino que elige un equipo activo de la unidad del evento (script 0027).
 *
 * equipo_entrante_id es el vínculo al equipo; equipo_entrante sigue guardando su nombre tal como era al transferir, de modo que el
 * historial no cambia si el equipo se renombra o se inactiva. Las transferencias anteriores quedan con el texto y sin vínculo
 * (equipo_entrante_id NULL). Que una transferencia nueva lleve equipo, y que sea activo y de la unidad del evento, lo exige la inserción
 * de la aplicación, no un CHECK, para no invalidar las anteriores.
 *
 * Las tablas solo tienen triggers INSTEAD OF UPDATE/DELETE, así que añadir una columna no las toca.
 */
ALTER TABLE dbo.seguimiento_acciones ADD
    equipo_entrante_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_sa_equipo_entrante REFERENCES dbo.equipos(id);
GO
ALTER TABLE dbo.seguimiento_acciones ADD CONSTRAINT CK_sa_equipo_entrante
    CHECK (equipo_entrante_id IS NULL OR tipo_codigo = 'TRANSFERENCIA');
GO

ALTER TABLE dbo.seguimiento_medico_acciones ADD
    equipo_entrante_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_sma_equipo_entrante REFERENCES dbo.equipos(id);
GO
ALTER TABLE dbo.seguimiento_medico_acciones ADD CONSTRAINT CK_sma_equipo_entrante
    CHECK (equipo_entrante_id IS NULL OR tipo_codigo = 'TRANSFERENCIA');
GO
