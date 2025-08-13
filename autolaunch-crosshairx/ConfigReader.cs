using System;

namespace autolaunch_crosshairx;

public class ConfigReader
{
    private readonly string _configFilePath;
    private readonly List<string> _lines = [];
    public bool IsLoaded { get; private set; } = false;

    public ConfigReader(string configFilePath)
    {
        _configFilePath = configFilePath;

        if (!File.Exists(_configFilePath))
        {
            Logger.Instance.Log($"no cfg file found in {_configFilePath}");

            return;
        }
        else
        {
            _lines = File.ReadAllLines(_configFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

            if (GetSingleApp(_lines, "open app:") != null && GetMultipleApps(_lines, "watch apps:").Count > 0)
            {
                Logger.Instance.Log("config successfully loaded");
                IsLoaded = true;
            }
            else
            {
                Logger.Instance.Log("cfg file has wrong layout");
            }
        }
    }

    public string? GetAppToOpen()
    {
        return GetSingleApp(_lines, "open app:");
    }

    public List<string> GetAppsToWatch()
    {
        return GetMultipleApps(_lines, "watch apps:");
    }

    private static string? GetSingleApp(List<string> lines, string section)
    {
        string? currentSection = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.EndsWith(':'))
            {
                currentSection = trimmedLine.ToLowerInvariant();
                continue;
            }

            if (currentSection == section && trimmedLine.StartsWith('\"') && trimmedLine.EndsWith('\"'))
            {
                return trimmedLine.Trim('\"');
            }
        }
        return null;
    }

    private static List<string> GetMultipleApps(List<string> lines, string section)
    {
        List<string> apps = [];

        string? currentSection = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.EndsWith(':'))
            {
                currentSection = trimmedLine.ToLowerInvariant();
                continue;
            }

            if (currentSection == section && trimmedLine.StartsWith('\"') && trimmedLine.EndsWith('\"'))
            {
                apps.Add(trimmedLine.Trim('\"'));
            }
        }
        return apps;
    }
}
