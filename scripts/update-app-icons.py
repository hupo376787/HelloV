from __future__ import annotations

import base64
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE_B64 = ROOT / "scripts" / "app-icon-source.b64"
SOURCE_PNG = base64.b64decode(SOURCE_B64.read_text(encoding="ascii").strip())
TEMP_SOURCE = ROOT / ".app-icon-source.png"
TEMP_SOURCE.write_bytes(SOURCE_PNG)

try:
    source = Image.open(TEMP_SOURCE).convert("RGBA")

    def write_source(relative_path: str) -> None:
        path = ROOT / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(SOURCE_PNG)

    # Shared 512x512 source used directly by Avalonia, Browser, Desktop and Android adaptive icon.
    for relative_path in (
        "src/HelloV.Core/Assets/AppIcon.png",
        "src/HelloV.Browser/wwwroot/app-icon.png",
        "src/HelloV.Desktop/Assets/app-icon.png",
        "src/HelloV.Android/Resources/drawable-nodpi/app_icon_foreground.png",
    ):
        write_source(relative_path)

    # Android legacy launcher icons.
    android_sizes = {
        "mdpi": 48,
        "hdpi": 72,
        "xhdpi": 96,
        "xxhdpi": 144,
        "xxxhdpi": 192,
    }
    for density, size in android_sizes.items():
        icon = source.resize((size, size), Image.Resampling.LANCZOS)
        for filename in ("app_icon.png", "app_icon_round.png"):
            path = ROOT / f"src/HelloV.Android/Resources/mipmap-{density}/{filename}"
            path.parent.mkdir(parents=True, exist_ok=True)
            icon.save(path, format="PNG", optimize=True)

    # Windows executable icon.
    ico_path = ROOT / "src/HelloV.Desktop/Assets/app-icon.ico"
    source.save(
        ico_path,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    # macOS application bundle icon.
    icns_path = ROOT / "src/HelloV.Desktop/Assets/app-icon.icns"
    source.save(
        icns_path,
        format="ICNS",
        sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)],
    )

    # iOS asset catalog. iOS icons must be opaque, so composite the supplied image over white.
    ios_sizes = {
        "AppIcon-ios-marketing-1024x1024@1x.png": 1024,
        "AppIcon-ipad-20x20@1x.png": 20,
        "AppIcon-ipad-20x20@2x.png": 40,
        "AppIcon-ipad-29x29@1x.png": 29,
        "AppIcon-ipad-29x29@2x.png": 58,
        "AppIcon-ipad-40x40@1x.png": 40,
        "AppIcon-ipad-40x40@2x.png": 80,
        "AppIcon-ipad-76x76@1x.png": 76,
        "AppIcon-ipad-76x76@2x.png": 152,
        "AppIcon-ipad-83_5x83_5@2x.png": 167,
        "AppIcon-iphone-20x20@2x.png": 40,
        "AppIcon-iphone-20x20@3x.png": 60,
        "AppIcon-iphone-29x29@2x.png": 58,
        "AppIcon-iphone-29x29@3x.png": 87,
        "AppIcon-iphone-40x40@2x.png": 80,
        "AppIcon-iphone-40x40@3x.png": 120,
        "AppIcon-iphone-60x60@2x.png": 120,
        "AppIcon-iphone-60x60@3x.png": 180,
    }
    ios_root = ROOT / "src/HelloV.iOS/Assets.xcassets/AppIcon.appiconset"
    for filename, size in ios_sizes.items():
        icon = source.resize((size, size), Image.Resampling.LANCZOS)
        opaque = Image.new("RGB", icon.size, "white")
        opaque.paste(icon, mask=icon.getchannel("A"))
        opaque.save(ios_root / filename, format="PNG", optimize=True)

    print("HelloV application icons updated from scripts/app-icon-source.b64")
finally:
    TEMP_SOURCE.unlink(missing_ok=True)
