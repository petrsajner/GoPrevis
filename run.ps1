param([switch]$Headless, [switch]$SmokeTest)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$previsGodot = Join-Path $PSScriptRoot '.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools\dotnet'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$previsArguments = @('--path', $PSScriptRoot)
if ($Headless) { $previsArguments += '--headless' }
if ($SmokeTest) { $previsArguments += @('--', '--smoke-test') }
& $previsGodot @previsArguments
exit $LASTEXITCODE
