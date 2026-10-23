# 本对话代码改动记录

日期：2026-10-06  
项目：MyCanvas  
范围：仅记录本对话中实际讨论并由助手修改的代码，不包含工作区中其他未相关的差异。

## 0.1 ROG Azoth 键盘小屏壁纸导出（2026-10-21 追加）

文件：`source/JonsboCanvas.WinUI/Core/KeyboardDisplay.cs`
文件：`source/JonsboCanvas.WinUI/Pages/WallpaperPage.xaml`
文件：`source/JonsboCanvas.WinUI/Pages/WallpaperPage.xaml.cs`
文件：`source/JonsboCanvas.WinUI/Core/Localization.cs`
文件：`source/MyCanvas.Tests/MyCanvas.Tests.csproj`
文件：`source/MyCanvas.Tests/Program.cs`

检测到的键盘为 `ROG AZOTH 96 HE / LITE`（Armoury Crate 名称 `ROG 夜魔 98 HE / LITE`，接收器 `VID_0B05&PID_1AD0`）。它的OLED不是 Windows 显示器，而是由 Armoury Crate/KeyboardSDK 控制的键盘固件屏幕。Wallpaper 页面新增「ROG 键盘小屏 → 导出 184×97」，把当前 MyCanvas 壁纸缩放成键盘屏专用 PNG，保存到数据目录的 `keyboard` 文件夹并打开资源管理器定位。

已验证：Render 输出尺寸为 `184×97`，MyCanvas 测试 `64 checks passed`，WinUI Release 构建 `0 errors`。预览版已发布到 `outputs\MyCanvas-keyboard-preview` 并正常启动，使用 Bing 壁纸生成并检查了实际 PNG 内容。

限制：ASUS 未提供公开的第三方写屏头文件。`ArmouryKbSDK.dll` 为32位且依赖 Armoury Crate 运行时；独立进程调用其 `ExecuteFunction` 在实现内部崩溃，因此没有把不可靠的直写路径加入 MyCanvas，也不会把未验证的公开HID命令发到真实键盘。正式版本尚未覆盖部署，因为已在运行的正式 MyCanvas 需要管理员权限才能结束。

## 0. 方屏时钟页显示城市名（2026-10-11 追加）

文件：`source/JonsboCanvas.WinUI/Core/WeatherService.cs`
文件：`source/JonsboCanvas.WinUI/Core/DualLayoutRenderer.Hardware.cs`
文件：`source/JonsboCanvas.WinUI/Core/CanvasEngine.cs`
文件：`source/JonsboCanvas/AppConfig.cs`

小屏（方屏）硬件模式时钟页在天气状况行右侧新增「定位图钉 + 城市名」，与右下角湿度「水滴 + 数值」排版呼应；天气状况文字在城市名存在时自动缩短避免重叠。

城市名来源：

- 默认优先使用 Windows 系统定位（Wi-Fi/GPS，不受代理影响）；未授权或获取失败时回退到公网 IP 定位，ip-api 请求增加 `city` 字段，随定位一并返回。
- 配置的 `WeatherLocation` 为 `lat,lon` 时：走 BigDataCloud 免 key 逆地理编码取城市名（city → locality → principalSubdivision）。
- `WeatherLocation` 现在也支持直接填地名：通过 Open-Meteo 地理编码解析成坐标，并把匹配名作为城市名。

逆地理编码失败只影响城市标签，不影响天气刷新。`WeatherSnapshot` 增加 `City` 字段。捕获模式新增 `CaptureWeather()` 桩数据（与 `CaptureMusic()` 同模式），让截图显示真实感天气与城市。


## 1. 灯光跟随音乐播放状态

文件：`source/JonsboCanvas.WinUI/Core/CanvasEngine.cs`

灯光是否跟随音乐改为依据播放器是否正在播放，而不是显示器页面当前是否显示音乐页。这样用户切换到硬件信息画面时，音乐播放状态仍可以驱动灯光。

