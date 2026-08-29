using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AiWritingAssistant.Audio;

internal sealed class AudioRecordingService : IAudioRecordingService
{
    private readonly Func<IAudioRecorder> _recorderFactory;

    public AudioRecordingService()
        : this(static () => new NAudioRecorderAdapter())
    {
    }

    internal AudioRecordingService(Func<IAudioRecorder> recorderFactory)
    {
        _recorderFactory = recorderFactory ?? throw new ArgumentNullException(nameof(recorderFactory));
    }

    public IAudioRecordingSession Start(string outputPath, AudioLevelBuffer levels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(levels);

        IAudioRecorder? recorder = null;
        WaveFileWriter? writer = null;

        try
        {
            recorder = _recorderFactory();
            writer = new WaveFileWriter(outputPath, recorder.WaveFormat);
            var session = new AudioRecordingSession(recorder, writer, outputPath, levels);
            recorder.StartRecording();
            return session;
        }
        catch
        {
            writer?.Dispose();
            if (recorder is not null)
                recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }
}

internal sealed class AudioRecordingSession : IAudioRecordingSession
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(500);
    private readonly IAudioRecorder _recorder;
    private readonly WaveFileWriter _writer;
    private readonly AudioLevelBuffer _levels;
    private readonly WaveFormat _standardFormat;
    private readonly TaskCompletionSource<Exception?> _recordingStopped = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _terminalGate = new();
    private Task<AudioCaptureResult>? _stopTask;
    private Task? _cancelTask;
    private bool _disposed;
    private long _packetCount;
    private long _capturedBytes;
    private float _maximumPeak;

    public AudioRecordingSession(
        IAudioRecorder recorder,
        WaveFileWriter writer,
        string outputPath,
        AudioLevelBuffer levels)
    {
        _recorder = recorder;
        _writer = writer;
        OutputPath = outputPath;
        _levels = levels;
        _standardFormat = recorder.WaveFormat.AsStandardWaveFormat();
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += OnRecordingStopped;
    }

    public string OutputPath { get; }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken)
    {
        lock (_terminalGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancelTask is not null)
                throw new InvalidOperationException("Recording cancellation is already in progress.");

            return _stopTask ??= StopCoreAsync(cancellationToken);
        }
    }

    public Task CancelAsync(CancellationToken cancellationToken)
    {
        lock (_terminalGate)
        {
            if (_disposed)
                return Task.CompletedTask;
            if (_stopTask is not null)
                return _stopTask;

            return _cancelTask ??= CancelCoreAsync(cancellationToken);
        }
    }

    private async Task<AudioCaptureResult> StopCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stopException = await StopRecorderAsync(cancellationToken).ConfigureAwait(false);
            if (stopException is not null)
                throw new InvalidOperationException("Audio capture stopped with an error.", stopException);

            var packets = Interlocked.Read(ref _packetCount);
            var bytes = Interlocked.Read(ref _capturedBytes);
            var duration = TimeSpan.FromSeconds(bytes / (double)_standardFormat.AverageBytesPerSecond);

            if (packets == 0 || duration < MinimumDuration)
                throw new NoUsableAudioException("Nothing was recorded.");

            return new AudioCaptureResult(
                OutputPath,
                _standardFormat.ToString(),
                packets,
                bytes,
                duration,
                _maximumPeak);
        }
        finally
        {
            _writer.Dispose();
            await DisposeRecorderAsync().ConfigureAwait(false);
        }
    }

    private async Task CancelCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await StopRecorderAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writer.Dispose();
            await DisposeRecorderAsync().ConfigureAwait(false);
        }
    }

    private async Task<Exception?> StopRecorderAsync(CancellationToken cancellationToken)
    {
        _recorder.StopRecording();
        return await _recordingStopped.Task
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
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

        var peak = CalculatePeak(buffer, _standardFormat);
        _maximumPeak = Math.Max(_maximumPeak, peak);
        _levels.Add(peak);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        _recordingStopped.TrySetResult(eventArgs.Exception);
    }

    internal static float CalculatePeak(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var maximum = 0.0f;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            foreach (var sample in MemoryMarshal.Cast<byte, float>(buffer))
            {
                if (float.IsFinite(sample))
                    maximum = Math.Max(maximum, Math.Abs(sample));
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
            return;

        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.RecordingStopped -= OnRecordingStopped;
        await _recorder.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await CancelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await DisposeRecorderAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class NAudioRecorderAdapter : IAudioRecorder
{
    private readonly WasapiRecorder _inner;
    private bool _disposed;

    public NAudioRecorderAdapter()
    {
        _inner = new WasapiRecorderBuilder().Build();
        _inner.DataAvailable += OnDataAvailable;
        _inner.RecordingStopped += OnRecordingStopped;
    }

    public event AudioDataAvailableHandler? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat => _inner.WaveFormat;

    public void StartRecording() => _inner.StartRecording();

    public void StopRecording() => _inner.StopRecording();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _inner.DataAvailable -= OnDataAvailable;
        _inner.RecordingStopped -= OnRecordingStopped;
        await _inner.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void OnDataAvailable(
        ReadOnlySpan<byte> buffer,
        AudioClientBufferFlags flags,
        long devicePosition,
        long qpcPosition)
    {
        DataAvailable?.Invoke(buffer, flags, devicePosition, qpcPosition);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        RecordingStopped?.Invoke(sender, eventArgs);
    }
}
