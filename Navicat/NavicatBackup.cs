using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>备份 / 还原窗口。</summary>
internal sealed class NvBackupRestoreForm : Form
{
    private readonly DbSession _session;
    private readonly TextBox _backupPath;
    private readonly CheckedListBox _tableList;
    private readonly RichTextBox _backupLog;
    private readonly ProgressBar _backupProgress;
    private readonly Button _backupButton;
    private readonly TextBox _restorePath;
    private readonly RichTextBox _restoreLog;
    private readonly ProgressBar _restoreProgress;
    private readonly Button _restoreButton;

    public NvBackupRestoreForm(DbSession session)
    {
        _session = session;
        Text = "备份 / 还原 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 660);
        MinimumSize = new Size(760, 560);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = NvTheme.HeaderBack };
        header.Controls.Add(new Label
        {
            Text = "备份 / 还原",
            Location = new Point(20, 8),
            AutoSize = true,
            Font = NvTheme.Ui(13f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = session.Profile.Name + " · " + session.Kind.ShortName() + " · " + session.CurrentDatabase,
            Location = new Point(22, 36),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        header.Controls.Add(new PictureBox
        {
            Image = NvIcons.Get(NvIcon.Backup, 30),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(840, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        });

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = NvTheme.Ui(9f), Padding = new Point(16, 6) };
        var backupPage = new TabPage("备份") { BackColor = NvTheme.Panel };
        var restorePage = new TabPage("还原") { BackColor = NvTheme.Panel };

        var backupTop = new Panel { Dock = DockStyle.Top, Height = 210, Padding = new Padding(14, 12, 14, 0) };
        backupTop.Controls.Add(new Label
        {
            Text = "选择要备份的表（默认全选）",
            Location = new Point(4, 4),
            AutoSize = true,
            Font = NvTheme.Ui(9.4f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        _tableList = new CheckedListBox
        {
            Location = new Point(4, 30),
            Size = new Size(400, 150),
            CheckOnClick = true,
            Font = NvTheme.Ui(9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        try
        {
            foreach (var table in session.GetTables(session.CurrentDatabase))
            {
                _tableList.Items.Add(table.DisplayName, true);
            }
        }
        catch
        {
            // ignore
        }
        backupTop.Controls.Add(_tableList);
        backupTop.Controls.Add(new Label
        {
            Text = "输出文件",
            Location = new Point(430, 30),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _backupPath = new TextBox
        {
            Location = new Point(430, 52),
            Width = 400,
            Font = NvTheme.Ui(9.2f),
            Text = Path.Combine(NvPaths.BackupsDir, session.Profile.Id,
                "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".sql")
        };
        backupTop.Controls.Add(_backupPath);
        var browseBackup = NvTheme.FlatButton("浏览…", null, null, 74);
        browseBackup.Location = new Point(720, 51);
        browseBackup.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "SQL 备份 (*.sql)|*.sql|所有文件 (*.*)|*.*",
                FileName = Path.GetFileName(_backupPath.Text)
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _backupPath.Text = dialog.FileName;
            }
        };
        backupTop.Controls.Add(browseBackup);
        backupTop.Controls.Add(new Label
        {
            Text = "说明：备份会生成完整的 CREATE TABLE / INSERT / CREATE VIEW 脚本，可在还原标签页或查询编辑器中执行。",
            Location = new Point(430, 90),
            Size = new Size(420, 44),
            ForeColor = NvTheme.Muted
        });
        _backupButton = NvTheme.FlatButton("开始备份", NvTheme.Accent, Color.White, 104);
        _backupButton.Location = new Point(430, 140);
        _backupButton.Click += async (_, _) => await StartBackupAsync();
        backupTop.Controls.Add(_backupButton);
        _backupProgress = new ProgressBar
        {
            Location = new Point(540, 144),
            Width = 300,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false
        };
        backupTop.Controls.Add(_backupProgress);

        _backupLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(9f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            WordWrap = false,
            Margin = new Padding(12)
        };
        var backupLogHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 14) };
        backupLogHost.Controls.Add(_backupLog);
        backupPage.Controls.Add(backupLogHost);
        backupPage.Controls.Add(backupTop);

        var restoreTop = new Panel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(14, 12, 14, 0) };
        restoreTop.Controls.Add(new Label
        {
            Text = "SQL 备份文件",
            Location = new Point(4, 20),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _restorePath = new TextBox
        {
            Location = new Point(104, 16),
            Width = 640,
            Font = NvTheme.Ui(9.2f)
        };
        restoreTop.Controls.Add(_restorePath);
        var browseRestore = NvTheme.FlatButton("浏览…", null, null, 74);
        browseRestore.Location = new Point(752, 15);
        browseRestore.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "SQL 备份 (*.sql)|*.sql|所有文件 (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _restorePath.Text = dialog.FileName;
            }
        };
        restoreTop.Controls.Add(browseRestore);
        restoreTop.Controls.Add(new Label
        {
            Text = "警告：还原会执行脚本中的全部语句，可能覆盖或删除现有数据，请先确认目标数据库。",
            Location = new Point(4, 56),
            Size = new Size(800, 24),
            ForeColor = NvTheme.Red
        });
        _restoreButton = NvTheme.FlatButton("开始还原", NvTheme.Orange, Color.White, 104);
        _restoreButton.Location = new Point(4, 90);
        _restoreButton.Click += async (_, _) => await StartRestoreAsync();
        restoreTop.Controls.Add(_restoreButton);
        _restoreProgress = new ProgressBar
        {
            Location = new Point(114, 94),
            Width = 300,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false
        };
        restoreTop.Controls.Add(_restoreProgress);

        _restoreLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(9f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            WordWrap = false
        };
        var restoreLogHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 14) };
        restoreLogHost.Controls.Add(_restoreLog);
        restorePage.Controls.Add(restoreLogHost);
        restorePage.Controls.Add(restoreTop);

        tabs.TabPages.Add(backupPage);
        tabs.TabPages.Add(restorePage);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = NvTheme.Background };
        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Location = new Point(ClientSize.Width - 100, 10);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.Cancel;
        bottom.Controls.Add(close);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 0) };
        body.Controls.Add(tabs);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
        CancelButton = close;
    }

