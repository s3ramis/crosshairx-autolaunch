using System;

namespace autolaunch_crosshairx;

public class ConfigReader
{
    private readonly string _configFilePath;
    private readonly List<string> _lines = new List<string>();
    public bool IsLoaded { get; private set; } = false;

    public ConfigReader(string configFilePath)
    {
        _configFilePath = configFilePath;

        if (!File.Exists(_configFilePath))
        {
            Console.WriteLine($"no cfg file found in {_configFilePath}");

            return;
        }
        else
        {
            _lines = File.ReadAllLines(_configFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

            if (GetSingleApp(_lines, "open app:") != null && GetMultipleApps(_lines, "watch apps:").Count > 0)
            {
                Console.WriteLine("config file successfully loaded");
                IsLoaded = true;
            }
            else
            {
                Console.WriteLine("cfg file has wrong layout");
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

    private string? GetSingleApp(List<string> lines, string section)
    {
        string? currentSection = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.EndsWith(":"))
            {
                currentSection = trimmedLine.ToLowerInvariant();
                continue;
            }

            if (currentSection == section && trimmedLine.StartsWith("\"") && trimmedLine.EndsWith("\""))
            {
                return trimmedLine.Trim('\"');
            }
        }
        return null;
    }

    private List<string> GetMultipleApps(List<string> lines, string section)
    {
        List<string> apps = new();

        string? currentSection = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.EndsWith(":"))
            {
                currentSection = trimmedLine.ToLowerInvariant();
                continue;
            }

            if (currentSection == section && trimmedLine.StartsWith("\"") && trimmedLine.EndsWith("\""))
            {
                apps.Add(trimmedLine.Trim('\"'));
            }
        }
        return apps;
    }
}
