using AutolaunchApp.Logging;
namespace AutolaunchApp.Commands;
public sealed class GroupCommand : ICommand
{
    public string Name => "group";
    public string Description => "Group related commands";
    public string Usage => "group init <groupName>";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public void Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Instance.Log("usage: group init <groupName>");
            return;
        }

        string groupName = args[1];
        ctx.ConfigFile.EnsureGroup(groupName);
    }
}