`needsMusic` 条件增加灯光启用判断，确保灯光开启时也采集音乐状态：

```csharp
bool needsMusic = Config.LightingEnabled || mode is "music" or "auto" || squareMode is "music" or "auto";
```

音乐播放状态统一判定：

```csharp
private bool MusicPlaying => _music?.Available == true && _music.Playing;
```

灯光循环与静态灯光均改用 `MusicPlaying`。灯光关闭时发送全灭输出；封面色缓存失效时重新提取；关闭封面色跟随时清理调色板缓存。音乐节拍循环增加每 3 秒一次的音频电平诊断日志。亮度变更和灯光设置变更也根据实时播放状态重新应用灯光。

核心亮度计算：

```csharp
byte brightness = (byte)Math.Clamp(
    Math.Round(Volatile.Read(ref _brightness) * 255 / 100.0 * level), 0, 255);
LightingController.SetColor(color.R, color.G, color.B, brightness);
```

## 2. 精选壁纸风格自定义

### 设置界面

文件：`source/JonsboCanvas.WinUI/Pages/WallpaperPage.xaml`  
文件：`source/JonsboCanvas.WinUI/Pages/WallpaperPage.xaml.cs`

精选风格由直接输入框改为风格选择按钮和自定义按钮。预设包括风景、动漫、赛博朋克、抽象、建筑。自定义关键词放在弹窗中编辑。

```csharp
private static readonly (string Tag, string Label, string Query)[] Styles =
{
    ("scenery", "风景", "landscape,mountains,space,forest,lake,night sky"),
    ("anime", "动漫", "anime,anime scenery,anime landscape"),
    ("cyberpunk", "赛博朋克", "cyberpunk,neon city,futuristic city"),
    ("abstract", "抽象", "abstract,geometric,minimalism"),
    ("architecture", "建筑", "architecture,modern architecture,interior"),
};
```

按钮选择预设时立即保存关键词并刷新精选结果。自定义弹窗确认后将输入内容保存；关键词用逗号分隔。

### 配置保存

文件：`source/JonsboCanvas/AppConfig.cs`

新增 `WallpaperQuery` 配置字段，默认关键词与原有风景池相同。读取旧配置文件时，如果没有该字段，会使用默认值。

```csharp
public string WallpaperQuery { get; set; }

// 默认值
WallpaperQuery = "landscape,mountains,space,forest,lake,night sky";
```

文件：`source/JonsboCanvas.WinUI/Core/CanvasEngine.cs`

启动时将配置交给壁纸服务；用户修改风格时更新服务、保存配置，在 Wallhaven 来源下立即请求新壁纸。

```csharp
public void SetWallpaperQuery(string query)
{
    Config.WallpaperQuery = string.IsNullOrWhiteSpace(query)
        ? "landscape,mountains,space,forest,lake,night sky"
        : query.Trim();
    _wallpaper.SetWallhavenQuery(Config.WallpaperQuery);
    SaveConfig();
    if (Config.WallpaperSource == "wallhaven") _wallpaper.Next();
    Invalidate();
}
```

## 3. Wallhaven 查询、筛选和 4K 限制

文件：`source/JonsboCanvas.WinUI/Core/WallpaperService.cs`

壁纸服务新增可变关键词列表。服务按逗号拆分关键词，并按小时轮换；空关键词时回退到默认列表。查询关键词变更会使当前快照失效并允许立即刷新。

```csharp
public void SetWallhavenQuery(string? query)
{
    string[] queries = (query ?? "").Split(
        ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    _wallhavenQueries = queries.Length > 0 ? queries : DefaultWallhavenQueries;
    Interlocked.Increment(ref _generation);
    Replace(ref _current, null);
    _nextCheckUtc = DateTime.MinValue;
}
```

