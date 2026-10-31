/*
 * Aviso por correo cuando el centro reprograma o cancela una cita confirmada (ADR 0012; lectura de privacidad en
 * docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md, 2.5). Comparte la tabla del aviso de propuesta (0081): una plantilla nueva, AVISO_CAMBIO_CITA, y dos motivos
 * de omisión nuevos. La clave única (plantilla, referencia, clave) sigue siendo lo que impide mandarlo dos veces:
 *
 *   referencia_id     la cita (no la solicitud).
 *   referencia_clave  el número del cambio en citas_cambios (reprogramación o cancelación): dos reprogramaciones, dos avisos; el mismo cambio dos veces, uno.
 *
 * No se guarda nada nuevo: ni la dirección, ni si fue cambio o cancelación, ni la hora, ni el motivo (se sabe uniendo con citas_cambios).
 *   CITA_NO_VIGENTE  el cambio ya no es el último de la cita, no lo hizo Administración, tiene causa de sistema (la baja) o la cita ya no está en el estado esperado.
 *   CITA_PASADA      la hora que importaba (la nueva en una reprogramación, la original en una cancelación) ya pasó.
 *
 * Los CHECK se rehacen aquí (no se edita 0081, que ya está aplicado). El disparador TR_envios_correo_guard y la seguridad por filas no cambian.
 */

ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_plantilla;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_plantilla CHECK (plantilla_codigo IN ('AVISO_PROPUESTA_CITA', 'AVISO_CAMBIO_CITA'));
GO
ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_motivo;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
    ('SIN_CORREO', 'SIN_ACCESO', 'PROPUESTA_NO_VIGENTE', 'CITA_NO_VIGENTE', 'CITA_PASADA', 'DESTINO_NO_PERMITIDO', 'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO'));
GO
