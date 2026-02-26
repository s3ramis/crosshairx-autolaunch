namespace AutolaunchApp.Commands;

public sealed class AddOpenCommand : ICommand
{
    public string Name => "add-open";
    public string Description => "Sets the 'open' path for a group";
    public string Usage => "add-open <groupName> \"C:\\Path\\To\\App.exe\"";
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

        ctx.ConfigFile.SetOpen(group, path);

        // optional: reload immediately
        ctx.ReloadWatcher();
    }
}