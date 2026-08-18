#requires -Version 5.1
param([switch]$Quiet)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

# Windows PowerShell 5.1 treats UTF-8 files without a BOM as the active ANSI code page.
# Read every script explicitly as UTF-8 and parse the decoded text instead of ParseFile().
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | ForEach-Object {
    $tokens = $null
    $parseErrors = $null

    try {
        $scriptText = [System.IO.File]::ReadAllText($_.FullName, $utf8)
        [void][System.Management.Automation.Language.Parser]::ParseInput(
            $scriptText,
            [ref]$tokens,
            [ref]$parseErrors)
    }
    catch {
        $errors.Add("$($_.Name): $($_.Exception.Message)")
        return
    }

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
        $errors.Add("Missing required file: $relativePath")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

if (-not $Quiet) {
    Write-Host 'Publish script validation passed.' -ForegroundColor Green
}
