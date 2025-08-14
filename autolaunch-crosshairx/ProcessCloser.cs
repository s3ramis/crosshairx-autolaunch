using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace autolaunch_crosshairx
{

    public class ProcessCloser
    {
        private readonly Process _processToClose;

        public ProcessCloser(Process processToClose)
        {
            _processToClose = processToClose ?? throw new ArgumentNullException(nameof(processToClose));
        }

        public void ShutdownProcess()
        {
            try
            {
                if (!TryCloseProcess(_processToClose))
                {
                    Logger.Instance.Log($"failed to shutdown {_processToClose.ProcessName}");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"closing process {_processToClose.ProcessName} has failed entirely");
                Logger.Instance.Log(ex.Message);
            }
            finally
            {
                _processToClose?.Dispose();
            }
        }

        private bool TryCloseProcess(Process process)
        {
            if (process == null || process.HasExited)
            {
                return true;
            }

            try
            {
                if (process.CloseMainWindow())
                {
                    if (process.WaitForExit(5000))
                    {
                        return true;
                    }
                }
                Logger.Instance.Log($"graceful shutdown failed, proceeding to force close {process.ProcessName}");
                process.Kill();
                process.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"closing process {process.ProcessName} has failed");
                Logger.Instance.Log(ex.Message);
                return false;
            }
        }
    }
}
