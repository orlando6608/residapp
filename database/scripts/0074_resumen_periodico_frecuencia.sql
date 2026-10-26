/*
 * Resumen periódico para la familia: la frecuencia, por centro (CJ, tercera tanda 1.1 a 1.5; las opciones y el valor de partida los decidió
 * Orlando el 2026-10-26). El resumen no se guarda: el Portal Familiar lo calcula al leer con los comunicados ya publicados del último periodo
 * cerrado, así que lo único que hay que guardar es cada cuánto se corta. Va en el horario de los comunicados del centro (0051), porque el
 * corte es la hora de publicación del centro.
 *
 * Sin fila en comunicados_horario_centro vale SEMANAL; con fila, la frecuencia que traiga (por defecto también SEMANAL, para las filas que ya
 * existen). El historial inmutable guarda la frecuencia anterior y la nueva de cada cambio (las filas anteriores a este script, SEMANAL en ambas).
 */

ALTER TABLE dbo.comunicados_horario_centro ADD
    resumen_frecuencia_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL
        CONSTRAINT DF_chc_resumen DEFAULT 'SEMANAL'
        CONSTRAINT CK_chc_resumen CHECK (resumen_frecuencia_codigo IN ('SEMANAL', 'DIARIA'));
GO

ALTER TABLE dbo.comunicados_horario_centro_historial ADD
    resumen_anterior_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL
        CONSTRAINT DF_chch_resumen_anterior DEFAULT 'SEMANAL'
        CONSTRAINT CK_chch_resumen_anterior CHECK (resumen_anterior_codigo IN ('SEMANAL', 'DIARIA')),
    resumen_nuevo_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL
        CONSTRAINT DF_chch_resumen_nuevo DEFAULT 'SEMANAL'
        CONSTRAINT CK_chch_resumen_nuevo CHECK (resumen_nuevo_codigo IN ('SEMANAL', 'DIARIA'));
GO
