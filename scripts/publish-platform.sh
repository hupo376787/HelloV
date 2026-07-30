#!/usr/bin/env bash
set -euo pipefail

TARGET="${1:-linux-x64}"
CONFIGURATION="${2:-Release}"
VERSION="${3:-1.0.0}"

case "$TARGET" in
  win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64|android|ios-simulator|ios) ;;
  *)
    echo "不支持的目标：$TARGET" >&2
    exit 2
    ;;
esac

if [[ ! "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]]; then
  echo "Version 只能包含字母、数字、点、下划线和连字符。" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARTIFACTS="$ROOT/artifacts"
STAGING="$ARTIFACTS/.staging"
HOST_OS="$(uname -s)"
DESKTOP_PROJECT="$ROOT/src/HelloV.Desktop/HelloV.Desktop.csproj"
ANDROID_PROJECT="$ROOT/src/HelloV.Android/HelloV.Android.csproj"
IOS_PROJECT="$ROOT/src/HelloV.iOS/HelloV.iOS.csproj"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "未找到命令：$1。请先安装并加入 PATH。" >&2
    exit 127
  fi
}

run() {
  printf '\n> '
  printf '%q ' "$@"
  printf '\n'
  "$@"
}

reset_dir() {
  rm -rf "$1"
  mkdir -p "$1"
}

mobile_version() {
  if [[ "$VERSION" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+) ]]; then
    printf '%s.%s.%s' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"
  else
    printf '1.0.0'
  fi
}

zip_package() {
  local package_dir="$1"
  local archive="$2"
  local prefer_ditto="${3:-0}"

  rm -f "$archive"
  if [[ "$prefer_ditto" == "1" && "$HOST_OS" == "Darwin" ]] && command -v ditto >/dev/null 2>&1; then
    run ditto -c -k --sequesterRsrc --keepParent "$package_dir" "$archive"
  else
    require_command zip
    (
      cd "$(dirname "$package_dir")"
      run zip -q -r "$archive" "$(basename "$package_dir")"
    )
  fi
}

complete_package() {
  local package_name="$1"
  local source_dir="$2"
  local prefer_ditto="${3:-0}"
  local package_dir="$STAGING/$package_name"
  local archive="$ARTIFACTS/$package_name.zip"

  if [[ ! -d "$source_dir" ]]; then
    echo "发布目录不存在：$source_dir" >&2
    exit 1
  fi

  reset_dir "$package_dir"
  cp -a "$source_dir/." "$package_dir/"
  zip_package "$package_dir" "$archive" "$prefer_ditto"
  echo
  echo "打包完成：$archive"
}

copy_model_if_available() {
  local output_dir="$1"
  local model_name candidate
  local copied_count=0
  local -a model_names=(
    "YOLOv10n_gestures.onnx"
    "YOLOv10x_gestures.onnx"
  )

  for model_name in "${model_names[@]}"; do
    for candidate in \
      "$ROOT/$model_name" \
      "$ROOT/src/HelloV.Desktop/Models/$model_name"; do
      if [[ ! -f "$candidate" ]]; then
        continue
      fi

      cp -f "$candidate" "$output_dir/$model_name"
      echo "已复制模型：$candidate"
      copied_count=$((copied_count + 1))
      break
    done
  done

  if (( copied_count > 0 )); then
    return 0
  fi

  local supported_names="YOLOv10n_gestures.onnx / YOLOv10x_gestures.onnx"
  if [[ "${HELLOV_REQUIRE_MODEL:-0}" == "1" ]]; then
    echo "发布标签构建要求模型文件，但未找到 $supported_names。" >&2
    echo "请放在仓库根目录或 src/HelloV.Desktop/Models/。" >&2
    exit 1
  fi

  echo "警告：未找到 $supported_names，桌面包仍会生成，但手势识别不可用。" >&2
}

new_mac_app_bundle() {
  local output_dir="$1"
  local bundle_version="$2"
  local bundle="$output_dir/HelloV.app"
  local contents="$bundle/Contents"
  local macos="$contents/MacOS"
  local resources="$contents/Resources"

  mkdir -p "$macos" "$resources"
  while IFS= read -r -d '' item; do
    mv "$item" "$macos/"
  done < <(find "$output_dir" -mindepth 1 -maxdepth 1 ! -name 'HelloV.app' -print0)

  if [[ -f "$ROOT/src/HelloV.Desktop/Assets/app-icon.icns" ]]; then
    cp -f "$ROOT/src/HelloV.Desktop/Assets/app-icon.icns" "$resources/app-icon.icns"
  fi

  cat > "$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>HelloV</string>
  <key>CFBundleDisplayName</key><string>HelloV</string>
  <key>CFBundleIdentifier</key><string>com.example.hellov</string>
  <key>CFBundleExecutable</key><string>HelloV.Desktop</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>app-icon</string>
  <key>CFBundleShortVersionString</key><string>$bundle_version</string>
  <key>CFBundleVersion</key><string>$bundle_version</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
  chmod +x "$macos/HelloV.Desktop" 2>/dev/null || true
}

