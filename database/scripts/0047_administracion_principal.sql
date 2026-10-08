/*
 * Administración «principal» del centro (CJ, 2026-10-07; administracion-ambito-familiares-cargos, 1.1 y 1.2).
 *
 * Solo una Administración marcada como «principal» ve todas las unidades del centro y se añade a su ámbito las que no tiene; el soporte de
 * la plataforma también puede marcarla y añadir unidades a cualquier Administración. No se exige que cada unidad tenga una Administración
 * (1.2): la principal y el soporte pueden recuperarla.
 *
 * La marca no es una columna de ambitos_perfil (que solo admite revocar): es un historial inmutable de cambios. El estado vigente de un
 * ámbito es el de su último cambio. Quien marca es otra Administración principal o el operador de plataforma; la aplicación lo comprueba
 * dentro de la transacción y el trigger solo admite ámbitos de Administración con una numeración sin huecos.
 */

CREATE TABLE dbo.administraciones_principales_cambios (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_apc PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    ambito_perfil_id        UNIQUEIDENTIFIER NOT NULL,
    numero                  INT NOT NULL,
    principal               BIT NOT NULL,
    cambiado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_apc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT FK_apc_ambito FOREIGN KEY (ambito_perfil_id, centro_id) REFERENCES dbo.ambitos_perfil (id, centro_id),
    CONSTRAINT CK_apc_numero CHECK (numero > 0),
    CONSTRAINT CK_apc_perfil CHECK (cambiado_por_perfil IN ('ADMINISTRACION', 'PLATAFORMA'))
);
CREATE UNIQUE INDEX UX_apc_numero ON dbo.administraciones_principales_cambios (ambito_perfil_id, numero);
GO

CREATE TRIGGER dbo.TR_apc_immutable ON dbo.administraciones_principales_cambios INSTEAD OF UPDATE, DELETE AS
    THROW 50470, 'ADMIN_PRINCIPAL_CHANGE_IMMUTABLE', 1;
GO

/* Solo ámbitos de Administración; el primer cambio es el número 1 y cada uno sigue al anterior y lo contradice. */
CREATE TRIGGER dbo.TR_apc_valid ON dbo.administraciones_principales_cambios AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE p.perfil_codigo <> 'ADMINISTRACION')
        THROW 50471, 'ADMIN_PRINCIPAL_PROFILE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.administraciones_principales_cambios previous
                 ON previous.ambito_perfil_id = i.ambito_perfil_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.principal = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.principal = i.principal)))
        THROW 50472, 'ADMIN_PRINCIPAL_SEQUENCE_INVALID', 1;
END;
GO
