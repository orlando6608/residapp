<#
.SYNOPSIS
    Baja (o sube) las respuestas que CJ ha guardado en el servidor de desarrollo.

.DESCRIPTION
    Las respuestas de CJ viven en el servidor (un fichero JSON por documento), no en el repositorio, que es público.
    - Bajar (por defecto): deja en la carpeta local (respuestas-cj-local\, ignorada por git) un <documento>.respuestas.json
      por cada documento que CJ haya respondido. Es lo que lee Claude en la sesión de análisis, y sirve de copia de seguridad.
    - Subir: manda al servidor los .respuestas.json de la carpeta local. No pisa lo que ya haya en el servidor, salvo con -Forzar.
      Solo hace falta para cargar respuestas antiguas que no estén en el servidor.
    Los documentos son los que tienen un <documento>.notas.json en docs\pendientes-cj\.
    Entra con la clave de los documentos de CJ: si no la pasas en -Clave ni en la variable RESIDAPP_CLAVE_CJ, te la pide (no se guarda).

.EXAMPLE
    .\tools\respuestas-cj.ps1
    .\tools\respuestas-cj.ps1 -Accion Subir
    .\tools\respuestas-cj.ps1 -Url http://localhost:5203
#>
param(
    [ValidateSet('Bajar', 'Subir')] [string] $Accion = 'Bajar',
    [string] $Url = 'https://dev.residapp.es',
    [string] $Carpeta = (Join-Path $PSScriptRoot '..\respuestas-cj-local'),
    [string] $Clave = $env:RESIDAPP_CLAVE_CJ,
    [switch] $Forzar
)

$ErrorActionPreference = 'Stop'
$Url = $Url.TrimEnd('/')
$utf8 = New-Object System.Text.UTF8Encoding $false
$documentos = Get-ChildItem (Join-Path $PSScriptRoot '..\docs\pendientes-cj') -Filter '*.notas.json' |
    ForEach-Object { $_.Name -replace '\.notas\.json$', '' } | Sort-Object

if (-not $Clave) {
    $segura = Read-Host 'Clave de los documentos de CJ' -AsSecureString
    $Clave = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura))
}

# Entrada, como hace el navegador: formulario con su token y la cookie de acceso.
$formulario = Invoke-WebRequest "$Url/AccesoCj" -SessionVariable sesion -UseBasicParsing
$token = [regex]::Match($formulario.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]*)"').Groups[1].Value
$null = Invoke-WebRequest "$Url/AccesoCj" -Method Post -WebSession $sesion -UseBasicParsing `
    -Body @{ __RequestVerificationToken = $token; clave = $Clave }
if (-not $sesion.Cookies.GetCookies($Url)['residapp_cj_acceso']) { throw 'La clave no es correcta.' }

function Leer([string] $documento) {
    # 200 con el JSON, o 204 si CJ aún no ha guardado nada.
    $r = Invoke-WebRequest "$Url/pendientes-cj/api/respuestas/$documento" -WebSession $sesion -UseBasicParsing
    if ($r.StatusCode -eq 200) { return $r.Content }
    return $null
}

New-Item -ItemType Directory -Force $Carpeta | Out-Null

if ($Accion -eq 'Bajar') {
    foreach ($documento in $documentos) {
        $json = Leer $documento
        if (-not $json) { Write-Host ('{0,-40} sin respuestas' -f $documento); continue }
        $datos = $json | ConvertFrom-Json
        [IO.File]::WriteAllText((Join-Path $Carpeta "$documento.respuestas.json"), $json, $utf8)
        Write-Host ('{0,-40} {1} de {2} respondidas, guardado el {3}' -f $documento, $datos.respondidas, $datos.total, $datos.guardado)
    }
    Write-Host "Respuestas en $((Resolve-Path $Carpeta).Path)"
}
else {
    foreach ($fichero in Get-ChildItem $Carpeta -Filter '*.respuestas.json') {
        $documento = $fichero.Name -replace '\.respuestas\.json$', ''
        if ($documentos -notcontains $documento) { Write-Host ('{0,-40} no es un documento activo, no se sube' -f $documento); continue }
        if ((Leer $documento) -and -not $Forzar) { Write-Host ('{0,-40} ya hay respuestas en el servidor, no se pisan (usa -Forzar)' -f $documento); continue }
        $cuerpo = $utf8.GetBytes([IO.File]::ReadAllText($fichero.FullName, $utf8))
        $null = Invoke-WebRequest "$Url/pendientes-cj/api/respuestas/$documento" -Method Put -WebSession $sesion -UseBasicParsing `
            -ContentType 'application/json; charset=utf-8' -Headers @{ 'X-Sin-Aviso' = '1' } -Body $cuerpo
        Write-Host ('{0,-40} subido' -f $documento)
    }
}
