namespace JonsboCanvas_WinUI;

// Pages read their text once when they are built; switching language rebuilds the open page.
public static class Localization
{
    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        ["App.Subtitle"] = "USB 屏幕与灯光",
        ["Nav.Home"] = "概览", ["Nav.Content"] = "画面", ["Nav.Wallpaper"] = "壁纸",
        ["Nav.Lighting"] = "灯光", ["Nav.Devices"] = "设备", ["Nav.Settings"] = "设置",
        ["Common.Off"] = "已关闭", ["Common.Cancel"] = "取消", ["Common.NotNow"] = "暂不", ["Common.OpenFolder"] = "打开文件夹",

        ["Home.Title"] = "概览", ["Home.Online"] = "{0}/2 块屏幕在线", ["Home.Showing"] = "正在显示{0}",
        ["Home.Long"] = "长条屏", ["Home.Square"] = "方屏",
        ["Home.NowPlaying"] = "正在播放", ["Home.Lighting"] = "机箱灯光", ["Home.Wallpaper"] = "壁纸",

        ["Mode.Auto"] = "自动", ["Mode.Hardware"] = "硬件", ["Mode.Music"] = "音乐", ["Mode.Clock"] = "时钟",

        ["State.Connected"] = "已连接", ["State.ConnectedOn"] = "已连接 · {0}", ["State.Searching"] = "正在查找设备…",
        ["State.NotResponding"] = "没有响应", ["State.Off"] = "未连接", ["State.Demo"] = "演示模式",
        ["State.NotRespondingHint"] = "长条屏连续几次没有回应。通常拔插一下 USB 线就能恢复，软件会自动重连。",

        ["Connection.Connect"] = "连接屏幕", ["Connection.Disconnect"] = "断开",
        ["Connection.InUse"] = "原厂 JONSBO-AIO 正在占用屏幕",
        ["Connection.TakeoverFailed"] = "没能关闭原厂程序，请手动退出后再连接",
        ["Connection.FailedCode"] = "连接失败，错误码 {0}", ["Connection.Failed"] = "连接失败：{0}",
        ["Connection.ResumeFailedCode"] = "睡眠唤醒后恢复失败，错误码 {0}",
        ["Connection.Takeover.Title"] = "接管 USB 屏幕？",
        ["Connection.Takeover.Content"] = "JONSBO-AIO 正在使用屏幕。关闭原厂程序后，My Canvas 才能连接。",
        ["Connection.Takeover.Primary"] = "关闭并接管",

        ["Music.State.Off"] = "未连接网易云", ["Music.State.Connecting"] = "正在连接…",
        ["Music.State.Waiting"] = "已连接，等待播放", ["Music.State.Synced"] = "同步中",
        ["Music.Connect"] = "连接网易云", ["Music.Recheck"] = "重新检测",
        ["Music.Playing"] = "播放中", ["Music.Paused"] = "已暂停",
        ["Music.Hint"] = "在网易云里播放歌曲，歌词会显示在屏幕上",
        ["Music.Reconnected"] = "实时通道已重新连接；开始播放后会自动同步歌词",
        ["Music.RecheckFailed"] = "重新检测失败；请重启网易云实时同步",
        ["Music.ConnectedStatus"] = "网易云已连接；播放后会同步进度与歌词",
        ["Music.StillDisconnected"] = "网易云仍未连接；请确认它已重新打开后再试",
        ["Music.SyncFailed"] = "网易云同步失败：{0}",
        ["Music.Restart.Title"] = "重启网易云以启用实时同步？",
        ["Music.Restart.Content"] = "网易云这次启动时没有打开实时同步端口。需要正常关闭并重新打开网易云，播放会短暂停止。",
        ["Music.Restart.Primary"] = "重启并同步",

