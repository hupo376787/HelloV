#requires -Version 5.1
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0',
    [ValidateSet('all', 'windows', 'win-x64', 'win-arm64', 'browser', 'android')]
    [string]$Target = 'all'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishPlatformPath = Join-Path $PSScriptRoot 'publish-platform.ps1'
$runtimePublishPlatformPath = Join-Path $PSScriptRoot '.publish-platform.runtime.ps1'

# Windows PowerShell 5.1 may decode UTF-8-without-BOM scripts as the active ANSI code page.
# Create a temporary UTF-8-BOM copy in the same directory so both parsing and $PSScriptRoot
# behave exactly like the original script.
$publishPlatformText = [System.IO.File]::ReadAllText(
    $publishPlatformPath,
    [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText(
    $runtimePublishPlatformPath,
    $publishPlatformText,
    [System.Text.Encoding]::UTF8)

function Invoke-PublishPlatform {
    param([Parameter(Mandatory)][string]$PlatformTarget)

    & $runtimePublishPlatformPath `
        -Target $PlatformTarget `
        -Configuration $Configuration `
        -Version $Version

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing target '$PlatformTarget' failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AndroidPublish {
    if ([string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        throw 'ANDROID_HOME was not detected. Install/configure the Android SDK before publishing Android.'
    }

    Invoke-PublishPlatform -PlatformTarget 'android'
}

try {
    switch ($Target) {
        'all' {
            Invoke-PublishPlatform -PlatformTarget 'browser'
            Invoke-PublishPlatform -PlatformTarget 'win-x64'
            Invoke-PublishPlatform -PlatformTarget 'win-arm64'

            if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
                Invoke-PublishPlatform -PlatformTarget 'android'
            }
            else {
                Write-Host 'ANDROID_HOME was not detected. Android publishing was skipped.' -ForegroundColor Yellow
            }
        }
        'windows' {
            Invoke-PublishPlatform -PlatformTarget 'win-x64'
            Invoke-PublishPlatform -PlatformTarget 'win-arm64'
        }
        'win-x64' {
            Invoke-PublishPlatform -PlatformTarget 'win-x64'
        }
        'win-arm64' {
            Invoke-PublishPlatform -PlatformTarget 'win-arm64'
        }
        'browser' {
            Invoke-PublishPlatform -PlatformTarget 'browser'
        }
        'android' {
            Invoke-AndroidPublish
        }
    }

    Write-Host ''
    Write-Host "Publish target '$Target' completed. Output: $(Join-Path $root 'artifacts')" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $runtimePublishPlatformPath) {
        Remove-Item -LiteralPath $runtimePublishPlatformPath -Force -ErrorAction SilentlyContinue
    }
}
