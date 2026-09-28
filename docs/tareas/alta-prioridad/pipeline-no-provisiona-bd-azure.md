# El pipeline de CI/CD no provisiona ni siembra la base de datos de Azure

**Estado: resuelto el 2026-09-28** (ver "Solución aplicada" al final).

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

Se corrigió puntualmente a mano esa vez (aplicando los scripts de esquema que faltaban y los scripts
de semilla contra Azure vía `sqlcmd`, usando la connection string leída de
`az webapp config connection-string list`).

## Impacto si no se corrige

Cualquier futuro script en `database/scripts/` (nuevo módulo, nueva tabla, columna añadida) quedará
aplicado en local y en el test de CI, pero **no en Azure**, hasta que alguien repita manualmente ese
mismo procedimiento contra `sql-residapp-dev.database.windows.net`. El síntoma es engañoso: el
deploy termina en verde, el código funciona, pero la pantalla correspondiente falla o se comporta de
forma distinta en Azure — como ya ocurrió con el selector de ámbito.

## Solución aplicada

- **Corrección de la propuesta original**: esta nota proponía reaplicar todos los scripts de
  `database/scripts/` en cada despliegue asumiendo que eran idempotentes. **No lo son**: `0001` hace
  `CREATE TABLE` sin guardas, `0002` renombra y `0003`-`0006` hacen `DROP CONSTRAINT`/`CREATE TABLE`/
  `CREATE TRIGGER` planos. Los seeds sí son idempotentes (`IF NOT EXISTS` por cuenta).
- [`database/aplicar-scripts.sh`](../../../database/aplicar-scripts.sh) aplica `database/scripts/*.sql`
  en orden alfabético **una sola vez cada uno**, registrándolos en `dbo.scripts_aplicados` (que crea
  si no existe), con `sqlcmd -b -I -f 65001`. Con `APLICAR_SEED=1` aplica además todos
  `database/seed/*.sql` en cada ejecución. Reintenta la conexión inicial porque la base serverless de
  Azure responde "not currently available" mientras se reanuda de la pausa.
- `build-and-test` usa ese mismo script (sin seed) en lugar de la lista de scripts escrita a mano, así
  que un script nuevo ya no hay que añadirlo al workflow.
- `deploy` lo ejecuta **antes** de desplegar la Web App, con `APLICAR_SEED=1`, leyendo la cadena
  `ResidApp` de la propia Web App con `az webapp config connection-string list` tras el login OIDC (sin
  secretos nuevos en GitHub; la contraseña se enmascara en el log).
- Decisión (2026-09-28): los seeds ficticios (`dev-admin`, `dev-enfermeria`, `dev-multi`, etc.) se
  aplican automáticamente en `app-residapp-dev` porque es un entorno de desarrollo/piloto interno sin
  datos reales. **Nunca** debe usarse `APLICAR_SEED=1` contra un futuro entorno de producción con
  datos reales — el propio encabezado de cada seed ya lo advierte.
- Bootstrap único en Azure: como `sqldb-residapp-dev` ya tenía aplicados a mano los scripts `0001` a
  `0006` (verificado el 2026-09-28 por las tablas y constraints que crea cada uno), se creó
  `dbo.scripts_aplicados` y se registraron esos 7 nombres para que el primer deploy no intentara
  rehacerlos. Cualquier otro entorno ya creado a mano necesita el mismo bootstrap; uno nuevo y vacío no.
