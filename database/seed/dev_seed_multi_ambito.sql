/*
 * Datos semilla de desarrollo para probar la pantalla de selección de ámbito activo con más de un ámbito
 * (ProfileScope/Select) — exclusivamente ficticios. NO ejecutar contra un entorno con datos reales.
 * Idempotente: si la cuenta 'dev-multi' ya existe, no inserta nada.
 *
 * A diferencia de dev_seed_residente_basal.sql (una única cuenta con un único ámbito, que autoselecciona
 * sin pedir confirmación), esta cuenta tiene dos ambitos_perfil activos en centros distintos, para
 * ejercitar el camino en el que ProfileScope/Select debe mostrar una lista y esperar una elección explícita
 * (ADM-30).
 */

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-multi')
BEGIN
    DECLARE @AccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CenterAId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CenterBId UNIQUEIDENTIFIER = NEWID();
    DECLARE @UnitAId UNIQUEIDENTIFIER = NEWID();
    DECLARE @UnitBId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeAId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeBId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
    VALUES (@AccountId, 'dev-multi', 'ACTIVE', @Now);

    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES
        (@CenterAId, 'CENTRO-DEV-A', 'Centro de desarrollo A (datos ficticios)', 'ACTIVE', @Now),
        (@CenterBId, 'CENTRO-DEV-B', 'Centro de desarrollo B (datos ficticios)', 'ACTIVE', @Now);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
    VALUES
        (@UnitAId, @CenterAId, 'UNIDAD-DEV-A', 'Unidad de desarrollo A', 'ACTIVE', @Now),
        (@UnitBId, @CenterBId, 'UNIDAD-DEV-B', 'Unidad de desarrollo B', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES
        (@ProfileScopeAId, @AccountId, @CenterAId, 'ENFERMERIA', 'ACTIVE', @Now, @AccountId),
        (@ProfileScopeBId, @AccountId, @CenterBId, 'DIRECCION_CLINICA', 'ACTIVE', @Now, @AccountId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    VALUES
        (NEWID(), @ProfileScopeAId, @CenterAId, @UnitAId, @Now, @AccountId),
        (NEWID(), @ProfileScopeBId, @CenterBId, @UnitBId, @Now, @AccountId);
END
GO

SELECT
    account.sujeto_externo AS ExternalSubject,
    profile.id AS ProfileScopeId,
    profile.centro_id AS CenterId,
    profile.perfil_codigo AS Perfil
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
 WHERE account.sujeto_externo = 'dev-multi';
