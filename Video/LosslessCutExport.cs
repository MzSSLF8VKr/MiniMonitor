using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Video;

internal enum LosslessExportMode
{
    Separate,
    Merge
}

internal enum LosslessContainer
{
    Keep,
    Mp4,
    Mkv,
    Webm
}

internal sealed class LosslessExportSettings
{
    public string OutputDir { get; set; } = "";
    public LosslessExportMode Mode { get; set; } = LosslessExportMode.Separate;
    public LosslessContainer Container { get; set; } = LosslessContainer.Keep;
    public bool Lossless { get; set; } = true;
    public bool UseGpu { get; set; } = true;
    public bool ActiveFileOnly { get; set; }
    public string NamingMode { get; set; } = "index";
    public string CustomPattern { get; set; } = "{name}_seg{index}";

    public string ExtensionFor(LosslessFile file)
    {
        return Container switch
        {
            LosslessContainer.Mp4 => ".mp4",
            LosslessContainer.Mkv => ".mkv",
            LosslessContainer.Webm => ".webm",
            _ => Path.GetExtension(file.Path).ToLowerInvariant() switch
            {
                ".mp4" or ".m4v" or ".mov" => ".mp4",
                ".webm" => ".webm",
                ".mkv" => ".mkv",
                ".avi" => ".avi",
                ".ts" or ".m2ts" => ".ts",
                ".mp3" => ".mp3",
                ".m4a" => ".m4a",
                ".flac" => ".flac",
                ".wav" => ".wav",
                _ => ".mkv"
            }
        };
    }
}

internal static class LosslessCutExporter
{
    public static string BuildOutputName(LosslessFile file, LosslessSegment segment, int index,
        LosslessExportSettings settings, string extension)
    {
        string baseName = Path.GetFileNameWithoutExtension(file.Name);
        string name = settings.NamingMode switch
        {
            "time" => baseName + "_" + Safe(segment.Start) + "-" + Safe(segment.End),
            "custom" => settings.CustomPattern
                .Replace("{name}", baseName, StringComparison.OrdinalIgnoreCase)
                .Replace("{index}", index.ToString("000", CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase)
                .Replace("{start}", Safe(segment.Start), StringComparison.OrdinalIgnoreCase)
                .Replace("{end}", Safe(segment.End), StringComparison.OrdinalIgnoreCase),
            _ => baseName + "_seg" + index.ToString("000", CultureInfo.InvariantCulture)
        };
        return Sanitize(name) + extension;
    }

    public static string BuildMergeName(LosslessFile file, LosslessExportSettings settings,
        string extension)
    {
        string baseName = Path.GetFileNameWithoutExtension(file.Name);
        return Sanitize(baseName + "_cut") + extension;
    }

    public static List<string> SegmentArguments(LosslessFile file, LosslessSegment segment,
        string output, LosslessExportSettings settings, HardwareEncoders encoders)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        if (settings.Lossless)
        {
            args.AddRange(new[]
            {
                "-ss", F(segment.Start), "-i", file.Path,
                "-t", F(segment.Duration),
                "-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero",
                "-ignore_unknown", output
            });
            return args;
        }
        args.AddRange(new[]
        {
            "-ss", F(segment.Start), "-to", F(segment.End), "-i", file.Path,
            "-map", "0:v?", "-map", "0:a?"
        });
        string extension = Path.GetExtension(output).ToLowerInvariant();
        if (extension is ".mp4" or ".mov" or ".mkv" or ".webm")
        {
            if (extension == ".webm")
            {
                args.AddRange(new[]
                {
                    "-c:v", "libvpx-vp9", "-crf", "28", "-b:v", "0", "-row-mt", "1",
                    "-c:a", "libopus", "-b:a", "160k"
                });
            }
            else
            {
                string encoder = encoders.H264(settings.UseGpu);
                args.AddRange(new[] { "-c:v", encoder });
                if (encoder == "libx264")
                {
                    args.AddRange(new[] { "-preset", "veryfast", "-crf", "20" });
                }
                else if (encoder == "h264_nvenc")
                {
                    args.AddRange(new[] { "-preset", "p4", "-rc", "vbr", "-cq", "20", "-b:v", "0" });
                }
                args.AddRange(new[] { "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k" });
                if (extension == ".mp4")
                {
                    args.AddRange(new[] { "-movflags", "+faststart" });
                }
            }
        }
        args.Add(output);
        return args;
    }

