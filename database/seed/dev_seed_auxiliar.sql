/*
 * Datos semilla de desarrollo para el vertical Auxiliar (grupo A1: AUX-01/AUX-02/AUX-03) — exclusivamente
 * ficticios. NO ejecutar contra un entorno con datos reales. Idempotente: si la cuenta 'dev-auxiliar' ya
 * existe, no inserta nada.
 *
 * A diferencia de dev_seed_residente_basal.sql, aquí los dos residentes se siembran directamente (no vía
 * Residents/Create) porque lo que se quiere probar es AUX-01/02/03 con residentes ya asignados. Ninguno
 * tiene basal vigente: no existe todavía, ni en este puerto ni en el prototipo legado, un caso de uso para
 * crear el contenido de un borrador de basal (ver src/ResidApp.Web/Controllers/BaselineController.cs) —
 * por eso ambos residentes muestran correctamente "Basal pendiente" hasta que el vertical Enfermería
 * construya esa capacidad (grupo E2 del plan de implementación).
 */

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-auxiliar')
BEGIN
    DECLARE @AccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CenterId UNIQUEIDENTIFIER = NEWID();
    DECLARE @UnitId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
    VALUES (@AccountId, 'dev-auxiliar', 'ACTIVE', @Now);

    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES (@CenterId, 'CENTRO-DEV-AUX', 'Centro de desarrollo Auxiliar (datos ficticios)', 'ACTIVE', @Now);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
    VALUES (@UnitId, @CenterId, 'UNIDAD-DEV-AUX', 'Unidad de desarrollo Auxiliar', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES (@ProfileScopeId, @AccountId, @CenterId, 'AUXILIAR', 'ACTIVE', @Now, @AccountId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @Now, @AccountId);

    DECLARE @Residents TABLE (ResidentId UNIQUEIDENTIFIER, DisplayName NVARCHAR(200), BirthDate DATE);
    INSERT INTO @Residents VALUES
        (NEWID(), 'Residente Auxiliar Uno (ficticio)', '1938-04-12'),
        (NEWID(), 'Residente Auxiliar Dos (ficticio)', '1942-11-03');

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

        INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
        VALUES (NEWID(), @ProfileScopeId, @CenterId, @ResidentId, @Now, @AccountId);

        FETCH NEXT FROM residents_cursor INTO @ResidentId, @DisplayName, @BirthDate;
    END;
    CLOSE residents_cursor;
    DEALLOCATE residents_cursor;
END
GO

SELECT
    account.sujeto_externo AS ExternalSubject,
    profile.id AS ProfileScopeId,
    profile.centro_id AS CenterId,
    resident.nombre_visible AS ResidenteAsignado
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
  JOIN dbo.ambitos_perfil_residente scope ON scope.ambito_perfil_id = profile.id
  JOIN dbo.residentes resident ON resident.id = scope.residente_id
 WHERE account.sujeto_externo = 'dev-auxiliar';
