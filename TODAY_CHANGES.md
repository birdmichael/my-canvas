# 本对话代码改动记录

日期：2026-10-06  
项目：MyCanvas  
范围：仅记录本对话中实际讨论并由助手修改的代码，不包含工作区中其他未相关的差异。

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

