using System.Text.RegularExpressions;

namespace VoiceOverlayProof;

internal sealed partial class FixtureRecordingStore
{
    private readonly string _rootWithSeparator;

    public FixtureRecordingStore(string sessionId)
    {
        if (!SafeSessionId().IsMatch(sessionId))
        {
            throw new ArgumentException(
                "Fixture session ID must contain only letters, digits and hyphens (1–64 characters).",
                nameof(sessionId));
        }

        var baseRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "AiWritingAssistant",
            "voice-proof"));
        RootPath = Path.GetFullPath(Path.Combine(baseRoot, sessionId));
        var basePrefix = baseRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!RootPath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fixture session escaped the owned proof root.");
        }

        _rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string Preserve(
        TemporaryRecordingStore sourceStore,
        string sourcePath,
        string fixtureId,
        int take)
    {
        if (!sourceStore.IsOwned(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException("Fixture source is missing or outside the capture TEMP root.");
        }

        var safeFixture = FixtureCatalog.Get(fixtureId).Id;
        if (take is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        var destination = Path.GetFullPath(Path.Combine(RootPath, $"{safeFixture}-{take:00}.wav"));
        EnsureOwned(destination);
        if (File.Exists(destination))
        {
            throw new IOException("The requested fixture take already exists.");
        }

        File.Move(sourcePath, destination);
        return destination;
    }

    public void DeleteOwnedFile(string path)
    {
        path = Path.GetFullPath(path);
        EnsureOwned(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void EnsureOwned(string path)
    {
        if (!path.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fixture path is outside the owned session root.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSessionId();
}
