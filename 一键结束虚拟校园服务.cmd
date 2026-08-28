@echo off
setlocal
cd /d "%~dp0"
echo Stopping Virtual Campus AI services...
set "STOP_SCRIPT=%~dpn0.ps1"
if not exist "%STOP_SCRIPT%" (
  echo ERROR: Shutdown PowerShell script was not found.
  pause
  exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%STOP_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%EXIT_CODE%"=="0" (
  echo Shutdown encountered an error. See the log directory.
) else (
  echo Virtual Campus AI services have stopped.
)
pause
exit /b %EXIT_CODE%
