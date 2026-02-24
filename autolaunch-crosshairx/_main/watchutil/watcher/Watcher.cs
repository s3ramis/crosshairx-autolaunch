using System.Management;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms.VisualStyles;

namespace autolaunch_app;

public sealed partial class AppWatcher : IDisposable
{
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
        CreateRuntimeStructureForConfig();

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
}