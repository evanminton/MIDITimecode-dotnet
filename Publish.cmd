@echo off
rem Double-click: publishes the standalone Windows build and installs it. Log: publish.log
echo Publishing MTC Explorer (self-contained)... output goes to publish.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %* > "%~dp0publish.log" 2>&1
echo EXIT %ERRORLEVEL% >> "%~dp0publish.log"
if errorlevel 1 (echo Publish failed, see publish.log & pause)
