using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace autolaunch_crosshairx
{
    internal static partial class Program
    {

        private static NotifyIcon? trayIcon;
        private static LogViewerForm? logViewerForm;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CreateTrayIcon();

            Thread watcherThread = new Thread(WatchForProcesses)
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
                bool isAnyProcessToBeWatchedRunning = false;

                foreach (var processToWatch in processesToWatchPaths)
                {
                    string processName = Path.GetFileNameWithoutExtension(processToWatch);
                    var runningProcesses = Process.GetProcessesByName(processName);

                    if (runningProcesses.Length > 0)
                    {
                        Logger.Instance.Log($"detected {processName}");
                        isAnyProcessToBeWatchedRunning = true;
                        break;
                    }
                }

                var openProcesses = Process.GetProcessesByName(processToOpenName);

                if (isAnyProcessToBeWatchedRunning)
                {
                    if (openProcesses.Length == 0)
                    {
                        Logger.Instance.Log($"starting {processToOpenName}");
                        try
                        {
                            Process.Start(processToOpenPath!);
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"failed to open app: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (openProcesses.Length > 0)
                    {
                        Logger.Instance.Log($"closing {processToOpenName}");
                        foreach (var proc in openProcesses)
                        {
                            ProcessCloser closer = new ProcessCloser(proc);
                            closer.ShutdownProcess();
                        }
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
            }

            logViewerForm.Show();
            logViewerForm.BringToFront();
        }
    }
}