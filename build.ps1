<#
.SYNOPSIS
    Compila VHD ISO Studio in un exe standalone (self-contained, file singolo).

.DESCRIPTION
    1. verifica/installa il .NET 10 SDK (winget, previa conferma)
    2. legge la versione da src\VhdIsoStudio\VhdIsoStudio.csproj (<Version>)
    3. esegue i test unitari (salvo -SkipTests)
    4. dotnet publish per ogni runtime richiesto (win-x64, win-arm64)
    5. esegue l'exe con --selftest (richiede console elevata: l'app e' requireAdministrator)
    6. calcola SHA-256 e scrive dist\build.log

.PARAMETER Runtime
    Uno o piu' RID: win-x64 (predefinito), win-arm64.

.PARAMETER SkipTests
    Salta i test unitari.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-x64,win-arm64

.NOTES
    Versione script: 1.0.0 (2026-11-17)
    Codici di uscita: 0 ok, 1 SDK mancante, 2 test falliti, 3 publish fallito, 4 selftest fallito.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string[]]$Runtime = @('win-x64'),
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\VhdIsoStudio\VhdIsoStudio.csproj'
$tests = Join-Path $root 'tests\VhdIsoStudio.Tests\VhdIsoStudio.Tests.csproj'
$dist = Join-Path $root 'dist'
$log = Join-Path $dist 'build.log'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

New-Item -ItemType Directory -Force -Path $dist | Out-Null
function Write-Log([string]$m, [string]$c = 'Gray') {
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $m
    Write-Host $line -ForegroundColor $c
    Add-Content -Path $log -Value $line -Encoding UTF8
}

# --- 1. .NET 10 SDK
$hasSdk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdks = & dotnet --list-sdks 2>$null
    $hasSdk = [bool]($sdks | Where-Object { $_ -match '^10\.' })
}
if (-not $hasSdk) {
    Write-Log '.NET 10 SDK non trovato.' 'Yellow'
    if ((Get-Command winget -ErrorAction SilentlyContinue) -and
        ((Read-Host 'Installarlo ora con winget (Microsoft.DotNet.SDK.10)? [S/n]') -notmatch '^[nN]')) {
        winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
        $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
    } else {
        Write-Log 'Installa il .NET 10 SDK da https://dotnet.microsoft.com/download e riprova.' 'Red'
        exit 1
    }
}

# --- 2. versione
$version = '0.0.0'
if ((Get-Content -Path $proj -Raw -Encoding UTF8) -match '<Version>([^<]+)</Version>') { $version = $Matches[1].Trim() }
Write-Log "VHD ISO Studio $version — runtime: $($Runtime -join ', ')" 'Cyan'

# --- 3. test
if (-not $SkipTests) {
    Write-Log 'Test unitari...'
    & dotnet test $tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Write-Log 'Test falliti.' 'Red'; exit 2 }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

foreach ($rid in $Runtime) {
    # --- 4. publish
    $out = Join-Path $dist $rid
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Write-Log "Publish $rid..."
    & dotnet publish $proj -c Release -r $rid --self-contained true -o $out --nologo
    if ($LASTEXITCODE -ne 0) { Write-Log "Publish $rid fallito." 'Red'; exit 3 }

    $exe = Join-Path $out 'VhdIsoStudio.exe'
    $named = Join-Path $dist ("VhdIsoStudio_{0}_{1}.exe" -f $version, $rid)
    Copy-Item $exe $named -Force

    # --- 5. selftest (solo x64 su host x64, richiede elevazione)
    $hostArm = $env:PROCESSOR_ARCHITECTURE -eq 'ARM64'
    if (($rid -eq 'win-x64') -or ($rid -eq 'win-arm64' -and $hostArm)) {
        if ($isAdmin) {
            $p = Start-Process -FilePath $named -ArgumentList '--selftest' -Wait -PassThru
            if ($p.ExitCode -ne 0) { Write-Log "Selftest $rid fallito (codice $($p.ExitCode))." 'Red'; exit 4 }
            Write-Log "Selftest $rid OK." 'Green'
        } else {
            Write-Log 'Selftest saltato: esegui build.ps1 da una console elevata per verificarlo.' 'Yellow'
        }
    }

    # --- 6. hash
    $hash = (Get-FileHash $named -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$named.sha256" -Value "$hash *$(Split-Path $named -Leaf)" -Encoding ASCII
    Write-Log ("{0}  {1:N1} MB  SHA256 {2}" -f (Split-Path $named -Leaf), ((Get-Item $named).Length / 1MB), $hash) 'Green'
}
Write-Log "Build completata: $dist" 'Green'
exit 0
