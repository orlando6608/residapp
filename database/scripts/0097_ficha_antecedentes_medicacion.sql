/*
 * Ficha de antecedentes de salud, fase 3: medicación habitual (CJ, medicina-alergias-y-plazos-cj: «deben constar los medicamentos que utiliza con los nombres, dosis y frecuencia, ej. paracetamol
 * 1 g c/8 h o en la cena o a las 22 h»; diseño en docs/producto/ficha-antecedentes-salud-diseno.md, decisión 5, aprobado por Orlando el 2026-11-15; lectura de dpo-residapp en
 * docs/privacidad/ficha-alergias-enfermedades-medicacion-lectura-dpo.md, tablas 1.3 y 5.1 y apartado 5.2). Reutiliza las dos tablas de 0094 y 0096 (versiones y elementos) sin tablas
 * nuevas: amplía las listas de los CHECK y añade las columnas de la dosis y de la pauta.
 *
 * Qué es: la lista informativa de lo que el residente toma de forma habitual, copiada de la receta o de la hoja de tratamiento (ejemplo inventado: «Fármaco Alfa», «1 g», «cada 8 h»).
 * Incluye las pautas «si precisa», parches, inhaladores, insulinas y suplementos. NO es administración de medicación: no hay registro de lo dado, ni avisos, ni alertas, ni cruces con las
 * alergias o entre fármacos, ni vía, fecha de inicio o fin, prescriptor, motivo de la prescripción, adherencia («no se la toma»), stock, documentos ni fotos de recetas (DPO, 5.2; ADR 0011 y
 * 0015). La ficha es una copia de segunda mano: la fuente es la receta electrónica o la hoja de tratamiento oficial del centro; el informe lo dice y remite a ellas.
 *
 * Estados del apartado MEDICACION: CON_ENTRADAS (al menos un medicamento), NO_REFIERE («No toma medicación habitual») y NO_CONSTA («No se sabe o no consta»); los dos últimos no admiten ningún
 * elemento, como en ENFERMEDADES. Lo que la BD impone: el apartado nuevo; hasta 60 elementos (el tope es técnico); tipo MEDICAMENTO solo en MEDICACION (y los de los otros apartados solo en los
 * suyos); en MEDICAMENTO, la dosis y la pauta son obligatorias (texto corto; «no consta» es un valor válido cuando no se sabe), la reacción y el año no existen, y el nombre, la dosis y la pauta
 * no pasan de 100, 50 y 100 caracteres y son de una sola línea (sin CR ni LF); ningún elemento en un apartado de medicación que no sea CON_ENTRADAS. Lo que solo impone la aplicación: que
 * CON_ENTRADAS tenga al menos un elemento y que se inserten exactamente numero_elementos filas en la misma transacción; que el texto no tenga otros caracteres de control ni separadores Unicode
 * de línea; que la dosis y la pauta lleven al menos una letra o un número (no «-» ni «?»: cuando no se sabe se escribe «no consta»); que no haya duplicados (mismo nombre, dosis y pauta) en una versión. La BD no puede impedir que se escriban en esos campos observaciones, adherencia o datos de terceros: lo mitiga la
 * ayuda de la pantalla (revisión del DPO de la fase 2, 2026-11-16).
 *
 * No cambia permisos (el mismo HEALTH_BACKGROUND_EDIT), políticas de seguridad por filas ni GRANT/DENY de 0094: las tablas son las mismas.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

ALTER TABLE dbo.fichas_antecedentes_versiones DROP CONSTRAINT CK_fav_apartado;
ALTER TABLE dbo.fichas_antecedentes_versiones DROP CONSTRAINT CK_fav_elementos;
GO

ALTER TABLE dbo.fichas_antecedentes_versiones ADD CONSTRAINT CK_fav_apartado CHECK (apartado_codigo IN ('ALERGIAS', 'ENFERMEDADES', 'MEDICACION'));
ALTER TABLE dbo.fichas_antecedentes_versiones ADD CONSTRAINT CK_fav_elementos CHECK (
    ((apartado_codigo = 'ALERGIAS' AND numero_elementos BETWEEN 0 AND 50)
        OR (apartado_codigo = 'ENFERMEDADES' AND numero_elementos BETWEEN 0 AND 100)
        OR (apartado_codigo = 'MEDICACION' AND numero_elementos BETWEEN 0 AND 60))
    AND (estado_codigo <> 'CON_ENTRADAS' OR numero_elementos >= 1)
    AND (apartado_codigo = 'ALERGIAS' OR estado_codigo = 'CON_ENTRADAS' OR numero_elementos = 0));
GO

ALTER TABLE dbo.fichas_antecedentes_elementos DROP CONSTRAINT CK_fae_tipo;
GO

ALTER TABLE dbo.fichas_antecedentes_elementos ADD dosis NVARCHAR(50) NULL;
ALTER TABLE dbo.fichas_antecedentes_elementos ADD pauta NVARCHAR(100) NULL;
GO

ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_tipo CHECK (tipo_codigo IN ('ALERGIA', 'INTOLERANCIA', 'ENFERMEDAD', 'INTERVENCION', 'MEDICAMENTO'));
-- La dosis y la pauta solo existen en un medicamento y son obligatorias en él; de una sola línea y con tope.
ALTER TABLE dbo.fichas_antecedentes_elementos ADD CONSTRAINT CK_fae_medicamento CHECK (
    (tipo_codigo <> 'MEDICAMENTO' AND dosis IS NULL AND pauta IS NULL)
    OR (tipo_codigo = 'MEDICAMENTO' AND dosis IS NOT NULL AND pauta IS NOT NULL
        AND LEN(LTRIM(RTRIM(dosis))) > 0 AND LEN(LTRIM(RTRIM(pauta))) > 0
        AND LEN(nombre) <= 100 AND LEN(dosis) <= 50 AND LEN(pauta) <= 100
        AND nombre NOT LIKE '%' + CHAR(10) + '%' AND nombre NOT LIKE '%' + CHAR(13) + '%'
        AND dosis NOT LIKE '%' + CHAR(10) + '%' AND dosis NOT LIKE '%' + CHAR(13) + '%'
        AND pauta NOT LIKE '%' + CHAR(10) + '%' AND pauta NOT LIKE '%' + CHAR(13) + '%'));
-- Un medicamento no lleva reacción ni año (CK_fae_reaccion_tipo y CK_fae_anio de 0096 ya lo impiden: la reacción solo en alergias e intolerancias y el año solo en enfermedades e intervenciones).
GO

-- Al insertar un elemento, además de lo de 0094 y 0096, el tipo debe ser el de su apartado y el apartado MEDICACION solo admite elementos si es CON_ENTRADAS.
-- La búsqueda de la versión lleva FORCESEEK (como los disparadores de 0056): sin él, con pocas filas (una BD recién creada, como la del CI) el plan recorre toda la clave primaria y dos
-- guardados de residentes distintos a la vez, cada uno con su versión recién insertada sin confirmar, se esperan entre sí (error 1205).
ALTER TRIGGER dbo.TR_fae_coherence ON dbo.fichas_antecedentes_elementos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.fichas_antecedentes_versiones v WITH (FORCESEEK) ON v.id = i.version_id
         WHERE v.transaccion_id <> CURRENT_TRANSACTION_ID()
            OR i.posicion > v.numero_elementos
            OR (i.tipo_codigo = 'ALERGIA' AND v.estado_codigo <> 'CON_ENTRADAS')
            OR (v.apartado_codigo = 'ALERGIAS' AND i.tipo_codigo NOT IN ('ALERGIA', 'INTOLERANCIA'))
            OR (v.apartado_codigo = 'ENFERMEDADES' AND (i.tipo_codigo NOT IN ('ENFERMEDAD', 'INTERVENCION') OR v.estado_codigo <> 'CON_ENTRADAS'))
            OR (v.apartado_codigo = 'MEDICACION' AND (i.tipo_codigo <> 'MEDICAMENTO' OR v.estado_codigo <> 'CON_ENTRADAS'))
            OR (i.anio IS NOT NULL AND i.anio > YEAR(DATEADD(DAY, 1, SYSUTCDATETIME()))))
        THROW 50922, 'HEALTH_BACKGROUND_ELEMENT_INCOHERENT', 1;
END;
GO

COMMIT TRANSACTION;
GO
