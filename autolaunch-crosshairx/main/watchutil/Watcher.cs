using System.Management;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace autolaunch_app;

public sealed class AppWatcher : IDisposable
{
    private readonly ConfigData _config;

    private readonly Func<bool> _isEnabled;
    private readonly object _actionLock = new();

    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    private volatile bool _started;
    private volatile bool _disposed;

    // how many processes are open for a given process name
    private readonly ConcurrentDictionary<string, int> _counts =
        new(StringComparer.OrdinalIgnoreCase);

    // process.start was already requested, but were waiting for app is running confirmation by wmi start event
    private readonly ConcurrentDictionary<string, byte> _launching =
        new(StringComparer.OrdinalIgnoreCase);

    // process closer was already called on process, but were waiting for app shutdown confirmation by wmi start event
    private readonly ConcurrentDictionary<string, byte> _closing =
        new(StringComparer.OrdinalIgnoreCase);

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
        
        // split config into appblocks openapp-appstowatch
        SplitConfigIntoAppSegments();

        if (_segments.Count == 0)
        {
            Logger.Instance.Log("no valid rules found in config, watcher not started");
            return;
        }

        // initial check, since we cannot rely on events for processes that are already open
        ResyncNow();

        try
        {
            // subscribe to start events
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += (_, e) => HandleWmiEvent(e, started: true);
            _startWatcher.Start();

            // subscribe to stop events
            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += (_, e) => HandleWmiEvent(e, started: false);
            _stopWatcher.Start();

            _started = true;
            Logger.Instance.Log("monitoring active");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"failed to start monitoring: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        if (_disposed) return;

        try { _startWatcher?.Stop(); } catch { }
        try { _stopWatcher?.Stop(); } catch { }

        try { _startWatcher?.Dispose(); } catch { }
        try { _stopWatcher?.Dispose(); } catch { }

        _startWatcher = null;
        _stopWatcher = null;
        _started = false;
    }

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

    private void HandleWmiEvent(EventArrivedEventArgs e, bool started)
    {
        if (_disposed) return;

        // if watcher is disabled, dont handle event
        if (!_isEnabled())
            return;

        string? raw = e.NewEvent?["ProcessName"]?.ToString();
        // transform process name to uniform name so we can compare it with processes from the config
        string? name = NormalizeWmiProcessName(raw);
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

                        // Safety: if our bookkeeping says "running" but snapshot says "not running",
                        // don't keep the close-guard forever.
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

    private static string? NormalizeWmiProcessName(string? processName)
    {
        // remove app suffix because we have to compare it to only-name list
        if (string.IsNullOrWhiteSpace(processName)) return null;

        processName = processName.Trim();

        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName[..^4];

        return string.IsNullOrWhiteSpace(processName) ? null : processName;
    }

    private static string? ProcessNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string name = Path.GetFileNameWithoutExtension(path.Trim());
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private void SplitConfigIntoAppSegments()
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { Stop(); } catch { }

        GC.SuppressFinalize(this);
    }

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