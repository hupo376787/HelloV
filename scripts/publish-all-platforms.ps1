#requires -Version 5.1
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$targets = @('win-x64', 'win-arm64')
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
    param([Parameter(Mandatory)][string]$Target)

    & $runtimePublishPlatformPath `
        -Target $Target `
        -Configuration $Configuration `
        -Version $Version

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing target '$Target' failed with exit code $LASTEXITCODE."
    }
}

try {
    Invoke-PublishPlatform -Target 'browser'

    foreach ($target in $targets) {
        Invoke-PublishPlatform -Target $target
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        Invoke-PublishPlatform -Target 'android'
    }
    else {
        Write-Host 'ANDROID_HOME was not detected. Android publishing was skipped.' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host "All available targets were published to: $(Join-Path $root 'artifacts')" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $runtimePublishPlatformPath) {
        Remove-Item -LiteralPath $runtimePublishPlatformPath -Force -ErrorAction SilentlyContinue
    }
}
