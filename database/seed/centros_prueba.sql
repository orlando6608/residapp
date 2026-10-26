/*
 * Centros de prueba del entorno de desarrollo (Azure dev y la base local de la aplicación). Exclusivamente ficticio. NO ejecutar contra un
 * entorno con datos reales. Sustituye a las semillas dev_seed_* (2026-10-24), cuando se retiró el acceso de desarrollo (ADR 0007).
 *
 * Sin contraseñas: cada cuenta se da de alta con su invitación, como en un centro real. La primera de cada juego, la de Plataforma, con
 * database/operaciones/invitar_cuenta.sql; Plataforma invita a las Administraciones y a las cuentas de varios centros, y cada Administración
 * a su personal y a los familiares.
 *
 * Dos juegos de cuentas con los mismos perfiles, para que nadie comparta credenciales:
 *   CJ        plataforma-cj, almendros-<perfil>, almendros-elena, olmos-<perfil> y varios-centros
 *   Orlando   plataforma-orlando, almendros-orlando-<perfil>, almendros-orlando-pablo, olmos-orlando-<perfil> y varios-centros-orlando
 *             (también para las comprobaciones automáticas de Claude tras un despliegue)
 *
 * «Residencia Los Almendros (ficticia)», el centro del ejemplo del Flujo de trabajo (wwwroot/flujo-de-trabajo.html) y de las guías de
 * pruebas de CJ: «Edificio principal» con Planta 1 y Planta 2 (una unidad cada una), habitaciones 101–104 y 201–204 con plazas A y B.
 *   administracion  Administración de las dos plantas (la de CJ es la principal, la de Orlando no)
 *   direccion       Dirección Clínica de las dos plantas: lectura clínica auditada, rangos de referencia y plazos de los hitos
 *   medicina        Medicina de las dos plantas, con permiso de basal
 *   enfermeria      Enfermería de las dos plantas, con permiso de basal; miembro de los equipos de las dos plantas
 *   auxiliar        Auxiliar de la Planta 1, con cinco residentes asignados (Rosa, Antonio, Carmen, Lucía y Manuel)
 *   elena / pablo   Portal Familiar: hija e hijo de Rosa, con la autorización activa (Elena es su contacto prioritario)
 * Residentes (identificador fijo: las guías los citan en direcciones):
 *   Rosa Sánchez     1A000000-0000-4000-8000-000000000001  Planta 1 · 104 · B. Su familia entra en el portal (comunicados y citas)
 *   Antonio García   1A000000-0000-4000-8000-000000000002  Planta 1 · 101 · A. preparar-almendros.js le firma el basal
 *   Carmen López     1A000000-0000-4000-8000-000000000003  Planta 1 · 101 · B. Sin basal: para crearlo desde cero
 *   Lucía Fernández  1A000000-0000-4000-8000-000000000004  Planta 1 · 103 · A. Sin basal, para el recorrido guiado del Manual: no se toca
 *   Manuel Ruiz      1A000000-0000-4000-8000-000000000005  Planta 1 · 102 · A. preparar-almendros.js le pone la foto
 *   Pilar Moreno     1A000000-0000-4000-8000-000000000006  Planta 2 · 201 · A. Sin asignar al Auxiliar. preparar-almendros.js le deja
 *                                                         historial (basal firmado, eventos cerrados e informe de derivación firmado)
 * Turnos de mañana, tarde y noche, sin planificación. Rangos de referencia con los valores sugeridos por CJ. Comunicados con el horario por
 * defecto. Citas con reserva directa.
 *
 * «Residencia Los Olmos (ficticia)», el segundo centro, para el aislamiento entre centros (guía 8): una unidad sin estructura, con
 * olmos-administracion (principal) y olmos-enfermeria, y dos residentes (Jaime Ortega, 1A000000-0000-4000-8000-000000000011, y Teresa Gil,
 * 1A000000-0000-4000-8000-000000000012).
 *
 * varios-centros: Enfermería y Medicina en Los Olmos, Dirección Clínica y Familiar (sin ningún residente) en Los Almendros.
 *
 * Idempotente: si la cuenta 'almendros-administracion' ya existe, no hace nada. Todo o nada: una transacción con XACT_ABORT. No se puede
 * reiniciar (la auditoría es inmutable y no hay borrados): los centros acumulan lo que se haga en ellos.
 */

