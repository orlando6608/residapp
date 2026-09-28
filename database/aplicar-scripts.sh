#!/usr/bin/env bash
# Aplica database/scripts/*.sql en orden alfabético, una sola vez cada uno, registrándolos en
# dbo.scripts_aplicados (los scripts de esquema NO son idempotentes: 0001 hace CREATE TABLE sin guardas).
# Con APLICAR_SEED=1 aplica además todos los database/seed/*.sql (idempotentes) en cada ejecución.
# Los seeds son datos ficticios: NUNCA usar APLICAR_SEED=1 contra un entorno con datos reales.
#
# Variables de entorno:
#   SQL_SERVER, SQL_DATABASE  (obligatorias)
#   SQL_USER, SQL_PASSWORD    (autenticación SQL; si SQL_USER está vacío se usa autenticación integrada -E)
#   SQLCMD_EXTRA              (flags adicionales, p. ej. "-C" para confiar en el certificado del servidor)
#   APLICAR_SEED              ("1" para aplicar también database/seed/)
set -euo pipefail

cd "$(dirname "$0")"

: "${SQL_SERVER:?Falta SQL_SERVER}"
: "${SQL_DATABASE:?Falta SQL_DATABASE}"

auth=(-E)
if [ -n "${SQL_USER:-}" ]; then
  auth=(-U "$SQL_USER")
  export SQLCMDPASSWORD="${SQL_PASSWORD:?Falta SQL_PASSWORD}"
fi
# shellcheck disable=SC2206
extra=(${SQLCMD_EXTRA:-})

run_sql() {
  sqlcmd -S "$SQL_SERVER" -d "$SQL_DATABASE" "${auth[@]}" "${extra[@]}" -b -I -f 65001 "$@"
}

# Azure SQL serverless en pausa responde "not currently available" mientras se reanuda: reintentar.
for intento in 1 2 3 4 5 6 7 8 9 10; do
  run_sql -l 60 -Q "SELECT 1" > /dev/null && break
  [ "$intento" = "10" ] && { echo "La base de datos no responde tras 10 intentos." >&2; exit 1; }
  echo "Base de datos no disponible (intento $intento), reintentando en 20 s..."
  sleep 20
done

run_sql -Q "IF OBJECT_ID(N'dbo.scripts_aplicados', N'U') IS NULL
  CREATE TABLE dbo.scripts_aplicados (
    nombre      NVARCHAR(200) NOT NULL CONSTRAINT PK_scripts_aplicados PRIMARY KEY,
    aplicado_en DATETIME2(3)  NOT NULL CONSTRAINT DF_scripts_aplicados_aplicado_en DEFAULT (SYSUTCDATETIME())
  );"

for fichero in scripts/*.sql; do
  nombre="$(basename "$fichero")"
  ya_aplicado="$(run_sql -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.scripts_aplicados WHERE nombre = N'$nombre';" | tr -d '[:space:]')"
  if [ "$ya_aplicado" = "1" ]; then
    echo "Ya aplicado: $nombre"
    continue
  fi
  echo "Aplicando: $nombre"
  run_sql -i "$fichero"
  run_sql -Q "INSERT INTO dbo.scripts_aplicados (nombre) VALUES (N'$nombre');"
done

if [ "${APLICAR_SEED:-}" = "1" ]; then
  for fichero in seed/*.sql; do
    echo "Aplicando seed: $(basename "$fichero")"
    run_sql -i "$fichero"
  done
fi
