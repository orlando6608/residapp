/*
 * Módulos opcionales por centro (ADR 0015), PR 3: una referencia de solicitud no se repite para el mismo centro y módulo (decidido por Orlando, 2026-11-16). La tabla modulos_centro es
 * de solo añadir y no se puede corregir, así que un doble envío (dos clics, o reenviar el formulario) dejaría dos filas y dos eventos de auditoría para una sola petición del centro.
 * La aplicación lo comprueba antes, dentro de la transacción que ya bloquea la fila del centro, y da un aviso claro (MODULE_REFERENCE_REPEATED); el índice es la red de seguridad si algo
 * se salta esa comprobación. Una renovación o una baja necesitan su propia referencia: cada una es otra petición del centro.
 */

CREATE UNIQUE INDEX UX_mc_referencia ON dbo.modulos_centro (centro_id, modulo_codigo, referencia_solicitud);
GO
