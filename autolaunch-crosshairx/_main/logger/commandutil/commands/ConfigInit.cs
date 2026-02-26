using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;

public sealed class ConfigInitCommand : ICommand
{
    public string Name => "config";
    public string Description => "config related commands";
    public string Usage => "config init [force]";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            Logger.Instance.Log("usage: config init [force]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        if (sub != "init")
        {
            Logger.Instance.Log("usage: config init [force]");
            return;
        }

        bool force = args.Count >= 2 && args[1].Equals("force", StringComparison.OrdinalIgnoreCase);
        ctx.ConfigFile.InitIfMissing(force);

        Logger.Instance.Log("use 'group init <name>', 'add-open', 'add-watch' and then 'reload' to set up first config");
    }
}