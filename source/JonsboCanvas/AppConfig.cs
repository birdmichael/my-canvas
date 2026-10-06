using System;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace JonsboCanvas
{
    public sealed class AppConfig
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Accent { get; set; }
        public string AccentSecondary { get; set; }
        public string BackgroundTop { get; set; }
        public string BackgroundBottom { get; set; }
        public string Text { get; set; }
        public string MutedText { get; set; }
        public string BackgroundImage { get; set; }
        public string ThemePreset { get; set; }
        public int RefreshMilliseconds { get; set; }
        public bool AutoConnect { get; set; }
        public bool ShowSeconds { get; set; }
        public bool Use24HourClock { get; set; }
        public int RotationDegrees { get; set; }
        public bool EnableCpuidSensors { get; set; }
        public bool StartWithWindows { get; set; }
        public bool AutoTakeOverOriginalApp { get; set; }
        public string DisplayMode { get; set; }
        public int AnimationFrameMilliseconds { get; set; }
        public int MusicFrameRate { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool StartHiddenOnAutoStart { get; set; }
        public double BackgroundOpacity { get; set; }
        public int NeteaseDebugPort { get; set; }
        public string Language { get; set; }
        public string SquareDisplayMode { get; set; }
        public string SerialPortName { get; set; }
        public int LongRotationDegrees { get; set; }
        public int SquareRotationDegrees { get; set; }
        public string PreviewDisplay { get; set; }
        public bool LightingEnabled { get; set; }
        public string LightingHardwareColor { get; set; }
        public string LightingMusicColor { get; set; }
        public int LightingBrightness { get; set; }
        public bool LightingBeatSync { get; set; }
        public bool LightingCoverColor { get; set; }
        // Outside music, take the light's colour and brightness from the wallpaper.
        public bool LightingWallpaperColor { get; set; }
        // "lat,lon"; empty locates by public IP.
        public string WeatherLocation { get; set; }
        // Hardware-mode photo: "bing" (image of the day), "wallhaven" (hourly scenery) or "custom" (WallpaperPath).
        public string WallpaperSource { get; set; }
        public string WallpaperPath { get; set; }
        public string WallpaperQuery { get; set; }
        // Also use the photo as the Windows desktop background; the user's own one is restored when turned off.
        public bool DesktopWallpaperSync { get; set; }
        public string DesktopWallpaperOriginal { get; set; }
        // Windows accent colour follows the lighting colour; the user's own one is restored when turned off.
        public bool AccentColorSync { get; set; }
        public string AccentColorOriginal { get; set; }
        // Long-screen lyrics: "apple" (flowing cover colours), "spotify" (flat colour) or "bigtype" (one huge line).
        public string LyricStyle { get; set; }

        public AppConfig()
        {
            Title = "MY CANVAS";
            Subtitle = "USB 硬件状态";
            Accent = "#54F4FF";
            AccentSecondary = "#A875FF";
            BackgroundTop = "#07131C";
            BackgroundBottom = "#03070C";
            Text = "#ECFBFF";
            MutedText = "#7793A3";
            BackgroundImage = @"themes\cyber-cyan.png";
            ThemePreset = "cyber-cyan";
            RefreshMilliseconds = 900;
            AutoConnect = true;
            ShowSeconds = true;
            Use24HourClock = true;
            RotationDegrees = 90;
            EnableCpuidSensors = true;
            StartWithWindows = false;
            AutoTakeOverOriginalApp = true;
            DisplayMode = "auto";
            AnimationFrameMilliseconds = 250;
            MusicFrameRate = 8;
            MinimizeToTray = true;
            StartHiddenOnAutoStart = true;
            BackgroundOpacity = 0.38;
            NeteaseDebugPort = 38476;
            Language = "zh-CN";
            SquareDisplayMode = "clock";
            SerialPortName = "";
            LongRotationDegrees = 270;
            SquareRotationDegrees = 90;
            PreviewDisplay = "long";
            LightingEnabled = true;
            LightingHardwareColor = "#FF6B35";
            LightingMusicColor = "#8B5CF6";
            LightingBrightness = 100;
            LightingBeatSync = true;
            LightingCoverColor = true;
            LightingWallpaperColor = true;
            WeatherLocation = "";
            WallpaperSource = "bing";
            WallpaperPath = "";
            WallpaperQuery = "landscape,mountains,space,forest,lake,night sky";
            DesktopWallpaperSync = true;
            DesktopWallpaperOriginal = "";
            AccentColorSync = true;
            AccentColorOriginal = "";
            LyricStyle = "apple";
        }

        public static readonly string[] LyricStyles =
            { "apple", "spotify", "bigtype", "lumiere", "fume", "partita", "cadenza", "tilt" };

        public static string NormalizeLyricStyle(string style) =>
            Array.IndexOf(LyricStyles, style) >= 0 ? style : "apple";

        public static AppConfig Load(string path)
        {
            AppConfig defaults = new AppConfig();
            try
            {
                if (!File.Exists(path))
                {
                    Save(path, defaults);
                    return defaults;
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = File.ReadAllText(path);
                AppConfig loaded = serializer.Deserialize<AppConfig>(json);
                if (loaded == null)
                    return defaults;

                loaded.Title = OrDefault(loaded.Title, defaults.Title);
                if (string.Equals(loaded.Title, "NEXUS // SYSTEM", StringComparison.OrdinalIgnoreCase))
                    loaded.Title = defaults.Title;
                loaded.Subtitle = OrDefault(loaded.Subtitle, defaults.Subtitle);
                loaded.Accent = OrDefault(loaded.Accent, defaults.Accent);
                loaded.AccentSecondary = OrDefault(loaded.AccentSecondary, defaults.AccentSecondary);
                loaded.BackgroundTop = OrDefault(loaded.BackgroundTop, defaults.BackgroundTop);
                loaded.BackgroundBottom = OrDefault(loaded.BackgroundBottom, defaults.BackgroundBottom);
                loaded.Text = OrDefault(loaded.Text, defaults.Text);
                loaded.MutedText = OrDefault(loaded.MutedText, defaults.MutedText);
                if (loaded.BackgroundImage == null)
                    loaded.BackgroundImage = "";
                loaded.ThemePreset = OrDefault(loaded.ThemePreset, defaults.ThemePreset);
                loaded.DisplayMode = OrDefault(loaded.DisplayMode, defaults.DisplayMode).ToLowerInvariant();
                if (loaded.DisplayMode != "auto" && loaded.DisplayMode != "hardware" && loaded.DisplayMode != "music")
                    loaded.DisplayMode = "hardware";
                loaded.AnimationFrameMilliseconds = Math.Max(250, Math.Min(1000,
                    loaded.AnimationFrameMilliseconds <= 0 ? defaults.AnimationFrameMilliseconds : loaded.AnimationFrameMilliseconds));
                loaded.MusicFrameRate = Math.Max(2, Math.Min(20,
                    loaded.MusicFrameRate <= 0 ? defaults.MusicFrameRate : loaded.MusicFrameRate));
                if (loaded.BackgroundOpacity <= 0 || loaded.BackgroundOpacity > 1)
                    loaded.BackgroundOpacity = defaults.BackgroundOpacity;
                if (loaded.NeteaseDebugPort < 1024 || loaded.NeteaseDebugPort > 65535)
                    loaded.NeteaseDebugPort = defaults.NeteaseDebugPort;
                loaded.Language = NormalizeLanguage(loaded.Language);
                loaded.SquareDisplayMode = OrDefault(loaded.SquareDisplayMode, defaults.SquareDisplayMode);
                if (loaded.SquareDisplayMode == "codex" || loaded.SquareDisplayMode == "claude") loaded.SquareDisplayMode = "hardware";
                if (loaded.SquareDisplayMode != "clock" && loaded.SquareDisplayMode != "auto" && loaded.SquareDisplayMode != "hardware" && loaded.SquareDisplayMode != "music")
                    loaded.SquareDisplayMode = defaults.SquareDisplayMode;
                loaded.SerialPortName = loaded.SerialPortName ?? "";
                loaded.WeatherLocation = loaded.WeatherLocation ?? "";
                loaded.WallpaperPath = loaded.WallpaperPath ?? "";
                loaded.WallpaperQuery = OrDefault(loaded.WallpaperQuery, defaults.WallpaperQuery);
                loaded.DesktopWallpaperOriginal = loaded.DesktopWallpaperOriginal ?? "";
                loaded.AccentColorOriginal = loaded.AccentColorOriginal ?? "";
                if (loaded.WallpaperSource != "custom" && loaded.WallpaperSource != "wallhaven")
                    loaded.WallpaperSource = "bing";
                loaded.LyricStyle = NormalizeLyricStyle(loaded.LyricStyle);
                if (loaded.LongRotationDegrees != 90 && loaded.LongRotationDegrees != 270)
                    loaded.LongRotationDegrees = defaults.LongRotationDegrees;
                if (json.IndexOf("\"SquareRotationDegrees\"", StringComparison.OrdinalIgnoreCase) < 0 ||
                    (loaded.SquareRotationDegrees != 0 && loaded.SquareRotationDegrees != 90 && loaded.SquareRotationDegrees != 180 && loaded.SquareRotationDegrees != 270))
                    loaded.SquareRotationDegrees = defaults.SquareRotationDegrees;
                loaded.PreviewDisplay = loaded.PreviewDisplay == "square" ? "square" : "long";
                if (json.IndexOf("\"StartWithWindows\"", StringComparison.OrdinalIgnoreCase) < 0)
                    loaded.StartWithWindows = defaults.StartWithWindows;
                if (json.IndexOf("\"AutoTakeOverOriginalApp\"", StringComparison.OrdinalIgnoreCase) < 0)
                    loaded.AutoTakeOverOriginalApp = defaults.AutoTakeOverOriginalApp;
                if (json.IndexOf("\"MinimizeToTray\"", StringComparison.OrdinalIgnoreCase) < 0)
                    loaded.MinimizeToTray = defaults.MinimizeToTray;
                if (json.IndexOf("\"StartHiddenOnAutoStart\"", StringComparison.OrdinalIgnoreCase) < 0)
                    loaded.StartHiddenOnAutoStart = defaults.StartHiddenOnAutoStart;
                loaded.RefreshMilliseconds = Math.Max(250, Math.Min(10000,
                    loaded.RefreshMilliseconds <= 0 ? defaults.RefreshMilliseconds : loaded.RefreshMilliseconds));
                if (loaded.RotationDegrees != 0 && loaded.RotationDegrees != 90 &&
                    loaded.RotationDegrees != 180 && loaded.RotationDegrees != 270)
                    loaded.RotationDegrees = defaults.RotationDegrees;
                return loaded;
            }
            catch (Exception exception)
            {
                Log.Write("Config load failed: " + exception);
                return defaults;
            }
        }

        public static void Save(string path, AppConfig config)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(path, serializer.Serialize(config));
        }

        public Color GetColor(string value, Color fallback)
        {
            try
            {
                return ColorTranslator.FromHtml(value);
            }
            catch
            {
                return fallback;
            }
        }

        public void ApplyPreset(string preset)
        {
            ThemePreset = string.IsNullOrWhiteSpace(preset) ? "cyber-cyan" : preset;
            Text = "#ECFBFF";
            switch (ThemePreset)
            {
                case "molten-amber":
                    Accent = "#FFB347";
                    AccentSecondary = "#FF5A36";
                    BackgroundTop = "#120B07";
                    BackgroundBottom = "#050302";
                    MutedText = "#B99A85";
                    break;
                case "aurora-violet":
                    Accent = "#A875FF";
                    AccentSecondary = "#6D8CFF";
                    BackgroundTop = "#0D0A20";
                    BackgroundBottom = "#04040B";
                    MutedText = "#9A93B8";
                    break;
                case "animated-cyan":
                    Accent = "#54F4FF";
                    AccentSecondary = "#6D8CFF";
                    BackgroundTop = "#07131C";
                    BackgroundBottom = "#03070C";
                    MutedText = "#7793A3";
                    break;
                default:
                    ThemePreset = "cyber-cyan";
                    Accent = "#54F4FF";
                    AccentSecondary = "#A875FF";
                    BackgroundTop = "#07131C";
                    BackgroundBottom = "#03070C";
                    MutedText = "#7793A3";
                    break;
            }
            BackgroundImage = Path.Combine("themes", ThemePreset +
                (ThemePreset == "animated-cyan" ? ".gif" : ".png"));
        }

        private static string OrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        public static string NormalizeLanguage(string value)
        {
            return AppLanguage.Normalize(value);
        }
    }
}
