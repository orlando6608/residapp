/*
 * M1 de la revisión de seguridad, segunda parte: las cuatro tablas «Provisión» (unidades, ambitos_perfil_unidad, administraciones_principales_cambios y logos_centro) pasan a la política
 * seg.pol_centro (ADR 0008). Son estructura de la organización, sin datos de residentes ni de familiares; hasta hoy solo las protegía el WHERE centro_id de cada consulta.
 * Lectura de dpo-residapp del 2026-11-04 (salto estrecho por tabla, decidido por Orlando).
 *
 * Plataforma (el personal de ResidApp) opera ENTRE centros al dar de alta un centro y al fijar su Administración principal, así que no se puede filtrar todo por el centro del ámbito.
 * El salto es el mínimo que hace el código hoy, por tabla y por operación (el ámbito de Plataforma ya solo puede vivir en el centro reservado: lo exige un disparador):
 *
 *   unidades ........................ Plataforma LEE e INSERTA en cualquier centro (cuenta sus unidades, lista sus nombres, crea la primera).
 *   ambitos_perfil_unidad ........... Plataforma LEE e INSERTA en cualquier centro; la fila ha de llevar como autora (concedido_por_cuenta_id) la cuenta del ámbito activo.
 *   administraciones_principales_cambios  Plataforma LEE (necesita el último número para numerar y para saber quién es principal) e INSERTA en cualquier centro; la fila lleva la cuenta
 *                                    y el perfil del ámbito activo. Ya es inmutable por disparador (TR_apc_immutable).
 *   logos_centro .................... Plataforma SOLO INSERTA (al dar de alta un centro); no lee ni cambia el logo de un centro ya creado. La lectura y los cambios son del centro del ámbito
 *                                    (la Administración de ese centro).
 *
 * Nadie salta UPDATE ni DELETE: los BLOCK AFTER UPDATE impiden mover o cambiar una fila ajena, y ver no es borrar porque las tres tablas que Plataforma puede VER de otros centros
 * ya prohíben el DELETE con disparadores INSTEAD OF (TR_units_no_delete, TR_pus_no_delete, TR_apc_immutable); logos_centro solo se ve en el centro propio. El filtro es siempre por centro, nunca por unidad: las comprobaciones de «al menos una Administración principal» y la numeración de cambios necesitan ver
 * todo el centro. Las funciones repiten el cuerpo (sin llamarse entre sí) para no enlazar con SCHEMABINDING, como hace 0088, y ninguna sirve de «salto genérico» para tablas futuras.
 *
 * Insertar en unidades o en ambitos_perfil_unidad exige además que el ámbito activo sea de ADMINISTRACION o de PLATAFORMA (decidido por Orlando, 2026-11-04, tras la revisión de calidad):
 * solo esos dos perfiles escriben ahí; una sesión de otro perfil (un auxiliar, un familiar) no puede darse una unidad. Leer las unidades sí lo hace cualquier perfil del centro.
 *
 * La hoja de acceso a otro centro (vinculaciones) la ve una persona que NO tiene ámbito en ese centro, y su resumen enseña los nombres de las unidades que se le ofrecen: con el filtro de
 * unidades esa lectura saldría vacía. dbo.nombres_unidades_vinculacion lo resuelve con la menor apertura posible: dado el resumen del código de una hoja VIVA (sin anular, sin canjear,
 * sin caducar) devuelve solo los nombres de sus unidades, ejecutándose como el propietario. Conocer el código de la hoja es lo que da derecho a leerlos. El permiso va a public
 * (no se puede conceder a los roles fijos, y este script se aplica antes de crear el usuario de la aplicación en CI): es inocuo, porque sin el código de una hoja viva no devuelve nada.
 *
 * Mantenimiento: las columnas que usan los predicados (centro_id, autor y perfil de cada tabla) quedan enlazadas al esquema; para cambiarlas hay que quitar antes la política, como con 0030.
 */

CREATE FUNCTION seg.fn_centro_o_plataforma(@centro_id UNIQUEIDENTIFIER)
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
               AND (ambito.centro_id = @centro_id OR ambito.perfil_codigo = N'PLATAFORMA'));
GO

CREATE FUNCTION seg.fn_unidad_escribe(@centro_id UNIQUEIDENTIFIER)
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
               AND ambito.perfil_codigo IN (N'ADMINISTRACION', N'PLATAFORMA')
               AND (ambito.centro_id = @centro_id OR ambito.perfil_codigo = N'PLATAFORMA'));
GO

CREATE FUNCTION seg.fn_asignacion_escribe(@centro_id UNIQUEIDENTIFIER, @cuenta_id UNIQUEIDENTIFIER)
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
               AND ambito.perfil_codigo IN (N'ADMINISTRACION', N'PLATAFORMA')
               AND (ambito.centro_id = @centro_id OR ambito.perfil_codigo = N'PLATAFORMA'));
GO

CREATE FUNCTION seg.fn_estructura_escribe(@centro_id UNIQUEIDENTIFIER, @cuenta_id UNIQUEIDENTIFIER, @perfil_autor NVARCHAR(32))
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
               AND ambito.perfil_codigo = @perfil_autor
               AND (ambito.centro_id = @centro_id OR ambito.perfil_codigo = N'PLATAFORMA'));
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_o_plataforma(centro_id) ON dbo.unidades,
    ADD BLOCK PREDICATE seg.fn_unidad_escribe(centro_id) ON dbo.unidades AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.unidades AFTER UPDATE,

    ADD FILTER PREDICATE seg.fn_centro_o_plataforma(centro_id) ON dbo.ambitos_perfil_unidad,
    ADD BLOCK PREDICATE seg.fn_asignacion_escribe(centro_id, concedido_por_cuenta_id) ON dbo.ambitos_perfil_unidad AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ambitos_perfil_unidad AFTER UPDATE,

    ADD FILTER PREDICATE seg.fn_centro_o_plataforma(centro_id) ON dbo.administraciones_principales_cambios,
    ADD BLOCK PREDICATE seg.fn_estructura_escribe(centro_id, cambiado_por_cuenta_id, cambiado_por_perfil) ON dbo.administraciones_principales_cambios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.administraciones_principales_cambios AFTER UPDATE,

    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.logos_centro,
    ADD BLOCK PREDICATE seg.fn_estructura_escribe(centro_id, actualizado_por_cuenta_id, actualizado_por_perfil) ON dbo.logos_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.logos_centro AFTER UPDATE;
GO

CREATE PROCEDURE dbo.nombres_unidades_vinculacion
    @resumen_token NVARCHAR(64),
    @ahora DATETIME2(3)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT unit.nombre_visible AS nombre
      FROM dbo.vinculaciones_cuenta link
      JOIN dbo.vinculaciones_cuenta_unidades link_unit ON link_unit.vinculacion_id = link.id
      JOIN dbo.unidades unit ON unit.centro_id = link_unit.centro_id AND unit.id = link_unit.unidad_id
     WHERE link.resumen_token = @resumen_token
       AND link.anulada_en IS NULL AND link.canjeada_en IS NULL AND link.caduca_en > @ahora
     ORDER BY unit.nombre_visible;
END;
GO

GRANT EXECUTE ON OBJECT::dbo.nombres_unidades_vinculacion TO public;
GO
