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

foreach ($target in $targets) {
    & (Join-Path $PSScriptRoot 'publish-platform.ps1') `
        -Target $target `
        -Configuration $Configuration `
        -Version $Version
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
    & (Join-Path $PSScriptRoot 'publish-platform.ps1') `
        -Target android `
        -Configuration $Configuration `
        -Version $Version
}
else {
    Write-Host '未检测到 ANDROID_HOME，跳过 Android。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host "全部可用目标已发布到：$(Join-Path $root 'artifacts')" -ForegroundColor Green
