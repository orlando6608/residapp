/*
 * Datos semilla de desarrollo para el vertical Residente/Basal — exclusivamente ficticios.
 * NO ejecutar contra un entorno con datos reales. Idempotente: si la cuenta 'dev-admin' ya existe, no
 * inserta nada y solo devuelve los identificadores para usarlos en las pantallas de ResidApp.Web
 * (DevAuth/Login para la identidad, Residents/Create para el alta).
 *
 * Perfil sembrado: ADMINISTRACION, porque ResidentBaselinePolicy.AuthorizeResidentIdentityCreate le
 * permite dar de alta residentes sin necesidad de permisos adicionales en dbo.profile_permissions.
 */

IF NOT EXISTS (SELECT 1 FROM dbo.accounts WHERE external_subject = 'dev-admin')
BEGIN
    DECLARE @AccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CenterId UNIQUEIDENTIFIER = NEWID();
    DECLARE @UnitId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.accounts (id, external_subject, status, created_at)
    VALUES (@AccountId, 'dev-admin', 'ACTIVE', @Now);

    INSERT INTO dbo.centers (id, code, display_name, status, created_at)
    VALUES (@CenterId, 'CENTRO-DEV', 'Centro de desarrollo (datos ficticios)', 'ACTIVE', @Now);

    INSERT INTO dbo.units (id, center_id, code, display_name, status, created_at)
    VALUES (@UnitId, @CenterId, 'UNIDAD-DEV', 'Unidad de desarrollo', 'ACTIVE', @Now);

    INSERT INTO dbo.profile_scopes (id, account_id, center_id, profile_code, status, granted_at, granted_by_account_id)
    VALUES (@ProfileScopeId, @AccountId, @CenterId, 'ADMINISTRACION', 'ACTIVE', @Now, @AccountId);

    INSERT INTO dbo.profile_unit_scopes (id, profile_scope_id, center_id, unit_id, granted_at, granted_by_account_id)
    VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @Now, @AccountId);
END
GO

SELECT
    account.external_subject AS ExternalSubject,
    profile.id AS ProfileScopeId,
    profile.center_id AS CenterId,
    unit.id AS UnitId
  FROM dbo.accounts account
  JOIN dbo.profile_scopes profile ON profile.account_id = account.id
  JOIN dbo.profile_unit_scopes unit_scope ON unit_scope.profile_scope_id = profile.id
  JOIN dbo.units unit ON unit.id = unit_scope.unit_id
 WHERE account.external_subject = 'dev-admin';
