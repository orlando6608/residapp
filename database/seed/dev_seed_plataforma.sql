/*
 * Datos semilla de desarrollo del perfil de plataforma — exclusivamente ficticios. NO ejecutar contra un entorno con datos
 * reales. Idempotente: si la cuenta 'dev-plataforma' ya existe, no inserta nada. El centro reservado «Plataforma» lo crea el
 * script 0026, que va antes que los seeds.
 */

IF NOT EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-plataforma')
BEGIN
    DECLARE @AccountId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en)
    VALUES (@AccountId, 'dev-plataforma', N'Operador de plataforma (desarrollo)', 'ACTIVE', @Now);

    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
    VALUES (NEWID(), @AccountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', @Now, @AccountId);
END;
