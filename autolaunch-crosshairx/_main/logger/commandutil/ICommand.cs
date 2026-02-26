namespace AutolaunchApp.Commands;

public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    IReadOnlyList<string> Aliases{ get; }

    void Execute(CommandContext context, IReadOnlyList<string> args);
}