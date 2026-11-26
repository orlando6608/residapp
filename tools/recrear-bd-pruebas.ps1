<#
.SYNOPSIS
    Recrea desde cero la base local de las pruebas (ResidAppPruebas).

.DESCRIPTION
    Cada pasada de la suite añade centros, cuentas y auditoría a la base de las pruebas; con muchas pasadas la suite se vuelve lenta y da
    tiempos de espera. Este script la borra, la crea vacía, le aplica database\scripts\ (los de la rama en la que estés) y la marca como
    base de desarrollo. Se pierde todo lo que haya en ella; con -Copia se guarda antes una copia en la carpeta de copias de SQL Server.
    No fuerza nada: se niega si hay pruebas en marcha o alguien conectado a la base. Solo admite bases ResidAppPruebas* o ResidAppCi* de la
    instancia local, nunca la de la aplicación. Necesita sqlcmd y Git para Windows (usa su bash para database\aplicar-scripts.sh).

.EXAMPLE
    .\tools\recrear-bd-pruebas.ps1
    .\tools\recrear-bd-pruebas.ps1 -Copia
    .\tools\recrear-bd-pruebas.ps1 -Confirm:$false
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidatePattern('^ResidApp(Pruebas|Ci)\w*$')] [string] $Base = 'ResidAppPruebas',
    [switch] $Copia
)

$ErrorActionPreference = 'Stop'
$servidor = 'ACER-ORLANDO'
$repo = Split-Path $PSScriptRoot

function Invoke-Sql([string] $baseDatos) {
    # El resto de argumentos (-Q consulta o -i fichero) pasa tal cual a sqlcmd.
    $salida = & sqlcmd -S $servidor -d $baseDatos -E -C -b -I -f 65001 -h -1 -W @args
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd ha fallado: $($salida -join ' ')" }
    $salida
}

function Assert-SinPruebas {
    if (Get-Process testhost -ErrorAction SilentlyContinue) { throw 'Hay pruebas en marcha (proceso testhost). Espera a que terminen.' }
}

# En PowerShell, «bash» a secas suele ser el de WSL: hay que usar el de Git. Se comprueba antes de borrar nada.
$bash = Join-Path (Split-Path (Split-Path (Get-Command git).Source)) 'bin\bash.exe'
if (-not (Test-Path $bash)) { throw "No encuentro el bash de Git en $bash." }
Assert-SinPruebas

$sinConexiones = "IF EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE is_user_process = 1 AND database_id = DB_ID(N'$Base')) " +
    "THROW 50000, N'Hay conexiones abiertas a la base: cierra la app, el editor de SQL u otra sesion.', 1;"
$existe = (Invoke-Sql master -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$Base';") -eq '1'
if ($existe) { Invoke-Sql master -Q $sinConexiones }

if (-not $PSCmdlet.ShouldProcess("$servidor, base $Base", 'Borrar la base de pruebas con todos sus datos y crearla de nuevo')) { return }

if ($existe) {
    if ($Copia) {
        # Un nombre sin ruta deja el fichero en la carpeta de copias de la instancia.
        $fichero = "$Base-$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
        $null = Invoke-Sql master -Q "BACKUP DATABASE [$Base] TO DISK = N'$fichero' WITH COPY_ONLY, COMPRESSION, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$fichero' WITH CHECKSUM;"
        Write-Host "Copia guardada y verificada: $fichero (carpeta de copias de SQL Server)."
    }
    Assert-SinPruebas
    Invoke-Sql master -Q "$sinConexiones DROP DATABASE [$Base];"
}
Invoke-Sql master -Q "CREATE DATABASE [$Base];"

Write-Host 'Aplicando los scripts...'
# SQL_USER y APLICAR_SEED se vacían: autenticación de Windows y sin datos de ejemplo, que las pruebas no usan.
$variables = @{ SQL_SERVER = $servidor; SQL_DATABASE = $Base; SQLCMD_EXTRA = '-C'; SQL_USER = $null; APLICAR_SEED = $null }
$anteriores = @{}
foreach ($nombre in $variables.Keys) {
    $anteriores[$nombre] = [Environment]::GetEnvironmentVariable($nombre)
    [Environment]::SetEnvironmentVariable($nombre, $variables[$nombre])
}
Push-Location $repo
try {
    $registro = & $bash database/aplicar-scripts.sh
    if ($LASTEXITCODE -ne 0) {
        $registro | Select-Object -Last 20 | Write-Host
        throw "Ha fallado la aplicación de los scripts: $Base ha quedado a medias. Corrige la causa y vuelve a ejecutar este script."
    }
}
finally {
    Pop-Location
    foreach ($nombre in $anteriores.Keys) { [Environment]::SetEnvironmentVariable($nombre, $anteriores[$nombre]) }
}

# Sin la marca de entorno, las pruebas de los módulos opcionales fallan.
$null = Invoke-Sql $Base -i (Join-Path $repo 'database\seed\entorno_desarrollo.sql')

$aplicados = [int](Invoke-Sql $Base -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.scripts_aplicados;')
$esperados = @(Get-ChildItem (Join-Path $repo 'database\scripts') -Filter '*.sql').Count
if ($aplicados -ne $esperados) { throw "Hay $esperados scripts en el repositorio y la base registra $aplicados." }
Write-Host "$Base recreada: $aplicados scripts aplicados y marcada como base de desarrollo."
