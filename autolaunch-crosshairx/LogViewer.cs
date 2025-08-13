public class LogViewerForm : Form
{
    private readonly TextBox logTextBox;
    private readonly string _logFilePath;
    private FileSystemWatcher logWatcher = null!;

    public LogViewerForm(string logFilePath)
    {
        _logFilePath = logFilePath;
        Text = "Log Viewer";
        Width = 750;
        Height = 500;
        BackColor = Color.Black;

        logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 12, FontStyle.Regular),
            BackColor = Color.Black,
            ForeColor = Color.LightGreen,
            BorderStyle = BorderStyle.None,
            WordWrap = false
        };

        Controls.Add(logTextBox);

        LoadLog();

        WatchLogFile();
    }

    private void LoadLog()
    {
        if (File.Exists(_logFilePath))
        {
            logTextBox.Text = File.ReadAllText(_logFilePath);
            ScrollToBottom();
        }
        else
        {
            logTextBox.Text = "No log file found.";
        }
    }

    private void ScrollToBottom()
    {
        logTextBox.SelectionStart = logTextBox.Text.Length;
        logTextBox.ScrollToCaret();
    }

    private void WatchLogFile()
    {
        var dir = Path.GetDirectoryName(_logFilePath);
        var file = Path.GetFileName(_logFilePath);

        logWatcher = new FileSystemWatcher(dir!, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        logWatcher.Changed += (s, e) =>
        {
            try
            {
                this.Invoke(new Action(LoadLog));
            }
            catch { }
        };
        logWatcher.EnableRaisingEvents = true;
    }
}