collect_packages() {
  local search_root="$1"
  local package_dir="$2"
  shift 2
  local found=0

  reset_dir "$package_dir"
  if [[ -d "$search_root" ]]; then
    while IFS= read -r -d '' file; do
      cp -f "$file" "$package_dir/"
      echo "已收集：$file"
      found=1
    done < <(find "$search_root" -type f \( "$@" \) -print0)
  fi

  [[ "$found" == "1" ]]
}

configure_xcode_for_ios_26_4() {
  if [[ "$HOST_OS" != "Darwin" ]]; then
    return
  fi

  local developer_dir="${DEVELOPER_DIR:-}"
  local xcode_app="${HELLOV_XCODE_PATH:-}"

  if [[ -n "$xcode_app" ]]; then
    developer_dir="$xcode_app/Contents/Developer"
  fi

  if [[ -z "$developer_dir" || ! -d "$developer_dir" ]]; then
    local candidate
    for candidate in \
      /Applications/Xcode_26.4.1.app \
      /Applications/Xcode_26.4.app \
      /Applications/Xcode_26.4*.app; do
      if [[ -d "$candidate/Contents/Developer" ]]; then
        developer_dir="$candidate/Contents/Developer"
        break
      fi
    done
  fi

  if [[ -z "$developer_dir" || ! -d "$developer_dir" ]]; then
    echo "未找到与 net10.0-ios 匹配的 Xcode 26.4。" >&2
    find /Applications -maxdepth 1 -name 'Xcode*.app' -print 2>/dev/null | sort >&2 || true
    exit 1
  fi

  export DEVELOPER_DIR="$developer_dir"
  local version_line
  version_line="$(xcodebuild -version | head -n 1)"
  if [[ "$version_line" != "Xcode 26.4"* ]]; then
    echo "当前选择的是 $version_line，但此发布脚本要求 Xcode 26.4。" >&2
    exit 1
  fi

  echo "iOS 构建使用：$version_line"
  echo "DEVELOPER_DIR=$DEVELOPER_DIR"
}

require_command dotnet
mkdir -p "$ARTIFACTS"
reset_dir "$STAGING"

case "$TARGET" in
  win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)
    OUTPUT="$ROOT/publish/desktop/$TARGET"
    reset_dir "$OUTPUT"
    run dotnet publish "$DESKTOP_PROJECT" \
      -c "$CONFIGURATION" \
      -r "$TARGET" \
      --self-contained true \
      -p:PublishSingleFile=false \
      -p:PublishTrimmed=false \
      "-p:Version=$VERSION" \
      "-p:InformationalVersion=$VERSION" \
      -o "$OUTPUT"
    copy_model_if_available "$OUTPUT"
    if [[ "$TARGET" == osx-* ]]; then
      new_mac_app_bundle "$OUTPUT" "$(mobile_version)"
    fi
    complete_package \
      "HelloV-Desktop-$TARGET-$VERSION" \
      "$OUTPUT" \
      "$([[ "$TARGET" == osx-* ]] && echo 1 || echo 0)"
    ;;

  android)
    MOBILE_VERSION="$(mobile_version)"
    BUILD_NUMBER="${GITHUB_RUN_NUMBER:-1}"
    [[ "$BUILD_NUMBER" =~ ^[0-9]+$ ]] || BUILD_NUMBER=1
    FRAMEWORK="net10.0-android"
    rm -rf "$ROOT/src/HelloV.Android/bin/$CONFIGURATION/$FRAMEWORK"
    run dotnet workload restore "$ANDROID_PROJECT"
    run dotnet publish "$ANDROID_PROJECT" \
      -c "$CONFIGURATION" \
      -f "$FRAMEWORK" \
      "-p:ApplicationDisplayVersion=$MOBILE_VERSION" \
      "-p:ApplicationVersion=$BUILD_NUMBER" \
      '-p:AndroidPackageFormats=apk%3Baab'

    PACKAGE_NAME="HelloV-Android-$VERSION"
    PACKAGE_DIR="$STAGING/$PACKAGE_NAME"
    SEARCH_ROOT="$ROOT/src/HelloV.Android/bin/$CONFIGURATION/$FRAMEWORK"
    if ! collect_packages "$SEARCH_ROOT" "$PACKAGE_DIR" -name '*.apk' -o -name '*.aab'; then
      echo "没有在 $SEARCH_ROOT 中找到 APK 或 AAB。" >&2
      exit 1
    fi
    zip_package "$PACKAGE_DIR" "$ARTIFACTS/$PACKAGE_NAME.zip"
    echo
    echo "打包完成：$ARTIFACTS/$PACKAGE_NAME.zip"
    ;;

  ios-simulator)
    if [[ "$HOST_OS" != "Darwin" ]]; then
      echo "iOS Simulator 构建必须在 macOS 上运行。" >&2
      exit 1
    fi

    MOBILE_VERSION="$(mobile_version)"
    BUILD_NUMBER="${GITHUB_RUN_NUMBER:-1}"
    [[ "$BUILD_NUMBER" =~ ^[0-9]+$ ]] || BUILD_NUMBER=1
    FRAMEWORK="net10.0-ios"
    RUNTIME="iossimulator-arm64"
    SEARCH_ROOT="$ROOT/src/HelloV.iOS/bin/$CONFIGURATION"

    configure_xcode_for_ios_26_4
    rm -rf "$ROOT/src/HelloV.iOS/bin/$CONFIGURATION/$FRAMEWORK"
    run dotnet workload restore "$IOS_PROJECT"
    run dotnet build "$IOS_PROJECT" \
      -c "$CONFIGURATION" \
      -f "$FRAMEWORK" \
      "-p:RuntimeIdentifier=$RUNTIME" \
      -p:EnableCodeSigning=false \
      "-p:ApplicationDisplayVersion=$MOBILE_VERSION" \
      "-p:ApplicationVersion=$BUILD_NUMBER"

    APP_PATH="$(find "$SEARCH_ROOT" -type d -name '*.app' -path "*$RUNTIME*" -print -quit 2>/dev/null || true)"
    if [[ -z "$APP_PATH" || ! -d "$APP_PATH" ]]; then
      echo "没有在 $SEARCH_ROOT 中找到 iOS Simulator .app。" >&2
      exit 1
    fi

    PACKAGE_NAME="HelloV-iOS-Simulator-arm64-$VERSION"
    PACKAGE_DIR="$STAGING/$PACKAGE_NAME"
    reset_dir "$PACKAGE_DIR"
    cp -a "$APP_PATH" "$PACKAGE_DIR/"
    cat > "$PACKAGE_DIR/README.txt" <<'README'
