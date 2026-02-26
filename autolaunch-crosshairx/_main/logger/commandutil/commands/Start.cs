using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;

public sealed class StartCommand : ICommand
{
    public string Name => "start";
    public string Description => "resumes watching and checks for processes that might have opened in the meantime";
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

        // check for events that might have started while watcher was paused
        ctx.GetWatcher()?.ResyncNow();
    }
}