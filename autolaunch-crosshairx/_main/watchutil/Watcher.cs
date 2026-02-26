using System.Management;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;

namespace autolaunch_app;

public sealed partial class AppWatcher : IDisposable
{
     private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    private readonly ConfigData _config;
    private readonly Func<bool> _isEnabled;
    private readonly object _actionLock = new();

    private volatile bool _started;
    private volatile bool _disposed;
    
    // saves event watcher mode so we can decide how event has to be handled
    private enum WmiMode { Trace, Instance }
    private WmiMode _mode;

    // how many process instances are running for a given process name
    private readonly ConcurrentDictionary<string, int> _counts =
        new(StringComparer.OrdinalIgnoreCase);

    // process.start was already requested, but were waiting for process is running confirmation by wmi start event
    private readonly ConcurrentDictionary<string, byte> _launching =
        new(StringComparer.OrdinalIgnoreCase);

    // process closer was already called on process, but were waiting for process shutdown confirmation by wmi start event
    private readonly ConcurrentDictionary<string, byte> _closing =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<int, string> _pIdToName = new();

    // watch list per open app
    private List<ConfigSegment> _segments = new();

    // open / watch groups, so only the processes affected by a specific event will get checked again 
    private Dictionary<string, ConfigSegment> _groupByOpenName =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<ConfigSegment>> _groupsByWatchName =
        new(StringComparer.OrdinalIgnoreCase);

    // open / close process name collection
    private HashSet<string> _relevantNames =
        new(StringComparer.OrdinalIgnoreCase);

