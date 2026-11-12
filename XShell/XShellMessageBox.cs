using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>XShell 主题消息框，确保深色模式下所有确认 / 提示窗口也保持深色。</summary>
internal static class XShellMessageBox
{
    public static DialogResult Show(string text,
        string caption = "XShell 复刻版",
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        return Show(null, text, caption, buttons, icon);
    }

    public static DialogResult Show(IWin32Window? owner, string text, string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        using var form = new XShellMessageForm(text, caption, buttons, icon);
        return owner != null ? form.ShowDialog(owner) : form.ShowDialog();
    }

    private sealed class XShellMessageForm : Form
    {
        public XShellMessageForm(string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            Text = caption;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = XShellTheme.Background;
            ForeColor = XShellTheme.Text;
            Font = XShellTheme.Ui(9f);
            DoubleBuffered = true;
            HandleCreated += (_, _) => XShellWindowTheme.Apply(this);

            var messageFont = XShellTheme.Ui(9.2f);
            Size measured = TextRenderer.MeasureText(text, messageFont,
                new Size(468, 10000), TextFormatFlags.WordBreak);
            int contentHeight = Math.Max(66, Math.Min(measured.Height + 24, 460));
            ClientSize = new Size(560, 60 + contentHeight + 62);

            var header = new XShellHeaderPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Title = caption,
                Subtitle = "XShell 复刻版"
            };
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = XShellTheme.PanelAlt,
                Padding = new Padding(0, 15, 18, 15)
            };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(XShellTheme.Border);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = XShellTheme.Panel,
                Padding = new Padding(22, 16, 22, 12)
            };
            int textLeft = 22;
            if (icon != MessageBoxIcon.None)
            {
                string glyph = icon switch
                {
                    MessageBoxIcon.Warning => "\uE7BA",
                    MessageBoxIcon.Error => "\uE783",
                    MessageBoxIcon.Question => "\uE9CE",
                    _ => "\uE946"
                };
                Color color = icon switch
                {
                    MessageBoxIcon.Warning => XShellTheme.Orange,
                    MessageBoxIcon.Error => XShellTheme.Red,
                    _ => XShellTheme.Accent
                };
                var picture = new PictureBox
                {
                    Image = XShellIcons.Glyph(glyph, color, 34),
                    Left = 22,
                    Top = 18,
                    Width = 36,
                    Height = 36,
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BackColor = Color.Transparent
                };
                content.Controls.Add(picture);
                textLeft = 72;
            }
            var label = new Label
            {
                Text = text,
                Left = textLeft,
                Top = 16,
                Width = 560 - textLeft - 40,
                Height = contentHeight - 10,
                ForeColor = XShellTheme.Text,
                BackColor = Color.Transparent,
                Font = messageFont
            };
            content.Controls.Add(label);

            var buttonFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = XShellTheme.PanelAlt,
                Padding = new Padding(0, 0, 0, 0)
            };
            DialogResult? cancelResult = null;
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel:
                    buttonFlow.Controls.Add(CreateButton("取消", DialogResult.Cancel, false));
                    buttonFlow.Controls.Add(CreateButton("确定", DialogResult.OK, true));
                    cancelResult = DialogResult.Cancel;
                    AcceptButton = buttonFlow.Controls[1] as Button;
                    break;
                case MessageBoxButtons.YesNo:
                    buttonFlow.Controls.Add(CreateButton("否", DialogResult.No, false));
                    buttonFlow.Controls.Add(CreateButton("是", DialogResult.Yes, true));
                    cancelResult = DialogResult.No;
                    AcceptButton = buttonFlow.Controls[1] as Button;
                    break;
                case MessageBoxButtons.YesNoCancel:
                    buttonFlow.Controls.Add(CreateButton("取消", DialogResult.Cancel, false));
                    buttonFlow.Controls.Add(CreateButton("否", DialogResult.No, false));
                    buttonFlow.Controls.Add(CreateButton("是", DialogResult.Yes, true));
                    cancelResult = DialogResult.Cancel;
                    AcceptButton = buttonFlow.Controls[2] as Button;
                    break;
                case MessageBoxButtons.RetryCancel:
                    buttonFlow.Controls.Add(CreateButton("取消", DialogResult.Cancel, false));
                    buttonFlow.Controls.Add(CreateButton("重试", DialogResult.Retry, true));
                    cancelResult = DialogResult.Cancel;
                    AcceptButton = buttonFlow.Controls[1] as Button;
                    break;
                default:
                    var ok = CreateButton("确定", DialogResult.OK, true);
                    buttonFlow.Controls.Add(ok);
                    AcceptButton = ok;
                    break;
            }
            if (cancelResult != null)
            {
                foreach (Control control in buttonFlow.Controls)
                {
                    if (control is Button button && button.DialogResult == cancelResult)
                    {
                        CancelButton = button;
                        break;
                    }
                }
            }
            footer.Controls.Add(buttonFlow);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);

            // 让内容区避开标题栏和底部按钮栏。
            content.BringToFront();
            Controls.SetChildIndex(content, 0);
            Controls.SetChildIndex(footer, 1);
            Controls.SetChildIndex(header, 2);
        }

        private static Button CreateButton(string text, DialogResult result, bool primary)
        {
            var button = primary
                ? XShellTheme.PrimaryButton(text, 88, 30)
                : XShellTheme.FlatButton(text, 88, 30);
            button.DialogResult = result;
            button.Margin = new Padding(6, 0, 0, 0);
            return button;
        }
    }
}
