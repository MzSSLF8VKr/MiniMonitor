namespace MiniMonitor.Music;

/// <summary>音乐标签编辑器（写入本地文件标签，不上传任何信息）。</summary>
public sealed class MusicTagDialog : Form
{
    private readonly MusicTrack _track;
    private readonly MusicLibrary _library;
    private readonly TextBox _title;
    private readonly TextBox _artist;
    private readonly TextBox _albumArtist;
    private readonly TextBox _album;
    private readonly TextBox _genre;
    private readonly NumericUpDown _year;
    private readonly NumericUpDown _trackNo;
    private readonly NumericUpDown _discNo;
    private readonly NumericUpDown _rating;
    private readonly TextBox _comment;

    public MusicTagDialog(MusicTrack track, MusicLibrary library)
    {
        _track = track;
        _library = library;
        Text = "编辑音乐标签";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 520);
        BackColor = Color.FromArgb(250, 250, 250);
        Font = new Font("Microsoft YaHei UI", 9.2f);
        Padding = new Padding(20);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _title = AddText(layout, 0, "标题", track.Title);
        _artist = AddText(layout, 1, "艺术家", track.Artist);
        _albumArtist = AddText(layout, 2, "专辑艺术家", track.AlbumArtist);
        _album = AddText(layout, 3, "专辑", track.Album);
        _genre = AddText(layout, 4, "流派", track.Genre);
        _year = AddNumber(layout, 5, "年份", track.Year, 0, 9999);
        _trackNo = AddNumber(layout, 6, "音轨号", track.TrackNo, 0, 999);
        _discNo = AddNumber(layout, 7, "碟号", track.DiscNo, 0, 99);
        _rating = AddNumber(layout, 8, "评分", track.Rating, 0, 5);
        _comment = AddText(layout, 9, "备注", track.Comment, true);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 46,
            Padding = new Padding(0, 8, 0, 0)
        };
        var save = new Button
        {
            Text = "保存",
            DialogResult = DialogResult.OK,
            Width = 90,
            Height = 32,
            BackColor = Color.FromArgb(0, 103, 192),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) => SaveTags();
        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 90,
            Height = 32,
            FlatStyle = FlatStyle.Flat
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static TextBox AddText(TableLayoutPanel layout, int row, string label, string value,
        bool multiline = false)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, row);
        var box = new TextBox
        {
            Text = value ?? "",
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Multiline = multiline
        };
        layout.Controls.Add(box, 1, row);
        return box;
    }

    private static NumericUpDown AddNumber(TableLayoutPanel layout, int row, string label,
        int value, int min, int max)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, row);
        var box = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 120,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(box, 1, row);
        return box;
    }

    private void SaveTags()
    {
        try
        {
            if (!File.Exists(_track.Path))
            {
                MessageBox.Show(this, "文件不存在，无法保存标签。", "编辑音乐标签",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var file = TagLib.File.Create(_track.Path))
            {
                var tag = file.Tag;
                tag.Title = _title.Text.Trim();
                tag.Performers = SplitPeople(_artist.Text);
                tag.AlbumArtists = SplitPeople(_albumArtist.Text);
                tag.Album = _album.Text.Trim();
                tag.Genres = string.IsNullOrWhiteSpace(_genre.Text)
                    ? Array.Empty<string>()
                    : new[] { _genre.Text.Trim() };
                tag.Year = (uint)_year.Value;
                tag.Track = (uint)_trackNo.Value;
                tag.Disc = (uint)_discNo.Value;
                tag.Comment = _comment.Text;
                file.Save();
            }
            _track.Title = _title.Text.Trim();
            _track.Artist = _artist.Text.Trim();
            _track.AlbumArtist = _albumArtist.Text.Trim();
            _track.Album = _album.Text.Trim();
            _track.Genre = _genre.Text.Trim();
            _track.Year = (int)_year.Value;
            _track.TrackNo = (int)_trackNo.Value;
            _track.DiscNo = (int)_discNo.Value;
            _track.Comment = _comment.Text;
            _track.Rating = (int)_rating.Value;
            _track.Modified = new FileInfo(_track.Path).LastWriteTimeUtc.Ticks;
            _library.UpdateTrackMetadata(_track);
            _library.SetRating(_track.Id, _track.Rating);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存标签失败：" + ex.Message, "编辑音乐标签",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string[] SplitPeople(string text)
    {
        return text.Split(new[] { ';', '/', ',', '，', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>播放列表名称输入框（新建 / 重命名）。</summary>
public sealed class MusicTextPromptDialog : Form
{
    private readonly TextBox _input;

    public MusicTextPromptDialog(string title, string label, string value = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 160);
        BackColor = Color.FromArgb(250, 250, 250);
        Font = new Font("Microsoft YaHei UI", 9.2f);
        Padding = new Padding(20);

        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _input = new TextBox
        {
            Text = value,
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 46
        };
        var ok = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Width = 84,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 103, 192),
            ForeColor = Color.White
        };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 84,
            Height = 30,
            FlatStyle = FlatStyle.Flat
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
        Controls.Add(_input);
        Controls.Add(labelControl);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _input.Text.Trim();

    public static string? Show(IWin32Window owner, string title, string label, string value = "")
    {
        using var dialog = new MusicTextPromptDialog(title, label, value);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Value.Length > 0
            ? dialog.Value
            : null;
    }
}
