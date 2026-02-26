namespace AutolaunchApp.Commands;

public sealed class AutostartCommand : ICommand
{
    public string Name => "autostart";
    public string Description => "Enable/disable autostart (HKCU Run)";
    public string Usage => "autostart on | autostart off | autostart status";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            Logger.Instance.Log($"usage: {Usage}");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "on":
                ctx.Autostart.Enable();
                Logger.Instance.Log("autostart enabled");
                break;

            case "off":
                ctx.Autostart.Disable();
                Logger.Instance.Log("autostart disabled");
                break;

            case "status":
                Logger.Instance.Log(ctx.Autostart.IsEnabled()
                    ? "autostart: enabled"
                    : "autostart: disabled");
                break;

            default:
                Logger.Instance.Log($"usage: {Usage}");
                break;
        }
    }
}