        ["Content.Title"] = "画面", ["Content.Subtitle"] = "屏幕上的歌词、时钟和硬件读数怎么显示。",
        ["Content.Music"] = "音乐", ["Content.Sync"] = "网易云实时同步",
        ["Content.Sync.Hint"] = "只读取网易云的播放进度和歌词，不会改动播放。",
        ["Content.Lyrics"] = "歌词样式", ["Content.Lyrics.Hint"] = "长条屏音乐画面的歌词效果，颜色取自专辑封面。",
        ["Content.Fps"] = "动画帧率", ["Content.Fps.Hint"] = "越高越流畅，CPU 和 USB 占用也越高。",
        ["Content.Clock"] = "时钟", ["Content.Clock24"] = "24 小时制", ["Content.Clock24.Hint"] = "方屏时钟的时间格式。",
        ["Content.Hardware"] = "硬件监控", ["Content.Refresh"] = "刷新间隔", ["Content.Refresh.Hint"] = "CPU、GPU、内存和温度的采样频率。",
        ["Content.ModesHint"] = "每块屏幕显示硬件、时钟还是音乐，在「概览」里切换。",
        ["Lyrics.apple"] = "流光 · 上下句滚动", ["Lyrics.spotify"] = "纯色 · 粗体高亮", ["Lyrics.bigtype"] = "大字 · 一次一句",
        ["Lyrics.lumiere"] = "绘光 · 逐字发光", ["Lyrics.fume"] = "字墙 · 整首滚动", ["Lyrics.partita"] = "云阶 · 错落台阶",
        ["Lyrics.cadenza"] = "散字 · 光圈", ["Lyrics.tilt"] = "倾斜 · 双行",
        ["Fps.5"] = "5 FPS · 省资源", ["Fps.8"] = "8 FPS · 推荐", ["Fps.10"] = "10 FPS", ["Fps.15"] = "15 FPS", ["Fps.20"] = "20 FPS · 最流畅",
        ["Refresh.500"] = "0.5 秒", ["Refresh.900"] = "0.9 秒 · 推荐", ["Refresh.2000"] = "2 秒 · 省资源",

        ["Wallpaper.Title"] = "壁纸", ["Wallpaper.Subtitle"] = "长条屏和方屏时钟背后的照片，也可以同步到电脑桌面。",
        ["Wallpaper.Next"] = "换一张", ["Wallpaper.Open"] = "打开位置", ["Wallpaper.Loading"] = "正在获取图片…",
        ["Wallpaper.Fetching"] = "正在获取新图片…", ["Wallpaper.Failed"] = "没能获取图片，请检查网络或换一张 JPG / PNG",
        ["Wallpaper.History"] = "最近的图片", ["Wallpaper.SourceSection"] = "来源",
        ["Wallpaper.Source"] = "图片来源", ["Wallpaper.Source.Hint"] = "必应每天一张；精选风景每小时换一张偏暗的风景照。",
        ["Wallpaper.File"] = "自定义图片", ["Wallpaper.Choose"] = "选择图片…", ["Wallpaper.None"] = "尚未选择图片",
        ["Wallpaper.DialogTitle"] = "选择壁纸图片", ["Wallpaper.SyncSection"] = "同步到 Windows",
        ["Wallpaper.Desktop"] = "同步为桌面壁纸", ["Wallpaper.Desktop.Hint"] = "照片一换，桌面也跟着换；关闭后恢复你原来的壁纸。",
        ["Wallpaper.Accent"] = "主题色跟随壁纸", ["Wallpaper.Accent.Hint"] = "用照片的柔和主色作为 Windows 和本软件的主题色；关闭后恢复原来的颜色。",
        ["Source.bing"] = "必应每日", ["Source.wallhaven"] = "精选风景", ["Source.custom"] = "自定义",

