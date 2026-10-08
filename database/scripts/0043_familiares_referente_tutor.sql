/*
 * Familiares de contacto prioritario, referentes y tutores legales al dar de alta (CJ, 2026-10-07; documento
 * administracion-ambito-familiares-cargos, 2.3).
 *
 *   - residentes_familiares.es_referente y es_tutor_legal: dos marcas por vínculo (un familiar puede ser las dos cosas, y su relación con un
 *     residente no es la que tiene con otro). El «contacto prioritario» es el contacto urgente de siempre (residentes_contacto_urgente).
 *   - El alta puede registrar a esos familiares en la misma transacción, también cuando la da Enfermería con permiso, así que quien designa
 *     el contacto urgente puede ser ADMINISTRACION o ENFERMERIA (CK_rcu_perfil; cambiar después el contacto sigue siendo de Administración).
 */

ALTER TABLE dbo.residentes_familiares ADD
    es_referente    BIT NOT NULL CONSTRAINT DF_rfa_referente DEFAULT (0),
    es_tutor_legal  BIT NOT NULL CONSTRAINT DF_rfa_tutor DEFAULT (0);
GO

ALTER TABLE dbo.residentes_contacto_urgente DROP CONSTRAINT CK_rcu_perfil;
ALTER TABLE dbo.residentes_contacto_urgente ADD CONSTRAINT CK_rcu_perfil CHECK (designado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'));
GO
