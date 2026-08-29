namespace AiWritingAssistant.Audio;

internal sealed class TemporaryRecordingStore
{
    private readonly string _rootWithSeparator;

    public TemporaryRecordingStore(string? rootPath = null)
    {
        RootPath = Path.GetFullPath(rootPath ?? Path.Combine(
            Path.GetTempPath(),
            "AiWritingAssistant",
            "recordings"));
        _rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateWavPath(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("A safe recording prefix is required.", nameof(prefix));

        var path = Path.GetFullPath(Path.Combine(RootPath, $"{prefix}-{Guid.NewGuid():N}.wav"));
        EnsureOwned(path);
        return path;
    }

    public bool IsOwned(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public bool DeleteOwnedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path);
        EnsureOwned(fullPath);

        if (!File.Exists(fullPath))
            return false;

        File.Delete(fullPath);
        return true;
    }

    public int CleanupOrphans(TimeSpan maximumAge, DateTimeOffset? now = null)
    {
        if (maximumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumAge));

        var cutoff = (now ?? DateTimeOffset.UtcNow) - maximumAge;
        var deleted = 0;

        foreach (var path in Directory.EnumerateFiles(RootPath, "*.wav", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(path) > cutoff.UtcDateTime)
                continue;

            if (DeleteOwnedFile(path))
                deleted++;
        }

        return deleted;
    }

    private void EnsureOwned(string fullPath)
    {
        if (!IsOwned(fullPath))
            throw new InvalidOperationException("Recording path is outside the owned TEMP root.");
    }
}
