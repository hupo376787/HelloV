#requires -Version 5.1
param(
    [ValidateSet(
        'win-x64', 'win-arm64',
        'linux-x64', 'linux-arm64',
        'osx-x64', 'osx-arm64',
        'browser', 'android', 'ios-simulator', 'ios')]
    [string]$Target = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version = '1.0.0',

    [string]$CodesignKey = $env:HELLOV_IOS_CODESIGN_KEY,
    [string]$CodesignProvision = $env:HELLOV_IOS_PROVISION,
    [string]$CodesignEntitlements = $env:HELLOV_IOS_ENTITLEMENTS
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $root 'artifacts'
$stagingRoot = Join-Path $artifactsRoot '.staging'
$desktopProject = Join-Path $root 'src/HelloV.Desktop/HelloV.Desktop.csproj'
$androidProject = Join-Path $root 'src/HelloV.Android/HelloV.Android.csproj'
$iosProject = Join-Path $root 'src/HelloV.iOS/HelloV.iOS.csproj'
$browserProject = Join-Path $root 'src/HelloV.Browser/HelloV.Browser.csproj'
$isWindowsHost = $env:OS -eq 'Windows_NT'
$isMacHost = $false

if (-not $isWindowsHost) {
    $uname = Get-Command uname -ErrorAction SilentlyContinue
    if ($null -ne $uname) {
        $isMacHost = ((& $uname.Source -s) -eq 'Darwin')
    }
}

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "未找到命令：$Name。请先安装并加入 PATH。"
    }
}

function Invoke-External {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    Write-Host ''
    Write-Host ("> {0} {1}" -f $FilePath, ($Arguments -join ' ')) -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败，退出代码：$LASTEXITCODE"
    }
}

function Reset-Directory {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -Path $Path -ItemType Directory -Force | Out-Null
}

function Get-MobileVersion {
    if ($Version -match '^(\d+)\.(\d+)\.(\d+)') {
        return "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    }
    return '1.0.0'
}

function New-ZipArchive {
    param(
        [Parameter(Mandatory)][string]$PackageDirectory,
        [Parameter(Mandatory)][string]$ArchivePath,
        [switch]$PreferMacDitto
    )

    if (Test-Path -LiteralPath $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }

    if ($PreferMacDitto -and $isMacHost -and $null -ne (Get-Command ditto -ErrorAction SilentlyContinue)) {
        Invoke-External -FilePath 'ditto' -Arguments @(
            '-c', '-k', '--sequesterRsrc', '--keepParent',
            $PackageDirectory,
            $ArchivePath)
        return
    }

    $zip = Get-Command zip -ErrorAction SilentlyContinue
    if ($null -ne $zip) {
        $parent = Split-Path -Parent $PackageDirectory
        $name = Split-Path -Leaf $PackageDirectory
        Push-Location $parent
        try {
            Invoke-External -FilePath $zip.Source -Arguments @('-q', '-r', $ArchivePath, $name)
        }
        finally {
            Pop-Location
        }
        return
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $PackageDirectory,
        $ArchivePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $true)
}

function Complete-Package {
    param(
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$SourceDirectory,
        [switch]$PreferMacDitto
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw "发布目录不存在：$SourceDirectory"
    }

    New-Item -Path $artifactsRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $stagingRoot -ItemType Directory -Force | Out-Null

    $packageDirectory = Join-Path $stagingRoot $PackageName
    Reset-Directory -Path $packageDirectory
    Get-ChildItem -LiteralPath $SourceDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $packageDirectory -Recurse -Force
    }

    $archivePath = Join-Path $artifactsRoot "$PackageName.zip"
    New-ZipArchive -PackageDirectory $packageDirectory -ArchivePath $archivePath -PreferMacDitto:$PreferMacDitto
    Write-Host ''
    Write-Host "打包完成：$archivePath" -ForegroundColor Green
}

