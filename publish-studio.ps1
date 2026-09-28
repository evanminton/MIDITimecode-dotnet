# Publishes MTC Studio as a standalone Windows app: self-contained (the .NET runtime and Windows
# App SDK are bundled, the target PC needs nothing installed) and unpackaged (a plain .exe, no MSIX).
#
#   .\publish-studio.ps1                     # Release, win-x64: portable zip + Inno setup.exe
#   .\publish-studio.ps1 -Install            # ...then run the setup wizard
#   .\publish-studio.ps1 -NoInno             # portable zip only
#   .\publish-studio.ps1 -Runtime win-arm64  # other architecture
#   .\publish-studio.ps1 -Configuration Debug
#
# Output:
#   artifacts\studio\<rid>\app\                          MtcStudio.exe + runtime (run it in place)
#   artifacts\studio\<rid>\cli\                          mtc.exe (single file)
#   artifacts\MtcStudio-<version>-<rid>-portable.zip     unzip anywhere, run app\MtcStudio.exe
#   artifacts\MtcStudio-<version>-<rid>-setup.exe        Inno Setup installer (needs Inno Setup 6.3+)
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')][string]$Runtime = 'win-x64',
    [switch]$Install,
    [switch]$NoInno
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
$root    = Join-Path $PSScriptRoot "artifacts\studio\$Runtime"
$appOut  = Join-Path $root 'app'
$cliOut  = Join-Path $root 'cli'
$zip     = Join-Path $PSScriptRoot "artifacts\MtcStudio-$version-$Runtime-portable.zip"

if (Test-Path $root) { Remove-Item $root -Recurse -Force }

Write-Host "== publish MTC Studio ($Configuration, $Runtime)" -ForegroundColor Cyan
dotnet publish app/MtcStudio/MtcStudio.csproj -c $Configuration -r $Runtime --nologo `
    -p:WindowsPackageType=None `
    -p:SelfContained=true `
    -p:WindowsAppSDKSelfContained=true `
    -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'Publish failed: MtcStudio' }

Write-Host "== publish mtc CLI ($Configuration, $Runtime)" -ForegroundColor Cyan
dotnet publish tools/MidiTimecode.Cli/MidiTimecode.Cli.csproj -c $Configuration -r $Runtime --nologo `
    --self-contained true `
    -p:PackAsTool=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $cliOut
if ($LASTEXITCODE -ne 0) { throw 'Publish failed: mtc CLI' }

Set-Content (Join-Path $root 'version.txt') $version
New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $root '*') -DestinationPath $zip
Write-Host "Portable: $zip" -ForegroundColor Green

$setup = $null
if (-not $NoInno) {
    $iscc = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        Write-Warning "Inno Setup 6 not found; skipping setup.exe. Install it with: winget install -e --id JRSoftware.InnoSetup"
    } else {
        Write-Host "== Inno Setup ($iscc)" -ForegroundColor Cyan
        & $iscc /Q installer\MtcStudio.iss "/DAppVersion=$version" "/DRid=$Runtime" "/DSourceDir=$root" "/DOutDir=$(Join-Path $PSScriptRoot 'artifacts')"
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed' }
        $setup = Join-Path $PSScriptRoot "artifacts\MtcStudio-$version-$Runtime-setup.exe"
        Write-Host "Installer: $setup" -ForegroundColor Green
    }
}

if ($Install) {
    if ($setup) { Start-Process $setup -Wait }
    else { Write-Warning 'No setup.exe to run; use the portable zip instead.' }
}
