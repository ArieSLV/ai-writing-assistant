namespace AiWritingAssistant.Transcription;

internal enum TranscriptionStage
{
    Uploading,
    WaitingForFile,
    Transcribing
}

internal sealed record TranscriptionOptions(
    string Model,
    TimeSpan Timeout,
    TimeSpan FileReadyTimeout)
{
    public static TranscriptionOptions CreateDefault(string model)
    {
        return new TranscriptionOptions(
            model,
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(30));
    }
}

internal sealed record TranscriptionResult(
    string Text,
    TimeSpan UploadDuration,
    TimeSpan FileReadyDuration,
    TimeSpan InteractionDuration,
    TimeSpan TotalDuration);

internal interface ITranscriptionClient
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        TranscriptionOptions options,
        IProgress<TranscriptionStage>? progress,
        CancellationToken cancellationToken);
}

internal sealed class TranscriptionClientException : InvalidOperationException
{
    public TranscriptionClientException(string userMessage, int? httpStatusCode = null, Exception? innerException = null)
        : base(userMessage, innerException)
    {
        HttpStatusCode = httpStatusCode;
    }

    public int? HttpStatusCode { get; }
}
