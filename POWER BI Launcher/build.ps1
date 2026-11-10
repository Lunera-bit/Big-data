$ErrorActionPreference = 'Stop'
$sources = @(
    (Join-Path $PSScriptRoot 'Dashboard.cs'),
    (Join-Path $PSScriptRoot 'ReportForm.cs'),
    (Join-Path $PSScriptRoot 'ReportCanvas.cs')
)
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot 'POWER BI Launcher.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw "No encuentro el compilador de C#: $compiler" }
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /out:"$output" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Data.dll /reference:System.Core.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el lanzador.' }
Write-Host "Ejecutable creado: $output"
