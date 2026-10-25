/*
 * Dos opciones rápidas nuevas en «Registrar cambio», decididas por CJ el 2026-10-25 (docs/pendientes-cj/dictado-por-voz-confirmaciones-cj, 1.2 y 1.4):
 *
 *  - PIEL_ENROJECIDA en «Lesiones en piel»: una zona enrojecida sin herida (por ejemplo, el sacro) es frecuente y útil para prevenir una UPP; sin casilla
 *    propia acababa marcada como «UPP», que es el error más grave.
 *  - COME_MENOS_HABITUAL en «Alimentación / hidratación»: comer menos de lo habitual es una señal temprana de empeoramiento; sin casilla acababa como
 *    «Disminución de ingesta de líquidos», un dato falso.
 *
 * Solo se amplía la lista de valores admitidos: las filas existentes siguen valiendo.
 */

ALTER TABLE dbo.cierres_cotidianos_cambio_area_opciones DROP CONSTRAINT CK_cccao_opcion;
ALTER TABLE dbo.cierres_cotidianos_cambio_area_opciones ADD CONSTRAINT CK_cccao_opcion CHECK (opcion_codigo IN (
    'NULA_INGESTA', 'RECHAZA_INGESTA', 'DISMINUCION_INGESTA_LIQUIDOS', 'ATRAGANTAMIENTO', 'COME_MENOS_HABITUAL',
    'NO_QUIERE_LEVANTARSE', 'INCAPACIDAD_CAMINAR', 'CAMINA_CON_DIFICULTAD', 'DEBILIDAD_GENERALIZADA',
    'DISMINUCION_ANIMO', 'IRRITABILIDAD', 'AGRESIVIDAD', 'HIPERREACTIVIDAD',
    'CEFALEA', 'DOLOR_MMSS', 'DOLOR_MMII', 'DOLOR_ABDOMINAL', 'DOLOR_OTRO',
    'DIARREA', 'ESTRENIMIENTO', 'DISMINUCION_DIURESIS', 'CAMBIOS_COLORACION_ORINA',
    'INSOMNIO', 'SOMNOLENCIA',
    'HERIDA', 'UPP', 'HEMATOMA', 'PIEL_ENROJECIDA'));
GO
