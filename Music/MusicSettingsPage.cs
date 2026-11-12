using System.Diagnostics;
using System.Text;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>
/// 离线音乐播放器设置页。网络能力（联网总开关、SSDP/DLNA）默认关闭，
/// 必须由用户显式打开；迷你播放器与桌面歌词插件默认开启。
/// </summary>
public sealed class MusicSettingsPage : UserControl
{
    private readonly MusicSettings _settings;
    private readonly MusicLibrary _library;
    private readonly MusicPlayer _player;
    private readonly AppConfig _config;
    private readonly RssTheme _theme;
    private readonly TableLayoutPanel _grid;
    private readonly Panel _scroll;

    private TextBox _pathBox = null!;
    private Label _perfectLabel = null!;
    private Label _dlnaStatus = null!;
    private CheckBox _includeSubfolders = null!;
    private CheckBox _perfectLiked = null!;
    private CheckBox _network = null!;
    private CheckBox _ssdp = null!;
    private NumericUpDown _ssdpPort = null!;
    private CheckBox _plugins = null!;
    private CheckBox _miniPlugin = null!;
    private CheckBox _desktopPlugin = null!;
    private CheckBox _showNowPlaying = null!;
    private CheckBox _scanOnOpen = null!;
    private CheckBox _fade = null!;
    private CheckBox _replayGain = null!;
    private CheckBox _exclusive = null!;
    private ComboBox _themeBox = null!;
    private NumericUpDown _lyricsFont = null!;
    private MusicSlider _speed = null!;
    private MusicSlider _balance = null!;
    private MusicSlider _width = null!;
    private Label _speedValue = null!;
    private Label _balanceValue = null!;
    private Label _widthValue = null!;
    private CheckBox _eqEnabled = null!;
    private readonly MusicSlider[] _eqSliders = new MusicSlider[6];
    private readonly Label[] _eqLabels = new Label[6];
    private MusicSlider _preamp = null!;
    private Label _preampValue = null!;
    private NumericUpDown _sleepMinutes = null!;
    private Button _sleepButton = null!;
    private Label _sleepStatus = null!;
    private bool _loading = true;
    private int _row;

    public MusicSettingsPage(MusicSettings settings, MusicLibrary library, MusicPlayer player,
        AppConfig config)
    {
        _settings = settings;
        _library = library;
        _player = player;
        _config = config;
        _theme = RssTheme.Create(settings.Theme);
        Dock = DockStyle.Fill;
        BackColor = _theme.Surface;

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = _theme.Surface,
            Padding = new Padding(20, 10, 20, 20)
        };
        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = _theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _scroll.Controls.Add(_grid);
        _scroll.Resize += (_, _) => ResizeGrid();
        Controls.Add(_scroll);

        BuildLibrarySection();
        BuildPlaybackSection();
        BuildNetworkSection();
        BuildPluginSection();
        BuildInterfaceSection();
        BuildDataSection();

