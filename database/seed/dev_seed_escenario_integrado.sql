/*
 * Escenario de prueba integrado para CJ (Product Owner): tres cuentas que comparten un único centro y
 * unidad, para recorrer en persona el flujo real que cruza tres perfiles sobre el mismo residente
 * (Auxiliar registra un cambio -> Enfermería lo ve en su bandeja y firma el basal -> Dirección Clínica
 * consulta ese basal firmado). Exclusivamente ficticio. NO ejecutar contra un entorno con datos reales.
 * Idempotente: si la cuenta 'dev-integrado-auxiliar' ya existe, no inserta nada.
 *
 * A diferencia de dev_seed_auxiliar.sql / dev_seed_enfermeria.sql (cada vertical aislado en su propio
 * centro, sin poder combinarse entre sí), aquí las tres cuentas comparten centro y unidad a propósito:
 * es el único seed pensado para demostrar el trabajo más reciente (grupos E2/E3/E4 de Enfermería) de
 * principio a fin, no solo cada pantalla por separado.
 *
 * El centro, el primer residente y el ámbito de Dirección Clínica usan GUID fijos (no NEWID()) porque son
 * los tres valores que hay que teclear a mano en Baseline/Direction (AmbitoPerfilId, CentroId,
 * ResidenteId): esa pantalla todavía no tiene selector amigable. Fijarlos permite escribirlos una sola vez
 * en el Manual de usuario y que sigan siendo válidos aunque se reaplique este script. El resto de
 * identificadores (cuentas, unidad, segundo residente) usa NEWID(): nunca hace falta teclearlos, el login
 * de desarrollo usa el texto sujeto_externo, no un GUID.
 */

