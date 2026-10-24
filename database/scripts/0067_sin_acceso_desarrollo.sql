/*
 * Fuera el acceso de desarrollo (ADR 0007): ya no se entra eligiendo una cuenta sin contraseña ni se crean invitaciones desde él.
 *
 *  - eventos_acceso deja de admitir ENTRADA_DESARROLLO y CREDENCIALES_DESARROLLO.
 *  - invitaciones_acceso cambia el emisor DESARROLLO por ARRANQUE: la invitación que emite quien administra la base de datos con
 *    database/operaciones/invitar_cuenta.sql, para la primera cuenta de Plataforma (nadie dentro de la aplicación puede invitarla) o para
 *    recuperarla si pierde sus credenciales.
 *
 * Falla si quedan filas con los valores retirados: las bases de desarrollo se recrearon desde cero el 2026-10-24.
 */

ALTER TABLE dbo.eventos_acceso DROP CONSTRAINT CK_eventos_acceso_tipo;
ALTER TABLE dbo.eventos_acceso ADD CONSTRAINT CK_eventos_acceso_tipo CHECK (tipo IN (
    'ENTRADA', 'CONTRASENA_FALLIDA', 'BLOQUEO', 'SEGUNDO_FACTOR_FALLIDO', 'SEGUNDO_FACTOR_ALTA',
    'CODIGO_RECUPERACION_USADO', 'INVITACION_USADA', 'RECUPERACION_USADA', 'INVITACION_NO_VALIDA',
    'CONTRASENA_CAMBIADA', 'CODIGOS_REGENERADOS', 'SESION_CERRADA', 'VINCULACION_ACEPTADA',
    'ENTRADA_PASSKEY', 'PASSKEY_ALTA', 'PASSKEY_BAJA', 'PASSKEY_FALLIDA'));
GO

ALTER TABLE dbo.invitaciones_acceso DROP CONSTRAINT CK_invitaciones_acceso_emisor;
ALTER TABLE dbo.invitaciones_acceso ADD CONSTRAINT CK_invitaciones_acceso_emisor
    CHECK (perfil_emisor IN ('ADMINISTRACION', 'PLATAFORMA', 'ARRANQUE'));
GO