    public static List<string> ConcatArguments(string listFile, string output, bool lossless)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "concat", "-safe", "0", "-i", listFile
        };
        if (lossless)
        {
            args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero" });
        }
        else if (Path.GetExtension(output).Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-c:v", "libvpx-vp9", "-crf", "28", "-b:v", "0",
                "-row-mt", "1", "-c:a", "libopus", "-b:a", "160k" });
        }
        else
        {
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
                "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p" });
        }
        args.Add(output);
        return args;
    }

    public static void WriteConcatList(string listPath, IEnumerable<string> files)
    {
        var builder = new StringBuilder();
        foreach (string file in files)
        {
            builder.Append("file '").Append(file.Replace("'", "'\\''")).AppendLine("'");
        }
        File.WriteAllText(listPath, builder.ToString(), new UTF8Encoding(false));
    }

    private static string Safe(double value)
    {
        var time = TimeSpan.FromSeconds(value);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}-{time.Minutes:00}-{time.Seconds:00}"
            : $"{time.Minutes:00}-{time.Seconds:00}";
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name.Length == 0 ? "output" : name;
    }

    private static string F(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}

internal sealed class LosslessCutExportDialog : Form
{
    private readonly ComboBox _mode;
    private readonly TextBox _outputDir;
    private readonly ComboBox _container;
    private readonly ComboBox _naming;
    private readonly TextBox _pattern;
    private readonly CheckBox _lossless;
    private readonly CheckBox _gpu;
    private readonly CheckBox _activeOnly;
    private readonly Label _patternLabel;
    private readonly Label _summary;

    public LosslessCutExportDialog(int selectedFileCount, bool hasActiveFile,
        HardwareEncoders encoders,
        IReadOnlyList<(string FileName, int Index, double Start, double End)>? preview = null,
        double inPoint = -1, double outPoint = -1)
    {
        Text = "导出片段 - 无损视频截取";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 618);
        BackColor = VideoTheme.Panel;
        ForeColor = VideoTheme.Text;
        Font = VideoTheme.Ui9;

        var title = new Label
        {
            Text = "导出片段",
            Location = new Point(18, 14),
            Size = new Size(300, 28),
            Font = VideoTheme.UiTitle,
            ForeColor = VideoTheme.Text
        };
        var subtitle = new Label
        {
            Text = "已勾选 " + selectedFileCount + " 个文件"
                + (hasActiveFile ? " · 当前文件可单独导出" : ""),
            Location = new Point(18, 44),
            Size = new Size(590, 22),
            ForeColor = VideoTheme.Muted
        };

        _mode = new ComboBox
        {
            Location = new Point(130, 82),
            Size = new Size(300, 28),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        VideoTheme.StyleCombo(_mode, 300);
        _mode.Items.AddRange(new object[] { "每个片段单独文件", "把当前文件的片段合并为一个文件" });
        _mode.SelectedIndex = 0;

        _outputDir = new TextBox
        {
            Location = new Point(130, 124),
            Size = new Size(390, 28),
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "MiniMonitor", "LosslessCut")
        };
        VideoTheme.StyleTextBox(_outputDir);
        var browse = VideoTheme.Button("浏览…", 88);
        browse.Location = new Point(528, 123);
        browse.Click += (_, _) => Browse();

