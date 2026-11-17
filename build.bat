@echo off
rem Doppio clic per compilare VHD ISO Studio (vedi build.ps1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
pause
