using System.Text;
using System.Windows.Forms;

namespace AutolaunchApp.Logging;

public class LogViewerForm : Form
{
    private readonly string _logFilePath;

    private RichTextBox _outputBox = null!;
    private TextBox _inputBox = null!;
    private TextBox _promptBox = null!;

    private FileSystemWatcher? _watcher;
    private System.Windows.Forms.Timer? _debounceTimer;

    // remember how much of logfile was processed at the last access, to prevent unneccessary re-reading
    private long _lastReadOffset = 0;

    // remember recent commands for arrow up/down functionality
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string _draftBeforeHistory = "";

    private const int MaxOutputChars = 300_000;

    public event EventHandler<string>? CommandEntered;

    public LogViewerForm(string logFilePath)
    {
        _logFilePath = logFilePath;

        InitializeForm();
        BuildUi();

        LoadInitialLog();
        StartWatching();

        Shown += (_, _) => _inputBox.Focus();
    }

    private void InitializeForm()
    {
        Text = "autolaunch_app";
        Width = 900;
        Height = 600;

        BackColor = Color.Black;
        ForeColor = Color.White;

        KeyPreview = true;

        // always direct user input into the input box
        KeyPress += LogViewerForm_KeyPress;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.Black,
            Padding = new Padding(6)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        _outputBox = CreateOutputBox();
        _inputBox = CreateInputBox();
        _promptBox = CreatePromptLabel();

        var cmdPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Padding = new Padding(0),
        };

        _promptBox.Dock = DockStyle.Left;
        _inputBox.Dock = DockStyle.Fill;

        cmdPanel.Controls.Add(_inputBox);
        cmdPanel.Controls.Add(_promptBox);

        root.Controls.Add(_outputBox, 0, 0);
        root.Controls.Add(cmdPanel, 0, 1);

        Controls.Add(root);

        _inputBox.KeyDown += InputBox_KeyDown;

