using System.Drawing.Drawing2D;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

public sealed partial class MusicPlayerForm
{
    private MusicCoverBox _coverBox = null!;
    private Label _npTitle = null!;
    private Label _npArtist = null!;
    private Label _npAlbum = null!;
    private MusicLyricsView _lyricsView = null!;
    private MusicSeekBar _seekBar = null!;
    private Label _timeCurrent = null!;
    private Label _timeTotal = null!;
    private MusicIconButton _prevButton = null!;
    private MusicIconButton _playButton = null!;
    private MusicIconButton _nextButton = null!;
    private MusicIconButton _shuffleButton = null!;
    private MusicIconButton _repeatButton = null!;
    private MusicIconButton _likeCurrentButton = null!;
    private MusicIconButton _lyricsToggleButton = null!;
    private MusicIconButton _miniButton = null!;
    private MusicIconButton _desktopLyricsButton = null!;
    private MusicIconButton _volumeButton = null!;
    private MusicSlider _volumeSlider = null!;
    private MusicCoverBox _barCover = null!;
    private Label _barTitle = null!;
    private Label _barArtist = null!;
    private TableLayoutPanel _contentLayout = null!;
    private TableLayoutPanel? _nowPlayingLayout;
    private bool _suppressVolume;
    private LrcDocument? _lyrics;
    private MusicMiniPlayerForm? _miniPlayer;
    private DesktopLyricsForm? _desktopLyrics;
    private TimeSpan? _abA;
    private TimeSpan? _abB;
    private MusicIconButton _abAButton = null!;
    private MusicIconButton _abBButton = null!;
    private MusicIconButton _abClearButton = null!;
    private CheckBox _desktopLyricsCheck = null!;
    private bool _suppressDesktopLyricsCheck;

