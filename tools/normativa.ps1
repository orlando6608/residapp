#requires -Version 7.0
<#
.SYNOPSIS
    Mantiene al día la base normativa local: los textos literales de las leyes y guías que consulta el agente dpo-residapp.

.DESCRIPTION
    Lee docs\privacidad\normativa\catalogo.json (qué normas y guías se siguen y de dónde salen) y deja en normativa-local\
    (ignorada por git) un .txt por entrada, con una línea por párrafo, y un INDICE.md con la versión y la fecha de cada texto.
    Es lo que lee el agente en lugar de fiarse de los resúmenes de la web.

    Fuentes:
    - BOE: API de datos abiertos, texto consolidado (la ley con sus reformas ya incorporadas). Sirve también las leyes catalanas, en castellano.
    - UE: Oficina de Publicaciones de la UE (el texto del Diario Oficial o, si existe, su versión consolidada más reciente).
      EUR-Lex bloquea las descargas automáticas, por eso no se usa.
    - PDF: se bajan y se pasan a texto con pdftotext (viene con Git para Windows; si no está, esa entrada da error y las demás siguen).
    Un texto consolidado sirve para consultar, pero no es la versión oficial: la oficial es la del BOE, el DOGC o el DOUE.

    Sin parámetros, mira la versión de cada texto en su fuente y baja solo lo que falta o ha cambiado (si no hay novedades tarda unos segundos).
    Si una entrada falla, las demás siguen y el script termina con código 1.

.EXAMPLE
    pwsh -NoProfile -File tools/normativa.ps1
    pwsh -NoProfile -File tools/normativa.ps1 -Comprobar
    pwsh -NoProfile -File tools/normativa.ps1 -Solo rgpd,lopdgdd -Forzar
