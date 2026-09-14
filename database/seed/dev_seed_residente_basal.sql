/*
 * Datos semilla de desarrollo para el vertical Residente/Basal — exclusivamente ficticios.
 * NO ejecutar contra un entorno con datos reales. Idempotente: si la cuenta 'dev-admin' ya existe, no
 * inserta nada y solo devuelve los identificadores para usarlos en las pantallas de ResidApp.Web
 * (DevAuth/Login para la identidad, Residents/Create para el alta).
 *
 * Perfil sembrado: ADMINISTRACION, porque ResidentBaselinePolicy.AuthorizeResidentIdentityCreate le
 * permite dar de alta residentes sin necesidad de permisos adicionales en dbo.profile_permissions.
 */

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-admin')
BEGIN
    DECLARE @AccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CenterId UNIQUEIDENTIFIER = NEWID();
    DECLARE @UnitId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
    VALUES (@AccountId, 'dev-admin', 'ACTIVE', @Now);

    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES (@CenterId, 'CENTRO-DEV', 'Centro de desarrollo (datos ficticios)', 'ACTIVE', @Now);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
    VALUES (@UnitId, @CenterId, 'UNIDAD-DEV', 'Unidad de desarrollo', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES (@ProfileScopeId, @AccountId, @CenterId, 'ADMINISTRACION', 'ACTIVE', @Now, @AccountId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @Now, @AccountId);
END
GO

SELECT
    account.sujeto_externo AS ExternalSubject,
    profile.id AS ProfileScopeId,
    profile.centro_id AS CenterId,
    unit.id AS UnitId
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
  JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
  JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id
 WHERE account.sujeto_externo = 'dev-admin';
