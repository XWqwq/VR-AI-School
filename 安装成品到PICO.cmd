@echo off
chcp 65001 >nul
title 安装苏州大学未来校区VR导览到PICO

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0安装成品到PICO.ps1"
set "INSTALL_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%INSTALL_EXIT_CODE%"=="0" (
    echo 安装未完成，请查看上方错误提示。
) else (
    echo 安装流程已完成。
)
echo.
pause
exit /b %INSTALL_EXIT_CODE%
