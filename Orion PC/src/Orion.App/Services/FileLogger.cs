namespace Orion.App.Services;

public interface IAppLogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? exception = null);
}

public class FileLogger : IAppLogger
{
    private static readonly object FileLock = new();
    private readonly string _logDirectory;

    public FileLogger()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(localAppData, "OrionStore", "Logs");
        try
        {
            Directory.CreateDirectory(_logDirectory);
            CleanOldLogs();
        }
        catch
        {
            // Ignore directory creation issues during logging setup
        }
    }

    public void LogInfo(string message) => WriteEntry("INFO", message, null);
    public void LogWarning(string message) => WriteEntry("WARN", message, null);
    public void LogError(string message, Exception? exception = null) => WriteEntry("ERROR", message, exception);

    private void WriteEntry(string level, string message, Exception? exception)
    {
        try
        {
            var fileName = $"app_{DateTime.UtcNow:yyyyMMdd}.log";
            var filePath = Path.Combine(_logDirectory, fileName);

            var sanitizedMessage = SanitizeLogMessage(message);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            var line = $"[{timestamp}] [{level}] {sanitizedMessage}";

            if (exception != null)
            {
                line += $"{Environment.NewLine}{exception}";
            }

            lock (FileLock)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Fallback: never throw from logger
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var files = Directory.GetFiles(_logDirectory, "app_*.log");
            var cutoff = DateTime.UtcNow.AddDays(-7);

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc < cutoff)
                {
                    try { fileInfo.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore rotation cleanup failures
        }
    }

    private static string SanitizeLogMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Mask potential sensitive data (tokens, API keys, basic auth)
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(api[_-]?key|token|secret|password)\s*[:=]\s*[^\s,;]+",
            "$1: [REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        return sanitized;
    }
}
