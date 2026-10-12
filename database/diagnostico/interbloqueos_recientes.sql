/*
 * Diagnóstico: interbloqueos (error 1205) que SQL Server tiene guardados en la sesión system_health, uno por proceso implicado, con el proceso que
 * se eligió como víctima, el recurso que esperaba y el principio de su sentencia. Solo lee. Lo usa el workflow de CI cuando fallan las pruebas
 * (la aplicación devuelve cualquier excepción como «No disponible» y el motivo no queda en el log) y sirve igual contra la BD local:
 *
 *   sqlcmd -S <servidor> -C -E -d master -I -W -s "|" -i database/diagnostico/interbloqueos_recientes.sql
 *
 * Funciona con rutas de Windows y de Linux. Necesita el permiso VIEW SERVER STATE (sa lo tiene).
 */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @log NVARCHAR(400) = CAST(SERVERPROPERTY('ErrorLogFileName') AS NVARCHAR(400));
DECLARE @separador NCHAR(1) = IIF(@log LIKE N'%/%', N'/', N'\');
DECLARE @ruta NVARCHAR(400) = LEFT(@log, LEN(@log) - CHARINDEX(@separador, REVERSE(@log))) + @separador + N'system_health*.xel';

WITH grafos AS (
    SELECT CAST(event_data AS XML) AS evento
      FROM sys.fn_xe_file_target_read_file(@ruta, NULL, NULL, NULL)
     WHERE object_name = N'xml_deadlock_report'
)
SELECT TOP (40)
       CONVERT(NVARCHAR(23), g.evento.value('(event/@timestamp)[1]', 'datetime2'), 126) AS utc,
       IIF(p.proceso.value('@id', 'NVARCHAR(60)') = g.evento.value('(event/data/value/deadlock/victim-list/victimProcess/@id)[1]', 'NVARCHAR(60)'),
           N'VICTIMA', N'') AS victima,
       p.proceso.value('@waitresource', 'NVARCHAR(100)') AS espera,
       LEFT(REPLACE(REPLACE(p.proceso.value('(inputbuf)[1]', 'NVARCHAR(MAX)'), CHAR(13), N' '), CHAR(10), N' '), 300) AS sentencia
  FROM grafos g
 CROSS APPLY g.evento.nodes('event/data/value/deadlock/process-list/process') AS p(proceso)
 ORDER BY 1 DESC, 2 DESC;