        ["Lighting.Title"] = "灯光", ["Lighting.Subtitle"] = "机箱风扇、水冷和灯条（技嘉主板 ARGB）。",
        ["Lighting.Enable"] = "灯光联动", ["Lighting.Enable.Hint"] = "已关闭，机箱灯光保持主板默认效果",
        ["Lighting.Current"] = "当前颜色 {0}", ["Lighting.Brightness"] = "亮度",
        ["Lighting.HardwareSection"] = "硬件和时钟画面", ["Lighting.MusicSection"] = "播放音乐时",
        ["Lighting.Source"] = "颜色来源",
        ["Lighting.Hardware.Hint"] = "跟随壁纸时，照片越暗灯越暗（不超过上面的亮度）。",
        ["Lighting.Music.Hint"] = "跟随封面时，每首歌换成封面的主色。",
        ["Lighting.FromWallpaper"] = "跟随壁纸", ["Lighting.FromCover"] = "跟随封面", ["Lighting.Fixed"] = "固定颜色",
        ["Lighting.Custom"] = "自定义",
        ["Lighting.Beat"] = "随节奏跳动", ["Lighting.Beat.Hint"] = "亮度跟随系统声音起伏，对任何播放器都有效。",

        ["Devices.Title"] = "设备", ["Devices.Subtitle"] = "屏幕连接、方向和驱动。",
        ["Devices.Connection.Hint"] = "长条屏走 USB 串口，方屏走原厂显示 SDK。",
        ["Devices.Long"] = "长条屏", ["Devices.Long.Hint"] = "1920×462 · USB 串口",
        ["Devices.Square"] = "方屏", ["Devices.Square.Hint"] = "480×480 · 显示 SDK",
        ["Devices.Rotation"] = "旋转 {0}°",
        ["Devices.BehaviorSection"] = "连接", ["Devices.AutoConnect"] = "启动时自动连接",
        ["Devices.AutoConnect.Hint"] = "打开软件后直接连接两块屏幕。",
        ["Devices.Takeover"] = "自动接管原厂程序", ["Devices.Takeover.Hint"] = "JONSBO-AIO 占用屏幕时直接把它关掉，不再询问。",
        ["Devices.ToolsSection"] = "工具", ["Devices.Driver"] = "USB 驱动", ["Devices.Repair"] = "检查并修复",
        ["Devices.SaveFrame"] = "保存当前画面", ["Devices.SaveFrame.Hint"] = "把长条屏现在的画面存成 PNG。", ["Devices.Save"] = "保存",
        ["Devices.Saved"] = "画面已保存到 {0}", ["Devices.SaveFailed"] = "保存失败：{0}",
        ["Devices.Logs"] = "日志与数据", ["Devices.Logs.Hint"] = "配置、日志和壁纸缓存所在的文件夹。",
        ["Driver.Ready"] = "USB 驱动正常（MS USB Display / libusb0）",
        ["Driver.Repairing"] = "正在检查并修复 USB 驱动…", ["Driver.RepairFailed"] = "驱动修复失败：{0}",

        ["Settings.Title"] = "设置", ["Settings.GeneralSection"] = "通用", ["Settings.Language"] = "语言",
        ["Settings.StartupSection"] = "启动与后台",
        ["Settings.StartWithWindows"] = "开机启动", ["Settings.StartWithWindows.Hint"] = "登录后以管理员权限运行，才能读取 CPU 温度；开启时需要确认一次授权。",
        ["Settings.StartHidden"] = "开机后隐藏到托盘", ["Settings.StartHidden.Hint"] = "只对开机启动生效，手动打开仍显示窗口。",
        ["Settings.Tray"] = "关闭窗口时留在托盘", ["Settings.Tray.Hint"] = "屏幕和灯光继续更新；窗口关闭后会释放界面占用的内存。",
        ["Settings.AboutSection"] = "关于", ["Settings.About"] = "版本 {0}", ["Settings.DataFolder"] = "数据文件夹",

        ["Status.ResourceFailed"] = "运行资源初始化失败：{0}",
        ["Status.StartupFailed"] = "开机启动未开启：需要在管理员授权中选择“是”",
        ["Status.StartupCleanupFailed"] = "已关闭开机启动，但计划任务清理失败，请查看日志",

