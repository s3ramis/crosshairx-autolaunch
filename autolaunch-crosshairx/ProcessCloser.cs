using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace autolaunch_crosshairx
{

    public class ProcessCloser(Process appToClose)
    {
        private readonly Process appToClose = appToClose;

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
                       
                        Logger.Instance.Log($"failed to shutdown {appToClose.ProcessName}, proceeding to force close");
                        appToClose.Kill();
                    }
                }
            }

            catch (Exception ex)
            {
                Logger.Instance.Log($"failed to exit app {appToClose.ProcessName}: {ex.Message}");
            }
            finally
            {
                appToClose?.Dispose();
            }
        }
    }
}
