using AiWritingAssistant.Audio;
using AiWritingAssistant.Credentials;
using AiWritingAssistant.Transcription;
using AiWritingAssistant.UI;

namespace AiWritingAssistant;

internal enum VoiceDictationState
{
    Idle,
    Starting,
    Recording,
    Stopping,
    Normalizing,
    Uploading,
    WaitingForFile,
    Transcribing,
    Copying,
    Cancelling,
    Disposed
}

internal sealed class VoiceDictationStateChangedEventArgs(VoiceDictationState state) : EventArgs
{
    public VoiceDictationState State { get; } = state;
}

internal sealed class VoiceDictationController : IAsyncDisposable
{
    private readonly ApplicationOperationCoordinator _coordinator;
    private readonly IGoogleCredentialProvider _credentialProvider;
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioFileNormalizer _normalizer;
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly IClipboardService _clipboardService;
    private readonly TemporaryRecordingStore _recordingStore;
    private readonly AudioLevelBuffer _levels;
    private readonly IVoiceDictationView _view;
    private readonly AppSettings _settings;
    private readonly AppLogger _logger;
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    private ApplicationOperationLease? _lease;
    private CancellationTokenSource? _sessionCancellation;
    private IAudioRecordingSession? _recording;
    private string? _nativePath;
    private string? _canonicalPath;
    private Task? _terminalTask;
    private bool _disposed;

    public VoiceDictationController(
        ApplicationOperationCoordinator coordinator,
        IGoogleCredentialProvider credentialProvider,
        IAudioRecordingService recordingService,
        IAudioFileNormalizer normalizer,
        ITranscriptionClient transcriptionClient,
        IClipboardService clipboardService,
        TemporaryRecordingStore recordingStore,
        AudioLevelBuffer levels,
        IVoiceDictationView view,
        AppSettings settings,
        AppLogger logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _transcriptionClient = transcriptionClient ?? throw new ArgumentNullException(nameof(transcriptionClient));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _recordingStore = recordingStore ?? throw new ArgumentNullException(nameof(recordingStore));
        _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _view.CancelRequested += OnCancelRequested;
        _view.MaximumDurationReached += OnMaximumDurationReached;

        try
        {
            var deleted = _recordingStore.CleanupOrphans(TimeSpan.FromHours(24));
            if (deleted > 0)
                _logger.Info($"Deleted {deleted} orphaned Voice recording(s) during startup cleanup.");
        }
        catch (Exception exception)
        {
            _logger.Warning("Voice orphan cleanup failed.", exception);
        }
    }

    public event EventHandler<VoiceDictationStateChangedEventArgs>? StateChanged;

    public event EventHandler<VoiceDictationOutcomeEventArgs>? OutcomeReported;

    public VoiceDictationState State { get; private set; } = VoiceDictationState.Idle;

    public async Task ToggleAsync()
    {
        Task? terminalTask = null;
        await _commandGate.WaitAsync();

        try
        {
            if (_disposed)
                return;

            switch (State)
            {
                case VoiceDictationState.Idle:
                    StartCore();
                    break;
                case VoiceDictationState.Recording:
                    terminalTask = _terminalTask ??= StopAndTranscribeCoreAsync();
                    break;
                default:
                    _logger.Info($"Voice toggle ignored while state={State}.");
                    break;
            }
        }
        finally
        {
            _commandGate.Release();
        }

        if (terminalTask is not null)
            await terminalTask;
    }

    public async Task StopAsync()
    {
        Task? terminalTask = null;
        await _commandGate.WaitAsync();

        try
        {
            if (!_disposed && State == VoiceDictationState.Recording)
                terminalTask = _terminalTask ??= StopAndTranscribeCoreAsync();
        }
        finally
        {
            _commandGate.Release();
        }

        if (terminalTask is not null)
            await terminalTask;
    }

