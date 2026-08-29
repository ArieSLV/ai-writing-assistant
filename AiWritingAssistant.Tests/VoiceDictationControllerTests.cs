using AiWritingAssistant.Audio;
using AiWritingAssistant.Credentials;
using AiWritingAssistant.Transcription;
using AiWritingAssistant.UI;

namespace AiWritingAssistant.Tests;

public sealed class VoiceDictationControllerTests
{
    [Fact]
    public async Task Successful_flow_copies_text_and_removes_local_audio()
    {
        await using var harness = new Harness();

        await harness.Controller.ToggleAsync();
        Assert.Equal(VoiceDictationState.Recording, harness.Controller.State);
        Assert.True(harness.View.RecordingShown);

        await harness.Controller.ToggleAsync();

        Assert.Equal(VoiceDictationState.Idle, harness.Controller.State);
        Assert.Equal("dictated text", harness.Clipboard.Text);
        Assert.Equal(1, harness.Transcription.Calls);
        Assert.Equal(VoiceDictationOutcome.Succeeded, Assert.Single(harness.Outcomes).Outcome);
        Assert.True(harness.Events.IndexOf("overlay-hide") < harness.Events.IndexOf("recording-stop"));
        Assert.True(harness.Events.IndexOf("clipboard-write") < harness.Events.IndexOf("outcome-Succeeded"));
        Assert.Empty(Directory.EnumerateFiles(harness.Root, "*.wav"));
    }

    [Fact]
    public async Task Cancel_during_recording_skips_transcription_and_clipboard()
    {
        await using var harness = new Harness();
        await harness.Controller.ToggleAsync();

        await harness.Controller.CancelAsync();

        Assert.Equal(VoiceDictationState.Idle, harness.Controller.State);
        Assert.True(harness.RecordingService.Session.Cancelled);
        Assert.Equal(0, harness.Transcription.Calls);
        Assert.Null(harness.Clipboard.Text);
        Assert.Equal(VoiceDictationOutcome.Cancelled, Assert.Single(harness.Outcomes).Outcome);
        Assert.True(harness.Events.IndexOf("overlay-hide") < harness.Events.IndexOf("recording-cancel"));
        Assert.Empty(Directory.EnumerateFiles(harness.Root, "*.wav"));
    }

    [Fact]
    public async Task Transcription_failure_preserves_clipboard_and_cleans_audio()
    {
        await using var harness = new Harness();
        harness.Transcription.Exception = new TranscriptionClientException("controlled failure", 429);
        await harness.Controller.ToggleAsync();

        await harness.Controller.ToggleAsync();

        Assert.Equal(VoiceDictationState.Idle, harness.Controller.State);
        Assert.Null(harness.Clipboard.Text);
        var outcome = Assert.Single(harness.Outcomes);
        Assert.Equal(VoiceDictationOutcome.Failed, outcome.Outcome);
        Assert.Equal("controlled failure", outcome.UserMessage);
        Assert.Empty(Directory.EnumerateFiles(harness.Root, "*.wav"));
    }

    [Fact]
    public async Task Shutdown_during_recording_cancels_capture_and_removes_audio()
    {
        await using var harness = new Harness();
        await harness.Controller.ToggleAsync();

        await harness.Controller.DisposeAsync();
        var secondDisposeFailure = await Record.ExceptionAsync(() =>
            harness.Controller.DisposeAsync().AsTask());

        Assert.Null(secondDisposeFailure);
        Assert.Equal(VoiceDictationState.Disposed, harness.Controller.State);
        Assert.True(harness.RecordingService.Session.Cancelled);
        Assert.Empty(Directory.EnumerateFiles(harness.Root, "*.wav"));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ApplicationOperationCoordinator _coordinator = new();
        private readonly string _loggerPath;

        public Harness()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "AiWritingAssistant.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _loggerPath = Path.Combine(Root, "test.log");
            RecordingService = new FakeRecordingService(Events);
            Transcription = new FakeTranscriptionClient();
            Clipboard = new FakeClipboardService(Events);
            View = new FakeView(Events);
            var credentials = new GoogleCredentialProvider(name =>
                name == GoogleCredentialProvider.VoiceEnvironmentVariable ? "voice-key" : null);

            Controller = new VoiceDictationController(
                _coordinator,
                credentials,
                RecordingService,
                new FakeNormalizer(),
                Transcription,
                Clipboard,
                new TemporaryRecordingStore(Root),
                new AudioLevelBuffer(),
                View,
                new AppSettings(),
                new AppLogger(_loggerPath));
            Controller.OutcomeReported += (_, eventArgs) =>
            {
                Outcomes.Add(eventArgs);
                Events.Add($"outcome-{eventArgs.Outcome}");
            };
        }

        public string Root { get; }

        public VoiceDictationController Controller { get; }

        public FakeRecordingService RecordingService { get; }

        public FakeTranscriptionClient Transcription { get; }

        public FakeClipboardService Clipboard { get; }

        public FakeView View { get; }

        public List<string> Events { get; } = new();

        public List<VoiceDictationOutcomeEventArgs> Outcomes { get; } = new();

        public async ValueTask DisposeAsync()
        {
            if (Controller.State != VoiceDictationState.Disposed)
                await Controller.DisposeAsync();
            _coordinator.Dispose();

            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeRecordingService(List<string> events) : IAudioRecordingService
    {
        public FakeRecordingSession Session { get; private set; } = null!;

        public IAudioRecordingSession Start(string outputPath, AudioLevelBuffer levels)
        {
            File.WriteAllBytes(outputPath, new byte[256]);
            Session = new FakeRecordingSession(outputPath, events);
            return Session;
        }
    }

    private sealed class FakeRecordingSession(
        string outputPath,
        List<string> events) : IAudioRecordingSession
    {
        public string OutputPath { get; } = outputPath;

        public bool Cancelled { get; private set; }

        public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("recording-stop");
            return Task.FromResult(new AudioCaptureResult(
                OutputPath,
                "fake",
                10,
                256,
                TimeSpan.FromSeconds(1),
                0.5f));
        }

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("recording-cancel");
            Cancelled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeNormalizer : IAudioFileNormalizer
    {
        public Task<NormalizedAudioFile> NormalizeAsync(
            string nativePath,
            string canonicalPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllBytes(canonicalPath, new byte[128]);
            return Task.FromResult(new NormalizedAudioFile(canonicalPath, "fake", "canonical", 128));
        }
    }

    private sealed class FakeTranscriptionClient : ITranscriptionClient
    {
        public int Calls { get; private set; }

        public Exception? Exception { get; set; }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            TranscriptionOptions options,
            IProgress<TranscriptionStage>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(TranscriptionStage.Uploading);
            progress?.Report(TranscriptionStage.Transcribing);

            if (Exception is not null)
                return Task.FromException<TranscriptionResult>(Exception);

            return Task.FromResult(new TranscriptionResult(
                "dictated text",
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class FakeClipboardService(List<string> events) : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("clipboard-write");
            Text = text;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeView(List<string> events) : IVoiceDictationView
    {
        public event EventHandler? MaximumDurationReached;

        public event EventHandler? CancelRequested;

        public bool RecordingShown { get; private set; }

        public void ShowRecording()
        {
            RecordingShown = true;
            events.Add("overlay-show");
        }

        public void HideOverlay()
        {
            events.Add("overlay-hide");
        }

        public void Dispose() { }

        public void RaiseMaximumDuration() => MaximumDurationReached?.Invoke(this, EventArgs.Empty);

        public void RaiseCancel() => CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
