# My Canvas · 双屏定制基础版

基于 [ZhouhaoJiang/JonsboCanvas](https://github.com/ZhouhaoJiang/JonsboCanvas) 的本地定制版本，版本号 `1.2.1-dual.2`。目前先完成设备适配，名称、图标、配色和最终内容布局可以继续定制。

## 使用

保留整个发布文件夹，双击 `双击启动正式版.cmd`。程序自带 .NET 和 Windows App SDK 运行时，无需另行安装。启动入口把配置、日志和运行资源放在同目录的 `data` 文件夹。

- 长条屏：1920 × 462，通过 VID_33C3/PID_F101 的串口传输 JPEG，自动识别 COM 端口，使用实机确认的 270°旋转。
- 方屏：480 × 480，通过 MS USB Display SDK，追加并选择480 × 480显示模式，使用实机确认的90°旋转。
- 左侧菜单选择长条屏内容；右上“方屏”下拉框独立选择时钟、自动切换、硬件或音乐。
- “预览屏幕”下拉框仅切换电脑上的预览，不影响另一块屏幕输出。
- 设置页分别报告两块屏幕的连接状态，可同时断开或连接。
- 自动接管开启时，连接前退出JONSBO-AIO，避免争用屏幕。关闭自动接管后，自动连接遇到原厂占用会等待手动处理。
- 默认不开启开机启动。开启后使用定制版专属计划任务，保留当前配置目录。
- 关闭窗口默认隐藏到托盘。彻底退出请从托盘菜单选择退出。

普通权限实机运行时已读到CPU占用、内存和GPU数据，但CPUID未提供CPU温度（初始化日志为result=0、error=5）。若需要重试传感器访问，先从托盘退出程序，再运行 `管理员启动.cmd` 并自行处理Windows权限提示。管理员方式的温度读取尚未验证，不保证一定解决。

直接运行MyCanvas.exe时默认使用 `%LocalAppData%\MyCanvas`；需要指定数据目录可传入 `--data-dir="目录"`。

## 当前设计

硬件画面按 Figma 稿重做，原生适配 1920×462 与 480×480。CPU、GPU、内存采用独立卡片，趋势使用真实采样。自动模式在播放音乐时显示音乐，暂停或无音乐时显示硬件；保留独立时钟。已移除 Codex、Claude 页面和后台额度采集。网易云实时同步保留。灯光目前处于兼容性研究阶段，尚未实现控制。

## 验证

- 此前独立测试程序已经完成实机方向校准，用户确认两块屏幕都正常。
- 定制WinUI程序的设置页报告2/2连接，运行日志记录两条通道持续发送成功。
- 新增26项检查覆盖配置持久化与恢复、端口匹配、JPEG尺寸与方向、内容布局、音乐进度单位和独立帧去重。
- 上游序列化、渲染节奏、托盘和唤醒策略回归检查通过。
- 发布构建通过；上游旧代码仍有可空性及旧网络API警告。

尚未进行长时间压力测试、高帧率动画测试和拔插/睡眠唤醒实机测试。串口音乐输出暂时独立限速5 FPS，USB音乐帧率按原项目设置。

## 源码与构建

主要新增代码位于 `work/JonsboCanvas.WinUI/Core/SerialJpegDisplay.cs`、`DualDisplayController.cs`、`DualLayoutRenderer.cs`。主界面、配置、嵌入运行时和USB控制代码做了相应修改。

使用.NET 10 SDK，从较短路径构建，避免Windows App SDK展开资源时遇到长路径问题：

```powershell
dotnet publish .\work\JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishTrimmed=false -p:PublishReadyToRun=false -o .\outputs\MyCanvas
dotnet run --project .\work\MyCanvas.Tests\MyCanvas.Tests.csproj -c Release
dotnet run --project .\work\JonsboCanvas.WinUI.Tests\JonsboCanvas.WinUI.Tests.csproj -c Release
```

上游仓库与原有第三方说明保留；原始项目授权说明见 `UPSTREAM-README.md`，第三方二进制说明见 `work/JonsboCanvas.WinUI/THIRD-PARTY-NOTICES.md`。本次没有上传或发布到GitHub。

## 网易云兼容修复

3.1.40 客户端使用共享 SDK 的播放事件订阅，保留播放器原有回调；优先连接主播放窗口，避免把桌面歌词窗口当成数据源。重连复用订阅，并更新传输绑定。兼容新版三参数播放状态，保留旧版两参数格式。实机已验证歌曲、歌词、持续进度和程序重启后恢复同步。额外的 JavaScript 回归检查运行：node .\work\MyCanvas.Tests\playback-bridge.test.cjs。