        ["Tray.Tooltip"] = "My Canvas", ["Tray.Open"] = "打开 My Canvas", ["Tray.Exit"] = "退出",
        ["Tray.Running.Title"] = "My Canvas 仍在运行", ["Tray.Running.Message"] = "屏幕和灯光会继续更新。单击托盘图标重新打开。",
    };

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["App.Subtitle"] = "USB screens and lighting",
        ["Nav.Home"] = "Overview", ["Nav.Content"] = "Content", ["Nav.Wallpaper"] = "Wallpaper",
        ["Nav.Lighting"] = "Lighting", ["Nav.Devices"] = "Devices", ["Nav.Settings"] = "Settings",
        ["Common.Off"] = "Off", ["Common.Cancel"] = "Cancel", ["Common.NotNow"] = "Not now", ["Common.OpenFolder"] = "Open folder",

        ["Home.Title"] = "Overview", ["Home.Online"] = "{0} of 2 screens online", ["Home.Showing"] = "showing {0}",
        ["Home.Long"] = "Long screen", ["Home.Square"] = "Square screen",
        ["Home.NowPlaying"] = "Now playing", ["Home.Lighting"] = "Case lighting", ["Home.Wallpaper"] = "Wallpaper",

        ["Mode.Auto"] = "Auto", ["Mode.Hardware"] = "Hardware", ["Mode.Music"] = "Music", ["Mode.Clock"] = "Clock",

        ["State.Connected"] = "Connected", ["State.ConnectedOn"] = "Connected · {0}", ["State.Searching"] = "Looking for device…",
        ["State.NotResponding"] = "Not responding", ["State.Off"] = "Not connected", ["State.Demo"] = "Demo mode",
        ["State.NotRespondingHint"] = "The long screen stopped answering. Unplugging and replugging its USB cable usually fixes it; the app reconnects by itself.",

        ["Connection.Connect"] = "Connect", ["Connection.Disconnect"] = "Disconnect",
        ["Connection.InUse"] = "JONSBO-AIO is using the screens",
        ["Connection.TakeoverFailed"] = "Couldn't close JONSBO-AIO; quit it yourself and connect again",
        ["Connection.FailedCode"] = "Connection failed, code {0}", ["Connection.Failed"] = "Connection failed: {0}",
        ["Connection.ResumeFailedCode"] = "Couldn't recover after sleep, code {0}",
        ["Connection.Takeover.Title"] = "Take over the USB screens?",
        ["Connection.Takeover.Content"] = "JONSBO-AIO is using the screens. My Canvas can connect once it is closed.",
        ["Connection.Takeover.Primary"] = "Close and take over",

        ["Music.State.Off"] = "NetEase not connected", ["Music.State.Connecting"] = "Connecting…",
        ["Music.State.Waiting"] = "Connected, waiting for a song", ["Music.State.Synced"] = "Syncing",
        ["Music.Connect"] = "Connect NetEase", ["Music.Recheck"] = "Check again",
        ["Music.Playing"] = "Playing", ["Music.Paused"] = "Paused",
        ["Music.Hint"] = "Play a song in NetEase Cloud Music to show its lyrics",
        ["Music.Reconnected"] = "Reconnected; lyrics sync when playback starts",
        ["Music.RecheckFailed"] = "Check failed; restart NetEase sync",
        ["Music.ConnectedStatus"] = "NetEase connected; progress and lyrics sync while playing",
        ["Music.StillDisconnected"] = "NetEase still isn't connected; make sure it reopened and try again",
        ["Music.SyncFailed"] = "NetEase sync failed: {0}",
        ["Music.Restart.Title"] = "Restart NetEase to enable live sync?",
        ["Music.Restart.Content"] = "NetEase Cloud Music was started without its live sync port. It needs to close and reopen; playback stops briefly.",
        ["Music.Restart.Primary"] = "Restart and sync",

        ["Content.Title"] = "Content", ["Content.Subtitle"] = "How lyrics, the clock and hardware readings look on the screens.",
        ["Content.Music"] = "Music", ["Content.Sync"] = "NetEase live sync",
        ["Content.Sync.Hint"] = "Only reads playback progress and lyrics; never changes what is playing.",
        ["Content.Lyrics"] = "Lyric style", ["Content.Lyrics.Hint"] = "Lyric effect on the long screen; colours come from the album art.",
        ["Content.Fps"] = "Animation frame rate", ["Content.Fps.Hint"] = "Smoother at higher rates, at the cost of CPU and USB load.",
        ["Content.Clock"] = "Clock", ["Content.Clock24"] = "24-hour time", ["Content.Clock24.Hint"] = "Time format on the square screen clock.",
        ["Content.Hardware"] = "Hardware monitor", ["Content.Refresh"] = "Refresh interval", ["Content.Refresh.Hint"] = "How often CPU, GPU, memory and temperatures are sampled.",
        ["Content.ModesHint"] = "Choose what each screen shows on the Overview page.",
        ["Lyrics.apple"] = "Flow · rolling lines", ["Lyrics.spotify"] = "Flat · bold highlight", ["Lyrics.bigtype"] = "Big type · one line",
        ["Lyrics.lumiere"] = "Lumière · glowing words", ["Lyrics.fume"] = "Wall · whole song", ["Lyrics.partita"] = "Steps · staggered",
        ["Lyrics.cadenza"] = "Scatter · halo", ["Lyrics.tilt"] = "Tilt · two lines",
        ["Fps.5"] = "5 FPS · light", ["Fps.8"] = "8 FPS · recommended", ["Fps.10"] = "10 FPS", ["Fps.15"] = "15 FPS", ["Fps.20"] = "20 FPS · smoothest",
        ["Refresh.500"] = "0.5 s", ["Refresh.900"] = "0.9 s · recommended", ["Refresh.2000"] = "2 s · light",

        ["Wallpaper.Title"] = "Wallpaper", ["Wallpaper.Subtitle"] = "The photo behind the long screen and the square clock; it can also become your desktop.",
        ["Wallpaper.Next"] = "Next", ["Wallpaper.Open"] = "Show file", ["Wallpaper.Loading"] = "Fetching a picture…",
        ["Wallpaper.Fetching"] = "Fetching a new picture…", ["Wallpaper.Failed"] = "Couldn't load a picture; check the network or try another JPG / PNG",
        ["Wallpaper.History"] = "Recent pictures", ["Wallpaper.SourceSection"] = "Source",
        ["Wallpaper.Source"] = "Picture source", ["Wallpaper.Source.Hint"] = "Bing changes daily; Scenery picks a darker landscape every hour.",
        ["Wallpaper.File"] = "Custom picture", ["Wallpaper.Choose"] = "Choose…", ["Wallpaper.None"] = "No picture chosen",
        ["Wallpaper.DialogTitle"] = "Choose a wallpaper", ["Wallpaper.SyncSection"] = "Sync with Windows",
        ["Wallpaper.Desktop"] = "Use as desktop wallpaper", ["Wallpaper.Desktop.Hint"] = "The desktop changes with the photo; your own wallpaper returns when this is off.",
        ["Wallpaper.Accent"] = "Accent colour follows wallpaper", ["Wallpaper.Accent.Hint"] = "A muted colour from the photo becomes the Windows and app accent; yours returns when this is off.",
        ["Source.bing"] = "Bing daily", ["Source.wallhaven"] = "Scenery", ["Source.custom"] = "Custom",

        ["Lighting.Title"] = "Lighting", ["Lighting.Subtitle"] = "Case fans, cooler and strips (Gigabyte motherboard ARGB).",
        ["Lighting.Enable"] = "Lighting sync", ["Lighting.Enable.Hint"] = "Off; the case keeps the motherboard's default effect",
        ["Lighting.Current"] = "Current colour {0}", ["Lighting.Brightness"] = "Brightness",
        ["Lighting.HardwareSection"] = "Hardware and clock", ["Lighting.MusicSection"] = "While music plays",
        ["Lighting.Source"] = "Colour source",
        ["Lighting.Hardware.Hint"] = "Following the wallpaper, darker photos give dimmer light (never above the brightness above).",
        ["Lighting.Music.Hint"] = "Following the cover, each song switches to its album art colour.",
        ["Lighting.FromWallpaper"] = "From wallpaper", ["Lighting.FromCover"] = "From cover", ["Lighting.Fixed"] = "Fixed colour",
        ["Lighting.Custom"] = "Custom",
        ["Lighting.Beat"] = "Pulse with the beat", ["Lighting.Beat.Hint"] = "Brightness follows the system audio level; works with any player.",

        ["Devices.Title"] = "Devices", ["Devices.Subtitle"] = "Screen connection, orientation and driver.",
        ["Devices.Connection.Hint"] = "The long screen uses a USB serial port; the square screen uses the vendor display SDK.",
        ["Devices.Long"] = "Long screen", ["Devices.Long.Hint"] = "1920×462 · USB serial",
        ["Devices.Square"] = "Square screen", ["Devices.Square.Hint"] = "480×480 · display SDK",
        ["Devices.Rotation"] = "Rotate {0}°",
        ["Devices.BehaviorSection"] = "Connection", ["Devices.AutoConnect"] = "Connect on start",
        ["Devices.AutoConnect.Hint"] = "Connect both screens as soon as the app opens.",
        ["Devices.Takeover"] = "Take over from JONSBO-AIO", ["Devices.Takeover.Hint"] = "Close the vendor app without asking when it holds the screens.",
        ["Devices.ToolsSection"] = "Tools", ["Devices.Driver"] = "USB driver", ["Devices.Repair"] = "Check and repair",
        ["Devices.SaveFrame"] = "Save current frame", ["Devices.SaveFrame.Hint"] = "Save what the long screen shows now as a PNG.", ["Devices.Save"] = "Save",
        ["Devices.Saved"] = "Frame saved to {0}", ["Devices.SaveFailed"] = "Save failed: {0}",
        ["Devices.Logs"] = "Logs and data", ["Devices.Logs.Hint"] = "Folder with the settings, log and wallpaper cache.",
        ["Driver.Ready"] = "USB driver ready (MS USB Display / libusb0)",
        ["Driver.Repairing"] = "Checking and repairing the USB driver…", ["Driver.RepairFailed"] = "Driver repair failed: {0}",

        ["Settings.Title"] = "Settings", ["Settings.GeneralSection"] = "General", ["Settings.Language"] = "Language",
        ["Settings.StartupSection"] = "Startup and background",
        ["Settings.StartWithWindows"] = "Start with Windows", ["Settings.StartWithWindows.Hint"] = "Runs as administrator after sign-in so CPU temperature can be read; asks for approval once.",
        ["Settings.StartHidden"] = "Start hidden in the tray", ["Settings.StartHidden.Hint"] = "Only for automatic start; opening it yourself still shows the window.",
        ["Settings.Tray"] = "Keep running in the tray when closed", ["Settings.Tray.Hint"] = "Screens and lighting keep updating; closing the window frees its memory.",
        ["Settings.AboutSection"] = "About", ["Settings.About"] = "Version {0}", ["Settings.DataFolder"] = "Data folder",

        ["Status.ResourceFailed"] = "Runtime resources failed to initialize: {0}",
        ["Status.StartupFailed"] = "Start with Windows is off: choose Yes in the administrator prompt",
        ["Status.StartupCleanupFailed"] = "Start with Windows is off, but the scheduled task couldn't be removed; see the log",

        ["Tray.Tooltip"] = "My Canvas", ["Tray.Open"] = "Open My Canvas", ["Tray.Exit"] = "Exit",
        ["Tray.Running.Title"] = "My Canvas is still running", ["Tray.Running.Message"] = "Screens and lighting keep updating. Click the tray icon to reopen.",
    };

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    public static bool IsEnglish => CurrentLanguage == "en-US";

    public static void SetLanguage(string language) =>
        CurrentLanguage = JonsboCanvas.AppConfig.NormalizeLanguage(language);

    public static string Get(string key)
    {
        Dictionary<string, string> source = IsEnglish ? English : Chinese;
        return source.TryGetValue(key, out string? value) || Chinese.TryGetValue(key, out value) ? value : key;
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    internal static IEnumerable<string> MissingEnglish() => Chinese.Keys.Where(key => !English.ContainsKey(key));
}
