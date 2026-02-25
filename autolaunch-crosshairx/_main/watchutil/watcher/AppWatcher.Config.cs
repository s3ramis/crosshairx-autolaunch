using System;
using System.Collections.Generic;
using System.Linq;

namespace autolaunch_app;

public sealed partial class AppWatcher
{
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
}