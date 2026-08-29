using AiWritingAssistant.Audio;

namespace AiWritingAssistant.Tests;

public sealed class TemporaryRecordingStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AiWritingAssistant.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Owned_file_can_be_deleted_idempotently()
    {
        var store = new TemporaryRecordingStore(_root);
        var path = store.CreateWavPath("native");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.True(store.IsOwned(path));
        Assert.True(store.DeleteOwnedFile(path));
        Assert.False(store.DeleteOwnedFile(path));
    }

    [Fact]
    public void Outside_path_is_rejected()
    {
        var store = new TemporaryRecordingStore(_root);
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.wav");

        Assert.False(store.IsOwned(outside));
        Assert.Throws<InvalidOperationException>(() => store.DeleteOwnedFile(outside));
    }

    [Fact]
    public void Cleanup_removes_only_old_top_level_wav_files()
    {
        var store = new TemporaryRecordingStore(_root);
        var oldWav = store.CreateWavPath("old");
        var freshWav = store.CreateWavPath("fresh");
        File.WriteAllBytes(oldWav, [1]);
        File.WriteAllBytes(freshWav, [2]);
        File.SetLastWriteTimeUtc(oldWav, DateTime.UtcNow.AddDays(-2));

        var deleted = store.CleanupOrphans(TimeSpan.FromHours(24));

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldWav));
        Assert.True(File.Exists(freshWav));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
