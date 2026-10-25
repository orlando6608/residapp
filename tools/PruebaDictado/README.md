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
`noDebeAparecer` (nombres que no deben salir en el resultado). **CJ validó los 31 de `jerga-es.json` el 2026-10-25**
(`docs/pendientes-cj/archivados/dictado-por-voz-confirmaciones-cj.respuestas.json`, pregunta 5.1): se corrigieron los casos c01, c03, c10, c25 y c28 y
ninguno queda dudoso; cada `nota` cita la respuesta de CJ que lo decide.

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
`registrar-cambio-v3` con **esquema por área** (cada área admite solo sus opciones): es el mismo texto que usa la aplicación
(`src/ResidApp.Infrastructure/IA/Prompts/registrar-cambio.md`) y lleva las reglas de CJ del 2026-10-25 («que proponga menos», jerga de piel y
comida, somnolencia, ánimo sin deducir). Si cambias uno, cambia el otro. `registrar-cambio-v2` y `registrar-cambio` (el v1) se conservan para comparar
(`Mistral__Prompt=registrar-cambio-v2`; el v1 con `Mistral__EsquemaPorArea=false`).

**Resultado del v3 (2026-10-25, solo texto, `mistral-small-latest`, dos pasadas):** 27 de 31 casos exactos en áreas y opciones, las 5 temperaturas
bien, ninguna sugerencia inválida, ninguna fuga de nombre y **ninguna casilla marcada de más**: los fallos son notas de más (por ejemplo, la orina
de c02 también como «Estado de conciencia») o de menos (c31, la caída, en una pasada), que la auxiliar corrige al revisar. El v2, con los mismos
casos: 27 en áreas y 26 en opciones, con «Disminución del ánimo» deducida en c03.
Modelos por defecto: `voxtral-mini-latest`, `mistral-small-latest` y `voxtral-mini-tts-3` (cámbialos con `Mistral:ModeloVoz`,
`Mistral:ModeloTexto` y `Mistral:ModeloTts`; la voz con `Mistral:VozTts`). No uses `voxtral-mini-tts-latest`: apunta a una versión sin voces
predefinidas.
