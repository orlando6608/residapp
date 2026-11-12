# Base normativa local

**Para qué sirve.** Que quien revise la protección de datos de ResidApp (el agente `dpo-residapp`, una persona o cualquier otra herramienta) lea el **texto literal** de las leyes y de las guías de las autoridades, en vez de fiarse de la memoria o de resúmenes de la web. Un resumen puede omitir un artículo, un plazo o una reforma reciente; el texto literal, no.

## Cómo funciona

- [`catalogo.json`](catalogo.json) (está en el repositorio) lista las normas y guías que se siguen y de dónde se bajan.
- [`tools/normativa.ps1`](../../../tools/normativa.ps1) las baja a la carpeta `normativa-local/` (en la raíz del repositorio), con una línea por párrafo, y escribe `normativa-local/INDICE.md` con la versión y la fecha de cada texto. Git ignora esa carpeta, igual que `respuestas-cj-local/`: son textos públicos ajenos, que se regeneran en un minuto y cambian con cada reforma.
- Cada archivo empieza con unas líneas que comienzan por `#` y dicen de dónde sale, qué versión es y cuándo se bajó.

Para ponerla al día (la primera vez baja todo, en torno a un minuto; después, solo lo que haya cambiado, unos 30 segundos):

```powershell
pwsh -NoProfile -File tools/normativa.ps1              # baja lo que falta o ha cambiado
pwsh -NoProfile -File tools/normativa.ps1 -Comprobar   # solo informa, no baja nada
pwsh -NoProfile -File tools/normativa.ps1 -Solo rgpd,lopdgdd -Forzar
```

Hace falta PowerShell 7 (`pwsh`) y `curl.exe` (viene con Windows 10 y 11). Los PDF se pasan a texto con `pdftotext`, que viene con Git para Windows; si no lo encuentra, esa entrada da error y las demás siguen.

## Cómo se consulta

- Con Grep indicando `path: normativa-local`. Sin esa ruta, Grep no mira allí porque git ignora la carpeta. Los artículos empiezan la línea: `^Artículo 28\.` en `rgpd.txt`.
- Con Read, usando `offset` y `limit`, se lee el artículo entero.
- Al citar, se pone la norma, el artículo y la versión del índice. El número de línea solo ayuda a localizarlo, porque cambia con cada versión.

## Cosas que conviene saber

- **Texto consolidado:** es el texto de una ley con todas sus reformas ya incorporadas. Sirve para consultar, pero no es la versión oficial: la oficial es la publicada en el BOE (Boletín Oficial del Estado), en el DOGC (Diari Oficial de la Generalitat de Catalunya) o en el DOUE (Diario Oficial de la Unión Europea). Cada archivo lo dice en su cabecera.
- **Leyes catalanas:** se bajan del BOE, que las publica en castellano. El original catalán está en el DOGC y en el Portal Jurídic de Catalunya, que cargan el texto con JavaScript y no se pueden leer con una descarga simple.
- **Reformas incorporadas:** en los textos del BOE, la cabecera lista las normas que los han reformado y su fecha de publicación; en los de la UE, el propio texto marca lo modificado con `►M` (modificación) y `►C` (rectificación de errores).
- **Considerandos:** son los párrafos numerados que preceden a los artículos de un reglamento europeo y explican qué persigue. El texto consolidado de la UE no los incluye, así que el RGPD, el Reglamento de IA y el del Espacio Europeo de Datos de Salud tienen además un archivo `-considerandos`, sacado del texto original.
- **Fuentes que sirven texto y fuentes que no:** el BOE (su interfaz de datos abiertos) y la Oficina de Publicaciones de la UE sirven el texto. EUR-Lex bloquea las descargas hechas por programas y no se usa. Para saber cuál es la última versión consolidada de un acto de la UE, el script lo pregunta al registro de la Oficina de Publicaciones.
- **Versión consolidada que el registro anuncia pero no se puede bajar** (pasó con el Espacio Europeo de Datos de Salud): el script usa el texto original del Diario Oficial y lo avisa en la cabecera del archivo.
- **PDF:** cada página va marcada con `[Página N]`. Las tablas pueden perder el orden de las columnas; ante la duda, se contrasta con el PDF original.

## Cómo añadir una norma o una guía

Copia una línea del mismo tipo en `catalogo.json` y cambia sus datos:

- `boe`: el identificador `BOE-A-…` de la norma en el BOE (aparece en la dirección de su página, tras `id=`).
- `ue`: el número CELEX sin el prefijo de sector (por ejemplo, `2016R0679`; es el número con el que la UE identifica cada norma). Con `"modo": "original"` se guarda el texto del Diario Oficial en lugar del consolidado, y con `"parte": "considerandos"` solo el preámbulo.
- `pdf`: la dirección del PDF. `maxEdadDias` indica a los cuántos días se vuelve a comprobar (60 si no se pone).

Después, `pwsh -NoProfile -File tools/normativa.ps1 -Solo <id>`.

## Lo que esto no resuelve

Esta base da el **texto**. No decide qué norma se aplica a un caso concreto (por ejemplo, si una residencia cuenta como centro sanitario, o si al centro piloto le corresponde la AEPD o la APDCAT) ni sustituye a un DPO o a un abogado. Lo que el catálogo no cubre (resoluciones y criterios sueltos, páginas web) se sigue consultando en la web, avisando de que lo leído es un resumen.
