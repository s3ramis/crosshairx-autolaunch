using System.Diagnostics;

namespace autolaunch_crosshairx
{
    public static class AutolaunchApp
    {

        private static NotifyIcon? trayIcon;
        private static LogViewerForm? logViewerForm;
        private static ManualResetEventSlim _waitForStart = new ManualResetEventSlim(true);

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
            Thread watcherThread = new(() => WatchForProcesses(config))
            {
                IsBackground = true
            };
            watcherThread.Start();
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
            trayIcon!.Visible = false;
            Environment.Exit(0);
        }

        private static ConfigData? LoadConfiguration()
        {
            string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "programs.cfg");
            var configLoader = new ConfigReader(configFile);

            if (!configLoader.IsLoaded)
            {
                return null;
            }

            return new ConfigData
            {
                ProcessToOpen = configLoader.GetAppToOpen(),
                ProcessesToWatch = configLoader.GetAppsToWatch()
            };
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

        private static void WatchForProcesses(ConfigData config)
        {
            string? processToOpenName = Path.GetFileNameWithoutExtension(config.ProcessToOpen);

            Logger.Instance.Log("watching for apps to start...");

            while (true)
            {
                _waitForStart.Wait();

                bool isAnyProcessToBeWatchedRunning = false;
                bool isProcessToBeOpenedRunning = Process.GetProcessesByName(processToOpenName).Length > 0;

                // look for programs to be watched
                foreach (var processToWatch in config.ProcessesToWatch)
                {
                    string processName = Path.GetFileNameWithoutExtension(processToWatch);
                    var runningProcesses = Process.GetProcessesByName(processName);

                    if (runningProcesses.Length > 0)
                    {
                        isAnyProcessToBeWatchedRunning = true;
                        if (!isProcessToBeOpenedRunning)
                        {
                            Logger.Instance.Log($"detected {processName}");
                        }
                        break;
                    }
                }

                if (isAnyProcessToBeWatchedRunning)
                {
                    if (!isProcessToBeOpenedRunning)
                    {
                        // any one apptobewatched is running but apptoopen isnt -> open apptoopen
                        Logger.Instance.Log($"starting {processToOpenName}");
                        try
                        {
                            using (Process process = new Process())
                            {
                                process.StartInfo.FileName = config.ProcessToOpen;
                                isProcessToBeOpenedRunning = process.Start();
                            }
                        }

                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"failed to open app: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (isProcessToBeOpenedRunning)
                        try
                        {
                            // no apptobewatched running -> close apptoopen
                            Logger.Instance.Log("no app to be watched is running ");
                            Logger.Instance.Log($"closing {processToOpenName}");

                            var processesToBeClosed = Process.GetProcessesByName(processToOpenName);
                            foreach (Process p in processesToBeClosed)
                            {
                                ProcessCloser closer = new ProcessCloser(p);
                                closer.ShutdownProcess();
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // if any process unexpectedly exited before process closer could close it
                        }
                }
                Thread.Sleep(5000);
            }
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

        // helper class for config data
        private class ConfigData
        {
            public List<string> ProcessesToWatch { get; set; } = new();
            public string? ProcessToOpen { get; set; }
        }
    }
}