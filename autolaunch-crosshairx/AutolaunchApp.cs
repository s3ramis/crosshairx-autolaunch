using System.Diagnostics;

namespace autolaunch_app
{
    public static class AutolaunchApp
    {

        private static NotifyIcon? trayIcon;
        private static LogViewerForm? logViewerForm;
        private static readonly ManualResetEventSlim _waitForStart = new(true);

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

        private static void WatchForProcesses(ConfigData config)
        {
            // single out config file into watchapp-openapp relations
            var rules = SingleOutRules(config).ToList();
            if (rules.Count == 0)
            {
                Logger.Instance.Log("no valid rules found in config, watcher stopped");
                return;
            }

            var openGroups = rules
                .GroupBy(rule => rule.OpenPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.Instance.Log("watching for apps to start...");

            // main loop
             while (true)
            {
                _waitForStart.Wait();

                foreach (var openGroup in openGroups)
                {
                    string openPath = openGroup.Key;
                    string? openName = SaveProcessNameFromPath(openPath);

                    if (openName == null)
                    {
                        Logger.Instance.Log($"invalid open path in config: '{openPath}'");
                        continue;
                    }

                    bool isOpenRunning = Process.GetProcessesByName(openName).Length > 0;

                    bool shouldBeOpen = false;
                    string? triggerInfo = null;

                    // irgendein Watch in irgendeiner Regel dieser Open-Gruppe?
                    foreach (var rule in openGroup)
                    {
                        foreach (var watchPath in rule.WatchPaths)
                        {
                            string? watchName = SaveProcessNameFromPath(watchPath);
                            if (watchName == null)
                                continue;

                            if (Process.GetProcessesByName(watchName).Length > 0)
                            {
                                shouldBeOpen = true;
                                triggerInfo = $"{watchName} (config id: {rule.Id})";
                                break;
                            }
                        }

                        if (shouldBeOpen)
                            break;
                    }

                    if (shouldBeOpen)
                    {
                        if (!isOpenRunning)
                        {
                            if (triggerInfo != null)
                                Logger.Instance.Log($"detected {triggerInfo}");

                            Logger.Instance.Log($"starting {openName}");
                            try
                            {
                                using Process p = new();
                                var unescapedPath = openPath.Replace("\\\\", "\\");
                                p.StartInfo.FileName = unescapedPath;
                                p.Start();
                            }
                            catch (Exception ex)
                            {
                                Logger.Instance.Log($"failed to open app '{openName}': {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        if (isOpenRunning)
                        {
                            try
                            {
                                Logger.Instance.Log($"no watch app running for '{openName}'");
                                Logger.Instance.Log($"closing {openName}");

                                var processesToBeClosed = Process.GetProcessesByName(openName);
                                foreach (Process p in processesToBeClosed)
                                {
                                    ProcessCloser closer = new ProcessCloser(p);
                                    closer.ShutdownProcess();
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                // process exited unexpectedly
                            }
                        }
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

        // --- helpers for config handling --
        private sealed class SingleRule
        {
            public string Id { get; init; } = "";
            public string OpenPath { get; init; } = "";
            public List<string> WatchPaths { get; init; } = new();
        }
        private static IEnumerable<SingleRule> SingleOutRules(ConfigData config)
        {
            foreach (var dict in config.Apps)
            {
                if (dict == null) continue;
                foreach (var kvp in dict)
                {
                    string id = kvp.Key?.Trim() ?? "";
                    var rule = kvp.Value;

                    if (string.IsNullOrWhiteSpace(id) || rule == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(rule.Open) || rule.Watch == null || rule.Watch.Count == 0)
                    {
                        continue;
                    }

                    yield return new SingleRule
                    {
                        Id = id,
                        OpenPath = rule.Open,
                        WatchPaths = rule.Watch
                    };
                }
            }
        }

        private static string? SaveProcessNameFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path.Trim());
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }   
}