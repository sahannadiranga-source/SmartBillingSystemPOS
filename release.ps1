# Builds the release package of POSGardenia for another computer (a computer that does NOT have .NET installed).
#
#   How to use:   1. change <Version> in POSGardenia\POSGardenia.csproj (for example 1.0.0 -> 1.0.1)
#                 2. run     powershell -ExecutionPolicy Bypass -File release.ps1
#   You get:      ..\POSGardenia-releases\<version>\POSGardenia-<version>-win-x64.zip
#                 (POSGardenia.exe with .NET inside it, plus HOW-TO-INSTALL.txt)

param(
    [string]$OutRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'POSGardenia-releases')
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'POSGardenia\POSGardenia.csproj'

# the version comes from the project file, so the package name and the app always agree
[xml]$xml = Get-Content -LiteralPath $project -Raw
$version = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> found in POSGardenia.csproj.' }

$name       = "POSGardenia-$version-win-x64"
$releaseDir = Join-Path $OutRoot $version
$packageDir = Join-Path $releaseDir $name
$zipPath    = Join-Path $releaseDir "$name.zip"

Write-Host "Building POSGardenia $version ..."

if (Test-Path -LiteralPath $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $packageDir | Out-Null

# one exe that carries .NET and the SQLite library inside it
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $packageDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# only the program goes to the other computer
Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -ne 'POSGardenia.exe' } | Remove-Item -Force

$notes = @'
POSGardenia {VERSION}
=====================

FIRST INSTALL (a new computer)
1. Copy the folder "POSGardenia-{VERSION}-win-x64" to the computer, for example to C:\POSGardenia\
2. Double-click POSGardenia.exe. Nothing else has to be installed.
   For a desktop icon: right-click POSGardenia.exe > Show more options > Send to > Desktop (create shortcut).
3. The first time, the program asks you to create the administrator account.
4. Then open Settings: choose the receipt / kitchen printers and a backup folder (a USB drive is best).

BRING YOUR EXISTING DATA TO THE NEW COMPUTER
1. On the old computer, close POSGardenia.
2. Open this folder (type it in the Windows Explorer address bar):   %LOCALAPPDATA%\POSGardenia
3. Copy  posgardenia.db  (and settings.json if you want the same printers and folders)
   into the same folder on the new computer, BEFORE starting the program there.
   If the folder is not there yet, create it.

UPDATE TO A NEWER VERSION (a patch)
1. In the program: Settings > Backup Now.
2. Close POSGardenia.
3. Copy the new POSGardenia.exe over the old one.
4. Start it. Your data stays where it is and upgrades itself.

IF WINDOWS SAYS "Windows protected your PC"
The program is not signed. Click "More info", then "Run anyway".

Needs: Windows 10 or 11, 64-bit.
'@
Set-Content -LiteralPath (Join-Path $packageDir 'HOW-TO-INSTALL.txt') -Value $notes.Replace('{VERSION}', $version) -Encoding UTF8

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force

$exe = Get-Item -LiteralPath (Join-Path $packageDir 'POSGardenia.exe')
Write-Host ''
Write-Host ("Program : {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))
Write-Host ("Package : {0}  ({1:N1} MB)" -f $zipPath, ((Get-Item -LiteralPath $zipPath).Length / 1MB))