-- Los índices filtrados de ambitos_perfil/ambitos_perfil_unidad/ambitos_perfil_residente/permisos_perfil
-- exigen QUOTED_IDENTIFIER ON; sqlcmd lo trae OFF por defecto salvo que se invoque con -I.
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-integrado-auxiliar')
BEGIN
    DECLARE @CenterId UNIQUEIDENTIFIER = 'A1000000-0000-0000-0000-000000000001';
    DECLARE @ResidentAId UNIQUEIDENTIFIER = 'A1000000-0000-0000-0000-000000000002';
    DECLARE @ProfileScopeDireccionId UNIQUEIDENTIFIER = 'A1000000-0000-0000-0000-000000000003';

    DECLARE @UnitId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ResidentBId UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuxiliarAccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @EnfermeriaAccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @DireccionAccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeAuxiliarId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ProfileScopeEnfermeriaId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
    VALUES
        (@AuxiliarAccountId, 'dev-integrado-auxiliar', 'ACTIVE', @Now),
        (@EnfermeriaAccountId, 'dev-integrado-enfermeria', 'ACTIVE', @Now),
        (@DireccionAccountId, 'dev-integrado-direccion', 'ACTIVE', @Now);

    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES (@CenterId, 'CENTRO-DEV-INTEGRADO', 'Centro de desarrollo — escenario integrado (datos ficticios)', 'ACTIVE', @Now);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
    VALUES (@UnitId, @CenterId, 'UNIDAD-DEV-INTEGRADO', 'Unidad de desarrollo — escenario integrado', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES
        (@ProfileScopeAuxiliarId, @AuxiliarAccountId, @CenterId, 'AUXILIAR', 'ACTIVE', @Now, @AuxiliarAccountId),
        (@ProfileScopeEnfermeriaId, @EnfermeriaAccountId, @CenterId, 'ENFERMERIA', 'ACTIVE', @Now, @EnfermeriaAccountId),
        (@ProfileScopeDireccionId, @DireccionAccountId, @CenterId, 'DIRECCION_CLINICA', 'ACTIVE', @Now, @DireccionAccountId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    VALUES
        (NEWID(), @ProfileScopeAuxiliarId, @CenterId, @UnitId, @Now, @AuxiliarAccountId),
        (NEWID(), @ProfileScopeEnfermeriaId, @CenterId, @UnitId, @Now, @EnfermeriaAccountId),
        (NEWID(), @ProfileScopeDireccionId, @CenterId, @UnitId, @Now, @DireccionAccountId);

    -- Enfermería necesita permiso explícito para crear/reevaluar un borrador de basal (ENF-20/BAS-*);
    -- Dirección Clínica necesita permiso explícito para leer el basal en modo auditado (Baseline/Direction).
    INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
    VALUES
        (NEWID(), @ProfileScopeEnfermeriaId, @CenterId, 'BASELINE_INITIAL_COMPLETE', @Now, @EnfermeriaAccountId),
        (NEWID(), @ProfileScopeEnfermeriaId, @CenterId, 'BASELINE_REEVALUATE', @Now, @EnfermeriaAccountId),
        (NEWID(), @ProfileScopeDireccionId, @CenterId, 'CLINICAL_DETAIL_READ', @Now, @DireccionAccountId);

    -- Dos residentes ficticios en la unidad compartida. El primero (ResidentAId) es el que se usa en el
    -- recorrido guiado del Manual: sin basal firmado todavía, para que CJ pueda crearlo y firmarlo ella
    -- misma como Enfermería y consultarlo después como Dirección Clínica.
    DECLARE @Residents TABLE (ResidentId UNIQUEIDENTIFIER, DisplayName NVARCHAR(200), BirthDate DATE);
    INSERT INTO @Residents VALUES
        (@ResidentAId, 'Residente Integrado Uno (ficticio)', '1940-02-14'),
        (@ResidentBId, 'Residente Integrado Dos (ficticio)', '1946-08-22');

    DECLARE @ResidentId UNIQUEIDENTIFIER, @DisplayName NVARCHAR(200), @BirthDate DATE;
    DECLARE residents_cursor CURSOR LOCAL FOR SELECT ResidentId, DisplayName, BirthDate FROM @Residents;
    OPEN residents_cursor;
    FETCH NEXT FROM residents_cursor INTO @ResidentId, @DisplayName, @BirthDate;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @EpisodeId UNIQUEIDENTIFIER = NEWID();

        INSERT INTO dbo.residentes
            (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
        VALUES (@ResidentId, @CenterId, @DisplayName, @BirthDate, 'unknown', 'ACTIVE', @Now, @AuxiliarAccountId, 'ADMINISTRACION');

        INSERT INTO dbo.episodios_residente_centro (id, residente_id, centro_id, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
        VALUES (@EpisodeId, @ResidentId, @CenterId, @Now, @Now, @AuxiliarAccountId, 'ADMINISTRACION');

        INSERT INTO dbo.intervalos_ubicacion_residente
            (id, residente_id, centro_id, episodio_id, unidad_id, vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
        VALUES (NEWID(), @ResidentId, @CenterId, @EpisodeId, @UnitId, @Now, @Now, @AuxiliarAccountId, 'ADMINISTRACION');

        INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
        VALUES (NEWID(), @ProfileScopeAuxiliarId, @CenterId, @ResidentId, @Now, @AuxiliarAccountId);

        FETCH NEXT FROM residents_cursor INTO @ResidentId, @DisplayName, @BirthDate;
    END;
    CLOSE residents_cursor;
    DEALLOCATE residents_cursor;
END
GO

SELECT
    account.sujeto_externo AS ExternalSubject,
    profile.perfil_codigo AS Perfil,
    profile.id AS ProfileScopeId,
    profile.centro_id AS CenterId
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
 WHERE account.sujeto_externo IN ('dev-integrado-auxiliar', 'dev-integrado-enfermeria', 'dev-integrado-direccion');

SELECT id AS ResidentId, nombre_visible AS DisplayName
  FROM dbo.residentes
 WHERE centro_id = 'A1000000-0000-0000-0000-000000000001';
