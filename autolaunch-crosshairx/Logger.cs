namespace autolaunch_crosshairx;

// single instance logger to prevent write conflicts if used in different threads
public sealed class Logger
{
    private static readonly Lazy<Logger> _instance = new(() => new Logger());
    private readonly string logFilePath;
    // store last message to prevent duplicate consecutive log entries
    private string? _lastMessage;

    // gets the above initialized logger instance
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"logger initialization failed: {ex.Message}");
        }
    }


    public void Log(string message)
    {
        try
        {
            // skip if last log entry == current entry to be logged
            if (message == _lastMessage)
                return;

            _lastMessage = message;

            string timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";

            File.AppendAllText(logFilePath, timestampedMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"logging failed: {ex.Message}");
        }
    }
}
