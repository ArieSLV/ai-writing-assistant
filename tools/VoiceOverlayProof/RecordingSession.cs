using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceOverlayProof;

internal sealed class RecordingSession : IAsyncDisposable
{
    private static readonly TimeSpan MinimumValidDuration = TimeSpan.FromMilliseconds(500);

    private readonly TemporaryRecordingStore _store;
    private readonly AudioLevelBuffer _levels;
    private readonly IAudioRecorder _recorder;
    private readonly WaveFileWriter _writer;
    private readonly WaveFormat _standardCaptureFormat;
    private readonly TaskCompletionSource<Exception?> _recordingStopped = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _terminalGate = new();
    private Task<CaptureResult>? _stopTask;
    private Task? _cancelTask;
    private bool _disposed;
    private long _packetCount;
    private long _capturedBytes;
    private long _nonSilentPackets;
    private float _maximumPeak;

    private RecordingSession(
        TemporaryRecordingStore store,
        AudioLevelBuffer levels,
        IAudioRecorder recorder,
        WaveFileWriter writer,
        string nativePath,
        string canonicalPath)
    {
        _store = store;
        _levels = levels;
        _recorder = recorder;
        _writer = writer;
        NativePath = nativePath;
        CanonicalPath = canonicalPath;
        _standardCaptureFormat = recorder.WaveFormat.AsStandardWaveFormat();

        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += OnRecordingStopped;
    }

    public string NativePath { get; }

    public string CanonicalPath { get; }

    public WaveFormat CaptureFormat => _recorder.WaveFormat;

    public static RecordingSession Start(
        TemporaryRecordingStore store,
        AudioLevelBuffer levels)
    {
        return Start(store, levels, static () => new NAudioRecorderAdapter());
    }

    internal static RecordingSession Start(
        TemporaryRecordingStore store,
        AudioLevelBuffer levels,
        Func<IAudioRecorder> recorderFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(recorderFactory);

        var nativePath = store.CreateWavPath("native");
        var canonicalPath = store.CreateWavPath("canonical");
        IAudioRecorder? recorder = null;
        WaveFileWriter? writer = null;

        try
        {
            recorder = recorderFactory();
            writer = new WaveFileWriter(nativePath, recorder.WaveFormat);
            var session = new RecordingSession(
                store,
                levels,
                recorder,
                writer,
                nativePath,
                canonicalPath);
            recorder.StartRecording();
            return session;
        }
        catch
        {
            writer?.Dispose();
            recorder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            store.DeleteOwnedFile(nativePath);
            store.DeleteOwnedFile(canonicalPath);
            throw;
        }
    }

    public Task<CaptureResult> StopAndNormalizeAsync()
    {
        lock (_terminalGate)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancelTask is not null)
            {
                throw new InvalidOperationException("Recording cancellation is already in progress.");
            }

