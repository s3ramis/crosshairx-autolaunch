using System.Text.Json;
using System.Text.Json.Nodes;
using AutolaunchApp.Logging;

namespace AutolaunchApp.Config;

public sealed class ConfigFileService
{
    public string ConfigPath { get; }

    public ConfigFileService(string configPath)
    {
        ConfigPath = configPath;
    }

    public void InitIfMissing(bool force = false)
    {
        if (File.Exists(ConfigPath) && !force)
        {
            Logger.Instance.Log("config already exists");
            return;
        }

        var root = new JsonObject
        {
            ["apps"] = new JsonArray { new JsonObject() }
        };

        Save(root);
        Logger.Instance.Log($"config initialized at '{ConfigPath}'");
    }

    public void EnsureGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            Logger.Instance.Log("group name is empty");
            return;
        }

        var root = LoadOrCreateRoot();
        var appsObj = GetOrCreateFirstAppsObject(root);

        if (appsObj[groupName] is JsonObject)
        {
            Logger.Instance.Log($"group '{groupName}' already exists");
            return;
        }

        appsObj[groupName] = new JsonObject
        {
            ["open"] = "",
            ["watch"] = new JsonArray()
        };

        Save(root);
        Logger.Instance.Log($"group '{groupName}' created");
    }

    public void SetOpen(string groupName, string openPath)
    {
        var root = LoadOrCreateRoot();
        var appsObj = GetOrCreateFirstAppsObject(root);
        var grp = GetOrCreateGroup(appsObj, groupName);

        grp["open"] = openPath ?? "";
        Save(root);

        Logger.Instance.Log($"set open for '{groupName}'");
    }

    public void AddWatch(string groupName, string watchPath)
    {
        var root = LoadOrCreateRoot();
        var appsObj = GetOrCreateFirstAppsObject(root);
        var grp = GetOrCreateGroup(appsObj, groupName);

        grp["watch"] ??= new JsonArray();

        if (grp["watch"] is not JsonArray arr)
        {
            arr = new JsonArray();
            grp["watch"] = arr;
        }

        string path = watchPath ?? "";
        if (string.IsNullOrWhiteSpace(path))
        {
            Logger.Instance.Log("watch path is empty");
            return;
        }

        // dedupe
        if (!arr.Any(n => string.Equals(n?.ToString(), path, StringComparison.OrdinalIgnoreCase)))
            arr.Add(path);

        Save(root);
        Logger.Instance.Log($"added watch for '{groupName}'");
    }

    private JsonObject LoadOrCreateRoot()
    {
        if (!File.Exists(ConfigPath))
        {
            return new JsonObject
            {
                ["apps"] = new JsonArray { new JsonObject() }
            };
        }

        try
        {
            var text = File.ReadAllText(ConfigPath);
            var docOpt = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };

            var node = JsonNode.Parse(text, nodeOptions: null, documentOptions: docOpt);
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            // if file is broken -> start fresh (you can also choose to throw)
            return new JsonObject
            {
                ["apps"] = new JsonArray { new JsonObject() }
            };
        }
    }

    private static JsonObject GetOrCreateFirstAppsObject(JsonObject root)
    {
        root["apps"] ??= new JsonArray();

        if (root["apps"] is not JsonArray appsArr)
        {
            appsArr = new JsonArray();
            root["apps"] = appsArr;
        }

        if (appsArr.Count == 0 || appsArr[0] is not JsonObject)
            appsArr.Insert(0, new JsonObject());

        return (JsonObject)appsArr[0]!;
    }

    private static JsonObject GetOrCreateGroup(JsonObject appsObj, string groupName)
    {
        if (appsObj[groupName] is JsonObject obj)
            return obj;

        obj = new JsonObject
        {
            ["open"] = "",
            ["watch"] = new JsonArray()
        };

        appsObj[groupName] = obj;
        return obj;
    }

    private void Save(JsonObject root)
    {
        var opt = new JsonSerializerOptions { WriteIndented = true };

        // atomic-ish write
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(opt));
        File.Copy(tmp, ConfigPath, overwrite: true);
        File.Delete(tmp);
    }
}