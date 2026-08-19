<#
    Builds the distributable bundle:

        dist\ComNotify-Setup.exe          single-file per-user installer (app embedded inside)
        dist\ComNotify-<ver>-portable.zip  the bare exe for people who do not want an installer

    Runs build.ps1 first, so this is the only script you need for a release.

    Usage:  powershell -ExecutionPolicy Bypass -File build-installer.ps1 [-Run]
#>
[CmdletBinding()]
param(
    [switch] $Run,          # launch the finished installer when done
    [switch] $SkipApp,      # reuse bin\ComNotify.exe instead of rebuilding it
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
$bin = Join-Path $root 'bin'
$dist = Join-Path $root 'dist'
$appExe = Join-Path $bin 'ComNotify.exe'
$appIco = Join-Path $bin 'ComNotify.ico'
$setupExe = Join-Path $dist 'ComNotify-Setup.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'Could not find the .NET Framework C# compiler (csc.exe).' }

# ---- 1. the application ---------------------------------------------------
if (-not $SkipApp) {
    & (Join-Path $root 'build.ps1')
}
foreach ($f in @($appExe, $appIco)) {
    if (-not (Test-Path $f)) { throw "Missing $f - run build.ps1 first (or drop -SkipApp)." }
}

# ---- 2. the installer -----------------------------------------------------
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }
if (Test-Path $setupExe) { Remove-Item $setupExe -Force }

$sources = Get-ChildItem (Join-Path $root 'installer') -Filter *.cs | ForEach-Object { $_.FullName }
if (-not $sources) { throw 'No sources found under installer\.' }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    '/langversion:5'
    '/platform:anycpu'
    '/warn:3'
    '/codepage:65001'   # sources are UTF-8; without this csc reads them as ANSI
    "/out:$setupExe"
    "/win32icon:$appIco"
    "/win32manifest:$(Join-Path $root 'installer\setup.manifest')"
    "/resource:$appExe,ComNotify.Payload.App"
    "/resource:$appIco,ComNotify.Payload.Icon"
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
) + $sources

Write-Host 'Compiling installer...'
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc.exe failed with exit code $LASTEXITCODE" }

# ---- 3. portable zip ------------------------------------------------------
$zip = Join-Path $dist ("ComNotify-{0}-portable.zip" -f $Version)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $appExe, $appIco -DestinationPath $zip -CompressionLevel Optimal

Write-Host ''
Write-Host ("  {0}  ({1:N0} bytes)" -f $setupExe, (Get-Item $setupExe).Length) -ForegroundColor Green
Write-Host ("  {0}  ({1:N0} bytes)" -f $zip, (Get-Item $zip).Length) -ForegroundColor Green
Write-Host ''
Write-Host 'Installer switches:'
Write-Host '  ComNotify-Setup.exe                        interactive'
Write-Host '  ComNotify-Setup.exe /silent [/dir=<path>] [/desktop] [/autostart] [/launch] [/noshortcut]'
Write-Host '  ComNotify-Setup.exe /uninstall [/silent]'

if ($Run) { Start-Process $setupExe }
