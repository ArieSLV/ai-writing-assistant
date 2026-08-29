namespace VoiceOverlayProof;

internal sealed class TemporaryRecordingStore
{
    private readonly string _rootWithSeparator;

    public TemporaryRecordingStore()
    {
        RootPath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "AiWritingAssistant",
            "voice-overlay-proof"));
        _rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateWavPath(string prefix)
    {
        var path = Path.GetFullPath(Path.Combine(RootPath, $"{prefix}-{Guid.NewGuid():N}.wav"));
        EnsureOwned(path);
        return path;
    }

    public void DeleteOwnedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = Path.GetFullPath(path);
        EnsureOwned(path);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool IsOwned(string path)
    {
        path = Path.GetFullPath(path);
        return path.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureOwned(string path)
    {
        if (!IsOwned(path))
        {
            throw new InvalidOperationException("Proof recording path is outside the owned TEMP root.");
        }
    }
}
