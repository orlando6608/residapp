/*
 * 0112 — Zonas horarias por centro: dos zonas de América en el catálogo, INACTIVAS (estudio docs/producto/zonas-horarias-por-centro-estudio.md, apartado 3.8)
 *
 *   America/New_York      «Miami (Florida, EE. UU.)»           Windows: Eastern Standard Time              (UTC-5 en invierno, UTC-4 en verano)
 *   America/Mexico_City   «Ciudad de México (México)»          Windows: Central Standard Time (Mexico)     (UTC-6 todo el año desde 2022)
 *
 * Se dan de alta con activa = 0: Plataforma NO las ofrece al crear un centro ni al corregir la zona de uno. Una zona fuera de España no se activa sin su estudio de cumplimiento (transferencias
 * internacionales, norma local de datos de salud, encargo del tratamiento): cada una se activa con su propio script, como Canarias con 0111. Mientras tanto se pueden probar con filas directas
 * en zonas_centro (la clave foránea solo exige que la zona esté en el catálogo, no que esté activa).
 *
 * El horario de verano de estas zonas NO coincide con el de España (EE. UU. cambia el segundo domingo de marzo y el primero de noviembre) y México no tiene horario de verano: durante unas semanas
 * al año la diferencia con Madrid es distinta de la habitual. Los municipios fronterizos de México que siguen el horario de EE. UU. no están en el catálogo (Windows no los distingue).
 * Antes de activar cualquiera, la prueba de coherencia de ZonaHorariaCentroTests compara el desfase de .NET (tzdata) con el de SQL (AT TIME ZONE) durante 2026–2027, en los dos sentidos.
 */

IF OBJECT_ID(N'dbo.zonas_vigentes_centros', N'V') IS NULL
    THROW 51120, 'ZONAS_AVISOS_NO_EXISTE: aplica antes 0110', 1;
GO

-- Mismo control que 0109 y 0111: el servidor tiene que conocer el nombre de Windows de cada zona, o AT TIME ZONE fallaría más tarde en un procedimiento de avisos.
IF NOT EXISTS (SELECT 1 FROM sys.time_zone_info WHERE name = N'Eastern Standard Time')
    THROW 51121, 'ZONA_WINDOWS_DESCONOCIDA: Eastern Standard Time', 1;
IF NOT EXISTS (SELECT 1 FROM sys.time_zone_info WHERE name = N'Central Standard Time (Mexico)')
    THROW 51122, 'ZONA_WINDOWS_DESCONOCIDA: Central Standard Time (Mexico)', 1;
GO

INSERT INTO dbo.zonas_horarias (codigo, nombre_windows, etiqueta, orden, activa)
VALUES (N'America/New_York', N'Eastern Standard Time', N'Miami (Florida, EE. UU.)', 3, 0),
       (N'America/Mexico_City', N'Central Standard Time (Mexico)', N'Ciudad de México (México)', 4, 0);
GO
