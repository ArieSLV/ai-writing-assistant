namespace AiWritingAssistant.Credentials;

internal enum GoogleCredentialPurpose
{
    TextGeneration,
    VoiceTranscription
}

internal interface IGoogleCredentialProvider
{
    string GetRequiredCredential(GoogleCredentialPurpose purpose);
}

internal sealed class GoogleCredentialProvider : IGoogleCredentialProvider
{
    public const string TextEnvironmentVariable = "AI_WRITING_ASSISTANT_GOOGLE_API_KEY";
    public const string VoiceEnvironmentVariable = "AI_WRITING_ASSISTANT_GOOGLE_VOICE_API_KEY";

    private readonly Func<string, string?> _readEnvironmentVariable;

    public GoogleCredentialProvider()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal GoogleCredentialProvider(Func<string, string?> readEnvironmentVariable)
    {
        _readEnvironmentVariable = readEnvironmentVariable ?? throw new ArgumentNullException(nameof(readEnvironmentVariable));
    }

    public string GetRequiredCredential(GoogleCredentialPurpose purpose)
    {
        var variableName = purpose switch
        {
            GoogleCredentialPurpose.TextGeneration => TextEnvironmentVariable,
            GoogleCredentialPurpose.VoiceTranscription => VoiceEnvironmentVariable,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };

        var value = _readEnvironmentVariable(variableName)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Set the {variableName} environment variable before using {purpose}.");
        }

        return value;
    }
}
