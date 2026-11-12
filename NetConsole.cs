using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 网络测试通用命令行控制台：上方输出区 + 底部命令输入行，支持历史记录与终端退格。
/// </summary>
public sealed class NetConsole : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(9, 14, 26);
    private static readonly Color BarColor = Color.FromArgb(17, 24, 39);
    private static readonly Color BorderColor = Color.FromArgb(30, 41, 59);
    private static readonly Color TextColor = Color.FromArgb(209, 213, 219);
    private static readonly Color MutedColor = Color.FromArgb(148, 163, 184);
    private static readonly Color CommandColor = Color.FromArgb(103, 232, 249);
    private static readonly Color SuccessColor = Color.FromArgb(74, 222, 128);
    private static readonly Color ErrorColor = Color.FromArgb(248, 113, 113);
    private static readonly Color WarningColor = Color.FromArgb(251, 191, 36);

    private readonly RichTextBox _output;
    private readonly ContextMenuStrip _outputMenu;
    private readonly ToolStripMenuItem _copySelectionItem;
    private readonly ToolStripMenuItem _copyAllItem;
    private readonly Panel _bar;
    private readonly Label _prompt;
    private readonly TextBox _input;
    private readonly Font _monoFont;
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string _promptText = "> ";
    private bool _disposed;

    public event Action<string>? CommandSubmitted;
    public event EventHandler? CtrlCloseBracketPressed;

    public NetConsole()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = BorderColor;
        Padding = new Padding(1);
        DoubleBuffered = true;

        _monoFont = new Font("Consolas", 10f, FontStyle.Regular);

        _output = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = BackgroundColor,
            ForeColor = TextColor,
            Font = _monoFont,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            DetectUrls = false,
            HideSelection = false,
            MaxLength = 4_000_000,
            TabStop = false
        };

        _copySelectionItem = new ToolStripMenuItem("复制选中内容");
        _copySelectionItem.ShortcutKeyDisplayString = "Ctrl+C";
        _copySelectionItem.Click += (_, _) => CopySelection();
        _copyAllItem = new ToolStripMenuItem("复制全部");
        _copyAllItem.Click += (_, _) => CopyAll();
        var selectAllItem = new ToolStripMenuItem("全选");
        selectAllItem.ShortcutKeyDisplayString = "Ctrl+A";
        selectAllItem.Click += (_, _) =>
        {
            _output.Focus();
            _output.SelectAll();
        };
        var clearItem = new ToolStripMenuItem("清屏");
        clearItem.Click += (_, _) => ClearOutput();
        _outputMenu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        _outputMenu.Items.AddRange(new ToolStripItem[]
        {
            _copySelectionItem, _copyAllItem, new ToolStripSeparator(), selectAllItem, clearItem
        });
        _outputMenu.Opening += (_, _) =>
        {
            _copySelectionItem.Enabled = _output.SelectionLength > 0;
            _copyAllItem.Enabled = _output.TextLength > 0;
            selectAllItem.Enabled = _output.TextLength > 0;
            clearItem.Enabled = _output.TextLength > 0;
        };
        _output.ContextMenuStrip = _outputMenu;
        Controls.Add(_output);

        _bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = BarColor
        };
        _bar.Resize += (_, _) => LayoutInputBar();
        Controls.Add(_bar);

        _prompt = new Label
        {
            AutoSize = false,
            BackColor = BarColor,
            ForeColor = CommandColor,
            Font = _monoFont,
            Text = _promptText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _bar.Controls.Add(_prompt);

        _input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = BarColor,
            ForeColor = Color.FromArgb(249, 250, 251),
            Font = new Font("Consolas", 10.5f, FontStyle.Regular),
            TabStop = true
        };
        _input.KeyDown += InputKeyDown;
        _bar.Controls.Add(_input);
        _output.MouseClick += (_, e) =>
        {
            // 选中文本后保留输出区焦点，方便 Ctrl+C 或右键复制；空白处单击才回到输入行。
            if (e.Button == MouseButtons.Left && _output.SelectionLength == 0)
            {
                _input.Focus();
            }
        };

        _bar.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, 0, _bar.Width, 0);
        };

        LayoutInputBar();
    }

    public string PromptText
    {
        get => _promptText;
        set
        {
            _promptText = value ?? "> ";
            _prompt.Text = _promptText;
            LayoutInputBar();
        }
    }

    public void FocusInput()
    {
        _input.Focus();
    }

    public void SetInputText(string text)
    {
        _input.Text = text ?? "";
        _input.SelectionStart = _input.TextLength;
        _input.Focus();
    }

    public void Write(string text, Color? color = null)
    {
        AppendText(text, color ?? TextColor, newline: false);
    }

    public void WriteLine(string text, Color? color = null)
    {
        AppendText(text, color ?? TextColor, newline: true);
    }

    public void WriteCommand(string command)
    {
        AppendText("> " + command, CommandColor, newline: true);
    }

    public void WriteInfo(string text)
    {
        AppendText(text, MutedColor, newline: true);
    }

    public void WriteSuccess(string text)
    {
        AppendText(text, SuccessColor, newline: true);
    }

    public void WriteWarning(string text)
    {
        AppendText(text, WarningColor, newline: true);
    }

    public void WriteError(string text)
    {
        AppendText(text, ErrorColor, newline: true);
    }

    public void ClearOutput()
    {
        RunOnUi(() => _output.Clear());
    }

    private void CopySelection()
    {
        if (_output.SelectionLength <= 0)
        {
            return;
        }
        try
        {
            Clipboard.SetText(_output.SelectedText);
        }
        catch
        {
            // 剪贴板被其它程序占用时静默忽略
        }
    }

    private void CopyAll()
    {
        if (_output.TextLength <= 0)
        {
            return;
        }
        try
        {
            Clipboard.SetText(_output.Text);
        }
        catch
        {
            // 剪贴板被其它程序占用时静默忽略
        }
    }

    /// <summary>追加终端原始文本：支持退格字符（\b），其余字符按原样显示。</summary>
    public void AppendTerminal(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        RunOnUi(() =>
        {
            var chunk = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '\b')
                {
                    FlushChunk(chunk);
                    if (_output.TextLength > 0)
                    {
                        int lastIndex = _output.TextLength - 1;
                        if (_output.Text[lastIndex] != '\n')
                        {
                            _output.Select(lastIndex, 1);
                            _output.SelectedText = "";
                        }
                    }
                    continue;
                }

                chunk.Append(c);
                if (chunk.Length >= 8192)
                {
                    FlushChunk(chunk);
                }
            }
            FlushChunk(chunk);
            ScrollToEnd();
        });
    }

    private void FlushChunk(StringBuilder chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }
        string text = chunk.ToString();
        chunk.Clear();
        AppendPlainText(text, TextColor, newline: false);
    }

    private void AppendText(string text, Color color, bool newline)
    {
        if (string.IsNullOrEmpty(text) && !newline)
        {
            return;
        }
        RunOnUi(() =>
        {
            AppendPlainText(text + (newline ? "\n" : ""), color, newline: false);
            ScrollToEnd();
        });
    }

    private void AppendPlainText(string text, Color color, bool newline)
    {
        if (_output.TextLength > 2_500_000)
        {
            _output.Select(0, 500_000);
            _output.SelectedText = "";
        }

        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;
        _output.SelectionColor = color;
        _output.AppendText(text + (newline ? "\n" : ""));
        _output.SelectionColor = TextColor;
    }

    private void ScrollToEnd()
    {
        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;
        _output.ScrollToCaret();
    }

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Submit();
            return;
        }
        if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            MoveHistory(-1);
            return;
        }
        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            MoveHistory(1);
            return;
        }
        if (e.Control && e.KeyCode == Keys.L)
        {
            e.SuppressKeyPress = true;
            ClearOutput();
            return;
        }
        if (e.Control && (e.KeyCode == Keys.OemCloseBrackets || e.KeyCode == Keys.Oem6))
        {
            e.SuppressKeyPress = true;
            CtrlCloseBracketPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Submit()
    {
        string text = _input.Text;
        _input.Clear();

        if (!string.IsNullOrEmpty(text))
        {
            if (_history.Count == 0 || !string.Equals(_history[^1], text, StringComparison.Ordinal))
            {
                _history.Add(text);
            }
            if (_history.Count > 200)
            {
                _history.RemoveAt(0);
            }
        }
        _historyIndex = -1;

        CommandSubmitted?.Invoke(text);
    }

    private void MoveHistory(int direction)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex == -1)
        {
            _historyIndex = direction < 0 ? _history.Count - 1 : _history.Count;
        }
        else
        {
            _historyIndex += direction;
        }

        if (_historyIndex < 0)
        {
            _historyIndex = 0;
        }
        if (_historyIndex >= _history.Count)
        {
            _historyIndex = _history.Count;
            _input.Clear();
            return;
        }

        _input.Text = _history[_historyIndex];
        _input.SelectionStart = _input.TextLength;
    }

    private void LayoutInputBar()
    {
        if (_bar == null || _prompt == null || _input == null)
        {
            return;
        }

        int promptWidth = TextRenderer.MeasureText(_prompt.Text, _prompt.Font).Width + 8;
        promptWidth = Math.Clamp(promptWidth, 28, Math.Max(28, _bar.Width / 2));
        int left = 12;
        int inputHeight = 24;
        int inputY = Math.Max(0, (_bar.Height - inputHeight) / 2);

        _prompt.SetBounds(left, 0, promptWidth, _bar.Height);
        _input.SetBounds(left + promptWidth, inputY, Math.Max(60, _bar.Width - left - promptWidth - 12), inputHeight);
    }

    private void RunOnUi(Action action)
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch (ObjectDisposedException)
            {
                // 窗口已释放
            }
            catch (InvalidOperationException)
            {
                // 窗口正在关闭
            }
            return;
        }

        action();
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing)
        {
            _outputMenu.Dispose();
            _monoFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
