/*
 * Comprobación de la seguridad por filas sobre datos reales (ADR 0008). Para cada tabla bajo seg.pol_centro: toma el centro con más
 * filas, entra con un ámbito activo de ese centro como usuario limitado (EXECUTE AS) y comprueba que ve exactamente las filas de ese
 * centro y ninguna de otro. Una tabla vacía, o un centro sin ámbito activo, se anota como «sin datos» y no falla.
 *
 * Se ejecuta DESPUÉS de las pruebas (el CI y las pruebas en contenedor), cuando las tablas ya tienen datos de varios centros:
 *   sqlcmd -S <servidor> -d <base> -U <admin> -C -b -W -i database/seguridad/comprobar_aislamiento.sql
 * Necesita un usuario de la base sin db_owner: residapp_app (crear_usuario_aplicacion.sql) o residapp_rls_test (lo crean las pruebas).
 * Termina con error si alguna tabla deja ver filas de otro centro o esconde las del propio.
 */

SET NOCOUNT ON;

DECLARE @usuario sysname = CASE
    WHEN DATABASE_PRINCIPAL_ID(N'residapp_app') IS NOT NULL THEN N'residapp_app'
    WHEN DATABASE_PRINCIPAL_ID(N'residapp_rls_test') IS NOT NULL THEN N'residapp_rls_test'
    END;
IF @usuario IS NULL THROW 50900, 'Falta un usuario sin db_owner (residapp_app o residapp_rls_test).', 1;

CREATE TABLE #resultado (
    tabla sysname NOT NULL, estado NVARCHAR(12) NOT NULL, filas_centro INT NULL, vistas INT NULL, vistas_ajenas INT NULL, detalle NVARCHAR(120) NULL);

DECLARE @tabla sysname, @sql NVARCHAR(MAX), @centro UNIQUEIDENTIFIER, @filas INT, @ambito UNIQUEIDENTIFIER, @sujeto NVARCHAR(200),
        @vistas INT, @ajenas INT;

DECLARE tablas CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT OBJECT_NAME(target_object_id) FROM sys.security_predicates ORDER BY 1;
OPEN tablas;
FETCH NEXT FROM tablas INTO @tabla;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @centro = NULL, @filas = NULL, @ambito = NULL, @sujeto = NULL, @vistas = NULL, @ajenas = NULL;

    SET @sql = N'SELECT TOP (1) @c = centro_id, @n = COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N' GROUP BY centro_id ORDER BY COUNT(*) DESC';
    EXEC sys.sp_executesql @sql, N'@c UNIQUEIDENTIFIER OUTPUT, @n INT OUTPUT', @c = @centro OUTPUT, @n = @filas OUTPUT;

    IF @centro IS NULL
    BEGIN
        INSERT #resultado VALUES (@tabla, N'sin datos', NULL, NULL, NULL, N'la tabla está vacía');
    END
    ELSE
    BEGIN
        SELECT TOP (1) @ambito = a.id, @sujeto = c.sujeto_externo
          FROM dbo.ambitos_perfil a JOIN dbo.cuentas c ON c.id = a.cuenta_id
         WHERE a.centro_id = @centro AND a.estado = 'ACTIVE' AND a.revocado_en IS NULL AND c.estado = 'ACTIVE';

        IF @ambito IS NULL
            INSERT #resultado VALUES (@tabla, N'sin datos', @filas, NULL, NULL, N'el centro con más filas no tiene ámbito activo');
        ELSE
        BEGIN
            EXEC sys.sp_set_session_context @key = N'sujeto_externo', @value = @sujeto;
            EXEC sys.sp_set_session_context @key = N'ambito_perfil_id', @value = @ambito;

            SET @sql = N'EXECUTE AS USER = N''' + @usuario + N''';
                SELECT @v = COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N';
                SELECT @a = COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N' WHERE centro_id <> @c;
                REVERT;';
            EXEC sys.sp_executesql @sql, N'@v INT OUTPUT, @a INT OUTPUT, @c UNIQUEIDENTIFIER',
                @v = @vistas OUTPUT, @a = @ajenas OUTPUT, @c = @centro;

            INSERT #resultado VALUES (@tabla,
                CASE WHEN @ajenas = 0 AND @vistas = @filas THEN N'OK' ELSE N'FALLO' END, @filas, @vistas, @ajenas,
                CASE WHEN @ajenas <> 0 THEN N'se ven filas de otro centro'
                     WHEN @vistas <> @filas THEN N'no se ven todas las filas del propio centro' END);
        END
    END
    FETCH NEXT FROM tablas INTO @tabla;
END
CLOSE tablas;
DEALLOCATE tablas;

SELECT tabla, estado, filas_centro, vistas, vistas_ajenas, detalle FROM #resultado ORDER BY estado DESC, tabla;
SELECT SUM(CASE WHEN estado = N'OK' THEN 1 ELSE 0 END) AS comprobadas_ok,
       SUM(CASE WHEN estado = N'sin datos' THEN 1 ELSE 0 END) AS sin_datos,
       SUM(CASE WHEN estado = N'FALLO' THEN 1 ELSE 0 END) AS fallos FROM #resultado;

IF EXISTS (SELECT 1 FROM #resultado WHERE estado = N'FALLO') THROW 50901, 'La seguridad por filas deja ver o esconde filas.', 1;
IF NOT EXISTS (SELECT 1 FROM #resultado WHERE estado = N'OK') THROW 50902, 'Ninguna tabla tenía datos que comprobar: el resultado no prueba nada.', 1;
