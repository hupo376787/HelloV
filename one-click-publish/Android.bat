@echo off
chcp 65001 >nul
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\validate-publish-scripts.ps1" -Quiet
if errorlevel 1 goto :failed
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\publish-platform.ps1" -Target android -Configuration Release -Version 1.0.0
set EXIT_CODE=%ERRORLEVEL%
goto :finish
:failed
set EXIT_CODE=%ERRORLEVEL%
:finish
echo.
if not "%EXIT_CODE%"=="0" echo 发布失败，错误代码：%EXIT_CODE%
pause
exit /b %EXIT_CODE%
