# Removes MTC Explorer installed by Install.ps1 (current user).
param([switch]$Quiet)
$ErrorActionPreference = 'Continue'
$InstallDir = $PSScriptRoot
$cliDir = Join-Path $InstallDir 'cli'

if (-not $Quiet) {
    $answer = Read-Host "Remove MTC Explorer from $InstallDir ? [y/N]"
    if ($answer -notmatch '^[yY]') { return }
}

Get-Process MtcExplorer -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir*" } | Stop-Process -Force

foreach ($l in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'MTC Explorer.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'MTC Explorer.lnk'))) {
    if (Test-Path $l) { Remove-Item $l -Force }
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$parts = @($userPath -split ';' | Where-Object { $_ -and $_ -ne $cliDir })
[Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')

Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MtcExplorer' -Recurse -Force -ErrorAction SilentlyContinue

Set-Location $env:TEMP
Remove-Item $InstallDir -Recurse -Force
Write-Host 'MTC Explorer removed.'