        _loading = false;
        ResizeGrid();
        UpdateDependentStates();
    }

    public event EventHandler? SettingsChanged;
    public event EventHandler? ThemeChanged;
    public event EventHandler? NetworkChanged;
    public event EventHandler? RescanRequested;
    public event EventHandler? OpenLibraryRequested;

    public void SetDlnaStatus(string status)
    {
        if (_dlnaStatus != null)
        {
            _dlnaStatus.Text = "状态：" + status;
        }
    }

    private void ResizeGrid()
    {
        int width = _scroll.ClientSize.Width - _scroll.Padding.Horizontal - 18;
        if (width > 400)
        {
            _grid.Width = width;
        }
    }

    // ================= 音乐库 =================

    private void BuildLibrarySection()
    {
        Section("音乐库", "默认使用 Windows 用户音乐目录。PerfectMusic 文件夹内的音乐会自动加入“喜欢音乐”。");
        _pathBox = new TextBox
        {
            Text = _settings.EffectiveLibraryPath,
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.8f),
            BorderStyle = BorderStyle.FixedSingle
        };
        _pathBox.Leave += (_, _) => SaveLibraryPath(_pathBox.Text);
        var browse = MakeButton("浏览…", primary: false);
        browse.Width = 80;
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择音乐库目录",
                SelectedPath = Directory.Exists(_settings.EffectiveLibraryPath)
                    ? _settings.EffectiveLibraryPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.SelectedPath;
                SaveLibraryPath(dialog.SelectedPath);
            }
        };
        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        pathRow.Controls.Add(_pathBox, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        AddRow("音乐目录", pathRow);

        _perfectLabel = Hint("PerfectMusic 路径：" + _settings.PerfectMusicPath
            + (_settings.PerfectMusicExists ? "（已找到）" : "（未找到，可手动创建）"));
        _includeSubfolders = Check("递归扫描子文件夹", _settings.IncludeSubfolders);
        _includeSubfolders.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.IncludeSubfolders = _includeSubfolders.Checked;
            Save();
        };
        _perfectLiked = Check("PerfectMusic 自动加入“喜欢音乐”", _settings.PerfectMusicAsLiked);
        _perfectLiked.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.PerfectMusicAsLiked = _perfectLiked.Checked;
            Save();
        };
        AddRow("", _includeSubfolders);
        AddRow("", _perfectLiked);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };
        var scan = MakeButton("立即扫描音乐目录", primary: true);
        scan.Click += (_, _) => RescanRequested?.Invoke(this, EventArgs.Empty);
        var open = MakeButton("打开音乐目录", primary: false);
        open.Click += (_, _) => OpenLibraryRequested?.Invoke(this, EventArgs.Empty);
        buttons.Controls.Add(scan);
        buttons.Controls.Add(open);
        AddRow("", buttons);
        Separator();
    }

    private void SaveLibraryPath(string path)
    {
        if (_loading) return;
        _settings.LibraryPath = path.Trim();
        _perfectLabel.Text = "PerfectMusic 路径：" + _settings.PerfectMusicPath
            + (_settings.PerfectMusicExists ? "（已找到）" : "（未找到，可手动创建）");
        Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ================= 播放 =================

    private void BuildPlaybackSection()
    {
        Section("播放", "本地解码与输出全部由 Windows / NAudio 完成，不访问网络。");
        _fade = Check("切歌淡入淡出", _settings.FadeInOut);
        _fade.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.FadeInOut = _fade.Checked;
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        _replayGain = Check("读取 ReplayGain 标签自动调整音量", _settings.ReplayGainEnabled);
        _replayGain.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.ReplayGainEnabled = _replayGain.Checked;
            _player.SetReplayGainEnabled(_replayGain.Checked);
            Save();
        };
        _exclusive = Check("WASAPI 独占输出（下一首生效）", _settings.ExclusiveOutput);
        _exclusive.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.ExclusiveOutput = _exclusive.Checked;
            Save();
        };
        AddRow("", _fade);
        AddRow("", _replayGain);
        AddRow("", _exclusive);

        var speedRow = SliderRow(out _speed, out _speedValue, 0.5, 2.0, _settings.PlaybackSpeed,
            "0.00");
        speedRow.Text = "";
        _speed.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.PlaybackSpeed = _speed.Value;
            _player.SetSpeed(_speed.Value);
            Save();
        };
        AddRow("播放速度", speedRow);
        Hint("变速会同时改变音高（类似唱片机转速）。");

        var balanceRow = SliderRow(out _balance, out _balanceValue, -1, 1, _settings.Balance, "0.00");
        _balance.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.Balance = (float)_balance.Value;
            _player.SetStereo(_settings.Balance, _settings.StereoWidth);
            Save();
        };
        AddRow("左右平衡", balanceRow);

        var widthRow = SliderRow(out _width, out _widthValue, 0, 2, _settings.StereoWidth, "0.00");
        _width.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.StereoWidth = (float)_width.Value;
            _player.SetStereo(_settings.Balance, _settings.StereoWidth);
            Save();
        };
        AddRow("立体声宽度", widthRow);

        _eqEnabled = Check("启用 6 段均衡器", _settings.EqEnabled);
        _eqEnabled.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.EqEnabled = _eqEnabled.Checked;
            ApplyEq();
            Save();
        };
        AddRow("均衡器", _eqEnabled);
        var eqGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 4)
        };
        eqGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        eqGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        eqGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        string[] names = { "60 Hz", "230 Hz", "910 Hz", "3.6 kHz", "14 kHz" };
        for (int i = 0; i < 6; i++)
        {
            string label = i < names.Length ? names[i] : "前置增益";
            double value = i < names.Length ? _settings.EqGains[i] : _settings.PreampDb;
            double min = i < names.Length ? -12 : -12;
            double max = 12;
            var slider = new MusicSlider
            {
                Dock = DockStyle.Fill,
                Theme = _theme,
                Minimum = min,
                Maximum = max,
                Value = value,
                ShowValue = false,
                Height = 22
            };
            var valueLabel = new Label
            {
                Text = ((int)Math.Round(value)) + " dB",
                Dock = DockStyle.Fill,
                Font = _theme.Font(8f),
                ForeColor = _theme.TextTertiary,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            int index = i;
            slider.ValueChanged += (_, _) =>
            {
                valueLabel.Text = ((int)Math.Round(slider.Value)) + " dB";
                if (_loading) return;
                if (index < names.Length)
                {
                    _settings.EqGains[index] = (float)slider.Value;
                }
                else
                {
                    _settings.PreampDb = (float)slider.Value;
                }
                ApplyEq();
                Save();
            };
            eqGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            eqGrid.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                Font = _theme.Font(8.2f),
                ForeColor = _theme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            }, 0, i);
            eqGrid.Controls.Add(slider, 1, i);
            eqGrid.Controls.Add(valueLabel, 2, i);
            if (i < names.Length)
            {
                _eqSliders[i] = slider;
                _eqLabels[i] = valueLabel;
            }
            else
            {
                _preamp = slider;
                _preampValue = valueLabel;
            }
        }
        AddFull(eqGrid);

        var sleepRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };
        _sleepMinutes = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 1440,
            Value = _settings.SleepMinutes,
            Width = 90,
            Font = _theme.Font(8.8f)
        };
        _sleepButton = MakeButton("设置睡眠定时", primary: false);
        _sleepButton.Click += (_, _) =>
        {
            _settings.SleepMinutes = (int)_sleepMinutes.Value;
            _player.SetSleepTimer(_settings.SleepMinutes);
            Save();
            _sleepStatus.Text = _settings.SleepMinutes <= 0
                ? "睡眠定时：关闭"
                : $"睡眠定时：{_settings.SleepMinutes} 分钟后停止";
        };
        _sleepStatus = new Label
        {
            Text = _settings.SleepMinutes <= 0 ? "睡眠定时：关闭" : $"睡眠定时：{_settings.SleepMinutes} 分钟",
            AutoSize = true,
            Font = _theme.Font(8.2f),
            ForeColor = _theme.TextTertiary,
            Margin = new Padding(12, 8, 0, 0),
            BackColor = Color.Transparent
        };
        sleepRow.Controls.Add(_sleepMinutes);
        sleepRow.Controls.Add(_sleepButton);
        sleepRow.Controls.Add(_sleepStatus);
        AddRow("睡眠定时", sleepRow);
        Separator();
    }

    private TableLayoutPanel SliderRow(out MusicSlider slider, out Label valueLabel,
        double min, double max, double value, string format)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        slider = new MusicSlider
        {
            Dock = DockStyle.Fill,
            Theme = _theme,
            Minimum = min,
            Maximum = max,
            Value = value,
            ShowValue = false,
            Height = 22
        };
        valueLabel = new Label
        {
            Text = value.ToString(format),
            Dock = DockStyle.Fill,
            Font = _theme.Font(8f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };
        var captured = slider;
        var capturedLabel = valueLabel;
        captured.ValueChanged += (_, _) =>
        {
            capturedLabel.Text = captured.Value.ToString(format);
        };
        row.Controls.Add(slider, 0, 0);
        row.Controls.Add(valueLabel, 1, 0);
        return row;
    }

    private void ApplyEq()
    {
        _player.SetEq(_settings.EqGains, _settings.EqEnabled, _settings.PreampDb);
    }

    // ================= 网络与共享 =================

    private void BuildNetworkSection()
    {
        Section("网络与共享（默认全部关闭）",
            "不开启联网总开关时，播放器完全离线：不会监听 SSDP/DLNA，也不会发送遥测。");
        _network = Check("联网总开关（允许 SSDP/DLNA）", _settings.NetworkEnabled);
        _network.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.NetworkEnabled = _network.Checked;
            if (!_network.Checked)
            {
                _settings.SsdpEnabled = false;
                _ssdp.Checked = false;
            }
            UpdateDependentStates();
            Save();
            NetworkChanged?.Invoke(this, EventArgs.Empty);
        };
        AddRow("", _network);

        _ssdp = Check("SSDP / DLNA 媒体共享通告", _settings.SsdpEnabled);
        _ssdp.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.SsdpEnabled = _ssdp.Checked;
            UpdateDependentStates();
            Save();
            NetworkChanged?.Invoke(this, EventArgs.Empty);
        };
        _ssdpPort = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = _settings.SsdpHttpPort,
            Width = 100,
            Font = _theme.Font(8.8f)
        };
        _ssdpPort.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.SsdpHttpPort = (int)_ssdpPort.Value;
            Save();
            NetworkChanged?.Invoke(this, EventArgs.Empty);
        };
        var ssdpRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        ssdpRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ssdpRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        ssdpRow.Controls.Add(_ssdp, 0, 0);
        ssdpRow.Controls.Add(_ssdpPort, 1, 0);
        AddRow("", ssdpRow);
        _dlnaStatus = Hint("状态：未启动");
        Hint("SSDP 只在局域网内通告媒体服务，不连接互联网；关闭后立即停止监听。");
        Separator();
    }

    private void UpdateDependentStates()
    {
        if (_ssdp == null) return;
        _ssdp.Enabled = _network.Checked;
        _ssdpPort.Enabled = _network.Checked && _ssdp.Checked;
        _miniPlugin.Enabled = _plugins.Checked;
        _desktopPlugin.Enabled = _plugins.Checked;
        if (_dlnaStatus != null)
        {
            _dlnaStatus.Text = "状态：" + (_settings.SsdpAvailable ? "等待启动" : "未启用");
        }
    }

    // ================= 插件 =================

    private void BuildPluginSection()
    {
        Section("插件（默认开启）",
            "迷你播放器与桌面歌词是本机 UI 增强，不会联网；可随时关闭。");
        _plugins = Check("启用插件功能（总开关）", _settings.PluginsEnabled);
        _plugins.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.PluginsEnabled = _plugins.Checked;
            if (!_plugins.Checked)
            {
                _settings.MiniPlayerPlugin = false;
                _settings.DesktopLyricsPlugin = false;
                _miniPlugin.Checked = false;
                _desktopPlugin.Checked = false;
            }
            UpdateDependentStates();
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        AddRow("", _plugins);
        _miniPlugin = Check("迷你播放器", _settings.MiniPlayerPlugin);
        _miniPlugin.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.MiniPlayerPlugin = _miniPlugin.Checked;
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        AddRow("", _miniPlugin);
        _desktopPlugin = Check("桌面歌词", _settings.DesktopLyricsPlugin);
        _desktopPlugin.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.DesktopLyricsPlugin = _desktopPlugin.Checked;
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        AddRow("", _desktopPlugin);
        Separator();
    }

    // ================= 界面 =================

    private void BuildInterfaceSection()
    {
        Section("界面", "主题、右侧播放面板与启动扫描偏好。");
        _themeBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            Font = _theme.Font(8.8f),
            FlatStyle = FlatStyle.Flat
        };
        _themeBox.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
        _themeBox.SelectedIndex = _settings.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        _themeBox.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.Theme = _themeBox.SelectedIndex switch
            {
                1 => "light",
                2 => "dark",
                _ => "system"
            };
            Save();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        };
        AddRow("主题", _themeBox);

        _showNowPlaying = Check("显示右侧“正在播放 / 歌词”面板", _settings.ShowNowPlaying);
        _showNowPlaying.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.ShowNowPlaying = _showNowPlaying.Checked;
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        AddRow("", _showNowPlaying);

        _scanOnOpen = Check("打开音乐窗口时自动扫描本地目录", _settings.ScanOnOpen);
        _scanOnOpen.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.ScanOnOpen = _scanOnOpen.Checked;
            Save();
        };
        AddRow("", _scanOnOpen);

        _lyricsFont = new NumericUpDown
        {
            Minimum = 16,
            Maximum = 96,
            Value = Math.Clamp(_settings.DesktopLyricsFontSize, 16, 96),
            Width = 90,
            Font = _theme.Font(8.8f)
        };
        _lyricsFont.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.DesktopLyricsFontSize = (int)_lyricsFont.Value;
            Save();
        };
        AddRow("桌面歌词字号", _lyricsFont);
        Hint("主窗口始终默认最大化显示；关闭主窗口会隐藏到托盘，音乐继续播放。");
        Separator();
    }

    // ================= 数据 =================

    private void BuildDataSection()
    {
        Section("数据与隐私", "所有数据保存在本机，不包含账号、云同步、Discord 或任何遥测。");
        var dbLabel = Hint("音乐库数据库：" + _library.DatabasePath);
        var coverLabel = Hint("封面缓存：" + MusicCoverCache.CacheDir);
        _ = dbLabel;
        _ = coverLabel;
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };
        var openData = MakeButton("打开数据目录", primary: false);
        openData.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppConfig.ConfigDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{AppConfig.ConfigDir}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开数据目录：" + ex.Message);
            }
        };
        var clearCache = MakeButton("清理封面缓存", primary: false);
        clearCache.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "确定清理封面缓存吗？\n下次扫描或播放时会重新生成。",
                    "离线音乐播放器", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                if (Directory.Exists(MusicCoverCache.CacheDir))
                {
                    Directory.Delete(MusicCoverCache.CacheDir, recursive: true);
                }
                MessageBox.Show(this, "封面缓存已清理。", "离线音乐播放器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "清理失败：" + ex.Message);
            }
        };
        var exportCsv = MakeButton("导出曲目清单 CSV", primary: false);
        exportCsv.Click += (_, _) => ExportCsv();
        buttons.Controls.Add(openData);
        buttons.Controls.Add(clearCache);
        buttons.Controls.Add(exportCsv);
        AddRow("", buttons);
        var version = Hint("离线音乐播放器 1.44.0 · Windows 原生复刻");
        _ = version;
        Separator();
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = "离线音乐播放器-曲目清单.csv",
            Title = "导出曲目清单"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("标题,艺术家,专辑,流派,年份,时长,比特率,采样率,位深,播放次数,喜欢,文件路径");
            foreach (var track in _library.GetAllTracks())
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Csv(track.DisplayTitle), Csv(track.DisplayArtist), Csv(track.DisplayAlbum),
                    Csv(track.DisplayGenre), track.Year.ToString(), track.DurationText,
                    track.Bitrate.ToString(), track.SampleRate.ToString(),
                    track.BitsPerSample.ToString(), track.PlayCount.ToString(),
                    track.Liked ? "是" : "否", Csv(track.Path)
                }));
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MessageBox.Show(this, "导出完成。", "离线音乐播放器",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message);
        }
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }

    // ================= 布局工具 =================

    private void Section(string title, string description)
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 58,
            BackColor = Color.Transparent,
            Margin = new Padding(0, _row == 0 ? 0 : 16, 0, 4)
        };
        header.Controls.Add(new Label
        {
            Text = title,
            AutoSize = false,
            Location = new Point(0, 4),
            Size = new Size(720, 26),
            Font = _theme.Font(11.6f, FontStyle.Bold),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = description,
            AutoSize = false,
            Location = new Point(1, 30),
            Size = new Size(720, 24),
            Font = _theme.Font(8.2f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent
        });
        _grid.Controls.Add(header, 0, _row);
        _grid.SetColumnSpan(header, 2);
        _row++;
    }

    private void AddRow(string label, Control control)
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        if (label.Length > 0)
        {
            _grid.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                Font = _theme.Font(8.8f),
                ForeColor = _theme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 10, 6)
            }, 0, _row);
            _grid.Controls.Add(control, 1, _row);
        }
        else
        {
            _grid.Controls.Add(control, 0, _row);
            _grid.SetColumnSpan(control, 2);
        }
        _row++;
    }

    private void AddFull(Control control)
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        _grid.Controls.Add(control, 0, _row);
        _grid.SetColumnSpan(control, 2);
        _row++;
    }

    private Label Hint(string text)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 24,
            Font = _theme.Font(8.1f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2)
        };
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(label, 0, _row);
        _grid.SetColumnSpan(label, 2);
        _row++;
        return label;
    }

    private void Separator()
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var line = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 1,
            BackColor = _theme.Border,
            Margin = new Padding(0, 14, 0, 6)
        };
        _grid.Controls.Add(line, 0, _row);
        _grid.SetColumnSpan(line, 2);
        _row++;
    }

    private CheckBox Check(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = true,
            Font = _theme.Font(8.9f),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 4)
        };
    }

    private Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Height = 30,
            Width = TextRenderer.MeasureText(text, _theme.Font(8.6f)).Width + 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? _theme.Accent : _theme.SurfaceAlt,
            ForeColor = primary ? Color.White : _theme.TextSecondary,
            Font = _theme.Font(8.6f),
            Margin = new Padding(0, 2, 8, 2),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? _theme.Accent : _theme.Border;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void Save()
    {
        _config.Save();
    }
}
