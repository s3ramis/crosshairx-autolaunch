using System.Diagnostics;

namespace autolaunch_app;

public sealed partial class AppWatcher
{
    public void ResyncNow()
    {
        if (_disposed) return;

        lock (_actionLock)
        {
            _launching.Clear();
            _closing.Clear();

            // reset open processes
            foreach (var n in _relevantNames)
                _counts[n] = 0;

            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return; }

            foreach (var p in procs)
            {
                try
                {
                    var name = p.ProcessName;

                    // is a relevant process already open?
                    if (_relevantNames.Contains(name))
                    {
                        // add relevant process to tracking dictionary
                        _counts.AddOrUpdate(name, 1, (_, old) => old + 1);
                    }
                }
                catch
                {
                    // race/permissions
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            // check all watch processes for each open groups
            foreach (var g in _segments)
                EvaluateGroup(g);
        }
    }
}