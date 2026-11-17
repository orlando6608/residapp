/*
 * Marca de entorno en la base de datos (ADR 0015, punto 7). El ajuste Modulos__ActivarTodos enciende todos los módulos sin filas y solo vale en un entorno de desarrollo;
 * hasta ahora lo decidía únicamente la aplicación (ASPNETCORE_ENVIRONMENT o Entorno__Tipo). Esta tabla es la segunda barrera: la aplicación solo respeta el ajuste si la base
 * de datos a la que está conectada declara ser de desarrollo, así que una aplicación de desarrollo apuntada por error a una base de producción no activaría el dictado (ni
 * ningún módulo de pago) a todos los centros reales.
 *
 * La tabla se crea VACÍA: toda base nace «sin marca» y una base sin marca cuenta como producción. La fila 'Desarrollo' solo la pone database/seed/entorno_desarrollo.sql, que
 * aplicar-scripts.sh ejecuta únicamente con APLICAR_SEED=1 (Azure dev, la base local, los CI), nunca contra una base con datos reales. Sin datos personales.
 *
 * Sin seguridad por filas (no es de ningún centro) y sin permisos de escritura para la aplicación: no recibe GRANT (la lectura le llega por db_datareader, como en
 * scripts_aplicados) y no inserta, actualiza ni borra. Una tabla de una sola fila (id = 1) y un solo valor posible.
 */

CREATE TABLE dbo.entorno_bd (
    id   TINYINT NOT NULL CONSTRAINT PK_entorno_bd PRIMARY KEY CONSTRAINT CK_entorno_bd_id CHECK (id = 1),
    tipo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT CK_entorno_bd_tipo CHECK (tipo = N'Desarrollo')
);
GO