function Copy-ModelIfAvailable {
    param([Parameter(Mandatory)][string]$OutputDirectory)

    $modelNames = @(
        'YOLOv10n_gestures.onnx',
        'YOLOv10x_gestures.onnx')
    $copiedCount = 0

    foreach ($modelName in $modelNames) {
        $candidates = @(
            (Join-Path $root "Models/$modelName"))

        foreach ($candidate in $candidates) {
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                continue
            }

            Copy-Item -LiteralPath $candidate -Destination (Join-Path $OutputDirectory $modelName) -Force
            Write-Host "已复制模型：$candidate" -ForegroundColor Green
            $copiedCount++
            break
        }
    }

    if ($copiedCount -gt 0) {
        return
    }

    $supportedNames = $modelNames -join ' / '
    if ($env:HELLOV_REQUIRE_MODEL -eq '1') {
        throw "发布标签构建要求模型文件，但未找到 $supportedNames。请放在仓库根目录的 Models/ 文件夹。"
    }

    Write-Warning "未找到 $supportedNames，桌面包仍会生成，但手势识别不可用。"
}

function New-MacAppBundle {
    param(
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][string]$BundleVersion
    )

    $bundlePath = Join-Path $OutputDirectory 'HelloV.app'
    $contentsPath = Join-Path $bundlePath 'Contents'
    $macOsPath = Join-Path $contentsPath 'MacOS'
    $resourcesPath = Join-Path $contentsPath 'Resources'

    New-Item -Path $macOsPath -ItemType Directory -Force | Out-Null
    New-Item -Path $resourcesPath -ItemType Directory -Force | Out-Null

    Get-ChildItem -LiteralPath $OutputDirectory -Force |
        Where-Object { $_.Name -ne 'HelloV.app' } |
        ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination $macOsPath -Force
        }

    $iconSource = Join-Path $root 'src/HelloV.Desktop/Assets/app-icon.icns'
    if (Test-Path -LiteralPath $iconSource -PathType Leaf) {
        Copy-Item -LiteralPath $iconSource -Destination (Join-Path $resourcesPath 'app-icon.icns') -Force
    }

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>HelloV</string>
  <key>CFBundleDisplayName</key><string>HelloV</string>
  <key>CFBundleIdentifier</key><string>com.xiaowei.hellov</string>
  <key>CFBundleExecutable</key><string>HelloV.Desktop</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>app-icon</string>
  <key>CFBundleShortVersionString</key><string>$BundleVersion</string>
  <key>CFBundleVersion</key><string>$BundleVersion</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
    Set-Content -LiteralPath (Join-Path $contentsPath 'Info.plist') -Value $plist -Encoding UTF8
}

function Get-UniquePackages {
    param(
        [Parameter(Mandatory)][string]$SearchRoot,
        [Parameter(Mandatory)][string[]]$Extensions
    )

    if (-not (Test-Path -LiteralPath $SearchRoot -PathType Container)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $SearchRoot -Recurse -File |
            Where-Object { $Extensions -contains $_.Extension.ToLowerInvariant() } |
            Group-Object Name |
            ForEach-Object {
                $_.Group |
                    Sort-Object Length, LastWriteTimeUtc -Descending |
                    Select-Object -First 1
            })
}

