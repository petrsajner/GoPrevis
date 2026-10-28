param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools\dotnet'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$previsGodot = Join-Path $PSScriptRoot '.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') build GoPrevis.csproj
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
& $previsGodot --headless --editor --path $PSScriptRoot --import --quit
if ($LASTEXITCODE -ne 0) { throw 'Godot asset import failed.' }
if ($Test) {
    & $previsGodot --headless --path $PSScriptRoot -- --smoke-test
    if ($LASTEXITCODE -ne 0) { throw 'Headless smoke checks failed.' }
}
$previsOutput = Join-Path $PSScriptRoot 'build\GoPrevis-0.21.1-Windows'
New-Item -ItemType Directory -Path $previsOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'output') -Force | Out-Null
$previsLog = Join-Path $PSScriptRoot 'output\export.log'
& $previsGodot --headless --path $PSScriptRoot --export-release 'Windows Desktop' *> $previsLog
if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath $previsLog -Pattern '^ERROR:' -Quiet)) { Get-Content -LiteralPath $previsLog -Tail 25; throw 'Windows export failed.' }
Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md', 'NAVRH-GOPREVIS.md' -Destination $previsOutput -Force
Copy-Item -LiteralPath 'docs\PORTABLE-README.md' -Destination (Join-Path $previsOutput 'README.md') -Force
Copy-Item -LiteralPath 'docs\GoPrevis-User-Guide-EN.pdf' -Destination $previsOutput -Force
$previsDocs = Join-Path $previsOutput 'docs'
New-Item -ItemType Directory -Path $previsDocs -Force | Out-Null
Copy-Item -LiteralPath 'docs\GoPrevis-User-Guide-EN.pdf', 'docs\ANIMATION-EDITOR.md' -Destination $previsDocs -Force
Copy-Item -LiteralPath 'docs\images' -Destination $previsDocs -Recurse -Force
Copy-Item -LiteralPath 'examples' -Destination $previsOutput -Recurse -Force
$previsBrandAssets=Join-Path $previsOutput 'assets'
New-Item -ItemType Directory -Path $previsBrandAssets -Force | Out-Null
Copy-Item -LiteralPath 'assets\goprevis.png', 'assets\goprevis.ico', 'assets\goprevis-icon-notes.md' -Destination $previsBrandAssets -Force
if (Test-Path -LiteralPath 'VALIDATION.md') { Copy-Item -LiteralPath 'VALIDATION.md' -Destination $previsOutput -Force }
Copy-Item -LiteralPath 'LICENSES' -Destination $previsOutput -Recurse -Force
Copy-Item -LiteralPath 'samples' -Destination $previsOutput -Recurse -Force
$previsContentNotices = Join-Path $previsOutput 'LICENSES\content'
New-Item -ItemType Directory -Path $previsContentNotices -Force | Out-Null
Copy-Item -LiteralPath 'assets\content\animals\SOURCE-LICENSE.txt' -Destination (Join-Path $previsContentNotices 'Quaternius-Animals.txt') -Force
Get-ChildItem -LiteralPath 'assets\content\props' -Filter '*-LICENSE.txt' | Copy-Item -Destination $previsContentNotices -Force
Copy-Item -LiteralPath 'assets\content\motions\human\READMEFIRST-cgspeed.txt' -Destination $previsContentNotices -Force
$previsCatalogOutput = Join-Path $previsOutput 'catalog'
New-Item -ItemType Directory -Path $previsCatalogOutput -Force | Out-Null
Get-ChildItem -LiteralPath 'assets\content\manifests' -Filter '*.json' | Copy-Item -Destination $previsCatalogOutput -Force
$previsVideoTools = Join-Path $previsOutput 'tools\ffmpeg'
New-Item -ItemType Directory -Path $previsVideoTools -Force | Out-Null
Get-ChildItem -LiteralPath '.tools\ffmpeg' -File | Copy-Item -Destination $previsVideoTools -Force
Copy-Item -LiteralPath 'tools\launchers\Open car chase demo.cmd' -Destination $previsOutput -Force
Copy-Item -LiteralPath 'tools\launchers\Open city street demo.cmd', 'tools\launchers\Open smooth orbit demo.cmd' -Destination $previsOutput -Force
Write-Output ('Built: ' + (Join-Path $previsOutput 'GoPrevis.exe'))