        // build right click menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, (_, _) => _outputBox.Copy());
        menu.Items.Add("Select All", null, (_, _) => _outputBox.SelectAll());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Clear Screen (cls)", null, (_, _) => ClearScreen());
        _outputBox.ContextMenuStrip = menu;
    }

    private static RichTextBox CreateOutputBox()
    {
        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Consolas", 11, FontStyle.Regular),

            DetectUrls = false,
            HideSelection = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            ShortcutsEnabled = true,
            TabStop = true
        };
        return rtb;
    }

    private static TextBox CreatePromptLabel()
    {
        // cmd-style autolaunch> ... label in front of inputbox
        var tb = new TextBox
        {
            Multiline = false,
            ReadOnly = true,
            TabStop = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            Text = "autolaunch> ",
            ShortcutsEnabled = false,
            Cursor = Cursors.Arrow,
            Margin = new Padding(0),
        };

        //tb.Width = TextRenderer.MeasureText(tb.Text, tb.Font).Width + 6;

        return tb;
    }

    private static TextBox CreateInputBox()
    {
        return new TextBox
        {
            Multiline = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Consolas", 11, FontStyle.Regular),

            // textbox recognizes ctrl + c, ctrl + v etc hotkeys
            ShortcutsEnabled = true
        };
    }

    private void LogViewerForm_KeyPress(object? sender, KeyPressEventArgs e)
    {
        // redirect user input to input box
        if (_inputBox.Focused) return;

        if (!char.IsControl(e.KeyChar))
        {
            _inputBox.Focus();
            _inputBox.AppendText(e.KeyChar.ToString());
            e.Handled = true;
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // execute command on enter
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;

            var cmd = _inputBox.Text.Trim();
            _inputBox.Clear();

            if (string.IsNullOrWhiteSpace(cmd))
                return;

            // textbox control commands cant be handled by commandutil
            if (cmd.Equals("cls", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                ClearScreen();
                return;
            }

            AppendOutput($"{_promptBox.Text}{cmd}{Environment.NewLine}");

            AddToHistory(cmd);

            CommandEntered?.Invoke(this, cmd);
            return;
        }

        // clear input box with escape
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            _inputBox.Clear();
            _historyIndex = _history.Count;
            return;
        }

        // move through command history
        if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            HistoryUp();
            return;
        }

        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            HistoryDown();
            return;
        }
    }

    private void AddToHistory(string cmd)
    {
        // avoid double consecutive entry
        if (_history.Count == 0 || !_history[^1].Equals(cmd, StringComparison.OrdinalIgnoreCase))
            _history.Add(cmd);

        _historyIndex = _history.Count;
        _draftBeforeHistory = "";
    }

    private void HistoryUp()
    {
        if (_history.Count == 0) return;

        if (_historyIndex == _history.Count)
            _draftBeforeHistory = _inputBox.Text;

        _historyIndex = Math.Max(0, _historyIndex - 1);
        _inputBox.Text = _history[_historyIndex];
        _inputBox.SelectionStart = _inputBox.TextLength;
    }

    private void HistoryDown()
    {
        if (_history.Count == 0) return;

        _historyIndex = Math.Min(_history.Count, _historyIndex + 1);

        if (_historyIndex == _history.Count)
        {
            _inputBox.Text = _draftBeforeHistory;
        }
        else
        {
            _inputBox.Text = _history[_historyIndex];
        }

        _inputBox.SelectionStart = _inputBox.TextLength;
    }

    private void ClearScreen()
    {
        _outputBox.Clear();
        AppendOutput("----- screen cleared -----" + Environment.NewLine);
    }

    private void LoadInitialLog()
    {
        if (!File.Exists(_logFilePath))
        {
            AppendOutput("no log file found" + Environment.NewLine);
            _lastReadOffset = 0;
            return;
        }

        try
        {
            using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var text = sr.ReadToEnd();
            _outputBox.Text = text.TrimEnd('\r', '\n') + Environment.NewLine;

            _lastReadOffset = fs.Length;
            ScrollToBottom();
        }
        catch (Exception ex)
        {
            AppendOutput($"failed to read log: {ex.Message}{Environment.NewLine}");
        }
    }

    private void StartWatching()
    {
        var dir = Path.GetDirectoryName(_logFilePath);
        var file = Path.GetFileName(_logFilePath);

        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(file))
            return;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer!.Stop();
            AppendNewLogTail();
        };

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += (_, _) => DebounceTailRead();
        _watcher.Created += (_, _) => DebounceTailRead();
        _watcher.Renamed += (_, _) => DebounceTailRead();

        _watcher.EnableRaisingEvents = true;
    }

    private void DebounceTailRead()
    {
        // dont re-read log all the time if entry-spam in logfile
        if (IsDisposed) return;

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_debounceTimer == null) return;
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }));
        }
        catch
        {
            // egal
        }
    }

    private void AppendNewLogTail()
    {
        if (!File.Exists(_logFilePath))
            return;

        try
        {
            using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // reset if log broken
            if (fs.Length < _lastReadOffset)
            {
                _outputBox.Clear();
                _lastReadOffset = 0;
            }

            fs.Seek(_lastReadOffset, SeekOrigin.Begin);

            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var newText = sr.ReadToEnd();

            _lastReadOffset = fs.Position;

            if (string.IsNullOrEmpty(newText))
                return;

            AppendOutput(newText);
        }
        catch (IOException)
        {
            // writing might still hold file hostage, just wait for next change
        }
        catch (Exception ex)
        {
            AppendOutput($"[log tail error] {ex.Message}{Environment.NewLine}");
        }
    }

    private void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        bool follow = IsScrolledToBottom();

        _outputBox.SuspendLayout();

        _outputBox.AppendText(text);

        // dont flood memory
        TrimOutputIfNeeded();

        if (follow)
            ScrollToBottom();

        _outputBox.ResumeLayout();
    }

    private void TrimOutputIfNeeded()
    {
        if (_outputBox.TextLength <= MaxOutputChars)
            return;

        int remove = _outputBox.TextLength - MaxOutputChars;

        _outputBox.Select(0, remove);
        _outputBox.SelectedText = "";
    }

    private bool IsScrolledToBottom()
    {
        int lastVisibleChar = _outputBox.GetCharIndexFromPosition(new Point(1, _outputBox.ClientSize.Height - 1));
        int lastVisibleLine = _outputBox.GetLineFromCharIndex(lastVisibleChar);

        int lastChar = Math.Max(0, _outputBox.TextLength - 1);
        int lastLine = _outputBox.GetLineFromCharIndex(lastChar);

        return lastVisibleLine >= lastLine - 1;
    }

    private void ScrollToBottom()
    {
        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.ScrollToCaret();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        try { _watcher?.Dispose(); } catch { }
        try { _debounceTimer?.Stop(); _debounceTimer?.Dispose(); } catch { }
    }
}