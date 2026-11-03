/*
 * Diagnóstico: estado de las 10 cuentas de CJ de las guías de pruebas 1 a 8 (juego sin «-orlando»). Solo lee. Para cada una dice si existe, si
 * tiene contraseña, si tiene el código del móvil activo, cuántas passkeys tiene, si le queda una invitación sin usar (con su caducidad) y la
 * última vez que alguien entró con ella. No muestra contraseñas, claves del móvil ni el contenido de las invitaciones.
 *
 * Con el administrador, en el Editor de consultas del portal de Azure (base sqldb-residapp-dev) pega el contenido y ejecuta: el editor del
 * portal no muestra los PRINT, por eso todo sale en un resultado. Por línea de comandos:
 *
 *   sqlcmd -S <servidor> -d <base> -I -W -s "|" -i database/diagnostico/cuentas_de_cj.sql
 *
 * Cómo leerlo (2026-11-03): «NO EXISTE» = no está aplicada la semilla; contraseña «No» = nadie se ha dado de alta; codigo_movil «No» solo es normal
 * en almendros-elena (la familia no usa segundo factor); ultima_entrada vacía = nadie ha entrado nunca.
 */
SET NOCOUNT ON;
SELECT n.usuario,
       CASE WHEN c.id IS NULL THEN 'NO EXISTE' ELSE c.estado END AS cuenta,
       CASE WHEN cr.hash_contrasena IS NOT NULL THEN 'Si' ELSE 'No' END AS tiene_contrasena,
       CASE WHEN cr.segundo_factor_activo = 1 THEN 'Si' ELSE 'No' END AS codigo_movil,
       (SELECT COUNT(*) FROM dbo.passkeys p WHERE p.cuenta_id = c.id) AS passkeys,
       (SELECT TOP (1) i.tipo + ' hasta ' + CONVERT(VARCHAR(16), i.caduca_en, 120)
          FROM dbo.invitaciones_acceso i
         WHERE i.cuenta_id = c.id AND i.usada_en IS NULL AND i.anulada_en IS NULL) AS invitacion_pendiente,
       (SELECT MAX(e.ocurrido_en) FROM dbo.eventos_acceso e
         WHERE e.cuenta_id = c.id AND e.tipo IN ('ENTRADA', 'ENTRADA_PASSKEY')) AS ultima_entrada
  FROM (VALUES ('almendros-administracion'), ('almendros-direccion'), ('almendros-medicina'), ('almendros-enfermeria'),
               ('almendros-enfermeria-planta1'), ('almendros-auxiliar'), ('almendros-elena'), ('olmos-enfermeria'),
               ('varios-centros'), ('plataforma-cj')) AS n(usuario)
  LEFT JOIN dbo.cuentas c ON c.sujeto_externo = n.usuario
  LEFT JOIN dbo.credenciales cr ON cr.cuenta_id = c.id
 ORDER BY n.usuario;
