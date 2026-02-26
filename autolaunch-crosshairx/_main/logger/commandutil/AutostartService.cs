using Microsoft.Win32;

namespace AutolaunchApp;

public sealed class AutostartService
{
    private readonly string _appName;
    private readonly string _exePath;
    private readonly string? _args;

    public AutostartService(string appName, string exePath, string? args = null)
    {
        _appName = appName;
        _exePath = exePath;
        _args = args;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
        var v = key?.GetValue(_appName)?.ToString();
        return !string.IsNullOrWhiteSpace(v);
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                     ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        var value = $"\"{_exePath}\"";
        if (!string.IsNullOrWhiteSpace(_args))
            value += " " + _args;

        key.SetValue(_appName, value);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key?.GetValue(_appName) != null)
            key.DeleteValue(_appName);
    }
}