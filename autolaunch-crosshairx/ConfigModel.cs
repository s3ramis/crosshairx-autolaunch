namespace autolaunch_app;

public sealed class ConfigData
{
    public List<Dictionary<string, AppRule>> Apps { get; set; } = new();
}

public sealed class AppRule
{
    public string Open { get; set; } = "";
    public List<string> Watch { get; set; } = new();
}