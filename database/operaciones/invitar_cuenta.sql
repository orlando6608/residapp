/*
 * Invitación de arranque (ADR 0007): la que emite quien administra la base de datos, fuera de la aplicación. Sirve para la primera cuenta
 * de Plataforma de un entorno (dentro de la aplicación nadie puede invitarla) y para recuperarla si pierde sus credenciales. El resto de
 * cuentas se invitan desde la aplicación: Plataforma a cada Administración y Administración a su personal y a los familiares.
 *
 * No es un script de esquema: no lo ejecuta aplicar-scripts.sh. Se ejecuta a mano, con el administrador de la base:
 *   sqlcmd -S <servidor> -d <base> -U <admin> -C -b -I -v CUENTA="plataforma-orlando" -i database/operaciones/invitar_cuenta.sql
 * Imprime la ruta del enlace de un solo uso, que vale 72 horas: ponle delante la dirección de la aplicación (por ejemplo
 * https://app-residapp-dev.azurewebsites.net), ábrelo y elige la contraseña (y, si es del personal, el código del móvil). No se pasa la
 * dirección como variable porque el sqlcmd de Windows rechaza «:» y «/» en los valores de -v. Igual que
 * las de la aplicación, es de RECUPERACION si la cuenta ya tiene contraseña (al usarla se sustituyen sus credenciales y se cierran sus
 * sesiones) y anula la invitación anterior sin usar. Aquí solo se guarda el resumen SHA-256 del token, como AccessInvitation.Hash.
 * El enlace no se guarda en ningún sitio: entrégalo en privado a la persona de la cuenta.
 */

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Subject NVARCHAR(200) = N'$(CUENTA)';
DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
DECLARE @AccountId UNIQUEIDENTIFIER, @CenterId UNIQUEIDENTIFIER;

SELECT @AccountId = account.id FROM dbo.cuentas account WHERE account.sujeto_externo = @Subject AND account.estado = 'ACTIVE';
-- El centro de la invitación es el de su ámbito activo más antiguo (Plataforma tiene el suyo, 0026).
SELECT TOP (1) @CenterId = scope.centro_id FROM dbo.ambitos_perfil scope
 WHERE scope.cuenta_id = @AccountId AND scope.estado = 'ACTIVE' ORDER BY scope.concedido_en;
IF @AccountId IS NULL OR @CenterId IS NULL
    THROW 50000, 'No hay ninguna cuenta activa con ese usuario y algún ámbito activo.', 1;

-- 32 bytes aleatorios en base64url sin relleno, como AccessInvitation.NewToken.
DECLARE @Bytes VARBINARY(32) = CRYPT_GEN_RANDOM(32);
DECLARE @Token VARCHAR(64) = REPLACE(REPLACE(REPLACE(
    CAST(N'' AS XML).value('xs:base64Binary(sql:variable("@Bytes"))', 'VARCHAR(64)'), '+', '-'), '/', '_'), '=', '');
DECLARE @TokenHash CHAR(64) = CONVERT(CHAR(64), HASHBYTES('SHA2_256', @Token), 2);
DECLARE @Kind NVARCHAR(16) = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.credenciales credential WHERE credential.cuenta_id = @AccountId AND credential.hash_contrasena IS NOT NULL)
    THEN N'RECUPERACION' ELSE N'ALTA' END;

BEGIN TRANSACTION;
UPDATE dbo.invitaciones_acceso SET anulada_en = @Now WHERE cuenta_id = @AccountId AND usada_en IS NULL AND anulada_en IS NULL;
INSERT INTO dbo.invitaciones_acceso
    (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor, caduca_en)
VALUES (NEWID(), @AccountId, @CenterId, @Kind, @TokenHash, @Now, @AccountId, 'ARRANQUE', DATEADD(HOUR, 72, @Now));
COMMIT;

PRINT CONCAT(N'Invitación de ', CASE @Kind WHEN N'ALTA' THEN N'alta' ELSE N'recuperación' END, N' para ', @Subject,
    N' (caduca a las 72 horas; pon delante la dirección de la aplicación):');
PRINT CONCAT(N'/Acceso/Invitacion/', @Token);