This is an unsigned Apple Silicon iOS Simulator build.
It cannot be installed on a physical iPhone or iPad.
Configure the GitHub iOS signing secrets to also produce a signed IPA.
README
    zip_package "$PACKAGE_DIR" "$ARTIFACTS/$PACKAGE_NAME.zip" 1
    echo
    echo "打包完成：$ARTIFACTS/$PACKAGE_NAME.zip"
    ;;

  ios)
    if [[ "$HOST_OS" != "Darwin" ]]; then
      echo "签名 iOS IPA 必须在 macOS + Xcode 上生成。" >&2
      exit 1
    fi

    MOBILE_VERSION="$(mobile_version)"
    BUILD_NUMBER="${GITHUB_RUN_NUMBER:-1}"
    [[ "$BUILD_NUMBER" =~ ^[0-9]+$ ]] || BUILD_NUMBER=1
    FRAMEWORK="net10.0-ios"

    configure_xcode_for_ios_26_4
    rm -rf "$ROOT/src/HelloV.iOS/bin/$CONFIGURATION/$FRAMEWORK"
    run dotnet workload restore "$IOS_PROJECT"

    IOS_ARGS=(
      publish "$IOS_PROJECT"
      -c "$CONFIGURATION"
      -f "$FRAMEWORK"
      '-p:RuntimeIdentifier=ios-arm64'
      '-p:ArchiveOnBuild=true'
      "-p:ApplicationDisplayVersion=$MOBILE_VERSION"
      "-p:ApplicationVersion=$BUILD_NUMBER"
    )

    [[ -n "${HELLOV_IOS_CODESIGN_KEY:-}" ]] && IOS_ARGS+=("-p:CodesignKey=$HELLOV_IOS_CODESIGN_KEY")
    [[ -n "${HELLOV_IOS_PROVISION:-}" ]] && IOS_ARGS+=("-p:CodesignProvision=$HELLOV_IOS_PROVISION")
    [[ -n "${HELLOV_IOS_ENTITLEMENTS:-}" ]] && IOS_ARGS+=("-p:CodesignEntitlements=$HELLOV_IOS_ENTITLEMENTS")

    run dotnet "${IOS_ARGS[@]}"

    PACKAGE_NAME="HelloV-iOS-$VERSION"
    PACKAGE_DIR="$STAGING/$PACKAGE_NAME"
    SEARCH_ROOT="$ROOT/src/HelloV.iOS/bin/$CONFIGURATION/$FRAMEWORK"
    if ! collect_packages "$SEARCH_ROOT" "$PACKAGE_DIR" -name '*.ipa'; then
      echo "没有在 $SEARCH_ROOT 中找到 IPA。请检查 Apple 证书和 Provisioning Profile。" >&2
      exit 1
    fi
    zip_package "$PACKAGE_DIR" "$ARTIFACTS/$PACKAGE_NAME.zip" 1
    echo
    echo "打包完成：$ARTIFACTS/$PACKAGE_NAME.zip"
    ;;
esac
