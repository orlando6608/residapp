/*
 * Cuenta de familiar de prueba para el Portal Familiar (script 0052): 'dev-integrado-familiar', hija de «Residente Portal Familiar
 * (ficticio)», con la autorización Activa. Exclusivamente ficticio. NO ejecutar contra un entorno con datos reales.
 * Idempotente: si la cuenta 'dev-integrado-familiar' ya existe, o falta el escenario integrado, no inserta nada.
 *
 * Va en el centro y la unidad del escenario integrado (dev_seed_escenario_integrado.sql), que se aplica antes (los seeds se aplican por
 * orden alfabético); si aún no existe, no hace nada. El residente es distinto de «Residente Integrado Uno», que se deja intacto para el
 * recorrido guiado del Manual.
 *
 * No crea comunicados: cuelgan de un evento asistencial, y se generan recorriendo el flujo real (Enfermería registra y cierra, o Medicina
 * cierra con una comunicación) y publicando (a su hora o con Administración o Dirección Clínica).
 */

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-integrado-familiar')
   AND EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-integrado-auxiliar')
   AND EXISTS (SELECT 1 FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-INTEGRADO')
BEGIN
    DECLARE @CenterId UNIQUEIDENTIFIER = (SELECT centro_id FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-INTEGRADO');
    DECLARE @UnitId UNIQUEIDENTIFIER = (SELECT id FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-INTEGRADO');
    DECLARE @StaffId UNIQUEIDENTIFIER = (SELECT id FROM dbo.cuentas WHERE sujeto_externo = 'dev-integrado-auxiliar');
    DECLARE @FamilyAccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ResidentId UNIQUEIDENTIFIER = NEWID();
    DECLARE @EpisodeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @FamilyId UNIQUEIDENTIFIER = NEWID();
    DECLARE @LinkId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.residentes
        (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
    VALUES (@ResidentId, @CenterId, 'Residente Portal Familiar (ficticio)', '1939-05-03', 'unknown', 'ACTIVE', @Now, @StaffId, 'ADMINISTRACION');

    INSERT INTO dbo.episodios_residente_centro (id, residente_id, centro_id, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
    VALUES (@EpisodeId, @ResidentId, @CenterId, @Now, @Now, @StaffId, 'ADMINISTRACION');

    INSERT INTO dbo.intervalos_ubicacion_residente
        (id, residente_id, centro_id, episodio_id, unidad_id, vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
    VALUES (NEWID(), @ResidentId, @CenterId, @EpisodeId, @UnitId, @Now, @Now, @StaffId, 'ADMINISTRACION');

    INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en)
    VALUES (@FamilyAccountId, 'dev-integrado-familiar', 'Familiar Portal Prueba (ficticio)', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @FamilyAccountId, @CenterId, 'FAMILIAR', 'ACTIVE', @Now, @StaffId);

    INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, creado_por_cuenta_id, creado_en, cuenta_id)
    VALUES (@FamilyId, @CenterId, 'Familiar Portal Prueba (ficticio)', '600000001', @StaffId, @Now, @FamilyAccountId);

    INSERT INTO dbo.residentes_familiares (id, centro_id, residente_id, familiar_id, relacion, vinculado_por_cuenta_id, vinculado_en)
    VALUES (@LinkId, @CenterId, @ResidentId, @FamilyId, 'Hija', @StaffId, @Now);

    -- La autorización se abre y se activa, como lo hace Administración (numeración sin huecos).
    INSERT INTO dbo.familiares_autorizaciones_cambios
        (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
    VALUES
        (NEWID(), @CenterId, @LinkId, 1, 'PENDIENTE', NULL, NULL, @StaffId, 'ADMINISTRACION', @Now),
        (NEWID(), @CenterId, @LinkId, 2, 'ACTIVA', NULL, NULL, @StaffId, 'ADMINISTRACION', DATEADD(SECOND, 1, @Now));
END
GO