    public async Task CancelAsync()
    {
        Task? terminalTask = null;
        await _commandGate.WaitAsync();

        try
        {
            if (_disposed || State == VoiceDictationState.Idle)
                return;

            if (State == VoiceDictationState.Recording)
            {
                terminalTask = _terminalTask ??= CancelRecordingCoreAsync();
            }
            else
            {
                SetState(VoiceDictationState.Cancelling);
                _view.HideOverlay();
                _sessionCancellation?.Cancel();
                terminalTask = _terminalTask;
            }
        }
        finally
        {
            _commandGate.Release();
        }

        if (terminalTask is not null)
            await terminalTask;
    }

    private void StartCore()
    {
        SetState(VoiceDictationState.Starting);

        if (!_coordinator.TryAcquire(ApplicationOperation.Voice, out _lease))
        {
            const string message = "Another operation is already running";
            _view.HideOverlay();
            SetState(VoiceDictationState.Idle);
            ReportOutcome(VoiceDictationOutcome.Failed, message);
            return;
        }

        try
        {
            _settings.Normalize();
            _ = _credentialProvider.GetRequiredCredential(GoogleCredentialPurpose.VoiceTranscription);
            _levels.Clear();
            _nativePath = _recordingStore.CreateWavPath("native");
            _canonicalPath = _recordingStore.CreateWavPath("canonical");
            _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lease!.CancellationToken);
            _recording = _recordingService.Start(_nativePath, _levels);
            SetState(VoiceDictationState.Recording);
            _view.ShowRecording();
            _logger.Info("Voice recording started with the Windows default input device.");
        }
        catch (Exception exception)
        {
            _logger.Error("Voice recording could not start.", exception);
            var message = GetUserMessage(exception);
            _view.HideOverlay();
            CleanupLocalFiles();
            ReleaseSession();
            SetState(VoiceDictationState.Idle);
            ReportOutcome(VoiceDictationOutcome.Failed, message);
        }
    }

    private async Task StopAndTranscribeCoreAsync()
    {
        try
        {
            if (_recording is null || _lease is null || _sessionCancellation is null ||
                string.IsNullOrWhiteSpace(_nativePath) || string.IsNullOrWhiteSpace(_canonicalPath))
            {
                throw new InvalidOperationException("Voice recording session is incomplete.");
            }

            if (!_lease.TryEnterVoiceProcessing())
                throw new InvalidOperationException("Voice operation could not enter processing state.");

            _view.HideOverlay();
            SetState(VoiceDictationState.Stopping);
            var capture = await _recording.StopAsync(_sessionCancellation.Token);

            SetState(VoiceDictationState.Normalizing);
            var normalized = await _normalizer.NormalizeAsync(
                capture.Path,
                _canonicalPath,
                _sessionCancellation.Token);
            _recordingStore.DeleteOwnedFile(_nativePath);

            var progress = new Progress<TranscriptionStage>(stage =>
            {
                SetState(stage switch
                {
                    TranscriptionStage.Uploading => VoiceDictationState.Uploading,
                    TranscriptionStage.WaitingForFile => VoiceDictationState.WaitingForFile,
                    TranscriptionStage.Transcribing => VoiceDictationState.Transcribing,
                    _ => State
                });
            });
            var transcription = await _transcriptionClient.TranscribeAsync(
                normalized.Path,
                TranscriptionOptions.CreateDefault(_settings.VoiceTranscriptionModel),
                progress,
                _sessionCancellation.Token);

            SetState(VoiceDictationState.Copying);
            await _clipboardService.SetTextAsync(transcription.Text, _sessionCancellation.Token);
            _logger.Info(
                $"Voice dictation completed. DurationMs={capture.Duration.TotalMilliseconds:F0}, " +
                $"AudioBytes={normalized.Bytes}, ResultLength={transcription.Text.Length}, " +
                $"PostStopMs={transcription.TotalDuration.TotalMilliseconds:F0}.");
            ReportOutcome(VoiceDictationOutcome.Succeeded, "Voice text copied to clipboard");
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Voice dictation was cancelled.");
            _view.HideOverlay();
            ReportOutcome(VoiceDictationOutcome.Cancelled, "Voice dictation cancelled");
        }
        catch (Exception exception)
        {
            _logger.Error("Voice dictation failed.", exception);
            _view.HideOverlay();
            ReportOutcome(VoiceDictationOutcome.Failed, GetUserMessage(exception));
        }
        finally
        {
            await CompleteSessionCleanupAsync();
        }
    }

    private async Task CancelRecordingCoreAsync()
    {
        SetState(VoiceDictationState.Cancelling);
        _view.HideOverlay();

        try
        {
            if (_recording is not null)
            {
                using var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _recording.CancelAsync(cleanupCancellation.Token);
            }

            _logger.Info("Voice recording cancelled before upload.");
            ReportOutcome(VoiceDictationOutcome.Cancelled, "Voice dictation cancelled");
        }
        catch (Exception exception)
        {
            _logger.Warning("Voice recording cancellation completed with an error.", exception);
            ReportOutcome(VoiceDictationOutcome.Failed, "The microphone could not be stopped cleanly");
        }
        finally
        {
            await CompleteSessionCleanupAsync();
        }
    }

    private async Task CompleteSessionCleanupAsync()
    {
        if (_recording is not null)
        {
            try
            {
                await _recording.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.Warning("Voice recorder disposal failed.", exception);
            }
        }

        CleanupLocalFiles();
        ReleaseSession();

        if (!_disposed)
            SetState(VoiceDictationState.Idle);
    }

    private void CleanupLocalFiles()
    {
        DeleteLocalFile(_nativePath);
        DeleteLocalFile(_canonicalPath);
    }

    private void DeleteLocalFile(string? path)
    {
        try
        {
            _recordingStore.DeleteOwnedFile(path);
        }
        catch (Exception exception)
        {
            _logger.Warning("Voice local recording cleanup failed.", exception);
        }
    }

    private void ReleaseSession()
    {
        _recording = null;
        _nativePath = null;
        _canonicalPath = null;
        _terminalTask = null;
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _lease?.Dispose();
        _lease = null;
    }

    private void SetState(VoiceDictationState state)
    {
        State = state;
        StateChanged?.Invoke(this, new VoiceDictationStateChangedEventArgs(state));
    }

    private void ReportOutcome(VoiceDictationOutcome outcome, string userMessage)
    {
        if (_disposed)
            return;

        OutcomeReported?.Invoke(
            this,
            new VoiceDictationOutcomeEventArgs(outcome, userMessage));
    }

    private static string GetUserMessage(Exception exception)
    {
        return exception switch
        {
            NoUsableAudioException => "No audio was recorded",
            TranscriptionClientException transcription => transcription.Message,
            InvalidOperationException invalidOperation when
                invalidOperation.Message.Contains(GoogleCredentialProvider.VoiceEnvironmentVariable, StringComparison.Ordinal) =>
                "Google Voice API key is not configured",
            _ => "Voice dictation failed"
        };
    }

    private async void OnCancelRequested(object? sender, EventArgs eventArgs)
    {
        await CancelAsync();
    }

    private async void OnMaximumDurationReached(object? sender, EventArgs eventArgs)
    {
        await StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        Task? terminalTask;
        await _commandGate.WaitAsync();

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            _coordinator.BeginShutdown();
            _sessionCancellation?.Cancel();

            if (State == VoiceDictationState.Recording)
                terminalTask = _terminalTask ??= CancelRecordingCoreAsync();
            else
                terminalTask = _terminalTask;
        }
        finally
        {
            _commandGate.Release();
        }

        if (terminalTask is not null)
        {
            try
            {
                await terminalTask.WaitAsync(TimeSpan.FromSeconds(12));
            }
            catch (Exception exception)
            {
                _logger.Warning("Voice shutdown cleanup exceeded its normal path.", exception);
            }
        }

        CleanupLocalFiles();
        ReleaseSession();
        _view.HideOverlay();
        _view.CancelRequested -= OnCancelRequested;
        _view.MaximumDurationReached -= OnMaximumDurationReached;
        _view.Dispose();

        if (_transcriptionClient is IDisposable disposableClient)
            disposableClient.Dispose();

        SetState(VoiceDictationState.Disposed);
        _commandGate.Dispose();
    }
}
