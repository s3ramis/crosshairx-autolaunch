namespace AutolaunchApp.Commands;

public sealed class CommandContext
{
    public ManualResetEventSlim Gate { get; }
    public Func<AppWatcher?> GetWatcher { get; }
    public Func<bool> ReloadWatcher { get; }
    public Config.ConfigFileService ConfigFile { get; }
    public AutostartService Autostart { get; }
    public Action ExitApp { get; }

    public CommandContext(
        ManualResetEventSlim gate,
        Func<AppWatcher?> getWatcher,
        Func<bool> reloadWatcher,
        Config.ConfigFileService configFile,
        AutostartService autostart,
        Action exitApp)
    {
        Gate = gate;
        GetWatcher = getWatcher;
        ReloadWatcher = reloadWatcher;
        ConfigFile = configFile;
        Autostart = autostart;
        ExitApp = exitApp;
    }
}