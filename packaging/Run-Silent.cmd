@echo off
setlocal DisableDelayedExpansion
set "toolkit_preset=%~1"
if not defined toolkit_preset set "toolkit_preset=all"
if not "%~4"=="" exit /b 64
if not "%~3"=="" goto with_inputs
if not "%~2"=="" goto with_config
"%~dp0Start.exe" --silent --preset "%toolkit_preset%"
exit /b %errorlevel%
:with_config
"%~dp0Start.exe" --silent --preset "%toolkit_preset%" --config "%~f2"
exit /b %errorlevel%
:with_inputs
"%~dp0Start.exe" --silent --preset "%toolkit_preset%" --config "%~f2" --inputs "%~f3"
exit /b %errorlevel%
