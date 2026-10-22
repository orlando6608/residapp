/*
 * «Mi cuenta» (ADR 0007, incremento I3): cambiar la propia contraseña, generar códigos de recuperación nuevos y cerrar las propias
 * sesiones abiertas.
 *
 *  - eventos_acceso admite tres tipos nuevos: CONTRASENA_CAMBIADA, CODIGOS_REGENERADOS y SESION_CERRADA (una sesión cerrada por su
 *    propietario desde otra).
 *  - sesiones admite el motivo de cierre CIERRE_PROPIO (la persona cierra sus otras sesiones o las cierra al cambiar la contraseña).
 *  - No hay tablas nuevas: la lista de sesiones sale de dbo.sesiones y la IP de la entrada, de dbo.eventos_acceso.
 */

ALTER TABLE dbo.eventos_acceso DROP CONSTRAINT CK_eventos_acceso_tipo;
ALTER TABLE dbo.eventos_acceso ADD CONSTRAINT CK_eventos_acceso_tipo CHECK (tipo IN (
    'ENTRADA', 'ENTRADA_DESARROLLO', 'CONTRASENA_FALLIDA', 'BLOQUEO', 'SEGUNDO_FACTOR_FALLIDO', 'SEGUNDO_FACTOR_ALTA',
    'CODIGO_RECUPERACION_USADO', 'CREDENCIALES_DESARROLLO', 'INVITACION_USADA', 'RECUPERACION_USADA', 'INVITACION_NO_VALIDA',
    'CONTRASENA_CAMBIADA', 'CODIGOS_REGENERADOS', 'SESION_CERRADA'));
GO

ALTER TABLE dbo.sesiones DROP CONSTRAINT CK_sesiones_cierre;
ALTER TABLE dbo.sesiones ADD CONSTRAINT CK_sesiones_cierre CHECK (
    (cerrada_en IS NULL AND motivo_cierre IS NULL)
    OR (cerrada_en >= creada_en AND motivo_cierre IN (
        'LOGOUT', 'INACTIVIDAD', 'CADUCIDAD', 'CAMBIO_IDENTIDAD', 'RECUPERACION', 'CIERRE_PROPIO')));
GO
