/*
 * Plazo de conservación de las bajas: de 5 a 15 años desde la baja (Orlando, 2026-11-03).
 *
 * Fuente: art. 12 de la Llei 21/2000 de Cataluña, en la redacción de la Ley 16/2010 (BOE-A-2010-10215, texto leído completo): 15 años desde el alta de cada proceso asistencial
 * para consentimientos informados, informes de alta, informes quirúrgicos, anestesia, exploraciones complementarias, necropsia y anatomía patológica; el resto «puede destruirse» a
 * los 5 años; y el cuadro de la Comissió Tècnica de Documentació Clínica (07-04-2017) recomienda 15 años para el curso clínico. Un plazo único y prudente para todo lo clínico.
 * Los 5 años anteriores salían de la respuesta de CJ (traslado-y-baja-residente, 2.1: «el tiempo que exija la RPGD, que en Cataluña es de 5 años»); el Reglamento no fija ese plazo.
 *
 * La aplicación (ResidentDischarge.RetentionYears) calcula ya 15 años en las bajas nuevas. Este script corrige las que se guardaron con 5: conservar_hasta pasa a ser la fecha de la
 * baja más 15 años, la misma cuenta que hace RetainUntil (fecha UTC de dada_en). TR_bjr_guard (0041) prohíbe cambiar conservar_hasta, así que se desactiva solo durante la corrección,
 * dentro de la misma transacción: si algo falla, se deshace todo, también el desactivado. No cambia el disparador ni el esquema, y no toca ninguna otra columna ni borra filas.
 * Nada la purga: ningún código usa conservar_hasta para borrar (lista de vencimientos en Administración > Residentes dados de baja, solo lectura).
 */

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DISABLE TRIGGER dbo.TR_bjr_guard ON dbo.bajas_residente;

UPDATE dbo.bajas_residente
   SET conservar_hasta = DATEADD(YEAR, 15, CAST(dada_en AS DATE))
 WHERE conservar_hasta <> DATEADD(YEAR, 15, CAST(dada_en AS DATE));

ENABLE TRIGGER dbo.TR_bjr_guard ON dbo.bajas_residente;

IF EXISTS (SELECT 1 FROM dbo.bajas_residente WHERE conservar_hasta <> DATEADD(YEAR, 15, CAST(dada_en AS DATE)))
    THROW 50860, 'RESIDENT_DISCHARGE_RETENTION_NOT_UPDATED', 1;
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_bjr_guard' AND parent_id = OBJECT_ID(N'dbo.bajas_residente') AND is_disabled = 1)
    THROW 50861, 'RESIDENT_DISCHARGE_GUARD_NOT_RESTORED', 1;

COMMIT TRANSACTION;
GO
