# Jonsbo Canvas

乔思伯 `960×376` USB 屏幕的独立自定义控制程序。它复用设备现有的 MS USB Display 驱动，直接发送画面，不刷写固件。

单文件版已把显示 SDK、传感器库、预设主题和签名 USB 驱动修复包全部嵌入 EXE。运行缓存、配置和日志位于 `%LocalAppData%\JonsboCanvas`，EXE 所在目录不会散落依赖文件。

## 使用

1. 双击 `JonsboCanvas-One.exe`，接受 Windows 管理员权限提示。它不需要旁边再放 DLL、主题或驱动文件。
   第一次启动会自动注册“登录后延迟 15 秒、最高权限运行”的 Windows 计划任务；以后开机登录即可自动接管屏幕，不再重复弹出 UAC。
2. 如果原厂 `JONSBO-AIO` 正在运行，程序会询问是否关闭它并接管屏幕。
3. 顶部模块切换已经拆分为“概览 / 硬件 / 音乐 / Codex / Claude”。音乐页会显示大尺寸旋转唱片封面、精确进度和逐行歌词。
4. 从主题下拉框选择“赛博青”“熔岩橙”“极光紫”或“动态流光”，点击“应用主题”。
5. 点击“自定义背景”可选择任意 PNG/JPG/BMP/GIF；GIF 会按帧播放，程序会记住该路径。
6. 点击“连接屏幕”。连接成功后，预览画面会同步发送到小屏。
7. 窗口底部的“音乐帧率”可选择 5、8、10、15 或 20 FPS；8 FPS 为默认推荐档。
8. Codex 页会直接读取本机 `%USERPROFILE%\.codex\sessions` 中的额度快照，并在屏幕内同时显示官方 Codex 伙伴动画、5 小时与 7 天窗口。
9. Claude 页使用原创橙色伙伴。首次点击“启用 Claude 余量同步”会备份已有配置（如有），再安装只写入模型、窗口用量和重置时间的本机状态栏桥接；发送下一条 Claude Code 消息后生效。

最小化按钮和窗口关闭按钮都会把程序收进系统托盘而不中断屏幕，同时暂停本地预览绘制以降低占用；USB 屏幕继续更新。也可以在设置区手动暂停/恢复本地预览。双击托盘图标可恢复；要完全退出，请右键托盘图标选择“退出”。

## 可配置内容

- `Title` / `Subtitle`：顶部标题。
- `Accent` / `AccentSecondary`：CPU、GPU 和强调色，使用十六进制颜色。
- `BackgroundTop` / `BackgroundBottom`：渐变背景颜色。
- `Text` / `MutedText`：主要文字和次要文字颜色。
- `BackgroundImage`：自定义 PNG/JPG 图片路径；可使用相对于程序目录的路径。
- `ThemePreset`：当前预设主题标识。
- `RefreshMilliseconds`：刷新间隔，最低 250 毫秒。
- `ShowSeconds` / `Use24HourClock`：时钟显示方式。
- `RotationDegrees`：屏幕旋转方向，默认 `90`；如果画面方向相反可改为 `270`。
- `EnableCpuidSensors`：原厂 CPUID 传感器读取，默认开启；修改后需重启程序。
- `StartWithWindows`：是否注册开机登录自启任务，默认开启。
- `AutoTakeOverOriginalApp`：开机自启时是否自动关闭冲突的原厂程序，默认开启。
- `DisplayMode`：`auto`、`hardware`、`music`、`codex` 或 `claude`。
- `AnimationFrameMilliseconds`：GIF/屏幕动画刷新间隔，默认 250 毫秒（最高约 4 FPS，降低 CPU 和 USB 占用）。
- `MusicFrameRate`：音乐页动画帧率，范围 2–20；界面提供 5/8/10/15/20 FPS 五档，默认 8 FPS。
- `MinimizeToTray`：最小化或关闭窗口时继续在托盘运行。
- `StartHiddenOnAutoStart`：开机启动后隐藏到托盘。
- `BackgroundOpacity`：背景图或 GIF 的显示强度，范围 0–1。
- `NeteaseDebugPort`：网易云本机实时事件端口，默认 `38476`，仅绑定 `127.0.0.1`。

大字界面显示时间、日期、内存、CPU 温度/占用率和 GPU 温度/占用率。CPU 温度已在目标电脑上通过管理员权限实测。

网易云音乐模式通过网易云 3.1.36 自带的 Chromium 调试通道接收 `onLoad`、`onPlayProgress` 和 `onPlayState` 事件，暂停、继续、拖动进度和换歌都会跟随播放器。封面和时间轴歌词只在换歌时从 `music.163.com` 获取；不会读取或保存网易云账号密码，也没有安装 BetterNCM 或注入 DLL。若实时通道不可用，程序会自动退回缓存识别。

网易云已经运行但没有开启实时端口时，程序会继续使用缓存识别；若需要暂停、拖动和换歌都精确同步，请完全退出网易云，再重新选择“网易云音乐”模式让本程序启动它。

## USB 驱动检测与修复

程序启动时会检查 `MS USB Display / libusb0` 驱动。驱动正常时无需处理；如果驱动缺失，窗口底部会显示提示，点击“安装/修复 USB 驱动”即可调用 Windows 官方 `pnputil` 导入随包附带的签名驱动。

- 驱动安装由 Windows 完成并校验签名，程序不会静默注入内核驱动。
- 修复包位于 `drivers/MSUSBDisplay`，来自当前设备已安装的 Driver Store 原始包，未修改 INF、CAT、SYS 或 DLL。
- 在本机删除原厂应用后，只要没有主动删除 Windows Driver Store 中的驱动，重新插入 USB 通常会自动匹配。
- 在新电脑上首次使用时，若 Windows Update 没有自动提供驱动，可使用程序中的安装/修复按钮；完成后建议拔插一次 USB。
- 驱动包的硬件 ID 为 `VID_345F&PID_9132&MI_03`，只会匹配对应的 MS USB Display 接口。

## 注意

- 同一时间只能由一个程序占用屏幕，不能同时运行 `JONSBO-AIO` 和 Jonsbo Canvas。
- 请保留原厂安装的 `MS USB Display` 驱动。
- 日志保存在程序目录的 `JonsboCanvas.log`。
- 音乐画面不采集 CPU/GPU 传感器；静态硬件画面约每秒刷新一次，音乐播放时可选 5–20 FPS，暂停时自动降到约 1.3 FPS，GIF 最高约 4 FPS。
- Codex/Claude 额度文件每 10 秒最多读取一次；伙伴动画约 4 FPS。相同画面不会重复推送；预览隐藏且 USB 未连接时只进行低频状态检查。
- 若要恢复原厂界面，退出 Jonsbo Canvas 后重新启动 `JONSBO-AIO` 即可。
