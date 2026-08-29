using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

const string ProofRootName = "AiWritingAssistant";
const string ProofDirectoryName = "voice-proof";

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "probe";

if (command is not ("probe" or "record"))
{
    Console.Error.WriteLine("Usage: VoiceCaptureProof [probe | record [seconds]]");
    return 2;
}

await using var recorder = new WasapiRecorderBuilder().Build();

Console.WriteLine("CAPTURE_DEVICE_AVAILABLE=True");
Console.WriteLine($"CAPTURE_FORMAT={recorder.WaveFormat}");

if (command == "probe")
{
    Console.WriteLine("CAPTURE_API_READY=True");
    return 0;
}

var durationSeconds = 5;
if (args.Length > 1 &&
    (!int.TryParse(args[1], out durationSeconds) || durationSeconds is < 1 or > 600))
{
    Console.Error.WriteLine("Recording duration must be between 1 and 600 seconds.");
    return 2;
}

var proofRoot = Path.GetFullPath(Path.Combine(
    Path.GetTempPath(),
    ProofRootName,
    ProofDirectoryName));

Directory.CreateDirectory(proofRoot);

var recordingPath = Path.Combine(
    proofRoot,
    $"capture-{Guid.NewGuid():N}.wav");
var canonicalPath = Path.Combine(
    proofRoot,
    $"canonical-{Guid.NewGuid():N}.wav");

var packetCount = 0L;
var capturedBytes = 0L;
var nonSilentPackets = 0L;
var maximumPeak = 0.0f;
var stopCompletion = new TaskCompletionSource<Exception?>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var standardCaptureFormat = recorder.WaveFormat.AsStandardWaveFormat();
var canonicalReady = false;

try
{
    using (var writer = new WaveFileWriter(recordingPath, recorder.WaveFormat))
    {
        recorder.DataAvailable += (buffer, _, _, _) =>
        {
            writer.Write(buffer);
            Interlocked.Increment(ref packetCount);
            Interlocked.Add(ref capturedBytes, buffer.Length);

            var peak = CalculatePeak(buffer, standardCaptureFormat);
            maximumPeak = Math.Max(maximumPeak, peak);
            if (peak >= 0.01f)
            {
                Interlocked.Increment(ref nonSilentPackets);
            }
        };

        recorder.RecordingStopped += (_, eventArgs) =>
            stopCompletion.TrySetResult(eventArgs.Exception);

        recorder.StartRecording();
        Console.WriteLine("RECORDING_STARTED=True");

        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        recorder.StopRecording();

        var stopException = await stopCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (stopException is not null)
        {
            throw new InvalidOperationException("Audio capture stopped with an error.", stopException);
        }
    }

    var recordingInfo = new FileInfo(recordingPath);

    using var nativeReader = new WaveFileReader(recordingPath);
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

    WaveFileWriter.CreateWaveFile16(canonicalPath, sampleProvider);

    using var canonicalReader = new WaveFileReader(canonicalPath);
    var canonicalFormat = canonicalReader.WaveFormat.AsStandardWaveFormat();

    if (canonicalFormat.SampleRate != 16_000 ||
        canonicalFormat.Channels != 1 ||
        canonicalFormat.BitsPerSample != 16 ||
        canonicalFormat.Encoding != WaveFormatEncoding.Pcm)
    {
        throw new InvalidOperationException(
            $"Unexpected canonical format: {canonicalFormat}.");
    }

    canonicalReady = true;
    var canonicalInfo = new FileInfo(canonicalPath);

    Console.WriteLine("RECORDING_STOPPED=True");
    Console.WriteLine($"PACKETS={packetCount}");
    Console.WriteLine($"NON_SILENT_PACKETS={nonSilentPackets}");
    Console.WriteLine($"MAX_PEAK={maximumPeak.ToString("F4", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"CAPTURED_BYTES={capturedBytes}");
    Console.WriteLine($"NATIVE_WAV_BYTES={recordingInfo.Length}");
    Console.WriteLine($"NATIVE_WAV_FORMAT={nativeFormat}");
    Console.WriteLine($"CANONICAL_WAV_BYTES={canonicalInfo.Length}");
    Console.WriteLine($"CANONICAL_WAV_FORMAT={canonicalFormat}");
    Console.WriteLine($"WAV_PATH={canonicalInfo.FullName}");
}
finally
{
    DeleteIfPresent(recordingPath);
    Console.WriteLine($"NATIVE_WAV_DELETED={!File.Exists(recordingPath)}");

    if (!canonicalReady)
    {
        DeleteIfPresent(canonicalPath);
    }
}

return 0;

static float CalculatePeak(ReadOnlySpan<byte> buffer, WaveFormat format)
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

static void DeleteIfPresent(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}
