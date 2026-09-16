namespace CtrlLauncher.Services;

public sealed class FileLogService
{
    private readonly AppPathService _paths;
    private readonly object _sync = new();

    public FileLogService(AppPathService paths) => _paths = paths;
    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            _paths.EnsureDirectories();
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
            if (exception is not null) line += Environment.NewLine + exception;
            lock (_sync)
                File.AppendAllText(Path.Combine(_paths.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log"),
                    line + Environment.NewLine);
        }
        catch
        {
            // Logging must never prevent returning to the launcher.
        }
    }
}
