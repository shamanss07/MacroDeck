param([string]$OutputName = 'MacroDeck.exe')
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$outputPath = Join-Path (Split-Path -Parent $sourceRoot) $OutputName
$windowsDirectory = [Environment]::GetFolderPath('Windows')
$compilerCandidates = @(
    (Join-Path $windowsDirectory 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $windowsDirectory 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'O compilador do .NET Framework não foi encontrado. Abra MacroDeck.csproj no Visual Studio ou instale o .NET Framework Developer Pack.' }
$sourceFiles = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName
$fontPath = Join-Path $sourceRoot 'Assets\Fonts\Montserrat.ttf'
$licensePath = Join-Path $sourceRoot 'Assets\Fonts\OFL.txt'
$iconPath = Join-Path $sourceRoot 'Assets\Branding\MacroDeck.ico'
$manifestPath = Join-Path $sourceRoot 'app.manifest'
if (-not (Test-Path -LiteralPath $fontPath)) { throw 'A fonte Montserrat não foi encontrada em Assets\Fonts.' }
if (-not (Test-Path -LiteralPath $licensePath)) { throw 'A licença da fonte Montserrat não foi encontrada em Assets\Fonts.' }
if (-not (Test-Path -LiteralPath $iconPath)) { throw 'O ícone do MacroDeck não foi encontrado em Assets\Branding.' }
$compilerArgs = @('/nologo', '/target:winexe', '/platform:anycpu', "/out:$outputPath", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll') + $sourceFiles
$compilerArgs += "/resource:$fontPath,MacroDeck.Assets.Montserrat.ttf"
$compilerArgs += "/resource:$licensePath,MacroDeck.Assets.OFL.txt"
$compilerArgs += "/win32icon:$iconPath"
$compilerArgs += "/win32manifest:$manifestPath"
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath $licensePath -Destination (Join-Path (Split-Path -Parent $sourceRoot) 'OFL.txt') -Force
Write-Host "Compilado: $outputPath"