            return _stopTask = StopAndNormalizeCoreAsync();
        }
    }

    public Task CancelAsync()
    {
        lock (_terminalGate)
        {
            if (_cancelTask is not null)
            {
                return _cancelTask;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopTask is not null)
            {
                throw new InvalidOperationException("Recording stop is already in progress.");
            }

            return _cancelTask = CancelCoreAsync();
        }
    }

    private async Task<CaptureResult> StopAndNormalizeCoreAsync()
    {
        var stopStopwatch = Stopwatch.StartNew();

        try
        {
            _recorder.StopRecording();
            var stopException = await _recordingStopped.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            if (stopException is not null)
            {
                throw new InvalidOperationException("Audio capture stopped with an error.", stopException);
            }

            _writer.Dispose();
            stopStopwatch.Stop();

            var capturedBytes = Interlocked.Read(ref _capturedBytes);
            var minimumBytes = (long)Math.Ceiling(
                _standardCaptureFormat.AverageBytesPerSecond * MinimumValidDuration.TotalSeconds);
            if (Interlocked.Read(ref _packetCount) == 0 || capturedBytes < minimumBytes)
            {
                throw new NoUsableAudioException(
                    $"Capture shorter than {MinimumValidDuration.TotalMilliseconds:F0} ms or without audio packets.");
            }

            // FileInfo.Length is evaluated lazily. Snapshot it while the native WAV
            // still exists; the owned file is deleted immediately after normalization.
            var nativeBytes = new FileInfo(NativePath).Length;
            var normalizationStopwatch = Stopwatch.StartNew();
            var normalization = await Task.Run(NormalizeAndValidate).ConfigureAwait(false);
            normalizationStopwatch.Stop();

            _store.DeleteOwnedFile(NativePath);

            return new CaptureResult(
                CaptureFormat.ToString(),
                normalization.NativeFormat,
                normalization.CanonicalFormat,
                Interlocked.Read(ref _packetCount),
                Interlocked.Read(ref _capturedBytes),
                Interlocked.Read(ref _nonSilentPackets),
                _maximumPeak,
                nativeBytes,
                normalization.CanonicalBytes,
                stopStopwatch.ElapsedMilliseconds,
                normalizationStopwatch.ElapsedMilliseconds,
                CanonicalPath);
        }
        catch
        {
            _writer.Dispose();
            _store.DeleteOwnedFile(NativePath);
            _store.DeleteOwnedFile(CanonicalPath);
            throw;
        }
        finally
        {
            await DisposeRecorderAsync().ConfigureAwait(false);
        }
    }

    private NormalizationResult NormalizeAndValidate()
    {
        using var nativeReader = new WaveFileReader(NativePath);
        var nativeFormat = nativeReader.WaveFormat.AsStandardWaveFormat();
        ISampleProvider sampleProvider = nativeReader.ToSampleProvider();

        sampleProvider = sampleProvider.WaveFormat.Channels switch
        {
            1 => sampleProvider,
            2 => new StereoToMonoSampleProvider(sampleProvider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            },
            _ => throw new NotSupportedException(
                $"Capture proof supports mono or stereo input, not {sampleProvider.WaveFormat.Channels} channels.")
        };

        if (sampleProvider.WaveFormat.SampleRate != 16_000)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16_000);
        }

        WaveFileWriter.CreateWaveFile16(CanonicalPath, sampleProvider);

        using var canonicalReader = new WaveFileReader(CanonicalPath);
        var canonicalFormat = canonicalReader.WaveFormat.AsStandardWaveFormat();

        if (canonicalFormat.SampleRate != 16_000 ||
            canonicalFormat.Channels != 1 ||
            canonicalFormat.BitsPerSample != 16 ||
            canonicalFormat.Encoding != WaveFormatEncoding.Pcm)
        {
            throw new InvalidOperationException($"Unexpected canonical format: {canonicalFormat}.");
        }

        return new NormalizationResult(
            nativeFormat.ToString(),
            canonicalFormat.ToString(),
            new FileInfo(CanonicalPath).Length);
    }

    private async Task CancelCoreAsync()
    {
        Exception? stopException = null;

        try
        {
            _recorder.StopRecording();
            stopException = await _recordingStopped.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
        }
        finally
        {
            _writer.Dispose();
            _store.DeleteOwnedFile(NativePath);
            _store.DeleteOwnedFile(CanonicalPath);
            await DisposeRecorderAsync().ConfigureAwait(false);
        }

        if (stopException is not null)
        {
            throw new InvalidOperationException("Audio capture failed while cancellation was completing.", stopException);
        }
    }

    private void OnDataAvailable(
        ReadOnlySpan<byte> buffer,
        AudioClientBufferFlags _,
        long __,
        long ___)
    {
        _writer.Write(buffer);
        Interlocked.Increment(ref _packetCount);
        Interlocked.Add(ref _capturedBytes, buffer.Length);

        var peak = CalculatePeak(buffer, _standardCaptureFormat);
        _maximumPeak = Math.Max(_maximumPeak, peak);
        if (peak >= 0.01f)
        {
            Interlocked.Increment(ref _nonSilentPackets);
        }

        _levels.Add(peak);
    }

    private void OnRecordingStopped(object? _, StoppedEventArgs eventArgs)
    {
        _recordingStopped.TrySetResult(eventArgs.Exception);
    }

    private static float CalculatePeak(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var maximum = 0.0f;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            foreach (var sample in MemoryMarshal.Cast<byte, float>(buffer))
            {
                if (float.IsFinite(sample))
                {
                    maximum = Math.Max(maximum, Math.Abs(sample));
                }
            }

            return Math.Min(maximum, 1.0f);
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            for (var offset = 0; offset + sizeof(short) <= buffer.Length; offset += sizeof(short))
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(buffer[offset..]);
                maximum = Math.Max(maximum, Math.Abs(sample / 32768.0f));
            }

            return maximum;
        }

        throw new NotSupportedException($"Peak calculation does not support {format}.");
    }

    private async Task DisposeRecorderAsync()
    {
        if (_disposed)
        {
            return;
        }

        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.RecordingStopped -= OnRecordingStopped;
        await _recorder.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            try
            {
                _recorder.StopRecording();
                _ = await _recordingStopped.Task
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Cleanup below remains mandatory even when the device is already unavailable.
            }

            _writer.Dispose();
            await DisposeRecorderAsync().ConfigureAwait(false);
        }

        _store.DeleteOwnedFile(NativePath);
        _store.DeleteOwnedFile(CanonicalPath);
    }

    private sealed record NormalizationResult(
        string NativeFormat,
        string CanonicalFormat,
        long CanonicalBytes);
}

internal sealed record CaptureResult(
    string CaptureFormat,
    string NativeFormat,
    string CanonicalFormat,
    long PacketCount,
    long CapturedBytes,
    long NonSilentPackets,
    float MaximumPeak,
    long NativeBytes,
    long CanonicalBytes,
    long StopMilliseconds,
    long NormalizationMilliseconds,
    string CanonicalPath);

internal sealed class NoUsableAudioException(string message) : InvalidOperationException(message);
