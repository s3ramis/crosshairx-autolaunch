using System.Diagnostics;

namespace autolaunch_crosshairx
{
    internal static class AutolaunchApp
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

            Thread watcherThread = new(WatchForProcesses)
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

        private static void WatchForProcesses()
        {
            string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "programs.cfg");
            var configLoader = new ConfigReader(configFile);
            if (!configLoader.IsLoaded)
            {
                Logger.Instance.Log("closing application...");
                using (var viewer = new LogViewerForm(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "autolaunchapp.log")))
                {
                    viewer.ShowDialog();
                }
                Environment.Exit(1);
            }
            var processesToWatchPaths = configLoader.GetAppsToWatch();
            string? processToOpenPath = configLoader.GetAppToOpen();
            string? processToOpenName = Path.GetFileNameWithoutExtension(processToOpenPath);
            Process? startedProcess = null;

            Logger.Instance.Log("watching for apps to start...");

            while (true)
            {
                _waitForStart.Wait();

                bool isAnyProcessToBeWatchedRunning = false;

                foreach (var processToWatch in processesToWatchPaths)
                {
                    string processName = Path.GetFileNameWithoutExtension(processToWatch);
                    var runningProcesses = Process.GetProcessesByName(processName);

                    if (runningProcesses.Length > 0)
                    {
                        isAnyProcessToBeWatchedRunning = true;
                        if (startedProcess == null || startedProcess.HasExited)
                        {
                            Logger.Instance.Log($"detected {processName}");
                        }
                        break;
                    }
                }

                if (isAnyProcessToBeWatchedRunning)
                {
                    if (startedProcess == null || startedProcess.HasExited)
                    {
                        Logger.Instance.Log($"starting {processToOpenName}");
                        try
                        {
                            startedProcess = Process.Start(processToOpenPath!);
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"failed to open app: {ex.Message}");
                            startedProcess = null;
                        }
                    }
                }
                else
                {
                    if (startedProcess != null)
                        try
                        {
                            startedProcess.Refresh();
                            if (!startedProcess.HasExited)
                            {
                                Logger.Instance.Log("no app to be watched is running ");
                                Logger.Instance.Log($"closing {processToOpenName}");
                                ProcessCloser closer = new ProcessCloser(startedProcess);
                                closer.ShutdownProcess();
                            }
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        finally
                        {
                            startedProcess = null;
                        }
                }
                Thread.Sleep(5000);
            }
        }

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
    }
}