曾经遇到“抽象”搜不到图。直接请求 Wallhaven 的 `abstract` 接口能返回结果，调查发现应用查询带有最低分辨率和比例约束，可能提前排除了可用结果。随后去掉 API URL 上的最低分辨率和宽高比条件，再由程序筛选至少 4K 的素材。

当前分辨率筛选：

```csharp
int width = item.TryGetProperty("dimension_x", out JsonElement widthElement)
    ? widthElement.GetInt32() : 0;
int height = item.TryGetProperty("dimension_y", out JsonElement heightElement)
    ? heightElement.GetInt32() : 0;
long size = item.TryGetProperty("file_size", out JsonElement s)
    ? s.GetInt64() : 0;
if (id == null || path == null || width < 3840 || height < 2160 || size > 30_000_000)
    continue;
```

这代表素材至少为 `3840 × 2160`，且下载文件不超过 30 MB。随后仍会优先选择较暗图片以保证屏幕文字可读；如果没有暗色候选则使用普通候选。

Wallhaven 请求当前只限定安全级别、分类、排序和时间范围：

```csharp
string url = "https://wallhaven.cc/api/v1/search?q=" + Uri.EscapeDataString(query) +
    "&categories=100&purity=100&sorting=toplist&topRange=1y&page=" +
    page.ToString(CultureInfo.InvariantCulture);
```

## 4. 本对话验证与发布记录

- 对 `abstract` 搜索做过一次在线接口验证：Wallhaven 返回 HTTP 200 和搜索结果。
- WinUI 项目构建通过。
- 使用项目现有的 `source/publish.ps1` 发布到 `C:\Code\MyCanvas\app`，脚本输出过 `published to C:\Code\MyCanvas\app and restarted`。
- 最后一次将筛选提高到 4K 后，发布脚本也完成并报告成功，编译成功；输出有项目原有警告。
- 没有为这份改动创建测试文件，也没有新增 PowerShell 脚本。
- 未在这段对话中完成 GUI 自动化操作来逐个点击验证每个预设；4K 数据筛选的实际 Wallhaven 下载仍需在应用里选风格并检查结果。

## 5. 手动复现

选择壁纸来源“精选风景”，然后选择任一风格，例如“抽象”。应用会搜索该风格的 Wallhaven 结果，并只下载至少 4K、文件不超过 30 MB 的素材。自定义风格在“自定义风格”弹窗中编辑。

重新发布使用项目已有脚本：

```powershell
C:\Code\MyCanvas\source\publish.ps1
```

## 6. 键盘休眠与主机静态灯去重（2026-10-23）

文件：`source/JonsboCanvas.WinUI/Core/KeyboardHidController.cs`
文件：`source/JonsboCanvas.WinUI/Core/KeyboardLightDedupPolicy.cs`
文件：`source/JonsboCanvas.WinUI/Core/KeyboardRetryBackoffPolicy.cs`
文件：`source/JonsboCanvas.WinUI/Core/LightingController.cs`
文件：`source/JonsboCanvas.WinUI/Core/CaseLightDedupPolicy.cs`
文件：`source/JonsboCanvas.WinUI/Core/CanvasEngine.cs`

硬件静态灯不再按固定时间重复写入。键盘灯和主机机箱灯都只在颜色/亮度变化时写一次；OLED 上传后仍恢复当前灯效，接收器重连后允许重新下发。音乐律动通过独立的 `Pulse` 路径保持实时刷新，不受到静态灯去重影响。

无线键盘断开时，灯效和壁纸重试使用指数退避，依次从 15 秒延长到最长 5 分钟，避免持续 HID 活动阻止键盘进入省电状态。主机两个屏幕的渲染和推送节奏未改动，继续按 `RefreshMilliseconds` 更新时间、天气和设备信息。

验证：`MyCanvas.Tests` 86 项通过，WinUI Release 构建 0 错误；部署后确认两个主机屏幕的发送计数继续增长，键盘灯进入休眠后再次操作可恢复屏幕和灯效。

