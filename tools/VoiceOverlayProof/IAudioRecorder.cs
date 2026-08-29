using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceOverlayProof;

internal delegate void AudioDataAvailableHandler(
    ReadOnlySpan<byte> buffer,
    AudioClientBufferFlags flags,
    long devicePosition,
    long qpcPosition);

internal interface IAudioRecorder : IAsyncDisposable
{
    event AudioDataAvailableHandler? DataAvailable;

    event EventHandler<StoppedEventArgs>? RecordingStopped;

    WaveFormat WaveFormat { get; }

    void StartRecording();

    void StopRecording();
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
        {
            return;
        }

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
