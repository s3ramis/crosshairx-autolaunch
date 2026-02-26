using System;
using System.Linq;
using System.Management;
using System.Security.Principal;

namespace AutolaunchApp;

public sealed partial class AppWatcher
{
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public void Stop()
    {  
        // stop all watchers
        if (_disposed) return;

        try { _startWatcher?.Stop(); } catch { }
        try { _stopWatcher?.Stop(); } catch { }

        try { _startWatcher?.Dispose(); } catch { }
        try { _stopWatcher?.Dispose(); } catch { }

        _startWatcher = null;
        _stopWatcher = null;
        _started = false;
    }

    private bool TryStartTraceWatchers()
    {
        // trace watcher if app is running as admin
        Stop();

        try
        {
            _mode = WmiMode.Trace;
            var scope = CreateScope();

            string filter = BuildWqlFilterString("ProcessName");

            string startWqlQuery = "SELECT * FROM Win32_ProcessStartTrace";
            if (!string.IsNullOrWhiteSpace(filter))
                startWqlQuery += " WHERE " + filter;
            // subscribe to process-started events
            _startWatcher = new ManagementEventWatcher(scope, new WqlEventQuery(startWqlQuery));
            _startWatcher.EventArrived += (_, e) => HandleWmiEvent(e, started: true);
            _startWatcher.Start();

            // subscribe to process-ended events
            _stopWatcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += (_, e) => HandleWmiEvent(e, started: false);
            _stopWatcher.Start();
            return true;
        }
        catch (ManagementException mex)
        {
            Logger.Instance.Log($"Trace watcher start failed: {mex.ErrorCode} - {mex.Message}");
            Stop();
            return false;
        }
        catch (UnauthorizedAccessException uex)
        {
            Logger.Instance.Log($"Trace watcher start failed (access denied): {uex.Message}");
            Stop();
            return false;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Trace watcher start failed: {ex.Message}");
            Stop();
            return false;
        }
    }

    private bool TryStartInstanceWatchers()
    {
        // watch for instance creation events which dont need admin elevation to be subscribed to
        Stop();

        try
        {
            _mode = WmiMode.Instance;
            var scope = CreateScope();

            // only process instances
            string condition = "TargetInstance ISA 'Win32_Process'";

            // build filter for event subscriber so only events concerning apps listed in config will be paid attention to
            string filter = BuildWqlFilterString("TargetInstance.Name");
            if (!string.IsNullOrWhiteSpace(filter))
                condition += " AND " + filter;

            // subscribe to instance-of-app-started event
            var startQuery = new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(1), condition);
            _startWatcher = new ManagementEventWatcher(scope, startQuery);
            _startWatcher.EventArrived += (_, e) => HandleWmiEvent(e, started: true);
            _startWatcher.Start();

            // subscribe to instance-of-app-closed event
            var stopQuery = new WqlEventQuery("__InstanceDeletionEvent", TimeSpan.FromSeconds(1), condition);
            _stopWatcher = new ManagementEventWatcher(scope, stopQuery);
            _stopWatcher.EventArrived += (_, e) => HandleWmiEvent(e, started: false);
            _stopWatcher.Start();
            return true;
        }
        catch (ManagementException mex)
        {
            Logger.Instance.Log($"Instance watcher start failed: {mex.ErrorCode} - {mex.Message}");
            Stop();
            return false;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Instance watcher start failed: {ex.Message}");
            Stop();
            return false;
        }
    }

    private void HandleWmiEvent(EventArrivedEventArgs e, bool started)
    {
        try
        {
            if (_disposed) return;

            // if watcher is disabled, dont handle event
            if (!_isEnabled())
                return;

            string? raw = ExtractProcessNameFromEvent(e);
            // transform process name to uniform name so we can compare it with processes from the config
            string? name = ParseWmiProcessName(raw);
            if (name == null) return;

            // irrelevant process, dont handle event
            if (!_relevantNames.Contains(name))
                return;

            if (started)
            // method called by process start watcher
            {
                _counts.AddOrUpdate(name, 1, (_, old) => old + 1);

                // process started is an app to be opened -> remove from currently-opening list
                if (_groupByOpenName.ContainsKey(name))
                    _launching.TryRemove(name, out _);
            }
            // method called by process stop watcher
            else
            {
                _counts.AddOrUpdate(name, 0, (_, old) => old > 0 ? old - 1 : 0);

                // app to open cannot be found anymore -> closing was successful -> currently-closing list entry can be removed
                if (_groupByOpenName.ContainsKey(name)
                    && _counts.TryGetValue(name, out var c) && c == 0)
                {
                    _closing.TryRemove(name, out _);
                }
            }

            // watch name was changed -> update all watch segments for app name
            if (_groupsByWatchName.TryGetValue(name, out var impacted))
            {
                foreach (var g in impacted)
                    EvaluateGroup(g);
            }

            // open name changed (app opened/closed manually or crashed) -> recheck affected group
            if (_groupByOpenName.TryGetValue(name, out var openGroup))
            {
                EvaluateGroup(openGroup);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"wmi handler error: {ex.Message}");
        }
    }

     private static string? ParseWmiProcessName(string? processName)
    {
        // remove app suffix because we have to compare it to only-name list
        if (string.IsNullOrWhiteSpace(processName)) return null;

        processName = processName.Trim();

        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName[..^4];

        return string.IsNullOrWhiteSpace(processName) ? null : processName;
    }

    private string BuildWqlFilterString(string wqlPropertyName, IEnumerable<string>? namesWithoutExe = null)
    {
        // build list of relevant app names
        var names = (namesWithoutExe ?? _relevantNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
            return "";

        // escape single quotes to conform to wql syntax
        static string Esc(string s) => s.Replace("'", "''");

        // build filter string dependent on the wql property name
        var parts = names.Select(n => $"{wqlPropertyName} = '{Esc(n)}'");
        return "(" + string.Join(" OR ", parts) + ")";
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static ManagementScope CreateScope()
    {
        // idk chatgpt
        var options = new ConnectionOptions
        {
            EnablePrivileges = true,
            Impersonation = ImpersonationLevel.Impersonate
        };

        var scope = new ManagementScope(@"\\.\root\cimv2", options);
        scope.Connect();
        return scope;
    }
}