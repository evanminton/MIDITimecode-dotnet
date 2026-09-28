# Removes MTC Explorer installed by Install.ps1 (current user).
param([switch]$Quiet)
$ErrorActionPreference = 'Continue'
$InstallDir = $PSScriptRoot
$cliDir = Join-Path $InstallDir 'cli'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MtcExplorer'

# Only act on the folder Install.ps1 registered, so running this from an unzipped package (or a
# stray copy) does not delete that folder or remove the real install's shortcuts and entry.
function Get-NormalizedPath([string]$p) {
    try {
        $full = [IO.Path]::GetFullPath(($p -replace '/', '\'))
        # Keep the backslash on a drive root: 'D:' alone means D's current folder.
        if ($full -eq [IO.Path]::GetPathRoot($full)) { $full } else { $full.TrimEnd('\') }
    } catch { $p }
}
$registered = (Get-ItemProperty $key -ErrorAction SilentlyContinue).InstallLocation
if (-not $registered -or ((Get-NormalizedPath $registered) -ne (Get-NormalizedPath $InstallDir))) {
    Write-Warning "MTC Explorer is not registered as installed in $InstallDir; nothing removed."
    return
}

if (-not $Quiet) {
    $answer = Read-Host "Remove MTC Explorer from $InstallDir ? [y/N]"
    if ($answer -notmatch '^[yY]') { return }
}

Get-Process MtcExplorer -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\*" } |
    ForEach-Object { $_ | Stop-Process -Force; $_ | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue }

foreach ($l in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'MTC Explorer.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'MTC Explorer.lnk'))) {
    if (Test-Path $l) { Remove-Item $l -Force }
}

# Edit the raw (unexpanded) user PATH so entries like %USERPROFILE%\... stay expandable, and only
# write it back if our folder was actually on it.
$envKey = Get-Item 'HKCU:\Environment'
$userPath = $envKey.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
$parts = @($userPath -split ';' | Where-Object { $_ })
$kept = @($parts | Where-Object { $_ -ne $cliDir })
if ($kept.Count -ne $parts.Count) {
    Set-ItemProperty 'HKCU:\Environment' Path ($kept -join ';') -Type ExpandString
    [Environment]::SetEnvironmentVariable('MTC_EXPLORER_PATH_REFRESH', $null, 'User')   # broadcasts WM_SETTINGCHANGE
}

# Remove only what Install.ps1 put there, then the folder if nothing else is left in it.
Set-Location $env:TEMP
$failed = $false
foreach ($item in 'app', 'cli', 'version.txt') {
    $p = Join-Path $InstallDir $item
    if (Test-Path $p) {
        Remove-Item $p -Recurse -Force
        if (Test-Path $p) { $failed = $true }
    }
}
if ($failed) {
    Write-Warning "Some files in $InstallDir could not be removed (in use?). Close MTC Explorer and run Uninstall again."
    return
}
Remove-Item (Join-Path $InstallDir 'Uninstall.ps1') -Force   # kept until now so a failed run can be retried
if (-not (Get-ChildItem $InstallDir -Force -ErrorAction SilentlyContinue)) { Remove-Item $InstallDir -Force }

Remove-Item $key -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'MTC Explorer removed.'