SET QUOTED_IDENTIFIER ON;
GO

SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'almendros-administracion')
BEGIN
    BEGIN TRANSACTION;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @PlatformCenterId UNIQUEIDENTIFIER = '5F3A1C00-0000-4000-8000-000000000001';
    DECLARE @CenterId UNIQUEIDENTIFIER = NEWID(), @BuildingId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Floor1Id UNIQUEIDENTIFIER = NEWID(), @Floor2Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @Unit1Id UNIQUEIDENTIFIER = NEWID(), @Unit2Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @OlmosId UNIQUEIDENTIFIER = NEWID(), @OlmosUnitId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Team1Id UNIQUEIDENTIFIER = NEWID(), @Team2Id UNIQUEIDENTIFIER = NEWID();

    -- Las cuentas: rol es lo que distingue a cada una dentro de su juego.
    DECLARE @Accounts TABLE (id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), juego NVARCHAR(10), rol NVARCHAR(40), sujeto NVARCHAR(200), nombre NVARCHAR(200));
    INSERT INTO @Accounts (juego, rol, sujeto, nombre)
    SELECT juego.codigo, rol.codigo,
           CASE rol.codigo
               WHEN 'plataforma' THEN 'plataforma-' + juego.sufijo
               WHEN 'varios-centros' THEN 'varios-centros' + juego.infijo
               WHEN 'familiar' THEN 'almendros' + juego.infijo + '-' + juego.familiar
               ELSE rol.centro + juego.infijo + '-' + rol.perfil END,
           CASE rol.codigo WHEN 'familiar' THEN juego.nombre_familiar ELSE rol.nombre + N' (' + juego.etiqueta + N')' END
      FROM (VALUES ('CJ', 'cj', '', 'elena', N'CJ', N'Elena Sánchez (ficticia)'),
                   ('ORLANDO', 'orlando', '-orlando', 'pablo', N'Orlando', N'Pablo Sánchez (ficticio)'))
           AS juego(codigo, sufijo, infijo, familiar, etiqueta, nombre_familiar)
     CROSS JOIN (VALUES
               ('plataforma', NULL, NULL, N'Plataforma'),
               ('almendros-administracion', 'almendros', 'administracion', N'Administración de Los Almendros'),
               ('almendros-direccion', 'almendros', 'direccion', N'Dirección Clínica de Los Almendros'),
               ('almendros-medicina', 'almendros', 'medicina', N'Medicina de Los Almendros'),
               ('almendros-enfermeria', 'almendros', 'enfermeria', N'Enfermería de Los Almendros'),
               ('almendros-auxiliar', 'almendros', 'auxiliar', N'Auxiliar de la Planta 1 de Los Almendros'),
               ('familiar', NULL, NULL, NULL),
               ('olmos-administracion', 'olmos', 'administracion', N'Administración de Los Olmos'),
               ('olmos-enfermeria', 'olmos', 'enfermeria', N'Enfermería de Los Olmos'),
               ('varios-centros', NULL, NULL, N'Varios centros')) AS rol(codigo, centro, perfil, nombre);

    INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en)
    SELECT id, sujeto, nombre, 'ACTIVE', @Now FROM @Accounts;

    DECLARE @AdminId UNIQUEIDENTIFIER = (SELECT id FROM @Accounts WHERE juego = 'CJ' AND rol = 'almendros-administracion');
    DECLARE @OlmosAdminId UNIQUEIDENTIFIER = (SELECT id FROM @Accounts WHERE juego = 'CJ' AND rol = 'olmos-administracion');
    DECLARE @DirectionId UNIQUEIDENTIFIER = (SELECT id FROM @Accounts WHERE juego = 'CJ' AND rol = 'almendros-direccion');

    -- 1. Nacen los centros (Los Almendros con su estructura: 0029).
    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES (@CenterId, 'LOS-ALMENDROS', N'Residencia Los Almendros (ficticia)', 'ACTIVE', @Now),
           (@OlmosId, 'LOS-OLMOS', N'Residencia Los Olmos (ficticia)', 'ACTIVE', @Now);

    INSERT INTO dbo.edificios (id, centro_id, nombre_visible, creado_en, creado_por_cuenta_id)
    VALUES (@BuildingId, @CenterId, N'Edificio principal', @Now, @AdminId);

    INSERT INTO dbo.plantas (id, centro_id, edificio_id, nombre_visible, creado_en, creado_por_cuenta_id)
    VALUES (@Floor1Id, @CenterId, @BuildingId, N'Planta 1', @Now, @AdminId), (@Floor2Id, @CenterId, @BuildingId, N'Planta 2', @Now, @AdminId);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en, edificio_id, planta_id)
    VALUES
        (@Unit1Id, @CenterId, 'PLANTA-1', N'Planta 1', 'ACTIVE', @Now, @BuildingId, @Floor1Id),
        (@Unit2Id, @CenterId, 'PLANTA-2', N'Planta 2', 'ACTIVE', @Now, @BuildingId, @Floor2Id);

    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
    VALUES (@OlmosUnitId, @OlmosId, 'UNIDAD-OLMOS', N'Unidad única', 'ACTIVE', @Now);

    INSERT INTO dbo.habitaciones (id, centro_id, unidad_id, nombre_visible, creado_en, creado_por_cuenta_id)
    SELECT NEWID(), @CenterId, unit.id, unit.floor + room.number, @Now, @AdminId
      FROM (VALUES (@Unit1Id, N'1'), (@Unit2Id, N'2')) AS unit(id, floor)
     CROSS JOIN (VALUES (N'01'), (N'02'), (N'03'), (N'04')) AS room(number);

    INSERT INTO dbo.plazas (id, centro_id, unidad_id, habitacion_id, nombre_visible, creado_en, creado_por_cuenta_id)
    SELECT NEWID(), room.centro_id, room.unidad_id, room.id, place.name, @Now, @AdminId
      FROM dbo.habitaciones room
     CROSS JOIN (VALUES (N'A'), (N'B')) AS place(name)
     WHERE room.centro_id = @CenterId;

    -- 2. Los ámbitos de cada cuenta: perfil, centro y unidades.
    DECLARE @Scopes TABLE (id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), cuenta_id UNIQUEIDENTIFIER, juego NVARCHAR(10), rol NVARCHAR(40),
                           centro_id UNIQUEIDENTIFIER, perfil VARCHAR(32), unidades VARCHAR(10));
    INSERT INTO @Scopes (cuenta_id, juego, rol, centro_id, perfil, unidades)
    SELECT account.id, account.juego, account.rol, scope.centro, scope.perfil, scope.unidades
      FROM @Accounts account
      JOIN (VALUES
               ('plataforma', @PlatformCenterId, 'PLATAFORMA', 'NINGUNA'),
               ('almendros-administracion', @CenterId, 'ADMINISTRACION', 'AMBAS'),
               ('almendros-direccion', @CenterId, 'DIRECCION_CLINICA', 'AMBAS'),
               ('almendros-medicina', @CenterId, 'MEDICINA', 'AMBAS'),
               ('almendros-enfermeria', @CenterId, 'ENFERMERIA', 'AMBAS'),
               ('almendros-auxiliar', @CenterId, 'AUXILIAR', 'PLANTA-1'),
               ('familiar', @CenterId, 'FAMILIAR', 'NINGUNA'),
               ('olmos-administracion', @OlmosId, 'ADMINISTRACION', 'OLMOS'),
               ('olmos-enfermeria', @OlmosId, 'ENFERMERIA', 'OLMOS'),
               ('varios-centros', @OlmosId, 'ENFERMERIA', 'OLMOS'),
               ('varios-centros', @OlmosId, 'MEDICINA', 'OLMOS'),
               ('varios-centros', @CenterId, 'DIRECCION_CLINICA', 'AMBAS'),
               ('varios-centros', @CenterId, 'FAMILIAR', 'NINGUNA')) AS scope(rol, centro, perfil, unidades)
        ON scope.rol = account.rol;

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    SELECT id, cuenta_id, centro_id, perfil, 'ACTIVE', @Now,
           CASE WHEN perfil = 'PLATAFORMA' THEN cuenta_id WHEN centro_id = @OlmosId THEN @OlmosAdminId ELSE @AdminId END
      FROM @Scopes;

    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
    SELECT NEWID(), scope.id, scope.centro_id, unit.id, @Now, CASE WHEN scope.centro_id = @OlmosId THEN @OlmosAdminId ELSE @AdminId END
      FROM @Scopes scope
      JOIN (VALUES ('AMBAS', @Unit1Id), ('AMBAS', @Unit2Id), ('PLANTA-1', @Unit1Id), ('OLMOS', @OlmosUnitId)) AS unit(grupo, id)
        ON unit.grupo = scope.unidades;

    -- Administración principal (0047): la de CJ en cada centro. La de Orlando en Los Almendros no lo es (guía 8: marcarla desde Plataforma).
    INSERT INTO dbo.administraciones_principales_cambios
        (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
    SELECT NEWID(), centro_id, id, 1, 1, cuenta_id, 'ADMINISTRACION', @Now
      FROM @Scopes WHERE juego = 'CJ' AND rol IN ('almendros-administracion', 'olmos-administracion');

    INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
    SELECT NEWID(), scope.id, scope.centro_id, permission.codigo, @Now, CASE WHEN scope.centro_id = @OlmosId THEN @OlmosAdminId ELSE @AdminId END
      FROM @Scopes scope
      JOIN (VALUES
               ('ENFERMERIA', 'BASELINE_INITIAL_COMPLETE'), ('ENFERMERIA', 'BASELINE_REEVALUATE'),
               ('MEDICINA', 'BASELINE_INITIAL_COMPLETE'), ('MEDICINA', 'BASELINE_REEVALUATE'),
               ('DIRECCION_CLINICA', 'CLINICAL_DETAIL_READ')) AS permission(perfil, codigo)
        ON permission.perfil = scope.perfil
    UNION ALL
    SELECT NEWID(), scope.id, scope.centro_id, permission.codigo, @Now, @AdminId
      FROM @Scopes scope
     CROSS JOIN (VALUES ('REFERENCE_RANGES_MANAGE'), ('PROCESS_DEADLINES_MANAGE')) AS permission(codigo)
     WHERE scope.rol = 'almendros-direccion';

    -- Turnos y equipos (0027), sin planificación. Las dos Enfermerías de Los Almendros en los dos equipos.
    INSERT INTO dbo.turnos_catalogo (id, centro_id, nombre_visible, hora_inicio, hora_fin, creado_en, creado_por_cuenta_id)
    VALUES
        (NEWID(), @CenterId, N'Mañana', '08:00', '15:00', @Now, @AdminId),
        (NEWID(), @CenterId, N'Tarde', '15:00', '22:00', @Now, @AdminId),
        (NEWID(), @CenterId, N'Noche', '22:00', '08:00', @Now, @AdminId);

    INSERT INTO dbo.equipos (id, centro_id, unidad_id, nombre_visible, creado_en, creado_por_cuenta_id)
    VALUES (@Team1Id, @CenterId, @Unit1Id, N'Enfermería Planta 1', @Now, @AdminId), (@Team2Id, @CenterId, @Unit2Id, N'Enfermería Planta 2', @Now, @AdminId);

    INSERT INTO dbo.equipos_miembros (id, centro_id, equipo_id, cuenta_id, concedido_en, concedido_por_cuenta_id)
    SELECT NEWID(), @CenterId, team.id, account.id, @Now, @AdminId
      FROM @Accounts account CROSS JOIN (VALUES (@Team1Id), (@Team2Id)) AS team(id)
     WHERE account.rol = 'almendros-enfermeria';

    -- 3. Rangos de referencia con los valores sugeridos (VitalSignReferenceRanges.Suggested) y su historial (0008).
    DECLARE @Ranges TABLE (code NVARCHAR(32), minimum DECIMAL(6, 1), maximum DECIMAL(6, 1));
    INSERT INTO @Ranges VALUES
        ('TEMPERATURA', 36, 36.9), ('TENSION_SISTOLICA', 90, 139), ('TENSION_DIASTOLICA', 60, 89), ('FRECUENCIA_CARDIACA', 60, 100),
        ('FRECUENCIA_RESPIRATORIA', 12, 20), ('SATURACION_O2', 95, 100), ('GLUCEMIA', 70, 120);

    INSERT INTO dbo.rangos_referencia_constantes (centro_id, constante_codigo, minimo, maximo)
    SELECT @CenterId, code, minimum, maximum FROM @Ranges;

    INSERT INTO dbo.rangos_referencia_constantes_historial
        (id, centro_id, constante_codigo, minimo_anterior, maximo_anterior, minimo_nuevo, maximo_nuevo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
    SELECT NEWID(), @CenterId, code, NULL, NULL, minimum, maximum, @DirectionId, 'DIRECCION_CLINICA', @Now FROM @Ranges;

    -- Citas con reserva directa (0054).
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

    -- 4. Los residentes, cada uno con su episodio y su ubicación (en Los Olmos, solo la unidad).
    DECLARE @Residents TABLE (id UNIQUEIDENTIFIER, episodio UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), centro_id UNIQUEIDENTIFIER,
                              nombre NVARCHAR(200), nacimiento DATE, sexo VARCHAR(16), unidad_id UNIQUEIDENTIFIER, habitacion NVARCHAR(10),
                              plaza NVARCHAR(10), asignado BIT);
    INSERT INTO @Residents (id, centro_id, nombre, nacimiento, sexo, unidad_id, habitacion, plaza, asignado)
    VALUES
        ('1A000000-0000-4000-8000-000000000001', @CenterId, N'Rosa Sánchez (ficticia)', '1936-04-12', 'female', @Unit1Id, N'104', N'B', 1),
        ('1A000000-0000-4000-8000-000000000002', @CenterId, N'Antonio García (ficticio)', '1934-11-03', 'male', @Unit1Id, N'101', N'A', 1),
        ('1A000000-0000-4000-8000-000000000003', @CenterId, N'Carmen López (ficticia)', '1939-02-27', 'female', @Unit1Id, N'101', N'B', 1),
        ('1A000000-0000-4000-8000-000000000004', @CenterId, N'Lucía Fernández (ficticia)', '1941-08-15', 'female', @Unit1Id, N'103', N'A', 1),
        ('1A000000-0000-4000-8000-000000000005', @CenterId, N'Manuel Ruiz (ficticio)', '1937-05-30', 'male', @Unit1Id, N'102', N'A', 1),
        ('1A000000-0000-4000-8000-000000000006', @CenterId, N'Pilar Moreno (ficticia)', '1933-12-09', 'female', @Unit2Id, N'201', N'A', 0),
        ('1A000000-0000-4000-8000-000000000011', @OlmosId, N'Jaime Ortega (ficticio)', '1938-03-21', 'male', @OlmosUnitId, NULL, NULL, 0),
        ('1A000000-0000-4000-8000-000000000012', @OlmosId, N'Teresa Gil (ficticia)', '1942-10-05', 'female', @OlmosUnitId, NULL, NULL, 0);

    INSERT INTO dbo.residentes
        (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
    SELECT id, centro_id, nombre, nacimiento, sexo, 'ACTIVE', @Now, CASE WHEN centro_id = @OlmosId THEN @OlmosAdminId ELSE @AdminId END, 'ADMINISTRACION'
      FROM @Residents;

    INSERT INTO dbo.episodios_residente_centro (id, residente_id, centro_id, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
    SELECT episodio, id, centro_id, @Now, @Now, CASE WHEN centro_id = @OlmosId THEN @OlmosAdminId ELSE @AdminId END, 'ADMINISTRACION'
      FROM @Residents;

    INSERT INTO dbo.intervalos_ubicacion_residente
        (id, residente_id, centro_id, episodio_id, unidad_id, edificio_id, planta_id, habitacion_id, plaza_id,
         vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
    SELECT NEWID(), resident.id, resident.centro_id, resident.episodio, resident.unidad_id,
           CASE WHEN room.id IS NULL THEN NULL ELSE @BuildingId END,
           CASE WHEN room.id IS NULL THEN NULL WHEN resident.unidad_id = @Unit1Id THEN @Floor1Id ELSE @Floor2Id END,
           room.id, place.id, @Now, @Now, CASE WHEN resident.centro_id = @OlmosId THEN @OlmosAdminId ELSE @AdminId END, 'ADMINISTRACION'
      FROM @Residents resident
      LEFT JOIN dbo.habitaciones room ON room.centro_id = resident.centro_id AND room.nombre_visible = resident.habitacion
      LEFT JOIN dbo.plazas place ON place.habitacion_id = room.id AND place.centro_id = room.centro_id AND place.nombre_visible = resident.plaza;

    -- Los dos Auxiliares tienen asignados a los mismos residentes de la Planta 1.
    INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
    SELECT NEWID(), scope.id, @CenterId, resident.id, @Now, @AdminId
      FROM @Scopes scope CROSS JOIN @Residents resident
     WHERE scope.rol = 'almendros-auxiliar' AND resident.asignado = 1;

    -- 5. Elena y Pablo: hija e hijo de Rosa, con la autorización abierta y activada (numeración sin huecos) y acceso al portal. Elena es
    -- su contacto prioritario.
    DECLARE @RosaId UNIQUEIDENTIFIER = '1A000000-0000-4000-8000-000000000001';
    DECLARE @Family TABLE (id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), vinculo UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), juego NVARCHAR(10),
                           nombre NVARCHAR(200), telefono VARCHAR(20), relacion NVARCHAR(40));
    INSERT INTO @Family (juego, nombre, telefono, relacion)
    VALUES ('CJ', N'Elena Sánchez (ficticia)', '600000002', N'Hija'), ('ORLANDO', N'Pablo Sánchez (ficticio)', '600000003', N'Hijo');

    INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, creado_por_cuenta_id, creado_en, cuenta_id)
    SELECT family.id, @CenterId, family.nombre, family.telefono, @AdminId, @Now, account.id
      FROM @Family family JOIN @Accounts account ON account.juego = family.juego AND account.rol = 'familiar';

    INSERT INTO dbo.residentes_familiares (id, centro_id, residente_id, familiar_id, relacion, vinculado_por_cuenta_id, vinculado_en)
    SELECT vinculo, @CenterId, @RosaId, id, relacion, @AdminId, @Now FROM @Family;

    INSERT INTO dbo.residentes_contacto_urgente
        (id, centro_id, residente_id, numero, vinculo_id, accion_codigo, designado_por_cuenta_id, designado_por_perfil, designado_en)
    SELECT NEWID(), @CenterId, @RosaId, 1, vinculo, 'REEMPLAZAR', @AdminId, 'ADMINISTRACION', @Now FROM @Family WHERE juego = 'CJ';

    INSERT INTO dbo.familiares_autorizaciones_cambios
        (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
    SELECT NEWID(), @CenterId, family.vinculo, change.numero, change.estado, NULL, NULL, @AdminId, 'ADMINISTRACION', DATEADD(SECOND, change.numero - 1, @Now)
      FROM @Family family CROSS JOIN (VALUES (1, 'PENDIENTE'), (2, 'ACTIVA')) AS change(numero, estado);

    COMMIT TRANSACTION;
END
GO

-- Foto del residente (0071): las Enfermerías de los centros de prueba tienen el permiso de la foto, concedido por la Administración de CJ de su
-- centro, para que sigan valiendo la guía 1 (prueba 13) y preparar-almendros.js. Va fuera del bloque de arriba porque esas cuentas ya existen
-- en las bases creadas antes de 0071. Solo se concede una vez: si después se revoca a mano, no se vuelve a dar.
INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
SELECT NEWID(), profile.id, profile.centro_id, 'RESIDENT_PHOTO_MANAGE', SYSUTCDATETIME(), grantor.cuenta_id
  FROM dbo.ambitos_perfil profile
  JOIN dbo.cuentas account ON account.id = profile.cuenta_id
 CROSS APPLY (SELECT TOP (1) admin_scope.cuenta_id
                FROM dbo.ambitos_perfil admin_scope
                JOIN dbo.cuentas admin_account ON admin_account.id = admin_scope.cuenta_id
               WHERE admin_scope.centro_id = profile.centro_id AND admin_scope.perfil_codigo = 'ADMINISTRACION'
                 AND admin_account.sujeto_externo IN ('almendros-administracion', 'olmos-administracion')) AS grantor
 WHERE profile.perfil_codigo = 'ENFERMERIA' AND profile.estado = 'ACTIVE'
   AND (account.sujeto_externo LIKE 'almendros-%' OR account.sujeto_externo LIKE 'olmos-%' OR account.sujeto_externo LIKE 'varios-centros%')
   AND NOT EXISTS (SELECT 1 FROM dbo.permisos_perfil existing
                    WHERE existing.ambito_perfil_id = profile.id AND existing.permiso_codigo = 'RESIDENT_PHOTO_MANAGE');
GO

-- Revisión de calidad (0076): las Direcciones de los centros de prueba tienen el permiso QUALITY_REVIEW, concedido por la Administración de CJ de su
-- centro, para que sigan valiendo las guías de CJ con Indicadores y Calidad de proceso. Mismo criterio que la foto: se da una vez y, si después se
-- revoca a mano, no se vuelve a dar.
INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
SELECT NEWID(), profile.id, profile.centro_id, 'QUALITY_REVIEW', SYSUTCDATETIME(), grantor.cuenta_id
  FROM dbo.ambitos_perfil profile
  JOIN dbo.cuentas account ON account.id = profile.cuenta_id
 CROSS APPLY (SELECT TOP (1) admin_scope.cuenta_id
                FROM dbo.ambitos_perfil admin_scope
                JOIN dbo.cuentas admin_account ON admin_account.id = admin_scope.cuenta_id
               WHERE admin_scope.centro_id = profile.centro_id AND admin_scope.perfil_codigo = 'ADMINISTRACION'
                 AND admin_account.sujeto_externo IN ('almendros-administracion', 'olmos-administracion')) AS grantor
 WHERE profile.perfil_codigo = 'DIRECCION_CLINICA' AND profile.estado = 'ACTIVE'
   AND (account.sujeto_externo LIKE 'almendros-%' OR account.sujeto_externo LIKE 'olmos-%' OR account.sujeto_externo LIKE 'varios-centros%')
   AND NOT EXISTS (SELECT 1 FROM dbo.permisos_perfil existing
                    WHERE existing.ambito_perfil_id = profile.id AND existing.permiso_codigo = 'QUALITY_REVIEW');
GO

SELECT account.sujeto_externo AS Cuenta, center.nombre_visible AS Centro, profile.perfil_codigo AS Perfil
  FROM dbo.cuentas account
  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
  JOIN dbo.centros center ON center.id = profile.centro_id
 WHERE account.sujeto_externo LIKE 'almendros-%' OR account.sujeto_externo LIKE 'olmos-%'
    OR account.sujeto_externo LIKE 'plataforma-%' OR account.sujeto_externo LIKE 'varios-centros%'
 ORDER BY account.sujeto_externo, center.nombre_visible, profile.perfil_codigo;