#>
param(
    [switch] $Comprobar,   # solo informa de qué falta o ha cambiado; no baja nada ni toca la carpeta
    [switch] $Forzar,      # baja de nuevo aunque el texto local esté al día
    [string[]] $Solo,      # limita a estas entradas del catálogo (por id)
    [string] $Carpeta = (Join-Path $PSScriptRoot '..\normativa-local'),
    [string] $Catalogo = (Join-Path $PSScriptRoot '..\docs\privacidad\normativa\catalogo.json')
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
try { [Console]::OutputEncoding = $utf8 } catch { }
$inv = [Globalization.CultureInfo]::InvariantCulture
$cliente = 'Mozilla/5.0 (compatible; normativa-local)'
$Carpeta = [IO.Path]::GetFullPath($Carpeta)
$temporal = Join-Path ([IO.Path]::GetTempPath()) 'residapp-normativa'
New-Item -ItemType Directory -Force $Carpeta, $temporal | Out-Null
$hoy = Get-Date
$ahora = $hoy.ToString('yyyy-MM-dd HH:mm', $inv)

# --- Utilidades -------------------------------------------------------------------------------------------------

# Se usa curl.exe (viene con Windows 10 y 11) porque Invoke-WebRequest falla con las respuestas de la Oficina de Publicaciones de la UE.
function Bajar([string] $url, [string] $destino, [string[]] $cabeceras = @()) {
    $a = @('-sL', '--fail', '--max-time', '240', '-A', $cliente, '-o', $destino, '-w', '%{http_code}')
    foreach ($c in $cabeceras) { $a += '-H', $c }
    $http = & curl.exe @a $url
    if ($LASTEXITCODE -ne 0) { throw "no se pudo bajar $url (HTTP $http, curl $LASTEXITCODE)" }
}
function Leer([string] $fichero) { [IO.File]::ReadAllText($fichero, $utf8) }
function Limpiar([string] $t) { ($t -replace '\s+', ' ').Trim() }
function FechaBoe([string] $s) { if ($s -match '^(\d{4})(\d{2})(\d{2})') { '{0}-{1}-{2}' -f $Matches[1], $Matches[2], $Matches[3] } else { $s } }

# Escribe el .txt (cabecera con la procedencia + una línea por párrafo) y devuelve cuántas líneas tiene.
function Escribir([string] $ruta, [string[]] $cabecera, $lineas) {
    $todo = New-Object System.Collections.Generic.List[string]
    $todo.AddRange($cabecera)
    $todo.Add('# ---')
    foreach ($l in $lineas) { $todo.Add($l) }
    [IO.File]::WriteAllText($ruta, (($todo -join "`n") + "`n"), $utf8)
    $todo.Count
}

# --- BOE ---------------------------------------------------------------------------------------------------------

function BoeMetadatos([string] $id) {
    $f = Join-Path $temporal "$id.meta.xml"
    Bajar "https://www.boe.es/datosabiertos/api/legislacion-consolidada/id/$id/metadatos" $f @('Accept: application/xml')
    [xml] $x = Leer $f
    if (-not $x.SelectSingleNode('//metadatos')) { throw "el BOE no devuelve metadatos para $id" }
    $m = @{}
    foreach ($n in 'titulo', 'fecha_actualizacion', 'url_eli') {
        $e = $x.SelectSingleNode("//metadatos/$n")
        $m[$n] = if ($e) { $e.InnerText } else { '' }
    }
    $m
}

# Texto vigente hoy de cada bloque (el BOE guarda todas las versiones); las versiones con vigencia futura se marcan aparte.
function BoeATexto([string] $id) {
    $f = Join-Path $temporal "$id.texto.xml"
    Bajar "https://www.boe.es/datosabiertos/api/legislacion-consolidada/id/$id/texto" $f @('Accept: application/xml')
    [xml] $x = Leer $f
    $hoyN = $hoy.ToString('yyyyMMdd', $inv)
    $lineas = New-Object System.Collections.Generic.List[string]
    $reformas = @{}
    foreach ($bloque in $x.SelectNodes('//texto/bloque')) {
        $vigente = $null; $desdeVigente = ''; $futuras = @()
        foreach ($v in $bloque.SelectNodes('version')) {
            $desde = $v.GetAttribute('fecha_vigencia'); $norma = $v.GetAttribute('id_norma')
            if ($norma -and $norma -ne $id) { $reformas[$norma] = $v.GetAttribute('fecha_publicacion') }
            if ($desde -le $hoyN) { if ($desde -ge $desdeVigente) { $vigente = $v; $desdeVigente = $desde } } else { $futuras += $v }
        }
        if ($vigente) { foreach ($p in $vigente.SelectNodes('.//p')) { $t = Limpiar $p.InnerText; if ($t) { $lineas.Add($t) } } }
        foreach ($v in $futuras) {
            $lineas.Add(('[VIGENCIA FUTURA desde {0}, según {1}]' -f (FechaBoe $v.GetAttribute('fecha_vigencia')), $v.GetAttribute('id_norma')))
            foreach ($p in $v.SelectNodes('.//p')) { $t = Limpiar $p.InnerText; if ($t) { $lineas.Add($t) } }
        }
    }
    if ($lineas.Count -lt 5) { throw "el texto de $id llegó vacío" }
    @{ lineas = $lineas; reformas = $reformas }
}

# --- Unión Europea (Oficina de Publicaciones) --------------------------------------------------------------------

# Última versión consolidada de un acto (CELEX sin el prefijo de sector, p. ej. 2016R0679), o $null si no tiene.
function UeUltimaConsolidada([string] $base) {
    $q = 'PREFIX cdm: <http://publications.europa.eu/ontology/cdm#> SELECT ?celex WHERE { ?w cdm:resource_legal_id_celex ?celex . FILTER(STRSTARTS(STR(?celex), "0' + $base + '-")) } ORDER BY DESC(?celex) LIMIT 1'
    $f = Join-Path $temporal 'consulta.json'
    Bajar ('https://publications.europa.eu/webapi/rdf/sparql?query=' + [uri]::EscapeDataString($q)) $f @('Accept: application/sparql-results+json')
    $r = (Leer $f) | ConvertFrom-Json
    $b = @($r.results.bindings)
    if ($b.Count -gt 0) { $b[0].celex.value } else { $null }
}

# El XHTML del Diario Oficial pasa a una línea por párrafo; las enumeraciones (a), b)...) están en tablas y se unen en una línea.
function UeATexto([string] $celex) {
    $f = Join-Path $temporal "$celex.xhtml"
    Bajar "https://publications.europa.eu/resource/celex/$celex" $f @('Accept: application/xhtml+xml', 'Accept-Language: spa')
    $h = Leer $f
    $h = [regex]::Replace($h, '(?is)<(script|style|head)\b.*?</\1>', '')
    $h = [regex]::Replace($h, '\s+', ' ')   # los saltos de línea del HTML son de formato, no de párrafo: los párrafos se marcan abajo
    $h = [regex]::Replace($h, '(?is)<tr\b[^>]*>(.*?)</tr>', [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $celdas = @([regex]::Matches($m.Groups[1].Value, '(?is)<t[dh]\b[^>]*>(.*?)</t[dh]>') |
            ForEach-Object { Limpiar ($_.Groups[1].Value -replace '(?s)<[^>]+>', ' ') } | Where-Object { $_ -ne '' })
        $sep = if ($celdas.Count -eq 2 -and $celdas[0].Length -le 6) { ' ' } else { ' | ' }
        "`n" + ($celdas -join $sep) + "`n"
    })
    # Las listas del texto consolidado van en dos columnas (la numeración y el párrafo): se unen en una línea.
    $h = [regex]::Replace($h, '(?is)(</(p|div)>\s*)+<div class="grid-list-column-2">', ' ')
    $h = [regex]::Replace($h, '(?i)</(p|div|h[1-6]|li|ul|ol|table|blockquote|section|article)>|<br\s*/?>', "`n")
    $h = [regex]::Replace($h, '(?s)<[^>]+>', '')
    $h = [System.Net.WebUtility]::HtmlDecode($h)
    $bruto = @($h -split "`n" | ForEach-Object { Limpiar $_ } | Where-Object { $_ -ne '' })
    # «Artículo 28» y su título vienen en líneas separadas: se unen para poder buscar «Artículo 28.» como en el BOE.
    $lineas = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $bruto.Count; $i++) {
        if ($bruto[$i] -match '^Art[ií]culo \d+( \w+)?$' -and $i + 1 -lt $bruto.Count -and $bruto[$i + 1].Length -le 150 -and $bruto[$i + 1] -notmatch '^(\d+\.|[►▼])') {
            $lineas.Add($bruto[$i] + '. ' + $bruto[$i + 1]); $i++
        } else { $lineas.Add($bruto[$i]) }
    }
    if ($lineas.Count -lt 5) { throw "el texto de $celex llegó vacío" }
    $lineas
}

