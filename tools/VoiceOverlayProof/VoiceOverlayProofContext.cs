using System.Globalization;

namespace VoiceOverlayProof;

internal sealed class VoiceOverlayProofContext : ApplicationContext
{
    private static readonly TimeSpan MaximumProofDuration = TimeSpan.FromMinutes(10);

    private readonly TemporaryRecordingStore _store = new();
    private readonly AudioLevelBuffer _levels = new(48);
    private readonly RecordingOverlayForm _overlay;
    private readonly FixtureRecordingOptions? _fixtureOptions;
    private readonly FixtureRecordingStore? _fixtureStore;
    private readonly FixturePromptForm? _fixturePrompt;
    private readonly HotKeyWindow _hotKeyWindow = new();
    private readonly System.Windows.Forms.Timer _exitTimer = new();
    private RecordingSession? _session;
    private ProofState _state = ProofState.Starting;
    private int _rejectedHotKeyCount;

    public VoiceOverlayProofContext(FixtureRecordingOptions? fixtureOptions = null)
    {
        _fixtureOptions = fixtureOptions;
        if (fixtureOptions is not null)
        {
            _fixtureStore = new FixtureRecordingStore(fixtureOptions.SessionId);
            _fixturePrompt = new FixturePromptForm(fixtureOptions);
        }

        _overlay = new RecordingOverlayForm(_levels, MaximumProofDuration);
        MainForm = _overlay;

        _hotKeyWindow.Pressed += OnHotKeyPressed;
        _overlay.MaximumDurationReached += OnMaximumDurationReached;
        _overlay.CancelRequested += OnCancelRequested;

        _exitTimer.Interval = 2_000;
        _exitTimer.Tick += (_, _) =>
        {
            _exitTimer.Stop();
            _overlay.Close();
            ExitThread();
        };

        try
        {
            _hotKeyWindow.Register();
            _state = ProofState.Idle;
            _overlay.ShowReady();
            _fixturePrompt?.ShowAtTopCenter();
            Console.WriteLine("HOTKEY_REGISTERED=True");
            Console.WriteLine("HOTKEY=Ctrl+Shift+G");
            Console.WriteLine("NOACTIVATE_STYLE=True");
            Console.WriteLine("PROOF_READY=True");
            if (_fixtureOptions is not null)
            {
                Console.WriteLine($"FIXTURE_ID={_fixtureOptions.Fixture.Id}");
                Console.WriteLine($"FIXTURE_TAKE={_fixtureOptions.Take:00}");
                Console.WriteLine("FIXTURE_LOCAL_ONLY=True");
            }
        }
        catch (Exception exception)
        {
            ExitCode = 2;
            _state = ProofState.Failed;
            _overlay.ShowFailure("Ctrl+Shift+G не удалось зарегистрировать");
            Console.WriteLine("HOTKEY_REGISTERED=False");
            Console.WriteLine($"FAILURE_TYPE={exception.GetType().Name}");
            _exitTimer.Start();
        }
    }

    public int ExitCode { get; private set; }

    private async void OnHotKeyPressed(object? sender, EventArgs eventArgs)
    {
        switch (_state)
        {
            case ProofState.Idle:
                await StartRecordingAsync();
                break;
            case ProofState.Recording:
                await StopRecordingAsync(autoStopped: false);
                break;
            default:
                _rejectedHotKeyCount++;
                Console.WriteLine($"HOTKEY_REJECTED_STATE={_state}");
                break;
        }
    }

    private async void OnMaximumDurationReached(object? sender, EventArgs eventArgs)
    {
        if (_state == ProofState.Recording)
        {
            await StopRecordingAsync(autoStopped: true);
        }
    }

    private async void OnCancelRequested(object? sender, EventArgs eventArgs)
    {
        if (_state == ProofState.Recording)
        {
            await CancelRecordingAsync();
            return;
        }

        _rejectedHotKeyCount++;
        Console.WriteLine($"CANCEL_REJECTED_STATE={_state}");
    }

    private Task StartRecordingAsync()
    {
        try
        {
            _state = ProofState.Starting;
            _levels.Clear();
            _session = RecordingSession.Start(_store, _levels);
            _state = ProofState.Recording;
            _overlay.ShowRecording();
            Console.WriteLine("RECORDING_STARTED=True");
            Console.WriteLine($"CAPTURE_FORMAT={_session.CaptureFormat}");
        }
        catch (Exception exception)
        {
            ExitCode = 3;
            _state = ProofState.Failed;
            _overlay.ShowFailure("Микрофон недоступен");
            Console.WriteLine("RECORDING_STARTED=False");
            Console.WriteLine($"FAILURE_TYPE={exception.GetType().Name}");
            _exitTimer.Start();
        }

        return Task.CompletedTask;
    }

