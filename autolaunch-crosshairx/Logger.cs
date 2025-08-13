namespace autolaunch_crosshairx;

public sealed class Logger
{
    private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());
    private readonly string logFilePath;
    private string? _lastMessage;

    public static Logger Instance => _instance.Value;

    private Logger()
    {
        string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        logFilePath = Path.Combine(logDirectory, "autolaunchapp.log");

        try
        {
            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);

            File.WriteAllText(logFilePath, $"----- log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} -----{Environment.NewLine}");

        }
        catch { }
    }

    public void Log(string message)
    {
        try
        {
            if (message == _lastMessage)
                return;

            _lastMessage = message;
            string timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
            File.AppendAllText(logFilePath, timestampedMessage);
        }
        catch { }
    }
}
