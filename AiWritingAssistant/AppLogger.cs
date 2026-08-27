namespace AiWritingAssistant;

internal sealed class AppLogger
{
    private const long MaxLogFileSizeBytes = 1_048_576;

    private readonly object _sync = new();

    public AppLogger(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Warning(string message, Exception? exception = null)
    {
        Write("WARN", message, exception);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_sync)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                RotateIfNeeded();

                using var writer = new StreamWriter(FilePath, append: true);
                writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");

                if (exception is not null)
                    writer.WriteLine(exception);
            }
        }
        catch
        {
            // Never let logging failures break the tray app.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(FilePath))
            return;

        var fileInfo = new FileInfo(FilePath);
        if (fileInfo.Length < MaxLogFileSizeBytes)
            return;

        var archivedPath = $"{FilePath}.previous";
        File.Move(FilePath, archivedPath, overwrite: true);
    }
}