    private async Task StopRecordingAsync(bool autoStopped)
    {
        if (_session is null || _state != ProofState.Recording)
        {
            _rejectedHotKeyCount++;
            return;
        }

        _state = ProofState.Stopping;
        _overlay.ShowProcessing();

        try
        {
            var result = await _session.StopAndNormalizeAsync();
            _state = ProofState.Completed;

            Console.WriteLine("RECORDING_STOPPED=True");
            Console.WriteLine($"AUTO_STOPPED={autoStopped}");
            Console.WriteLine($"PACKETS={result.PacketCount}");
            Console.WriteLine($"NON_SILENT_PACKETS={result.NonSilentPackets}");
            Console.WriteLine($"MAX_PEAK={result.MaximumPeak.ToString("F4", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"CAPTURED_BYTES={result.CapturedBytes}");
            Console.WriteLine($"NATIVE_WAV_BYTES={result.NativeBytes}");
            Console.WriteLine($"NATIVE_WAV_FORMAT={result.NativeFormat}");
            Console.WriteLine($"CANONICAL_WAV_BYTES={result.CanonicalBytes}");
            Console.WriteLine($"CANONICAL_WAV_FORMAT={result.CanonicalFormat}");
            Console.WriteLine($"STOP_MS={result.StopMilliseconds}");
            Console.WriteLine($"NORMALIZE_MS={result.NormalizationMilliseconds}");
            Console.WriteLine($"HOTKEY_REJECTED_COUNT={_rejectedHotKeyCount}");

            if (_fixtureOptions is not null && _fixtureStore is not null)
            {
                var fixturePath = _fixtureStore.Preserve(
                    _store,
                    result.CanonicalPath,
                    _fixtureOptions.Fixture.Id,
                    _fixtureOptions.Take);
                Console.WriteLine("FIXTURE_AUDIO_PRESERVED=True");
                Console.WriteLine($"FIXTURE_AUDIO_PATH={fixturePath}");
                Console.WriteLine("TRANSCRIPTION_ATTEMPTED=False");
            }
            else
            {
                _store.DeleteOwnedFile(result.CanonicalPath);
                Console.WriteLine($"LOCAL_CLEANUP_OK={!File.Exists(result.CanonicalPath)}");
            }

            await _session.DisposeAsync();
            _session = null;
            _overlay.ShowFinished();
            _exitTimer.Start();
        }
        catch (Exception exception)
        {
            ExitCode = 4;
            _state = ProofState.Failed;
            _overlay.ShowFailure("Не удалось завершить запись");
            Console.WriteLine("RECORDING_STOPPED=False");
            Console.WriteLine($"FAILURE_TYPE={exception.GetType().Name}");

            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            _exitTimer.Start();
        }
    }

    private async Task CancelRecordingAsync()
    {
        if (_session is null || _state != ProofState.Recording)
        {
            _rejectedHotKeyCount++;
            return;
        }

        _state = ProofState.Cancelling;
        _overlay.ShowCancelling();

        try
        {
            var nativePath = _session.NativePath;
            var canonicalPath = _session.CanonicalPath;
            await _session.CancelAsync();
            await _session.DisposeAsync();
            _session = null;
            _state = ProofState.Cancelled;

            Console.WriteLine("CANCELLED=True");
            Console.WriteLine("TRANSCRIPTION_ATTEMPTED=False");
            Console.WriteLine($"LOCAL_CLEANUP_OK={!File.Exists(nativePath) && !File.Exists(canonicalPath)}");
            _overlay.ShowCancelled();
            _exitTimer.Start();
        }
        catch (Exception exception)
        {
            ExitCode = 5;
            _state = ProofState.Failed;
            _overlay.ShowFailure("Не удалось отменить запись безопасно");
            Console.WriteLine("CANCELLED=False");
            Console.WriteLine($"FAILURE_TYPE={exception.GetType().Name}");

            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            _exitTimer.Start();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exitTimer.Dispose();
            _hotKeyWindow.Pressed -= OnHotKeyPressed;
            _hotKeyWindow.Dispose();
            _overlay.MaximumDurationReached -= OnMaximumDurationReached;
            _overlay.CancelRequested -= OnCancelRequested;
            _fixturePrompt?.Dispose();

            if (_session is not null)
            {
                _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _session = null;
            }
        }

        base.Dispose(disposing);
    }

    private enum ProofState
    {
        Starting,
        Idle,
        Recording,
        Stopping,
        Cancelling,
        Completed,
        Cancelled,
        Failed
    }
}
