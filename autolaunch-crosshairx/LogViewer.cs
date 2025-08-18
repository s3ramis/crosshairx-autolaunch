namespace autolaunch_crosshairx
{
    public class LogViewerForm : Form
    {
        private readonly TextBox logTextBox = null!;
        private readonly TextBox inputTextBox = null!;
        private readonly string _logFilePath;
        private FileSystemWatcher logWatcher = null!;
    
        // triggered when command is entered in the input box
        public EventHandler<string>? CommandEntered;

        public LogViewerForm(string logFilePath)
        {
            _logFilePath = logFilePath;

            InitializeForm();
            InitializeControls(CreateInputTextBox(), CreateLogTextBox());

            LoadLog();
            WatchLogFile();
        }

        private void InitializeForm()
        {
            Text = "Log Viewer";
            Width = 750;
            Height = 500;
            BackColor = Color.Black;
        }

        private void InitializeControls(TextBox inputTextBox, TextBox logTextBox)
        {
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };

            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            // if logviewer is clicked -> set cursor into input box
            logTextBox.Enter += (s, e) => inputTextBox.Focus();
            inputTextBox.KeyDown += InputTextBox_KeyDown;

            mainPanel.Controls.Add(logTextBox, 0, 0);
            mainPanel.Controls.Add(inputTextBox, 0, 1);

            Controls.Add(mainPanel);
        }

        private static TextBox CreateLogTextBox()
        {
            return new TextBox
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
        }

        private static TextBox CreateInputTextBox()
        {
            return new TextBox
            {
                Multiline = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 12, FontStyle.Regular),
                BackColor = Color.Black,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None
            };
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
    }
}