    public AppWatcher(ConfigData config, Func<bool>? isEnabled = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _isEnabled = isEnabled ?? (() => true);
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AppWatcher));
        if (_started) return;
        
        // translate the config into a format the app watcher can unterstand
        CompileConfig();

        if (_segments.Count == 0)
        {
            Logger.Instance.Log("no valid rules found in config, watcher not started");
            return;
        }

        try
        {
            bool startedTrace = false;

            // check if app is running elevated and try to start wmi trace event watcher
            if (IsRunningAsAdministrator() && TryStartTraceWatchers())
            {
                startedTrace = true;
            }
            else
            {
                if (IsRunningAsAdministrator())
                    Logger.Instance.Log("trace events not available (access denied). falling back to instance events");

                if (!TryStartInstanceWatchers())
                {
                    Logger.Instance.Log("failed to start monitoring (no wmi watcher could be started)");
                    Stop();
                    return;
                }
            }

            _started = true;
            // log for whichever watcher was successfully started
            Logger.Instance.Log(startedTrace
                ? "monitoring active (trace events)"
                : "monitoring active (instance events, within polling)");

            // initial check, since we cannot rely on events for processes that are already open
            ResyncNow();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"failed to start monitoring: {ex.Message}");
            Stop();
        }
    }

    

    

    private void EvaluateGroup(ConfigSegment group)
    {
        if (_disposed) return;

        lock (_actionLock)
        {
            // should open app be running?
            WatchEntry? trigger = null;
            foreach (var w in group.WatchList)
            {
                if (GetCount(w.WatchName) > 0)
                {
                    trigger = w;
                    break;
                }
            }

            bool shouldBeOpen = trigger != null;

            int openCount = GetCount(group.OpenName);
            bool openRunning = openCount > 0 || _launching.ContainsKey(group.OpenName);

            if (shouldBeOpen)
            {
                // app should be running -> remove it from currently-closing list
                _closing.TryRemove(group.OpenName, out _);

                if (!openRunning)
                {
                    // app is not running -> add it to currently-opening list and start the process
                    if (!_launching.TryAdd(group.OpenName, 0))
                        return;

                    Logger.Instance.Log($"detected {trigger!.WatchName} (config id: {trigger.SegmentId})");
                    Logger.Instance.Log($"starting {group.OpenName}");

                    try
                    {
                        using Process p = new();
                        p.StartInfo.FileName = group.OpenPath;

                        bool ok = p.Start();
                        if (!ok)
                        {
                            Logger.Instance.Log($"failed to start '{group.OpenName}'");
                            _launching.TryRemove(group.OpenName, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Log($"failed to open app '{group.OpenName}': {ex.Message}");
                        _launching.TryRemove(group.OpenName, out _);
                    }
                }
            }
            else
            {
                // app should not be open anymore -> remove it from currently-opening list
                _launching.TryRemove(group.OpenName, out _);

                if (openCount > 0)
                {
                    // add app to currently-closing list, bcs it is already closing
                    if (!_closing.TryAdd(group.OpenName, 0))
                        return;

                    Logger.Instance.Log($"no watch app running for '{group.OpenName}'");
                    Logger.Instance.Log($"closing {group.OpenName}");

                    try
                    {
                         var procsToClose = Process.GetProcessesByName(group.OpenName);

                        // if there is an entry in the currently-closing list, but its detected to not be running, remove entry from list
                        if (procsToClose.Length == 0)
                        {
                            _closing.TryRemove(group.OpenName, out _);
                            return;
                        }

                        foreach (var p in procsToClose)
                        {
                            try
                            {
                                ProcessCloser closer = new ProcessCloser(p);
                                closer.ShutdownProcess();
                            }
                            catch (InvalidOperationException)
                            {
                                // process crashed -> we dont care should be closed now anyways
                            }
                            finally
                            {
                                try { p.Dispose(); } catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Log($"failed to close '{group.OpenName}': {ex.Message}");
                        // remove process from currently-closing list, otherwise we might get stuck in infinite closing loop
                        _closing.TryRemove(group.OpenName, out _);
                    }
                }
                else
                {
                    // process wasnt open to begin with -> remove from currently-closing list
                    _closing.TryRemove(group.OpenName, out _);
                }
            }
        }
    }

    private int GetCount(string name)
    {
        return _counts.TryGetValue(name, out var c) ? c : 0;
    }

    private static string? ProcessNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string name = Path.GetFileNameWithoutExtension(path.Trim());
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private string? ExtractProcessNameFromEvent(EventArrivedEventArgs e)
    {
        var ev = e.NewEvent;
        if (ev == null) return null;
        switch (_mode)
        {
            // extract process name from trace events
            case WmiMode.Trace:
            {
                var traceName = e.NewEvent?["ProcessName"]?.ToString();
                if (!string.IsNullOrWhiteSpace(traceName))
                    return traceName;
                break;
            }
            // extract process name from instance events
            case WmiMode.Instance:
            {
                if (e.NewEvent?["TargetInstance"] is ManagementBaseObject target)
                {
                    var instName = target["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(instName))
                        return instName;
                }
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { Stop(); } catch { }

        GC.SuppressFinalize(this);
    }
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
        if (!_isEnabled()) return;

        int? pId = GetProcessIdFromEvent(e);

        string? raw = ExtractProcessNameFromEvent(e);
        string? name = ParseWmiProcessName(raw);

        if (_mode == WmiMode.Trace && pId.HasValue)
        {
            if (started)
            {
                string? resolved = null;
                try
                {
                    using var p = Process.GetProcessById(pId.Value);
                    resolved = p.ProcessName;
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(resolved))
                    name = resolved;

                if (name == null) return;
                if (!_relevantNames.Contains(name)) return;

                // save process id of started process, because in the stop event 
                _pIdToName[pId.Value] = name;
            }
            else
            {
                // stop: prefer pid->name mapping (fixes StopTrace truncation)
                if (_pIdToName.TryRemove(pId.Value, out var mapped))
                    name = mapped;

                if (name == null) return;
                if (!_relevantNames.Contains(name)) return;
            }
        }
        else
        {
            // Instance mode (or trace without pid): old behaviour
            if (name == null) return;
            if (!_relevantNames.Contains(name)) return;
        }

        // --- your existing count + evaluate logic stays almost the same ---

        if (started)
        {
            _counts.AddOrUpdate(name, 1, (_, old) => old + 1);

            if (_groupByOpenName.ContainsKey(name))
                _launching.TryRemove(name, out _);
        }
        else
        {
            _counts.AddOrUpdate(name, 0, (_, old) => old > 0 ? old - 1 : 0);

            if (_groupByOpenName.ContainsKey(name)
                && _counts.TryGetValue(name, out var c) && c == 0)
            {
                _closing.TryRemove(name, out _);
            }
        }

        if (_groupsByWatchName.TryGetValue(name, out var impacted))
        {
            foreach (var g in impacted)
                EvaluateGroup(g);
        }

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

    private int? GetProcessIdFromEvent(EventArrivedEventArgs e)
    {
    var ev = e.NewEvent;
    if (ev == null) return null;

    switch (_mode)
        {
            case WmiMode.Trace:
            {
                if (TryGetUInt32Property(ev, "ProcessID", out var pid) ||TryGetUInt32Property(ev, "ProcessId", out pid))
                {
                    return unchecked((int)pid);
                }
                return null;
            }
            case WmiMode.Instance:
                {
                if (ev.Properties["TargetInstance"]?.Value is ManagementBaseObject target)
                {
                    if (TryGetUInt32Property(target, "ProcessId", out var pid) || TryGetUInt32Property(target, "ProcessID", out pid))
                    {
                        return unchecked((int)pid);
                    }
                }
                break;
            }
        }
        return null;
    }

    private static bool TryGetUInt32Property(ManagementBaseObject obj, string prop, out uint value)
    {
        value = 0;

        foreach (PropertyData p in obj.Properties)
        {
            if (!string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.Value == null) return false;

            try
            {
                value = Convert.ToUInt32(p.Value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public void ResyncNow()
    {
        if (_disposed) return;

        lock (_actionLock)
        {
            _launching.Clear();
            _closing.Clear();
            _pIdToName.Clear();

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
                        _pIdToName[p.Id] = name;
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

    private void CompileConfig()
    {
        // reset previously built segments/groups
        _segments.Clear();
        _groupByOpenName.Clear();
        _groupsByWatchName.Clear();
        _relevantNames.Clear();

        // group by name bcs process name doesnt contain path
        var byOpenName = new Dictionary<string, ConfigSegment>(StringComparer.OrdinalIgnoreCase);
        var watchSeenPerOpen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dict in _config.Apps ?? new())
        {
            if (dict == null) continue;

            foreach (var kvp in dict)
            {
                // config element id
                string elementId = kvp.Key?.Trim() ?? "";
                var segment = kvp.Value;

                // skip invalid entries
                if (string.IsNullOrWhiteSpace(elementId) || segment == null)
                    continue;

                // get open app name
                string openPath = segment.Open?.Trim() ?? "";
                string? openName = ProcessNameFromPath(openPath);
                if (openName == null)
                    continue;

                // build app-to-watch list from json array
                if (!byOpenName.TryGetValue(openName, out var group))
                {
                    group = new ConfigSegment
                    {
                        OpenName = openName,
                        OpenPath = openPath,
                        WatchList = new List<WatchEntry>()
                    };
                    byOpenName[openName] = group;

                    // save new entries to be able to check for dupes more easily
                    watchSeenPerOpen[openName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    // multiple paths exist for app to be opened, pick first path and log warning
                    if (!string.Equals(group.OpenPath, openPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Instance.Log(
                            $"warning: multiple open paths share same exe name '{openName}'. " +
                            $"Using '{group.OpenPath}', ignoring '{openPath}'.");
                    }
                }

                // no watch list -> exit early
                if (segment.Watch == null || segment.Watch.Count == 0)
                    continue;
                    
                // build hashset for all apps to be watched
                var seen = watchSeenPerOpen[openName];

                foreach (var watchPathRaw in segment.Watch)
                {
                    string watchPath = watchPathRaw?.Trim() ?? "";
                    string? watchName = ProcessNameFromPath(watchPath);
                    if (watchName == null) continue;

                    // prevent double entry 
                    if (!seen.Add(watchName))
                        continue;

                    group.WatchList.Add(new WatchEntry
                    {
                        SegmentId = elementId,
                        WatchName = watchName
                    });
                }
            }
        }

        // only keep groups with watch entry
        _segments = byOpenName.Values.Where(g => g.WatchList.Count > 0).ToList();

        // build
        foreach (var segment in _segments)
        {
            _groupByOpenName[segment.OpenName] = segment;
            _relevantNames.Add(segment.OpenName);

            foreach (var w in segment.WatchList)
            {
                _relevantNames.Add(w.WatchName);

                if (!_groupsByWatchName.TryGetValue(w.WatchName, out var list))
                {
                    list = new List<ConfigSegment>();
                    _groupsByWatchName[w.WatchName] = list;
                }

                // only add segment if segment with this id doesnt already exist
                if (!list.Contains(segment))
                    list.Add(segment);
            }
        }
    }

    // helper classes for data structuring
    private sealed class ConfigSegment
    {
        public string OpenName { get; init; } = "";
        public string OpenPath { get; init; } = "";
        public List<WatchEntry> WatchList { get; init; } = new();
    }

    private sealed class WatchEntry
    {
        public string SegmentId { get; init; } = "";
        public string WatchName { get; init; } = "";
    }
}