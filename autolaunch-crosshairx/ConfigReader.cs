namespace autolaunch_crosshairx;

/*
expected config layout:

open app:
"C:\Path\To\App.exe"

watch apps:
"C:\Path\To\First\App.exe"
"C:\Path\To\Second\App.exe"
*/

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
            // open app AND at least one app to watch must exist
            if (GetApps(_lines, "open app:") != null && GetApps(_lines, "watch apps:").Count > 0)
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
        return GetApps(_lines, "open app:", true).FirstOrDefault();
    }

    public List<string> GetAppsToWatch()
    {
        return GetApps(_lines, "watch apps:");
    }

    private static List<string> GetApps(List<string> lines, string section, bool single = false)
    {
        List<string> apps = [];

        string? currentSection = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // look for section marker
            if (trimmedLine.EndsWith(':'))
            {
                currentSection = trimmedLine.ToLowerInvariant();
                continue;
            }

            // get filepaths after specified section marker
            if (currentSection == section && trimmedLine.StartsWith('\"') && trimmedLine.EndsWith('\"'))
            {
                apps.Add(trimmedLine.Trim('\"'));
                if (single)
                {
                    break;
                }
            }
        }
        return apps;
    }
}