    private int Dpi(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private Panel BuildNowPlayingPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface, Padding = new Padding(0) };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, 0, panel.Height);
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = new Padding(Dpi(20), Dpi(14), Dpi(20), Dpi(12)),
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(286)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(86)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(40)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _nowPlayingLayout = layout;

        var caption = new Label
        {
            Text = "正在播放",
            Dock = DockStyle.Fill,
            Font = _theme.Font(9.4f, FontStyle.Bold),
            ForeColor = _theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        layout.Controls.Add(caption, 0, 0);
        _coverBox = new MusicCoverBox { Dock = DockStyle.Fill, Theme = _theme };
        layout.Controls.Add(_coverBox, 0, 1);

        var info = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _npTitle = new Label
        {
            AutoSize = false,
            Location = new Point(0, Dpi(6)),
            Height = Dpi(28),
            Font = _theme.Font(11.6f, FontStyle.Bold),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent,
            Text = "未在播放",
            AutoEllipsis = true
        };
        _npArtist = new Label
        {
            AutoSize = false,
            Location = new Point(0, Dpi(36)),
            Height = Dpi(22),
            Font = _theme.Font(9.2f),
            ForeColor = _theme.TextSecondary,
            BackColor = Color.Transparent,
            Text = "选择一首歌开始播放"
        };
        _npAlbum = new Label
        {
            AutoSize = false,
            Location = new Point(0, Dpi(58)),
            Height = Dpi(20),
            Font = _theme.Font(8.2f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent
        };
        info.Controls.Add(_npTitle);
        info.Controls.Add(_npArtist);
        info.Controls.Add(_npAlbum);
        info.Resize += (_, _) =>
        {
            foreach (var label in new[] { _npTitle, _npArtist, _npAlbum })
            {
                label.Width = info.Width;
            }
        };
        layout.Controls.Add(info, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Dpi(2), 0, 0)
        };
        _likeCurrentButton = new MusicIconButton(_theme, "\uE734", 11f) { Size = new Size(36, 34) };
        _likeCurrentButton.Click += (_, _) =>
        {
            if (_player.CurrentTrack != null) ToggleLike(_player.CurrentTrack);
        };
        var editButton = new MusicIconButton(_theme, "\uE70F", 10.5f) { Size = new Size(36, 34) };
        editButton.Click += (_, _) =>
        {
            if (_player.CurrentTrack != null) EditCurrentTrack();
        };
        var locateButton = new MusicIconButton(_theme, "\uE838", 10.5f) { Size = new Size(36, 34) };
        locateButton.Click += (_, _) =>
        {
            var track = _player.CurrentTrack;
            if (track == null) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{track.Path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Toast("无法定位文件：" + ex.Message);
            }
        };
        var queueButton = new MusicIconButton(_theme, "\uE8FD", 10.5f) { Size = new Size(36, 34) };
        queueButton.Click += (_, _) => ShowView(MusicView.Songs);
        _abAButton = new MusicIconButton(_theme, "\uE8EF", 10.5f) { Size = new Size(36, 34) };
        _abAButton.Click += (_, _) => SetAbPoint(true);
        _abBButton = new MusicIconButton(_theme, "\uE8F0", 10.5f) { Size = new Size(36, 34) };
        _abBButton.Click += (_, _) => SetAbPoint(false);
        _abClearButton = new MusicIconButton(_theme, "\uE711", 9.5f) { Size = new Size(36, 34) };
        _abClearButton.Click += (_, _) => ClearAbLoop();
        actions.Controls.Add(_likeCurrentButton);
        actions.Controls.Add(editButton);
        actions.Controls.Add(locateButton);
        actions.Controls.Add(queueButton);
        actions.Controls.Add(_abAButton);
        actions.Controls.Add(_abBButton);
        actions.Controls.Add(_abClearButton);
        layout.Controls.Add(actions, 0, 3);

        var lyricsHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        lyricsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lyricsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(104)));
        var lyricsCaption = new Label
        {
            Text = "歌词",
            Dock = DockStyle.Fill,
            Font = _theme.Font(9f, FontStyle.Bold),
            ForeColor = _theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        _desktopLyricsCheck = new CheckBox
        {
            Text = "桌面歌词",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextSecondary,
            BackColor = Color.Transparent,
            Checked = _settings.DesktopLyricsVisible,
            TextAlign = ContentAlignment.MiddleRight
        };
        _desktopLyricsCheck.CheckedChanged += (_, _) =>
        {
            if (_suppressDesktopLyricsCheck)
            {
                return;
            }
            SetDesktopLyricsVisible(_desktopLyricsCheck.Checked);
        };
        lyricsHeader.Controls.Add(lyricsCaption, 0, 0);
        lyricsHeader.Controls.Add(_desktopLyricsCheck, 1, 0);
        layout.Controls.Add(lyricsHeader, 0, 4);
        _lyricsView = new MusicLyricsView { Dock = DockStyle.Fill, Theme = _theme };
        _lyricsView.LineClicked += (_, time) => _player.Seek(time);
        layout.Controls.Add(_lyricsView, 0, 5);

        panel.Controls.Add(layout);
        return panel;
    }

    private Panel BuildPlayerBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(Dpi(18), Dpi(8), Dpi(18), Dpi(8)),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(300)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(360)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 左：缩略封面 + 标题
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(60)));
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _barCover = new MusicCoverBox { Dock = DockStyle.Fill, Theme = _theme, Margin = new Padding(0, 0, Dpi(10), 0) };
        left.Controls.Add(_barCover, 0, 0);
        var textPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _barTitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = Dpi(28),
            Font = _theme.Font(9.6f, FontStyle.Bold),
            ForeColor = _theme.Text,
            Text = "未在播放",
            AutoEllipsis = true
        };
        _barArtist = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = Dpi(22),
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextTertiary,
            Text = "离线音乐播放器",
            AutoEllipsis = true
        };
        textPanel.Controls.Add(_barArtist);
        textPanel.Controls.Add(_barTitle);
        left.Controls.Add(textPanel, 1, 0);
        layout.Controls.Add(left, 0, 0);

        // 中：控制按钮 + 进度
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(42)));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Dpi(2), 0, 0)
        };
        _shuffleButton = new MusicIconButton(_theme, "\uE8B1", 10f) { Size = new Size(34, 32) };
        _shuffleButton.Click += (_, _) =>
        {
            _settings.Shuffle = !_settings.Shuffle;
            _player.SetShuffle(_settings.Shuffle);
            _config.Save();
            UpdatePlayerBarState();
            Toast(_settings.Shuffle ? "随机播放已开启" : "随机播放已关闭");
        };
        _prevButton = new MusicIconButton(_theme, "\uE892", 12f) { Size = new Size(36, 32) };
        _prevButton.Click += (_, _) => _player.Previous();
        _playButton = new MusicIconButton(_theme, "\uE768", 16f) { Size = new Size(46, 34), Active = true };
        _playButton.Click += (_, _) => TogglePlayPause();
        _nextButton = new MusicIconButton(_theme, "\uE893", 12f) { Size = new Size(36, 32) };
        _nextButton.Click += (_, _) => _player.Next();
        _repeatButton = new MusicIconButton(_theme, "\uE8EE", 10f) { Size = new Size(34, 32) };
        _repeatButton.Click += (_, _) =>
        {
            string mode = _player.CycleRepeatMode();
            _config.Save();
            UpdatePlayerBarState();
            Toast(mode switch
            {
                "off" => "列表循环已关闭",
                "all" => "列表循环",
                _ => "单曲循环"
            });
        };
        buttons.Controls.Add(_shuffleButton);
        buttons.Controls.Add(_prevButton);
        buttons.Controls.Add(_playButton);
        buttons.Controls.Add(_nextButton);
        buttons.Controls.Add(_repeatButton);
        center.Controls.Add(buttons, 0, 0);

        var seekRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(48)));
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(48)));
        _timeCurrent = new Label
        {
            Text = "0:00",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };
        _seekBar = new MusicSeekBar { Dock = DockStyle.Fill, Theme = _theme };
        _seekBar.SeekRequested += (_, fraction) => _player.SeekFraction(fraction);
        _timeTotal = new Label
        {
            Text = "0:00",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        seekRow.Controls.Add(_timeCurrent, 0, 0);
        seekRow.Controls.Add(_seekBar, 1, 0);
        seekRow.Controls.Add(_timeTotal, 2, 0);
        center.Controls.Add(seekRow, 0, 1);
        layout.Controls.Add(center, 1, 0);

        // 右：音量与插件
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Dpi(4), 0, 0)
        };
        var moreButton = new MusicIconButton(_theme, "\uE712", 10f) { Size = new Size(34, 30) };
        var moreMenu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        moreMenu.Items.Add("导入 M3U 播放列表…", null, (_, _) => ImportM3U());
        moreMenu.Items.Add("导出当前列表为 M3U…", null, (_, _) => ExportM3U());
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("扫描音乐目录 (F5)", null, (_, _) => StartScan());
        moreMenu.Items.Add("打开音乐目录", null, (_, _) => OpenLibraryFolder());
        moreMenu.Items.Add("听歌统计", null, (_, _) => ShowView(MusicView.Stats));
        moreMenu.Items.Add("设置", null, (_, _) => ShowView(MusicView.Settings));
        moreButton.Click += (_, _) => moreMenu.Show(moreButton, new Point(0, moreButton.Height));
        _desktopLyricsButton = new MusicIconButton(_theme, "\uE7F4", 10f) { Size = new Size(34, 30) };
        _desktopLyricsButton.Click += (_, _) => ToggleDesktopLyrics();
        _miniButton = new MusicIconButton(_theme, "\uE8A4", 10f) { Size = new Size(34, 30) };
        _miniButton.Click += (_, _) => ToggleMiniPlayer();
        _lyricsToggleButton = new MusicIconButton(_theme, "\uE8A5", 10f) { Size = new Size(34, 30) };
        _lyricsToggleButton.Click += (_, _) =>
        {
            _settings.ShowNowPlaying = !_settings.ShowNowPlaying;
            _config.Save();
            ApplyPlaybackSettings();
        };
        _volumeButton = new MusicIconButton(_theme, "\uE767", 10f) { Size = new Size(32, 30) };
        _volumeButton.Click += (_, _) =>
        {
            _settings.Mute = !_settings.Mute;
            _player.SetMute(_settings.Mute);
            _config.Save();
            UpdateVolumeUi();
        };
        _volumeSlider = new MusicSlider
        {
            Width = Dpi(96),
            Height = 30,
            Minimum = 0,
            Maximum = 100,
            Value = _settings.Volume,
            Theme = _theme,
            ShowValue = false,
            Margin = new Padding(0, Dpi(2), Dpi(4), 0)
        };
        _suppressVolume = true;
        _volumeSlider.Value = _settings.Volume;
        _suppressVolume = false;
        _volumeSlider.ValueChanged += (_, _) =>
        {
            if (_suppressVolume) return;
            _settings.Volume = (int)Math.Round(_volumeSlider.Value);
            _settings.Mute = false;
            _player.SetVolume(_settings.Volume);
            _config.Save();
            UpdateVolumeUi();
        };
        var settingsButton = new MusicIconButton(_theme, "\uE713", 10f) { Size = new Size(34, 30) };
        settingsButton.Click += (_, _) => ShowView(MusicView.Settings);
        right.Controls.Add(moreButton);
        right.Controls.Add(settingsButton);
        right.Controls.Add(_desktopLyricsButton);
        right.Controls.Add(_miniButton);
        right.Controls.Add(_lyricsToggleButton);
        right.Controls.Add(_volumeSlider);
        right.Controls.Add(_volumeButton);
        layout.Controls.Add(right, 2, 0);

        bar.Controls.Add(layout);
        return bar;
    }

    private void BindPlayerEvents()
    {
        _player.StateChanged += (_, _) => UpdatePlayerBarState();
        _player.TrackChanged += (_, _) =>
        {
            _abA = null;
            _abB = null;
            _player.ClearAbLoop();
            UpdateAbButtons();
            UpdateNowPlayingTrack();
            _trackList.PlayingTrackId = (int)(_player.CurrentTrack?.Id ?? -1);
            SaveQueue();
            _miniPlayer?.RefreshFromPlayer();
            _desktopLyrics?.RefreshFromPlayer();
        };
        _player.QueueChanged += (_, _) => SaveQueue();
        _player.PositionChanged += (_, _) =>
        {
            UpdatePositionUi();
        };
        _player.PlayCompleted += (_, track) =>
        {
            try
            {
                _library.RecordPlay(track.Id, track.DurationSeconds);
            }
            catch
            {
                // 统计写入失败不影响播放
            }
            if (_view is MusicView.MostPlayed or MusicView.Stats)
            {
                ReloadCurrentView();
            }
        };
        _player.PlaybackError += (_, message) => Toast(message);
    }

    private void PlayAllVisible()
    {
        var tracks = _trackList.Tracks;
        if (tracks.Count == 0)
        {
            Toast("当前列表没有曲目");
            return;
        }
        PlayTrack(tracks[0]);
    }

    private void PlayTrack(MusicTrack track)
    {
        var source = _trackList.Tracks;
        int index = source.ToList().FindIndex(t => t.Id == track.Id);
        if (index < 0)
        {
            index = 0;
            source = new List<MusicTrack> { track };
        }
        _player.SetQueue(source, index, true);
        UpdateNowPlayingTrack();
    }

    private void TogglePlayPause()
    {
        _player.TogglePlayPause();
        UpdatePlayerBarState();
    }

    private void ToggleLike(MusicTrack track)
    {
        track.Liked = !track.Liked;
        _library.SetLiked(track.Id, track.Liked);
        _trackList.RefreshTrack(track);
        RefreshSidebar();
        UpdateLikeButton();
        Toast(track.Liked ? "已加入喜欢音乐" : "已取消喜欢");
    }

    private void UpdateLikeButton()
    {
        var track = _player.CurrentTrack;
        if (_likeCurrentButton == null) return;
        _likeCurrentButton.Enabled = track != null;
        _likeCurrentButton.Glyph = track?.Liked == true ? "\uE735" : "\uE734";
        _likeCurrentButton.Active = track?.Liked == true;
    }

    private void UpdateNowPlayingTrack()
    {
        var track = _player.CurrentTrack;
        if (track == null)
        {
            _coverBox.CoverKey = "";
            _barCover.CoverKey = "";
            _npTitle.Text = "未在播放";
            _npArtist.Text = "选择一首歌开始播放";
            _npAlbum.Text = "";
            _barTitle.Text = "未在播放";
            _barArtist.Text = "离线音乐播放器";
            _lyrics = null;
            _lyricsView.Document = null;
            UpdateLikeButton();
            return;
        }
        _coverBox.CoverKey = track.CoverKey;
        _barCover.CoverKey = track.CoverKey;
        _npTitle.Text = track.DisplayTitle;
        _npArtist.Text = track.DisplayArtist;
        _npAlbum.Text = $"{track.DisplayAlbum} · {track.DurationText}"
            + (track.IsHiRes ? " · Hi-Res" : "");
        _barTitle.Text = track.DisplayTitle;
        _barArtist.Text = $"{track.DisplayArtist} · {track.DisplayAlbum}";
        LoadLyrics(track);
        UpdateLikeButton();
        _desktopLyrics?.RefreshFromPlayer();
        if (_settings.DesktopLyricsAvailable && _desktopLyrics == null)
        {
            // 未打开桌面歌词时保持关闭
        }
    }

    private void LoadLyrics(MusicTrack track)
    {
        _lyrics = LrcDocument.LoadForTrack(track);
        _lyricsView.Document = _lyrics;
        if (_lyrics == null)
        {
            _lyricsView.Placeholder = "暂无歌词";
        }
        else if (!_lyrics.HasTimestamps)
        {
            _lyricsView.Placeholder = "未同步歌词";
        }
        _lyricsView.CurrentIndex = -1;
        UpdateLyricsPosition();
    }

    private void UpdatePositionUi()
    {
        var duration = _player.Duration;
        var position = _player.Position;
        _seekBar.Value = duration.TotalSeconds > 0
            ? position.TotalSeconds / duration.TotalSeconds
            : 0;
        _timeCurrent.Text = FormatTime(position);
        _timeTotal.Text = FormatTime(duration);
        UpdateLyricsPosition();
        if (_desktopLyrics is { Visible: false })
        {
            // 保持隐藏状态
        }
    }

    private void UpdateLyricsPosition()
    {
        if (_lyrics == null || _lyricsView == null)
        {
            return;
        }
        int index = _lyrics.IndexAt(_player.Position);
        _lyricsView.CurrentIndex = index;
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }

    private void UpdatePlayerBarState()
    {
        if (_playButton == null) return;
        _playButton.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
        _playButton.Active = _player.IsPlaying;
        _shuffleButton.Active = _settings.Shuffle;
        _repeatButton.Glyph = _settings.RepeatMode switch
        {
            "one" => "\uE8ED",
            "off" => "\uE8EE",
            _ => "\uE8EE"
        };
        _repeatButton.Active = _settings.RepeatMode != "off";
        UpdateVolumeUi();
        UpdateLikeButton();
        _miniPlayer?.RefreshFromPlayer();
    }

    private void UpdateVolumeUi()
    {
        if (_volumeButton == null) return;
        bool muted = _settings.Mute || _settings.Volume == 0;
        _volumeButton.Glyph = muted ? "\uE74F" : _settings.Volume < 35
            ? "\uE993" : "\uE767";
        _volumeButton.Active = muted;
        _suppressVolume = true;
        _volumeSlider.Value = _settings.Volume;
        _suppressVolume = false;
    }

    private void ApplyPlaybackSettings()
    {
        _player.SetVolume(_settings.Volume);
        _player.SetMute(_settings.Mute);
        _player.SetShuffle(_settings.Shuffle);
        _player.SetEq(_settings.EqGains, _settings.EqEnabled, _settings.PreampDb);
        _player.SetStereo(_settings.Balance, _settings.StereoWidth);
        _player.SetSpeed(_settings.PlaybackSpeed);
        _player.SetReplayGainEnabled(_settings.ReplayGainEnabled);
        if (_nowPlayingPanel != null)
        {
            _nowPlayingPanel.Visible = _settings.ShowNowPlaying;
            if (_contentLayout.ColumnStyles.Count >= 3)
            {
                _contentLayout.ColumnStyles[2].Width = _settings.ShowNowPlaying ? Dpi(326) : 0;
            }
        }
        UpdatePlayerBarState();
        UpdatePluginButtons();
    }

    private void UpdatePluginButtons()
    {
        if (_miniButton == null) return;
        bool mini = _settings.MiniPlayerAvailable;
        bool lyrics = _settings.DesktopLyricsAvailable;
        _miniButton.Enabled = mini;
        _desktopLyricsButton.Enabled = lyrics;
        if (_titleMiniButton != null) _titleMiniButton.Enabled = mini;
        if (_titleLyricsButton != null) _titleLyricsButton.Enabled = lyrics;
        if (_desktopLyricsCheck != null) _desktopLyricsCheck.Enabled = lyrics;
        if (!mini && _miniPlayer != null)
        {
            _miniPlayer.Close();
            _miniPlayer = null;
        }
        if (!lyrics && _desktopLyrics != null)
        {
            _desktopLyrics.Close();
            _desktopLyrics = null;
        }
        UpdateDesktopLyricsUi();
        if (_titleMiniButton != null)
        {
            _titleMiniButton.Active = _miniPlayer is { Visible: true };
        }
    }

    private int _dlnaPort;

    private void ApplyNetworkServices()
    {
        if (_settings.SsdpAvailable)
        {
            if (_dlna != null && _dlnaPort != _settings.SsdpHttpPort)
            {
                _dlna.Dispose();
                _dlna = null;
            }
            _dlna ??= new DlnaMediaServer(_library, _settings.SsdpHttpPort);
            _dlnaPort = _settings.SsdpHttpPort;
            if (!_dlna.IsRunning)
            {
                _dlna.Start();
                Toast(_dlna.IsRunning ? "SSDP/DLNA 已启动" : _dlna.Status);
            }
        }
        else
        {
            if (_dlna is { IsRunning: true })
            {
                _dlna.Stop();
            }
        }
    }

    private void OpenMiniPlayer()
    {
        if (!_settings.MiniPlayerAvailable)
        {
            Toast("迷你播放器插件未开启，请前往设置 → 插件");
            return;
        }
        try
        {
            if (_miniPlayer == null || _miniPlayer.IsDisposed)
            {
                _miniPlayer = new MusicMiniPlayerForm(_player, _settings, _theme);
                _miniPlayer.ShowMainRequested += (_, _) => SwitchToMainWindow();
                _miniPlayer.FormClosed += (_, _) =>
                {
                    _miniPlayer = null;
                    if (!_allowClose && !Visible)
                    {
                        ShowReader();
                    }
                };
            }
            _miniPlayer.Show();
            _miniPlayer.RefreshFromPlayer();
            _miniPlayer.BringToFront();
        }
        catch (Exception ex)
        {
            Toast("无法打开迷你播放器：" + ex.Message);
        }
    }

    private void ToggleMiniPlayer()
    {
        if (!_settings.MiniPlayerAvailable)
        {
            Toast("迷你播放器插件未开启，请前往设置 → 插件");
            return;
        }
        if (_miniPlayer is { Visible: true })
        {
            SwitchToMainWindow();
            return;
        }
        SaveWindowState();
        OpenMiniPlayer();
        if (_miniPlayer is { Visible: true })
        {
            Hide();
        }
    }

    private void SwitchToMainWindow()
    {
        _miniPlayer?.Hide();
        ShowReader();
        UpdatePluginButtons();
    }

    private void ToggleDesktopLyrics()
    {
        if (!_settings.DesktopLyricsAvailable)
        {
            Toast("桌面歌词插件未开启，请前往设置 → 插件");
            return;
        }
        bool visible = _desktopLyrics is { Visible: true };
        SetDesktopLyricsVisible(!visible);
    }

    private void SetDesktopLyricsVisible(bool visible)
    {
        if (visible && !_settings.DesktopLyricsAvailable)
        {
            Toast("桌面歌词插件未开启，请前往设置 → 插件");
            UpdateDesktopLyricsUi();
            return;
        }
        if (visible)
        {
            try
            {
                if (_desktopLyrics == null || _desktopLyrics.IsDisposed)
                {
                    _desktopLyrics = new DesktopLyricsForm(_player, _settings, _theme);
                    _desktopLyrics.ShowMainRequested += (_, _) => SwitchToMainWindow();
                    _desktopLyrics.FormClosed += (_, _) =>
                    {
                        _desktopLyrics = null;
                        if (!_allowClose)
                        {
                            _settings.DesktopLyricsVisible = false;
                            _config.Save();
                        }
                        UpdateDesktopLyricsUi();
                    };
                }
                _desktopLyrics.Show();
                _desktopLyrics.RefreshFromPlayer();
                _desktopLyrics.BringToFront();
                _settings.DesktopLyricsVisible = true;
            }
            catch (Exception ex)
            {
                _settings.DesktopLyricsVisible = false;
                Toast("无法显示桌面歌词：" + ex.Message);
            }
        }
        else
        {
            _desktopLyrics?.Hide();
            _settings.DesktopLyricsVisible = false;
        }
        _config.Save();
        UpdateDesktopLyricsUi();
    }

    private void UpdateDesktopLyricsUi()
    {
        bool available = _settings.DesktopLyricsAvailable;
        bool visible = _desktopLyrics is { Visible: true };
        if (_desktopLyricsButton != null)
        {
            _desktopLyricsButton.Enabled = available;
            _desktopLyricsButton.Active = visible;
        }
        if (_titleLyricsButton != null)
        {
            _titleLyricsButton.Enabled = available;
            _titleLyricsButton.Active = visible;
        }
        if (_desktopLyricsCheck != null)
        {
            _suppressDesktopLyricsCheck = true;
            _desktopLyricsCheck.Enabled = available;
            _desktopLyricsCheck.Checked = visible;
            _suppressDesktopLyricsCheck = false;
        }
    }

    private void SetAbPoint(bool isA)
    {
        if (_player.CurrentTrack == null || _player.Duration <= TimeSpan.Zero)
        {
            Toast("请先播放一首歌曲");
            return;
        }
        if (isA)
        {
            _abA = _player.Position;
            _abB = null;
            _player.ClearAbLoop();
            Toast("已设置 A 点：" + FormatTime(_abA.Value));
        }
        else
        {
            if (_abA == null)
            {
                Toast("请先设置 A 点");
                return;
            }
            if (_player.Position <= _abA.Value)
            {
                Toast("B 点必须晚于 A 点");
                return;
            }
            _abB = _player.Position;
            _player.SetAbLoop(_abA, _abB);
            Toast($"A-B 循环：{FormatTime(_abA.Value)} → {FormatTime(_abB.Value)}");
        }
        UpdateAbButtons();
    }

    private void ClearAbLoop()
    {
        _abA = null;
        _abB = null;
        _player.ClearAbLoop();
        UpdateAbButtons();
        Toast("已取消 A-B 循环");
    }

    private void UpdateAbButtons()
    {
        if (_abAButton == null) return;
        _abAButton.Active = _abA != null;
        _abBButton.Active = _abB != null;
        _abClearButton.Enabled = _abA != null || _abB != null;
    }

    private void EditCurrentTrack()
    {
        var track = _player.CurrentTrack;
        if (track == null)
        {
            return;
        }
        using var dialog = new MusicTagDialog(track, _library);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            UpdateNowPlayingTrack();
            LoadLyrics(track);
            Toast("标签已保存");
        }
    }
}
