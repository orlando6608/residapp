# El pipeline de CI/CD no provisiona ni siembra la base de datos de Azure

Detectado el 2026-09-15, al depurar por qué "Cambiar ámbito" no aparecía para `dev-enfermeria` en
`app-residapp-dev` (Azure) aunque el fix de código ya estaba desplegado.

## Qué se encontró

El job `deploy` de [`ci-cd.yml`](../../../.github/workflows/ci-cd.yml) hace `dotnet publish` + login
OIDC + `azure/webapps-deploy@v3`, pero **nunca ejecuta nada de `database/scripts/` ni
`database/seed/` contra la base de datos de Azure** (`sqldb-residapp-dev` en
`sql-residapp-dev.database.windows.net`). El job `build-and-test` sí aplica los scripts, pero solo
contra la base de test efímera de SQL Server en contenedor (Ubuntu), que se descarta al terminar el
run.

Al comprobarlo el 2026-09-15, `sqldb-residapp-dev` tenía únicamente el esquema de `0001`/`0002` (22
tablas) y **cero cuentas** — ni siquiera `dev-admin`. Nadie había vuelto a aplicar `0003`-`0006` ni
ningún script de `database/seed/` desde que se creó la base, así que cualquier cuenta de prueba
devolvía "Tu cuenta no tiene ningún ámbito activo en este momento" en la app desplegada, aunque
funcionara perfectamente en local.

Se corrigió puntualmente a mano esa vez (aplicando los 4 scripts de esquema que faltaban y los 5
scripts de semilla contra Azure vía `sqlcmd`, usando la connection string leída de
`az webapp config connection-string list`), pero **el pipeline sigue sin hacerlo por sí solo**.

## Impacto si no se corrige

Cualquier futuro script en `database/scripts/` (nuevo módulo, nueva tabla, columna añadida) quedará
aplicado en local y en el test de CI, pero **no en Azure**, hasta que alguien repita manualmente ese
mismo procedimiento contra `sql-residapp-dev.database.windows.net`. El síntoma es engañoso: el
deploy termina en verde, el código funciona, pero la pantalla correspondiente falla o se comporta de
forma distinta en Azure — como ya ocurrió con el selector de ámbito.

## Propuesta

Añadir un paso al job `deploy` de `ci-cd.yml`, después de `Azure login` y antes o después de
`Desplegar en Azure Web App`, que aplique con `sqlcmd -I -f 65001` (mismos flags que ya usa
`build-and-test`, más `-f 65001` para evitar la corrupción de codificación que también apareció al
aplicar estos scripts a mano) todos los scripts de `database/scripts/` en orden contra
`sqldb-residapp-dev`. Todos son idempotentes (`IF NOT EXISTS` / aditivos), así que reaplicarlos en
cada despliegue es seguro.

Decisión pendiente, a validar con CJ antes de implementarlo: si `database/seed/*.sql` (datos
ficticios de desarrollo: `dev-admin`, `dev-enfermeria`, `dev-multi`, etc.) debe aplicarse también de
forma automática en cada deploy a este entorno (`app-residapp-dev` es un entorno de desarrollo/piloto
interno, no producción con datos reales de residentes) o si debe seguir siendo un paso manual
deliberado para no sembrar cuentas ficticias sin que alguien lo decida explícitamente. En cualquier
caso, estos scripts de seed **no deben ejecutarse nunca** contra un futuro entorno de producción con
datos reales — el propio encabezado de cada uno ya lo advierte.

La cadena de conexión a usar es la ya configurada en la Web App
(`ConnectionStrings:ResidApp`, vía `az webapp config connection-string list --name app-residapp-dev
--resource-group rg-residapp-dev`); no debería quedar en texto plano en el propio workflow, sino
pasarse como secreto de GitHub Actions (`secrets.AZURE_SQL_CONNECTION_STRING` o similar), igual que
ya se hace con `CI_SQL_SA_PASSWORD` para la base de test.
