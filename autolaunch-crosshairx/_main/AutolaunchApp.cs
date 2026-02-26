using System.Diagnostics;

namespace AutolaunchApp
{
    public static class AutolaunchApp
    {

        private static NotifyIcon? trayIcon;
        private static LogViewerForm? logViewerForm;
        private static readonly ManualResetEventSlim _waitForStart = new(true);
        private static AppWatcher? _appWatcher;
        private static Commands.CommandProcessor? _cmd;
        private static Config.ConfigFileService? _cfgFileService;
        private static AutostartService? _autoStart;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CreateTrayIcon();

            var config = LoadConfiguration();
            if (config == null)
            {
                ShowErrorAndExit();
                return;
            }

            // isolate watch logic in seperate thread to keep ui responsive
            _appWatcher = new AppWatcher(config, () => _waitForStart.IsSet);
            _appWatcher.Start();
            Application.Run();
        }

        private static void CreateTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "autolaunch crosshairX",
                Visible = true,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("show log", null, (_, _) => OpenLogViewer());
            menu.Items.Add("exit app", null, (_, _) => ExitApp());

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (s, e) => OpenLogViewer();
        }

        private static void ExitApp()
        {
            try
            {
                _appWatcher?.Dispose();
            }
            catch
            {
                
            }
            ;
            trayIcon!.Visible = false;
            Environment.Exit(0);
        }

        private static ConfigData? LoadConfiguration()
        {
            string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "programs.cfg");
            var configLoader = new ConfigReader(configFile);

            if (!configLoader.IsLoaded || configLoader == null)
            {
                return null;
            }

            return configLoader.Config;
        }

        private static void ShowErrorAndExit()
        {
            Logger.Instance.Log("closing application...");
            using (var viewer = new LogViewerForm(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "autolaunchapp.log")))
            {
                viewer.ShowDialog();
            }
            Environment.Exit(1);
        }

        // openlogviewer and start eventhandler for commands
        private static void OpenLogViewer()
        {
            string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "autolaunchapp.log");

            if (logViewerForm == null || logViewerForm.IsDisposed)
            {
                logViewerForm = new LogViewerForm(logFile);
                logViewerForm.CommandEntered += LogViewerForm_CommandEntered;
            }

            logViewerForm.Show();
            logViewerForm.BringToFront();
        }

        // handle commands entered in log viewer
        private static void LogViewerForm_CommandEntered(object? sender, string command)
        {
            switch (command.ToLowerInvariant())
            {
                case "stop":
                    if (!_waitForStart.IsSet)
                    {
                        Logger.Instance.Log("watcher already paused");
                    }
                    else
                    {
                        _waitForStart.Reset();
                        Logger.Instance.Log("watcher paused by user input");
                    }
                    break;

                case "start":
                    if (_waitForStart.IsSet)
                    {
                        Logger.Instance.Log("watcher already running");
                    }
                    else
                    {
                        _waitForStart.Set();
                        Logger.Instance.Log("watcher resumed by user input");
                    }
                    break;

                case "exit":
                    Logger.Instance.Log("app closed by user input");
                    Environment.Exit(1);
                    break;

                default:
                    Logger.Instance.Log($"command '{command}' not recognized");
                    break;
            }
        }

        // --- helpers for config handling --
        private sealed class ConfigSegment
        {
            public string Id { get; init; } = "";
            public string OpenPath { get; init; } = "";
            public List<string> WatchPaths { get; init; } = new();
        }
        private static IEnumerable<ConfigSegment> SplitConfig(ConfigData config)
        {
            // returns only correctly formatted config segments
            foreach (var dict in config.Apps)
            {
                if (dict == null) continue;
                foreach (var kvp in dict)
                {

                    string cfgSegmentId = kvp.Key?.Trim() ?? "";
                    var cfgSegment = kvp.Value;

                    if (string.IsNullOrWhiteSpace(cfgSegmentId) || cfgSegment == null)
                    {
                        continue;
                    }
                    //  no program to be opened || program to watch array not defined || program to watch array empty
                    if  (string.IsNullOrWhiteSpace(cfgSegment.Open) || cfgSegment.Watch == null || cfgSegment.Watch.Count == 0)
                    {
                        continue;
                    }

                    yield return new ConfigSegment
                    {
                        Id = cfgSegmentId,
                        OpenPath = cfgSegment.Open,
                        WatchPaths = cfgSegment.Watch
                    };
                }
            }
        }

        private static string? GetProcessNameFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path.Trim());
            // return null if name is emtpy else return process name,
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }   
}