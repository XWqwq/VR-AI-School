@echo off
setlocal
cd /d "%~dp0"

echo Starting Virtual Campus AI services. Keep this window open...

set "START_SCRIPT=%~dpn0.ps1"
if not exist "%START_SCRIPT%" (
  echo ERROR: Startup PowerShell script was not found.
  pause
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%START_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
  echo Startup failed. See the startup log directory for details.
) else (
  echo All services started successfully. You can open Unity/PDC now.
)

pause
exit /b %EXIT_CODE%
