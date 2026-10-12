/*
 * Interbloqueos dentro de triggers, en una BD recién creada (como la del CI).
 *
 * Con las tablas vacías el plan se compila para recorrer la clave primaria entera aunque haya un índice por la columna que se busca
 * (lección de 0011/0013, «el índice no basta en una BD recién creada»). Un trigger AFTER corre dentro de la transacción de quien escribe, que ya
 * tiene bloqueada la fila recién insertada: si recorre la clave primaria, pide bloqueos compartidos sobre las filas que otra transacción acaba de
 * insertar y esa otra pide los de ésta. SQL Server aborta una (error 1205) y la aplicación lo devuelve como «No disponible». El registro de
 * excepciones del CI (SqlErrorLog) y el grafo de system_health lo localizaron el 12/10/2026 en una BD de CI reproducida en local:
 *
 *  - TR_fac_transition (0022), el LEFT JOIN a la autorización anterior del vínculo: bloqueos de clave sobre PK_fac.
 *  - TR_ea_suspension_guard (0041), la búsqueda de la suspensión abierta del residente: bloqueos sobre PK_spr y PK_ea.
 *
 * WITH (FORCESEEK) obliga a buscar por clave (UX_fac_numero y UX_spr_open / IX_spr_lookup). Los otros cuatro triggers de suspensión de 0041
 * (cierres cotidianos, eventos clínicos, borradores de basal y comunicados) hacen la misma búsqueda y reciben la misma pista. Solo cambia el plan;
 * la lógica, los códigos de error y el orden de los triggers no se tocan.
 */

ALTER TRIGGER dbo.TR_fac_transition ON dbo.familiares_autorizaciones_cambios AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        LEFT JOIN dbo.familiares_autorizaciones_cambios previous WITH (FORCESEEK)
               ON previous.vinculo_id = i.vinculo_id AND previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
        CROSS APPLY (SELECT COALESCE(previous.estado_codigo, N'') AS estado) prev  -- sin anterior: '' (evita NULL en el NOT)
        WHERE NOT (
            (i.numero = 1 AND i.estado_codigo = 'PENDIENTE')
            OR (prev.estado = 'PENDIENTE' AND i.estado_codigo IN ('ACTIVA', 'REVOCADA'))
            OR (prev.estado = 'ACTIVA' AND i.estado_codigo IN ('ACTIVA', 'SUSPENDIDA', 'REVOCADA'))
            OR (prev.estado = 'SUSPENDIDA' AND i.estado_codigo IN ('ACTIVA', 'REVOCADA'))))
        THROW 50405, 'FAMILY_AUTHORIZATION_TRANSITION_INVALID', 1;
END;
GO

/* Mientras haya una suspensión abierta, no se escribe nada clínico sobre el residente. */
ALTER TRIGGER dbo.TR_cc_suspension_guard ON dbo.cierres_cotidianos_residente AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s WITH (FORCESEEK)
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
ALTER TRIGGER dbo.TR_ec_suspension_guard ON dbo.eventos_clinicos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s WITH (FORCESEEK)
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
ALTER TRIGGER dbo.TR_bd_suspension_guard ON dbo.basales_borrador AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s WITH (FORCESEEK)
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
ALTER TRIGGER dbo.TR_cf_suspension_guard ON dbo.comunicaciones_familiares AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s WITH (FORCESEEK)
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
/* Toda acción sobre un evento (valoración, constantes, indicación, seguimiento, cierre, protocolo) avanza su revisión: se frena aquí.
   Mover la unidad del evento por un traslado no cambia nada clínico y se deja pasar. */
ALTER TRIGGER dbo.TR_ea_suspension_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                JOIN dbo.suspensiones_residente s WITH (FORCESEEK)
                  ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL
                WHERE i.unidad_id = d.unidad_id)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
