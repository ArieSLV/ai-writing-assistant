using AiWritingAssistant.Audio;
using NAudio.Wave;

namespace AiWritingAssistant.Tests;

public sealed class AudioPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AiWritingAssistant.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Level_buffer_is_bounded_and_ordered()
    {
        var levels = new AudioLevelBuffer(8);
        for (var value = 0; value < 12; value++)
            levels.Add(value / 10f);

        var snapshot = levels.Snapshot();

        Assert.Equal(8, snapshot.Length);
        Assert.Equal(0.4f, snapshot[0], 3);
        Assert.Equal(1.0f, snapshot[^1], 3);
    }

    [Fact]
    public async Task Normalizer_converts_stereo_48khz_float_to_mono_16khz_pcm16()
    {
        Directory.CreateDirectory(_root);
        var nativePath = Path.Combine(_root, "native.wav");
        var canonicalPath = Path.Combine(_root, "canonical.wav");
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        using (var writer = new WaveFileWriter(nativePath, format))
        {
            var samples = new float[48_000 * 2];
            for (var index = 0; index < samples.Length; index += 2)
            {
                var value = MathF.Sin(index / 2f * 2f * MathF.PI * 440f / 48_000f) * 0.25f;
                samples[index] = value;
                samples[index + 1] = value;
            }

            writer.WriteSamples(samples, 0, samples.Length);
        }

        var result = await new AudioFileNormalizer().NormalizeAsync(
            nativePath,
            canonicalPath,
            CancellationToken.None);

        using var reader = new WaveFileReader(result.Path);
        Assert.Equal(16_000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.True(result.Bytes > 32_000);
    }

    [Fact]
    public void Peak_calculation_supports_pcm16()
    {
        var bytes = new byte[] { 0x00, 0x40, 0x00, 0xC0 };

        var peak = AudioRecordingSession.CalculatePeak(bytes, new WaveFormat(16_000, 16, 1));

        Assert.Equal(0.5f, peak, 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
