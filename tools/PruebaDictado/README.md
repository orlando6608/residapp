# Prueba de precisión del dictado (ADR 0009)

Herramienta de desarrollo, **fuera de la solución y de la aplicación**. Comprueba si Voxtral (voz a texto) y un modelo Mistral Small (texto
a campos) rellenan bien «Registrar cambio» del Auxiliar. **No uses audios ni nombres de residentes reales**: todavía no hay contrato de
encargado con Mistral ni evaluación de impacto.

En lenguaje llano: *transcribir* es pasar la voz a texto; *esquema estricto* es una plantilla que obliga al modelo a contestar solo con
los campos y valores permitidos; *TTS* (*text to speech*) es una voz sintética que lee un texto; *user-secrets* es un almacén de claves de
tu usuario de Windows, fuera del repositorio.

## Uso

```powershell
dotnet user-secrets --project tools/PruebaDictado set Mistral:ApiKey <tu clave>

# 1. Solo texto (prueba el segundo paso, sin audio)
dotnet run --project tools/PruebaDictado -- tools/PruebaDictado/casos/jerga-es.json salida.json

# 2. Generar los audios con voz sintética (Voxtral TTS, voz Elena) y sus variantes con ruido y velocidad (necesita ffmpeg)
dotnet run --project tools/PruebaDictado -- generar tools/PruebaDictado/casos/jerga-es.json tools/PruebaDictado/audios/limpio
bash tools/PruebaDictado/variantes.sh

# 3. Evaluar con voz: transcribe <carpeta>/<id>.* y estructura el resultado
dotnet run --project tools/PruebaDictado -- tools/PruebaDictado/casos/jerga-es.json salida.json --voz tools/PruebaDictado/audios/planta
```

Los audios (`audios/`) no se suben a git: se regeneran con los comandos de arriba.

## Los casos

`casos/ejemplo.json` (2 casos) y `casos/jerga-es.json` (31 casos de habla de planta con jerga: UPP, Mepentol, dieta túrmix, absorbente,
decul, cambios posturales…). Cada caso: `texto` (lo que se dice y, con `generar`, lo que se sintetiza), `esperado` (áreas, opciones rápidas y
temperatura), `jerga` (términos que no deben perderse al transcribir), `dudoso` y `nota` (mapeo clínico que CJ debe confirmar) y
`noDebeAparecer` (nombres que no deben salir en el resultado). **Los resultados esperados son provisionales hasta que CJ los valide.**

## Qué mide

Áreas exactas, opciones exactas, temperatura, sugerencias inválidas (opción de otra área o área repetida), fugas de nombre y, con voz,
las palabras que difieren del texto original y la jerga que se perdió al transcribir. Se resume con y sin los casos dudosos. El `texto`
libre no se puntúa: se revisa a ojo en `salida.json`.

## Límites de la prueba

- **El audio sintético es limpio y claro; el de la planta no.** La prueba es optimista con el reconocimiento de voz y **no sustituye** a
  audios reales de planta (ADR 0009). Las variantes añaden velocidad ×1,3, ruido rosa 10 dB por debajo de la voz y calidad de móvil (opus
  a 24 kbps), pero el ruido de una residencia real (carros, voces, timbres, acentos) es otra cosa.
- Hay una sola voz en español (Elena); no se clonan voces de personas sin su consentimiento.
- La voz puede pronunciar mal la jerga; ante un fallo hay que mirar si falla la transcripción (columna de jerga perdida) o la estructuración.

El prompt (las instrucciones que recibe el modelo) está en `Prompts/`, versionado como el código (ADR 0011). Por defecto se usa
`registrar-cambio-v2` con **esquema por área** (cada área admite solo sus opciones); `registrar-cambio` es el v1, que se conserva para
comparar (`Mistral__Prompt=registrar-cambio`, `Mistral__EsquemaPorArea=false`).
Modelos por defecto: `voxtral-mini-latest`, `mistral-small-latest` y `voxtral-mini-tts-3` (cámbialos con `Mistral:ModeloVoz`,
`Mistral:ModeloTexto` y `Mistral:ModeloTts`; la voz con `Mistral:VozTts`). No uses `voxtral-mini-tts-latest`: apunta a una versión sin voces
predefinidas.
