<#
    Builds ComNotify.exe with the C# compiler that ships with the .NET Framework, so no SDK,
    Visual Studio or NuGet restore is required on the machine.

    Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Run]
#>
[CmdletBinding()]
param(
    [switch] $Run,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $OutDir) { $OutDir = Join-Path $root 'bin' }
$exe = Join-Path $OutDir 'ComNotify.exe'
$ico = Join-Path $OutDir 'ComNotify.ico'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "Could not find the .NET Framework C# compiler (csc.exe). Install the .NET Framework 4.x developer files or build with Visual Studio."
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# A running instance holds a lock on the exe.
Get-Process -Name ComNotify -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping running ComNotify (pid $($_.Id))..."
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 300
    if (-not $_.HasExited) { $_ | Stop-Process -Force }
    Start-Sleep -Milliseconds 200
}

Write-Host 'Generating icon...'
& (Join-Path $root 'tools\make-icon.ps1') -OutFile $ico

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
if (-not $sources) { throw 'No sources found under src\.' }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    '/langversion:5'
    '/platform:anycpu'
    '/warn:3'
    '/codepage:65001'   # sources are UTF-8; without this csc reads them as ANSI
    "/out:$exe"
    "/win32icon:$ico"
    "/win32manifest:$(Join-Path $root 'src\app.manifest')"
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
) + $sources

Write-Host 'Compiling...'
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc.exe failed with exit code $LASTEXITCODE" }

Write-Host ("Built {0} ({1:N0} bytes)" -f $exe, (Get-Item $exe).Length) -ForegroundColor Green

if ($Run) {
    Write-Host 'Starting ComNotify...'
    Start-Process $exe
}
