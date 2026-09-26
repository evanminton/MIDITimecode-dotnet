@echo off
rem Double-click to install MTC Explorer for the current user.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
if errorlevel 1 pause
