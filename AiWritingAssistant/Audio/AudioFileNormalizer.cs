using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AiWritingAssistant.Audio;

internal interface IAudioFileNormalizer
{
    Task<NormalizedAudioFile> NormalizeAsync(
        string nativePath,
        string canonicalPath,
        CancellationToken cancellationToken);
}

internal sealed record NormalizedAudioFile(
    string Path,
    string NativeFormat,
    string CanonicalFormat,
    long Bytes);

internal sealed class AudioFileNormalizer : IAudioFileNormalizer
{
    public Task<NormalizedAudioFile> NormalizeAsync(
        string nativePath,
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);

        return Task.Run(() => Normalize(nativePath, canonicalPath, cancellationToken), cancellationToken);
    }

    private static NormalizedAudioFile Normalize(
        string nativePath,
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var nativeReader = new WaveFileReader(nativePath);
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
                $"Audio normalization supports mono or stereo input, not {sampleProvider.WaveFormat.Channels} channels.")
        };

        if (sampleProvider.WaveFormat.SampleRate != 16_000)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16_000);

        cancellationToken.ThrowIfCancellationRequested();
        WaveFileWriter.CreateWaveFile16(canonicalPath, sampleProvider);
        cancellationToken.ThrowIfCancellationRequested();

        using var canonicalReader = new WaveFileReader(canonicalPath);
        var canonicalFormat = canonicalReader.WaveFormat.AsStandardWaveFormat();

        if (canonicalFormat.SampleRate != 16_000 ||
            canonicalFormat.Channels != 1 ||
            canonicalFormat.BitsPerSample != 16 ||
            canonicalFormat.Encoding != WaveFormatEncoding.Pcm)
        {
            throw new InvalidOperationException($"Unexpected canonical audio format: {canonicalFormat}.");
        }

        return new NormalizedAudioFile(
            canonicalPath,
            nativeFormat.ToString(),
            canonicalFormat.ToString(),
            new FileInfo(canonicalPath).Length);
    }
}
