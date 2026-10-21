@echo off
setlocal
set "toolkit_powershell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "toolkit_powershell=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%toolkit_powershell%" -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Start.ps1"
if errorlevel 1 pause
