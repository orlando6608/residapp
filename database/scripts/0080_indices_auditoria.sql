/*
 * Índices de dbo.eventos_auditoria según cómo se consulta de verdad (análisis del 2026-10-28; solo índices, ni datos ni columnas nuevas).
 *
 * Medido con una tabla simulada de 300.000 filas (un centro con 250.000, un año de uso), con las consultas reales:
 *   - Pantalla de auditoría de Administración (centro + periodo + acciones, más recientes primero): 6.834 lecturas de página -> 15.
 *   - Trazabilidad de un residente (DIR-14; centro + residente + acciones):                         6.319 lecturas de página -> 20.
 * IX_audit_scope_time (centro, unidad, residente, fecha) no servía para ninguna de las dos: la unidad, que va en medio, impide buscar por residente o por fecha sin recorrer
 * todo el centro. Los dos índices nuevos cubren esos accesos y IX_audit_scope_time pasa a sobrar (ningún código ni consulta la nombra), así que se quita: el número de índices
 * de una tabla que solo recibe inserciones no sube. Las dos consultas filtran también por unidad_id, por eso va en INCLUDE (evita buscar la fila entera para descartarla).
 *
 * No se añade un índice por cuenta: ninguna pantalla lo usa todavía (cuando la revisión de accesos del DPO lo pida, se crea entonces).
 */

CREATE INDEX IX_audit_centro_tiempo ON dbo.eventos_auditoria (centro_id, ocurrido_en DESC) INCLUDE (accion_codigo, unidad_id);
GO
CREATE INDEX IX_audit_residente_tiempo ON dbo.eventos_auditoria (centro_id, residente_id, ocurrido_en DESC) INCLUDE (accion_codigo, unidad_id);
GO
DROP INDEX IX_audit_scope_time ON dbo.eventos_auditoria;
GO
