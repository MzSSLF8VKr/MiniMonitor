using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;

namespace MiniMonitor.Video;

/// <summary>
/// 无损视频截取复刻版（对标 LosslessCut 3.70.2）：关键帧吸附、片段列表、
/// 无损流复制导出、片段合并、文件拼接、提取音频、截图、场景 / 黑帧检测。
/// 默认最大化，F11 全屏，Esc 退出全屏。
/// </summary>
public sealed class LosslessCutForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AppConfig _config;
    private readonly VideoPreviewControl _preview;
    private readonly LosslessCutTimeline _timeline;
    private readonly ListView _fileList;
    private readonly ListView _segmentList;
    private readonly Label _infoLabel;
    private Label _inPointLabel = null!;
    private Label _outPointLabel = null!;
    private Label _markerDurationLabel = null!;
    private readonly Label _timeLabel;
    private readonly Label _durationLabel;
    private readonly Button _playButton;
    private readonly VideoSeekBar _seek;
    private readonly TrackBar _volume;
    private readonly ComboBox _speed;
    private readonly HScrollBar _scroll;
    private readonly SplitContainer _mainSplit;
    private readonly SplitContainer _rightSplit;
    private readonly Panel _previewControls;
    private readonly Panel _timelineHost;
    private readonly MenuStrip _menu;
    private readonly ToolStrip _toolbar;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _gpuLabel;
    private readonly ToolStripStatusLabel _statusInfo;
    private readonly ToolStripStatusLabel _countLabel;
    private readonly System.Windows.Forms.Timer _playTimer;
    private readonly Stopwatch _playClock = new();
    private readonly List<LosslessFile> _files = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private LosslessFile? _activeFile;
    private LosslessSegment? _selectedSegment;
    private HardwareEncoders _encoders = new();
    private double _playhead;
    private double _playAnchor;
    private double _playbackRate = 1;
    private double _inPoint = -1;
    private double _outPoint = -1;
    private bool _playing;
    private bool _busy;
    private bool _fullscreen;
    private bool _suppressFileSelection;
    private bool _suppressSegmentSelection;
    private string? _previewLoadedPath;
    private readonly Dictionary<string, string> _previewProxies =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _proxyBuilding =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _proxyFailed =
        new(StringComparer.OrdinalIgnoreCase);
    private FormBorderStyle _savedBorder;
    private FormWindowState _savedState;
    private VideoProgressDialog? _progressDialog;

    public LosslessCutForm(AppConfig config)
    {
        _config = config;
        Text = "无损视频截取 - MiniMonitor";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 700);
        BackColor = VideoTheme.Window;
        ForeColor = VideoTheme.Text;
        Font = VideoTheme.Ui9;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Font;
        try
        {
            using var iconStream = typeof(LosslessCutForm).Assembly.GetManifestResourceStream("MiniMonitor.app.ico");
            if (iconStream != null)
            {
                Icon = new Icon(iconStream);
            }
        }
        catch
        {
            // ignore
        }

        _menu = BuildMenu();
        _toolbar = BuildToolbar();

        _fileList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        VideoTheme.StyleListView(_fileList);
        _fileList.Columns.Add("文件", 210);
        _fileList.Columns.Add("时长", 78);
        _fileList.Columns.Add("大小", 78);
        _fileList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressFileSelection)
            {
                return;
            }
            if (_fileList.SelectedItems.Count > 0
                && _fileList.SelectedItems[0].Tag is LosslessFile file)
            {
                SetActiveFile(file);
            }
        };
        _fileList.ItemChecked += (_, e) =>
        {
            if (e.Item?.Tag is LosslessFile file)
            {
                file.Enabled = e.Item.Checked;
                UpdateCount();
            }
        };
        _fileList.DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        _fileList.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                await AddFilesAsync(files);
            }
        };
        _fileList.AllowDrop = true;

        _infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = VideoTheme.Muted,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiSmall,
            Padding = new Padding(8, 6, 8, 6),
            Text = "打开视频或音频文件开始"
        };

        _segmentList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false
        };
        VideoTheme.StyleListView(_segmentList);
        _segmentList.Columns.Add("#", 38);
        _segmentList.Columns.Add("开始", 78);
        _segmentList.Columns.Add("结束", 78);
        _segmentList.Columns.Add("时长", 70);
        _segmentList.Columns.Add("名称", 130);
        var left = BuildLeftPanel();
        var right = BuildRightPanel(out _previewControls, out _timelineHost, out _rightSplit,
            out _preview, out _timeline, out _seek, out _playButton, out _timeLabel,
            out _durationLabel, out _volume, out _speed, out _scroll);

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            BackColor = VideoTheme.Border
        };
        _mainSplit.Panel1.Controls.Add(left);
        _mainSplit.Panel2.Controls.Add(right);

        _status = new StatusStrip
        {
            BackColor = VideoTheme.Panel,
            ForeColor = VideoTheme.Muted,
            Font = VideoTheme.UiSmall,
            SizingGrip = false
        };
        _gpuLabel = new ToolStripStatusLabel("GPU：检测中…")
        {
            ForeColor = VideoTheme.Muted
        };
        _statusInfo = new ToolStripStatusLabel("就绪")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VideoTheme.Muted
        };
        _countLabel = new ToolStripStatusLabel("0 个文件 · 0 个片段")
        {
            ForeColor = VideoTheme.Muted
        };
        _status.Items.AddRange(new ToolStripItem[] { _gpuLabel, new ToolStripSeparator(), _statusInfo, _countLabel });

        Controls.Add(_mainSplit);
        Controls.Add(_status);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;

        _timeline.SeekRequested += (_, time) =>
        {
            if (_playing)
            {
                Pause();
            }
            SeekTo(time, true);
        };
        _timeline.SelectionChanged += (_, _) =>
        {
            _selectedSegment = _timeline.SelectedSegment;
            SyncSegmentListSelection();
        };
        _timeline.BeforeEdit += (_, _) => PushUndo();
        _timeline.SegmentChanged += (_, _) =>
        {
            _activeFile?.Segments.Sort((left, right) => left.Start.CompareTo(right.Start));
            RefreshSegmentList();
            UpdateCount();
            _statusInfo.Text = "片段已更新";
        };
        _timeline.SegmentContextRequested += TimelineSegmentContextRequested;
        _segmentList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSegmentSelection)
            {
                return;
            }
            _selectedSegment = _segmentList.SelectedItems.Count > 0
                ? _segmentList.SelectedItems[0].Tag as LosslessSegment : null;
            _timeline.SelectedSegment = _selectedSegment;
        };
        _segmentList.MouseDoubleClick += (_, _) =>
        {
            if (_selectedSegment != null)
            {
                SeekTo(_selectedSegment.Start, true);
            }
        };

        _playTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _playTimer.Tick += (_, _) => OnPlayTick();

        Load += (_, _) =>
        {
            PositionSplitters();
            UpdateCount();
            UpdateInfo();
            UpdateMarkers();
            _ = RefreshEncodersAsync();
            if (VideoToolkit.Ffmpeg(_config.FfmpegPath) == null)
            {
                _statusInfo.Text = "未找到 ffmpeg.exe：请把 ffmpeg.exe / ffprobe.exe 放到程序目录，或在“帮助 → 设置 FFmpeg”中指定。";
            }
        };
        FormClosing += (_, _) =>
        {
            _playTimer.Stop();
            if (_progressDialog is { IsDisposed: false } progress)
            {
                progress.CancelAndClose();
            }
        };
        Resize += (_, _) => UpdateScrollBar();
    }

    // ================= 界面 =================

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };
        VideoTheme.StyleMenuStrip(menu);

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(MenuItem("打开文件…", Keys.Control | Keys.O, async () => await OpenFilesDialogAsync()));
        file.DropDownItems.Add(MenuItem("添加文件…", Keys.Control | Keys.Shift | Keys.O,
            async () => await OpenFilesDialogAsync()));
        file.DropDownItems.Add(MenuItem("移除当前文件", Keys.None, RemoveActiveFile));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("退出", Keys.None, Close));

        var edit = new ToolStripMenuItem("编辑(&E)");
        edit.DropDownItems.Add(MenuItem("撤销", Keys.Control | Keys.Z, Undo));
        edit.DropDownItems.Add(MenuItem("重做", Keys.Control | Keys.Y, Redo));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(MenuItem("设入点（I）", Keys.I, SetInPoint));
        edit.DropDownItems.Add(MenuItem("设出点（O）", Keys.O, SetOutPoint));
        edit.DropDownItems.Add(MenuItem("添加片段", Keys.None, AddSegmentFromMarkers));
        edit.DropDownItems.Add(MenuItem("在播放头分割", Keys.S, SplitAtPlayhead));
        edit.DropDownItems.Add(MenuItem("删除选中片段", Keys.Delete, DeleteSelectedSegments));
        edit.DropDownItems.Add(MenuItem("合并选中片段", Keys.None, MergeSelectedSegments));

        var segments = new ToolStripMenuItem("片段(&S)");
        segments.DropDownItems.Add(MenuItem("全选片段", Keys.Control | Keys.A, SelectAllSegments));
        segments.DropDownItems.Add(MenuItem("反转启用状态", Keys.None, InvertSelection));
        segments.DropDownItems.Add(MenuItem("启用 / 禁用选中", Keys.None, ToggleSelectedEnabled));
        segments.DropDownItems.Add(new ToolStripSeparator());
        segments.DropDownItems.Add(MenuItem("检测场景切换…", Keys.None, DetectScenes));
        segments.DropDownItems.Add(MenuItem("检测黑帧…", Keys.None, DetectBlackFrames));

        var export = new ToolStripMenuItem("导出(&X)");
        export.DropDownItems.Add(MenuItem("导出片段…", Keys.Control | Keys.E, ShowExportDialog));
        export.DropDownItems.Add(MenuItem("合并当前文件…", Keys.None, MergeCurrentFiles));
        export.DropDownItems.Add(MenuItem("提取音频…", Keys.None, ExtractAudio));
        export.DropDownItems.Add(MenuItem("截取当前帧…", Keys.None, TakeScreenshot));
        export.DropDownItems.Add(MenuItem("导出章节（FFmetadata）…", Keys.None, ExportChapters));

        var view = new ToolStripMenuItem("视图(&V)");
        view.DropDownItems.Add(MenuItem("全屏预览", Keys.F11, ToggleFullscreen));
        view.DropDownItems.Add(MenuItem("放大时间线", Keys.Control | Keys.Oemplus,
            () => SetZoom(_timeline.PixelsPerSecond * 1.25)));
        view.DropDownItems.Add(MenuItem("缩小时间线", Keys.Control | Keys.OemMinus,
            () => SetZoom(_timeline.PixelsPerSecond / 1.25)));
        view.DropDownItems.Add(MenuItem("适应文件", Keys.None, FitTimeline));
        view.DropDownItems.Add(MenuItem("重新生成预览代理", Keys.None, RebuildPreviewProxy));

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(MenuItem("设置 FFmpeg…", Keys.None, ConfigureFfmpeg));
        help.DropDownItems.Add(MenuItem("打开日志目录", Keys.None, VideoLog.OpenFolder));
        help.DropDownItems.Add(MenuItem("键盘快捷键", Keys.None, ShowShortcuts));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(MenuItem("关于无损视频截取", Keys.None, ShowAbout));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, segments, export, view, help });
        VideoTheme.StyleMenuItems(menu.Items);
        return menu;
    }

    private static ToolStripMenuItem MenuItem(string text, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != Keys.None)
        {
            Keys modifiers = shortcut & Keys.Modifiers;
            Keys keyCode = shortcut & Keys.KeyCode;
            // ToolStripMenuItem.ShortcutKeys 只接受带修饰键的组合或 F1~F24；
            // 空格、Delete、方向键、单个字母等由窗体的 ProcessCmdKey 处理。
            if (modifiers != Keys.None || (keyCode >= Keys.F1 && keyCode <= Keys.F24))
            {
                item.ShortcutKeys = shortcut;
            }
            else
            {
                item.ShortcutKeyDisplayString = keyCode switch
                {
                    Keys.Space => "Space",
                    Keys.Delete => "Del",
                    Keys.Left => "←",
                    Keys.Right => "→",
                    Keys.Up => "↑",
                    Keys.Down => "↓",
                    Keys.Home => "Home",
                    Keys.End => "End",
                    _ => keyCode.ToString()
                };
            }
        }
        item.Click += (_, _) => action();
        return item;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { Dock = DockStyle.Top, Height = 34 };
        VideoTheme.StyleToolStrip(toolbar);
        toolbar.Items.Add(VideoTheme.ToolButton("打开文件", (_, _) => _ = OpenFilesDialogAsync()));
        toolbar.Items.Add(VideoTheme.ToolButton("添加文件", (_, _) => _ = OpenFilesDialogAsync()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(VideoTheme.ToolButton("撤销", (_, _) => Undo()));
        toolbar.Items.Add(VideoTheme.ToolButton("重做", (_, _) => Redo()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(VideoTheme.ToolButton("入点", (_, _) => SetInPoint()));
        toolbar.Items.Add(VideoTheme.ToolButton("出点", (_, _) => SetOutPoint()));
        toolbar.Items.Add(VideoTheme.ToolButton("分割", (_, _) => SplitAtPlayhead()));
        toolbar.Items.Add(VideoTheme.ToolButton("删除", (_, _) => DeleteSelectedSegments()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(VideoTheme.ToolButton("导出片段", (_, _) => ShowExportDialog()));
        toolbar.Items.Add(VideoTheme.ToolButton("合并文件", (_, _) => MergeCurrentFiles()));
        toolbar.Items.Add(VideoTheme.ToolButton("提取音频", (_, _) => ExtractAudio()));
        toolbar.Items.Add(VideoTheme.ToolButton("截图", (_, _) => TakeScreenshot()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(VideoTheme.ToolButton("场景检测", (_, _) => DetectScenes()));
        toolbar.Items.Add(VideoTheme.ToolButton("黑帧检测", (_, _) => DetectBlackFrames()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(VideoTheme.ToolButton("全屏", (_, _) => ToggleFullscreen()));
        return toolbar;
    }

    private Panel BuildLeftPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = VideoTheme.Panel };
        var fileToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = VideoTheme.Panel,
            Padding = new Padding(4, 3, 4, 0),
            WrapContents = false
        };
        var open = VideoTheme.Button("打开", 64, true);
        open.Click += (_, _) => _ = OpenFilesDialogAsync();
        var add = VideoTheme.Button("添加", 64);
        add.Click += (_, _) => _ = OpenFilesDialogAsync();
        var remove = VideoTheme.Button("移除", 64);
        remove.Click += (_, _) => RemoveActiveFile();
        fileToolbar.Controls.AddRange(new Control[] { open, add, remove });

        var segmentToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = VideoTheme.Panel,
            Padding = new Padding(4, 2, 4, 0),
            WrapContents = true
        };
        var setIn = VideoTheme.Button("入点", 58);
        setIn.Click += (_, _) => SetInPoint();
        var setOut = VideoTheme.Button("出点", 58);
        setOut.Click += (_, _) => SetOutPoint();
        var addSegment = VideoTheme.Button("添加片段", 78);
        addSegment.Click += (_, _) => AddSegmentFromMarkers();
        var split = VideoTheme.Button("分割", 58);
        split.Click += (_, _) => SplitAtPlayhead();
        var delete = VideoTheme.Button("删除", 58);
        delete.Click += (_, _) => DeleteSelectedSegments();
        var merge = VideoTheme.Button("合并", 58);
        merge.Click += (_, _) => MergeSelectedSegments();
        var invert = VideoTheme.Button("反转", 58);
        invert.Click += (_, _) => InvertSelection();
        segmentToolbar.Controls.AddRange(new Control[]
        {
            setIn, setOut, addSegment, split, delete, merge, invert
        });

        var markerPanel = BuildMarkerPanel();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = VideoTheme.Panel
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var fileListHost = VideoTheme.WrapWithDarkHeader(_fileList);
        var segmentListHost = VideoTheme.WrapWithDarkHeader(_segmentList);
        layout.Controls.Add(fileToolbar, 0, 0);
        layout.Controls.Add(fileListHost, 0, 1);
        layout.Controls.Add(_infoLabel, 0, 2);
        layout.Controls.Add(markerPanel, 0, 3);
        layout.Controls.Add(segmentToolbar, 0, 4);
        layout.Controls.Add(segmentListHost, 0, 5);
        panel.Controls.Add(layout);
        return panel;
    }

    /// <summary>左侧截取标记区：始终显示已设置的入点 / 出点与标记时长。</summary>
    private Panel BuildMarkerPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = VideoTheme.Panel,
            Padding = new Padding(8, 3, 8, 3)
        };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(VideoTheme.Border);
            e.Graphics.DrawLine(pen, 8, 0, panel.Width - 8, 0);
            e.Graphics.DrawLine(pen, 8, panel.Height - 1, panel.Width - 8, panel.Height - 1);
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = VideoTheme.Panel
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var inCaption = new Label
        {
            Text = "入点",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VideoTheme.Success,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiSmall
        };
        _inPointLabel = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VideoTheme.Text,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiTimeSmall,
            Cursor = Cursors.Hand
        };
        _inPointLabel.Click += (_, _) =>
        {
            if (_inPoint >= 0)
            {
                SeekTo(_inPoint, true);
            }
        };
        var outCaption = new Label
        {
            Text = "出点",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VideoTheme.Playhead,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiSmall
        };
        _outPointLabel = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VideoTheme.Text,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiTimeSmall,
            Cursor = Cursors.Hand
        };
        _outPointLabel.Click += (_, _) =>
        {
            if (_outPoint >= 0)
            {
                SeekTo(_outPoint, true);
            }
        };
        _markerDurationLabel = new Label
        {
            Text = "未设置截取标记",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VideoTheme.Muted,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiSmall,
            Padding = new Padding(6, 0, 0, 0),
            AutoEllipsis = true
        };
        table.Controls.Add(inCaption, 0, 0);
        table.Controls.Add(_inPointLabel, 1, 0);
        table.Controls.Add(outCaption, 0, 1);
        table.Controls.Add(_outPointLabel, 1, 1);
        table.Controls.Add(_markerDurationLabel, 2, 0);
        table.SetRowSpan(_markerDurationLabel, 2);
        panel.Controls.Add(table);
        return panel;
    }

    private void UpdateMarkers()
    {
        if (_inPointLabel == null || _outPointLabel == null)
        {
            return;
        }
        _inPointLabel.Text = _inPoint >= 0 ? VideoTheme.Timecode(_inPoint) : "—";
        _outPointLabel.Text = _outPoint >= 0 ? VideoTheme.Timecode(_outPoint) : "—";
        if (_inPoint >= 0 && _outPoint >= 0 && _outPoint > _inPoint + 0.001)
        {
            _markerDurationLabel.Text = "时长 " + VideoTheme.Timecode(_outPoint - _inPoint);
            _markerDurationLabel.ForeColor = VideoTheme.Text;
        }
        else if (_inPoint >= 0 || _outPoint >= 0)
        {
            _markerDurationLabel.Text = _inPoint >= 0 ? "待设出点" : "待设入点";
            _markerDurationLabel.ForeColor = VideoTheme.Muted;
        }
        else
        {
            _markerDurationLabel.Text = "未设置标记（I / O）";
            _markerDurationLabel.ForeColor = VideoTheme.Muted;
        }
    }

    private Panel BuildRightPanel(out Panel previewControls, out Panel timelineHost,
        out SplitContainer rightSplit, out VideoPreviewControl preview,
        out LosslessCutTimeline timeline, out VideoSeekBar seek, out Button playButton,
        out Label timeLabel, out Label durationLabel, out TrackBar volume,
        out ComboBox speed, out HScrollBar scroll)
    {
        preview = new VideoPreviewControl { Dock = DockStyle.Fill };
        preview.MediaError += (_, message) =>
        {
            if (_statusInfo != null)
            {
                _statusInfo.Text = "预览失败：" + message;
            }
            VideoLog.Warn("预览失败：" + message + " · 当前源=" + (_previewLoadedPath ?? "—"));
            var file = _activeFile;
            if (file != null && _previewLoadedPath != null
                && _previewLoadedPath.Contains("VideoProxy", StringComparison.OrdinalIgnoreCase))
            {
                _proxyFailed.Add(file.Path);
                if (_statusInfo != null)
                {
                    _statusInfo.Text = "预览代理也无法播放，请把日志发我进一步定位：" + message;
                }
                return;
            }
            if (file != null)
            {
                VideoLog.Info("预览代理状态：" + file.Path
                    + " · cached=" + _previewProxies.ContainsKey(file.Path)
                    + " · building=" + _proxyBuilding.Contains(file.Path)
                    + " · failed=" + _proxyFailed.Contains(file.Path));
                if (!_previewProxies.ContainsKey(file.Path)
                    && !_proxyBuilding.Contains(file.Path)
                    && !_proxyFailed.Contains(file.Path))
                {
                    _ = BuildPreviewProxyAsync(file);
                }
            }
        };
        previewControls = BuildPreviewControls(out seek, out playButton, out timeLabel,
            out durationLabel, out volume, out speed);

        var timelineControl = new LosslessCutTimeline { Dock = DockStyle.Fill };
        var scrollBar = new HScrollBar
        {
            Dock = DockStyle.Bottom,
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            SmallChange = 10,
            LargeChange = 100
        };
        scrollBar.ValueChanged += (_, _) =>
        {
            if (!_suppressSegmentSelection)
            {
                timelineControl.ScrollSeconds = scrollBar.Value / 10.0;
            }
        };
        timelineControl.ScrollChanged += (_, _) => UpdateScrollBar();

        var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        previewPanel.Controls.Add(preview);
        previewPanel.Controls.Add(previewControls);

        timelineHost = new Panel { Dock = DockStyle.Fill, BackColor = VideoTheme.Window };
        var timelineTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "  时间线 · 关键帧刻度 · 片段",
            ForeColor = VideoTheme.Muted,
            BackColor = VideoTheme.Panel,
            Font = VideoTheme.UiSmall,
            TextAlign = ContentAlignment.MiddleLeft
        };
        timelineHost.Controls.Add(timelineControl);
        timelineHost.Controls.Add(scrollBar);
        timelineHost.Controls.Add(timelineTitle);

        rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 5,
            BackColor = VideoTheme.Border
        };
        rightSplit.Panel1.Controls.Add(previewPanel);
        rightSplit.Panel2.Controls.Add(timelineHost);
        timeline = timelineControl;
        scroll = scrollBar;
        return previewPanel;
    }

    private Panel BuildPreviewControls(out VideoSeekBar seek, out Button playButton,
        out Label timeLabel, out Label durationLabel, out TrackBar volume,
        out ComboBox speed)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            BackColor = VideoTheme.Panel,
            Padding = new Padding(6, 4, 6, 4)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1,
            BackColor = VideoTheme.Panel
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

        var play = VideoTheme.Button("播放", 60, true);
        play.Click += (_, _) => TogglePlay();
        var stop = VideoTheme.Button("停止", 48);
        stop.Click += (_, _) => StopPlayback();
        var previous = VideoTheme.Button("上帧", 50);
        previous.Click += (_, _) => StepFrame(-1);
        var next = VideoTheme.Button("下帧", 50);
        next.Click += (_, _) => StepFrame(1);
        var timeText = new Label
        {
            Text = "00:00.000",
            ForeColor = VideoTheme.Text,
            Font = VideoTheme.UiTime,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };
        var seekBar = new VideoSeekBar { Dock = DockStyle.Fill, Margin = new Padding(4, 9, 4, 9) };
        seekBar.SeekRequested += (_, time) => SeekTo(time, true);
        var durationText = new Label
        {
            Text = "00:00.000",
            ForeColor = VideoTheme.Muted,
            Font = VideoTheme.UiTimeSmall,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };
        var volumeBar = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 80,
            TickStyle = TickStyle.None,
            Dock = DockStyle.Fill,
            BackColor = VideoTheme.Panel
        };
        volumeBar.ValueChanged += (_, _) => _preview.ApplyVolume(volumeBar.Value / 100.0);
        var speedCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 7, 4, 7)
        };
        VideoTheme.StyleCombo(speedCombo);
        speedCombo.Items.AddRange(new object[] { "0.25x", "0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x", "3x", "4x" });
        speedCombo.SelectedIndex = 3;
        speedCombo.SelectedIndexChanged += (_, _) =>
        {
            _playbackRate = speedCombo.SelectedIndex switch
            {
                0 => 0.25,
                1 => 0.5,
                2 => 0.75,
                3 => 1,
                4 => 1.25,
                5 => 1.5,
                6 => 2,
                7 => 3,
                8 => 4,
                _ => 1
            };
            _preview.SetSpeed(_playbackRate);
        };
        var fullscreen = VideoTheme.Button("全屏", 58);
        fullscreen.Click += (_, _) => ToggleFullscreen();

        layout.Controls.Add(play, 0, 0);
        layout.Controls.Add(stop, 1, 0);
        layout.Controls.Add(previous, 2, 0);
        layout.Controls.Add(next, 3, 0);
        layout.Controls.Add(timeText, 4, 0);
        layout.Controls.Add(seekBar, 5, 0);
        layout.Controls.Add(durationText, 6, 0);
        layout.Controls.Add(volumeBar, 7, 0);
        layout.Controls.Add(speedCombo, 8, 0);
        panel.Controls.Add(layout);
        seek = seekBar;
        playButton = play;
        timeLabel = timeText;
        durationLabel = durationText;
        volume = volumeBar;
        speed = speedCombo;
        _ = fullscreen;
        return panel;
    }

    // ================= 文件 =================

    private async Task OpenFilesDialogAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开视频 / 音频文件",
            Filter = "媒体文件|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.flv;*.m4v;*.ts;*.m2ts;"
                + "*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.opus;*.wma;*.aiff|"
                + "视频|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.flv;*.m4v;*.ts;*.m2ts|"
                + "音频|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.opus;*.wma;*.aiff|所有文件|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await AddFilesAsync(dialog.FileNames);
        }
    }

    private async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (_busy)
        {
            return;
        }
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0)
        {
            return;
        }
        _busy = true;
        bool hadActive = _activeFile != null;
        try
        {
            foreach (string path in files)
            {
                var existing = _files.FirstOrDefault(file => string.Equals(file.Path, path,
                    StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    SetActiveFile(existing);
                    continue;
                }
                _statusInfo.Text = "正在读取：" + Path.GetFileName(path);
                VideoMediaInfo info;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    info = await VideoToolkit.ProbeAsync(path, _config.FfmpegPath, cts.Token);
                }
                catch (Exception ex)
                {
                    VideoLog.Error("读取媒体失败：" + path, ex);
                    info = new VideoMediaInfo { Path = path };
                }
                if (IsDisposed)
                {
                    return;
                }
                if (info.Duration <= 0.02 && !VideoToolkit.IsImageFile(path))
                {
                    _statusInfo.Text = "无法读取时长：" + Path.GetFileName(path);
                }
                var file = new LosslessFile
                {
                    Path = path,
                    Info = info
                };
                file.Segments.Add(new LosslessSegment
                {
                    Start = 0,
                    End = Math.Max(0.05, info.Duration),
                    Name = "完整文件"
                });
                _files.Add(file);
                RefreshFileList();
                if (!hadActive && ReferenceEquals(_files[0], file))
                {
                    SetActiveFile(file);
                    hadActive = true;
                }
            }
            _statusInfo.Text = "已添加 " + files.Length + " 个文件";
            UpdateCount();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task LoadFileDetailsAsync(LosslessFile file)
    {
        if (file.KeyframesLoaded || file.LoadingDetails)
        {
            return;
        }
        file.LoadingDetails = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var keyframes = await VideoToolkit.GetKeyframesAsync(file.Path, _config.FfmpegPath, cts.Token);
            if (IsDisposed)
            {
                return;
            }
            file.Keyframes = keyframes;
            file.KeyframesLoaded = true;
            if (ReferenceEquals(file, _activeFile))
            {
                _timeline.RefreshTimeline();
                UpdateInfo();
                _statusInfo.Text = "关键帧：" + keyframes.Count + " 个";
            }
            if (file.Info.HasAudio && file.Info.Duration > 0)
            {
                var waveform = await VideoToolkit.GetWaveformAsync(file.Path, file.Info.Duration,
                    2400, _config.FfmpegPath, cts.Token);
                if (IsDisposed)
                {
                    return;
                }
                file.Waveform = waveform;
                file.WaveformLoaded = true;
                if (ReferenceEquals(file, _activeFile))
                {
                    _timeline.RefreshTimeline();
                }
            }
        }
        catch (OperationCanceledException)
        {
            VideoLog.Warn("读取关键帧 / 波形超时：" + file.Path);
        }
        catch (Exception ex)
        {
            VideoLog.Error("读取关键帧 / 波形失败：" + file.Path, ex);
        }
        finally
        {
            file.LoadingDetails = false;
        }
    }

    private void RefreshFileList()
    {
        _suppressFileSelection = true;
        try
        {
            _fileList.BeginUpdate();
            _fileList.Items.Clear();
            foreach (var file in _files)
            {
                var item = new ListViewItem(file.Name)
                {
                    Tag = file,
                    Checked = file.Enabled,
                    ToolTipText = file.Path
                };
                item.SubItems.Add(VideoTheme.Timecode(file.Info.Duration, false));
                item.SubItems.Add(FormatSize(file.Info.Size));
                _fileList.Items.Add(item);
                if (ReferenceEquals(file, _activeFile))
                {
                    item.Selected = true;
                    item.Focused = true;
                }
            }
            _fileList.EndUpdate();
        }
        finally
        {
            _suppressFileSelection = false;
        }
        UpdateCount();
    }

    private void SetActiveFile(LosslessFile file)
    {
        if (ReferenceEquals(_activeFile, file))
        {
            return;
        }
        Pause();
        _activeFile = file;
        _previewLoadedPath = null;
        _selectedSegment = file.Segments.FirstOrDefault();
        _timeline.File = file;
        if (!file.KeyframesLoaded && !file.LoadingDetails)
        {
            _ = LoadFileDetailsAsync(file);
        }
        _timeline.SelectedSegment = _selectedSegment;
        _playhead = 0;
        _inPoint = -1;
        _outPoint = -1;
        UpdateMarkers();
        _undo.Clear();
        _redo.Clear();
        RefreshSegmentList();
        UpdateInfo();
        SeekTo(0, false);
        UpdateScrollBar();
        _suppressFileSelection = true;
        try
        {
            foreach (ListViewItem item in _fileList.Items)
            {
                item.Selected = ReferenceEquals(item.Tag, file);
            }
        }
        finally
        {
            _suppressFileSelection = false;
        }
    }

    private void RemoveActiveFile()
    {
        if (_activeFile == null)
        {
            return;
        }
        var file = _activeFile;
        _files.Remove(file);
        _activeFile = null;
        RefreshFileList();
        if (_files.Count > 0)
        {
            SetActiveFile(_files[0]);
        }
        else
        {
            _timeline.File = null;
            _preview.Clear("打开视频或音频文件开始");
            _inPoint = -1;
            _outPoint = -1;
            UpdateMarkers();
            RefreshSegmentList();
            UpdateInfo();
        }
        _statusInfo.Text = "已移除：" + file.Name;
    }

    private void UpdateInfo()
    {
        if (_activeFile == null)
        {
            _infoLabel.Text = "打开视频或音频文件开始\n\n"
                + "支持无损流复制切割、片段合并、文件拼接、提取音频、截图、场景与黑帧检测。";
            return;
        }
        var info = _activeFile.Info;
        _infoLabel.Text = _activeFile.Name + "\n"
            + "时长 " + VideoTheme.Timecode(info.Duration) + " · " + FormatSize(info.Size) + "\n"
            + info.AspectLabel + " · " + info.CodecLabel + "\n"
            + "容器 " + (string.IsNullOrWhiteSpace(info.FormatName) ? "—" : info.FormatName)
            + " · 关键帧 " + (_activeFile.KeyframesLoaded ? _activeFile.Keyframes.Count.ToString(CultureInfo.InvariantCulture) : "读取中…")
            + " · 片段 " + _activeFile.Segments.Count;
    }

    private void UpdateCount()
    {
        if (_countLabel == null)
        {
            return;
        }
        int segments = _files.Sum(file => file.Segments.Count);
        _countLabel.Text = _files.Count + " 个文件 · " + segments + " 个片段";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
    }

    // ================= 片段 =================

    private void RefreshSegmentList()
    {
        _suppressSegmentSelection = true;
        try
        {
            _segmentList.BeginUpdate();
            _segmentList.Items.Clear();
            if (_activeFile != null)
            {
                _activeFile.Segments.Sort((left, right) => left.Start.CompareTo(right.Start));
                for (int i = 0; i < _activeFile.Segments.Count; i++)
                {
                    var segment = _activeFile.Segments[i];
                    var item = new ListViewItem("#" + (i + 1))
                    {
                        Tag = segment,
                        ToolTipText = segment.Name,
                        ForeColor = segment.Enabled ? VideoTheme.Text : VideoTheme.Muted
                    };
                    item.SubItems.Add(VideoTheme.Timecode(segment.Start));
                    item.SubItems.Add(VideoTheme.Timecode(segment.End));
                    item.SubItems.Add(VideoTheme.Timecode(segment.Duration));
                    item.SubItems.Add(string.IsNullOrWhiteSpace(segment.Name)
                        ? (segment.Enabled ? "" : "不导出") : segment.Name);
                    if (ReferenceEquals(segment, _selectedSegment))
                    {
                        item.Selected = true;
                        item.Focused = true;
                    }
                    _segmentList.Items.Add(item);
                }
            }
            _segmentList.EndUpdate();
        }
        finally
        {
            _suppressSegmentSelection = false;
        }
        UpdateInfo();
        UpdateCount();
        _timeline.RefreshTimeline();
    }

    private void SyncSegmentListSelection()
    {
        _suppressSegmentSelection = true;
        try
        {
            foreach (ListViewItem item in _segmentList.Items)
            {
                item.Selected = ReferenceEquals(item.Tag, _selectedSegment);
            }
        }
        finally
        {
            _suppressSegmentSelection = false;
        }
    }

    private IEnumerable<LosslessSegment> SelectedSegments()
    {
        if (_segmentList.SelectedItems.Count == 0)
        {
            return _selectedSegment != null
                ? new[] { _selectedSegment }
                : Array.Empty<LosslessSegment>();
        }
        return _segmentList.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as LosslessSegment)
            .Where(segment => segment != null)
            .Cast<LosslessSegment>()
            .ToArray();
    }

    private void SetInPoint()
    {
        _inPoint = _playhead;
        UpdateMarkers();
        _statusInfo.Text = "入点：" + VideoTheme.Timecode(_inPoint);
    }

    private void SetOutPoint()
    {
        _outPoint = _playhead;
        UpdateMarkers();
        _statusInfo.Text = "出点：" + VideoTheme.Timecode(_outPoint);
    }

    private void AddSegmentFromMarkers()
    {
        if (_activeFile == null)
        {
            return;
        }
        double start = _inPoint >= 0 ? _inPoint : _playhead;
        double end = _outPoint >= 0 ? _outPoint : _activeFile.Duration;
        if (end <= start + 0.02)
        {
            _statusInfo.Text = "出点必须大于入点";
            return;
        }
        PushUndo();
        var segment = new LosslessSegment
        {
            Start = Math.Max(0, start),
            End = Math.Min(_activeFile.Duration, end),
            Name = "片段 " + (_activeFile.Segments.Count + 1)
        };
        _activeFile.Segments.Add(segment);
        _selectedSegment = segment;
        RefreshSegmentList();
        _timeline.SelectedSegment = segment;
        _inPoint = -1;
        _outPoint = -1;
        UpdateMarkers();
        _statusInfo.Text = "已添加片段 " + VideoTheme.Timecode(segment.Start)
            + " → " + VideoTheme.Timecode(segment.End);
    }

    private void SplitAtPlayhead()
    {
        if (_activeFile == null)
        {
            return;
        }
        var segment = _activeFile.SegmentAt(_playhead)
            ?? _selectedSegment;
        if (segment == null || _playhead <= segment.Start + 0.02 || _playhead >= segment.End - 0.02)
        {
            _statusInfo.Text = "播放头不在可分割的片段内";
            return;
        }
        PushUndo();
        double originalEnd = segment.End;
        segment.End = _playhead;
        var right = new LosslessSegment
        {
            Start = _playhead,
            End = originalEnd,
            Name = segment.Name
        };
        _activeFile.Segments.Add(right);
        _selectedSegment = right;
        RefreshSegmentList();
        _timeline.SelectedSegment = right;
        _statusInfo.Text = "已在 " + VideoTheme.Timecode(_playhead) + " 分割";
    }

    private void DeleteSelectedSegments()
    {
        if (_activeFile == null)
        {
            return;
        }
        var selected = SelectedSegments().ToList();
        if (selected.Count == 0)
        {
            return;
        }
        PushUndo();
        foreach (var segment in selected)
        {
            _activeFile.Segments.Remove(segment);
        }
        _selectedSegment = _activeFile.Segments.FirstOrDefault();
        RefreshSegmentList();
        _timeline.SelectedSegment = _selectedSegment;
        _statusInfo.Text = "已删除 " + selected.Count + " 个片段";
    }

    private void MergeSelectedSegments()
    {
        if (_activeFile == null)
        {
            return;
        }
        var selected = SelectedSegments().OrderBy(segment => segment.Start).ToList();
        if (selected.Count < 2)
        {
            _statusInfo.Text = "请选择至少两个片段再合并";
            return;
        }
        PushUndo();
        double start = selected.Min(segment => segment.Start);
        double end = selected.Max(segment => segment.End);
        foreach (var segment in selected)
        {
            _activeFile.Segments.Remove(segment);
        }
        var merged = new LosslessSegment
        {
            Start = start,
            End = end,
            Name = "合并片段"
        };
        _activeFile.Segments.Add(merged);
        _selectedSegment = merged;
        RefreshSegmentList();
        _timeline.SelectedSegment = merged;
        _statusInfo.Text = "已合并 " + selected.Count + " 个片段";
    }

    private void ToggleSelectedEnabled()
    {
        var selected = SelectedSegments().ToList();
        if (selected.Count == 0)
        {
            return;
        }
        PushUndo();
        foreach (var segment in selected)
        {
            segment.Enabled = !segment.Enabled;
        }
        RefreshSegmentList();
    }

    private void InvertSelection()
    {
        if (_activeFile == null || _activeFile.Segments.Count == 0)
        {
            return;
        }
        PushUndo();
        foreach (var segment in _activeFile.Segments)
        {
            segment.Enabled = !segment.Enabled;
        }
        RefreshSegmentList();
        _statusInfo.Text = "已反转片段启用状态";
    }

    private void SelectAllSegments()
    {
        _suppressSegmentSelection = true;
        try
        {
            foreach (ListViewItem item in _segmentList.Items)
            {
                item.Selected = true;
            }
        }
        finally
        {
            _suppressSegmentSelection = false;
        }
    }

    private void PushUndo()
    {
        if (_activeFile == null)
        {
            return;
        }
        try
        {
            _undo.Push(JsonSerializer.Serialize(_activeFile.Segments, JsonOptions));
            while (_undo.Count > 60)
            {
                var items = _undo.ToArray();
                _undo.Clear();
                foreach (string item in items.Take(60).Reverse())
                {
                    _undo.Push(item);
                }
                break;
            }
            _redo.Clear();
        }
        catch (Exception ex)
        {
            VideoLog.Warn("片段撤销快照失败：" + ex.Message);
        }
    }

    private void Undo()
    {
        if (_activeFile == null || _undo.Count == 0)
        {
            _statusInfo.Text = "没有可撤销的片段操作";
            return;
        }
        try
        {
            _redo.Push(JsonSerializer.Serialize(_activeFile.Segments, JsonOptions));
            RestoreSegments(_undo.Pop());
            _statusInfo.Text = "已撤销";
        }
        catch (Exception ex)
        {
            VideoLog.Error("撤销失败", ex);
        }
    }

    private void Redo()
    {
        if (_activeFile == null || _redo.Count == 0)
        {
            _statusInfo.Text = "没有可重做的片段操作";
            return;
        }
        try
        {
            _undo.Push(JsonSerializer.Serialize(_activeFile.Segments, JsonOptions));
            RestoreSegments(_redo.Pop());
            _statusInfo.Text = "已重做";
        }
        catch (Exception ex)
        {
            VideoLog.Error("重做失败", ex);
        }
    }

    private void RestoreSegments(string json)
    {
        if (_activeFile == null)
        {
            return;
        }
        var segments = JsonSerializer.Deserialize<List<LosslessSegment>>(json, JsonOptions);
        if (segments == null)
        {
            return;
        }
        _activeFile.Segments = segments;
        _selectedSegment = _activeFile.Segments.FirstOrDefault();
        RefreshSegmentList();
        _timeline.SelectedSegment = _selectedSegment;
    }

    private void TimelineSegmentContextRequested(object? sender,
        (LosslessSegment Segment, Point Screen) args)
    {
        _selectedSegment = args.Segment;
        _timeline.SelectedSegment = args.Segment;
        var menu = new ContextMenuStrip { Renderer = VideoTheme.CreateRenderer() };
        menu.Items.Add("从入点播放", null, (_, _) => SeekTo(args.Segment.Start, true));
        menu.Items.Add("设入点为片段开始", null, (_, _) =>
        {
            _inPoint = args.Segment.Start;
            UpdateMarkers();
            _statusInfo.Text = "入点：" + VideoTheme.Timecode(_inPoint);
        });
        menu.Items.Add("设出点为片段结束", null, (_, _) =>
        {
            _outPoint = args.Segment.End;
            UpdateMarkers();
            _statusInfo.Text = "出点：" + VideoTheme.Timecode(_outPoint);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("在播放头分割", null, (_, _) => SplitAtPlayhead());
        menu.Items.Add("合并选中片段", null, (_, _) => MergeSelectedSegments());
        menu.Items.Add(args.Segment.Enabled ? "设为不导出" : "设为导出", null, (_, _) =>
        {
            PushUndo();
            args.Segment.Enabled = !args.Segment.Enabled;
            RefreshSegmentList();
        });
        menu.Items.Add("删除片段", null, (_, _) => DeleteSelectedSegments());
        VideoTheme.StyleContextMenu(menu);
        menu.Show(args.Screen);
    }

    // ================= 预览与播放 =================

    private void TogglePlay()
    {
        if (_playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void Play()
    {
        if (_activeFile == null || _activeFile.Duration <= 0.05)
        {
            return;
        }
        if (_playhead >= _activeFile.Duration - 0.03)
        {
            SeekTo(0, false);
        }
        _playing = true;
        _playAnchor = _playhead;
        _playClock.Restart();
        _playTimer.Start();
        _playButton.Text = "暂停";
        if (!string.Equals(_previewLoadedPath, PreviewPathFor(_activeFile),
            StringComparison.OrdinalIgnoreCase))
        {
            LoadPreview();
        }
        else
        {
            _preview.SetSpeed(_playbackRate);
            _preview.Play();
        }
    }

    private void Pause()
    {
        _playing = false;
        _playTimer.Stop();
        _playClock.Stop();
        _playButton.Text = "播放";
        _preview.Pause();
    }

    private void StopPlayback()
    {
        Pause();
        SeekTo(0, true);
    }

    private void StepFrame(int direction)
    {
        Pause();
        double fps = _activeFile?.Info.Fps ?? 30;
        SeekTo(_playhead + direction / Math.Max(1, fps), true);
    }

    private void SeekTo(double time, bool fromUser)
    {
        if (_activeFile == null)
        {
            return;
        }
        double duration = Math.Max(0.01, _activeFile.Duration);
        _playhead = Math.Clamp(time, 0, duration);
        _playAnchor = _playhead;
        if (_playing)
        {
            _playClock.Restart();
        }
        _timeline.SetPlayhead(_playhead, false);
        _seek.Position = _playhead;
        _timeLabel.Text = VideoTheme.Timecode(_playhead);
        _durationLabel.Text = VideoTheme.Timecode(_activeFile.Duration);
        _seek.Duration = Math.Max(0.01, _activeFile.Duration);
        if (!string.Equals(_previewLoadedPath, PreviewPathFor(_activeFile),
            StringComparison.OrdinalIgnoreCase))
        {
            LoadPreview();
        }
        else
        {
            _preview.Seek(_playhead);
        }
        if (fromUser)
        {
            _timeline.EnsureVisible(_playhead);
        }
    }

    private void OnPlayTick()
    {
        if (!_playing || _activeFile == null)
        {
            return;
        }
        double duration = _activeFile.Duration;
        double target = _playAnchor + _playClock.Elapsed.TotalSeconds * _playbackRate;
        if (target >= duration)
        {
            _playhead = duration;
            _timeline.SetPlayhead(duration, false);
            _seek.Position = duration;
            _timeLabel.Text = VideoTheme.Timecode(duration);
            Pause();
            return;
        }
        _playhead = target;
        _timeline.SetPlayhead(target, false);
        _timeline.EnsureVisible(target);
        _seek.Position = target;
        _timeLabel.Text = VideoTheme.Timecode(target);
        if (Math.Abs(_preview.Position - target) > 0.45)
        {
            _preview.Seek(target);
        }
    }

    private void LoadPreview()
    {
        if (_activeFile == null)
        {
            _preview.Clear("打开视频或音频文件开始");
            return;
        }
        double duration = _activeFile.Info.Duration > 0 ? _activeFile.Info.Duration : 1;
        string previewPath = PreviewPathFor(_activeFile);
        bool usingProxy = !string.Equals(previewPath, _activeFile.Path,
            StringComparison.OrdinalIgnoreCase);
        if (!usingProxy && NeedsPreviewProxy(_activeFile)
            && !_proxyFailed.Contains(_activeFile.Path)
            && _progressDialog is not { IsDisposed: false }
            && VideoToolkit.Ffmpeg(_config.FfmpegPath) != null)
        {
            _previewLoadedPath = null;
            if (!_proxyBuilding.Contains(_activeFile.Path))
            {
                _preview.Clear("正在生成预览代理…");
                _ = BuildPreviewProxyAsync(_activeFile);
            }
            return;
        }
        bool isAudio = !_activeFile.Info.HasVideo && _activeFile.Info.HasAudio;
        _previewLoadedPath = previewPath;
        _ = _preview.LoadAsync(previewPath, false, duration, _playhead, _playing, isAudio);
        _preview.SetSpeed(_playbackRate);
        _preview.ApplyVolume(_volume.Value / 100.0);
    }

    private void RebuildPreviewProxy()
    {
        if (_activeFile == null)
        {
            return;
        }
        _proxyFailed.Remove(_activeFile.Path);
        _previewProxies.Remove(_activeFile.Path);
        _previewLoadedPath = null;
        _preview.Clear("正在重新生成预览代理…");
        LoadPreview();
    }

    private static bool NeedsPreviewProxy(LosslessFile file)
    {
        string extension = Path.GetExtension(file.Path).ToLowerInvariant();
        string video = file.Info.VideoCodec.ToLowerInvariant();
        string audio = file.Info.AudioCodec.ToLowerInvariant();
        if (file.Info.HasVideo)
        {
            bool container = extension is ".mp4" or ".m4v" or ".webm" or ".ogv";
            bool codec = video is "h264" or "vp8" or "vp9" or "av1";
            string pixelFormat = file.Info.PixelFormat;
            bool pixelOk = string.IsNullOrWhiteSpace(pixelFormat)
                || pixelFormat.Equals("yuv420p", StringComparison.OrdinalIgnoreCase)
                || pixelFormat.Equals("yuvj420p", StringComparison.OrdinalIgnoreCase)
                || pixelFormat.Equals("nv12", StringComparison.OrdinalIgnoreCase);
            if (!container || !codec || !pixelOk)
            {
                return true;
            }
        }
        if (file.Info.HasAudio)
        {
            bool audioSupported = audio is "aac" or "mp3" or "opus" or "vorbis"
                or "flac" or "pcm_s16le" or "pcm_s24le" or "pcm_f32le";
            bool audioContainer = extension is ".mp3" or ".m4a" or ".aac" or ".ogg"
                or ".oga" or ".opus" or ".flac" or ".wav" or ".weba";
            if (!audioSupported || !audioContainer)
            {
                return true;
            }
        }
        return false;
    }

    private string PreviewPathFor(LosslessFile file)
    {
        return _previewProxies.TryGetValue(file.Path, out string? proxy) && File.Exists(proxy)
            ? proxy : file.Path;
    }

    /// <summary>
    /// 浏览器不支持的编码（MKV / HEVC / WMV / AV1 等）自动生成 H.264 预览代理，
    /// 缓存到 %TEMP%\MiniMonitor\VideoProxy，原文件不修改。
    /// </summary>
    private async Task BuildPreviewProxyAsync(LosslessFile file)
    {
        if (_proxyBuilding.Contains(file.Path) || _proxyFailed.Contains(file.Path)
            || _progressDialog is { IsDisposed: false })
        {
            return;
        }
        _proxyBuilding.Add(file.Path);
        bool wasPlaying = _playing;
        Pause();
        VideoProgressDialog? progress = null;
        bool success = false;
        try
        {
            string? ffmpeg = VideoToolkit.Ffmpeg(_config.FfmpegPath);
            if (ffmpeg == null)
            {
                VideoLog.Warn("无法生成预览代理：未找到 ffmpeg.exe · " + file.Path);
                if (!IsDisposed)
                {
                    _statusInfo.Text = "当前编码无法直接预览，且未找到 ffmpeg.exe，无法生成预览代理。";
                }
                return;
            }
            VideoLog.Info("生成预览代理：" + file.Path
                + " · HasVideo=" + file.Info.HasVideo
                + " · HasAudio=" + file.Info.HasAudio
                + " · Duration=" + file.Info.Duration.ToString("F3",
                    System.Globalization.CultureInfo.InvariantCulture));
            string folder = Path.Combine(AppPaths.Temp, "VideoProxy");
            Directory.CreateDirectory(folder);
            string key = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(file.Path + "|"
                        + new FileInfo(file.Path).Length + "|"
                        + File.GetLastWriteTimeUtc(file.Path).Ticks)))[..16];
            string output = Path.Combine(folder, key + ".mp4");
            if (!File.Exists(output))
            {
                if (!IsDisposed)
                {
                    _statusInfo.Text = "正在生成预览代理…";
                }
                progress = new VideoProgressDialog("正在生成预览代理",
                    file.Name + "\n当前编码无法在浏览器中直接播放，正在转换为 H.264 预览副本。");
                _progressDialog = progress;
                progress.Show(this);
                if (!IsDisposed)
                {
                    Enabled = false;
                }
                bool eightBit420 = string.IsNullOrWhiteSpace(file.Info.PixelFormat)
                    || file.Info.PixelFormat.Equals("yuv420p", StringComparison.OrdinalIgnoreCase)
                    || file.Info.PixelFormat.Equals("yuvj420p", StringComparison.OrdinalIgnoreCase)
                    || file.Info.PixelFormat.Equals("nv12", StringComparison.OrdinalIgnoreCase);
                bool canRemux = file.Info.HasVideo && eightBit420
                    && file.Info.VideoCodec.Equals("h264", StringComparison.OrdinalIgnoreCase);
                var args = BuildProxyArguments(file, output, forceTranscode: false);
                VideoLog.Info("预览代理命令：ffmpeg " + string.Join(" ", args));
                var result = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath, args,
                    file.Info.Duration, (fraction, status) =>
                    {
                        if (!progress.IsDisposed)
                        {
                            progress.UpdateProgress(fraction, status);
                        }
                    }, progress.Token);
                long outputSize = File.Exists(output) ? new FileInfo(output).Length : 0;
                if ((result.ExitCode != 0 || outputSize < 1024) && canRemux)
                {
                    // H.264 直接复制失败时（例如 10-bit / 异常容器），回退到完整转码。
                    try
                    {
                        File.Delete(output);
                    }
                    catch
                    {
                        // ignore
                    }
                    VideoLog.Warn("预览代理快速封装失败，改为完整转码：" + file.Path);
                    args = BuildProxyArguments(file, output, forceTranscode: true);
                    VideoLog.Info("预览代理命令：ffmpeg " + string.Join(" ", args));
                    result = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath, args,
                        file.Info.Duration, (fraction, status) =>
                        {
                            if (!progress.IsDisposed)
                            {
                                progress.UpdateProgress(fraction, status);
                            }
                        }, progress.Token);
                    outputSize = File.Exists(output) ? new FileInfo(output).Length : 0;
                }
                if (result.ExitCode != 0 || outputSize < 1024)
                {
                    _proxyFailed.Add(file.Path);
                    VideoLog.Error("预览代理生成失败：exit=" + result.ExitCode
                        + " size=" + outputSize + " · " + file.Path
                        + "\n" + Tail(result.StdErr, 1200));
                    if (!IsDisposed)
                    {
                        _statusInfo.Text = "预览代理生成失败（exit " + result.ExitCode
                            + "，输出 " + outputSize + " 字节），请查看日志。";
                    }
                    return;
                }
            }
            _previewProxies[file.Path] = output;
            success = true;
            if (!IsDisposed && ReferenceEquals(_activeFile, file))
            {
                _previewLoadedPath = null;
                LoadPreview();
                if (wasPlaying)
                {
                    Play();
                }
                else
                {
                    SeekTo(_playhead, false);
                }
            }
            if (!IsDisposed)
            {
                _statusInfo.Text = "已切换为预览代理（原文件不会被修改）";
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                _statusInfo.Text = "预览代理生成已取消";
            }
        }
        catch (Exception ex)
        {
            _proxyFailed.Add(file.Path);
            VideoLog.Error("预览代理异常：" + file.Path, ex);
            if (!IsDisposed)
            {
                _statusInfo.Text = "预览代理生成失败：" + ex.Message;
            }
        }
        finally
        {
            _proxyBuilding.Remove(file.Path);
            if (progress != null)
            {
                if (!progress.IsDisposed)
                {
                    progress.Finish(success, success ? "预览代理已生成" : "预览代理未生成");
                    progress.Close();
                }
                _progressDialog = null;
            }
            if (!IsDisposed)
            {
                Enabled = true;
            }
        }
    }

    private static List<string> BuildProxyArguments(LosslessFile file, string output,
        bool forceTranscode)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-i", file.Path
        };
        if (!file.Info.HasVideo && !file.Info.HasAudio)
        {
            // 探测信息缺失时也尝试按实际流生成，避免生成空文件。
            args.AddRange(new[]
            {
                "-map", "0:v:0?", "-map", "0:a:0?",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28",
                "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k",
                "-sn", "-dn", "-movflags", "+faststart", output
            });
            return args;
        }
        bool eightBit420 = string.IsNullOrWhiteSpace(file.Info.PixelFormat)
            || file.Info.PixelFormat.Equals("yuv420p", StringComparison.OrdinalIgnoreCase)
            || file.Info.PixelFormat.Equals("yuvj420p", StringComparison.OrdinalIgnoreCase)
            || file.Info.PixelFormat.Equals("nv12", StringComparison.OrdinalIgnoreCase);
        bool copyVideo = !forceTranscode && file.Info.HasVideo && eightBit420
            && file.Info.VideoCodec.Equals("h264", StringComparison.OrdinalIgnoreCase);
        if (copyVideo)
        {
            // MKV/AVI 中的 H.264 通常只需换容器，几秒钟即可完成。
            args.AddRange(new[] { "-map", "0:v:0", "-c:v", "copy" });
            if (file.Info.HasAudio)
            {
                bool copyAudio = file.Info.AudioCodec.Equals("aac", StringComparison.OrdinalIgnoreCase)
                    || file.Info.AudioCodec.Equals("mp3", StringComparison.OrdinalIgnoreCase);
                args.AddRange(new[] { "-map", "0:a:0" });
                if (copyAudio)
                {
                    args.AddRange(new[] { "-c:a", "copy" });
                }
                else
                {
                    args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k" });
                }
            }
            else
            {
                args.Add("-an");
            }
            args.AddRange(new[] { "-sn", "-dn", "-movflags", "+faststart", output });
            return args;
        }
        if (file.Info.HasVideo)
        {
            args.AddRange(new[]
            {
                "-map", "0:v:0", "-vf",
                "scale=960:-2:force_original_aspect_ratio=decrease",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28",
                "-pix_fmt", "yuv420p"
            });
        }
        else
        {
            args.Add("-vn");
        }
        if (file.Info.HasAudio)
        {
            args.AddRange(new[] { "-map", "0:a:0", "-c:a", "aac", "-b:a", "128k" });
        }
        else
        {
            args.Add("-an");
        }
        args.AddRange(new[] { "-sn", "-dn", "-movflags", "+faststart", output });
        return args;
    }

    private void UpdateScrollBar()
    {
        if (_timeline == null || _scroll == null)
        {
            return;
        }
        double total = Math.Max(_timeline.TotalSeconds, _timeline.VisibleSeconds);
        int value = (int)Math.Round(_timeline.ScrollSeconds * 10);
        _scroll.Minimum = 0;
        _scroll.Maximum = Math.Max(1, (int)Math.Ceiling(total * 10));
        _scroll.LargeChange = Math.Max(1, (int)Math.Ceiling(_timeline.VisibleSeconds * 10));
        _scroll.Value = Math.Min(_scroll.Maximum, Math.Max(0, value));
    }

    private void SetZoom(double pixelsPerSecond)
    {
        _timeline.PixelsPerSecond = Math.Clamp(pixelsPerSecond, 4, 1600);
        UpdateScrollBar();
    }

    private void FitTimeline()
    {
        if (_activeFile == null)
        {
            return;
        }
        SetZoom(Math.Max(1, (_timeline.Width - 30) / Math.Max(1, _activeFile.Duration)));
        _timeline.ScrollSeconds = 0;
        UpdateScrollBar();
    }

    // ================= 导出 =================

    private int GetSelectedFileCount()
    {
        int count = _files.Count(file => file.Enabled);
        return _activeFile != null && !_activeFile.Enabled ? count + 1 : count;
    }

    private void ShowExportDialog()
    {
        if (_files.Count == 0)
        {
            MessageBox.Show(this, "请先打开视频或音频文件。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (VideoToolkit.Ffmpeg(_config.FfmpegPath) == null)
        {
            if (MessageBox.Show(this, "未找到 ffmpeg.exe，无法导出。\n\n是否现在设置？",
                "缺少 FFmpeg", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                ConfigureFfmpeg();
            }
            return;
        }
        var preview = new List<(string FileName, int Index, double Start, double End)>();
        foreach (var file in _files.Where(file => file.Enabled))
        {
            int index = 0;
            foreach (var segment in file.Segments
                .Where(segment => segment.Enabled && segment.Duration > 0.02)
                .OrderBy(segment => segment.Start))
            {
                index++;
                preview.Add((file.Name, index, segment.Start, segment.End));
                if (preview.Count >= 500)
                {
                    break;
                }
            }
            if (preview.Count >= 500)
            {
                break;
            }
        }
        using var dialog = new LosslessCutExportDialog(GetSelectedFileCount(),
            _activeFile != null, _encoders, preview, _inPoint, _outPoint);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _ = ExportSegmentsAsync(dialog.Settings);
    }

    private async Task ExportSegmentsAsync(LosslessExportSettings settings)
    {
        if (_busy)
        {
            return;
        }
        var targets = settings.ActiveFileOnly
            ? (_activeFile != null ? new List<LosslessFile> { _activeFile } : new List<LosslessFile>())
            : _files.Where(file => file.Enabled).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "没有勾选要导出的文件。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var jobs = new List<(LosslessFile File, LosslessSegment Segment)>();
        foreach (var file in targets)
        {
            foreach (var segment in file.Segments.Where(segment => segment.Enabled
                && segment.Duration > 0.02))
            {
                jobs.Add((file, segment));
            }
        }
        if (jobs.Count == 0)
        {
            MessageBox.Show(this, "没有启用且有效的片段可导出。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Directory.CreateDirectory(settings.OutputDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法创建输出目录：\n" + ex.Message, "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _busy = true;
        using var progress = new VideoProgressDialog("正在导出片段",
            "共 " + jobs.Count + " 个片段 · " + (settings.Lossless ? "无损流复制" : "精确重编码"));
        _progressDialog = progress;
        progress.Show(this);
        Enabled = false;
        int completed = 0;
        try
        {
            foreach (var file in targets)
            {
                var segments = file.Segments.Where(segment => segment.Enabled
                    && segment.Duration > 0.02).ToList();
                if (segments.Count == 0)
                {
                    continue;
                }
                string extension = settings.ExtensionFor(file);
                if (settings.Mode == LosslessExportMode.Merge)
                {
                    string tempDir = Path.Combine(AppPaths.Temp,
                        "MiniMonitor_lossless_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    var parts = new List<string>();
                    try
                    {
                        for (int i = 0; i < segments.Count; i++)
                        {
                            if (progress.Token.IsCancellationRequested)
                            {
                                throw new OperationCanceledException();
                            }
                            var segment = segments[i];
                            string part = Path.Combine(tempDir,
                                "part" + i.ToString("000", CultureInfo.InvariantCulture) + ".mkv");
                            var args = LosslessCutExporter.SegmentArguments(file, segment, part,
                                settings, _encoders);
                            progress.SetDetails(file.Name + " · 片段 " + (i + 1)
                                + " · 入点 " + VideoTheme.Timecode(segment.Start)
                                + " → 出点 " + VideoTheme.Timecode(segment.End));
                            var result = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath, args,
                                segment.Duration, (fraction, status) =>
                                {
                                    double overall = (completed + fraction * 0.8) / jobs.Count;
                                    progress.UpdateProgress(overall, status + " · " + file.Name);
                                }, progress.Token);
                            if (result.ExitCode != 0 || !File.Exists(part))
                            {
                                throw new InvalidOperationException("片段导出失败："
                                    + Tail(result.StdErr, 500));
                            }
                            parts.Add(part);
                            completed++;
                        }
                        string output = UniqueOutput(Path.Combine(settings.OutputDir,
                            LosslessCutExporter.BuildMergeName(file, settings, extension)));
                        string listFile = Path.Combine(tempDir, "list.txt");
                        LosslessCutExporter.WriteConcatList(listFile, parts);
                        var concatArgs = LosslessCutExporter.ConcatArguments(listFile, output,
                            settings.Lossless);
                        var concatResult = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath,
                            concatArgs, file.Duration, (fraction, status) =>
                            {
                                double overall = Math.Min(1, (completed + fraction) / jobs.Count);
                                progress.UpdateProgress(overall, "合并：" + status);
                            }, progress.Token);
                        if (concatResult.ExitCode != 0)
                        {
                            throw new InvalidOperationException("合并失败："
                                + Tail(concatResult.StdErr, 500));
                        }
                        _statusInfo.Text = "已导出：" + output;
                    }
                    finally
                    {
                        try
                        {
                            Directory.Delete(tempDir, recursive: true);
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < segments.Count; i++)
                    {
                        if (progress.Token.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                        var segment = segments[i];
                        string name = LosslessCutExporter.BuildOutputName(file, segment, i + 1,
                            settings, extension);
                        string output = UniqueOutput(Path.Combine(settings.OutputDir, name));
                        progress.SetDetails(file.Name + " · 入点 "
                            + VideoTheme.Timecode(segment.Start) + " → 出点 "
                            + VideoTheme.Timecode(segment.End)
                            + " · 时长 " + VideoTheme.Timecode(segment.Duration));
                        var args = LosslessCutExporter.SegmentArguments(file, segment, output,
                            settings, _encoders);
                        var result = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath, args,
                            segment.Duration, (fraction, status) =>
                            {
                                double overall = (completed + fraction) / jobs.Count;
                                progress.UpdateProgress(overall, status + " · " + Path.GetFileName(output));
                            }, progress.Token);
                        if (result.ExitCode != 0 || !File.Exists(output))
                        {
                            throw new InvalidOperationException("片段导出失败："
                                + Tail(result.StdErr, 500));
                        }
                        completed++;
                    }
                }
            }
            progress.Finish(true, "导出完成：成功 " + completed + " 个片段");
            if (MessageBox.Show(this, "导出完成，共 " + completed + " 个片段。\n\n是否打开输出目录？",
                "无损截取", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = settings.OutputDir,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch (OperationCanceledException)
        {
            progress.Finish(false, "已取消：" + completed + " 个片段已完成");
            _statusInfo.Text = "导出已取消";
        }
        catch (Exception ex)
        {
            VideoLog.Error("片段导出失败", ex);
            progress.Finish(false, "导出失败：" + ex.Message);
            MessageBox.Show(this, "导出失败：\n" + ex.Message,
                "无损截取", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                Enabled = true;
            }
            _progressDialog = null;
            _busy = false;
            if (!progress.IsDisposed)
            {
                progress.Close();
            }
        }
    }

    private static string UniqueOutput(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }
        string directory = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 1; i < 9999; i++)
        {
            string candidate = Path.Combine(directory, name + "_" + i + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(directory, name + "_" + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    private async void MergeCurrentFiles()
    {
        if (_files.Count < 2)
        {
            MessageBox.Show(this, "至少需要打开两个文件才能拼接。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (VideoToolkit.Ffmpeg(_config.FfmpegPath) == null)
        {
            ConfigureFfmpeg();
            return;
        }
        var result = MessageBox.Show(this,
            "是否以无损流复制方式拼接？\n\n是：不重新编码，要求所有文件编码参数一致\n否：重新编码（较慢）",
            "拼接文件", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel)
        {
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "保存拼接文件",
            Filter = "MKV|*.mkv|MP4|*.mp4|所有文件|*.*",
            FileName = "merged.mkv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        string listFile = Path.Combine(AppPaths.Temp,
            "MiniMonitor_concat_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            LosslessCutExporter.WriteConcatList(listFile, _files.Select(file => file.Path));
            var args = LosslessCutExporter.ConcatArguments(listFile, dialog.FileName,
                result == DialogResult.Yes);
            await RunSimpleAsync("正在拼接文件", args, _files.Sum(file => file.Duration),
                dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "拼接失败：\n" + ex.Message, "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                File.Delete(listFile);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void ExtractAudio()
    {
        if (_activeFile == null)
        {
            MessageBox.Show(this, "请先打开一个文件。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_activeFile.Info.HasAudio)
        {
            MessageBox.Show(this, "当前文件没有音频流。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "提取音频",
            Filter = "M4A / AAC|*.m4a|MP3|*.mp3|FLAC|*.flac|WAV|*.wav|原样复制（MKA）|*.mka",
            FileName = Path.GetFileNameWithoutExtension(_activeFile.Name) + ".m4a",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", _activeFile.Path, "-vn"
        };
        switch (extension)
        {
            case ".mp3":
                args.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", "2" });
                break;
            case ".flac":
                args.AddRange(new[] { "-c:a", "flac" });
                break;
            case ".wav":
                args.AddRange(new[] { "-c:a", "pcm_s16le" });
                break;
            case ".mka":
                args.AddRange(new[] { "-c:a", "copy" });
                break;
            default:
                args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
                break;
        }
        args.Add(dialog.FileName);
        _ = RunSimpleAsync("正在提取音频", args, _activeFile.Duration, dialog.FileName);
    }

    private void ExportChapters()
    {
        if (_activeFile == null || _activeFile.Segments.Count == 0)
        {
            MessageBox.Show(this, "当前文件没有可导出的片段。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "导出章节",
            Filter = "FFmetadata|*.txt;*.ffmetadata|所有文件|*.*",
            FileName = Path.GetFileNameWithoutExtension(_activeFile.Name) + "_chapters.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine(";FFMETADATA1");
            builder.AppendLine("title=" + _activeFile.Name.Replace("=", "\\="));
            int index = 1;
            foreach (var segment in _activeFile.Segments.OrderBy(segment => segment.Start))
            {
                if (segment.Duration <= 0.02)
                {
                    continue;
                }
                builder.AppendLine("[CHAPTER]");
                builder.AppendLine("TIMEBASE=1/1000");
                builder.AppendLine("START=" + (long)Math.Round(segment.Start * 1000));
                builder.AppendLine("END=" + (long)Math.Round(segment.End * 1000));
                string title = string.IsNullOrWhiteSpace(segment.Name)
                    ? "片段 " + index : segment.Name;
                builder.AppendLine("title=" + title.Replace("=", "\\="));
                index++;
            }
            File.WriteAllText(dialog.FileName, builder.ToString(),
                new System.Text.UTF8Encoding(false));
            _statusInfo.Text = "已导出章节：" + dialog.FileName;
            MessageBox.Show(this, "章节已导出：\n" + dialog.FileName, "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            VideoLog.Error("导出章节失败", ex);
            MessageBox.Show(this, "导出章节失败：\n" + ex.Message, "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TakeScreenshot()
    {
        if (_activeFile == null)
        {
            MessageBox.Show(this, "请先打开一个文件。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_activeFile.Info.HasVideo)
        {
            MessageBox.Show(this, "当前文件没有视频流。", "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "MiniMonitor", "LosslessCut");
        Directory.CreateDirectory(folder);
        string output = Path.Combine(folder,
            Path.GetFileNameWithoutExtension(_activeFile.Name) + "_"
            + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-ss", _playhead.ToString("F3", CultureInfo.InvariantCulture),
            "-i", _activeFile.Path, "-frames:v", "1", "-q:v", "2", output
        };
        _ = RunSimpleAsync("正在截取当前帧", args, _activeFile.Duration, output);
    }

    private async Task RunSimpleAsync(string title, List<string> args, double duration,
        string output)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        using var progress = new VideoProgressDialog(title, Path.GetFileName(output));
        progress.Show(this);
        Enabled = false;
        try
        {
            var result = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath, args, duration,
                (fraction, status) => progress.UpdateProgress(fraction, status), progress.Token);
            if (result.ExitCode == 0 && File.Exists(output))
            {
                progress.Finish(true, "完成：" + output);
                _statusInfo.Text = "完成：" + output;
                if (MessageBox.Show(this, "已完成：\n" + output + "\n\n是否打开所在目录？",
                    title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Path.GetDirectoryName(output) ?? "",
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            else
            {
                progress.Finish(false, "失败");
                MessageBox.Show(this, "操作失败：\n" + Tail(result.StdErr, 1000), title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (OperationCanceledException)
        {
            progress.Finish(false, "已取消");
        }
        catch (Exception ex)
        {
            VideoLog.Error(title + "失败", ex);
            progress.Finish(false, ex.Message);
            MessageBox.Show(this, title + "失败：\n" + ex.Message, title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                Enabled = true;
            }
            _busy = false;
            if (!progress.IsDisposed)
            {
                progress.Close();
            }
        }
    }

    // ================= 检测 =================

    private async void DetectScenes()
    {
        if (_activeFile == null || _busy)
        {
            return;
        }
        double threshold = 0.35;
        using var input = new Form
        {
            Text = "检测场景切换",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 150),
            BackColor = VideoTheme.Panel,
            ForeColor = VideoTheme.Text,
            Font = VideoTheme.Ui9
        };
        var label = VideoTheme.Label("场景阈值（0.05 - 0.95，越大越严格）");
        label.Location = new Point(18, 16);
        var numeric = new NumericUpDown
        {
            Location = new Point(18, 48),
            Size = new Size(120, 28),
            Minimum = 0.05M,
            Maximum = 0.95M,
            DecimalPlaces = 2,
            Increment = 0.05M,
            Value = 0.35M
        };
        VideoTheme.StyleNumeric(numeric, 120, 2);
        var ok = VideoTheme.Button("开始检测", 100, true);
        ok.Location = new Point(150, 96);
        ok.DialogResult = DialogResult.OK;
        var cancel = VideoTheme.Button("取消", 80);
        cancel.Location = new Point(258, 96);
        cancel.DialogResult = DialogResult.Cancel;
        input.Controls.AddRange(new Control[] { label, numeric, ok, cancel });
        input.AcceptButton = ok;
        input.CancelButton = cancel;
        if (input.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        threshold = (double)numeric.Value;
        _busy = true;
        using var progress = new VideoProgressDialog("正在检测场景切换",
            "阈值 " + threshold.ToString("0.00") + " · 文件 " + _activeFile.Name);
        progress.Show(this);
        Enabled = false;
        try
        {
            var scenes = await VideoToolkit.DetectScenesAsync(_activeFile.Path, threshold,
                _config.FfmpegPath, progress.Token);
            int added = 0;
            if (scenes.Count > 0)
            {
                PushUndo();
                foreach (double time in scenes)
                {
                    if (TrySplitAt(time))
                    {
                        added++;
                    }
                }
                RefreshSegmentList();
            }
            progress.Finish(true, "检测到 " + scenes.Count + " 个场景，新增 " + added + " 个片段");
            _statusInfo.Text = "场景检测完成：发现 " + scenes.Count + " 个场景";
        }
        catch (OperationCanceledException)
        {
            progress.Finish(false, "已取消");
        }
        catch (Exception ex)
        {
            VideoLog.Error("场景检测失败", ex);
            progress.Finish(false, "检测失败：" + ex.Message);
            MessageBox.Show(this, "场景检测失败：\n" + ex.Message, "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                Enabled = true;
            }
            _busy = false;
            if (!progress.IsDisposed)
            {
                progress.Close();
            }
        }
    }

    private async void DetectBlackFrames()
    {
        if (_activeFile == null || _busy)
        {
            return;
        }
        _busy = true;
        using var progress = new VideoProgressDialog("正在检测黑帧",
            "文件 " + _activeFile.Name);
        progress.Show(this);
        Enabled = false;
        try
        {
            var ranges = await VideoToolkit.DetectBlackAsync(_activeFile.Path,
                _config.FfmpegPath, progress.Token);
            int added = 0;
            if (ranges.Count > 0)
            {
                PushUndo();
                foreach (var range in ranges)
                {
                    if (TrySplitAt(range.Start))
                    {
                        added++;
                    }
                    if (TrySplitAt(range.End))
                    {
                        added++;
                    }
                }
                RefreshSegmentList();
            }
            progress.Finish(true, "检测到 " + ranges.Count + " 段黑帧，新增 " + added + " 个分割");
            _statusInfo.Text = "黑帧检测完成：发现 " + ranges.Count + " 段";
        }
        catch (OperationCanceledException)
        {
            progress.Finish(false, "已取消");
        }
        catch (Exception ex)
        {
            VideoLog.Error("黑帧检测失败", ex);
            progress.Finish(false, "检测失败：" + ex.Message);
            MessageBox.Show(this, "黑帧检测失败：\n" + ex.Message, "无损截取",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                Enabled = true;
            }
            _busy = false;
            if (!progress.IsDisposed)
            {
                progress.Close();
            }
        }
    }

    private bool TrySplitAt(double time)
    {
        if (_activeFile == null)
        {
            return false;
        }
        var segment = _activeFile.Segments.FirstOrDefault(item =>
            time > item.Start + 0.02 && time < item.End - 0.02);
        if (segment == null)
        {
            return false;
        }
        double originalEnd = segment.End;
        segment.End = time;
        _activeFile.Segments.Add(new LosslessSegment
        {
            Start = time,
            End = originalEnd,
            Name = segment.Name
        });
        return true;
    }

    // ================= 其他 =================

    private async Task RefreshEncodersAsync()
    {
        try
        {
            _encoders = await VideoToolkit.DetectEncodersAsync(_config.FfmpegPath, CancellationToken.None);
            if (!IsDisposed)
            {
                _gpuLabel.Text = _encoders.GpuAvailable
                    ? "GPU：" + _encoders.Label + "（精确切割默认启用）"
                    : "GPU：未检测到硬件编码器，使用 CPU";
                _gpuLabel.ForeColor = _encoders.GpuAvailable ? VideoTheme.Success : VideoTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            VideoLog.Warn("编码器检测失败：" + ex.Message);
        }
    }

    private void ConfigureFfmpeg()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (VideoToolkit.Ffmpeg(_config.FfmpegPath) is string current)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _config.FfmpegPath = dialog.FileName;
        _config.Save();
        _statusInfo.Text = "FFmpeg 路径已更新：" + dialog.FileName;
        _ = RefreshEncodersAsync();
    }

    private void ShowShortcuts()
    {
        MessageBox.Show(this,
            "空格：播放 / 暂停\n"
            + "I / O：设入点 / 出点\n"
            + "S：在播放头分割\n"
            + "Delete：删除选中片段\n"
            + "Ctrl+A：全选片段\n"
            + "Ctrl+Z / Ctrl+Y：撤销 / 重做\n"
            + "Ctrl+O：打开文件\n"
            + "Ctrl+E：导出片段\n"
            + "← / →：上一帧 / 下一帧\n"
            + "F11：全屏；Esc：退出全屏\n"
            + "Ctrl+滚轮：缩放；Shift+滚轮：横向滚动；滚轮：前后 1 秒",
            "无损截取快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            "无损视频截取 · MiniMonitor 复刻版\n\n"
            + "功能对标 LosslessCut 3.70.2：关键帧吸附、片段列表、"
            + "无损流复制切割、片段合并、文件拼接、提取音频、截图、"
            + "场景切换与黑帧检测。\n"
            + "LosslessCut 是 mifi 的开源项目（GPL v2），本功能为独立 Windows 原生实现。\n\nMiniMonitor v"
            + Application.ProductVersion,
            "关于无损视频截取", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _savedBorder = FormBorderStyle;
            _savedState = WindowState;
            _fullscreen = true;
            _menu.Visible = false;
            _toolbar.Visible = false;
            _status.Visible = false;
            _mainSplit.Panel1Collapsed = true;
            _rightSplit.Panel2Collapsed = true;
            _previewControls.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            _fullscreen = false;
            FormBorderStyle = _savedBorder;
            WindowState = _savedState;
            _menu.Visible = true;
            _toolbar.Visible = true;
            _status.Visible = true;
            _mainSplit.Panel1Collapsed = false;
            _rightSplit.Panel2Collapsed = false;
            _previewControls.Visible = true;
            PositionSplitters();
        }
        _timeline.Invalidate();
    }

    private void PositionSplitters()
    {
        // 不设置 Panel1MinSize / Panel2MinSize，避免窗体尚未布局时
        // SplitContainer 用默认 150px 宽度校验最小值而抛 InvalidOperationException。
        try
        {
            int width = _mainSplit.ClientSize.Width;
            if (width > 120)
            {
                int max = Math.Max(1, width - _mainSplit.SplitterWidth - 1);
                _mainSplit.SplitterDistance = Math.Clamp((int)(width * 0.24), 1, max);
            }
            int height = _rightSplit.ClientSize.Height;
            if (height > 80)
            {
                int max = Math.Max(1, height - _rightSplit.SplitterWidth - 1);
                _rightSplit.SplitterDistance = Math.Clamp((int)(height * 0.62), 1, max);
            }
        }
        catch (InvalidOperationException ex)
        {
            VideoLog.Warn("设置分隔条位置失败：" + ex.Message);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is TextBoxBase || ActiveControl is NumericUpDown
            || ActiveControl is ComboBox || ReferenceEquals(ActiveControl, _fileList))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }
        switch (keyData)
        {
            case Keys.Space:
                TogglePlay();
                return true;
            case Keys.I:
                SetInPoint();
                return true;
            case Keys.O:
                SetOutPoint();
                return true;
            case Keys.S:
                SplitAtPlayhead();
                return true;
            case Keys.Delete:
                DeleteSelectedSegments();
                return true;
            case Keys.Left:
                StepFrame(-1);
                return true;
            case Keys.Right:
                StepFrame(1);
                return true;
            case Keys.F11:
                ToggleFullscreen();
                return true;
            case Keys.Escape when _fullscreen:
                ToggleFullscreen();
                return true;
            case Keys.Control | Keys.O:
                _ = OpenFilesDialogAsync();
                return true;
            case Keys.Control | Keys.E:
                ShowExportDialog();
                return true;
            case Keys.Control | Keys.Z:
                Undo();
                return true;
            case Keys.Control | Keys.Y:
                Redo();
                return true;
            case Keys.Control | Keys.A:
                SelectAllSegments();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static string Tail(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "未知错误";
        }
        text = text.Trim();
        return text.Length <= maxLength ? text : text[^maxLength..];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _playTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
