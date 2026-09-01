# HelloV

<p align="center">
  <a href="README.md">English</a> · <strong>简体中文</strong>
</p>

HelloV 是一款有趣的实时手势识别应用，基于 HaGRIDv2 与 YOLOv10。只需将手势展示在摄像头前，即可用 33 种支持的手势触发丰富的全屏视觉特效。

## 应用截图

| V 手势 · 气球 | 点赞 · 大拇指 |
| --- | --- |
| ![V 手势触发气球与胜利手势动画](screenshots/1.jpg) | ![点赞手势触发大拇指动画](screenshots/2.jpg) |

## 功能亮点

- 使用 YOLOv10 ONNX 模型实时识别 33 种 HaGRIDv2 手势。
- 每种手势都有专属动画，并提供烟花、下雨、彩纸和激光等双手组合特效。
- 基于同一套 Avalonia 代码支持 Windows、Linux、macOS、Android、iOS 与 WebAssembly 浏览器。
- 原生平台使用 ONNX Runtime、本地浏览器使用 ONNX Runtime Web 完成推理。
- 支持摄像头选择、前后摄像头切换、镜像修正、全屏模式，以及立即切换特效的打断模式。
- 内置简体中文和英文界面，并支持通过 JSON 语言包扩展其他语言。
- 提供动画预览面板，无需实际做出手势也能逐个测试特效。

## 支持平台

| 平台 | 项目 | 说明 |
| --- | --- | --- |
| Windows / Linux / macOS | `HelloV.Desktop` | 通过 OpenCV 获取桌面端摄像头画面 |
| 浏览器 | `HelloV.Browser` | WebAssembly 应用，需要摄像头权限 |
| Android 8.0+ | `HelloV.Android` | 基于 CameraX，支持前后摄像头 |
| iOS 15.0+ | `HelloV.iOS` | 构建与签名需要 macOS 和 Xcode |

## 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 摄像头
- 构建移动端时需要对应的 .NET Android 或 iOS 工作负载

仓库已经包含轻量模型 `Models/YOLOv10n_gestures.onnx`，构建时会自动复制到对应的应用包中。

### 运行桌面版

```powershell
dotnet restore HelloV.sln
dotnet run --project src/HelloV.Desktop/HelloV.Desktop.csproj
```

### 运行浏览器版

```powershell
dotnet run --project src/HelloV.Browser/HelloV.Browser.csproj
```

浏览器提示时请允许摄像头权限。部署到非本机环境时，请通过 HTTPS 提供发布后的浏览器应用，以便浏览器开放摄像头 API。

### 构建移动端

```powershell
dotnet build src/HelloV.Android/HelloV.Android.csproj -c Release
dotnet build src/HelloV.iOS/HelloV.iOS.csproj -c Release
```

## 发布

在 Windows 上可启动交互式发布工具：

```powershell
.\one-click-publish.cmd
```

也可以直接指定发布目标：

```powershell
.\scripts\one-click-publish.ps1 -Target desktop -Configuration Release -Version 1.0.0
```

在 Linux 或 macOS 上，可通过 Shell 脚本发布指定运行时：

```bash
./scripts/publish-platform.sh linux-x64 Release 1.0.0
```

生成的安装包位于 `artifacts/`。发布脚本支持 Windows、Linux、macOS、浏览器、Android 与 iOS 等目标；移动端发布还需要对应的平台工具链。

## 项目结构

```text
HelloV/
├── Models/                 # 共享的 YOLOv10 ONNX 手势模型
├── screenshots/            # README 使用的应用截图
├── scripts/                # 模型准备与发布脚本
└── src/
    ├── HelloV.Core/        # 共享 UI、手势识别、本地化与特效
    ├── HelloV.Desktop/     # Windows、Linux 与 macOS 宿主
    ├── HelloV.Browser/     # WebAssembly 宿主
    ├── HelloV.Android/     # Android 宿主
    └── HelloV.iOS/         # iOS 宿主
```

## 开源协议

HelloV 使用 [MIT License](LICENSE) 发布。
