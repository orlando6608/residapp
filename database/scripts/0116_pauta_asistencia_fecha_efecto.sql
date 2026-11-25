/*
 * 0116 — La pauta de asistencia del Centro de Día tiene FECHA DE EFECTO (ADR 0019, punto 5; decisión de Orlando, 2026-11-25; lectura de dpo-residapp en
 * docs/privacidad/centro-de-dia-pauta-fecha-efecto-lectura-dpo.md).
 *
 * Antes, la lista de seguimiento aplicaba la última versión de la pauta a todo el pasado: cambiar de lunes-miércoles-viernes a todos los días cambiaba lo que «tocaba» en días ya cerrados. Ahora cada
 * versión lleva su fecha y el día D se espera según la versión de mayor numero con vigente_desde <= D (no la de mayor fecha: así una versión nueva sustituye a una futura puesta por error, aunque la
 * tabla sea de solo añadir, y con la misma fecha vale la última).
 *
 *   vigente_desde   El primer día en que esa versión cuenta. La primera versión de cada persona vale «desde siempre» (0001-01-01: no se muestra nunca como fecha). Una versión nueva no puede ser
 *                   anterior a la de la versión que está en vigor hoy (la última con vigente_desde <= hoy, en la zona del centro), ni posterior a hoy + 90 días (valor propuesto, sin validar por CJ).
 *                   Se permite una fecha pasada, con ese suelo, para corregir un error: queda la constancia de quién, cuándo y con qué efecto (la fila guarda cuenta, perfil y momento).
 *
 * Las filas que ya existen valen «desde siempre» (por defecto), de modo que se leen igual que antes. Sin permisos nuevos: la tabla sigue siendo de solo añadir.
 * Código de error nuevo: 51157 ASSISTANCE_PATTERN_DATE_INVALID.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

ALTER TABLE dbo.asistencia_pauta ADD vigente_desde DATE NOT NULL CONSTRAINT DF_asp_vigente_desde DEFAULT '00010101';
GO

ALTER TRIGGER dbo.TR_asp_reglas ON dbo.asistencia_pauta AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.tipo_de_servicio <> N'CENTRO_DE_DIA' OR c.codigo = N'PLATAFORMA')
        THROW 51150, 'ASSISTANCE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.residentes r ON r.centro_id = i.centro_id AND r.id = i.residente_id
         WHERE r.estado <> N'ACTIVE'
            OR EXISTS (SELECT 1 FROM dbo.suspensiones_residente s WHERE s.residente_id = i.residente_id AND s.finalizada_en IS NULL))
        THROW 51153, 'ASSISTANCE_RESIDENT_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.asistencia_pauta previous ON previous.centro_id = i.centro_id AND previous.residente_id = i.residente_id AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND (previous.id IS NULL OR previous.dias_esperados = i.dias_esperados))
        THROW 51151, 'ASSISTANCE_SEQUENCE_INVALID', 1;
    -- La primera versión vale desde siempre.
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.numero = 1 AND i.vigente_desde <> '00010101')
        THROW 51157, 'ASSISTANCE_PATTERN_DATE_INVALID', 1;
    -- Una versión nueva: no anterior a la que está en vigor hoy, ni posterior a hoy + 90 días (hoy, en la zona del centro).
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.zonas_vigentes_centros z ON z.centro_id = i.centro_id
          CROSS APPLY (SELECT CAST(CAST(SYSUTCDATETIME() AS DATETIMEOFFSET) AT TIME ZONE z.nombre_windows AS DATE) AS hoy) t
          OUTER APPLY (SELECT TOP (1) p.vigente_desde FROM dbo.asistencia_pauta p
                        WHERE p.centro_id = i.centro_id AND p.residente_id = i.residente_id AND p.numero < i.numero AND p.vigente_desde <= t.hoy
                        ORDER BY p.numero DESC) inforce
         WHERE i.numero > 1 AND (i.vigente_desde > DATEADD(DAY, 90, t.hoy) OR i.vigente_desde < ISNULL(inforce.vigente_desde, '00010101')))
        THROW 51157, 'ASSISTANCE_PATTERN_DATE_INVALID', 1;
END;
GO

COMMIT TRANSACTION;
GO