# --- PDF -----------------------------------------------------------------------------------------------------------

function BuscarPdftotext {
    $c = Get-Command pdftotext -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($r in @("$env:ProgramFiles\Git\mingw64\bin\pdftotext.exe", "${env:ProgramFiles(x86)}\Git\mingw64\bin\pdftotext.exe")) { if (Test-Path $r) { return $r } }
    $null
}

# Cada página queda marcada con [Página N] para poder citarla.
function PdfATexto([string] $pdf) {
    $exe = BuscarPdftotext
    if (-not $exe) { throw 'falta pdftotext (viene con Git para Windows, en Program Files\Git\mingw64\bin)' }
    $salida = Join-Path $temporal 'pdf.txt'
    & $exe -enc UTF-8 -layout $pdf $salida
    if ($LASTEXITCODE -ne 0) { throw "pdftotext terminó con el error $LASTEXITCODE" }
    $lineas = New-Object System.Collections.Generic.List[string]
    $n = 0
    foreach ($pagina in ((Leer $salida) -split "`f")) {
        $n++
        $filas = @($pagina -split "`r?`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_.Trim() -ne '' })
        if ($filas.Count -eq 0) { continue }
        $lineas.Add("[Página $n]")
        foreach ($l in $filas) { $lineas.Add($l) }
    }
    if ($lineas.Count -lt 5) { throw 'el PDF no tiene texto que se pueda extraer (¿es un escaneo?)' }
    $lineas
}

# --- Índice -----------------------------------------------------------------------------------------------------------

$uso = (@'
## Cómo usarla

1. **Buscar:** con Grep indicando siempre `path: normativa-local` (sin esa ruta, Grep no mira aquí porque la carpeta está ignorada por git). Los artículos empiezan la línea: por ejemplo `^Artículo 28\.` en `rgpd.txt`. Con Read y `offset`/`limit` se lee el artículo entero.
2. **Citar:** la norma, el artículo y la versión de la tabla de abajo; el número de línea solo ayuda a localizarlo (cambia con cada versión). Un texto consolidado (con las reformas ya incorporadas) sirve para consultar, pero no es la versión oficial: dilo cuando cites uno. En los PDF, `[Página N]` cuenta las páginas del archivo PDF (portada incluida), que puede no coincidir con el número impreso: cita las dos si difieren. En las leyes del BOE, la cabecera lista las reformas solo por su código `BOE-A-…`; el nombre de cada una sale de las notas dentro del texto (busca el código).
3. **Poner al día:** `pwsh -NoProfile -File tools/normativa.ps1` baja lo que falta o ha cambiado (`-Comprobar` solo mira; `-Solo id` limita a una entrada; `-Forzar` baja de nuevo). Hazlo si la comprobación más antigua de arriba tiene más de una semana, o si la consulta depende de una reforma reciente.
4. **Si la norma no está aquí:** búscala en la web y, si la vas a usar más veces, añade una línea a `docs/privacidad/normativa/catalogo.json`.
5. **Lo que el catálogo no cubre** (resoluciones, criterios y dictámenes sueltos, páginas web) se consulta en la web; en ese caso di que lo leído es un resumen y no el texto literal.
'@) -split "`r?`n"

function EscribirIndice {
    $md = New-Object System.Collections.Generic.List[string]
    $comprobadas = @($todas | ForEach-Object { $estado[$_.id] } | Where-Object { $_ -and $_.comprobado } | ForEach-Object { [string] $_.comprobado } | Sort-Object)
    $masAntigua = if ($comprobadas.Count) { $comprobadas[0] } else { 'nunca' }
    $md.Add('# Base normativa local')
    $md.Add('')
    $md.Add(('Generada por `tools/normativa.ps1` el {0}. Comprobación más antigua de las entradas: {1}. Esta carpeta no se versiona en git: se regenera con el script.' -f $ahora, $masAntigua))
    $md.Add('')
    foreach ($l in $uso) { $md.Add($l) }
    foreach ($g in @($todas | ForEach-Object { $_.grupo } | Select-Object -Unique)) {
        $md.Add(''); $md.Add("## $g"); $md.Add('')
        $md.Add('| Id | Norma o documento | Versión | Comprobado | Archivo | Nota |')
        $md.Add('|---|---|---|---|---|---|')
        foreach ($e in ($todas | Where-Object { $_.grupo -eq $g })) {
            $s = $estado[$e.id]
            $nota = if ($e.nota) { $e.nota } else { '' }
            if ($s -and $s.error) { $nota = ('ERROR: ' + $s.error + ' ' + $nota).Trim() }
            if ($s -and $s.archivo) {
                $md.Add(('| {0} | {1} | {2} | {3} | `{4}` ({5} líneas) | {6} |' -f $e.id, $e.titulo, $s.descripcion, $s.comprobado, $s.archivo, $s.lineas, $nota))
            } else {
                $md.Add(('| {0} | {1} | sin descargar | | | {2} |' -f $e.id, $e.titulo, $nota))
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $Carpeta 'INDICE.md'), (($md -join "`n") + "`n"), $utf8)
}

# --- Proceso -------------------------------------------------------------------------------------------------------

$cat = (Leer $Catalogo) | ConvertFrom-Json
$todas = @($cat.entradas)
# Con «pwsh -File ... -Solo a,b» PowerShell pasa «a,b» como un solo texto: se separa aquí.
$Solo = @($Solo | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Solo.Count) {
    $faltan = @($Solo | Where-Object { $_ -notin @($todas | ForEach-Object { $_.id }) })
    if ($faltan.Count) { throw ('No están en el catálogo: ' + ($faltan -join ', ')) }
}
$entradas = @($todas | Where-Object { (-not $Solo.Count) -or ($Solo -contains $_.id) })

$ficheroEstado = Join-Path $Carpeta 'estado.json'
$estado = @{}
if (Test-Path $ficheroEstado) { foreach ($p in ((Leer $ficheroEstado) | ConvertFrom-Json).PSObject.Properties) { $estado[$p.Name] = $p.Value } }

$errores = 0
$cuenta = @{ nuevo = 0; actualizado = 0; 'al día' = 0; pendiente = 0 }

foreach ($e in $entradas) {
    $id = $e.id
    $ant = $estado[$id]
    $ruta = Join-Path $Carpeta "$id.txt"
    $falta = -not (Test-Path $ruta)
    try {
        $lineasN = $null; $cab = @(); $desc = ''; $version = ''; $oficial = ''; $toca = $false; $detalle = ''

        if ($e.tipo -eq 'boe') {
            $meta = BoeMetadatos $e.boe
            $version = $meta['fecha_actualizacion']
            $oficial = $meta['url_eli']
            $toca = $Forzar -or $falta -or (-not $ant) -or ([string] $ant.version -ne $version)
            if ($ant -and $ant.version) { $detalle = '{0} -> {1}' -f $ant.version, $version }
            if ($toca -and -not $Comprobar) {
                $t = BoeATexto $e.boe
                $lineasN = $t.lineas
                $fechas = @($t.reformas.Values | Sort-Object)
                $reformas = @($t.reformas.GetEnumerator() | Sort-Object Value | ForEach-Object { '{0} ({1})' -f $_.Key, (FechaBoe $_.Value) })
                $desc = 'BOE actualizado el ' + (FechaBoe $version)
                if ($fechas.Count) { $desc += '; última reforma incorporada: ' + (FechaBoe $fechas[-1]) }
                $cab = @("# $($e.titulo)", "# Fuente: BOE, texto consolidado (API de datos abiertos), $($e.boe). Enlace oficial: $oficial", "# Versión: $desc")
                $cab += if ($reformas.Count) { '# Reformas incorporadas (norma que reforma y fecha de su publicación): ' + ($reformas -join '; ') } else { '# Reformas incorporadas: ninguna' }
                $cab += "# Descargado: $ahora"
                $cab += '# Aviso: texto consolidado; sirve para consultar, pero no es la versión oficial (la oficial es la publicada en el BOE y, en las leyes catalanas, en el DOGC).'
                if ($e.nota) { $cab += "# Nota: $($e.nota)" }
            }
        }
        elseif ($e.tipo -eq 'ue') {
            if ($e.modo -eq 'original') { $celex = '3' + $e.celex }
            else { $celex = UeUltimaConsolidada $e.celex; if (-not $celex) { $celex = '3' + $e.celex } }
            $version = $celex
            $oficial = 'https://eur-lex.europa.eu/legal-content/ES/TXT/?uri=CELEX:' + $celex
            $toca = $Forzar -or $falta -or (-not $ant) -or ([string] $ant.version -ne $version)
            if ($ant -and $ant.version) { $detalle = '{0} -> {1}' -f $ant.version, $version }
            if ($toca -and -not $Comprobar) {
                $usado = $celex; $aviso = ''
                try { $lineasN = @(UeATexto $usado) }
                catch {
                    if (-not $celex.StartsWith('0')) { throw }
                    # La base de datos anuncia una versión consolidada que no se sirve: se usa el texto original, sin reformas posteriores.
                    $usado = '3' + $e.celex
                    $oficial = 'https://eur-lex.europa.eu/legal-content/ES/TXT/?uri=CELEX:' + $usado
                    $aviso ="la versión consolidada $celex figura en la base de datos pero no se pudo bajar; este es el texto original del Diario Oficial y puede no incluir reformas posteriores"
                    $lineasN = @(UeATexto $usado)
                }
                if ($usado -match '^0.+-(\d{4})(\d{2})(\d{2})$') { $desc = 'Consolidada a {0}-{1}-{2} (CELEX {3})' -f $Matches[1], $Matches[2], $Matches[3], $usado }
                else { $desc = "Texto del Diario Oficial (CELEX $usado)" }
                if ($e.parte -eq 'considerandos') {
                    # El texto consolidado no trae los considerandos (el preámbulo): se sacan del texto original, de «(1)» a «HAN ADOPTADO».
                    $ini = -1; $fin = -1
                    for ($i = 0; $i -lt $lineasN.Count; $i++) {
                        if ($ini -lt 0 -and $lineasN[$i] -match '^\(1\) ') { $ini = $i }
                        elseif ($ini -ge 0 -and $lineasN[$i] -match '^HAN ADOPTADO') { $fin = $i; break }
                    }
                    if ($ini -lt 0 -or $fin -lt 0) { throw 'no se encuentran los considerandos (de «(1)» a «HAN ADOPTADO»)' }
                    $lineasN = $lineasN[$ini..($fin - 1)]
                    $desc = "Considerandos del texto original del Diario Oficial (CELEX $celex)"
                }
                $cab = @("# $($e.titulo)", "# Fuente: Oficina de Publicaciones de la UE, https://publications.europa.eu/resource/celex/$usado . Enlace oficial: $oficial", "# Versión: $desc", "# Descargado: $ahora")
                if ($aviso) { $cab += "# ATENCIÓN: $aviso." }
                if ($usado.StartsWith('0')) { $cab += '# Aviso: versión consolidada (las marcas ►M y ►C señalan lo modificado o rectificado; ▼B vuelve al texto original); sirve para consultar, pero no es la versión oficial, que es la del Diario Oficial de la UE. No incluye los considerandos: están en el archivo «-considerandos» de la misma norma.' }
                if ($e.nota) { $cab += "# Nota: $($e.nota)" }
            }
        }
        elseif ($e.tipo -eq 'pdf') {
            $oficial = $e.url
            $max = 60; if ($e.maxEdadDias) { $max = [int] $e.maxEdadDias }
            $edad = 99999; $dt = [datetime]::MinValue
            if ($ant -and $ant.comprobado -and [datetime]::TryParse([string] $ant.comprobado, $inv, [Globalization.DateTimeStyles]::None, [ref] $dt)) { $edad = ($hoy - $dt).TotalDays }
            $toca = $Forzar -or $falta -or (-not $ant) -or ($edad -gt $max)
            if ($toca -and $ant -and -not $falta) { $detalle = 'comprobado hace {0:N0} días (se renueva a los {1})' -f $edad, $max }
            if ($toca -and -not $Comprobar) {
                $pdf = Join-Path $temporal "$id.pdf"
                Bajar $e.url $pdf
                $huella = (Get-FileHash $pdf -Algorithm SHA256).Hash.Substring(0, 12).ToLower()
                if ($Forzar -or $falta -or (-not $ant) -or ([string] $ant.version -ne $huella)) {   # si el PDF no ha cambiado, queda «al día»
                    $version = $huella
                    $lineasN = @(PdfATexto $pdf)
                    $desc = "PDF (huella $huella)"
                    $cab = @("# $($e.titulo)", "# Fuente: $($e.url)", "# Versión: $desc", "# Descargado: $ahora", '# Aviso: texto extraído de un PDF con pdftotext; las páginas van marcadas con [Página N]. Las tablas pueden perder el orden de las columnas: ante la duda, contrasta con el PDF.')
                    if ($e.nota) { $cab += "# Nota: $($e.nota)" }
                }
            }
        }
        else { throw "tipo desconocido: $($e.tipo)" }

        if ($Comprobar) {
            if (-not $toca) { $m = 'al día' }
            elseif ($falta -or -not $ant) { $m = 'falta (se baja al ejecutar sin -Comprobar)'; $cuenta.pendiente++ }
            else { $m = 'hay que renovarlo: ' + $detalle; $cuenta.pendiente++ }
        }
        elseif ($lineasN) {
            $n = Escribir $ruta $cab $lineasN
            $estado[$id] = [pscustomobject] @{ version = $version; descripcion = $desc; descargado = $ahora; comprobado = $ahora; archivo = "$id.txt"; lineas = $n; oficial = $oficial }
            if ($falta) { $m = "nuevo ($n líneas)"; $cuenta.nuevo++ } else { $m = "actualizado ($n líneas)"; $cuenta.actualizado++ }
        }
        else {
            $ant.comprobado = $ahora
            if ($ant.PSObject.Properties['error']) { $ant.error = '' }
            $m = 'al día'; $cuenta['al día']++
        }
        Write-Host ('{0,-48} {1}' -f $id, $m)
    }
    catch {
        $errores++
        $msg = ($_.Exception.Message -replace '\s+', ' ')
        Write-Host ('{0,-48} ERROR: {1}' -f $id, $msg)
        if (-not $Comprobar) {
            if ($ant) { Add-Member -InputObject $ant -NotePropertyName error -NotePropertyValue $msg -Force }
            else { $estado[$id] = [pscustomobject] @{ error = $msg } }
        }
    }
}

if (-not $Comprobar) {
    [IO.File]::WriteAllText($ficheroEstado, ($estado | ConvertTo-Json -Depth 4), $utf8)
    EscribirIndice
}
Remove-Item $temporal -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
$seg = ((Get-Date) - $hoy).TotalSeconds
if ($Comprobar) { Write-Host ('Comprobadas {0} entradas; pendientes de bajar o renovar: {1}. Hecho en {2:N0} s.' -f $entradas.Count, $cuenta.pendiente, $seg) }
else { Write-Host ('{0} nuevas, {1} actualizadas, {2} al día. Hecho en {3:N0} s. Índice: {4}' -f $cuenta.nuevo, $cuenta.actualizado, $cuenta['al día'], $seg, (Join-Path $Carpeta 'INDICE.md')) }
if ($errores) { Write-Host "$errores entrada(s) con error."; exit 1 }