        _naming = new ComboBox
        {
            Location = new Point(130, 166),
            Size = new Size(300, 28),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        VideoTheme.StyleCombo(_naming, 300);
        _naming.Items.AddRange(new object[]
        {
            "原文件名_序号（name_seg001）",
            "原文件名_开始结束时间（name_00-00-00-00-00-05）",
            "自定义模式"
        });
        _naming.SelectedIndex = 0;

        _patternLabel = VideoTheme.Label("自定义", true);
        _patternLabel.Location = new Point(18, 206);
        _patternLabel.Enabled = false;
        _pattern = new TextBox
        {
            Location = new Point(130, 204),
            Size = new Size(300, 28),
            Text = "{name}_seg{index}",
            Enabled = false
        };
        VideoTheme.StyleTextBox(_pattern);
        _naming.SelectedIndexChanged += (_, _) =>
        {
            _pattern.Enabled = _naming.SelectedIndex == 2;
            _patternLabel.Enabled = _pattern.Enabled;
        };

        _container = new ComboBox
        {
            Location = new Point(130, 246),
            Size = new Size(300, 28),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        VideoTheme.StyleCombo(_container, 300);
        _container.Items.AddRange(new object[]
        {
            "保持原容器（推荐无损）",
            "MP4",
            "MKV",
            "WebM"
        });
        _container.SelectedIndex = 0;

        _lossless = new CheckBox
        {
            Text = "无损流复制（不重新编码，最快且画质不变）",
            Location = new Point(130, 288),
            Size = new Size(460, 26),
            Checked = true,
            ForeColor = VideoTheme.Text,
            BackColor = Color.Transparent
        };
        _gpu = new CheckBox
        {
            Text = encoders.GpuAvailable
                ? "精确切割时使用 GPU 编码（" + encoders.Label + "）"
                : "精确切割时使用 CPU 编码",
            Location = new Point(130, 318),
            Size = new Size(460, 26),
            Checked = encoders.GpuAvailable,
            Enabled = false,
            ForeColor = encoders.GpuAvailable ? VideoTheme.Text : VideoTheme.Muted,
            BackColor = Color.Transparent
        };
        _lossless.CheckedChanged += (_, _) => _gpu.Enabled = !_lossless.Checked;
        _activeOnly = new CheckBox
        {
            Text = "只导出当前文件（不批量）",
            Location = new Point(130, 348),
            Size = new Size(300, 26),
            Checked = !hasActiveFile || selectedFileCount <= 1,
            Enabled = hasActiveFile && selectedFileCount > 1,
            ForeColor = VideoTheme.Text,
            BackColor = Color.Transparent
        };
        var markerInfo = new Label
        {
            Location = new Point(18, 376),
            Size = new Size(604, 38),
            ForeColor = VideoTheme.Text,
            BackColor = Color.Transparent,
            Font = VideoTheme.UiSmall
        };
        string inText = inPoint >= 0 ? VideoTheme.Timecode(inPoint) : "—";
        string outText = outPoint >= 0 ? VideoTheme.Timecode(outPoint) : "—";
        if (inPoint >= 0 && outPoint >= 0 && outPoint > inPoint + 0.001)
        {
            markerInfo.Text = "视频截取入点：" + inText + "      视频截取出点：" + outText
                + "      标记时长：" + VideoTheme.Timecode(outPoint - inPoint);
        }
        else if (inPoint >= 0 || outPoint >= 0)
        {
            markerInfo.Text = "视频截取入点：" + inText + "      视频截取出点：" + outText
                + "      （再设置另一个标记后可组成完整截取范围）";
        }
        else
        {
            markerInfo.Text = "视频截取入点：—      视频截取出点：—      （在主窗口按 I / O 设置截取标记）";
        }
        var previewLabel = new Label
        {
            Text = "将导出的片段（入点 → 出点）",
            Location = new Point(18, 416),
            Size = new Size(420, 20),
            ForeColor = VideoTheme.Muted,
            BackColor = Color.Transparent
        };
        var previewList = new ListView
        {
            Location = new Point(18, 438),
            Size = new Size(604, 100),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None
        };
        VideoTheme.StyleListView(previewList);
        previewList.Columns.Add("文件", 190);
        previewList.Columns.Add("#", 42);
        previewList.Columns.Add("入点", 110);
        previewList.Columns.Add("出点", 110);
        previewList.Columns.Add("时长", 120);
        if (preview != null)
        {
            foreach (var item in preview)
            {
                var row = new ListViewItem(item.FileName)
                {
                    ToolTipText = item.FileName
                };
                row.SubItems.Add("#" + item.Index);
                row.SubItems.Add(VideoTheme.Timecode(item.Start));
                row.SubItems.Add(VideoTheme.Timecode(item.End));
                row.SubItems.Add(VideoTheme.Timecode(Math.Max(0, item.End - item.Start)));
                previewList.Items.Add(row);
            }
        }
        if (previewList.Items.Count == 0)
        {
            previewList.Items.Add(new ListViewItem("没有启用且有效的片段"));
        }
        var previewHost = VideoTheme.WrapWithDarkHeader(previewList);
        previewHost.Location = new Point(18, 438);
        previewHost.Size = new Size(604, 100);

        _summary = new Label
        {
            Location = new Point(18, 544),
            Size = new Size(600, 24),
            ForeColor = VideoTheme.Muted,
            Text = "无损模式按关键帧切割；需要逐帧精确时关闭“无损流复制”。"
        };

        var export = VideoTheme.Button("开始导出", 108, true);
        export.Location = new Point(404, 574);
        export.Click += (_, _) => Confirm();
        var cancel = VideoTheme.Button("取消", 88);
        cancel.Location = new Point(522, 574);
        cancel.DialogResult = DialogResult.Cancel;

        var modeLabel = VideoTheme.Label("模式", true);
        modeLabel.Location = new Point(18, 88);
        var outputLabel = VideoTheme.Label("输出目录", true);
        outputLabel.Location = new Point(18, 130);
        var namingLabel = VideoTheme.Label("命名", true);
        namingLabel.Location = new Point(18, 172);
        var containerLabel = VideoTheme.Label("容器", true);
        containerLabel.Location = new Point(18, 252);

        Controls.AddRange(new Control[]
        {
            title, subtitle, modeLabel, _mode, outputLabel, _outputDir, browse,
            namingLabel, _naming, _patternLabel, _pattern, containerLabel, _container,
            _lossless, _gpu, _activeOnly, markerInfo, previewLabel, previewHost,
            _summary, export, cancel
        });
        AcceptButton = export;
        CancelButton = cancel;
    }

    public LosslessExportSettings Settings { get; private set; } = new();

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择片段输出目录",
            SelectedPath = Directory.Exists(_outputDir.Text)
                ? _outputDir.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputDir.Text = dialog.SelectedPath;
        }
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_outputDir.Text))
        {
            MessageBox.Show(this, "请选择输出目录。", "导出", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        Settings = new LosslessExportSettings
        {
            OutputDir = _outputDir.Text.Trim(),
            Mode = _mode.SelectedIndex == 1 ? LosslessExportMode.Merge : LosslessExportMode.Separate,
            Container = _container.SelectedIndex switch
            {
                1 => LosslessContainer.Mp4,
                2 => LosslessContainer.Mkv,
                3 => LosslessContainer.Webm,
                _ => LosslessContainer.Keep
            },
            Lossless = _lossless.Checked,
            UseGpu = _gpu.Checked,
            ActiveFileOnly = _activeOnly.Checked,
            NamingMode = _naming.SelectedIndex switch
            {
                1 => "time",
                2 => "custom",
                _ => "index"
            },
            CustomPattern = string.IsNullOrWhiteSpace(_pattern.Text)
                ? "{name}_seg{index}" : _pattern.Text.Trim()
        };
        DialogResult = DialogResult.OK;
    }
}