    private void AppendBackupLog(string text)
    {
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => AppendBackupLog(text)));
            }
            catch
            {
                // 窗口已关闭
            }
            return;
        }
        _backupLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
    }

    private void AppendRestoreLog(string text)
    {
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => AppendRestoreLog(text)));
            }
            catch
            {
                // 窗口已关闭
            }
            return;
        }
        _restoreLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
    }

    private async Task StartBackupAsync()
    {
        string path = _backupPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            NvPrompt.Error(this, "备份", "请填写输出文件路径。");
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? NvPaths.BackupsDir);
        }
        catch (Exception ex)
        {
            NvPrompt.Error(this, "备份", "无法创建输出目录：" + ex.Message);
            return;
        }
        _backupButton.Enabled = false;
        _backupProgress.Visible = true;
        _backupLog.Clear();
        AppendBackupLog("开始备份数据库 " + _session.CurrentDatabase + "…");
        var selected = _tableList.CheckedItems.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        if (selected.Count == 0)
        {
            _backupButton.Enabled = true;
            _backupProgress.Visible = false;
            NvPrompt.Error(this, "备份", "请至少选择一张表。");
            return;
        }
        try
        {
            int rows = await Task.Run(() => BackupService.DumpDatabase(_session, _session.CurrentDatabase, path,
                AppendBackupLog, selected));
            AppendBackupLog("备份完成：" + path + "，共 " + rows + " 行。");
            NvSettingsStore.Current.BackupFolder = Path.GetDirectoryName(path) ?? NvSettingsStore.Current.BackupFolder;
            NvSettingsStore.Save();
            NvPrompt.Info(this, "备份完成", "备份完成，共 " + rows + " 行：\n" + path);
        }
        catch (Exception ex)
        {
            AppendBackupLog("备份失败：" + ex.Message);
            NvLog.Error(ex, "工具异常：备份失败");
            NvPrompt.Error(this, "备份失败", ex.Message);
        }
        finally
        {
            _backupButton.Enabled = true;
            _backupProgress.Visible = false;
        }
    }

    private async Task StartRestoreAsync()
    {
        string path = _restorePath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            NvPrompt.Error(this, "还原", "请选择有效的 SQL 备份文件。");
            return;
        }
        if (!NvPrompt.Confirm(this, "还原数据库",
            "还原会执行脚本中的全部 SQL 语句，可能覆盖现有数据。\n确定继续吗？", true))
        {
            return;
        }
        _restoreButton.Enabled = false;
        _restoreProgress.Visible = true;
        _restoreLog.Clear();
        AppendRestoreLog("开始还原：" + path);
        try
        {
            int count = await Task.Run(() => BackupService.Restore(_session, path, AppendRestoreLog));
            AppendRestoreLog("还原完成，共执行 " + count + " 条语句。");
            NvPrompt.Info(this, "还原完成", "还原完成，共执行 " + count + " 条语句。");
        }
        catch (Exception ex)
        {
            AppendRestoreLog("还原失败：" + ex.Message);
            NvLog.Error(ex, "工具异常：还原失败");
            NvPrompt.Error(this, "还原失败", ex.Message);
        }
        finally
        {
            _restoreButton.Enabled = true;
            _restoreProgress.Visible = false;
        }
    }
}
