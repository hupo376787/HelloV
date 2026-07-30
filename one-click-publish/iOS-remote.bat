@echo off
chcp 65001 >nul
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\validate-publish-scripts.ps1" -Quiet
if errorlevel 1 goto :failed
echo iOS 签名发布必须在 macOS 上运行。请在配对 Mac 上使用 iOS.command，或在 GitHub Actions 中配置 Apple 签名 Secrets。
set EXIT_CODE=1
goto :finish
:failed
set EXIT_CODE=%ERRORLEVEL%
:finish
echo.
pause
exit /b %EXIT_CODE%
