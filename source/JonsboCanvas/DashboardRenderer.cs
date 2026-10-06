using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace JonsboCanvas
{
    internal sealed class DashboardRenderer : IDisposable
    {
        public const int CanvasWidth = 960;
        public const int CanvasHeight = 376;

        private readonly AppConfig _config;
        private readonly List<float> _cpuHistory = new List<float>();
        private readonly List<float> _gpuHistory = new List<float>();
        private readonly Dictionary<string, float> _fittedFontSizes =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private Image _backgroundImage;
        private MemoryStream _backgroundStream;
        private EventHandler _animationHandler;
        private Bitmap _musicBase;
        private Image _codexPetStrip;
        private MemoryStream _codexPetStream;
        private Image _claudeCompanion;
        private MemoryStream _claudeCompanionStream;
        private string _musicBaseKey = "";
        private string _lastRenderedLyric = "";
        private DateTime _lyricChangedUtc = DateTime.MinValue;
        private Color _accent;
        private Color _secondary;
        private Color _backgroundTop;
        private Color _backgroundBottom;
        private Color _text;
        private Color _muted;
        private bool English { get { return AppConfig.NormalizeLanguage(_config.Language) == "en-US"; } }

        public DashboardRenderer(AppConfig config)
        {
            _config = config;
            _accent = config.GetColor(config.Accent, Color.FromArgb(84, 244, 255));
            _secondary = config.GetColor(config.AccentSecondary, Color.FromArgb(168, 117, 255));
            _backgroundTop = config.GetColor(config.BackgroundTop, Color.FromArgb(7, 19, 28));
            _backgroundBottom = config.GetColor(config.BackgroundBottom, Color.FromArgb(3, 7, 12));
            _text = config.GetColor(config.Text, Color.FromArgb(236, 251, 255));
            _muted = config.GetColor(config.MutedText, Color.FromArgb(119, 147, 163));
            LoadBackground();
            LoadCompanionAssets();
        }

        public Bitmap Render(MetricsSnapshot metrics)
        {
            AddHistory(_cpuHistory, (float)metrics.CpuUsage);
            AddHistory(_gpuHistory, (float)(metrics.GpuUsage ?? 0));

            Bitmap bitmap = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                DrawBackground(graphics);
                DrawMinimalHeader(graphics);
                DrawMinimalClock(graphics, metrics);
                DrawMinimalMetricCard(graphics, new RectangleF(333, 66, 293, 286), "CPU",
                    metrics.CpuUsage, metrics.CpuTemperature, _accent);
                DrawMinimalMetricCard(graphics, new RectangleF(642, 66, 294, 286), "GPU",
                    metrics.GpuUsage ?? 0, metrics.GpuTemperature, _secondary);
            }
            return bitmap;
        }

        public Bitmap RenderMusic(MusicSnapshot music)
        {
            string currentLyric = music == null ? T("打开网易云音乐并开始播放", "Open NetEase Cloud Music and start playback") : music.CurrentLyric;
            if (!string.Equals(currentLyric, _lastRenderedLyric, StringComparison.Ordinal))
            {
                _lastRenderedLyric = currentLyric;
                _lyricChangedUtc = DateTime.UtcNow;
            }

            EnsureMusicBase(music);
            Bitmap bitmap = new Bitmap(_musicBase);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                RectangleF record = new RectangleF(50, 40, 292, 292);
                DrawRotatingRecordLabel(graphics, music, record);
                DrawTonearm(graphics, music);
                DrawSyncedLyrics(graphics, music);
                DrawMusicTimeline(graphics, music, new RectangleF(406, 309, 518, 38));
            }
            return bitmap;
        }

        public Bitmap RenderCoding(CodingUsageSnapshot usage, bool codex)
        {
            if (usage == null)
                usage = new CodingUsageSnapshot { Provider = codex ? "CODEX" : "CLAUDE" };
            Color providerColor = codex ? Color.FromArgb(71, 221, 255) : Color.FromArgb(255, 157, 45);
            Color weeklyColor = codex ? Color.FromArgb(151, 121, 255) : Color.FromArgb(255, 204, 91);
            Bitmap bitmap = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawCodingBackdrop(graphics, providerColor, codex);
                DrawCodingCompanion(graphics, codex, providerColor);
                DrawCodingHeader(graphics, usage, codex, providerColor);
                DrawQuotaRow(graphics, new RectangleF(356, 63, 568, 124), "5H", T("5 小时窗口", "5-hour window"),
                    usage.ShortWindow, providerColor);
                DrawQuotaRow(graphics, new RectangleF(356, 204, 568, 124), "7D", T("7 天窗口", "7-day window"),
                    usage.WeeklyWindow, weeklyColor);
            }
            return bitmap;
        }

        private static void DrawCodingBackdrop(Graphics graphics, Color accent, bool codex)
        {
            Color top = codex ? Color.FromArgb(7, 12, 20) : Color.FromArgb(18, 10, 6);
            Color bottom = codex ? Color.FromArgb(2, 5, 10) : Color.FromArgb(5, 4, 3);
            using (LinearGradientBrush gradient = new LinearGradientBrush(
                new Rectangle(0, 0, CanvasWidth, CanvasHeight), top, bottom, 18f))
                graphics.FillRectangle(gradient, 0, 0, CanvasWidth, CanvasHeight);
            using (Pen grid = new Pen(Color.FromArgb(14, accent), 1f))
            {
                for (int x = 18; x < CanvasWidth; x += 42)
                    graphics.DrawLine(grid, x, 0, x, CanvasHeight);
                for (int y = 16; y < CanvasHeight; y += 42)
                    graphics.DrawLine(grid, 0, y, CanvasWidth, y);
            }
            using (Brush glow = new SolidBrush(Color.FromArgb(24, accent)))
                graphics.FillEllipse(glow, -75, 15, 420, 420);
            using (Brush shade = new SolidBrush(Color.FromArgb(58, 0, 0, 0)))
                graphics.FillRectangle(shade, 320, 0, 640, CanvasHeight);
        }

        private void DrawCodingCompanion(Graphics graphics, bool codex, Color accent)
        {
            float bob = (float)Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 620.0) * 5f;
            using (Brush halo = new SolidBrush(Color.FromArgb(23, accent)))
                graphics.FillEllipse(halo, 27, 55 + bob, 294, 255);
            using (Pen ring = new Pen(Color.FromArgb(68, accent), 1.3f))
                graphics.DrawEllipse(ring, 37, 65 + bob, 274, 235);

            if (codex && _codexPetStrip != null)
            {
                int frames = Math.Max(1, _codexPetStrip.Width / 192);
                int frame = (int)(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 520.0) % frames;
                RectangleF source = new RectangleF(frame * 192, 0, 192, 208);
                RectangleF target = new RectangleF(71, 72 + bob, 218, 236);
                InterpolationMode before = graphics.InterpolationMode;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.DrawImage(_codexPetStrip, target, source, GraphicsUnit.Pixel);
                graphics.InterpolationMode = before;
            }
            else if (!codex && _claudeCompanion != null)
            {
                RectangleF target = FitInside(_claudeCompanion, new RectangleF(55, 52 + bob, 255, 270));
                graphics.DrawImage(_claudeCompanion, target);
            }
            string companionName = codex
                ? "THE ORIGINAL CODEX COMPANION"
                : "ORIGINAL CLAUDE COMPANION";
            using (Font name = CreateFittingFont(graphics, companionName, 10, 7,
                FontStyle.Bold, 286))
            using (Brush nameBrush = new SolidBrush(Color.FromArgb(220, 241, 246, 249)))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
                graphics.DrawString(companionName, name, nameBrush,
                    new RectangleF(36, 326, 286, 23), center);
        }

        private void DrawCodingHeader(Graphics graphics, CodingUsageSnapshot usage, bool codex, Color accent)
        {
            string provider = codex ? "CODEX" : "CLAUDE";
            string detail = string.IsNullOrWhiteSpace(usage.Model)
                ? LocalizeUsageStatus(usage.Status) : usage.Model + (string.IsNullOrWhiteSpace(usage.Plan) ? "" : "  ·  " + usage.Plan.ToUpperInvariant());
            using (Font providerFont = CreateFont(12, FontStyle.Bold))
            using (Font detailFont = CreateFont(9, FontStyle.Regular))
            using (Brush accentBrush = new SolidBrush(accent))
            using (Brush detailBrush = new SolidBrush(Color.FromArgb(170, 197, 207, 216)))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
            {
                graphics.FillEllipse(accentBrush, 356, 29, 8, 8);
                graphics.DrawString(provider + "  USAGE", providerFont, accentBrush, 372, 20);
                graphics.DrawString(detail, detailFont, detailBrush, new RectangleF(562, 22, 362, 24), right);
            }
        }

        private void DrawQuotaRow(Graphics graphics, RectangleF bounds, string shortLabel,
            string label, UsageQuotaWindow quota, Color accent)
        {
            using (GraphicsPath panel = RoundedRectangle(bounds, 15))
            using (Brush fill = new SolidBrush(Color.FromArgb(180, 9, 14, 21)))
            using (Pen border = new Pen(Color.FromArgb(54, accent), 1f))
            {
                graphics.FillPath(fill, panel);
                graphics.DrawPath(border, panel);
            }

            double remaining = quota != null && quota.Available ? Clamp(100.0 - quota.UsedPercent, 0, 100) : 0;
            string value = quota != null && quota.Available ? remaining.ToString("0") + "%" : "--";
            string reset = quota != null && quota.Available ? FormatReset(quota.ResetAtLocal) : T("等待额度快照", "Waiting for snapshot");
            using (Font badge = CreateFont(13, FontStyle.Bold))
            using (Font labelFont = CreateFont(9, FontStyle.Bold))
            using (Font valueFont = CreateFont(29, FontStyle.Bold))
            using (Font remainingFont = CreateFont(8, FontStyle.Bold))
            using (Font resetFont = CreateFont(9, FontStyle.Regular))
            using (Brush accentBrush = new SolidBrush(accent))
            using (Brush textBrush = new SolidBrush(Color.FromArgb(242, 244, 248, 251)))
            using (Brush mutedBrush = new SolidBrush(Color.FromArgb(220, 184, 196, 207)))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
            {
                graphics.DrawString(shortLabel, badge, accentBrush, bounds.X + 20, bounds.Y + 17);
                graphics.DrawString(label, labelFont, mutedBrush, bounds.X + 20, bounds.Y + 49);
                graphics.DrawString(T("剩余", "REMAINING"), remainingFont, mutedBrush, bounds.X + 128, bounds.Y + 18);
                graphics.DrawString(value, valueFont, textBrush, bounds.X + 126, bounds.Y + 36);
                graphics.DrawString(reset, resetFont, mutedBrush,
                    new RectangleF(bounds.Right - 228, bounds.Y + 24, 204, 24), right);
                DrawProgress(graphics, new RectangleF(bounds.X + 128, bounds.Bottom - 27,
                    bounds.Width - 153, 8), remaining, accent);
            }
        }

        private string FormatReset(DateTime resetAt)
        {
            if (resetAt == DateTime.MinValue)
                return T("重置时间未知", "Reset time unknown");
            TimeSpan left = resetAt - DateTime.Now;
            if (left.TotalSeconds <= 0)
                return T("即将刷新", "Refreshing soon");
            if (left.TotalDays >= 1)
                return English ? "Reset  " + (int)left.TotalDays + "d " + left.Hours + "h" : "重置  " + (int)left.TotalDays + "天 " + left.Hours + "小时";
            if (left.TotalHours >= 1)
                return English ? "Reset  " + (int)left.TotalHours + "h " + left.Minutes + "m" : "重置  " + (int)left.TotalHours + "小时 " + left.Minutes + "分";
            return English ? "Reset  " + Math.Max(1, left.Minutes) + "m" : "重置  " + Math.Max(1, left.Minutes) + "分钟";
        }

        private static RectangleF FitInside(Image image, RectangleF bounds)
        {
            float scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            float width = image.Width * scale;
            float height = image.Height * scale;
            return new RectangleF(bounds.X + (bounds.Width - width) / 2f,
                bounds.Y + (bounds.Height - height) / 2f, width, height);
        }

        private void EnsureMusicBase(MusicSnapshot music)
        {
            string key = (music == null ? "" : music.SongId) + "|" +
                (music == null ? "" : music.Title) + "|" +
                (music == null ? "" : music.Artist) + "|" +
                (music != null && music.Realtime ? "R" : "S") + "|" +
                (music == null || music.Cover == null ? "0" : music.Cover.GetHashCode().ToString());
            if (_musicBase != null && string.Equals(key, _musicBaseKey, StringComparison.Ordinal))
                return;

            Bitmap nextBase = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(nextBase))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawMusicBackdrop(graphics, music);
                DrawVinylBase(graphics, new RectangleF(50, 40, 292, 292));

                string titleText = music == null ? T("等待网易云播放", "Waiting for NetEase playback") : music.Title;
                string artistText = music == null ? "NETEASE CLOUD MUSIC" : music.Artist;
                using (Font title = CreateFittingFont(graphics, titleText, 35, 24,
                    FontStyle.Bold, 515))
                using (Font artist = CreateFittingFont(graphics, artistText, 14, 10,
                    FontStyle.Regular, 360))
                using (Font tiny = CreateFont(8, FontStyle.Bold))
                using (Brush titleBrush = new SolidBrush(Color.FromArgb(245, 246, 246, 244)))
                using (Brush artistBrush = new SolidBrush(Color.FromArgb(220, 240, 240, 238)))
                using (Brush statusBrush = new SolidBrush(Color.FromArgb(210, 242, 242, 240)))
                using (StringFormat left = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                using (StringFormat right = new StringFormat
                {
                    Alignment = StringAlignment.Far,
                    LineAlignment = StringAlignment.Center
                })
                {
                    graphics.DrawString(titleText, title, titleBrush,
                        new RectangleF(405, 57, 515, 54), left);
                    graphics.DrawString(artistText, artist, artistBrush,
                        new RectangleF(407, 119, 390, 26), left);
                    string channel = music != null && music.Realtime ? T("逐行同步", "LIVE LYRICS") : T("等待同步", "WAITING");
                    graphics.FillEllipse(statusBrush, 842, 27, 7, 7);
                    graphics.DrawString(channel, tiny, statusBrush,
                        new RectangleF(850, 20, 73, 20), right);
                }
            }

            Bitmap previous = _musicBase;
            _musicBase = nextBase;
            _musicBaseKey = key;
            if (previous != null)
                previous.Dispose();
        }

        private void DrawMusicBackdrop(Graphics graphics, MusicSnapshot music)
        {
            graphics.Clear(Color.FromArgb(2, 5, 9));
            if (music != null && music.Cover != null)
            {
                using (Bitmap softened = new Bitmap(480, 188, PixelFormat.Format32bppArgb))
                using (Graphics small = Graphics.FromImage(softened))
                {
                    small.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    small.SmoothingMode = SmoothingMode.HighQuality;
                    RectangleF source = CropSource(music.Cover, new RectangleF(0, 0, 480, 188));
                    small.DrawImage(music.Cover, new RectangleF(0, 0, 480, 188), source, GraphicsUnit.Pixel);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(softened, new RectangleF(0, 0, CanvasWidth, CanvasHeight));
                }
            }

            using (Brush darken = new SolidBrush(Color.FromArgb(112, 0, 3, 8)))
                graphics.FillRectangle(darken, 0, 0, CanvasWidth, CanvasHeight);
            using (LinearGradientBrush leftShade = new LinearGradientBrush(
                new Rectangle(0, 0, CanvasWidth, CanvasHeight),
                Color.FromArgb(118, 0, 0, 0), Color.FromArgb(28, 0, 0, 0), 0f))
                graphics.FillRectangle(leftShade, 0, 0, CanvasWidth, CanvasHeight);
            using (LinearGradientBrush bottomShade = new LinearGradientBrush(
                new Rectangle(0, 140, CanvasWidth, 236),
                Color.Transparent, Color.FromArgb(145, 0, 1, 4), 90f))
                graphics.FillRectangle(bottomShade, 0, 140, CanvasWidth, 236);
        }

        private void DrawVinylBase(Graphics graphics, RectangleF bounds)
        {
            using (Brush shadow = new SolidBrush(Color.FromArgb(175, 0, 0, 0)))
                graphics.FillEllipse(shadow, bounds.X + 9, bounds.Y + 11, bounds.Width, bounds.Height);
            using (LinearGradientBrush disc = new LinearGradientBrush(bounds,
                Color.FromArgb(43, 46, 49), Color.FromArgb(2, 3, 5), 32f))
                graphics.FillEllipse(disc, bounds);
            using (Pen rim = new Pen(Color.FromArgb(85, 225, 225, 222), 1.2f))
                graphics.DrawEllipse(rim, bounds);
            using (Pen groove = new Pen(Color.FromArgb(34, 215, 215, 212), 1f))
            {
                for (int i = 0; i < 18; i++)
                {
                    float inset = 8 + i * 7.2f;
                    graphics.DrawEllipse(groove, bounds.X + inset, bounds.Y + inset,
                        bounds.Width - inset * 2, bounds.Height - inset * 2);
                }
            }
            RectangleF shine = new RectangleF(bounds.X + 5, bounds.Y + 5,
                bounds.Width - 10, bounds.Height - 10);
            using (Pen highlight = new Pen(Color.FromArgb(98, 245, 245, 242), 11f))
            using (Pen lowlight = new Pen(Color.FromArgb(36, 210, 210, 208), 3f))
            {
                graphics.DrawArc(highlight, shine, 214, 72);
                graphics.DrawArc(lowlight, shine, 28, 75);
            }
        }

        private void DrawRotatingRecordLabel(Graphics graphics, MusicSnapshot music, RectangleF bounds)
        {
            float labelInset = bounds.Width * 0.17f;
            RectangleF label = new RectangleF(bounds.X + labelInset, bounds.Y + labelInset,
                bounds.Width - labelInset * 2, bounds.Height - labelInset * 2);
            double seconds = music == null ? 0 : music.ElapsedSeconds;
            float angle = (float)((seconds * 20.0) % 360.0);
            if (music != null && music.Cover != null)
            {
                GraphicsState state = graphics.Save();
                using (GraphicsPath clip = new GraphicsPath())
                {
                    clip.AddEllipse(label);
                    graphics.SetClip(clip);
                    graphics.TranslateTransform(label.X + label.Width / 2, label.Y + label.Height / 2);
                    graphics.RotateTransform(angle);
                    graphics.TranslateTransform(-(label.X + label.Width / 2), -(label.Y + label.Height / 2));
                    RectangleF source = CropSource(music.Cover, label);
                    graphics.DrawImage(music.Cover, label, source, GraphicsUnit.Pixel);
                }
                graphics.Restore(state);
            }
            else
            {
                using (Brush empty = new SolidBrush(Color.FromArgb(38, 230, 230, 228)))
                    graphics.FillEllipse(empty, label);
            }

            using (Pen edge = new Pen(Color.FromArgb(88, 238, 238, 235), 1.2f))
                graphics.DrawEllipse(edge, label);
            RectangleF hub = new RectangleF(bounds.X + bounds.Width / 2 - 8,
                bounds.Y + bounds.Height / 2 - 8, 16, 16);
            using (Brush hubBrush = new SolidBrush(Color.FromArgb(232, 22, 23, 25)))
            using (Pen hubPen = new Pen(Color.FromArgb(190, 235, 235, 232), 1.4f))
            {
                graphics.FillEllipse(hubBrush, hub);
                graphics.DrawEllipse(hubPen, hub);
            }

            if (music != null && music.Playing)
            {
                RectangleF sweep = new RectangleF(bounds.X + 10, bounds.Y + 10,
                    bounds.Width - 20, bounds.Height - 20);
                using (Pen motion = new Pen(Color.FromArgb(38, 248, 248, 245), 3f))
                    graphics.DrawArc(motion, sweep, angle - 18, 42);
            }
        }

        private void DrawTonearm(Graphics graphics, MusicSnapshot music)
        {
            float motion = music != null && music.Playing
                ? (float)Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.8) * 1.2f : 0f;
            PointF end = new PointF(307 + motion, 192 + motion);
            using (GraphicsPath arm = new GraphicsPath())
            {
                arm.AddBezier(new PointF(343, -18), new PointF(344, 42),
                    new PointF(360, 96), end);
                using (Pen shadow = new Pen(Color.FromArgb(165, 0, 0, 0), 10f))
                using (Pen silver = new Pen(Color.FromArgb(245, 196, 198, 199), 6f))
                using (Pen highlight = new Pen(Color.FromArgb(235, 252, 252, 250), 2f))
                {
                    shadow.StartCap = shadow.EndCap = LineCap.Round;
                    silver.StartCap = silver.EndCap = LineCap.Round;
                    highlight.StartCap = highlight.EndCap = LineCap.Round;
                    graphics.DrawPath(shadow, arm);
                    graphics.DrawPath(silver, arm);
                    graphics.DrawPath(highlight, arm);
                }
            }

            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(end.X, end.Y);
            graphics.RotateTransform(-43f + motion * 0.4f);
            RectangleF cartridge = new RectangleF(-8, -7, 34, 17);
            using (GraphicsPath shell = RoundedRectangle(cartridge, 4f))
            using (Brush body = new SolidBrush(Color.FromArgb(248, 225, 226, 225)))
            using (Pen outline = new Pen(Color.FromArgb(185, 90, 92, 94), 1f))
            {
                graphics.FillPath(body, shell);
                graphics.DrawPath(outline, shell);
            }
            using (Pen slot = new Pen(Color.FromArgb(210, 38, 40, 42), 2f))
            {
                graphics.DrawLine(slot, 3, -2, 15, -2);
                graphics.DrawLine(slot, 3, 4, 11, 4);
            }
            using (Pen needle = new Pen(Color.FromArgb(230, 238, 238, 236), 1.4f))
                graphics.DrawLine(needle, 24, 7, 34, 13);
            graphics.Restore(state);
        }

        private void DrawSyncedLyrics(Graphics graphics, MusicSnapshot music)
        {
            string previousText = music == null ? "" : music.PreviousLyric;
            string current = music == null ? T("打开网易云音乐并开始播放", "Open NetEase Cloud Music and start playback") : music.CurrentLyric;
            string nextText = music == null ? "" : music.NextLyric;
            double age = _lyricChangedUtc == DateTime.MinValue ? 1.0 :
                (DateTime.UtcNow - _lyricChangedUtc).TotalSeconds;
            float appear = (float)Clamp(age / 0.34, 0, 1);
            float offsetY = (1f - appear) * 14f;

            RectangleF previousArea = new RectangleF(420, 158 - (1f - appear) * 6f, 476, 24);
            using (Font previous = CreateFittingFont(graphics, previousText, 12, 10,
                FontStyle.Regular, previousArea.Width))
            using (Brush previousBrush = new SolidBrush(
                Color.FromArgb((int)(48 * appear), 242, 242, 240)))
            using (StringFormat previousFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
                graphics.DrawString(previousText, previous, previousBrush, previousArea, previousFormat);

            RectangleF lyricArea = new RectangleF(420, 190 + offsetY, 500, 50);
            using (Font lyric = CreateFittingFont(graphics, current, 23, 17,
                FontStyle.Bold, lyricArea.Width))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags |= StringFormatFlags.NoWrap;
                SizeF measured = graphics.MeasureString(current, lyric, int.MaxValue, format);
                float x = lyricArea.X;
                float y = lyricArea.Y + Math.Max(0, (lyricArea.Height - measured.Height) / 2f);
                int activeAlpha = Math.Max(0, Math.Min(255, (int)(247 * appear)));
                using (Brush active = new SolidBrush(Color.FromArgb(activeAlpha, 248, 248, 246)))
                    graphics.DrawString(current, lyric, active, x, y, format);
                using (Brush marker = new SolidBrush(Color.FromArgb(activeAlpha, _accent)))
                    graphics.FillRectangle(marker, 404, lyricArea.Y + 10, 4, 30);
            }

            RectangleF nextArea = new RectangleF(420, 248 + offsetY * 0.45f, 476, 28);
            using (Font next = CreateFittingFont(graphics, nextText, 14, 11,
                FontStyle.Regular, nextArea.Width))
            using (Brush muted = new SolidBrush(Color.FromArgb((int)(82 * appear), 242, 242, 240)))
            using (StringFormat left = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
                graphics.DrawString(nextText, next, muted, nextArea, left);
        }

        private void DrawMusicVisualizer(Graphics graphics, MusicSnapshot music, RectangleF bounds)
        {
            int bars = 34;
            float gap = 4f;
            float width = (bounds.Width - gap * (bars - 1)) / bars;
            double speed = music != null && music.Playing ? 3.2 : 0.35;
            double phase = DateTime.UtcNow.TimeOfDay.TotalSeconds * speed;
            int seed = TextSeed(music == null ? "NETEASE" : music.Title);
            for (int i = 0; i < bars; i++)
            {
                double wave = Math.Abs(Math.Sin(i * 0.57 + phase + seed * 0.013));
                double pulse = Math.Abs(Math.Sin(i * 0.21 - phase * 0.38));
                float height = 7f + (float)(wave * pulse * (bounds.Height - 11f));
                float x = bounds.X + i * (width + gap);
                float y = bounds.Y + (bounds.Height - height) / 2f;
                Color color = i % 3 == 0 ? _secondary : _accent;
                using (Brush bar = new SolidBrush(Color.FromArgb(30 + (int)(wave * 45), color)))
                    graphics.FillRectangle(bar, x, y, width, height);
            }
        }

        private void DrawMusicTimeline(Graphics graphics, MusicSnapshot music, RectangleF bounds)
        {
            double progress = music == null ? 0 : music.Progress;
            RectangleF line = new RectangleF(bounds.X, bounds.Y, bounds.Width, 3);
            using (Brush background = new SolidBrush(Color.FromArgb(66, 244, 244, 242)))
                graphics.FillRectangle(background, line);
            RectangleF foreground = line;
            foreground.Width = line.Width * (float)(Clamp(progress, 0, 100) / 100.0);
            using (Brush fill = new SolidBrush(Color.FromArgb(222, 248, 248, 246)))
                graphics.FillRectangle(fill, foreground);
            float knobX = line.X + foreground.Width;
            using (Brush glow = new SolidBrush(Color.FromArgb(58, 255, 255, 252)))
            using (Brush knob = new SolidBrush(Color.FromArgb(248, 250, 250, 248)))
            {
                graphics.FillEllipse(glow, knobX - 7, line.Y - 6, 14, 14);
                graphics.FillEllipse(knob, knobX - 3, line.Y - 2, 6, 6);
            }

            string elapsed = FormatMusicTime(music == null ? 0 : music.ElapsedSeconds);
            string duration = FormatMusicTime(music == null ? 0 : music.DurationSeconds);
            using (Font tiny = CreateFont(9, FontStyle.Regular))
            using (Brush time = new SolidBrush(Color.FromArgb(230, 245, 245, 243)))
            {
                graphics.DrawString(elapsed, tiny, time, bounds.X, bounds.Y + 10);
                using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                    graphics.DrawString(duration, tiny, time,
                        new RectangleF(bounds.X, bounds.Y + 10, bounds.Width, 18), right);
            }
        }

        private Font CreateFittingFont(Graphics graphics, string value, float maximum,
            float minimum, FontStyle style, float maximumWidth)
        {
            string text = string.IsNullOrWhiteSpace(value) ? " " : value;
            string key = ((int)style).ToString() + "|" + maximum.ToString("R") + "|" +
                minimum.ToString("R") + "|" + maximumWidth.ToString("R") + "|" + text;
            float cachedSize;
            if (_fittedFontSizes.TryGetValue(key, out cachedSize))
                return CreateFont(cachedSize, style);

            for (float size = maximum; size > minimum; size -= 1f)
            {
                Font candidate = CreateFont(size, style);
                if (graphics.MeasureString(text, candidate).Width <= maximumWidth)
                {
                    RememberFittedFont(key, size);
                    return candidate;
                }
                candidate.Dispose();
            }
            RememberFittedFont(key, minimum);
            return CreateFont(minimum, style);
        }

        private void RememberFittedFont(string key, float size)
        {
            if (_fittedFontSizes.Count >= 128)
                _fittedFontSizes.Clear();
            _fittedFontSizes[key] = size;
        }

        private static RectangleF CropSource(Image image, RectangleF target)
        {
            float sourceRatio = image.Width / (float)image.Height;
            float targetRatio = target.Width / target.Height;
            if (sourceRatio > targetRatio)
            {
                float width = image.Height * targetRatio;
                return new RectangleF((image.Width - width) / 2f, 0, width, image.Height);
            }
            float height = image.Width / targetRatio;
            return new RectangleF(0, (image.Height - height) / 2f, image.Width, height);
        }

        private static int TextSeed(string value)
        {
            int seed = 17;
            if (value == null)
                return seed;
            for (int i = 0; i < value.Length; i++)
                seed = unchecked(seed * 31 + value[i]);
            return seed & 0x7fffffff;
        }

        private static string FormatMusicTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                seconds = 0;
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        private void DrawCoverGlow(Graphics graphics, Image cover)
        {
            using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = 0.16f;
                attributes.SetColorMatrix(matrix);
                graphics.DrawImage(cover, new Rectangle(0, 0, CanvasWidth, CanvasHeight),
                    0, 0, cover.Width, cover.Height, GraphicsUnit.Pixel, attributes);
            }
            using (Brush shade = new SolidBrush(Color.FromArgb(150, _backgroundBottom)))
                graphics.FillRectangle(shade, 0, 0, CanvasWidth, CanvasHeight);
        }

        private void DrawImageCrop(Graphics graphics, Image image, RectangleF target)
        {
            float sourceRatio = image.Width / (float)image.Height;
            float targetRatio = target.Width / target.Height;
            RectangleF source;
            if (sourceRatio > targetRatio)
            {
                float width = image.Height * targetRatio;
                source = new RectangleF((image.Width - width) / 2f, 0, width, image.Height);
            }
            else
            {
                float height = image.Width / targetRatio;
                source = new RectangleF(0, (image.Height - height) / 2f, image.Width, height);
            }
            GraphicsState state = graphics.Save();
            using (GraphicsPath path = RoundedRectangle(target, 18f))
            {
                graphics.SetClip(path);
                graphics.DrawImage(image, target, source, GraphicsUnit.Pixel);
            }
            graphics.Restore(state);
        }

        private void DrawMusicPlaceholder(Graphics graphics, RectangleF target)
        {
            using (Brush brush = new SolidBrush(Color.FromArgb(65, _accent)))
            using (Font note = CreateFont(72, FontStyle.Bold))
            using (Brush noteBrush = new SolidBrush(_accent))
            using (StringFormat center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.FillRectangle(brush, target);
                graphics.DrawString("♪", note, noteBrush, target, center);
            }
        }

        private void DrawMinimalHeader(Graphics graphics)
        {
            string titleText = string.Equals(_config.Title, "NEXUS // SYSTEM",
                StringComparison.OrdinalIgnoreCase) ? "JONSBO CANVAS" : _config.Title;
            using (Font title = CreateFont(14, FontStyle.Bold))
            using (Font mode = CreateFont(9, FontStyle.Bold))
            using (Brush titleBrush = new SolidBrush(_text))
            using (Brush mutedBrush = new SolidBrush(_muted))
            using (Brush accentBrush = new SolidBrush(_accent))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
            {
                graphics.DrawString(titleText, title, titleBrush, 24, 17);
                graphics.FillEllipse(accentBrush, 806, 26, 7, 7);
                graphics.DrawString(T("硬件监控", "HARDWARE"), mode, mutedBrush,
                    new RectangleF(818, 19, 118, 22), right);
                graphics.FillRectangle(accentBrush, 24, 49, 54, 3);
            }
        }

        private void DrawMinimalClock(Graphics graphics, MetricsSnapshot metrics)
        {
            RectangleF panel = new RectangleF(24, 66, 293, 286);
            DrawPanel(graphics, panel, _accent);
            string clockText = metrics.Timestamp.ToString(_config.Use24HourClock
                ? (_config.ShowSeconds ? "HH:mm:ss" : "HH:mm")
                : (_config.ShowSeconds ? "hh:mm:ss" : "hh:mm"));
            string period = _config.Use24HourClock ? "" : metrics.Timestamp.ToString("tt");
            using (Font date = CreateFont(12, FontStyle.Regular))
            using (Font time = CreateFittingFont(graphics, clockText, 36, 28,
                FontStyle.Bold, 250))
            using (Font periodFont = CreateFont(10, FontStyle.Bold))
            using (Font ramLabel = CreateFont(11, FontStyle.Bold))
            using (Font ramValue = CreateFont(29, FontStyle.Bold))
            using (Font ramDetail = CreateFont(9, FontStyle.Regular))
            using (Brush textBrush = new SolidBrush(_text))
            using (Brush accentBrush = new SolidBrush(_accent))
            using (Brush mutedBrush = new SolidBrush(_muted))
            {
                graphics.DrawString(metrics.Timestamp.ToString(English ? "ddd, MMM d yyyy" : "yyyy 年 M 月 d 日  dddd",
                    English ? System.Globalization.CultureInfo.GetCultureInfo("en-US") : System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), date,
                    mutedBrush, panel.X + 28, panel.Y + 24);
                graphics.DrawString(clockText, time, textBrush,
                    new RectangleF(panel.X + 22, panel.Y + 55, 252, 74));
                if (!_config.ShowSeconds && !string.IsNullOrWhiteSpace(period))
                    graphics.DrawString(period, periodFont, accentBrush,
                        panel.X + 222, panel.Y + 85);

                using (Pen divider = new Pen(Color.FromArgb(44, _muted), 1f))
                    graphics.DrawLine(divider, panel.X + 28, panel.Y + 151,
                        panel.Right - 28, panel.Y + 151);
                graphics.DrawString(T("内存", "MEMORY"), ramLabel, mutedBrush, panel.X + 28, panel.Y + 174);
                graphics.DrawString(metrics.MemoryUsage.ToString("0") + "%", ramValue, textBrush,
                    panel.X + 28, panel.Y + 195);
                using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                    graphics.DrawString(metrics.MemoryUsedGb.ToString("0.0") + " / " +
                        metrics.MemoryTotalGb.ToString("0.0") + " GB", ramDetail, mutedBrush,
                        new RectangleF(panel.X + 132, panel.Y + 220, panel.Width - 160, 20), right);
                DrawProgress(graphics, new RectangleF(panel.X + 28, panel.Y + 251, panel.Width - 56, 8),
                    metrics.MemoryUsage, _accent);
            }
        }

        private void DrawMinimalMetricCard(Graphics graphics, RectangleF panel, string label,
            double usage, double? temperature, Color accent)
        {
            DrawPanel(graphics, panel, accent);
            bool showTemperature = temperature.HasValue;
            string primaryText = showTemperature
                ? temperature.Value.ToString("0") + "°"
                : usage.ToString("0") + "%";
            string primaryLabel = showTemperature ? T("温度", "TEMPERATURE") : T("负载", "LOAD");
            using (Font labelFont = CreateFont(18, FontStyle.Bold))
            using (Font temperatureFont = CreateFont(42, FontStyle.Bold))
            using (Font primaryLabelFont = CreateFont(10, FontStyle.Bold))
            using (Font usageLabelFont = CreateFont(10, FontStyle.Regular))
            using (Font usageFont = CreateFont(22, FontStyle.Bold))
            using (Brush accentBrush = new SolidBrush(accent))
            using (Brush textBrush = new SolidBrush(_text))
            using (Brush mutedBrush = new SolidBrush(_muted))
            using (StringFormat centered = new StringFormat
            {
                Alignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoClip
            })
            using (StringFormat left = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            })
            using (StringFormat primaryValue = new StringFormat
            {
                Alignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoClip
            })
            using (StringFormat usageValue = new StringFormat
            {
                Alignment = StringAlignment.Far,
                FormatFlags = StringFormatFlags.NoClip
            })
            {
                graphics.DrawString(label, labelFont, accentBrush,
                    new RectangleF(panel.X, panel.Y + 18, panel.Width, 34), centered);
                graphics.DrawString(primaryLabel, primaryLabelFont, mutedBrush,
                    new RectangleF(panel.X, panel.Y + 66, panel.Width, 20), centered);
                graphics.DrawString(primaryText, temperatureFont, textBrush,
                    new PointF(panel.X + panel.Width / 2f, panel.Y + 88), primaryValue);
                using (Pen divider = new Pen(Color.FromArgb(42, accent), 1f))
                    graphics.DrawLine(divider, panel.X + 28, panel.Y + 174,
                        panel.Right - 28, panel.Y + 174);
                if (showTemperature)
                {
                    graphics.DrawString(T("负载", "LOAD"), usageLabelFont, mutedBrush,
                        new RectangleF(panel.X + 30, panel.Y + 196, 90, 38), left);
                    graphics.DrawString(usage.ToString("0") + "%", usageFont, accentBrush,
                        new PointF(panel.Right - 30, panel.Y + 190), usageValue);
                }
                else
                {
                    graphics.DrawString(T("温度数据不可用", "TEMPERATURE UNAVAILABLE"), usageLabelFont, mutedBrush,
                        new RectangleF(panel.X, panel.Y + 199, panel.Width, 30), centered);
                }
                DrawProgress(graphics, new RectangleF(panel.X + 28, panel.Y + 246, panel.Width - 56, 8),
                    usage, accent);
            }
        }

        private void DrawBackground(Graphics graphics)
        {
            Rectangle full = new Rectangle(0, 0, CanvasWidth, CanvasHeight);
            using (LinearGradientBrush gradient = new LinearGradientBrush(full, _backgroundTop, _backgroundBottom, 90f))
                graphics.FillRectangle(gradient, full);

            if (_backgroundImage != null)
            {
                if (ImageAnimator.CanAnimate(_backgroundImage))
                    ImageAnimator.UpdateFrames(_backgroundImage);
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = (float)_config.BackgroundOpacity;
                    attributes.SetColorMatrix(matrix);
                    graphics.DrawImage(_backgroundImage, full, 0, 0, _backgroundImage.Width, _backgroundImage.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }

            using (Pen grid = new Pen(Color.FromArgb(15, _accent), 1f))
            {
                for (int x = 0; x <= CanvasWidth; x += 32)
                    graphics.DrawLine(grid, x, 0, x, CanvasHeight);
                for (int y = 0; y <= CanvasHeight; y += 32)
                    graphics.DrawLine(grid, 0, y, CanvasWidth, y);
            }

            using (LinearGradientBrush glow = new LinearGradientBrush(
                new Rectangle(0, 0, CanvasWidth, 130), Color.FromArgb(35, _accent), Color.Transparent, 90f))
                graphics.FillRectangle(glow, 0, 0, CanvasWidth, 130);

            using (Pen line = new Pen(Color.FromArgb(120, _accent), 2f))
                graphics.DrawLine(line, 24, 58, 936, 58);
        }

        private void DrawHeader(Graphics graphics, MetricsSnapshot metrics)
        {
            using (Font title = CreateFont(15, FontStyle.Bold))
            using (Font subtitle = CreateFont(8, FontStyle.Regular))
            using (Brush titleBrush = new SolidBrush(_text))
            using (Brush mutedBrush = new SolidBrush(_muted))
            {
                graphics.DrawString(_config.Title, title, titleBrush, 24, 15);
                graphics.DrawString(_config.Subtitle, subtitle, mutedBrush, 25, 39);
            }

            string timeFormat;
            if (_config.Use24HourClock)
                timeFormat = _config.ShowSeconds ? "HH:mm:ss" : "HH:mm";
            else
                timeFormat = _config.ShowSeconds ? "hh:mm:ss tt" : "hh:mm tt";

            using (Font time = CreateFont(20, FontStyle.Bold))
            using (Brush timeBrush = new SolidBrush(_accent))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                graphics.DrawString(metrics.Timestamp.ToString(timeFormat), time, timeBrush,
                    new RectangleF(730, 12, 206, 38), right);
        }

        private void DrawIdentityPanel(Graphics graphics, MetricsSnapshot metrics)
        {
            RectangleF panel = new RectangleF(24, 72, 240, 278);
            DrawPanel(graphics, panel, _accent);

            string day = metrics.Timestamp.ToString("dddd").ToUpperInvariant();
            string date = metrics.Timestamp.ToString("yyyy.MM.dd");
            using (Font dayFont = CreateFont(22, FontStyle.Bold))
            using (Font dateFont = CreateFont(12, FontStyle.Regular))
            using (Font labelFont = CreateFont(8, FontStyle.Bold))
            using (Font detailFont = CreateFont(9, FontStyle.Regular))
            using (Brush textBrush = new SolidBrush(_text))
            using (Brush accentBrush = new SolidBrush(_accent))
            using (Brush mutedBrush = new SolidBrush(_muted))
            {
                graphics.DrawString(day, dayFont, textBrush, 42, 91);
                graphics.DrawString(date, dateFont, accentBrush, 43, 126);

                graphics.DrawString("PROCESSOR", labelFont, mutedBrush, 43, 174);
                graphics.DrawString(TrimName(metrics.CpuName, 30), detailFont, textBrush,
                    new RectangleF(43, 190, 200, 38));
                graphics.DrawString("GRAPHICS", labelFont, mutedBrush, 43, 237);
                graphics.DrawString(TrimName(metrics.GpuName, 30), detailFont, textBrush,
                    new RectangleF(43, 253, 200, 38));

                graphics.DrawString("UPTIME", labelFont, mutedBrush, 43, 305);
                string uptime = string.Format("{0:00}D  {1:00}H  {2:00}M",
                    (int)metrics.Uptime.TotalDays, metrics.Uptime.Hours, metrics.Uptime.Minutes);
                graphics.DrawString(uptime, detailFont, accentBrush, 43, 320);
            }
        }

        private void DrawGaugePanel(Graphics graphics, RectangleF panel, string label, double usage,
            double? temperature, Color accent)
        {
            DrawPanel(graphics, panel, accent);
            RectangleF ring = new RectangleF(panel.X + 32, panel.Y + 33, 124, 124);
            using (Pen basePen = new Pen(Color.FromArgb(45, accent), 10f))
            using (Pen valuePen = new Pen(accent, 10f))
            {
                basePen.StartCap = basePen.EndCap = LineCap.Round;
                valuePen.StartCap = valuePen.EndCap = LineCap.Round;
                graphics.DrawArc(basePen, ring, -90, 360);
                graphics.DrawArc(valuePen, ring, -90, (float)(360 * Clamp(usage, 0, 100) / 100.0));
            }

            using (Font labelFont = CreateFont(10, FontStyle.Bold))
            using (Font valueFont = CreateFont(27, FontStyle.Bold))
            using (Font degreeFont = CreateFont(10, FontStyle.Regular))
            using (Brush accentBrush = new SolidBrush(accent))
            using (Brush textBrush = new SolidBrush(_text))
            using (Brush mutedBrush = new SolidBrush(_muted))
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center })
            {
                graphics.DrawString(label, labelFont, accentBrush,
                    new RectangleF(panel.X, panel.Y + 10, panel.Width, 20), centered);
                graphics.DrawString(Math.Round(usage).ToString("0") + "%", valueFont, textBrush,
                    new RectangleF(panel.X, panel.Y + 76, panel.Width, 45), centered);
                string temperatureText = temperature.HasValue ? temperature.Value.ToString("0") + "°C" : "--°C";
                graphics.DrawString(temperatureText, degreeFont, mutedBrush,
                    new RectangleF(panel.X, panel.Y + 124, panel.Width, 22), centered);
                graphics.DrawString("UTILIZATION", degreeFont, mutedBrush,
                    new RectangleF(panel.X, panel.Bottom - 25, panel.Width, 18), centered);
            }
        }

        private void DrawMemoryPanel(Graphics graphics, MetricsSnapshot metrics)
        {
            RectangleF panel = new RectangleF(700, 72, 236, 278);
            DrawPanel(graphics, panel, _secondary);
            using (Font title = CreateFont(9, FontStyle.Bold))
            using (Font value = CreateFont(18, FontStyle.Bold))
            using (Font small = CreateFont(8, FontStyle.Regular))
            using (Brush textBrush = new SolidBrush(_text))
            using (Brush mutedBrush = new SolidBrush(_muted))
            using (Brush accentBrush = new SolidBrush(_secondary))
            {
                graphics.DrawString("MEMORY", title, accentBrush, 720, 91);
                graphics.DrawString(metrics.MemoryUsage.ToString("0") + "%", value, textBrush, 720, 111);
                graphics.DrawString(string.Format("{0:0.0} / {1:0.0} GB", metrics.MemoryUsedGb, metrics.MemoryTotalGb),
                    small, mutedBrush, 720, 142);
                DrawProgress(graphics, new RectangleF(720, 162, 196, 6), metrics.MemoryUsage, _secondary);

                graphics.DrawString("GPU MEMORY", title, accentBrush, 720, 194);
                graphics.DrawString((metrics.GpuMemoryUsage ?? 0).ToString("0") + "%", value, textBrush, 720, 214);
                string vram = metrics.GpuMemoryUsedGb.HasValue
                    ? string.Format("{0:0.0} / {1:0.0} GB", metrics.GpuMemoryUsedGb, metrics.GpuMemoryTotalGb)
                    : "NO DATA";
                graphics.DrawString(vram, small, mutedBrush, 720, 245);
                DrawProgress(graphics, new RectangleF(720, 265, 196, 6), metrics.GpuMemoryUsage ?? 0, _accent);

                graphics.DrawString("POWER", title, mutedBrush, 720, 301);
                string cpuPower = metrics.CpuPower.HasValue ? "CPU " + metrics.CpuPower.Value.ToString("0") + "W" : "CPU --W";
                string gpuPower = metrics.GpuPower.HasValue ? "GPU " + metrics.GpuPower.Value.ToString("0") + "W" : "GPU --W";
                graphics.DrawString(cpuPower + "   " + gpuPower, small, textBrush, 720, 322);
            }
        }

        private void DrawHistory(Graphics graphics, RectangleF bounds)
        {
            using (Pen border = new Pen(Color.FromArgb(45, _muted), 1f))
                graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            DrawHistoryLine(graphics, bounds, _cpuHistory, _accent);
            DrawHistoryLine(graphics, bounds, _gpuHistory, _secondary);

            using (Font font = CreateFont(7, FontStyle.Bold))
            using (Brush cpu = new SolidBrush(_accent))
            using (Brush gpu = new SolidBrush(_secondary))
            {
                graphics.DrawString("CPU", font, cpu, bounds.X + 8, bounds.Y + 6);
                graphics.DrawString("GPU", font, gpu, bounds.X + 44, bounds.Y + 6);
            }
        }

        private void DrawFooter(Graphics graphics, MetricsSnapshot metrics)
        {
            using (Font font = CreateFont(7, FontStyle.Regular))
            using (Brush muted = new SolidBrush(Color.FromArgb(150, _muted)))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                graphics.DrawString("JONSBO CANVAS  •  DIRECT USB FRAME", font, muted,
                    new RectangleF(700, 356, 236, 14), right);
        }

        private static void DrawPanel(Graphics graphics, RectangleF bounds, Color accent)
        {
            using (GraphicsPath path = RoundedRectangle(bounds, 14))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(168, 7, 14, 21)))
            using (Pen outline = new Pen(Color.FromArgb(82, accent), 1.2f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }
            using (SolidBrush marker = new SolidBrush(accent))
                graphics.FillRectangle(marker, bounds.X + 14, bounds.Y, 38, 3);
        }

        private static void DrawProgress(Graphics graphics, RectangleF bounds, double value, Color color)
        {
            using (SolidBrush background = new SolidBrush(Color.FromArgb(42, color)))
                graphics.FillRectangle(background, bounds);
            RectangleF foreground = bounds;
            foreground.Width = bounds.Width * (float)(Clamp(value, 0, 100) / 100.0);
            using (SolidBrush brush = new SolidBrush(color))
                graphics.FillRectangle(brush, foreground);
        }

        private static void DrawHistoryLine(Graphics graphics, RectangleF bounds, List<float> values, Color color)
        {
            if (values.Count < 2)
                return;
            PointF[] points = new PointF[values.Count];
            float step = bounds.Width / Math.Max(1, values.Count - 1);
            for (int index = 0; index < values.Count; index++)
            {
                points[index] = new PointF(bounds.X + index * step,
                    bounds.Bottom - 5 - (bounds.Height - 18) * Clamp(values[index], 0, 100) / 100f);
            }
            using (Pen pen = new Pen(Color.FromArgb(215, color), 2f))
            {
                pen.LineJoin = LineJoin.Round;
                graphics.DrawLines(pen, points);
            }
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Font CreateFont(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
        }

        private static void AddHistory(List<float> values, float value)
        {
            values.Add(value);
            while (values.Count > 40)
                values.RemoveAt(0);
        }

        private static string TrimName(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNKNOWN";
            value = value.Trim();
            return value.Length <= maximum ? value : value.Substring(0, maximum - 1) + "…";
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private string T(string chinese, string english)
        {
            return English ? english : chinese;
        }

        private string LocalizeUsageStatus(string status)
        {
            if (!English || string.IsNullOrWhiteSpace(status))
                return status;
            switch (status)
            {
                case "尚无本地会话记录": return "No local sessions yet";
                case "未检测到 Codex": return "Codex not detected";
                case "本机额度快照": return "Local usage snapshot";
                case "有会话数据，暂无码率快照": return "Sessions found; no rate snapshot yet";
                case "尚未找到额度事件": return "No usage event found yet";
                case "读取失败": return "Read failed";
                case "点击控制器启用同步": return "Enable sync in the controller";
                case "未检测到 Claude Code": return "Claude Code not detected";
                case "Claude 状态栏同步": return "Claude status-line sync";
                case "等待 Claude 首次响应": return "Waiting for Claude's first response";
                case "同步数据损坏": return "Sync data is invalid";
                default: return status;
            }
        }

        private void LoadBackground()
        {
            if (string.IsNullOrWhiteSpace(_config.BackgroundImage))
                return;
            try
            {
                string path = _config.BackgroundImage;
                if (!Path.IsPathRooted(path))
                    path = EmbeddedRuntime.ResolveDataPath(path);
                if (!File.Exists(path))
                    return;
                byte[] data = File.ReadAllBytes(path);
                _backgroundStream = new MemoryStream(data, false);
                _backgroundImage = Image.FromStream(_backgroundStream);
                if (ImageAnimator.CanAnimate(_backgroundImage))
                {
                    _animationHandler = delegate { };
                    ImageAnimator.Animate(_backgroundImage, _animationHandler);
                }
            }
            catch (Exception exception)
            {
                Log.Write("Background load failed: " + exception.Message);
            }
        }

        private void LoadCompanionAssets()
        {
            LoadImageCopy(EmbeddedRuntime.ResolveDataPath(@"assets\codex-pet-idle.png"),
                out _codexPetStream, out _codexPetStrip);
            LoadImageCopy(EmbeddedRuntime.ResolveDataPath(@"assets\claude-companion.png"),
                out _claudeCompanionStream, out _claudeCompanion);
        }

        private static void LoadImageCopy(string path, out MemoryStream stream, out Image image)
        {
            stream = null;
            image = null;
            try
            {
                if (!File.Exists(path))
                    return;
                stream = new MemoryStream(File.ReadAllBytes(path), false);
                image = Image.FromStream(stream);
            }
            catch (Exception exception)
            {
                Log.Write("Companion asset load failed: " + exception.Message);
                if (image != null) image.Dispose();
                if (stream != null) stream.Dispose();
                image = null;
                stream = null;
            }
        }

        public void Dispose()
        {
            if (_backgroundImage != null)
            {
                if (ImageAnimator.CanAnimate(_backgroundImage))
                    ImageAnimator.StopAnimate(_backgroundImage, _animationHandler);
                _backgroundImage.Dispose();
            }
            _backgroundImage = null;
            _animationHandler = null;
            if (_backgroundStream != null)
                _backgroundStream.Dispose();
            _backgroundStream = null;
            if (_musicBase != null)
                _musicBase.Dispose();
            _musicBase = null;
            if (_codexPetStrip != null)
                _codexPetStrip.Dispose();
            _codexPetStrip = null;
            if (_codexPetStream != null)
                _codexPetStream.Dispose();
            _codexPetStream = null;
            if (_claudeCompanion != null)
                _claudeCompanion.Dispose();
            _claudeCompanion = null;
            if (_claudeCompanionStream != null)
                _claudeCompanionStream.Dispose();
            _claudeCompanionStream = null;
            _fittedFontSizes.Clear();
        }
    }
}
