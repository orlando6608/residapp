/*
 * Varios contactos urgentes (prioritarios) por residente (CJ, 2026-10-07).
 *
 * residentes_contacto_urgente seguía siendo un historial solo de inserción, pero cada fila sustituía al contacto anterior. Ahora cada fila
 * dice qué hace con su familiar (accion_codigo) y el conjunto vigente se obtiene recorriendo las filas por orden de número:
 *   - REEMPLAZAR: el conjunto pasa a ser solo ese familiar, o queda vacío si vinculo_id es NULL (el significado de las filas anteriores).
 *   - AGREGAR:    añade ese familiar al conjunto.
 *   - QUITAR:     lo quita del conjunto.
 * AGREGAR y QUITAR exigen vinculo_id. Las filas que ya existen quedan como REEMPLAZAR, que es lo que eran.
 */
ALTER TABLE dbo.residentes_contacto_urgente ADD accion_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL
    CONSTRAINT DF_rcu_accion DEFAULT 'REEMPLAZAR';
GO
ALTER TABLE dbo.residentes_contacto_urgente DROP CONSTRAINT DF_rcu_accion;
ALTER TABLE dbo.residentes_contacto_urgente ADD CONSTRAINT CK_rcu_accion CHECK (
    accion_codigo IN ('REEMPLAZAR', 'AGREGAR', 'QUITAR') AND (accion_codigo = 'REEMPLAZAR' OR vinculo_id IS NOT NULL));
GO
