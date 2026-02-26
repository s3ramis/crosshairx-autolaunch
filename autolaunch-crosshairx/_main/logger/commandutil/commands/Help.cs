using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;

public sealed class HelpCommand : ICommand
{
    private readonly Func<IEnumerable<ICommand>> _getAll;

    public HelpCommand(Func<IEnumerable<ICommand>> getAll)
    {
        _getAll = getAll;
    }

    public string Name => "help";
    public string Description => "lists available commands";
    public string Usage => "help";
    public IReadOnlyList<string> Aliases => new[] { "h", "?" };

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        Logger.Instance.Log("available commands:");
        foreach (var c in _getAll().OrderBy(c => c.Name))
            Logger.Instance.Log($"  {c.Name} - {c.Description} | usage: {c.Usage}");
    }
}