/*
 * Un residente puede quedarse sin contacto urgente, indicando un motivo (CJ, 2026-10-10; documento confirmaciones-segunda-tanda-cj, 2.1:
 * «sin familia conocida»). Hasta ahora el alta exigía un contacto prioritario y la ficha no dejaba quitar el último; la regla estaba solo en la
 * aplicación, no en la base de datos.
 *
 *   - residentes_contacto_urgente.accion_codigo admite SIN_CONTACTO: el conjunto vigente queda vacío (como REEMPLAZAR sin vínculo) y la fila lleva
 *     el motivo. REEMPLAZAR sin vínculo no se reutiliza para esto porque ya existen filas así sin motivo (anteriores al 0044).
 *   - motivo: obligatorio (1 a 500 caracteres) en SIN_CONTACTO y NULL en las demás acciones. La tabla sigue siendo solo de inserción
 *     (TR_rcu_immutable), así que el motivo no se puede cambiar.
 */
ALTER TABLE dbo.residentes_contacto_urgente ADD motivo NVARCHAR(500) NULL;
GO
ALTER TABLE dbo.residentes_contacto_urgente DROP CONSTRAINT CK_rcu_accion;
ALTER TABLE dbo.residentes_contacto_urgente ADD CONSTRAINT CK_rcu_accion CHECK (
    accion_codigo IN ('REEMPLAZAR', 'AGREGAR', 'QUITAR', 'SIN_CONTACTO')
    AND (accion_codigo IN ('REEMPLAZAR', 'SIN_CONTACTO') OR vinculo_id IS NOT NULL)
    AND (accion_codigo <> 'SIN_CONTACTO' OR vinculo_id IS NULL));
ALTER TABLE dbo.residentes_contacto_urgente ADD CONSTRAINT CK_rcu_motivo CHECK (
    (accion_codigo = 'SIN_CONTACTO' AND motivo IS NOT NULL AND LEN(LTRIM(RTRIM(motivo))) > 0)
    OR (accion_codigo <> 'SIN_CONTACTO' AND motivo IS NULL));
GO
