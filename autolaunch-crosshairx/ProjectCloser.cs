using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace autolaunch_crosshairx
{

    public class ProgramCloser
    {
        private Process appToClose;

        public ProgramCloser(Process appToClose)
        {
            this.appToClose = appToClose;
        }
        public void ShutdownProcess()
        {
            try
            {
                if (appToClose == null || appToClose.HasExited)
                    return;

                if (!appToClose.CloseMainWindow())
                {
                    if (!appToClose.WaitForExit(5000))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"failed to shutdown {appToClose.ProcessName}, proceeding to force close");
                        appToClose.Kill();
                    }
                }
            }

            catch (Exception ex)
            {
                Console.WriteLine($"failed to exit app {appToClose.ProcessName}: {ex.Message}");
            }
            finally
            {
                appToClose?.Dispose();
            }
        }
    }
}
