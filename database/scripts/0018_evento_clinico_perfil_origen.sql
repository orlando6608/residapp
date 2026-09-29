/*
 * Coherencia entre el perfil que registra un evento propio y el origen de su ciclo de vida: un evento de
 * dbo.eventos_clinicos registrado por ENFERMERIA solo puede tener origen EVENTO_ENFERMERIA en
 * dbo.eventos_asistenciales, y uno de MEDICINA solo EVENTO_MEDICINA (0017). Hasta ahora lo garantizaba
 * únicamente SqlClinicalEventRepository, que escribe las dos filas en la misma transacción; al estar en tablas
 * distintas, ningún CHECK podía imponerlo.
 *
 * Se impone con una clave foránea compuesta sobre una columna calculada persistida, sin añadir un trigger:
 * - evento_clinico_perfil traduce el origen al perfil esperado; en CAMBIO_AUXILIAR vale NULL y la clave foránea
 *   no se comprueba (CK_ea_origen ya exige evento_clinico_id NULL en ese origen).
 * - La columna usa la misma collation que eventos_clinicos.registrado_por_perfil, como exige la clave foránea.
 * - origen_codigo es inmutable (TR_ea_transition_guard), así que la columna no cambia nunca.
 * - ADD CONSTRAINT valida las filas existentes: si alguna fuera incoherente, el script falla sin aplicarse.
 */

CREATE UNIQUE INDEX UX_ec_perfil ON dbo.eventos_clinicos (id, registrado_por_perfil);

ALTER TABLE dbo.eventos_asistenciales ADD evento_clinico_perfil AS
    CAST(CASE origen_codigo WHEN 'EVENTO_ENFERMERIA' THEN 'ENFERMERIA'
                            WHEN 'EVENTO_MEDICINA' THEN 'MEDICINA' END AS NVARCHAR(32))
        COLLATE Latin1_General_100_BIN2 PERSISTED;
GO

ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT FK_ea_evento_clinico_perfil
    FOREIGN KEY (evento_clinico_id, evento_clinico_perfil) REFERENCES dbo.eventos_clinicos (id, registrado_por_perfil);
GO
