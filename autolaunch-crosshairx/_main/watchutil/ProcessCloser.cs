using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AutolaunchApp.Logging;

namespace AutolaunchApp
{
    // handles shutdown for a specified process
    public class ProcessCloser(Process processToClose)
    {
        private readonly Process _processToClose = processToClose ?? throw new ArgumentNullException(nameof(processToClose));

        public void ShutdownProcess()
        {
            try
            {
                if (!TryCloseProcess(_processToClose))
                    Logger.Instance.Log($"failed to shutdown {_processToClose.ProcessName}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"closing process failed entirely: {ex.Message}");
            }
            finally
            {
                try { _processToClose.Dispose(); } catch { }
            }
        }

        private static bool TryCloseProcess(Process process)
        {
            if (process == null) return true;

            try
            {
                process.Refresh();
                if (process.HasExited) return true;

                int pid = -1;
                string name = "unknown";

                try { pid = process.Id; } catch { }
                try { name = process.ProcessName; } catch { }

                bool hasWindow = false;
                try
                {
                    process.Refresh();
                    hasWindow = process.MainWindowHandle != IntPtr.Zero;
                }
                catch { }

                bool closeRequested = false;

                // soft close only makes sense if there is a window present
                if (hasWindow)
                {
                    try
                    {
                        closeRequested = process.CloseMainWindow();
                        Logger.Instance.Log($"requested close for {name}");
                    }
                    catch
                    {
                        // catch error so we can proceed to force close if something goes wrong
                    }
                }

                if (process.WaitForExit(5000))
                    return true;

                process.Refresh();
                if (process.HasExited)
                    return true;

                // 
                Logger.Instance.Log($"soft shutdown timed out, {name} will be killed");

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    process.Kill();
                }

                // wirklich prüfen, ob er weg ist
                if (process.WaitForExit(5000))
                    return true;

                process.Refresh();
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // dont care if crashed -> should be closed anyways
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"closing process failed: {ex.Message}");
                return false;
            }
        }
    }
}
