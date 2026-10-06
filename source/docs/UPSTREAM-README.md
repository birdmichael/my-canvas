<div align="center">
  <img src="work/JonsboCanvas.WinUI/Assets/Square150x150Logo.scale-200.png" width="112" alt="Jonsbo Canvas logo">
  <h1>Jonsbo Canvas</h1>
  <p>为 JONSBO 960 × 376 USB 小屏打造的原生 Windows 控制器。</p>
  <p>A native Windows controller for the JONSBO 960 × 376 USB display.</p>
  <p>硬件监控 · 网易云音乐实时歌词 · Codex / Claude 余量画布 · 系统托盘</p>
  <p>Hardware monitoring · NetEase Cloud Music lyrics · Codex / Claude usage canvas · System tray</p>
</div>

<p align="center">
  <a href="#简体中文">简体中文</a> · <a href="#english">English</a>
</p>

## 简体中文

### 下载

前往 [Releases](https://github.com/ZhouhaoJiang/JonsboCanvas/releases/latest) 下载：

**`JonsboCanvas-Setup-v1.2.0.exe`**

只需要这一个文件。双击后会自动安装应用、创建桌面与开始菜单快捷方式并启动；目标电脑不需要预装 .NET 或 Windows App SDK。

> 当前安装包尚未购买代码签名证书，Windows SmartScreen 可能显示“未知发布者”。请确认文件来自本仓库的正式 Release，并用 Release 页面提供的 SHA-256 校验值核对。

### 界面预览

#### 硬件监控

![Jonsbo Canvas 硬件监控界面](work/JonsboCanvas.WinUI/audit-2026-07-20/03-final-hardware.png)

#### 网易云音乐与实时歌词

![Jonsbo Canvas 音乐歌词界面](work/JonsboCanvas.WinUI/audit-2026-07-20/04-final-music.png)

#### Codex 余量画布

![Jonsbo Canvas Codex 余量画布](work/JonsboCanvas.WinUI/audit-2026-07-20/08-ratio-codex.png)

Codex 页面读取本机用量快照，在小屏上显示当前模型、5 小时与 7 天窗口的剩余百分比和重置时间，并配有 Codex 伙伴动画。它不会读取或上传对话内容。

### 主要功能

- 原生 WinUI 3 / Fluent 深色界面，按 960 × 376 屏幕比例提供实时预览。
- 显示 CPU、GPU、内存、温度和负载，并通过 USB 直接发送到 JONSBO 小屏。
- 监听网易云音乐原生播放事件，同步歌曲、封面、播放进度、暂停状态和逐行歌词。
- 提供自动切换、硬件、音乐、Codex 与 Claude 五种画面。
- 最小化或关闭到系统托盘；支持单实例运行和可选的开机启动。
- 采集器按需加载、动态渲染节奏、重复帧抑制和日志轮转，降低空闲资源占用。
- 内置 USB 驱动检查与修复入口，不修改屏幕固件。

### 使用条件

- Windows 10/11 x64。
- JONSBO 960 × 376 USB 屏幕；当前目标设备接口为 `VID_345F&PID_9132&MI_03`。
- 使用音乐画面时需要 Windows 版网易云音乐。
- 同一时间只能有一个程序占用屏幕；连接前请退出原厂 `JONSBO-AIO`，或让本应用接管。

配置、运行缓存和日志保存在 `%LocalAppData%\JonsboCanvas`。正式版安装目录为 `%LocalAppData%\Programs\JonsboCanvas`。

### 从源码构建

源码构建需要 .NET 10 SDK 与 Windows 10/11 SDK。

```powershell
dotnet restore .\work\JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj
dotnet publish .\work\JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishTrimmed=false -p:PublishReadyToRun=false `
  -o .\outputs\JonsboCanvas-WinUI
```

生成单 EXE 安装器：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\work\JonsboCanvas.Installer\Build-Installer.ps1 `
  -SourceDirectory .\outputs\JonsboCanvas-WinUI `
  -OutputPath .\outputs\JonsboCanvas-Setup.exe
```

### 隐私与许可证

应用不读取或保存网易云账号密码。音乐同步只监听本机回环地址上的播放器事件；歌曲封面和时间轴歌词仅在换歌时向网易云公开接口请求。Codex / Claude 页面只读取本机额度快照，不上传会话内容。

当前尚未选择项目级开源许可证。仓库公开仅用于查看、审计和协作，不自动授予复制、修改或再分发权。随仓库提供的 CPUID、USB 显示 SDK 与驱动二进制仍归各自权利人所有，详见 [THIRD-PARTY-NOTICES](work/JonsboCanvas.WinUI/THIRD-PARTY-NOTICES.md)。

更多开发细节请参阅 [WinUI README](work/JonsboCanvas.WinUI/README.md)。

---

## English

### Download

Download the latest release from [Releases](https://github.com/ZhouhaoJiang/JonsboCanvas/releases/latest):

**`JonsboCanvas-Setup-v1.2.0.exe`**

This is the only file you need. Double-click it to install the application, create Desktop and Start Menu shortcuts, and launch Jonsbo Canvas. The target PC does not need a separate .NET or Windows App SDK installation.

> The installer is not currently code-signed. Windows SmartScreen may display an “Unknown publisher” warning. Make sure the file came from this repository's official Release and verify its SHA-256 checksum against the value on the Release page.

### Screenshots

#### Hardware monitoring

![Jonsbo Canvas hardware monitoring screen](work/JonsboCanvas.WinUI/audit-2026-07-20/03-final-hardware.png)

#### NetEase Cloud Music and synchronized lyrics

![Jonsbo Canvas music and lyrics screen](work/JonsboCanvas.WinUI/audit-2026-07-20/04-final-music.png)

#### Codex usage canvas

![Jonsbo Canvas Codex usage canvas](work/JonsboCanvas.WinUI/audit-2026-07-20/08-ratio-codex.png)

The Codex screen reads local usage snapshots and displays the active model, remaining percentages, and reset times for the 5-hour and 7-day windows, together with an animated Codex companion. It does not read or upload conversation content.

### Features

- Native WinUI 3 and Fluent dark interface with a live preview matching the 960 × 376 display ratio.
- CPU, GPU, memory, temperature, and load monitoring sent directly to the JONSBO display over USB.
- Native NetEase Cloud Music event synchronization for track information, artwork, progress, pause state, and line-by-line lyrics.
- Five display modes: automatic switching, hardware, music, Codex, and Claude.
- Minimize or close to the system tray, single-instance operation, and optional startup with Windows.
- On-demand collectors, adaptive rendering cadence, duplicate-frame suppression, and log rotation for lower idle resource usage.
- Built-in USB driver inspection and repair without modifying display firmware.

### Requirements

- Windows 10/11 x64.
- A JONSBO 960 × 376 USB display. The currently supported interface is `VID_345F&PID_9132&MI_03`.
- The Windows desktop version of NetEase Cloud Music is required for the music screen.
- Only one application can control the display at a time. Exit the original `JONSBO-AIO` application before connecting, or allow Jonsbo Canvas to take over.

Configuration, runtime cache, and logs are stored in `%LocalAppData%\JonsboCanvas`. Release builds install to `%LocalAppData%\Programs\JonsboCanvas`.

### Build from source

Building from source requires the .NET 10 SDK and the Windows 10/11 SDK.

```powershell
dotnet restore .\work\JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj
dotnet publish .\work\JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishTrimmed=false -p:PublishReadyToRun=false `
  -o .\outputs\JonsboCanvas-WinUI
```

Build the single-EXE installer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\work\JonsboCanvas.Installer\Build-Installer.ps1 `
  -SourceDirectory .\outputs\JonsboCanvas-WinUI `
  -OutputPath .\outputs\JonsboCanvas-Setup.exe
```

### Privacy and licensing

The application does not read or store NetEase account credentials. Music synchronization listens only to player events on the local loopback interface. Artwork and timestamped lyrics are requested from NetEase's public endpoints only when the track changes. The Codex and Claude screens read local usage snapshots and do not upload conversation content.

No project-level open-source license has been selected yet. Making this repository public allows viewing, auditing, and collaboration, but does not automatically grant permission to copy, modify, or redistribute the project. The bundled CPUID, USB display SDK, and driver binaries remain the property of their respective owners; see [THIRD-PARTY-NOTICES](work/JonsboCanvas.WinUI/THIRD-PARTY-NOTICES.md).

For additional development details, see the [WinUI README](work/JonsboCanvas.WinUI/README.md).
