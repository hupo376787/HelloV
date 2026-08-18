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
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

# Windows PowerShell 5.1 does not reliably treat UTF-8-without-BOM .ps1 files as UTF-8.
# Decode the platform script ourselves, then execute the resulting ScriptBlock.
$publishPlatformText = [System.IO.File]::ReadAllText($publishPlatformPath, $utf8)
$publishPlatform = [ScriptBlock]::Create($publishPlatformText)

function Invoke-PublishPlatform {
    param([Parameter(Mandatory)][string]$Target)

    & $publishPlatform `
        -Target $Target `
        -Configuration $Configuration `
        -Version $Version

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

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
