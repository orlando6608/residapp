/*
 * Datos semilla de desarrollo para probar la pantalla de selección de ámbito activo con más de un ámbito
 * (ProfileScope/Select) — exclusivamente ficticios. NO ejecutar contra un entorno con datos reales.
 * Idempotente: si la cuenta 'dev-multi' ya existe, el bloque de alta no inserta nada; los ámbitos
 * Medicina/Familiar y el permiso de Dirección Clínica se conceden en bloques idempotentes separados, más
 * abajo, para que una base ya sembrada con la versión original (solo 2 ámbitos) los reciba igual al
 * reaplicar este script (cuentas de prueba para CJ, docs/producto — ver Manual de usuario).
 *
 * A diferencia de dev_seed_residente_basal.sql (una única cuenta con un único ámbito, que autoselecciona
 * sin pedir confirmación), esta cuenta acumula cuatro ambitos_perfil activos (Enfermería y Medicina en el
 * centro A, Dirección Clínica y Familiar en el centro B), para ejercitar el camino en el que
 * ProfileScope/Select debe mostrar una lista y esperar una elección explícita (ADM-30). Medicina y
 * Familiar todavía no tienen ninguna pantalla propia construida: sus ámbitos solo demuestran que el
 * selector funciona con ellos, sin nada más que probar por ahora.
 */

-- Los índices filtrados de ambitos_perfil/ambitos_perfil_unidad/permisos_perfil (UX_ps_active, UX_pus_active,
-- UX_pp_active) exigen QUOTED_IDENTIFIER ON; sqlcmd lo trae OFF por defecto salvo que se invoque con -I.
SET QUOTED_IDENTIFIER ON;
GO

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

/*
 * Amplía 'dev-multi' de 2 a 4 ámbitos: MEDICINA en el mismo centro A que su ámbito Enfermería, y FAMILIAR
 * en el mismo centro B que su ámbito Dirección Clínica. Ninguno de los dos perfiles tiene pantallas
 * propias construidas todavía, así que no hace falta un centro/residentes dedicados: esta cuenta solo
 * necesita demostrar que el selector de ámbito (ProfileScope/Select) funciona igual con más de dos filas.
 * Bloque idempotente separado del original para que una base ya sembrada con la versión de 2 ámbitos
 * reciba los 2 nuevos igual al reaplicar este script.
 */
IF NOT EXISTS (
    SELECT 1 FROM dbo.ambitos_perfil ap
    JOIN dbo.cuentas c ON c.id = ap.cuenta_id
    WHERE c.sujeto_externo = 'dev-multi' AND ap.perfil_codigo = 'MEDICINA')
BEGIN
    DECLARE @GrantAccountId UNIQUEIDENTIFIER, @GrantCenterAId UNIQUEIDENTIFIER, @GrantCenterBId UNIQUEIDENTIFIER,
            @GrantUnitAId UNIQUEIDENTIFIER, @GrantUnitBId UNIQUEIDENTIFIER, @GrantNow DATETIME2(3) = SYSUTCDATETIME();
    SELECT @GrantAccountId = id FROM dbo.cuentas WHERE sujeto_externo = 'dev-multi';
    SELECT @GrantCenterAId = id FROM dbo.centros WHERE codigo = 'CENTRO-DEV-A';
    SELECT @GrantCenterBId = id FROM dbo.centros WHERE codigo = 'CENTRO-DEV-B';
    SELECT @GrantUnitAId = id FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-A' AND centro_id = @GrantCenterAId;
    SELECT @GrantUnitBId = id FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-B' AND centro_id = @GrantCenterBId;

    DECLARE @ProfileScopeMedId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeFamId UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES
        (@ProfileScopeMedId, @GrantAccountId, @GrantCenterAId, 'MEDICINA', 'ACTIVE', @GrantNow, @GrantAccountId),
        (@ProfileScopeFamId, @GrantAccountId, @GrantCenterBId, 'FAMILIAR', 'ACTIVE', @GrantNow, @GrantAccountId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    VALUES
        (NEWID(), @ProfileScopeMedId, @GrantCenterAId, @GrantUnitAId, @GrantNow, @GrantAccountId),
        (NEWID(), @ProfileScopeFamId, @GrantCenterBId, @GrantUnitBId, @GrantNow, @GrantAccountId);
END
GO

/*
 * Corrige un hueco de la versión original: el ámbito Dirección Clínica de 'dev-multi' nunca tuvo el
 * permiso CLINICAL_DETAIL_READ, así que jamás pudo usar Baseline/Direction. Se concede aquí, en un bloque
 * idempotente separado, mismo criterio que el bloque de permisos de dev_seed_enfermeria.sql.
 */
IF NOT EXISTS (
    SELECT 1 FROM dbo.permisos_perfil pp
    JOIN dbo.ambitos_perfil ap ON ap.id = pp.ambito_perfil_id
    JOIN dbo.cuentas c ON c.id = ap.cuenta_id
    WHERE c.sujeto_externo = 'dev-multi' AND ap.perfil_codigo = 'DIRECCION_CLINICA' AND pp.permiso_codigo = 'CLINICAL_DETAIL_READ')
BEGIN
    DECLARE @DirGrantAccountId UNIQUEIDENTIFIER, @DirProfileScopeId UNIQUEIDENTIFIER, @DirCenterId UNIQUEIDENTIFIER;
    SELECT @DirGrantAccountId = ap.cuenta_id, @DirProfileScopeId = ap.id, @DirCenterId = ap.centro_id
      FROM dbo.ambitos_perfil ap
      JOIN dbo.cuentas c ON c.id = ap.cuenta_id
     WHERE c.sujeto_externo = 'dev-multi' AND ap.perfil_codigo = 'DIRECCION_CLINICA';

    INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @DirProfileScopeId, @DirCenterId, 'CLINICAL_DETAIL_READ', SYSUTCDATETIME(), @DirGrantAccountId);
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