function Configure-XcodeForIos264 {
    if (-not $isMacHost) {
        return
    }

    $developerDirectory = $env:DEVELOPER_DIR
    $xcodeApp = $env:HELLOV_XCODE_PATH

    if (-not [string]::IsNullOrWhiteSpace($xcodeApp)) {
        $developerDirectory = Join-Path $xcodeApp 'Contents/Developer'
    }

    if ([string]::IsNullOrWhiteSpace($developerDirectory) -or
        -not (Test-Path -LiteralPath $developerDirectory -PathType Container)) {
        $candidates = @('/Applications/Xcode_26.4.1.app', '/Applications/Xcode_26.4.app')
        $candidates += Get-ChildItem -LiteralPath '/Applications' -Directory -Filter 'Xcode_26.4*.app' -ErrorAction SilentlyContinue |
            ForEach-Object { $_.FullName }

        foreach ($candidate in ($candidates | Select-Object -Unique)) {
            $candidateDeveloperDirectory = Join-Path $candidate 'Contents/Developer'
            if (Test-Path -LiteralPath $candidateDeveloperDirectory -PathType Container) {
                $developerDirectory = $candidateDeveloperDirectory
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($developerDirectory) -or
        -not (Test-Path -LiteralPath $developerDirectory -PathType Container)) {
        throw '未找到与 net10.0-ios 匹配的 Xcode 26.4。'
    }

    $env:DEVELOPER_DIR = $developerDirectory
    $versionOutput = & xcodebuild -version
    if ($LASTEXITCODE -ne 0) {
        throw '执行 xcodebuild -version 失败。'
    }
    $versionLine = @($versionOutput)[0]
    if (-not $versionLine.StartsWith('Xcode 26.4', [StringComparison]::Ordinal)) {
        throw "当前选择的是 $versionLine，但此发布脚本要求 Xcode 26.4。"
    }

    Write-Host "iOS 构建使用：$versionLine" -ForegroundColor Green
}

if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') {
    throw 'Version 只能包含字母、数字、点、下划线和连字符。'
}

Assert-Command -Name 'dotnet'
New-Item -Path $artifactsRoot -ItemType Directory -Force | Out-Null
Reset-Directory -Path $stagingRoot

if ($Target -in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')) {
    $output = Join-Path $root "publish/desktop/$Target"
    Reset-Directory -Path $output
    Invoke-External -FilePath 'dotnet' -Arguments @(
        'publish', $desktopProject,
        '-c', $Configuration,
        '-r', $Target,
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version",
        '-o', $output)

    Copy-ModelIfAvailable -OutputDirectory $output
    $preferMacDitto = $Target.StartsWith('osx-', [StringComparison]::Ordinal)
    if ($preferMacDitto) {
        New-MacAppBundle -OutputDirectory $output -BundleVersion (Get-MobileVersion)
    }
    Complete-Package `
        -PackageName "HelloV-Desktop-$Target-$Version" `
        -SourceDirectory $output `
        -PreferMacDitto:$preferMacDitto
}
elseif ($Target -eq 'browser') {
    if ($env:HELLOV_REQUIRE_MODEL -eq '1' -and
        -not (Test-Path -LiteralPath (Join-Path $root 'Models/YOLOv10n_gestures.onnx') -PathType Leaf) -and
        -not (Test-Path -LiteralPath (Join-Path $root 'Models/YOLOv10x_gestures.onnx') -PathType Leaf)) {
        throw '浏览器发布要求 ONNX 模型。请将 YOLOv10n_gestures.onnx 或 YOLOv10x_gestures.onnx 放入 Models/。'
    }

    $framework = 'net10.0-browser'
    $browserProjectDirectory = Split-Path -Parent $browserProject
    $publishRoot = Join-Path $browserProjectDirectory "bin/$Configuration/$framework/publish"
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }

    Invoke-External -FilePath 'dotnet' -Arguments @('workload', 'restore', $browserProject)
    Invoke-External -FilePath 'dotnet' -Arguments @(
        'publish', $browserProject,
        '-c', $Configuration,
        '-f', $framework,
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version")

    $staticSite = Join-Path $publishRoot 'wwwroot'
    if (-not (Test-Path -LiteralPath $staticSite -PathType Container)) {
        throw "浏览器静态站点不存在：$staticSite"
    }

    Complete-Package `
        -PackageName "HelloV-Browser-$Version" `
        -SourceDirectory $staticSite
}
elseif ($Target -eq 'android') {
    $framework = 'net10.0-android'
    $mobileVersion = Get-MobileVersion
    $buildNumber = if ($env:GITHUB_RUN_NUMBER -match '^\d+$') { $env:GITHUB_RUN_NUMBER } else { '1' }

    Invoke-External -FilePath 'dotnet' -Arguments @('workload', 'restore', $androidProject)
    Invoke-External -FilePath 'dotnet' -Arguments @(
        'publish', $androidProject,
        '-c', $Configuration,
        '-f', $framework,
        "-p:ApplicationDisplayVersion=$mobileVersion",
        "-p:ApplicationVersion=$buildNumber",
        '-p:AndroidPackageFormats=apk%3Baab')

    $searchRoot = Join-Path (Split-Path -Parent $androidProject) "bin/$Configuration/$framework"
    $packages = Get-UniquePackages -SearchRoot $searchRoot -Extensions @('.apk', '.aab')
    if ($packages.Count -eq 0) {
        throw "没有在 $searchRoot 中找到 APK 或 AAB。"
    }

    $packageName = "HelloV-Android-$Version"
    $packageDirectory = Join-Path $stagingRoot $packageName
    Reset-Directory -Path $packageDirectory
    foreach ($package in $packages) {
        Copy-Item -LiteralPath $package.FullName -Destination $packageDirectory -Force
    }
    New-ZipArchive -PackageDirectory $packageDirectory -ArchivePath (Join-Path $artifactsRoot "$packageName.zip")
}
elseif ($Target -eq 'ios-simulator') {
    if (-not $isMacHost) {
        throw 'iOS Simulator 构建必须在 macOS 上运行。'
    }

    Configure-XcodeForIos264
    $framework = 'net10.0-ios'
    $runtime = 'iossimulator-arm64'
    $mobileVersion = Get-MobileVersion
    $buildNumber = if ($env:GITHUB_RUN_NUMBER -match '^\d+$') { $env:GITHUB_RUN_NUMBER } else { '1' }

    Invoke-External -FilePath 'dotnet' -Arguments @('workload', 'restore', $iosProject)
    Invoke-External -FilePath 'dotnet' -Arguments @(
        'build', $iosProject,
        '-c', $Configuration,
        '-f', $framework,
        "-p:RuntimeIdentifier=$runtime",
        '-p:EnableCodeSigning=false',
        "-p:ApplicationDisplayVersion=$mobileVersion",
        "-p:ApplicationVersion=$buildNumber")

    $searchRoot = Join-Path (Split-Path -Parent $iosProject) "bin/$Configuration"
    $app = Get-ChildItem -LiteralPath $searchRoot -Directory -Filter '*.app' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*$runtime*" } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $app) {
        throw "没有在 $searchRoot 中找到 iOS Simulator .app。"
    }

    $packageName = "HelloV-iOS-Simulator-arm64-$Version"
    $packageDirectory = Join-Path $stagingRoot $packageName
    Reset-Directory -Path $packageDirectory
    Copy-Item -LiteralPath $app.FullName -Destination $packageDirectory -Recurse -Force
    Set-Content -LiteralPath (Join-Path $packageDirectory 'README.txt') -Encoding UTF8 -Value @(
        'This is an unsigned Apple Silicon iOS Simulator build.',
        'It cannot be installed on a physical iPhone or iPad.',
        'Configure the GitHub iOS signing secrets to also produce a signed IPA.')
    New-ZipArchive -PackageDirectory $packageDirectory -ArchivePath (Join-Path $artifactsRoot "$packageName.zip") -PreferMacDitto
}
else {
    if (-not $isMacHost) {
        throw '签名 iOS IPA 必须在 macOS + Xcode 上生成。'
    }

    Configure-XcodeForIos264
    $framework = 'net10.0-ios'
    $mobileVersion = Get-MobileVersion
    $buildNumber = if ($env:GITHUB_RUN_NUMBER -match '^\d+$') { $env:GITHUB_RUN_NUMBER } else { '1' }

    Invoke-External -FilePath 'dotnet' -Arguments @('workload', 'restore', $iosProject)
    $arguments = @(
        'publish', $iosProject,
        '-c', $Configuration,
        '-f', $framework,
        '-p:RuntimeIdentifier=ios-arm64',
        '-p:ArchiveOnBuild=true',
        "-p:ApplicationDisplayVersion=$mobileVersion",
        "-p:ApplicationVersion=$buildNumber")

    if (-not [string]::IsNullOrWhiteSpace($CodesignKey)) {
        $arguments += "-p:CodesignKey=$CodesignKey"
    }
    if (-not [string]::IsNullOrWhiteSpace($CodesignProvision)) {
        $arguments += "-p:CodesignProvision=$CodesignProvision"
    }
    if (-not [string]::IsNullOrWhiteSpace($CodesignEntitlements)) {
        $arguments += "-p:CodesignEntitlements=$CodesignEntitlements"
    }

    Invoke-External -FilePath 'dotnet' -Arguments $arguments

    $searchRoot = Join-Path (Split-Path -Parent $iosProject) "bin/$Configuration/$framework"
    $packages = Get-UniquePackages -SearchRoot $searchRoot -Extensions @('.ipa')
    if ($packages.Count -eq 0) {
        throw "没有在 $searchRoot 中找到 IPA。请检查 Apple 证书和 Provisioning Profile。"
    }

    $packageName = "HelloV-iOS-$Version"
    $packageDirectory = Join-Path $stagingRoot $packageName
    Reset-Directory -Path $packageDirectory
    foreach ($package in $packages) {
        Copy-Item -LiteralPath $package.FullName -Destination $packageDirectory -Force
    }
    New-ZipArchive -PackageDirectory $packageDirectory -ArchivePath (Join-Path $artifactsRoot "$packageName.zip") -PreferMacDitto
}
