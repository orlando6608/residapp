/*
 * Ficha de antecedentes de salud, fase 2: enfermedades e intervenciones (CJ, medicina-alergias-y-plazos-cj 2.x; diseño en docs/producto/ficha-antecedentes-salud-diseno.md, aprobado por
 * Orlando el 2026-11-15; lectura de dpo-residapp en docs/privacidad/ficha-alergias-enfermedades-medicacion-lectura-dpo.md, apartado 1.3 y tabla del apartado 5.1). Reutiliza las dos tablas
 * de 0094 (versiones y elementos) sin tablas nuevas: amplía las listas de los CHECK y añade la columna del año.
 *
 * Qué es: la lista de enfermedades e intervenciones quirúrgicas del residente (ejemplo inventado: «Hipertensión arterial», «Apendicectomía, 1999»). Es informativa. Cada elemento es un
 * nombre corto, su tipo (ENFERMEDAD o INTERVENCION) y, opcionalmente, el año. Antecedentes familiares, narrativa y valoraciones no se admiten (alcance); la BD no puede impedir que se
 * escriban en el nombre: lo mitiga la ayuda de la pantalla (revisión del DPO, 2026-11-16). «Pendiente» sigue siendo no tener ninguna versión.
 *
 * Estados del apartado ENFERMEDADES: CON_ENTRADAS (al menos un elemento), NO_REFIERE («Sin enfermedades ni intervenciones conocidas») y NO_CONSTA («No se sabe o no consta»). A diferencia
 * de las alergias, aquí NO_REFIERE y NO_CONSTA no admiten ningún elemento. Lo que la BD impone: el apartado nuevo; hasta 100 elementos (CJ pidió una lista completa que pueda crecer; el DPO
 * retiró el tope clínico, el tope es técnico); el tipo de cada elemento según su apartado (ALERGIA/INTOLERANCIA solo en ALERGIAS; ENFERMEDAD/INTERVENCION solo en ENFERMEDADES); la reacción
 * solo en alergias e intolerancias y el año solo en enfermedades e intervenciones; el año entre 1900 y el año del día de mañana en UTC (la aplicación rechaza además el futuro con la hora
 * del servidor, cuya zona fija el despliegue a la del centro); ningún elemento en un apartado de enfermedades que no sea CON_ENTRADAS; en enfermedades e intervenciones, un nombre de hasta
 * 100 caracteres y de una sola línea (sin CR ni LF; la columna admite 200 por 0094). Lo que solo impone la aplicación: que CON_ENTRADAS tenga al menos un elemento y que se inserten
 * exactamente numero_elementos filas en la misma transacción; que el texto no tenga otros caracteres de control ni separadores Unicode de línea; que no haya duplicados (mismo tipo,
 * nombre y año) en una versión.
 *
 * No cambia permisos (el mismo HEALTH_BACKGROUND_EDIT), políticas de seguridad por filas ni GRANT/DENY de 0094: las tablas son las mismas.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

ALTER TABLE dbo.fichas_antecedentes_versiones DROP CONSTRAINT CK_fav_apartado;
ALTER TABLE dbo.fichas_antecedentes_versiones DROP CONSTRAINT CK_fav_elementos;
GO

ALTER TABLE dbo.fichas_antecedentes_versiones ADD CONSTRAINT CK_fav_apartado CHECK (apartado_codigo IN ('ALERGIAS', 'ENFERMEDADES'));
ALTER TABLE dbo.fichas_antecedentes_versiones ADD CONSTRAINT CK_fav_elementos CHECK (
    ((apartado_codigo = 'ALERGIAS' AND numero_elementos BETWEEN 0 AND 50) OR (apartado_codigo = 'ENFERMEDADES' AND numero_elementos BETWEEN 0 AND 100))
    AND (estado_codigo <> 'CON_ENTRADAS' OR numero_elementos >= 1)
    AND (apartado_codigo <> 'ENFERMEDADES' OR estado_codigo = 'CON_ENTRADAS' OR numero_elementos = 0));
GO

ALTER TABLE dbo.fichas_antecedentes_elementos DROP CONSTRAINT CK_fae_posicion;
ALTER TABLE dbo.fichas_antecedentes_elementos DROP CONSTRAINT CK_fae_tipo;
GO

ALTER TABLE dbo.fichas_antecedentes_elementos ADD anio SMALLINT NULL;
GO

ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_posicion CHECK (posicion BETWEEN 1 AND 100);
ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_tipo CHECK (tipo_codigo IN ('ALERGIA', 'INTOLERANCIA', 'ENFERMEDAD', 'INTERVENCION'));
ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_reaccion_tipo CHECK (reaccion IS NULL OR tipo_codigo IN ('ALERGIA', 'INTOLERANCIA'));
ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_anio CHECK (anio IS NULL OR (tipo_codigo IN ('ENFERMEDAD', 'INTERVENCION') AND anio BETWEEN 1900 AND 2200));
-- Defensa en profundidad (DPO): si una inserción se salta la validación de la aplicación, la BD sigue rechazando un nombre largo o de varias líneas.
ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_nombre_enfermedad CHECK (
    tipo_codigo NOT IN ('ENFERMEDAD', 'INTERVENCION')
    OR (LEN(nombre) <= 100 AND nombre NOT LIKE '%' + CHAR(10) + '%' AND nombre NOT LIKE '%' + CHAR(13) + '%'));
GO

-- Al insertar un elemento, además de lo de 0094 (misma transacción que su versión, posición dentro de numero_elementos, ALERGIA solo con CON_ENTRADAS), el tipo debe ser el de su apartado,
-- el apartado ENFERMEDADES solo admite elementos si es CON_ENTRADAS, y el año no puede ser posterior al del día siguiente en UTC.
ALTER TRIGGER dbo.TR_fae_coherence ON dbo.fichas_antecedentes_elementos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.fichas_antecedentes_versiones v ON v.id = i.version_id
         WHERE v.transaccion_id <> CURRENT_TRANSACTION_ID()
            OR i.posicion > v.numero_elementos
            OR (i.tipo_codigo = 'ALERGIA' AND v.estado_codigo <> 'CON_ENTRADAS')
            OR (v.apartado_codigo = 'ALERGIAS' AND i.tipo_codigo NOT IN ('ALERGIA', 'INTOLERANCIA'))
            OR (v.apartado_codigo = 'ENFERMEDADES' AND (i.tipo_codigo NOT IN ('ENFERMEDAD', 'INTERVENCION') OR v.estado_codigo <> 'CON_ENTRADAS'))
            OR (i.anio IS NOT NULL AND i.anio > YEAR(DATEADD(DAY, 1, SYSUTCDATETIME()))))
        THROW 50922, 'HEALTH_BACKGROUND_ELEMENT_INCOHERENT', 1;
END;
GO

COMMIT TRANSACTION;
GO
