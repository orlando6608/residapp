<#
.SYNOPSIS
  Pasa a HTML los documentos de la sección «Documentos» de Plataforma (src\ResidApp.Web\Documentos\documentos.json).

.DESCRIPTION
  Cada documento tiene su fuente en Markdown (campo «fuente» del manifiesto, por ejemplo docs\seguridad\...md) y su página en
  src\ResidApp.Web\Documentos\<id>.html. La fuente manda: no se edita el HTML a mano. Cada página lleva la huella (SHA-256) del
  texto del que salió, y una prueba (DocumentosPlataformaTests) falla si se cambia el Markdown sin volver a ejecutar este script.

  La página se sirve desde la aplicación, no como fichero estático, así que cumple la política de contenido (CSP): sin tipografías de
  otros sitios y sin JavaScript escrito dentro de la página (el tema claro u oscuro lo pone /js/tema.js).

  Uso (desde la raíz del repositorio, en PowerShell 7 o superior):
      pwsh tools\documentos-a-html.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$carpeta = Join-Path $raiz 'src\ResidApp.Web\Documentos'
$manifiesto = Get-Content -Raw -Encoding utf8 (Join-Path $carpeta 'documentos.json') | ConvertFrom-Json
$utf8 = New-Object System.Text.UTF8Encoding($false)
$hoy = (Get-Date).ToString('yyyy-MM-dd')

