using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;

public sealed class StopCommand : ICommand
{
    public string Name => "stop";
    public string Description => "pauses watching";
    public string Usage => "stop";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (!ctx.Gate.IsSet)
        {
            Logger.Instance.Log("watcher already paused");
            return;
        }

        ctx.Gate.Reset();
        Logger.Instance.Log("watcher paused");
    }
}