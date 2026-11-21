/*
 * 0111 — Zonas horarias por centro, F3: se activa Canarias (estudio docs/producto/zonas-horarias-por-centro-estudio.md, apartado 5)
 *
 * Canarias (Atlantic/Canary) se dio de alta INACTIVA en 0109 para no ofrecerla antes de tiempo. Ya están hechas las dos condiciones que se pusieron:
 *   - las pantallas, los informes y «hoy» salen de la zona del centro (F0 y F1);
 *   - los recordatorios y avisos por correo, y sus procedimientos de SQL, usan la zona de cada centro (F2, 0110).
 * Desde este script Plataforma la ofrece al dar de alta un centro y al corregir la zona de uno. Los centros que ya existen siguen en Madrid: nada cambia para ellos.
 * La zona de fuera de España (F4) no está en el catálogo: espera a su propio estudio.
 */

IF OBJECT_ID(N'dbo.zonas_vigentes_centros', N'V') IS NULL
    THROW 51110, 'ZONAS_AVISOS_NO_EXISTE: aplica antes 0110', 1;
GO

-- Mismo control que 0109: el servidor tiene que conocer el nombre de Windows de la zona, o AT TIME ZONE fallaría más tarde en un procedimiento de avisos.
IF NOT EXISTS (SELECT 1 FROM sys.time_zone_info WHERE name = N'GMT Standard Time')
    THROW 51111, 'ZONA_WINDOWS_DESCONOCIDA: GMT Standard Time', 1;
GO

UPDATE dbo.zonas_horarias SET activa = 1 WHERE codigo = N'Atlantic/Canary' AND nombre_windows = N'GMT Standard Time';
IF @@ROWCOUNT <> 1
    THROW 51112, 'ZONA_CANARIAS_NO_ENCONTRADA', 1;
GO
