@echo off
rem Double-click: publishes standalone MTC Studio (portable zip + setup.exe). Log: publish-studio.log
echo Publishing MTC Studio (self-contained)... output goes to publish-studio.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-studio.ps1" %* > "%~dp0publish-studio.log" 2>&1
echo EXIT %ERRORLEVEL% >> "%~dp0publish-studio.log"
if errorlevel 1 (echo Publish failed, see publish-studio.log & pause)
