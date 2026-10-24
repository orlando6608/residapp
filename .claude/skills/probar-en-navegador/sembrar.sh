#!/usr/bin/env bash
# Crea en la BD local un centro ficticio con la unidad «Unidad Sur» y una cuenta con un perfil en ese centro y esa unidad, y le emite una
# invitación de alta con database/operaciones/invitar_cuenta.sql. Imprime dos líneas: el usuario y el enlace de la invitación, con el que
# comun.js entra como lo haría la persona (contraseña y código del móvil). Cada llamada crea un centro nuevo: cada recorrido empieza de cero.
# Uso: mapfile -t cuenta < <(bash sembrar.sh [PERFIL])   (ADMINISTRACION por defecto; también AUXILIAR, ENFERMERIA, MEDICINA o DIRECCION_CLINICA)
set -euo pipefail
perfil="${1:-ADMINISTRACION}"
servidor="${SQL_SERVER:-ACER-ORLANDO}"
base="${SQL_DATABASE:-ResidApp}"
direccion="${BASE_URL:-http://localhost:5203}"
raiz="$(cd "$(dirname "$0")/../../.." && pwd)"
case "$servidor" in *database.windows.net*) echo "Este guion es solo para la BD local." >&2; exit 1 ;; esac
case "$perfil" in AUXILIAR | ENFERMERIA | MEDICINA | ADMINISTRACION | DIRECCION_CLINICA) ;; *) echo "Perfil no válido: $perfil" >&2; exit 1 ;; esac
sufijo=$(date +%Y%m%d%H%M%S)
sujeto="prueba-navegador-$(echo "$perfil" | tr 'A-Z_' 'a-z-')-$sufijo"
sqlcmd -S "$servidor" -d "$base" -E -b -I -Q "
SET NOCOUNT ON;
DECLARE @now DATETIME2(3) = SYSUTCDATETIME(), @acc UNIQUEIDENTIFIER = NEWID(), @cen UNIQUEIDENTIFIER = NEWID(), @uni UNIQUEIDENTIFIER = NEWID(), @sc UNIQUEIDENTIFIER = NEWID();
BEGIN TRAN;
INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@acc, N'$sujeto', 'ACTIVE', @now);
INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@cen, N'PRUEBA-NAVEGADOR-$sufijo', N'Residencia de prueba (navegador $sufijo)', 'ACTIVE', @now);
INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@uni, @cen, 'SUR', N'Unidad Sur', 'ACTIVE', @now);
INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id) VALUES (@sc, @acc, @cen, '$perfil', 'ACTIVE', @now, @acc);
INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id) VALUES (NEWID(), @sc, @cen, @uni, @now, @acc);
COMMIT;" > /dev/null
# Con ruta relativa: el sqlcmd de Windows no abre la ruta absoluta que le pasa Git Bash.
ruta=$(cd "$raiz" && sqlcmd -S "$servidor" -d "$base" -E -b -I -f 65001 -v CUENTA="$sujeto" \
  -i database/operaciones/invitar_cuenta.sql | grep -o '/Acceso/Invitacion/[A-Za-z0-9_-]*')
echo "$sujeto"
echo "$direccion$ruta"
