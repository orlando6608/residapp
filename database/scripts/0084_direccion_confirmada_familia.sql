/*
 * «Dirección confirmada con la familia» (ADR 0012; CJ, aviso-citas-por-correo-cj 2.1 A, 2026-11-02; lectura en docs/privacidad/direccion-confirmada-familia.md, D1 a D10).
 *
 * Administración lee la dirección de correo a la persona a quien pertenece y marca la casilla. Sin la marca no se envía ningún aviso por correo (ni el de propuesta ni el de
 * cambio de cita). La marca vale SOLO para avisos genéricos: no sustituye a una verificación técnica (un correo con contenido o un código de acceso la exigirían).
 *
 *   1. Dos columnas en dbo.familiares (la marca es del familiar y de su dirección, no del vínculo con cada residente): cuándo y qué cuenta la puso. Ambas nulas o ambas con
 *      valor. NO se guarda el canal, ni la dirección leída, ni quién contestó. Nadie las rellena aquí: los familiares que ya tienen correo quedan SIN confirmar (D3).
 *   2. La invariante la garantiza la base, no cada camino de código (D1): cualquier UPDATE que cambie o quite el correo deja la marca sin efecto, salvo que ese mismo UPDATE la
 *      ponga de forma deliberada con una fecha posterior a la anterior; y una marca sin correo no existe. Hay tres caminos de código que escriben el correo y un UPDATE directo
 *      también queda cubierto.
 *   3. Un motivo nuevo en el registro de envíos: DIRECCION_NO_CONFIRMADA. El aviso omitido por esta causa no se manda más tarde al confirmar (D6): consume su clave única.
 *   4. La tarea programada (rol residapp_tareas, 0083) necesita leer la marca para volver a comprobarla al enviar (D5); solo esa columna, sin el nombre ni quién la puso.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.familiares ADD
    correo_confirmado_en DATETIME2(3) NULL,
    correo_confirmado_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_fam_correo_confirmado_cuenta REFERENCES dbo.cuentas(id);
GO

ALTER TABLE dbo.familiares ADD CONSTRAINT CK_fam_correo_confirmado_par CHECK (
    (correo_confirmado_en IS NULL AND correo_confirmado_por_cuenta_id IS NULL)
    OR (correo_confirmado_en IS NOT NULL AND correo_confirmado_por_cuenta_id IS NOT NULL));
GO

CREATE TRIGGER dbo.TR_fam_correo_confirmado ON dbo.familiares AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    -- Cambiar o quitar la dirección quita la marca, salvo que el mismo UPDATE ponga otra más reciente (la acción de confirmar sobre la dirección nueva).
    -- Se compara byte a byte (BIN2): pasar «A@x.org» a «a@x.org» también es un cambio de dirección.
    -- WITH (FORCESEEK): buscar por clave y no recorrer la tabla entera, como en 0056 (con la tabla casi vacía el plan la recorre y se bloquea con otras transacciones).
    UPDATE f
       SET correo_confirmado_en = NULL, correo_confirmado_por_cuenta_id = NULL
      FROM inserted i
      JOIN deleted d ON d.id = i.id
      JOIN dbo.familiares f WITH (FORCESEEK) ON f.id = i.id
     WHERE i.correo_confirmado_en IS NOT NULL
       AND ISNULL(i.correo, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(d.correo, N'') COLLATE Latin1_General_100_BIN2
       AND d.correo_confirmado_en IS NOT NULL AND i.correo_confirmado_en <= d.correo_confirmado_en;

    -- Una marca sin dirección no existe. Se decide con inserted y deleted, sin volver a leer la tabla: la fila que acaba de quedar sin marca por el UPDATE de arriba
    -- (dirección quitada) no cuenta.
    IF EXISTS (
        SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.id = i.id
         WHERE i.correo IS NULL AND i.correo_confirmado_en IS NOT NULL
           AND NOT (d.id IS NOT NULL AND d.correo IS NOT NULL AND d.correo_confirmado_en IS NOT NULL AND i.correo_confirmado_en <= d.correo_confirmado_en))
        THROW 50840, 'FAMILY_EMAIL_CONFIRMATION_INVALID', 1;
END;
GO

ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_motivo;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
    ('SIN_CORREO', 'SIN_ACCESO', 'DIRECCION_NO_CONFIRMADA', 'PROPUESTA_NO_VIGENTE', 'CITA_NO_VIGENTE', 'CITA_PASADA', 'DESTINO_NO_PERMITIDO', 'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO'));
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT SELECT (correo_confirmado_en) ON dbo.familiares TO residapp_tareas;
GO

COMMIT TRANSACTION;
GO
