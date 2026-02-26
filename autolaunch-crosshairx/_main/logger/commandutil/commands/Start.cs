using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;

public sealed class StartCommand : ICommand
{
    public string Name => "start";
    public string Description => "Resumes watching and resyncs process snapshot";
    public string Usage => "start";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (ctx.Gate.IsSet)
        {
            Logger.Instance.Log("watcher already running");
            return;
        }

        ctx.Gate.Set();
        Logger.Instance.Log("watcher resumed");

        // IMPORTANT: while paused, events were ignored -> counts can be stale
        ctx.GetWatcher()?.ResyncNow();
    }
}