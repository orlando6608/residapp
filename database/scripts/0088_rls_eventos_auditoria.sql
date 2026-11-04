/*
 * M1 de la revisión de seguridad: eventos_auditoria pasa a la política seg.pol_centro (ADR 0008). Era la tabla «Provisión» de más peso:
 * guarda quién hizo qué sobre qué recurso en todos los centros, y solo la protegían el WHERE centro_id de cada consulta y su clave compuesta.
 *
 *   Lectura ........ FILTER con seg.fn_centro_del_ambito: se ven las filas del centro del ámbito activo, y solo ellas. Plataforma tampoco lee
 *                    los eventos de otros centros (ni los suyos de otro centro): su código solo inserta en esta tabla, no la consulta.
 *   Inserción ...... BLOCK con seg.fn_auditoria_escribe. La fila ha de llevar como autora la cuenta del ámbito activo, y o bien el perfil de
 *                    ese ámbito y su centro, o bien (salto de Plataforma) perfil PLATAFORMA en cualquier centro si el ámbito activo es un ámbito
 *                    vigente de PLATAFORMA. Nadie escribe auditoría a nombre de otra cuenta ni de otro perfil. El salto existe para que
 *                    Plataforma deje constancia de sus altas de centro, invitaciones y suspensiones de cuentas en el centro afectado; sin él
 *                    esas operaciones fallarían. Quien lo usa deja constancia, pero no ve (el FILTER es el mismo para todos).
 *   Modificación ... BLOCK AFTER UPDATE como en el resto; el disparador TR_audit_immutable ya impide cualquier UPDATE o DELETE.
 *
 * La segunda función repite el cuerpo de la primera (en lugar de llamarla) para no enlazar ambas con SCHEMABINDING: así 0030 se puede
 * seguir cambiando sin tocar esta.
 */

CREATE FUNCTION seg.fn_auditoria_escribe(
    @centro_id UNIQUEIDENTIFIER, @cuenta_id UNIQUEIDENTIFIER, @perfil_activo NVARCHAR(32))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS permitido
     WHERE IS_MEMBER(N'db_owner') = 1
        OR EXISTS (
            SELECT 1
              FROM dbo.ambitos_perfil ambito
              JOIN dbo.cuentas cuenta ON cuenta.id = ambito.cuenta_id
             WHERE ambito.id = TRY_CONVERT(UNIQUEIDENTIFIER, SESSION_CONTEXT(N'ambito_perfil_id'))
               AND cuenta.sujeto_externo = CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'sujeto_externo'))
               AND ambito.estado = 'ACTIVE' AND ambito.revocado_en IS NULL
               AND cuenta.estado = 'ACTIVE'
               AND cuenta.id = @cuenta_id
               AND ambito.perfil_codigo = @perfil_activo
               AND (ambito.centro_id = @centro_id OR ambito.perfil_codigo = N'PLATAFORMA'));
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.eventos_auditoria,
    ADD BLOCK PREDICATE seg.fn_auditoria_escribe(centro_id, cuenta_id, perfil_activo) ON dbo.eventos_auditoria AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.eventos_auditoria AFTER UPDATE;
GO
