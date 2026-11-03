/*
 * Anonimizar a un familiar (Orlando, 2026-11-03; docs/privacidad/expurgo-y-supresion.md, apartado 3). Hoy ningún camino atiende una petición de supresión de un familiar:
 * TR_fam_no_delete impide borrar su fila (citas, autorizaciones, auditoría y registro de envíos apuntan a ella). La salida es sobrescribir sus datos de contacto, no borrar la fila.
 *
 * Tres columnas nuevas en dbo.familiares: cuándo se anonimizó, qué cuenta de Administración lo hizo y quién lo pidió (lista cerrada: la persona, su representante, sus allegados o
 * herederos, o el centro). Ni quién hizo la petición, ni su documento, ni un texto libre: eso va en el registro de derechos del centro. CK_fam_anonimizado hace que la base de datos
 * garantice el estado: un familiar anonimizado tiene la marca fija de nombre y de teléfono, ningún correo y ninguna confirmación de correo (la aplicación pone esos valores; la base
 * los exige). Los familiares que ya existen no cambian.
 *
 * TR_fam_update_guard (0052) se amplía: una vez anonimizado, nadie puede quitarle la marca ni cambiar quién lo pidió, y no se le puede dar una cuenta de portal nueva si no tenía
 * (GrantPortalAccessAsync le crearía una). El trigger solo lee inserted y deleted: no accede a la tabla y no necesita FORCESEEK (lección de 0056 y 0084).
 *
 * NO bloquea ni guarda copia de los datos anteriores: decisión de Orlando (2026-11-03) hasta que el DPO real o un abogado diga por escrito si basta con borrar o hay que bloquear
 * (art. 32 de la LOPDGDD). Hasta entonces la función no se usa con familias reales (docs/privacidad/antes-de-datos-reales.md). No toca el identificador de acceso de la cuenta
 * (sujeto_externo, inmutable) ni los textos libres que otros escribieron en filas inmutables.
 */

SET XACT_ABORT ON;
GO

ALTER TABLE dbo.familiares ADD
    anonimizado_en            DATETIME2(3) NULL,
    anonimizado_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_fam_anonimizado_por REFERENCES dbo.cuentas(id),
    anonimizado_motivo_codigo NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NULL;
GO

ALTER TABLE dbo.familiares ADD CONSTRAINT CK_fam_anonimizado CHECK (
    (anonimizado_en IS NULL AND anonimizado_por_cuenta_id IS NULL AND anonimizado_motivo_codigo IS NULL)
    OR (anonimizado_en IS NOT NULL AND anonimizado_por_cuenta_id IS NOT NULL
        AND anonimizado_motivo_codigo IN ('PETICION_DEL_INTERESADO', 'PETICION_DE_REPRESENTANTE', 'PETICION_DE_ALLEGADOS_O_HEREDEROS', 'INICIATIVA_DEL_CENTRO')
        AND nombre_visible = N'Persona anonimizada' AND telefono = N'-' AND correo IS NULL
        AND correo_confirmado_en IS NULL AND correo_confirmado_por_cuenta_id IS NULL));
GO

ALTER TRIGGER dbo.TR_fam_update_guard ON dbo.familiares AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.centro_id <> d.centro_id OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en)
        THROW 50400, 'FAMILY_MEMBER_UPDATE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.cuenta_id IS NOT NULL AND (i.cuenta_id IS NULL OR i.cuenta_id <> d.cuenta_id))
        THROW 50407, 'FAMILY_PORTAL_ACCOUNT_IMMUTABLE', 1;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.anonimizado_en IS NOT NULL
          AND (i.anonimizado_en IS NULL OR i.anonimizado_en <> d.anonimizado_en OR i.anonimizado_por_cuenta_id <> d.anonimizado_por_cuenta_id
               OR i.anonimizado_motivo_codigo <> d.anonimizado_motivo_codigo OR (d.cuenta_id IS NULL AND i.cuenta_id IS NOT NULL)))
        THROW 50870, 'FAMILY_MEMBER_ANONYMIZED_IMMUTABLE', 1;
END;
GO
