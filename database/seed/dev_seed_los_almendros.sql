/*
 * Centro de prueba «Residencia Los Almendros», el del ejemplo del Flujo de trabajo (src/ResidApp.Web/wwwroot/flujo-de-trabajo.html), con los
 * pasos 1 a 5 del ejemplo ya hechos: el centro montado, una cuenta por perfil y Rosa dada de alta con su hija Elena. El basal, los episodios,
 * los comunicados y las citas se hacen en vivo. Exclusivamente ficticio. NO ejecutar contra un entorno con datos reales.
 *
 * Sin contraseñas: se entra por el acceso de desarrollo (en Azure dev, con su clave) hasta que llegue el proveedor de identidad (ADR 0007).
 * Idempotente: si la cuenta 'almendros-administracion' ya existe, no hace nada. Todo o nada: una transacción con XACT_ABORT. No se puede
 * reiniciar (la auditoría es inmutable y no hay borrados): el centro acumula lo que se haga en él.
 *
 * Cuentas, nombradas por perfil como en el Flujo de trabajo:
 *   almendros-administracion  Administración principal; Planta 1 y Planta 2
 *   almendros-direccion       Dirección Clínica; las dos plantas; lectura clínica auditada, rangos de referencia y plazos de los hitos
 *   almendros-medicina        Medicina; las dos plantas; basal
 *   almendros-enfermeria      Enfermería; las dos plantas; basal; miembro de los equipos de las dos plantas
 *   almendros-auxiliar        Auxiliar de la Planta 1, con Rosa asignada
 *   almendros-elena           Portal Familiar: Elena, hija de Rosa y su contacto prioritario, con la autorización activa
 * Plataforma sigue siendo 'dev-plataforma', que es global (dev_seed_plataforma.sql).
 *
 * Estructura: «Edificio principal» con Planta 1 y Planta 2 (una unidad cada una), habitaciones 101–104 y 201–204 con plazas A y B. Rosa está
 * en Planta 1 · 104 · B. Turnos de mañana, tarde y noche, sin planificación (la hace Administración). Rangos de referencia con los valores
 * sugeridos por CJ. Comunicados con el horario por defecto (19:00, una hora de margen). Citas con reserva directa, como
 * dev_seed_familiar_citas.sql.
 */

SET QUOTED_IDENTIFIER ON;
GO

SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'almendros-administracion')
BEGIN
    BEGIN TRANSACTION;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @CenterId UNIQUEIDENTIFIER = NEWID(), @BuildingId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Floor1Id UNIQUEIDENTIFIER = NEWID(), @Floor2Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @Unit1Id UNIQUEIDENTIFIER = NEWID(), @Unit2Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @AdminId UNIQUEIDENTIFIER = NEWID(), @DirectionId UNIQUEIDENTIFIER = NEWID(), @MedicineId UNIQUEIDENTIFIER = NEWID();
    DECLARE @NursingId UNIQUEIDENTIFIER = NEWID(), @AuxiliarId UNIQUEIDENTIFIER = NEWID(), @ElenaAccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @AdminScope UNIQUEIDENTIFIER = NEWID(), @DirectionScope UNIQUEIDENTIFIER = NEWID(), @MedicineScope UNIQUEIDENTIFIER = NEWID();
    DECLARE @NursingScope UNIQUEIDENTIFIER = NEWID(), @AuxiliarScope UNIQUEIDENTIFIER = NEWID();
    DECLARE @Team1Id UNIQUEIDENTIFIER = NEWID(), @Team2Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @RosaId UNIQUEIDENTIFIER = NEWID(), @EpisodeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ElenaId UNIQUEIDENTIFIER = NEWID(), @LinkId UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en)
    VALUES
        (@AdminId, 'almendros-administracion', N'Administración (Los Almendros)', 'ACTIVE', @Now),
        (@DirectionId, 'almendros-direccion', N'Dirección Clínica (Los Almendros)', 'ACTIVE', @Now),
        (@MedicineId, 'almendros-medicina', N'Medicina (Los Almendros)', 'ACTIVE', @Now),
        (@NursingId, 'almendros-enfermeria', N'Enfermería (Los Almendros)', 'ACTIVE', @Now),
        (@AuxiliarId, 'almendros-auxiliar', N'Auxiliar Planta 1 (Los Almendros)', 'ACTIVE', @Now),
        (@ElenaAccountId, 'almendros-elena', N'Elena Sánchez (ficticia)', 'ACTIVE', @Now);

    -- 1. Nace el centro, con su Administración principal (0047).
    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES (@CenterId, 'LOS-ALMENDROS', N'Residencia Los Almendros (ficticia)', 'ACTIVE', @Now);

    -- 2. Se monta el centro: edificio, plantas, unidades, habitaciones y plazas (0029).
    INSERT INTO dbo.edificios (id, centro_id, nombre_visible, creado_en, creado_por_cuenta_id)
    VALUES (@BuildingId, @CenterId, N'Edificio principal', @Now, @AdminId);

    INSERT INTO dbo.plantas (id, centro_id, edificio_id, nombre_visible, creado_en, creado_por_cuenta_id)
    VALUES (@Floor1Id, @CenterId, @BuildingId, N'Planta 1', @Now, @AdminId), (@Floor2Id, @CenterId, @BuildingId, N'Planta 2', @Now, @AdminId);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en, edificio_id, planta_id)
    VALUES
        (@Unit1Id, @CenterId, 'PLANTA-1', N'Planta 1', 'ACTIVE', @Now, @BuildingId, @Floor1Id),
        (@Unit2Id, @CenterId, 'PLANTA-2', N'Planta 2', 'ACTIVE', @Now, @BuildingId, @Floor2Id);

    INSERT INTO dbo.habitaciones (id, centro_id, unidad_id, nombre_visible, creado_en, creado_por_cuenta_id)
    SELECT NEWID(), @CenterId, unit.id, unit.floor + room.number, @Now, @AdminId
      FROM (VALUES (@Unit1Id, N'1'), (@Unit2Id, N'2')) AS unit(id, floor)
     CROSS JOIN (VALUES (N'01'), (N'02'), (N'03'), (N'04')) AS room(number);

    INSERT INTO dbo.plazas (id, centro_id, unidad_id, habitacion_id, nombre_visible, creado_en, creado_por_cuenta_id)
    SELECT NEWID(), room.centro_id, room.unidad_id, room.id, place.name, @Now, @AdminId
      FROM dbo.habitaciones room
     CROSS JOIN (VALUES (N'A'), (N'B')) AS place(name)
     WHERE room.centro_id = @CenterId;

    -- Las cuentas del personal con su perfil, sus unidades y sus permisos.
    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES
        (@AdminScope, @AdminId, @CenterId, 'ADMINISTRACION', 'ACTIVE', @Now, @AdminId),
        (@DirectionScope, @DirectionId, @CenterId, 'DIRECCION_CLINICA', 'ACTIVE', @Now, @AdminId),
        (@MedicineScope, @MedicineId, @CenterId, 'MEDICINA', 'ACTIVE', @Now, @AdminId),
        (@NursingScope, @NursingId, @CenterId, 'ENFERMERIA', 'ACTIVE', @Now, @AdminId),
        (@AuxiliarScope, @AuxiliarId, @CenterId, 'AUXILIAR', 'ACTIVE', @Now, @AdminId),
        (NEWID(), @ElenaAccountId, @CenterId, 'FAMILIAR', 'ACTIVE', @Now, @AdminId);

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    SELECT NEWID(), scope.id, @CenterId, unit.id, @Now, @AdminId
      FROM (VALUES (@AdminScope), (@DirectionScope), (@MedicineScope), (@NursingScope)) AS scope(id)
     CROSS JOIN (VALUES (@Unit1Id), (@Unit2Id)) AS unit(id)
    UNION ALL
    SELECT NEWID(), @AuxiliarScope, @CenterId, @Unit1Id, @Now, @AdminId;

    INSERT INTO dbo.administraciones_principales_cambios
        (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
    VALUES (NEWID(), @CenterId, @AdminScope, 1, 1, @AdminId, 'ADMINISTRACION', @Now);

    INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
    VALUES
        (NEWID(), @NursingScope, @CenterId, 'BASELINE_INITIAL_COMPLETE', @Now, @AdminId),
        (NEWID(), @NursingScope, @CenterId, 'BASELINE_REEVALUATE', @Now, @AdminId),
        (NEWID(), @MedicineScope, @CenterId, 'BASELINE_INITIAL_COMPLETE', @Now, @AdminId),
        (NEWID(), @MedicineScope, @CenterId, 'BASELINE_REEVALUATE', @Now, @AdminId),
        (NEWID(), @DirectionScope, @CenterId, 'CLINICAL_DETAIL_READ', @Now, @AdminId),
        (NEWID(), @DirectionScope, @CenterId, 'REFERENCE_RANGES_MANAGE', @Now, @AdminId),
        (NEWID(), @DirectionScope, @CenterId, 'PROCESS_DEADLINES_MANAGE', @Now, @AdminId);

    -- Turnos y equipos (0027), sin planificación.
    INSERT INTO dbo.turnos_catalogo (id, centro_id, nombre_visible, hora_inicio, hora_fin, creado_en, creado_por_cuenta_id)
    VALUES
        (NEWID(), @CenterId, N'Mañana', '08:00', '15:00', @Now, @AdminId),
        (NEWID(), @CenterId, N'Tarde', '15:00', '22:00', @Now, @AdminId),
        (NEWID(), @CenterId, N'Noche', '22:00', '08:00', @Now, @AdminId);

    INSERT INTO dbo.equipos (id, centro_id, unidad_id, nombre_visible, creado_en, creado_por_cuenta_id)
    VALUES (@Team1Id, @CenterId, @Unit1Id, N'Enfermería Planta 1', @Now, @AdminId), (@Team2Id, @CenterId, @Unit2Id, N'Enfermería Planta 2', @Now, @AdminId);

    INSERT INTO dbo.equipos_miembros (id, centro_id, equipo_id, cuenta_id, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @CenterId, @Team1Id, @NursingId, @Now, @AdminId), (NEWID(), @CenterId, @Team2Id, @NursingId, @Now, @AdminId);

    -- 3. Las referencias del centro: rangos de referencia con los valores sugeridos (VitalSignReferenceRanges.Suggested) y su historial (0008).
    DECLARE @Ranges TABLE (code NVARCHAR(32), minimum DECIMAL(6, 1), maximum DECIMAL(6, 1));
    INSERT INTO @Ranges VALUES
        ('TEMPERATURA', 36, 36.9), ('TENSION_SISTOLICA', 90, 139), ('TENSION_DIASTOLICA', 60, 89), ('FRECUENCIA_CARDIACA', 60, 100),
        ('FRECUENCIA_RESPIRATORIA', 12, 20), ('SATURACION_O2', 95, 100), ('GLUCEMIA', 70, 120);

    INSERT INTO dbo.rangos_referencia_constantes (centro_id, constante_codigo, minimo, maximo)
    SELECT @CenterId, code, minimum, maximum FROM @Ranges;

    INSERT INTO dbo.rangos_referencia_constantes_historial
        (id, centro_id, constante_codigo, minimo_anterior, maximo_anterior, minimo_nuevo, maximo_nuevo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
    SELECT NEWID(), @CenterId, code, NULL, NULL, minimum, maximum, @DirectionId, 'DIRECCION_CLINICA', @Now FROM @Ranges;

    -- Citas con reserva directa (0054), como dev_seed_familiar_citas.sql.
    INSERT INTO dbo.citas_configuracion_centro (centro_id, modo_codigo, antelacion_minima_horas, horizonte_maximo_dias, version)
    VALUES (@CenterId, 'RESERVA_DIRECTA', 24, 30, 1);

    INSERT INTO dbo.citas_modalidades_habilitadas (centro_id, equipo_codigo, modalidad_codigo)
    SELECT @CenterId, team.code, modality.code
      FROM (VALUES ('MEDICO'), ('ENFERMERIA')) AS team(code)
     CROSS JOIN (VALUES ('PRESENCIAL'), ('TELEFONICA'), ('VIDEOCONFERENCIA')) AS modality(code);

    INSERT INTO dbo.citas_franjas (id, centro_id, equipo_codigo, dia_semana, hora_inicio, hora_fin, duracion_minutos, creada_por_cuenta_id, creada_en)
    SELECT NEWID(), @CenterId, window.team, day.value, window.starts, window.ends, 30, @AdminId, @Now
      FROM (VALUES
               ('MEDICO',     CAST('10:00' AS TIME(0)), CAST('13:00' AS TIME(0))),
               ('MEDICO',     CAST('16:00' AS TIME(0)), CAST('18:00' AS TIME(0))),
               ('ENFERMERIA', CAST('09:00' AS TIME(0)), CAST('12:00' AS TIME(0))),
               ('ENFERMERIA', CAST('16:00' AS TIME(0)), CAST('18:00' AS TIME(0)))) AS window(team, starts, ends)
     CROSS JOIN (VALUES (1), (2), (3), (4), (5)) AS day(value);

    -- 4. Alta de Rosa en Planta 1 · 104 · B, asignada al Auxiliar de la planta. Sin basal: lo crea y firma Enfermería en vivo.
    INSERT INTO dbo.residentes
        (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
    VALUES (@RosaId, @CenterId, N'Rosa Sánchez (ficticia)', '1936-04-12', 'female', 'ACTIVE', @Now, @AdminId, 'ADMINISTRACION');

    INSERT INTO dbo.episodios_residente_centro (id, residente_id, centro_id, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
    VALUES (@EpisodeId, @RosaId, @CenterId, @Now, @Now, @AdminId, 'ADMINISTRACION');

    INSERT INTO dbo.intervalos_ubicacion_residente
        (id, residente_id, centro_id, episodio_id, unidad_id, edificio_id, planta_id, habitacion_id, plaza_id,
         vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
    SELECT NEWID(), @RosaId, @CenterId, @EpisodeId, @Unit1Id, @BuildingId, @Floor1Id, room.id, place.id, @Now, @Now, @AdminId, 'ADMINISTRACION'
      FROM dbo.habitaciones room
      JOIN dbo.plazas place ON place.habitacion_id = room.id AND place.centro_id = room.centro_id
     WHERE room.centro_id = @CenterId AND room.nombre_visible = N'104' AND place.nombre_visible = N'B';

    INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @AuxiliarScope, @CenterId, @RosaId, @Now, @AdminId);

    -- 5. Elena: hija de Rosa, su contacto prioritario, con la autorización abierta y activada (numeración sin huecos) y acceso al portal.
    INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, creado_por_cuenta_id, creado_en, cuenta_id)
    VALUES (@ElenaId, @CenterId, N'Elena Sánchez (ficticia)', '600000002', @AdminId, @Now, @ElenaAccountId);

    INSERT INTO dbo.residentes_familiares (id, centro_id, residente_id, familiar_id, relacion, vinculado_por_cuenta_id, vinculado_en)
    VALUES (@LinkId, @CenterId, @RosaId, @ElenaId, N'Hija', @AdminId, @Now);

    INSERT INTO dbo.residentes_contacto_urgente
        (id, centro_id, residente_id, numero, vinculo_id, accion_codigo, designado_por_cuenta_id, designado_por_perfil, designado_en)
    VALUES (NEWID(), @CenterId, @RosaId, 1, @LinkId, 'REEMPLAZAR', @AdminId, 'ADMINISTRACION', @Now);

    INSERT INTO dbo.familiares_autorizaciones_cambios
        (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
    VALUES
        (NEWID(), @CenterId, @LinkId, 1, 'PENDIENTE', NULL, NULL, @AdminId, 'ADMINISTRACION', @Now),
        (NEWID(), @CenterId, @LinkId, 2, 'ACTIVA', NULL, NULL, @AdminId, 'ADMINISTRACION', DATEADD(SECOND, 1, @Now));

    COMMIT TRANSACTION;
END
GO

SELECT account.sujeto_externo AS Cuenta, profile.perfil_codigo AS Perfil
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
 WHERE account.sujeto_externo LIKE 'almendros-%'
 ORDER BY account.sujeto_externo;
