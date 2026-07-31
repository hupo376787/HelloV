$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $repoRoot ".model-tools"
$venvDir = Join-Path $toolsDir "venv"
$checkpoint = Join-Path $toolsDir "YOLOv10n_gestures.pt"
$modelDir = Join-Path $repoRoot "Models"
$output = Join-Path $modelDir "YOLOv10n_gestures.onnx"
$exportScript = Join-Path $PSScriptRoot "export-hagridv2-onnx.py"
$checkpointUrl = "https://rndml-team-cv.obs.ru-moscow-1.hc.sbercloud.ru/datasets/hagrid_v2/models/YOLOv10n_gestures.pt"

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

if (-not (Test-Path -LiteralPath $exportScript -PathType Leaf)) {
    throw "Export script was not found: $exportScript"
}

if (-not (Test-Path -LiteralPath $checkpoint -PathType Leaf)) {
    Write-Host "Downloading the HaGRIDv2 YOLOv10n gesture model..."
    Invoke-WebRequest -UseBasicParsing -Uri $checkpointUrl -OutFile $checkpoint
}

$pythonCommand = Get-Command "py.exe" -ErrorAction SilentlyContinue
$usePyLauncher = $null -ne $pythonCommand

if ($null -eq $pythonCommand) {
    $pythonCommand = Get-Command "python.exe" -ErrorAction SilentlyContinue
    $usePyLauncher = $false
}

if ($null -eq $pythonCommand) {
    throw "Python was not found. Install Python 3.10 or later and enable Add python.exe to PATH."
}

if (-not (Test-Path -LiteralPath $venvDir -PathType Container)) {
    Write-Host "Creating the model export virtual environment..."

    if ($usePyLauncher) {
        & $pythonCommand.Source -3 -m venv $venvDir
    }

    if (-not $usePyLauncher) {
        & $pythonCommand.Source -m venv $venvDir
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create the Python virtual environment. Exit code: $LASTEXITCODE"
    }
}

$venvPython = Join-Path $venvDir "Scripts\python.exe"

if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    throw "The virtual environment is incomplete. Delete '$venvDir' and run this script again."
}

Write-Host "Updating pip..."
& $venvPython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) {
    throw "pip update failed. Exit code: $LASTEXITCODE"
}

Write-Host "Installing model export packages..."
& $venvPython -m pip install "ultralytics>=8.3,<9" "onnx>=1.16" "onnxslim>=0.1.50"
if ($LASTEXITCODE -ne 0) {
    throw "Package installation failed. Exit code: $LASTEXITCODE"
}

Write-Host "Exporting the ONNX model..."
& $venvPython $exportScript --checkpoint $checkpoint --output $output --imgsz 640
if ($LASTEXITCODE -ne 0) {
    throw "ONNX export failed. Exit code: $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
    throw "The exporter completed without creating the expected model: $output"
}

Write-Host "Shared Desktop / Android / iOS / Browser model: $output"
