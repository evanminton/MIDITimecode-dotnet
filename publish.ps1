# Publishes a standalone (self-contained, no .NET / Windows App SDK install needed) Windows build
# of MTC Explorer plus the `mtc` command-line utility, and packages them with an installer.
#
#   .\publish.ps1                     # Release, win-x64, builds zip + Inno setup.exe, then runs the installer
#   .\publish.ps1 -NoInstall          # only build the zip and setup.exe
#   .\publish.ps1 -NoInno             # skip Inno Setup (zip + Install.ps1 only)
#   .\publish.ps1 -Runtime win-arm64  # other architecture
#   .\publish.ps1 -Configuration Debug
#
# Output:
#   artifacts\publish\<rid>\app\    MTC Explorer (MtcExplorer.exe + runtime)
#   artifacts\publish\<rid>\cli\    mtc.exe (single file)
#   artifacts\MtcExplorer-<version>-<rid>.zip        (unzip anywhere, run Install.cmd)
#   artifacts\MtcExplorer-<version>-<rid>-setup.exe  (Inno Setup installer; needs Inno Setup 6.3+)
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')][string]$Runtime = 'win-x64',
    [switch]$NoInstall,
    [switch]$NoInno
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$tfm     = 'net10.0-windows10.0.19041.0'
$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
$root    = Join-Path $PSScriptRoot "artifacts\publish\$Runtime"
$appOut  = Join-Path $root 'app'
$cliOut  = Join-Path $root 'cli'
$zip     = Join-Path $PSScriptRoot "artifacts\MtcExplorer-$version-$Runtime.zip"

if (Test-Path $root) { Remove-Item $root -Recurse -Force }

Write-Host "== publish MTC Explorer ($Configuration, $Runtime)" -ForegroundColor Cyan
dotnet publish samples/MtcExplorer/MtcExplorer.csproj -c $Configuration -f $tfm -r $Runtime --nologo `
    -p:EnableAndroid=false `
    -p:WindowsPackageType=None `
    -p:SelfContained=true `
    -p:WindowsAppSDKSelfContained=true `
    -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'Publish failed: MtcExplorer' }

Write-Host "== publish mtc CLI ($Configuration, $Runtime)" -ForegroundColor Cyan
dotnet publish tools/MidiTimecode.Cli/MidiTimecode.Cli.csproj -c $Configuration -r $Runtime --nologo `
    --self-contained true `
    -p:PackAsTool=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $cliOut
if ($LASTEXITCODE -ne 0) { throw 'Publish failed: mtc CLI' }

Copy-Item installer\Install.ps1, installer\Uninstall.ps1, installer\Install.cmd $root
Set-Content (Join-Path $root 'version.txt') $version

New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $root '*') -DestinationPath $zip
Write-Host "Package: $zip" -ForegroundColor Green

# Inno Setup installer (artifacts\MtcExplorer-<version>-<rid>-setup.exe)
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
        & $iscc /Q installer\MtcExplorer.iss "/DAppVersion=$version" "/DRid=$Runtime" "/DSourceDir=$root" "/DOutDir=$(Join-Path $PSScriptRoot 'artifacts')"
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed' }
        $setup = Join-Path $PSScriptRoot "artifacts\MtcExplorer-$version-$Runtime-setup.exe"
        Write-Host "Installer: $setup" -ForegroundColor Green
    }
}

if (-not $NoInstall) {
    if ($setup) { Start-Process $setup -Wait }      # interactive Inno wizard
    else { & (Join-Path $root 'Install.ps1') }       # script install fallback
}
