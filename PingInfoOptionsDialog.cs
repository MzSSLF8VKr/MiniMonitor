using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>PingInfoView 高级选项窗口。</summary>
public sealed class PingInfoOptionsDialog : Form
{
    private readonly NumericUpDown _payloadSize;
    private readonly NumericUpDown _ttl;
    private readonly CheckBox _dontFragment;
    private readonly CheckBox _allowIpv6;
    private readonly CheckBox _resolveEveryPing;
    private readonly CheckBox _highResolution;
    private readonly NumericUpDown _historyLimit;
    private readonly CheckBox _blackBackground;
    private readonly CheckBox _markOddEven;
    private readonly CheckBox _beepOnFailure;
    private readonly NumericUpDown _beepThreshold;
    private readonly CheckBox _skipCidrFirst;
    private readonly CheckBox _skipCidrLast;

    public PingInfoOptionsDialog(PingInfoSettings settings)
    {
        Text = "PingInfoView 选项";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 540);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);

        var pingTitle = MakeLabel("Ping 参数", 18, 12, 300, 24, 9.5f, true);
        Controls.Add(pingTitle);

        _payloadSize = MakeNumeric(190, 48, 100, 1, 65500, settings.PayloadSize);
        Controls.Add(MakeLabel("数据包大小", 18, 48, 150, 26, 8.5f, true));
        Controls.Add(_payloadSize);
        Controls.Add(MakeLabel("字节", 298, 48, 60, 26, 8.5f, false));

        _ttl = MakeNumeric(190, 84, 100, 1, 255, settings.Ttl);
        Controls.Add(MakeLabel("TTL", 18, 84, 150, 26, 8.5f, true));
        Controls.Add(_ttl);

        _dontFragment = MakeCheckBox("不分片（Don't Fragment）", 18, 120, 210, settings.DontFragment);
        _allowIpv6 = MakeCheckBox("允许 IPv6 地址", 320, 120, 180, settings.AllowIpv6);
        _resolveEveryPing = MakeCheckBox("每次 Ping 重新解析主机名", 18, 152, 260, settings.ResolveEveryPing);
        _highResolution = MakeCheckBox("高精度计时（含少量调度开销）", 320, 152, 250, settings.HighResolution);
        Controls.AddRange(new Control[]
        {
            _dontFragment, _allowIpv6, _resolveEveryPing, _highResolution
        });

        var logTitle = MakeLabel("日志与显示", 18, 194, 300, 24, 9.5f, true);
        Controls.Add(logTitle);

        _historyLimit = MakeNumeric(190, 230, 110, 100, 200000, settings.HistoryLimit, 10);
        Controls.Add(MakeLabel("日志上限", 18, 230, 150, 26, 8.5f, true));
        Controls.Add(_historyLimit);
        Controls.Add(MakeLabel("条（限制内存占用）", 308, 230, 220, 26, 8.5f, false));

        _blackBackground = MakeCheckBox("表格黑底白字", 18, 266, 180, settings.BlackBackground);
        _markOddEven = MakeCheckBox("奇偶行交替底色", 220, 266, 180, settings.MarkOddEvenRows);
        Controls.AddRange(new Control[] { _blackBackground, _markOddEven });

        var notifyTitle = MakeLabel("通知与地址范围", 18, 308, 300, 24, 9.5f, true);
        Controls.Add(notifyTitle);

        _beepOnFailure = MakeCheckBox("连续失败时蜂鸣", 18, 344, 180, settings.BeepOnFailure);
        _beepThreshold = MakeNumeric(330, 344, 70, 1, 1000, settings.ConsecutiveFailuresForBeep, 1);
        Controls.Add(_beepOnFailure);
        Controls.Add(MakeLabel("连续失败次数", 220, 344, 100, 26, 8.5f, false));
        Controls.Add(_beepThreshold);

        _skipCidrFirst = MakeCheckBox("CIDR 跳过首地址", 18, 380, 200, settings.CidrSkipFirst);
        _skipCidrLast = MakeCheckBox("CIDR 跳过末地址", 220, 380, 200, settings.CidrSkipLast);
        Controls.AddRange(new Control[] { _skipCidrFirst, _skipCidrLast });

        var hint = new Label
        {
            Text = "并发 Ping 数在主界面工具栏调整；间隔、超时也在主界面调整。",
            Location = new Point(18, 424),
            Size = new Size(560, 24),
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White
        };
        Controls.Add(hint);

        var okButton = new Button
        {
            Text = "确定",
            Location = new Point(396, 484),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(13, 148, 136),
            ForeColor = Color.White,
            DialogResult = DialogResult.OK
        };
        okButton.FlatAppearance.BorderColor = Color.FromArgb(13, 148, 136);
        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(494, 484),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        Controls.AddRange(new Control[] { okButton, cancelButton });
        AcceptButton = okButton;
        CancelButton = cancelButton;

        _beepOnFailure.CheckedChanged += (_, _) =>
            _beepThreshold.Enabled = _beepOnFailure.Checked;
        _beepThreshold.Enabled = _beepOnFailure.Checked;
    }

    public PingInfoSettings Apply(PingInfoSettings target)
    {
        target.PayloadSize = (int)_payloadSize.Value;
        target.Ttl = (int)_ttl.Value;
        target.DontFragment = _dontFragment.Checked;
        target.AllowIpv6 = _allowIpv6.Checked;
        target.ResolveEveryPing = _resolveEveryPing.Checked;
        target.HighResolution = _highResolution.Checked;
        target.HistoryLimit = (int)_historyLimit.Value;
        target.BlackBackground = _blackBackground.Checked;
        target.MarkOddEvenRows = _markOddEven.Checked;
        target.BeepOnFailure = _beepOnFailure.Checked;
        target.ConsecutiveFailuresForBeep = (int)_beepThreshold.Value;
        target.CidrSkipFirst = _skipCidrFirst.Checked;
        target.CidrSkipLast = _skipCidrLast.Checked;
        return target;
    }

    private static Label MakeLabel(
        string text, int x, int y, int width, int height, float fontSize, bool bold)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = new Font("Microsoft YaHei UI", fontSize,
                bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = bold ? ShutdownTheme.Text : ShutdownTheme.Muted,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static NumericUpDown MakeNumeric(
        int x, int y, int width, decimal min, decimal max, decimal value, decimal increment = 1)
    {
        return new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(width, 26),
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Increment = increment,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
    }

    private static CheckBox MakeCheckBox(
        string text, int x, int y, int width, bool isChecked)
    {
        return new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 24),
            Checked = isChecked,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White
        };
    }
}
