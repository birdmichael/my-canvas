using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JonsboCanvas
{
    internal sealed class MainForm : Form
    {
        private readonly string _configPath;
        private AppConfig _config;
        private DashboardRenderer _renderer;
        private readonly MetricsCollector _metrics;
        private readonly NeteaseMediaCollector _media;
        private readonly NativeDisplay _display;
        private readonly PictureBox _preview;
        private readonly Label _status;
        private readonly ComboBox _modePicker;
        private readonly Button _connect;
        private readonly Button _disconnect;
        private readonly ComboBox _themePicker;
        private readonly Button _applyTheme;
        private readonly Button _customBackground;
        private readonly Button _savePreview;
        private readonly ComboBox _musicFpsPicker;
        private readonly Label _driverStatus;
        private readonly Button _repairDriver;
        private readonly Panel _settingsPanel;
        private readonly Button[] _modeButtons;
        private readonly Label _contextTitle;
        private readonly Label _contextHint;
        private readonly Label _musicFpsLabel;
        private readonly Button _previewToggle;
        private readonly Button _claudeSync;
        private readonly Timer _timer;
        private readonly NotifyIcon _trayIcon;
        private Bitmap _currentFrame;
        private Bitmap _previewFrame;
        private MetricsSnapshot _lastMetrics;
        private MusicSnapshot _lastMusic;
        private readonly VibeCodingCollector _vibeCoding;
        private VibeCodingSnapshot _lastVibeCoding;
        private DateTime _lastMetricsAt;
        private DateTime _lastMusicAt;
        private DateTime _lastVibeCodingAt;
        private DateTime _lastFrameAt;
        private ulong _lastSentSignature;
        private bool _hasLastSentSignature;
        private bool _localPreviewPaused;
        private bool _busy;
        private readonly bool _autoStarted;
        private bool _allowExit;
        private bool _trayNoticeShown;

        public MainForm(string configPath, AppConfig config, bool autoStarted)
        {
            _configPath = configPath;
            _config = config;
            _autoStarted = autoStarted;
            _renderer = new DashboardRenderer(_config);
            _metrics = new MetricsCollector(_config.EnableCpuidSensors);
            _media = new NeteaseMediaCollector(_config.NeteaseDebugPort);
            _vibeCoding = new VibeCodingCollector();
            _display = new NativeDisplay(DashboardRenderer.CanvasWidth, DashboardRenderer.CanvasHeight);
            _display.StatusChanged += OnDisplayStatusChanged;

            Text = "Jonsbo Canvas — 自定义屏幕控制器";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 800);
            MinimumSize = new Size(1050, 700);
            BackColor = Color.FromArgb(8, 13, 19);
            ForeColor = Color.FromArgb(235, 248, 252);
            Font = new Font("Microsoft YaHei UI", 9f);

            Label brand = new Label();
            brand.Text = "JONSBO  CANVAS";
            brand.Location = new Point(24, 17);
            brand.Size = new Size(245, 28);
            brand.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            brand.ForeColor = Color.FromArgb(231, 247, 250);
            Controls.Add(brand);

            Label subBrand = new Label();
            subBrand.Text = "USB DISPLAY STUDIO";
            subBrand.Location = new Point(27, 47);
            subBrand.Size = new Size(220, 18);
            subBrand.Font = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Bold);
            subBrand.ForeColor = Color.FromArgb(92, 158, 170);
            Controls.Add(subBrand);

            string[] modeNames = { "概览", "硬件", "音乐", "Codex", "Claude" };
            _modeButtons = new Button[modeNames.Length];
            for (int index = 0; index < modeNames.Length; index++)
            {
                int captured = index;
                _modeButtons[index] = MakeNavButton(modeNames[index], 344 + index * 104, 20,
                    delegate { SetDisplayMode(ModeFromIndex(captured)); });
            }

            _connect = MakeButton("连接屏幕", 1024, 20, ConnectDisplay);
            _connect.Size = new Size(92, 36);
            _connect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _disconnect = MakeButton("停止", 1124, 20, DisconnectDisplay);
            _disconnect.Size = new Size(92, 36);
            _disconnect.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _preview = new PictureBox();
            _preview.Location = new Point(24, 88);
            _preview.Size = new Size(1192, 467);
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.BackColor = Color.Black;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_preview);

            _status = new Label();
            _modePicker = new ComboBox();
            _modePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            _modePicker.Location = new Point(-500, -500);
            _modePicker.Size = new Size(112, 28);
            _modePicker.Visible = false;
            _modePicker.Items.AddRange(new object[] { "自动切换", "硬件监控", "网易云音乐", "Codex", "Claude" });
            _modePicker.SelectedIndex = ModeToIndex(_config.DisplayMode);
            _modePicker.SelectedIndexChanged += DisplayModeChanged;
            Controls.Add(_modePicker);

            _settingsPanel = new Panel();
            _settingsPanel.Location = new Point(24, 573);
            _settingsPanel.Size = new Size(1192, 150);
            _settingsPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _settingsPanel.BackColor = Color.FromArgb(12, 20, 29);
            _settingsPanel.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_settingsPanel);

            _contextTitle = new Label();
            _contextTitle.Location = new Point(20, 14);
            _contextTitle.Size = new Size(310, 28);
            _contextTitle.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            _contextTitle.ForeColor = Color.FromArgb(233, 246, 249);
            _settingsPanel.Controls.Add(_contextTitle);

            _contextHint = new Label();
            _contextHint.Location = new Point(20, 44);
            _contextHint.Size = new Size(600, 24);
            _contextHint.ForeColor = Color.FromArgb(132, 173, 184);
            _settingsPanel.Controls.Add(_contextHint);

            _status.Location = new Point(24, 740);
            _status.Size = new Size(780, 28);
            _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Color.FromArgb(120, 215, 225);
            _status.Text = "正在初始化硬件数据…";
            Controls.Add(_status);

            _themePicker = new ComboBox();
            _themePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            _themePicker.Location = new Point(20, 93);
            _themePicker.Size = new Size(132, 28);
            _themePicker.Items.AddRange(new object[] { "赛博青", "熔岩橙", "极光紫", "动态流光" });
            _themePicker.SelectedIndex = _config.ThemePreset == "molten-amber" ? 1 :
                (_config.ThemePreset == "aurora-violet" ? 2 :
                (_config.ThemePreset == "animated-cyan" ? 3 : 0));
            _settingsPanel.Controls.Add(_themePicker);

            _applyTheme = MakePanelButton(_settingsPanel, "应用主题", 162, 91, 86, ApplyTheme);
            _customBackground = MakePanelButton(_settingsPanel, "自定义背景", 258, 91, 104, ChooseBackground);
            _savePreview = MakePanelButton(_settingsPanel, "保存画面", 1058, 91, 104, SavePreview);

            _driverStatus = new Label();
            _driverStatus.Location = new Point(384, 91);
            _driverStatus.Size = new Size(330, 30);
            _driverStatus.TextAlign = ContentAlignment.MiddleLeft;
            _driverStatus.ForeColor = Color.FromArgb(150, 200, 210);
            _driverStatus.Text = "正在检查 USB 驱动…";
            _settingsPanel.Controls.Add(_driverStatus);

            _musicFpsLabel = new Label();
            _musicFpsLabel.Location = new Point(20, 91);
            _musicFpsLabel.Size = new Size(80, 30);
            _musicFpsLabel.TextAlign = ContentAlignment.MiddleLeft;
            _musicFpsLabel.ForeColor = Color.FromArgb(170, 214, 220);
            _musicFpsLabel.Text = "动画帧率";
            _settingsPanel.Controls.Add(_musicFpsLabel);

            _musicFpsPicker = new ComboBox();
            _musicFpsPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            _musicFpsPicker.Location = new Point(102, 93);
            _musicFpsPicker.Size = new Size(132, 28);
            _musicFpsPicker.Items.AddRange(new object[]
            {
                "5 FPS 省电", "8 FPS 推荐", "10 FPS", "15 FPS", "20 FPS 流畅"
            });
            _musicFpsPicker.SelectedIndex = MusicFrameRateToIndex(_config.MusicFrameRate);
            _musicFpsPicker.SelectedIndexChanged += MusicFrameRateChanged;
            _settingsPanel.Controls.Add(_musicFpsPicker);

            _repairDriver = MakePanelButton(_settingsPanel, "安装/修复 USB 驱动", 724, 91, 180, RepairUsbDriver);
            _previewToggle = MakePanelButton(_settingsPanel, "暂停本地预览", 914, 91, 134, ToggleLocalPreview);
            _claudeSync = MakePanelButton(_settingsPanel, "启用 Claude 余量同步", 20, 91, 190, EnableClaudeSync);

            RefreshModeChrome();

            _timer = new Timer();
            _timer.Interval = 33;
            _timer.Tick += TimerTick;

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("打开控制器", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("自动切换", null, delegate { SetDisplayMode("auto"); });
            trayMenu.Items.Add("硬件监控", null, delegate { SetDisplayMode("hardware"); });
            trayMenu.Items.Add("网易云音乐", null, delegate { SetDisplayMode("music"); });
            trayMenu.Items.Add("Codex 余量", null, delegate { SetDisplayMode("codex"); });
            trayMenu.Items.Add("Claude 余量", null, delegate { SetDisplayMode("claude"); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("检查/修复 USB 驱动", null, RepairUsbDriver);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, ExitFromTray);
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = Icon;
            _trayIcon.Text = "Jonsbo Canvas";
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            Shown += FormShown;
            Resize += MainFormResize;
            FormClosing += MainFormClosing;
        }

        private Button MakeButton(string text, int x, int y, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(text.Length > 4 ? 98 : 68, 30);
            button.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(58, 155, 170);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 48, 58);
            button.BackColor = Color.FromArgb(10, 25, 33);
            button.ForeColor = Color.FromArgb(220, 248, 252);
            button.Click += click;
            Controls.Add(button);
            return button;
        }

        private Button MakeNavButton(string text, int x, int y, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(96, 38);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.Transparent;
            button.ForeColor = Color.FromArgb(153, 183, 191);
            button.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.Click += click;
            Controls.Add(button);
            return button;
        }

        private static Button MakePanelButton(Panel panel, string text, int x, int y, int width, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 32);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(45, 107, 120);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(23, 49, 59);
            button.BackColor = Color.FromArgb(13, 29, 38);
            button.ForeColor = Color.FromArgb(218, 242, 247);
            button.Cursor = Cursors.Hand;
            button.Click += click;
            panel.Controls.Add(button);
            return button;
        }

        private static string ModeFromIndex(int index)
        {
            return index == 1 ? "hardware" : (index == 2 ? "music" :
                (index == 3 ? "codex" : (index == 4 ? "claude" : "auto")));
        }

        private static int ModeToIndex(string mode)
        {
            return mode == "hardware" ? 1 : (mode == "music" ? 2 :
                (mode == "codex" ? 3 : (mode == "claude" ? 4 : 0)));
        }

        private void RefreshModeChrome()
        {
            int selected = ModeToIndex(_config.DisplayMode);
            for (int index = 0; index < _modeButtons.Length; index++)
            {
                bool active = index == selected;
                _modeButtons[index].BackColor = active ? Color.FromArgb(19, 48, 59) : Color.Transparent;
                _modeButtons[index].ForeColor = active
                    ? Color.FromArgb(89, 232, 255) : Color.FromArgb(153, 183, 191);
                _modeButtons[index].FlatAppearance.BorderSize = active ? 1 : 0;
                _modeButtons[index].FlatAppearance.BorderColor = Color.FromArgb(46, 139, 155);
            }

            bool music = _config.DisplayMode == "music";
            bool claude = _config.DisplayMode == "claude";
            bool coding = claude || _config.DisplayMode == "codex";
            _themePicker.Visible = !music && !coding;
            _applyTheme.Visible = !music && !coding;
            _customBackground.Visible = !music && !coding;
            _musicFpsLabel.Visible = music;
            _musicFpsPicker.Visible = music;
            _claudeSync.Visible = claude;

            _contextTitle.Text = _config.DisplayMode == "hardware" ? "硬件监控设置" :
                (_config.DisplayMode == "music" ? "网易云音乐动效" :
                (_config.DisplayMode == "codex" ? "Codex 伙伴与余量" :
                (_config.DisplayMode == "claude" ? "Claude 伙伴与余量" : "概览与主题")));
            _contextHint.Text = _config.DisplayMode == "music" ? "唱片、歌词和封面跟随播放状态；可在下方控制动画帧率。" :
                (_config.DisplayMode == "codex" ? "每 10 秒读取本机 Codex 会话快照；屏幕同时显示官方伙伴动画和余量。" :
                (_config.DisplayMode == "claude" ? "启用一次状态栏桥接后，每 10 秒读取 Claude 的 5 小时与 7 天窗口。" :
                "主题只影响硬件监控；USB 推送与本地预览分别运行，最小化会自动暂停本地预览。"));
        }

        private void FormShown(object sender, EventArgs args)
        {
            RenderFrame();
            _timer.Start();
            UsbDriverStatus driver = RefreshUsbDriverStatus();
            if (_config.AutoConnect)
            {
                if (!driver.Ready)
                {
                    SetStatus("USB 驱动尚未就绪，请点击“安装/修复 USB 驱动”");
                }
                else if (OriginalAppRunning())
                {
                    if (_autoStarted && _config.AutoTakeOverOriginalApp && TryStopOriginalApp())
                        ConnectDisplay(this, EventArgs.Empty);
                    else
                        SetStatus("预览已运行；请先从系统托盘退出 JONSBO-AIO，再点击“连接屏幕”");
                }
                else
                    ConnectDisplay(this, EventArgs.Empty);
            }
            if (_config.DisplayMode == "music" && !NeteaseLauncher.IsRunning())
                SetStatus(NeteaseLauncher.StartRealtime(_config.NeteaseDebugPort));
            if (_autoStarted && _config.StartHiddenOnAutoStart)
                BeginInvoke(new Action(HideToTray));
        }

        private void TimerTick(object sender, EventArgs args)
        {
            DateTime now = DateTime.UtcNow;
            bool codingVisible = _config.DisplayMode == "codex" || _config.DisplayMode == "claude";
            bool musicVisible = _config.DisplayMode == "music" ||
                (_config.DisplayMode == "auto" && _lastMusic != null && _lastMusic.Available);
            bool localPreviewActive = Visible && WindowState != FormWindowState.Minimized && !_localPreviewPaused;
            int frameInterval = codingVisible ? 250 : (musicVisible
                ? (_lastMusic == null || !_lastMusic.Available ? 500 :
                    (!_lastMusic.Playing ? 750 : Math.Max(50, 1000 / _config.MusicFrameRate)))
                :
                (IsAnimatedBackground() ? _config.AnimationFrameMilliseconds : _config.RefreshMilliseconds));
            if (!_display.Connected && !localPreviewActive)
                frameInterval = 2000;
            if (_timer.Interval != frameInterval)
                _timer.Interval = frameInterval;
            if ((now - _lastFrameAt).TotalMilliseconds < frameInterval)
                return;
            RenderFrame();
        }

        private static int MusicFrameRateToIndex(int frameRate)
        {
            if (frameRate <= 5) return 0;
            if (frameRate <= 8) return 1;
            if (frameRate <= 10) return 2;
            if (frameRate <= 15) return 3;
            return 4;
        }

        private static int MusicFrameRateFromIndex(int index)
        {
            return index == 0 ? 5 : (index == 1 ? 8 :
                (index == 2 ? 10 : (index == 3 ? 15 : 20)));
        }

        private void MusicFrameRateChanged(object sender, EventArgs args)
        {
            int frameRate = MusicFrameRateFromIndex(_musicFpsPicker.SelectedIndex);
            if (_config.MusicFrameRate == frameRate)
                return;
            _config.MusicFrameRate = frameRate;
            AppConfig.Save(_configPath, _config);
            _lastFrameAt = DateTime.MinValue;
            SetStatus("音乐动画已调整为 " + frameRate + " FPS");
        }

        private void RenderFrame()
        {
            if (_busy)
                return;
            _busy = true;
            try
            {
                DateTime now = DateTime.UtcNow;
                bool codingMode = _config.DisplayMode == "codex" || _config.DisplayMode == "claude";
                bool localPreviewActive = Visible && WindowState != FormWindowState.Minimized && !_localPreviewPaused;
                if (!_display.Connected && !localPreviewActive)
                {
                    if (codingMode && (_lastVibeCoding == null ||
                        (now - _lastVibeCodingAt).TotalSeconds >= 10))
                    {
                        _lastVibeCoding = _vibeCoding.Collect();
                        _lastVibeCodingAt = now;
                    }
                    _lastFrameAt = now;
                    return;
                }

                if (!codingMode && _config.DisplayMode != "hardware" &&
                    (_lastMusic == null || (now - _lastMusicAt).TotalMilliseconds >=
                    (_media.RealtimeConnected ? 100 : 1000)))
                {
                    _lastMusic = _media.Collect();
                    _lastMusicAt = now;
                }

                bool musicMode = !codingMode && (_config.DisplayMode == "music" ||
                    (_config.DisplayMode == "auto" && _lastMusic != null && _lastMusic.Available));
                if (codingMode && (_lastVibeCoding == null ||
                    (now - _lastVibeCodingAt).TotalSeconds >= 10))
                {
                    _lastVibeCoding = _vibeCoding.Collect();
                    _lastVibeCodingAt = now;
                }
                if (!codingMode && !musicMode &&
                    (_lastMetrics == null || (now - _lastMetricsAt).TotalMilliseconds >= _config.RefreshMilliseconds))
                {
                    _lastMetrics = _metrics.Collect();
                    _lastMetricsAt = now;
                }
                Bitmap next = codingMode
                    ? _renderer.RenderCoding(_config.DisplayMode == "codex"
                        ? (_lastVibeCoding == null ? null : _lastVibeCoding.Codex)
                        : (_lastVibeCoding == null ? null : _lastVibeCoding.Claude),
                        _config.DisplayMode == "codex")
                    : (musicMode
                        ? _renderer.RenderMusic(_lastMusic ?? new MusicSnapshot())
                        : _renderer.Render(_lastMetrics));
                Bitmap previous = _currentFrame;
                _currentFrame = next;
                if (previous != null)
                    previous.Dispose();

                if (localPreviewActive)
                {
                    Bitmap preview = new Bitmap(next);
                    Bitmap oldPreview = _previewFrame;
                    _previewFrame = preview;
                    _preview.Image = preview;
                    if (oldPreview != null)
                        oldPreview.Dispose();
                }

                if (_display.Connected)
                {
                    ulong signature = ComputeFrameSignature(next);
                    if (!_hasLastSentSignature || signature != _lastSentSignature)
                    {
                        int result = _display.Send(next, _config.RotationDegrees);
                        if (result != 0)
                            SetStatus("画面发送失败，错误码 " + result);
                        else
                        {
                            _lastSentSignature = signature;
                            _hasLastSentSignature = true;
                        }
                    }
                }
                _lastFrameAt = now;
            }
            catch (Exception exception)
            {
                Log.Write("Render failed: " + exception);
                SetStatus("渲染失败：" + exception.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private static ulong ComputeFrameSignature(Bitmap bitmap)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                const int columns = 24;
                const int rows = 10;
                for (int row = 0; row < rows; row++)
                {
                    int y = Math.Min(bitmap.Height - 1, row * (bitmap.Height - 1) / (rows - 1));
                    for (int column = 0; column < columns; column++)
                    {
                        int x = Math.Min(bitmap.Width - 1, column * (bitmap.Width - 1) / (columns - 1));
                        hash ^= (uint)bitmap.GetPixel(x, y).ToArgb();
                        hash *= 1099511628211UL;
                    }
                }
                return hash;
            }
        }

        private bool IsAnimatedBackground()
        {
            return string.Equals(Path.GetExtension(_config.BackgroundImage), ".gif",
                StringComparison.OrdinalIgnoreCase);
        }

        private void ConnectDisplay(object sender, EventArgs args)
        {
            if (_display.Connected)
            {
                SetStatus("屏幕已经连接");
                return;
            }

            UsbDriverStatus driver = RefreshUsbDriverStatus();
            if (!driver.Ready)
            {
                RepairUsbDriver(this, EventArgs.Empty);
                if (!RefreshUsbDriverStatus().Ready)
                {
                    SetStatus("USB 驱动尚未就绪");
                    return;
                }
            }
            if (OriginalAppRunning())
            {
                DialogResult choice = MessageBox.Show(this,
                    "JONSBO-AIO 仍在后台占用 USB 屏幕。\n\n是否现在关闭原厂程序并由 Jonsbo Canvas 接管？",
                    "切换屏幕控制程序", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice != DialogResult.Yes || !TryStopOriginalApp())
                {
                    SetStatus("等待退出 JONSBO-AIO");
                    return;
                }
            }

            try
            {
                _hasLastSentSignature = false;
                int result = _display.Start();
                if (result == 0)
                    SetStatus("显示 SDK 已启动，正在连接…");
            }
            catch (DllNotFoundException exception)
            {
                SetStatus("缺少原厂显示 DLL：" + exception.Message);
            }
            catch (Exception exception)
            {
                Log.Write("Connect failed: " + exception);
                SetStatus("连接失败：" + exception.Message);
            }
        }

        private void DisconnectDisplay(object sender, EventArgs args)
        {
            _display.Stop();
            _hasLastSentSignature = false;
        }

        private UsbDriverStatus RefreshUsbDriverStatus()
        {
            UsbDriverStatus driver = UsbDriverManager.Check();
            _driverStatus.Text = driver.Message;
            _driverStatus.ForeColor = driver.Ready
                ? Color.FromArgb(105, 225, 170)
                : Color.FromArgb(255, 185, 95);
            _repairDriver.Text = driver.Ready ? "检查/修复 USB 驱动" : "安装/修复 USB 驱动";
            return driver;
        }

        private void RepairUsbDriver(object sender, EventArgs args)
        {
            UsbDriverStatus before = UsbDriverManager.Check();
            if (!before.RepairPackagePresent)
            {
                MessageBox.Show(this, before.Message, "USB 驱动",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string question = before.Ready
                ? "当前 USB 驱动工作正常。是否仍要重新导入并修复签名驱动？"
                : "即将通过 Windows 官方 PnP 工具安装 MS USB Display 驱动。是否继续？";
            if (MessageBox.Show(this, question, "安装/修复 USB 驱动",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _repairDriver.Enabled = false;
            _driverStatus.Text = "正在由 Windows 安装并核验签名驱动…";
            try
            {
                UsbDriverInstallResult result = UsbDriverManager.InstallOrRepair();
                UsbDriverStatus after = RefreshUsbDriverStatus();
                MessageBox.Show(this, result.Message, "USB 驱动",
                    MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                if (after.Ready)
                    SetStatus("USB 驱动已就绪，可以连接屏幕");
            }
            finally
            {
                _repairDriver.Enabled = true;
            }
        }

        private void ReloadTheme(object sender, EventArgs args)
        {
            try
            {
                _config = AppConfig.Load(_configPath);
                DashboardRenderer old = _renderer;
                _renderer = new DashboardRenderer(_config);
                old.Dispose();
                _timer.Interval = 33;
                RenderFrame();
                SetStatus("主题配置已重载");
            }
            catch (Exception exception)
            {
                SetStatus("配置重载失败：" + exception.Message);
            }
        }

        private void ApplyTheme(object sender, EventArgs args)
        {
            string preset = _themePicker.SelectedIndex == 1 ? "molten-amber" :
                (_themePicker.SelectedIndex == 2 ? "aurora-violet" :
                (_themePicker.SelectedIndex == 3 ? "animated-cyan" : "cyber-cyan"));
            _config.ApplyPreset(preset);
            AppConfig.Save(_configPath, _config);
            RebuildRenderer("主题已应用");
        }

        private void ChooseBackground(object sender, EventArgs args)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择背景图片";
                dialog.Filter = "图片或动图|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                _config.ThemePreset = "custom";
                _config.BackgroundImage = dialog.FileName;
                AppConfig.Save(_configPath, _config);
                RebuildRenderer("自定义背景已应用");
            }
        }

        private void RebuildRenderer(string status)
        {
            DashboardRenderer old = _renderer;
            _renderer = new DashboardRenderer(_config);
            old.Dispose();
            RenderFrame();
            SetStatus(status);
        }

        private void DisplayModeChanged(object sender, EventArgs args)
        {
            string mode = ModeFromIndex(_modePicker.SelectedIndex);
            if (_config.DisplayMode != mode)
                SetDisplayMode(mode);
        }

        private void SetDisplayMode(string mode)
        {
            _config.DisplayMode = mode;
            int index = ModeToIndex(mode);
            if (_modePicker.SelectedIndex != index)
                _modePicker.SelectedIndex = index;
            _lastMusicAt = DateTime.MinValue;
            _lastVibeCodingAt = DateTime.MinValue;
            _hasLastSentSignature = false;
            AppConfig.Save(_configPath, _config);
            RefreshModeChrome();
            RenderFrame();
            string status = mode == "hardware" ? "硬件监控模式" :
                (mode == "music" ? "网易云音乐模式" :
                (mode == "codex" ? "Codex 伙伴与余量模式" :
                (mode == "claude" ? "Claude 伙伴与余量模式" : "自动切换模式")));
            if (mode == "music")
                status = NeteaseLauncher.StartRealtime(_config.NeteaseDebugPort);
            SetStatus(status);
        }

        private void ToggleLocalPreview(object sender, EventArgs args)
        {
            _localPreviewPaused = !_localPreviewPaused;
            _previewToggle.Text = _localPreviewPaused ? "恢复本地预览" : "暂停本地预览";
            _previewToggle.ForeColor = _localPreviewPaused
                ? Color.FromArgb(255, 190, 92) : Color.FromArgb(218, 242, 247);
            if (!_localPreviewPaused)
            {
                _lastFrameAt = DateTime.MinValue;
                RenderFrame();
            }
            SetStatus(_localPreviewPaused
                ? "本地预览已暂停；USB 屏幕仍会继续更新"
                : "本地预览已恢复");
        }

        private void EnableClaudeSync(object sender, EventArgs args)
        {
            bool isOurBridge;
            bool hasExisting = ClaudeStatusBridge.HasExistingStatusLine(out isOurBridge);
            if (hasExisting && !isOurBridge)
            {
                DialogResult replace = MessageBox.Show(this,
                    "Claude 已有其他状态栏配置。继续会先备份 settings.json，再替换为 Jonsbo 余量桥接。\n\n是否继续？",
                    "启用 Claude 余量同步", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (replace != DialogResult.Yes)
                    return;
            }
            try
            {
                string result = ClaudeStatusBridge.Install(hasExisting && !isOurBridge);
                _lastVibeCodingAt = DateTime.MinValue;
                SetStatus(result);
                MessageBox.Show(this, result, "Claude 余量同步",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                Log.Write("Claude bridge install failed: " + exception);
                SetStatus("Claude 同步配置失败：" + exception.Message);
            }
        }

        private void MainFormResize(object sender, EventArgs args)
        {
            if (WindowState == FormWindowState.Minimized && _config.MinimizeToTray)
                HideToTray();
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
            if (!_trayNoticeShown)
            {
                _trayNoticeShown = true;
                _trayIcon.ShowBalloonTip(1800, "Jonsbo Canvas",
                    "程序仍在后台控制屏幕，双击托盘图标可恢复。", ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ExitFromTray(object sender, EventArgs args)
        {
            _allowExit = true;
            Close();
        }

        private void EditConfig(object sender, EventArgs args)
        {
            try
            {
                Process.Start("notepad.exe", "\"" + _configPath + "\"");
                SetStatus("保存 config.json 后点击“重载主题”");
            }
            catch (Exception exception)
            {
                SetStatus("无法打开配置文件：" + exception.Message);
            }
        }

        private void SavePreview(object sender, EventArgs args)
        {
            if (_currentFrame == null)
                return;
            try
            {
                string path = EmbeddedRuntime.PreviewPath;
                _currentFrame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                SetStatus("预览已保存到 " + path);
            }
            catch (Exception exception)
            {
                SetStatus("保存预览失败：" + exception.Message);
            }
        }

        private void OnDisplayStatusChanged(string value)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
                BeginInvoke(new Action<string>(SetStatus), value);
            else
                SetStatus(value);
        }

        private void SetStatus(string value)
        {
            _status.Text = value;
            Log.Write("STATUS: " + value);
        }

        private static bool OriginalAppRunning()
        {
            try { return Process.GetProcessesByName("JONSBO-AIO").Length > 0; }
            catch { return false; }
        }

        private static bool TryStopOriginalApp()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("JONSBO-AIO");
                foreach (Process process in processes)
                {
                    using (process)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                System.Threading.Thread.Sleep(350);
                return !OriginalAppRunning();
            }
            catch (Exception exception)
            {
                Log.Write("Unable to stop original app: " + exception);
                return false;
            }
        }

        private void MainFormClosing(object sender, FormClosingEventArgs args)
        {
            if (!_allowExit && _config.MinimizeToTray && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                HideToTray();
                return;
            }
            _timer.Stop();
            _display.Dispose();
            _metrics.Dispose();
            _media.Dispose();
            _renderer.Dispose();
            if (_currentFrame != null)
                _currentFrame.Dispose();
            _preview.Image = null;
            if (_previewFrame != null)
                _previewFrame.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }
}
