@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"

:menu
cls
echo ========================================
echo HelloV One Click Publish
echo Version: %VERSION%
echo ========================================
echo.
echo   [1] Windows x64
echo   [2] Windows ARM64
echo   [3] Windows All ^(x64 + ARM64^)
echo   [4] Browser WebAssembly
echo   [5] Android ^(APK + AAB^)
echo   [6] All Available Platforms
echo   [0] Exit
echo.
set "SELECT="
set /p "SELECT=Select a publish target: "

if "%SELECT%"=="1" set "TARGET=win-x64"& set "TARGET_NAME=Windows x64"& goto :publish
if "%SELECT%"=="2" set "TARGET=win-arm64"& set "TARGET_NAME=Windows ARM64"& goto :publish
if "%SELECT%"=="3" set "TARGET=windows"& set "TARGET_NAME=Windows x64 + ARM64"& goto :publish
if "%SELECT%"=="4" set "TARGET=browser"& set "TARGET_NAME=Browser WebAssembly"& goto :publish
if "%SELECT%"=="5" set "TARGET=android"& set "TARGET_NAME=Android"& goto :publish
if "%SELECT%"=="6" set "TARGET=all"& set "TARGET_NAME=All Available Platforms"& goto :publish
if "%SELECT%"=="0" goto :eof

echo.
echo Invalid selection. Please enter 0-6.
timeout /t 2 /nobreak >nul
goto :menu

:publish
cls
echo ========================================
echo HelloV One Click Publish
echo Target : %TARGET_NAME%
echo Version: %VERSION%
echo ========================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\validate-publish-scripts.ps1" -Quiet
if errorlevel 1 goto :failed

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish-all-platforms.ps1" -Configuration Release -Version "%VERSION%" -Target "%TARGET%"
if errorlevel 1 goto :failed

echo.
echo Publish completed successfully.
echo Output: %~dp0artifacts
goto :done

:failed
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo Publish failed. Error code: %EXIT_CODE%

:done
echo.
echo Press any key to return to the menu...
pause >nul
goto :menu
