@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"

echo ========================================
echo HelloV One Click Publish
echo Version: %VERSION%
echo ========================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\validate-publish-scripts.ps1" -Quiet
if errorlevel 1 goto :failed

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish-all-platforms.ps1" -Configuration Release -Version "%VERSION%"
if errorlevel 1 goto :failed

echo.
echo Publish completed successfully.
echo Output: %~dp0artifacts
set "EXIT_CODE=0"
goto :finish

:failed
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo Publish failed. Error code: %EXIT_CODE%

:finish
echo.
pause
exit /b %EXIT_CODE%
