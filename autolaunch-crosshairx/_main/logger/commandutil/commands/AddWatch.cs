namespace AutolaunchApp.Commands;

public sealed class AddWatchCommand : ICommand
{
    public string Name => "add-watch";
    public string Description => "Adds a 'watch' entry to a group";
    public string Usage => "add-watch <groupName> \"C:\\Path\\To\\Watch.exe\"";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            Logger.Instance.Log($"usage: {Usage}");
            return;
        }

        string group = args[0];
        string path = args[1];

        ctx.ConfigFile.AddWatch(group, path);

        // optional: reload immediately
        ctx.ReloadWatcher();
    }
}