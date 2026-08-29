using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AiWritingAssistant.Audio;

internal interface IAudioRecordingService
{
    IAudioRecordingSession Start(string outputPath, AudioLevelBuffer levels);
}

internal interface IAudioRecordingSession : IAsyncDisposable
{
    string OutputPath { get; }

    Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken);

    Task CancelAsync(CancellationToken cancellationToken);
}

internal sealed record AudioCaptureResult(
    string Path,
    string NativeFormat,
    long PacketCount,
    long CapturedBytes,
    TimeSpan Duration,
    float MaximumPeak);

internal sealed class NoUsableAudioException(string message) : InvalidOperationException(message);

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
