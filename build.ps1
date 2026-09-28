# Builds and tests the solution in Debug and Release.
#   .\build.ps1              # both configurations, including the MAUI app
#   .\build.ps1 -Configuration Release
#   .\build.ps1 -NoApp       # skip the MAUI apps (samples/MtcExplorer, app/MtcStudio; no MAUI workload needed)
param(
    [ValidateSet('Debug', 'Release', 'Both')][string]$Configuration = 'Both',
    [switch]$NoApp
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$configs = if ($Configuration -eq 'Both') { @('Debug', 'Release') } else { @($Configuration) }
$projects = @('src/MidiTimecode/MidiTimecode.csproj', 'tools/MidiTimecode.Cli/MidiTimecode.Cli.csproj', 'tests/MidiTimecode.Tests/MidiTimecode.Tests.csproj')
if (-not $NoApp) {
    $projects += 'samples/MtcExplorer/MtcExplorer.csproj'
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { $projects += 'app/MtcStudio/MtcStudio.csproj' }   # Windows-only app
}

foreach ($c in $configs) {
    foreach ($p in $projects) {
        Write-Host "== build $p ($c)" -ForegroundColor Cyan
        dotnet build $p -c $c --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $p ($c)" }
    }
    Write-Host "== test ($c)" -ForegroundColor Cyan
    dotnet test tests/MidiTimecode.Tests/MidiTimecode.Tests.csproj -c $c --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($c)" }
}
Write-Host "All builds and tests passed: $($configs -join ', ')" -ForegroundColor Green
