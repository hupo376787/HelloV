#requires -Version 5.1
param(
    [ValidateSet('all', 'desktop', 'browser', 'android')]
    [string]$Target = 'all',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root 'artifacts'
$PublishPlatformPath = Join-Path $PSScriptRoot 'publish-platform.ps1'
$RuntimePublishPlatformPath = Join-Path $PSScriptRoot '.publish-platform.runtime.ps1'

New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null

# Windows PowerShell 5.1 treats UTF-8 files without a BOM as the active ANSI code page.
# Run a temporary UTF-8-BOM copy so publish-platform.ps1 keeps its original path semantics.
$PublishPlatformText = [System.IO.File]::ReadAllText(
    $PublishPlatformPath,
    [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText(
    $RuntimePublishPlatformPath,
    $PublishPlatformText,
    [System.Text.Encoding]::UTF8)

function Invoke-PublishPlatform {
    param([Parameter(Mandatory)][string]$PlatformTarget)

    & $RuntimePublishPlatformPath `
        -Target $PlatformTarget `
        -Configuration $Configuration `
        -Version $Version

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing target '$PlatformTarget' failed with exit code $LASTEXITCODE."
    }
}

function Publish-Desktop {
    $Rids = @(
        'win-x64',
        'win-arm64',
        'linux-x64',
        'linux-arm64',
        'osx-x64',
        'osx-arm64'
    )

    foreach ($Rid in $Rids) {
        Write-Host "`n=== Desktop: $Rid ===" -ForegroundColor Yellow
        Invoke-PublishPlatform -PlatformTarget $Rid
    }
}

function Publish-Browser {
    Write-Host "`n=== Browser ===" -ForegroundColor Yellow
    Invoke-PublishPlatform -PlatformTarget 'browser'
}

function Publish-Android {
    Write-Host "`n=== Android ===" -ForegroundColor Yellow
    Invoke-PublishPlatform -PlatformTarget 'android'
}

try {
    switch ($Target) {
        'desktop' { Publish-Desktop }
        'browser' { Publish-Browser }
        'android' { Publish-Android }
        'all' {
            Publish-Desktop
            Publish-Browser
            Publish-Android
        }
    }

    Write-Host "`nPublish finished. Artifacts: $Artifacts" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $RuntimePublishPlatformPath) {
        Remove-Item -LiteralPath $RuntimePublishPlatformPath -Force -ErrorAction SilentlyContinue
    }
}
