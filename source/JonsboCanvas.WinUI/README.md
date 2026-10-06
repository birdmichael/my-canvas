# Jonsbo Canvas · WinUI 3

这是 Jonsbo Canvas 的原生 WinUI 3 / Fluent 重构版。旧的 WinForms 版本没有被覆盖，两个版本可以独立保留。

## 直接运行

1. 先完全退出原厂 `JONSBO-AIO`，避免两个进程同时占用 USB 屏幕。
2. 运行 `..\..\outputs\JonsboCanvas-WinUI\JonsboCanvas.WinUI.exe`。
3. 接受 Windows 的管理员权限提示。管理员权限用于 USB 设备访问、驱动修复及在需要时停止原厂进程。
4. 点击右上角“连接屏幕”。未连接实体设备时，可以保持“本地预览”开启来查看实时渲染。

发布目录是自包含的 x64 版本，不需要目标电脑预装 .NET 或 Windows App SDK。请保留整个发布文件夹；不要只复制 EXE。体积较大是因为运行时一并打包进去了。

## 已实现

- WinUI 3 原生窗口、Fluent NavigationView、Mica/深色设计语言与自定义标题栏
- 聚焦式工作区：首页只保留连接、实时预览和当前模式控制，主题、自动连接与驱动维护集中到独立设置页
- 硬件、音乐、Codex、Claude 和设置五类工作模式
- 复用现有 `DashboardRenderer`、指标采集、网易云音乐桥接、USB 传输和驱动修复逻辑
- 960 × 376 屏幕的像素级预览，并随 Windows DPI/显示器缩放自动调整
- 左侧导航作为唯一模式入口，避免与右侧设置重复
- 连接状态、刷新率、主题、可直接选择的 5/8/10/15/20 FPS 音乐档位、实际 FPS 读数、网易云实时同步、Claude 同步、本地预览和帧保存等控制
- 高对比原生标题栏按钮，保留最小化、最大化、关闭及键盘操作
- 根据可用工作区自动选择窗口尺寸，避免固定 1440 × 1024 在小屏幕上被任务栏或屏幕边缘裁切
- 自包含 Windows x64 发布，可直接分发整个文件夹或 ZIP

## 从源码构建

仓库内已经准备了本地 .NET SDK。双击或在 PowerShell 中执行：

```powershell
.\build-winui.cmd
```

脚本会还原、构建并发布到 `..\..\outputs\JonsboCanvas-WinUI`。主要命令等价于：

```powershell
$env:DOTNET_CLI_HOME = '..\tools\dotnet-home'
..\tools\dotnet\dotnet.exe restore --configfile .\NuGet.Config -p:Platform=x64 -p:RuntimeIdentifier=win-x64
..\tools\dotnet\dotnet.exe publish -c Release -r win-x64 --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishTrimmed=false -p:PublishReadyToRun=false -o '..\..\outputs\JonsboCanvas-WinUI'
```

## 验收说明

项目提供 `capture-winui.ps1` 确定性截图模式，可在不向实体 USB 屏幕发送数据的情况下检查概览、各模式和设置页。首次真机测试仍需在退出原厂应用后进行。

## 网易云实时同步

如果网易云由系统自动启动，它通常不会带 Jonsbo Canvas 所需的实时调试端口。此时音乐页会显示“启用同步”并暂停音乐画面，不再通过缓存猜测歌曲和进度。

点击“启用同步”并确认后，Jonsbo Canvas 会正常关闭再重新打开网易云，启动参数会加入仅监听本机的实时同步端口。播放会短暂停止一次；以后同一次网易云进程存活期间不需要重复操作。

音乐页的帧率下拉框同时控制桌面预览和 USB 输出，可直接选择 5/8/10/15/20 FPS；“实际 FPS”显示当前完整渲染循环达到的帧率。

同步协议按开源项目 [Taskbar-Lyrics](https://github.com/mo-jinran/taskbar-lyrics) 和 [LibSongInfo](https://github.com/Steve-xmh/LibSongInfo) 的实现校对：歌曲 ID、播放秒数、暂停和结束状态只来自网易云原生 `onLoad / onPlayProgress / onPlayState / onEnd` 事件。歌词使用 Taskbar-Lyrics 相同的 `song/lyric/v1` 时间轴，并兼容网易云当前返回的 JSONL 富歌词与旧 LRC。实时通道断开时画面停止，不会混入缓存中的另一首歌。
