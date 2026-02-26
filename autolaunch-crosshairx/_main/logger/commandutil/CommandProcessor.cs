namespace AutolaunchApp.Commands;

public sealed class CommandProcessor
{
    private readonly CommandContext _ctx;
    private readonly Dictionary<string, ICommand> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    public CommandProcessor(CommandContext ctx)
    {
        _ctx = ctx;
    }

    public void Register(ICommand cmd)
    {
        _commands[cmd.Name] = cmd;
        foreach (var a in cmd.Aliases)
            _commands[a] = cmd;
    }

    public IEnumerable<ICommand> AllCommandsUnique()
        => _commands.Values.Distinct();

    public void Execute(string input)
    {
        var tokens = CommandLineTokenizer.Tokenize(input);
        if (tokens.Count == 0) return;

        var name = tokens[0];
        var args = tokens.Skip(1).ToList();

        if (_commands.TryGetValue(name, out var cmd))
        {
            cmd.Execute(_ctx, args);
        }
        else
        {
            Logger.Instance.Log($"command '{name}' not recognized (type 'help')");
        }
    }
}