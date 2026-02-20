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

            var parsed = JsonSerializer.Deserialize<ConfigData> (json, options);

            if (HasAnyValidRule(parsed))
            {
                Config = Normalize(parsed);
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

    private static ConfigData? Normalize(ConfigData? cfg)
    {
        if (cfg == null) return null;

        // wir filtern kaputte Einträge raus, statt komplett zu failen
        var normalized = new ConfigData();

        foreach (var dict in cfg.Apps ?? new())
        {
            if (dict == null || dict.Count == 0) continue;

            var cleaned = new Dictionary<string, AppRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in dict)
            {
                string id = kvp.Key?.Trim() ?? "";
                AppRule? rule = kvp.Value;

                if (string.IsNullOrWhiteSpace(id) || rule == null)
                    continue;

                rule.Open = rule.Open?.Trim() ?? "";
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

    private static bool HasAnyValidRule(ConfigData? cfg)
    {
        if (cfg?.Apps == null || cfg.Apps.Count == 0)
            return false;

        foreach (var dict in cfg.Apps)
        {
            if (dict == null) continue;
            foreach (var kvp in dict)
            {
                if (kvp.Value == null) continue;
                if (!string.IsNullOrWhiteSpace(kvp.Key) &&
                    !string.IsNullOrWhiteSpace(kvp.Value.Open) &&
                    kvp.Value.Watch != null && kvp.Value.Watch.Count > 0)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
