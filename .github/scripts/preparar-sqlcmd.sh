#!/usr/bin/env bash
# Deja un «sqlcmd» en el PATH de los pasos siguientes del job, sin instalar nada con apt.
#
# Antes cada job lo instalaba con apt-get desde el repositorio de Microsoft, pero sus dependencias (unixodbc...) salen de los servidores de Ubuntu: si el
# runner no los resuelve (pasó el 2026-11-11, y cada intento tardaba 9 minutos en rendirse), ni las pruebas ni el despliegue llegaban a empezar. Ahora se usa
# el sqlcmd que ya trae la imagen de SQL Server (la misma que el servicio de las pruebas, descargada desde mcr.microsoft.com): un contenedor auxiliar con el
# repositorio montado en la misma ruta, y un «sqlcmd» que ejecuta dentro de él (docker exec) en el directorio actual. Se comporta como el sqlcmd de
# mssql-tools18 de antes (es el mismo binario), con la red del runner (--network host: «localhost,1433» es el SQL Server del servicio).
#
# Solo pasa al contenedor la variable SQLCMDPASSWORD (la que usa database/aplicar-scripts.sh) y solo si está definida.
set -euo pipefail

imagen="mcr.microsoft.com/mssql/server:2022-latest"
contenedor="sqlcmd-herramientas"
bin="${RUNNER_TEMP:?Falta RUNNER_TEMP}/bin"

docker run -d --name "$contenedor" --network host --entrypoint sleep \
  -v "${GITHUB_WORKSPACE:?Falta GITHUB_WORKSPACE}:$GITHUB_WORKSPACE" "$imagen" infinity > /dev/null

mkdir -p "$bin"
cat > "$bin/sqlcmd" << 'FIN'
#!/usr/bin/env bash
entorno=()
[ -n "${SQLCMDPASSWORD:-}" ] && entorno=(-e SQLCMDPASSWORD)
exec docker exec "${entorno[@]}" -w "$PWD" sqlcmd-herramientas /opt/mssql-tools18/bin/sqlcmd "$@"
FIN
chmod +x "$bin/sqlcmd"
echo "$bin" >> "$GITHUB_PATH"

# Falla aquí, con un mensaje claro, y no a mitad de las pruebas, si el contenedor no arranca o la herramienta no está.
PATH="$bin:$PATH" sqlcmd -? > /dev/null
