/*
 * Efectos del cambio de correo de un familiar cuando entra en el portal con un código que le llega a él (ADR 0012, pieza 3; lectura de privacidad, borrador por validar, en
 * docs/privacidad/codigo-acceso-familiar-por-correo.md, apartado «PR 2: Administración y última entrada»). Sin tablas ni columnas nuevas:
 *  - dbo.sesiones admite el motivo de cierre CAMBIO_CORREO: Administración cambió o borró el correo y se cierran las sesiones de esa cuenta.
 *  - dbo.envios_correo admite la plantilla AVISO_CAMBIO_CORREO (aviso a la dirección anterior) y el motivo CORREO_APAGADO. La fila se escribe ya resuelta, después del
 *    intento, así que nunca queda PENDIENTE (ninguna tarea la cerraría). referencia_id es el familiar y referencia_clave un identificador del cambio. No guarda ninguna
 *    dirección, ni la anterior ni la nueva; la purga a los 12 meses del 0085 la alcanza como a las demás.
 * No es idempotente: lo aplica una sola vez database/aplicar-scripts.sh.
 */

ALTER TABLE dbo.sesiones DROP CONSTRAINT CK_sesiones_cierre;
GO
ALTER TABLE dbo.sesiones ADD CONSTRAINT CK_sesiones_cierre CHECK (
    (cerrada_en IS NULL AND motivo_cierre IS NULL)
    OR (cerrada_en >= creada_en AND motivo_cierre IN (
        'LOGOUT', 'INACTIVIDAD', 'CADUCIDAD', 'CAMBIO_IDENTIDAD', 'RECUPERACION', 'CIERRE_PROPIO', 'CAMBIO_CORREO')));
GO

ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_plantilla;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_plantilla CHECK (plantilla_codigo IN
    ('AVISO_PROPUESTA_CITA', 'AVISO_CAMBIO_CITA', 'RECORDATORIO_CITA', 'AVISO_CITA_EXTERNA', 'AVISO_CITA_EXTERNA_SIN_CUBRIR', 'AVISO_CAMBIO_CORREO'));
GO
ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_motivo;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
    ('SIN_CORREO', 'SIN_ACCESO', 'DIRECCION_NO_CONFIRMADA', 'PROPUESTA_NO_VIGENTE', 'CITA_NO_VIGENTE', 'CITA_PASADA', 'CITA_RECIENTE', 'FUERA_DE_PLAZO', 'DESTINO_NO_PERMITIDO',
     'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO', 'RESULTADO_DESCONOCIDO', 'FAMILIA_NO_VE', 'AVISOS_DESACTIVADOS', 'BAJA_DE_AVISOS', 'YA_CUBIERTA', 'NO_PUEDE',
     'CORREO_APAGADO'));
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_cambio_correo CHECK (plantilla_codigo <> 'AVISO_CAMBIO_CORREO' OR estado_codigo <> 'PENDIENTE');
GO
