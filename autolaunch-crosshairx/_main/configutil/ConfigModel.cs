namespace AutolaunchApp.Config;

public sealed class ConfigData
{
    public List<Dictionary<string, ConfigSegment>> Apps { get; set; } = new();
}

public sealed class ConfigSegment
{
    public string Open { get; set; } = "";
    public List<string> Watch { get; set; } = new();
}