/*
 * Usuario de la tarea programada del aviso de cambio de cita para Azure SQL Database (ADR 0012 y ADR 0008; docs/privacidad/tarea-correo-identidad-rls.md): un usuario
 * contenido en la propia base de datos, con contraseña, que solo ocupa el rol residapp_tareas (script de esquema 0083: permisos mínimos por columnas, sin leer el motivo
 * de los cambios de cita, y la excepción del filtro de centro solo para ese rol). No es un script de esquema: no lo ejecuta aplicar-scripts.sh. Se ejecuta UNA vez,
 * después de que el CI haya aplicado el 0083, conectado a la base de datos de la aplicación (no a master) con el administrador:
 *   sqlcmd -S <servidor>.database.windows.net -d <base> -U <admin> -b -v CONTRASENA="<contraseña larga y distinta a la de residapp_app, sin comillas simples>" ^
 *          -i database/seguridad/crear_usuario_tarea_azure.sql
 * o pegándolo en el Editor de consultas del portal, sustituyendo $(CONTRASENA). La contraseña no se escribe nunca en el repositorio.
 * Después, en la Web App: el ajuste de cadena de conexión «ResidAppTareas» (con este usuario y esa contraseña, mismo servidor y base que «ResidApp»). Solo la usa la tarea
 * programada; el resto de la aplicación sigue con residapp_app. Para rotar la contraseña: ALTER USER residapp_tarea WITH PASSWORD = N'...'; y cambiar el ajuste.
 * Comprobación: con ese usuario, IS_MEMBER('residapp_tareas') da 1, IS_MEMBER('db_owner') da 0, SELECT COUNT(*) FROM dbo.envios_correo ve las filas de todos los centros
 * y SELECT motivo FROM dbo.citas_cambios da «permiso denegado».
 */

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NULL
    THROW 50840, 'ROLE_RESIDAPP_TAREAS_MISSING: aplica antes el script 0083', 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'residapp_tarea')
    CREATE USER residapp_tarea WITH PASSWORD = N'$(CONTRASENA)';
GO

ALTER ROLE residapp_tareas ADD MEMBER residapp_tarea;
GO
