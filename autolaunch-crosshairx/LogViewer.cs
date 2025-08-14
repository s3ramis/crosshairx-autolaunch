namespace autolaunch_crosshairx
{
    public class LogViewerForm : Form
    {
        private readonly TextBox logTextBox;
        private readonly TextBox inputTextBox;
        private readonly string _logFilePath;
        private FileSystemWatcher logWatcher = null!;

        public LogViewerForm(string logFilePath)
        {
            _logFilePath = logFilePath;
            Text = "Log Viewer";
            Width = 750;
            Height = 500;
            BackColor = Color.Black;

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            logTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 12, FontStyle.Regular),
                BackColor = Color.Black,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };

            inputTextBox = new TextBox
            {
                Multiline = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 12, FontStyle.Regular),
                BackColor = Color.Black,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            logTextBox.Enter += (s, e) => inputTextBox.Focus();
            inputTextBox.KeyDown += InputTextBox_KeyDown;

            mainPanel.Controls.Add(logTextBox, 0, 0);
            mainPanel.Controls.Add(inputTextBox, 0, 1);

            Controls.Add(mainPanel);

            LoadLog();
            WatchLogFile();
        }

        private void LoadLog()
        {
            if (File.Exists(_logFilePath))
            {
                logTextBox.Text = File.ReadAllText(_logFilePath).TrimEnd('\r', '\n');
                ScrollToBottom();
            }
            else
            {
                logTextBox.Text = "No log file found.";
            }
        }

        private void ReloadLog()
        {
            if (!File.Exists(_logFilePath)) return;

            string logText = File.ReadAllText(_logFilePath);
            logTextBox.Text = logText;
            ScrollToBottom();
        }


        private void InputTextBox_KeyDown(object? sender, KeyEventArgs kea)
        {
            if (kea.KeyCode == Keys.Enter)
            {
                kea.SuppressKeyPress = true;
                string command = inputTextBox.Text.Trim();
                inputTextBox.Clear();

                if (!string.IsNullOrEmpty(command))
                    CommandEntered?.Invoke(this, command);
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
                    Invoke(new Action(ReloadLog));
                }
                catch { }
            };
            logWatcher.EnableRaisingEvents = true;
        }

        public EventHandler<string>? CommandEntered;
    }
}