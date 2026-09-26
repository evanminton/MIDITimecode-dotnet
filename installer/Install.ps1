# Installs MTC Explorer and the `mtc` CLI for the current user (no admin rights needed).
#   - Files:        %LOCALAPPDATA%\Programs\MTC Explorer
#   - Start menu:   MTC Explorer
#   - PATH:         <install>\cli added to the user PATH (skip with -NoPath)
#   - Uninstall:    Settings > Apps > MTC Explorer, or Uninstall.ps1 in the install folder
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\MTC Explorer'),
    [switch]$NoPath,
    [switch]$Desktop,     # also put a shortcut on the desktop
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
$src     = $PSScriptRoot
$version = if (Test-Path "$src\version.txt") { (Get-Content "$src\version.txt").Trim() } else { '1.0.0' }

if (-not (Test-Path "$src\app\MtcExplorer.exe")) { throw "app\MtcExplorer.exe not found next to Install.ps1" }

# Close a running copy so files can be replaced.
Get-Process MtcExplorer -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\*" } |
    ForEach-Object { $_ | Stop-Process -Force; $_ | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue }

Write-Host "Installing MTC Explorer $version to $InstallDir"
if (Test-Path "$InstallDir\app") { Remove-Item "$InstallDir\app" -Recurse -Force }
if (Test-Path "$InstallDir\cli") { Remove-Item "$InstallDir\cli" -Recurse -Force }
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item "$src\app", "$src\cli" $InstallDir -Recurse -Force
Copy-Item "$src\Uninstall.ps1", "$src\version.txt" $InstallDir -Force -ErrorAction SilentlyContinue

$exe = Join-Path $InstallDir 'app\MtcExplorer.exe'

# Shortcuts
$shell = New-Object -ComObject WScript.Shell
$links = @(Join-Path ([Environment]::GetFolderPath('Programs')) 'MTC Explorer.lnk')
if ($Desktop) { $links += Join-Path ([Environment]::GetFolderPath('Desktop')) 'MTC Explorer.lnk' }
foreach ($l in $links) {
    $s = $shell.CreateShortcut($l)
    $s.TargetPath = $exe
    $s.WorkingDirectory = Split-Path $exe
    $s.IconLocation = "$exe,0"
    $s.Description = 'MIDI Time Code explorer'
    $s.Save()
}

# CLI on the user PATH
$cliDir = Join-Path $InstallDir 'cli'
if (-not $NoPath) {
    # Raw (unexpanded) value, written back as REG_EXPAND_SZ so %VAR% entries keep working.
    $userPath = (Get-Item 'HKCU:\Environment').GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    $parts = @($userPath -split ';' | Where-Object { $_ })
    if ($parts -notcontains $cliDir) {
        Set-ItemProperty 'HKCU:\Environment' Path (($parts + $cliDir) -join ';') -Type ExpandString
        # Setting the registry directly does not broadcast WM_SETTINGCHANGE; this call does (no-op value change).
        [Environment]::SetEnvironmentVariable('MTC_EXPLORER_PATH_REFRESH', $null, 'User')
        Write-Host "Added $cliDir to your PATH (open a new terminal to use 'mtc')."
    }
}

# Apps & features entry
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MtcExplorer'
New-Item $key -Force | Out-Null
$size = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\Uninstall.ps1`""
$values = @{
    DisplayName = 'MTC Explorer'; DisplayVersion = $version; Publisher = 'Evan Minton'
    DisplayIcon = $exe; InstallLocation = $InstallDir
    UninstallString = $uninstall; QuietUninstallString = "$uninstall -Quiet"
}
foreach ($k in $values.Keys) { Set-ItemProperty $key $k $values[$k] }
Set-ItemProperty $key EstimatedSize $size -Type DWord
Set-ItemProperty $key NoModify 1 -Type DWord
Set-ItemProperty $key NoRepair 1 -Type DWord

Write-Host "Installed. Start menu: MTC Explorer   CLI: $cliDir\mtc.exe" -ForegroundColor Green
if (-not $NoLaunch) { Start-Process $exe }
