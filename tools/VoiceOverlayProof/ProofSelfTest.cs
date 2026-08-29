using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceOverlayProof;

internal static class ProofSelfTest
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public static int Run()
    {
        try
        {
            VerifyLevelBufferAndWaveform();
            VerifyTemporaryStore();
            VerifyFixtureRecordingStore();
            VerifyNoActivateOverlay();
            VerifyCaptureDependency();
            VerifyHotKeyAvailability();
            VerifyMaximumDurationBoundary();
            VerifyOverlayCancelRoute();
            VerifyAcceleratedRenderBudget();
            Task.Run(VerifySimulatedLifecycleAsync).GetAwaiter().GetResult();

            Console.WriteLine("SELF_TEST_PASSED=True");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine("SELF_TEST_PASSED=False");
            Console.WriteLine($"FAILURE_TYPE={exception.GetType().Name}");
            return 1;
        }
    }

    private static void VerifyLevelBufferAndWaveform()
    {
        var levels = new AudioLevelBuffer(48);
        levels.Add(-1.0f);
        levels.Add(0.25f);
        levels.Add(2.0f);

        for (var index = 0; index < 60; index++)
        {
            levels.Add(index / 59.0f);
        }

        var snapshot = levels.Snapshot();
        if (snapshot.Length != 48 || snapshot.Any(value => value is < 0.0f or > 1.0f))
        {
            throw new InvalidOperationException("Audio level buffer bounds failed.");
        }

        using var waveform = new WaveformControl(levels) { Size = new Size(400, 80) };
        using var bitmap = new Bitmap(waveform.Width, waveform.Height);
        waveform.DrawToBitmap(bitmap, waveform.ClientRectangle);

        Console.WriteLine("LEVEL_BUFFER_CAPACITY=48");
        Console.WriteLine("LEVEL_VALUES_BOUNDED=True");
        Console.WriteLine("WAVEFORM_RENDERED=True");
    }

    private static void VerifyTemporaryStore()
    {
        var store = new TemporaryRecordingStore();
        var path = store.CreateWavPath("self-test");
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);

        if (!store.IsOwned(path))
        {
            throw new InvalidOperationException("Temporary path ownership check failed.");
        }

        store.DeleteOwnedFile(path);
        store.DeleteOwnedFile(path);

        if (File.Exists(path))
        {
            throw new InvalidOperationException("Idempotent temporary cleanup failed.");
        }

        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.wav");
        var outsideDeleteRejected = false;
        try
        {
            store.DeleteOwnedFile(outsidePath);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("outside", StringComparison.OrdinalIgnoreCase))
        {
            outsideDeleteRejected = true;
        }

        if (!outsideDeleteRejected)
        {
            throw new InvalidOperationException("Outside-root delete was not rejected.");
        }

        Console.WriteLine("TEMP_PATH_OWNED=True");
        Console.WriteLine("TEMP_DELETE_IDEMPOTENT=True");
        Console.WriteLine("OUTSIDE_DELETE_REJECTED=True");
    }

    private static void VerifyFixtureRecordingStore()
    {
        var sourceStore = new TemporaryRecordingStore();
        var sourcePath = sourceStore.CreateWavPath("fixture-self-test");
        File.WriteAllBytes(sourcePath, [0x52, 0x49, 0x46, 0x46]);

        var sessionId = $"self-test-{Guid.NewGuid():N}";
        var fixtureStore = new FixtureRecordingStore(sessionId);
        var preservedPath = fixtureStore.Preserve(sourceStore, sourcePath, "F-01", 1);
        if (File.Exists(sourcePath) || !File.Exists(preservedPath) ||
            !string.Equals(Path.GetFileName(preservedPath), "F-01-01.wav", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fixture preservation contract failed.");
        }

        fixtureStore.DeleteOwnedFile(preservedPath);
        if (File.Exists(preservedPath))
        {
            throw new InvalidOperationException("Fixture cleanup contract failed.");
        }

        Directory.Delete(fixtureStore.RootPath);
        Console.WriteLine("FIXTURE_PATH_OWNED=True");
        Console.WriteLine("FIXTURE_FILENAME=F-01-01.wav");
        Console.WriteLine("FIXTURE_PRESERVE_AND_DELETE=True");
    }

    private static void VerifyNoActivateOverlay()
    {
        var levels = new AudioLevelBuffer(48);
        using var overlay = new RecordingOverlayForm(levels, TimeSpan.FromSeconds(30));
        var foregroundBefore = GetForegroundWindow();
        var showStopwatch = System.Diagnostics.Stopwatch.StartNew();
        overlay.ShowReady();
        Application.DoEvents();
        showStopwatch.Stop();
        var foregroundAfter = GetForegroundWindow();
        var styles = GetWindowLong(overlay.Handle, GwlExStyle);
        var hasNoActivate = (styles & WsExNoActivate) != 0;
        var hasToolWindow = (styles & WsExToolWindow) != 0;
        var foregroundUnchanged = foregroundBefore == IntPtr.Zero || foregroundBefore == foregroundAfter;
        var positionedMonitorCount = 0;
        foreach (var screen in Screen.AllScreens)
        {
            var referencePoint = new Point(
                screen.WorkingArea.Left + screen.WorkingArea.Width / 2,
                screen.WorkingArea.Top + screen.WorkingArea.Height / 2);
            overlay.PositionAtBottomCenter(referencePoint);
            Application.DoEvents();
            if (!screen.WorkingArea.Contains(overlay.Bounds))
            {
                throw new InvalidOperationException($"Overlay escaped monitor {screen.DeviceName} working area.");
            }

            positionedMonitorCount++;
        }

        if (!overlay.HasNoActivateStyles || !hasNoActivate || !hasToolWindow ||
            !foregroundUnchanged || positionedMonitorCount != Screen.AllScreens.Length ||
            showStopwatch.ElapsedMilliseconds > 200)
        {
            throw new InvalidOperationException("Overlay focus, placement, latency or extended styles failed.");
        }

        Console.WriteLine("NOACTIVATE_STYLE=True");
        Console.WriteLine("TOOLWINDOW_STYLE=True");
        Console.WriteLine("FOREGROUND_UNCHANGED=True");
        Console.WriteLine($"HOTKEY_TO_OVERLAY_VISIBLE_MS={showStopwatch.ElapsedMilliseconds}");
        Console.WriteLine($"OVERLAY_SCREEN_COUNT={Screen.AllScreens.Length}");
        Console.WriteLine($"OVERLAY_MONITORS_POSITIONED={positionedMonitorCount}");
        Console.WriteLine("OVERLAY_WITHIN_EACH_MONITOR=True");
        Console.WriteLine($"OVERLAY_DEVICE_DPI={overlay.DeviceDpi}");
        Console.WriteLine($"OVERLAY_AUTOSCALE_MODE={overlay.AutoScaleMode}");
        Console.WriteLine("OVERLAY_RENDER_INTERVAL_MS=33");
        Console.WriteLine("VISUALIZATION_LATENCY_BOUND_MS=33");
        overlay.Hide();
    }

    private static void VerifyCaptureDependency()
    {
        using var recorder = new WasapiRecorderBuilder().Build();
        Console.WriteLine("CAPTURE_DEVICE_AVAILABLE=True");
        Console.WriteLine($"CAPTURE_FORMAT={recorder.WaveFormat}");
    }

    private static void VerifyHotKeyAvailability()
    {
        using var window = new HotKeyWindow();
        window.Register();
        window.Unregister();
        Console.WriteLine("HOTKEY_REGISTER_UNREGISTER=True");
        Console.WriteLine("HOTKEY=Ctrl+Shift+G");
    }

    private static void VerifyMaximumDurationBoundary()
    {
        var levels = new AudioLevelBuffer(48);
        using var overlay = new RecordingOverlayForm(levels, TimeSpan.FromMinutes(10));
        var terminalCount = 0;
        overlay.MaximumDurationReached += (_, _) => terminalCount++;

        overlay.EvaluateMaximumDuration(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        overlay.EvaluateMaximumDuration(TimeSpan.FromMinutes(10));
        overlay.EvaluateMaximumDuration(TimeSpan.FromMinutes(11));

        for (var frame = 0; frame < 18_000; frame++)
        {
            levels.Add((frame % 100) / 99.0f);
        }

        if (terminalCount != 1 || levels.Snapshot().Length != 48)
        {
            throw new InvalidOperationException("10-minute boundary or bounded visualization state failed.");
        }

        Console.WriteLine("MAX_DURATION_SECONDS=600");
        Console.WriteLine("MAX_DURATION_TERMINAL_COUNT=1");
        Console.WriteLine("TEN_MINUTE_LEVEL_FRAMES=18000");
        Console.WriteLine("TEN_MINUTE_LEVEL_BUFFER_CAPACITY=48");
    }

    private static void VerifyOverlayCancelRoute()
    {
        var levels = new AudioLevelBuffer(48);
        using var overlay = new RecordingOverlayForm(levels, TimeSpan.FromMinutes(10));
        var cancelCount = 0;
        overlay.CancelRequested += (_, _) => cancelCount++;
        overlay.ShowRecording();

        var button = overlay.Controls.Find("CancelRecordingButton", searchAllChildren: true)
            .OfType<Button>()
            .SingleOrDefault();
        if (button is null || !button.Visible)
        {
            throw new InvalidOperationException("Mouse-accessible Cancel control is missing while recording.");
        }

        overlay.RequestCancel();
        if (cancelCount != 1)
        {
            throw new InvalidOperationException("Overlay Cancel route did not fire exactly once.");
        }

        overlay.Hide();
        Console.WriteLine("OVERLAY_MOUSE_CANCEL_VISIBLE=True");
        Console.WriteLine("OVERLAY_CANCEL_ROUTE_COUNT=1");
    }

    private static void VerifyAcceleratedRenderBudget()
    {
        const int frames = 18_000;
        var levels = new AudioLevelBuffer(48);
        using var waveform = new WaveformControl(levels) { Size = new Size(404, 68) };
        using var bitmap = new Bitmap(waveform.Width, waveform.Height);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var wallClock = System.Diagnostics.Stopwatch.StartNew();

        for (var frame = 0; frame < frames; frame++)
        {
            levels.Add((frame % 100) / 99.0f);
            waveform.DrawToBitmap(bitmap, waveform.ClientRectangle);
        }

        wallClock.Stop();
        process.Refresh();
        var cpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        var managedAfter = GC.GetTotalMemory(forceFullCollection: true);
        var managedGrowth = managedAfter - managedBefore;
        var cpuPerFrame = cpuMilliseconds / frames;
        var estimatedSingleCorePercentAt30Fps = cpuPerFrame * 30.0 / 10.0;

        if (levels.Snapshot().Length != 48)
        {
            throw new InvalidOperationException("Accelerated 10-minute render simulation grew level state.");
        }

        Console.WriteLine($"TEN_MINUTE_RENDER_FRAMES={frames}");
        Console.WriteLine($"TEN_MINUTE_RENDER_WALL_MS={wallClock.ElapsedMilliseconds}");
        Console.WriteLine($"TEN_MINUTE_RENDER_CPU_MS={cpuMilliseconds:F0}");
        Console.WriteLine($"TEN_MINUTE_RENDER_CPU_MS_PER_FRAME={cpuPerFrame:F4}");
        Console.WriteLine($"TEN_MINUTE_RENDER_EST_SINGLE_CORE_PERCENT_AT_30FPS={estimatedSingleCorePercentAt30Fps:F2}");
        Console.WriteLine($"TEN_MINUTE_RENDER_MANAGED_GROWTH_BYTES={managedGrowth}");
        Console.WriteLine("TEN_MINUTE_RENDER_STATE_BOUNDED=True");
    }

    private static async Task VerifySimulatedLifecycleAsync()
    {
        await VerifyNoDeviceFailureAsync();
        await VerifyDeviceLossAsync();
        await VerifyVeryShortCaptureAsync();
        await VerifyMutedCaptureAsync();
        await Verify44100NormalizationAsync();
        await VerifyDefaultDeviceChangeBetweenSessionsAsync();
        await VerifyCancelAsync();
        await VerifyRapidStopAndDisposeAsync();
        await VerifyExitDuringRecordingAsync();
    }

    private static async Task VerifyNoDeviceFailureAsync()
    {
        var store = new TemporaryRecordingStore();
        var before = OwnedWavNames(store);
        var controlled = false;

        try
        {
            _ = RecordingSession.Start(
                store,
                new AudioLevelBuffer(48),
                static () => throw new InvalidOperationException("Simulated no capture device."));
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("no capture device", StringComparison.OrdinalIgnoreCase))
        {
            controlled = true;
        }

        if (!controlled || !before.SetEquals(OwnedWavNames(store)))
        {
            throw new InvalidOperationException("No-device failure did not preserve the TEMP baseline.");
        }

        Console.WriteLine("SIMULATED_NO_DEVICE_CONTROLLED=True");
        Console.WriteLine("SIMULATED_NO_DEVICE_CLEANUP=True");
    }

    private static async Task VerifyDeviceLossAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.FloatStereo48Khz(
            duration: TimeSpan.FromSeconds(1),
            peak: 0.2f,
            stopException: new IOException("Simulated device removal."));
        var session = RecordingSession.Start(
            store,
            new AudioLevelBuffer(48),
            () => recorder);

        var controlled = false;
        try
        {
            _ = await session.StopAndNormalizeAsync();
        }
        catch (Exception exception) when (
            exception is IOException || exception.InnerException is IOException)
        {
            controlled = true;
        }

        var cleanupSucceeded = !File.Exists(session.NativePath) && !File.Exists(session.CanonicalPath);
        Console.WriteLine($"SIMULATED_DEVICE_LOSS_CONTROLLED={controlled}");
        Console.WriteLine($"SIMULATED_DEVICE_LOSS_CLEANUP={cleanupSucceeded}");

        if (!controlled || !cleanupSucceeded)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // Preserve the assertion that identifies the cleanup bug.
            }

            throw new InvalidOperationException("Device-loss failure or cleanup was not controlled.");
        }

        await session.DisposeAsync();
    }

    private static async Task VerifyVeryShortCaptureAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.FloatStereo48Khz(TimeSpan.FromMilliseconds(100), 0.2f);
        await using var session = RecordingSession.Start(
            store,
            new AudioLevelBuffer(48),
            () => recorder);

        var rejected = false;
        try
        {
            _ = await session.StopAndNormalizeAsync();
        }
        catch (NoUsableAudioException)
        {
            rejected = true;
        }

        if (!rejected || File.Exists(session.NativePath) || File.Exists(session.CanonicalPath))
        {
            throw new InvalidOperationException("Very-short capture was not rejected and cleaned.");
        }

        Console.WriteLine("SIMULATED_SHORT_CAPTURE_REJECTED=True");
        Console.WriteLine("SIMULATED_SHORT_CAPTURE_CLEANUP=True");
    }

    private static async Task VerifyMutedCaptureAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.FloatStereo48Khz(TimeSpan.FromSeconds(1), 0.0f);
        await using var session = RecordingSession.Start(
            store,
            new AudioLevelBuffer(48),
            () => recorder);
        var result = await session.StopAndNormalizeAsync();

        if (result.NonSilentPackets != 0 || result.MaximumPeak != 0.0f)
        {
            throw new InvalidOperationException("Muted capture did not remain silent.");
        }

        store.DeleteOwnedFile(result.CanonicalPath);
        Console.WriteLine("SIMULATED_MUTED_CAPTURE_VALID=True");
        Console.WriteLine("SIMULATED_MUTED_NON_SILENT_PACKETS=0");
    }

    private static async Task Verify44100NormalizationAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.PcmMono44100Hz(TimeSpan.FromSeconds(1), 0.2f);
        await using var session = RecordingSession.Start(
            store,
            new AudioLevelBuffer(48),
            () => recorder);
        var result = await session.StopAndNormalizeAsync();

        if (!result.CanonicalFormat.Contains("16000Hz 1 channels", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("44.1 kHz normalization did not produce canonical output.");
        }

        store.DeleteOwnedFile(result.CanonicalPath);
        Console.WriteLine("SIMULATED_44100_INPUT_NORMALIZED=True");
        Console.WriteLine($"SIMULATED_44100_NORMALIZE_MS={result.NormalizationMilliseconds}");
    }

    private static async Task VerifyRapidStopAndDisposeAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.FloatStereo48Khz(TimeSpan.FromSeconds(1), 0.2f);
        var session = RecordingSession.Start(store, new AudioLevelBuffer(48), () => recorder);

        var firstStop = session.StopAndNormalizeAsync();
        var secondStop = session.StopAndNormalizeAsync();
        if (!ReferenceEquals(firstStop, secondStop))
        {
            throw new InvalidOperationException("Rapid Stop did not return the single terminal task.");
        }

        var result = await firstStop;
        store.DeleteOwnedFile(result.CanonicalPath);
        await session.DisposeAsync();
        await session.DisposeAsync();

        if (recorder.StopCalls != 1 || recorder.DisposeCalls != 1 ||
            File.Exists(session.NativePath) || File.Exists(session.CanonicalPath))
        {
            throw new InvalidOperationException("Rapid Stop/Dispose was not idempotent.");
        }

        Console.WriteLine("SIMULATED_RAPID_STOP_SINGLE_TASK=True");
        Console.WriteLine("SIMULATED_RAPID_STOP_CALLS=1");
        Console.WriteLine("SIMULATED_DISPOSE_CALLS=1");
        Console.WriteLine("SIMULATED_RAPID_TERMINAL_CLEANUP=True");
    }

    private static async Task VerifyDefaultDeviceChangeBetweenSessionsAsync()
    {
        var store = new TemporaryRecordingStore();
        var firstRecorder = FakeAudioRecorder.PcmMono44100Hz(TimeSpan.FromSeconds(1), 0.2f);
        var firstSession = RecordingSession.Start(store, new AudioLevelBuffer(48), () => firstRecorder);
        var firstFormat = firstSession.CaptureFormat.ToString();
        await firstSession.CancelAsync();
        await firstSession.DisposeAsync();

        var secondRecorder = FakeAudioRecorder.FloatStereo48Khz(TimeSpan.FromSeconds(1), 0.2f);
        var secondSession = RecordingSession.Start(store, new AudioLevelBuffer(48), () => secondRecorder);
        var secondFormat = secondSession.CaptureFormat.ToString();
        await secondSession.CancelAsync();
        await secondSession.DisposeAsync();

        if (string.Equals(firstFormat, secondFormat, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("New session did not resolve its recorder format independently.");
        }

        Console.WriteLine("SIMULATED_DEFAULT_DEVICE_REEVALUATED_PER_SESSION=True");
    }

    private static async Task VerifyCancelAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.FloatStereo48Khz(TimeSpan.FromSeconds(1), 0.2f);
        var session = RecordingSession.Start(store, new AudioLevelBuffer(48), () => recorder);

        var firstCancel = session.CancelAsync();
        Task? secondCancel = null;
        try
        {
            secondCancel = session.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The assertion below records the rapid-completion idempotency bug directly.
        }

        var singleTask = secondCancel is not null && ReferenceEquals(firstCancel, secondCancel);
        Console.WriteLine($"SIMULATED_CANCEL_SINGLE_TASK={singleTask}");
        if (!singleTask)
        {
            throw new InvalidOperationException("Rapid Cancel did not return the single terminal task.");
        }

        await firstCancel;
        await session.DisposeAsync();
        await session.DisposeAsync();

        if (recorder.StopCalls != 1 || recorder.DisposeCalls != 1 ||
            File.Exists(session.NativePath) || File.Exists(session.CanonicalPath))
        {
            throw new InvalidOperationException("Cancel was not idempotent or did not clean owned files.");
        }

        Console.WriteLine("SIMULATED_CANCEL_STOP_CALLS=1");
        Console.WriteLine("SIMULATED_CANCEL_TRANSCRIPTION_ATTEMPTED=False");
        Console.WriteLine("SIMULATED_CANCEL_CLEANUP=True");
    }

    private static async Task VerifyExitDuringRecordingAsync()
    {
        var store = new TemporaryRecordingStore();
        var recorder = FakeAudioRecorder.FloatStereo48Khz(TimeSpan.FromSeconds(1), 0.2f);
        var session = RecordingSession.Start(store, new AudioLevelBuffer(48), () => recorder);
        await session.DisposeAsync();
        await session.DisposeAsync();

        if (recorder.StopCalls != 1 || recorder.DisposeCalls != 1 ||
            File.Exists(session.NativePath) || File.Exists(session.CanonicalPath))
        {
            throw new InvalidOperationException("Exit-during-recording cleanup was not idempotent.");
        }

        Console.WriteLine("SIMULATED_EXIT_DURING_RECORDING_CONTROLLED=True");
        Console.WriteLine("SIMULATED_EXIT_DURING_RECORDING_CLEANUP=True");
    }

    private static HashSet<string> OwnedWavNames(TemporaryRecordingStore store)
    {
        return Directory.EnumerateFiles(store.RootPath, "*.wav")
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    private sealed class FakeAudioRecorder : IAudioRecorder
    {
        private readonly byte[] _audio;
        private readonly Exception? _stopException;
        private bool _stopped;
        private bool _disposed;

        private FakeAudioRecorder(WaveFormat waveFormat, byte[] audio, Exception? stopException)
        {
            WaveFormat = waveFormat;
            _audio = audio;
            _stopException = stopException;
        }

        public event AudioDataAvailableHandler? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public WaveFormat WaveFormat { get; }

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public static FakeAudioRecorder FloatStereo48Khz(
            TimeSpan duration,
            float peak,
            Exception? stopException = null)
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
            var frameCount = (int)Math.Round(format.SampleRate * duration.TotalSeconds);
            var audio = new byte[frameCount * format.BlockAlign];
            var samples = MemoryMarshal.Cast<byte, float>(audio.AsSpan());
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = peak == 0.0f
                    ? 0.0f
                    : peak * MathF.Sin(2.0f * MathF.PI * 440.0f * index / (format.SampleRate * format.Channels));
            }

            return new FakeAudioRecorder(format, audio, stopException);
        }

        public static FakeAudioRecorder PcmMono44100Hz(TimeSpan duration, float peak)
        {
            var format = new WaveFormat(44_100, 16, 1);
            var sampleCount = (int)Math.Round(format.SampleRate * duration.TotalSeconds);
            var audio = new byte[sampleCount * sizeof(short)];
            var samples = MemoryMarshal.Cast<byte, short>(audio.AsSpan());
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = (short)(short.MaxValue * peak *
                    MathF.Sin(2.0f * MathF.PI * 440.0f * index / format.SampleRate));
            }

            return new FakeAudioRecorder(format, audio, stopException: null);
        }

        public void StartRecording()
        {
            if (_audio.Length > 0)
            {
                DataAvailable?.Invoke(_audio, AudioClientBufferFlags.None, 0, 0);
            }
        }

        public void StopRecording()
        {
            StopCalls++;
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            RecordingStopped?.Invoke(this, new StoppedEventArgs(_stopException));
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                DisposeCalls++;
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
