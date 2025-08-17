using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;

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

            Logger.Instance.Log("watching for apps to start...");

            while (true)
            {
                _waitForStart.Wait();

                bool isAnyProcessToBeWatchedRunning = false;
                bool isProcessToBeOpenedRunning = Process.GetProcessesByName(processToOpenName).Length > 0;

                foreach (var processToWatch in processesToWatchPaths)
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
                        Logger.Instance.Log($"starting {processToOpenName}");
                        try
                        {
                            using (Process process = new Process())
                            {
                                process.StartInfo.FileName = processToOpenPath;
                                isProcessToBeOpenedRunning = process.Start();
                            }                         
                        }

                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"failed to open app: {ex.Message}");
                            isProcessToBeOpenedRunning = false;
                        }
                    }
                }
                else
                {
                    if (isProcessToBeOpenedRunning)
                        try
                        {
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
                        }
                        finally
                        {
                            
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