function Get-Huella([string]$texto) {
    # El mismo cálculo que hace la prueba: saltos de línea normalizados a LF y UTF-8 sin BOM.
    $bytes = $utf8.GetBytes($texto.Replace("`r`n", "`n"))
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function ConvertTo-HtmlSeguro([string]$texto) {
    # Solo lo imprescindible: las letras con tilde van tal cual (la página es UTF-8).
    return $texto.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

function ConvertTo-TextoPlano([string]$html) {
    return [System.Net.WebUtility]::HtmlDecode(([regex]::Replace($html, '<[^>]+>', '')))
}

$estilo = @'
  :root {
    color-scheme: light;
    --bg: #f5f7f7; --surface: #ffffff; --ink: #1a2427; --muted: #55686c; --rule: #d3dcdd;
    --accent: #1b5f6b; --accent-soft: #e3eff0; --danger: #b3261e; --danger-bg: #fbeceb; --ok: #22703a; --ok-bg: #e7f4ea;
    --serif: Georgia, "Times New Roman", serif;
    --sans: "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    --mono: Consolas, "Courier New", monospace;
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-bs-theme="light"]) {
      color-scheme: dark;
      --bg: #101618; --surface: #172023; --ink: #e2eaeb; --muted: #9aafb2; --rule: #2a383b;
      --accent: #72c0cb; --accent-soft: #1b2e32; --danger: #f2b8b5; --danger-bg: #2c1716; --ok: #8fd3a2; --ok-bg: #15261a;
    }
  }
  :root[data-bs-theme="dark"] {
    color-scheme: dark;
    --bg: #101618; --surface: #172023; --ink: #e2eaeb; --muted: #9aafb2; --rule: #2a383b;
    --accent: #72c0cb; --accent-soft: #1b2e32; --danger: #f2b8b5; --danger-bg: #2c1716; --ok: #8fd3a2; --ok-bg: #15261a;
  }
  body { margin: 0; background: var(--bg); color: var(--ink); font-family: var(--sans); font-size: 16px; line-height: 1.6; padding-inline: 16px; }
  .page { max-width: 1040px; margin: 0 auto; padding-block: 32px 96px; display: flex; flex-direction: column; gap: 32px; }
  h1, h2, h3, h4 { font-family: var(--serif); font-weight: 600; text-wrap: balance; line-height: 1.25; margin: 0; }
  h1 { font-size: 2.1rem; }
  h2 { font-size: 1.5rem; margin-top: 16px; padding-top: 16px; border-top: 1px solid var(--rule); scroll-margin-top: 16px; }
  h3 { font-size: 1.15rem; margin-top: 8px; scroll-margin-top: 16px; }
  h4 { font-size: 1.02rem; margin-top: 4px; }
  p, ul, ol { margin: 0; }
  code { font-family: var(--mono); font-size: 0.86em; overflow-wrap: anywhere; }
  strong { font-weight: 600; }
  a { color: var(--accent); }
  header { display: flex; flex-direction: column; gap: 14px; }
  .marca { display: flex; align-items: center; gap: 12px; padding-bottom: 12px; border-bottom: 1px solid var(--rule); color: var(--muted); font-size: 0.95rem; }
  .marca img { width: 40px; height: 40px; flex: none; }
  .marca strong { color: var(--ink); }
  .back { font-size: 0.95rem; display: flex; flex-wrap: wrap; gap: 0 24px; }
  .back a { display: inline-flex; align-items: center; min-height: 48px; }
  .eyebrow { font-size: 0.78rem; letter-spacing: 0.08em; text-transform: uppercase; color: var(--accent); font-weight: 600; }
  .meta { color: var(--muted); font-size: 0.95rem; }
  .indice { display: flex; flex-wrap: wrap; gap: 8px; list-style: none; padding: 0; margin: 0; }
  .indice a { display: inline-flex; align-items: center; min-height: 44px; padding: 0 14px; border: 1px solid var(--rule); border-radius: 999px; background: var(--surface); text-decoration: none; font-size: 0.93rem; }
  .indice a:hover { border-color: var(--accent); }
  .documento { display: flex; flex-direction: column; gap: 14px; }
  .documento > p, .documento > ul, .documento > ol, .documento > blockquote { max-width: 80ch; }
  .documento ul, .documento ol { padding-left: 1.4em; display: flex; flex-direction: column; gap: 6px; }
  .documento li > ul, .documento li > ol { margin-top: 6px; }
  .documento blockquote { margin: 0; background: var(--surface); border: 1px solid var(--rule); border-left: 5px solid var(--accent); border-radius: 8px; padding: 12px 16px; display: flex; flex-direction: column; gap: 10px; }
  .documento hr { width: 100%; border: 0; border-top: 1px solid var(--rule); margin: 8px 0; }
  .tabla { overflow-x: auto; }
  table.datos { border-collapse: collapse; width: 100%; min-width: 640px; background: var(--surface); font-size: 0.95rem; }
  table.datos th, table.datos td { text-align: left; padding: 8px 12px; border: 1px solid var(--rule); vertical-align: top; }
  table.datos thead th { background: var(--accent-soft); font-weight: 600; }
  footer { color: var(--muted); font-size: 0.9rem; border-top: 1px solid var(--rule); padding-top: 16px; }
  @media (max-width: 640px) { h1 { font-size: 1.75rem; } }
  @media print {
    :root, :root:not([data-bs-theme="light"]), :root[data-bs-theme="dark"] {
      color-scheme: light;
      --bg: #ffffff; --surface: #ffffff; --ink: #1a2427; --muted: #55686c; --rule: #d3dcdd;
      --accent: #1b5f6b; --accent-soft: #e3eff0;
    }
    body { padding: 0; }
    .back, .indice { display: none; }
    .page { padding-block: 0 24px; max-width: none; }
    .tabla { overflow: visible; }
    table.datos { min-width: 0; }
    table.datos tr, .documento blockquote { break-inside: avoid; }
    h2, h3, h4 { break-after: avoid; }
  }
'@

$porFuente = @{}
foreach ($d in $manifiesto) { $porFuente[$d.fuente] = $d.id }

