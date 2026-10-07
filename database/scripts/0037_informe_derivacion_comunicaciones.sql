/*
 * Informe de derivación a Urgencias: apartado «Comunicaciones» (decisión de CJ, 2026-10-06). El motivo de la derivación sigue
 * siendo lo ocurrido y por qué se deriva; las anotaciones de contacto de esa derivación (por ejemplo, «Contacto telefónico con SEM
 * a las 18 h. Avisamos a la familia del traslado a Urgencias») van en un campo propio del informe que escribe el profesional. No
 * se rellena solo con los contactos del protocolo ni con el campo «Comunicaciones» de la valoración (DER-04 sigue vigente para lo
 * automático). Opcional: los informes ya firmados quedan en NULL y siguen siendo inmutables (TR_id_immutable); añadir una columna
 * no dispara el trigger.
 */

ALTER TABLE dbo.informes_derivacion ADD comunicaciones NVARCHAR(4000) NULL;
GO

ALTER TABLE dbo.informes_derivacion ADD CONSTRAINT CK_id_comunicaciones
    CHECK (comunicaciones IS NULL OR LEN(LTRIM(RTRIM(comunicaciones))) > 0);
GO
