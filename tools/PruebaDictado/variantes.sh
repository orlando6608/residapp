#!/usr/bin/env bash
# Genera variantes de los audios limpios para acercarlos a la planta (requiere ffmpeg):
#   rapido: habla un 30 % más rápida
#   ruido:  ruido de fondo sintético (ruido rosa, sin audios de terceros) a unos 10 dB por debajo de la voz
#   planta: rápido + ruido + calidad de móvil (opus a 24 kbps)
# Uso: tools/PruebaDictado/variantes.sh [carpeta-audios]   (por defecto tools/PruebaDictado/audios)
set -euo pipefail
base="${1:-$(dirname "$0")/audios}"
limpio="$base/limpio"
[ -d "$limpio" ] || { echo "No existe $limpio. Genera antes los audios con 'PruebaDictado generar'." >&2; exit 1; }
mkdir -p "$base/rapido" "$base/ruido" "$base/planta"

# Ruido a -10 dB respecto a la voz: se calcula con la media de volumen de cada fichero.
ruido_mezcla() { # $1 entrada  $2 salida  $3 filtros previos (opcional)
  local entrada="$1" salida="$2" previo="${3:-}"
  local media
  media=$(ffmpeg -hide_banner -nostats -i "$entrada" ${previo:+-af "$previo"} -af "${previo:+$previo,}volumedetect" -f null - 2>&1 | sed -n 's/.*mean_volume: \(-\?[0-9.]*\) dB.*/\1/p')
  # El ruido rosa de anoisesrc con a=0.5 tiene un volumen medio de -19.9 dB: se resta para dejarlo 10 dB por debajo de la voz.
  local nivel; nivel=$(awk -v m="${media:--20}" 'BEGIN { printf "%.1f", m - 10 + 19.9 }')
  ffmpeg -hide_banner -loglevel error -y -i "$entrada" -f lavfi -i "anoisesrc=c=pink:r=24000:a=0.5" \
    -filter_complex "[0:a]${previo:+$previo,}aresample=24000[v];[1:a]volume=${nivel}dB[n];[v][n]amix=inputs=2:duration=first:normalize=0" \
    "$salida"
}

for f in "$limpio"/*.wav; do
  id=$(basename "$f" .wav)
  ffmpeg -hide_banner -loglevel error -y -i "$f" -af "atempo=1.3" "$base/rapido/$id.wav"
  ruido_mezcla "$f" "$base/ruido/$id.wav"
  ruido_mezcla "$f" "$base/planta/$id.wav" "atempo=1.3"
  ffmpeg -hide_banner -loglevel error -y -i "$base/planta/$id.wav" -ar 16000 -c:a libopus -b:a 24k "$base/planta/$id.opus"
  rm "$base/planta/$id.wav"
  echo "$id ok"
done