foreach ($d in $manifiesto) {
    $rutaFuente = Join-Path $raiz ($d.fuente -replace '/', '\')
    $texto = Get-Content -Raw -Encoding utf8 $rutaFuente
    $html = (ConvertFrom-Markdown -InputObject $texto).Html

    # El título de la página es el primer encabezado del documento; no se repite en el cuerpo.
    $h1 = [regex]::Match($html, '<h1[^>]*>(.*?)</h1>\s*')
    if (-not $h1.Success) { throw "$($d.fuente): no tiene un encabezado de primer nivel (# ...)." }
    $titulo = ConvertTo-TextoPlano $h1.Groups[1].Value
    $cuerpo = $html.Remove($h1.Index, $h1.Length)

    # Tablas con el estilo de las demás páginas y desplazables dentro de su caja.
    $cuerpo = $cuerpo.Replace('<table>', '<div class="tabla"><table class="datos">').Replace('</table>', '</table></div>')
    # Una ruta de otro documento publicado se vuelve un enlace a esa página; las demás rutas del repositorio quedan como texto.
    $cuerpo = [regex]::Replace($cuerpo, '<code>(docs/[^<]+\.md)</code>', {
        param($m)
        if ($porFuente.ContainsKey($m.Groups[1].Value)) { "<a href=`"/Plataforma/Documento/$($porFuente[$m.Groups[1].Value])`">$($m.Value)</a>" } else { $m.Value }
    })
    # Los enlaces a otros sitios no pasan datos de esta página.
    $cuerpo = [regex]::Replace($cuerpo, '<a href="(https?://[^"]+)"', '<a href="$1" rel="noopener noreferrer"')

    $indice = [regex]::Matches($cuerpo, '<h2 id="([^"]+)">(.*?)</h2>') | ForEach-Object {
        '        <li><a href="#' + $_.Groups[1].Value + '">' + (ConvertTo-HtmlSeguro (ConvertTo-TextoPlano $_.Groups[2].Value)) + '</a></li>'
    }
    $bloqueIndice = if ($indice) { "    <nav aria-label=`"En esta página`">`n      <ul class=`"indice`">`n$($indice -join "`n")`n      </ul>`n    </nav>`n" } else { '' }

    $tituloHtml = ConvertTo-HtmlSeguro ($titulo)
    $temaHtml = ConvertTo-HtmlSeguro ($d.tema)
    $paraQuienHtml = ConvertTo-HtmlSeguro ($d.paraQuien)
    $huella = Get-Huella $texto

    $pagina = @"
<!doctype html>
<!--
  Generado por tools\documentos-a-html.ps1 a partir de $($d.fuente). No se edita a mano: se cambia el Markdown y se vuelve a ejecutar el script.
  Se sirve solo a Plataforma desde /Plataforma/Documento/$($d.id) (no es un fichero estático público).
-->
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$tituloHtml - ResidApp</title>
<meta name="robots" content="noindex, nofollow">
<meta name="fuente" content="$($d.fuente)">
<meta name="fuente-sha256" content="$huella">
<link rel="icon" href="/images/logo.svg">
<script src="/js/tema.js"></script>
<style>
$estilo
</style>
</head>
<body>
<div class="page">
  <header>
    <div class="marca">
      <img src="/images/logo.svg" alt="" width="40" height="40">
      <span><strong>ResidApp</strong> · Plataforma Asistencial Geriátrica</span>
    </div>
    <nav class="back" aria-label="Navegación">
      <a href="/Plataforma/Documentos">← Volver a Documentos</a>
      <a href="/">Inicio</a>
    </nav>
    <p class="eyebrow">ResidApp · Documentos · $temaHtml</p>
    <h1>$tituloHtml</h1>
    <p class="meta">Para: $paraQuienHtml · Generado el $hoy desde <code>$($d.fuente)</code></p>
$bloqueIndice  </header>
  <main>
    <article class="documento">
$cuerpo
    </article>
  </main>
  <footer>
    Documento de trabajo para Plataforma, no para el centro ni para las familias. Esta página se genera del fichero <code>$($d.fuente)</code>; si hay que cambiar algo, se cambia allí.
  </footer>
</div>
</body>
</html>
"@

    $destino = Join-Path $carpeta "$($d.id).html"
    [System.IO.File]::WriteAllText($destino, $pagina.Replace("`r`n", "`n"), $utf8)
    Write-Host "OK  $($d.id).html  ($($cuerpo.Length) caracteres de cuerpo)"
}
