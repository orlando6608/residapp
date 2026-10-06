/*
 * Seguridad por filas por centro (ADR 0008, incremento G1), primera tanda: residentes, eventos asistenciales y familiares.
 * Segunda barrera tras el WHERE centro_id de cada consulta y las claves compuestas: si una consulta lo olvida, el motor no
 * devuelve ni acepta filas de otro centro.
 *
 * La aplicación fija en SESSION_CONTEXT, al abrir cada conexión (SqlConnectionFactory), el sujeto verificado por el proveedor de
 * identidad y el id del ámbito activo (de la cookie, que no es de fiar). El predicado no confía en ese id: comprueba que el ámbito
 * pertenece a esa cuenta, que ambos están activos y que el ámbito es del centro de la fila. Una cookie manipulada no sale de los
 * ámbitos de la propia cuenta, y solo se ve el centro del ámbito activo.
 *
 * Sin sujeto o sin ámbito en la sesión no se ve ninguna fila. No hay salto para PLATAFORMA en estas tablas: no ve contenido
 * clínico (ADR 0006).
 *
 * Los predicados de RLS se aplican a todos los usuarios, también a dbo y a sysadmin, así que la función deja pasar a los miembros
 * de db_owner (scripts, seeds, tests y, hoy, el usuario de la aplicación en Azure). La política queda latente hasta que la
 * aplicación se conecte con un usuario que no sea db_owner (db_datareader y db_datawriter bastan; decisión de despliegue aparte,
 * ADR 0008). Un db_owner puede en todo caso desactivar la política: esto protege de errores de la aplicación, no de quien
 * administra la base. Las pruebas (RlsCentroTests) la ejercen suplantando un usuario sin login con EXECUTE AS.
 * Las tablas cuentas y ambitos_perfil quedan fuera de la política: el login las lee antes de que haya ámbito.
 *
 * La función está enlazada al esquema (SCHEMABINDING, obligatorio): un script futuro que cambie o elimine las columnas
 * id, cuenta_id, centro_id, estado o revocado_en de ambitos_perfil, o id, sujeto_externo o estado de cuentas, tendrá que
 * quitar antes la política (DROP SECURITY POLICY seg.pol_centro) y volver a crearla.
 */

CREATE SCHEMA seg;
GO

CREATE FUNCTION seg.fn_centro_del_ambito(@centro_id UNIQUEIDENTIFIER)
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
               AND ambito.centro_id = @centro_id
               AND ambito.estado = 'ACTIVE' AND ambito.revocado_en IS NULL
               AND cuenta.estado = 'ACTIVE');
GO

CREATE SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.residentes AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.eventos_asistenciales,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.eventos_asistenciales AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.eventos_asistenciales AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.familiares,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.familiares AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.familiares AFTER UPDATE
    WITH (STATE = ON);
GO
