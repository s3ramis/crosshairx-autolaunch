namespace AutolaunchApp.Commands;

public sealed class ReloadCommand : ICommand
{
    public string Name => "reload";
    public string Description => "Reloads config and restarts watcher";
    public string Usage => "reload";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (!ctx.ReloadWatcher())
            Logger.Instance.Log("reload failed");
    }
}