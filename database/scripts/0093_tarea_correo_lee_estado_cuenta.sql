/*
 * La tarea programada de los avisos por correo comprueba también que la CUENTA del familiar siga activa (hallazgo de dpo-residapp en docs/privacidad/recordatorio-cita-por-correo.md,
 * apartado 3; Orlando, 2026-11-14).
 *
 * La consulta de acceso del portal (SqlFamilyPortalReader.AccessibleLinks) mira el vínculo, la autorización, el residente y su suspensión, pero no cuentas.estado: lo exige la sesión
 * (la seguridad por filas, 0030, pide cuenta activa) y no la consulta. Los avisos por correo sí la usan sin sesión, así que una cuenta de familia suspendida (Plataforma suspende las
 * cuentas con perfiles en varios centros; la anonimización de un familiar también suspende la cuenta) con la autorización aún Activa seguiría recibiendo los tres avisos de algo
 * que ya no puede ver.
 *
 * El rol de la tarea (residapp_tareas, 0083) gana solo SELECT de dos columnas de dbo.cuentas: el identificador y el estado. Sigue sin poder leer el identificador de acceso
 * (sujeto_externo), el nombre ni nada más de la cuenta, ni dbo.ambitos_perfil. dbo.cuentas no tiene centro_id: no está bajo la seguridad por filas, así que no hay nada más que tocar.
 */

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT SELECT (id, estado) ON dbo.cuentas TO residapp_tareas;
GO
