/*
 * Datos semilla de desarrollo para el vertical Enfermería (grupo E1: ENF-01/ENF-17/ENF-18) —
 * exclusivamente ficticios. NO ejecutar contra un entorno con datos reales. Idempotente: si la cuenta
 * 'dev-enfermeria' ya existe, no inserta nada.
 *
 * A diferencia de dev_seed_auxiliar.sql, aquí NO se concede ninguna fila en ambitos_perfil_residente:
 * Enfermería sigue la regla de "ámbito por defecto" (SqlEnfermeriaResidentDirectory /
 * SqlAuthorizationEvidenceProvider) — sin restricciones explícitas, ve todos los residentes de su unidad
 * concedida. Ninguno tiene basal vigente todavía: la autoría/firma del basal (ENF-19 a ENF-22) es el
 * siguiente grupo del vertical Enfermería.
 */

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-enfermeria')
BEGIN
    DECLARE @AccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CenterId UNIQUEIDENTIFIER = NEWID();
    DECLARE @UnitId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
    VALUES (@AccountId, 'dev-enfermeria', 'ACTIVE', @Now);

    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES (@CenterId, 'CENTRO-DEV-ENF', 'Centro de desarrollo Enfermería (datos ficticios)', 'ACTIVE', @Now);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
    VALUES (@UnitId, @CenterId, 'UNIDAD-DEV-ENF', 'Unidad de desarrollo Enfermería', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES (@ProfileScopeId, @AccountId, @CenterId, 'ENFERMERIA', 'ACTIVE', @Now, @AccountId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @Now, @AccountId);

    DECLARE @Residents TABLE (ResidentId UNIQUEIDENTIFIER, DisplayName NVARCHAR(200), BirthDate DATE);
    INSERT INTO @Residents VALUES
        (NEWID(), 'Residente Enfermería Uno (ficticio)', '1937-06-20'),
        (NEWID(), 'Residente Enfermería Dos (ficticio)', '1944-09-08');

    DECLARE @ResidentId UNIQUEIDENTIFIER, @DisplayName NVARCHAR(200), @BirthDate DATE;
    DECLARE residents_cursor CURSOR LOCAL FOR SELECT ResidentId, DisplayName, BirthDate FROM @Residents;
    OPEN residents_cursor;
    FETCH NEXT FROM residents_cursor INTO @ResidentId, @DisplayName, @BirthDate;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @EpisodeId UNIQUEIDENTIFIER = NEWID();

        INSERT INTO dbo.residentes
            (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
        VALUES (@ResidentId, @CenterId, @DisplayName, @BirthDate, 'unknown', 'ACTIVE', @Now, @AccountId, 'ADMINISTRACION');

        INSERT INTO dbo.episodios_residente_centro (id, residente_id, centro_id, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
        VALUES (@EpisodeId, @ResidentId, @CenterId, @Now, @Now, @AccountId, 'ADMINISTRACION');

        INSERT INTO dbo.intervalos_ubicacion_residente
            (id, residente_id, centro_id, episodio_id, unidad_id, vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
        VALUES (NEWID(), @ResidentId, @CenterId, @EpisodeId, @UnitId, @Now, @Now, @AccountId, 'ADMINISTRACION');

        FETCH NEXT FROM residents_cursor INTO @ResidentId, @DisplayName, @BirthDate;
    END;
    CLOSE residents_cursor;
    DEALLOCATE residents_cursor;
END
GO

SELECT
    account.sujeto_externo AS ExternalSubject,
    profile.id AS ProfileScopeId,
    profile.centro_id AS CenterId
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
 WHERE account.sujeto_externo = 'dev-enfermeria';
