using System.Text.Json;

namespace autolaunch_app;

/*
expected config layout (json):

{
  "apps": [
    {
      "app1": { "open": "...", "watch": ["...", "..."] },
      "app2": { "open": "...", "watch": ["...", "..."] }
    }
  ]
}
*/

public class ConfigReader
{
    private readonly string _configFilePath;
    public ConfigData? Config { get; private set; }
    public bool IsLoaded { get; private set; } = false;

    public ConfigReader(string configFilePath)
    {
        _configFilePath = configFilePath;

        if (!File.Exists(_configFilePath))
        {
            Logger.Instance.Log($"no cfg file found in {_configFilePath}");

            return;
        }

        try
        {
            string json = File.ReadAllText(_configFilePath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var parsedConfig = JsonSerializer.Deserialize<ConfigData> (json, options);
            var cleanedConfig = CleanConfig(parsedConfig);

            if (cleanedConfig?.Apps.Count > 0)
            {
                Logger.Instance.Log("config successfully loaded");
                IsLoaded = true;
            }
            else
            {
                Logger.Instance.Log("config has wrong layout");
            }
        }
        catch (JsonException ex)
        {
            Logger.Instance.Log($"config has invalid jason: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"failed to read config: {ex.Message}");
        }
    }

    private static ConfigData? CleanConfig(ConfigData? cfg)
    {
        if (cfg == null) return null;

        var normalized = new ConfigData();

        foreach (var dict in cfg.Apps ?? new())
        {
            if (dict == null || dict.Count == 0) continue;

            var cleaned = new Dictionary<string, ConfigSegment>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in dict)
            {
                // return empty string if key is empty to prevent key.trim from throwing exception
                string id = kvp.Key?.Trim() ?? "";
                ConfigSegment? rule = kvp.Value;

                if (string.IsNullOrWhiteSpace(id) || rule == null)
                    continue;

                rule.Open = rule.Open?.Trim() ?? "";
                // try to convert json array to list of string otherwise return empty list 
                rule.Watch = rule.Watch?.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList()
                             ?? new List<string>();

                if (string.IsNullOrWhiteSpace(rule.Open) || rule.Watch.Count == 0)
                {
                    Logger.Instance.Log($"ignoring invalid rule '{id}' (open/watch missing)");
                    continue;
                }
                cleaned[id] = rule;
            }

            if (cleaned.Count > 0)
                normalized.Apps.Add(cleaned);
        }

        return normalized;
    }
}
