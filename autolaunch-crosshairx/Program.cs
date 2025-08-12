using System;
using System.Data;
using System.Diagnostics;
using System.Threading;


namespace autolaunch_crosshairx
{
    class Program
    {
        static void Main(string[] args)
        {
            string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "programs.cfg");
            var configLoader = new ConfigReader(configFile);
            if (!configLoader.IsLoaded)
            {
                Console.WriteLine("closing application...");
                return;
            }
            var processesToWatchPaths = configLoader.GetAppsToWatch();
            string? processToOpenPath = configLoader.GetAppToOpen();
            string? processToOpenName = Path.GetFileNameWithoutExtension(processToOpenPath);

            Console.WriteLine("watching for apps to start...");
            Console.WriteLine();

            while (true)
            {
                bool isAnyProcessToBeWatchedRunning = false;

                foreach (var processToWatch in processesToWatchPaths)
                {
                    string processName = Path.GetFileNameWithoutExtension(processToWatch);
                    var runningProcesses = Process.GetProcessesByName(processName);
                    string lastMessage = "";

                    if (runningProcesses.Length > 0)
                    {
                        lastMessage = logMessage($"detected {processName}", lastMessage);
                        isAnyProcessToBeWatchedRunning = true;
                        break;
                    }
                }

                var openProcesses = Process.GetProcessesByName(processToOpenName);

                if (isAnyProcessToBeWatchedRunning)
                {
                    if (openProcesses.Length == 0)
                    {
                        Console.WriteLine($"starting {processToOpenName}");
                        try
                        {
                            Process.Start(processToOpenPath!);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"failed to open app: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (openProcesses.Length > 0)
                    {
                        Console.WriteLine($"closing {processToOpenName}");
                        foreach (var proc in openProcesses)
                        {
                            ProgramCloser closer = new ProgramCloser(proc);
                            closer.ShutdownProcess();
                        }
                    }
                }
                Thread.Sleep(30000);
            }
        }
        static string logMessage(string message, string lastMessage)
        {
            if (message != lastMessage)
            {
                Console.WriteLine(message);
                return message;
            }
            else return lastMessage;
        }
    }
}