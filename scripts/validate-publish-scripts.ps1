#requires -Version 5.1
param([switch]$Quiet)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | ForEach-Object {
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $_.FullName,
        [ref]$tokens,
        [ref]$parseErrors)

    foreach ($parseError in $parseErrors) {
        $errors.Add("$($_.Name): $($parseError.Message)")
    }
}

$required = @(
    'src/HelloV.Desktop/HelloV.Desktop.csproj',
    'src/HelloV.Android/HelloV.Android.csproj',
    'src/HelloV.iOS/HelloV.iOS.csproj',
    'src/HelloV.Browser/HelloV.Browser.csproj',
    'src/HelloV.Browser/wwwroot/js/hellov-browser.js',
    '.github/workflows/build-all-platforms.yml')

foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relativePath))) {
        $errors.Add("缺少文件：$relativePath")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

if (-not $Quiet) {
    Write-Host '发布脚本检查通过。' -ForegroundColor Green
}
