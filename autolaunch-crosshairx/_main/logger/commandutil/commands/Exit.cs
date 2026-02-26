using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;
public sealed class ExitCommand : ICommand
{
    public string Name => "exit";
    public string Description => "Closes the application";
    public string Usage => "exit";
    public IReadOnlyList<string> Aliases => new[] { "quit" };

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        Logger.Instance.Log("exit requested");
        ctx.ExitApp();
    }
}