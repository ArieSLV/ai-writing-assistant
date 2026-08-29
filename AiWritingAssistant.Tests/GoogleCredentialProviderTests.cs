using AiWritingAssistant.Credentials;

namespace AiWritingAssistant.Tests;

public sealed class GoogleCredentialProviderTests
{
    [Fact]
    public void Purposes_resolve_only_their_own_environment_variables()
    {
        var values = new Dictionary<string, string?>
        {
            [GoogleCredentialProvider.TextEnvironmentVariable] = " text-key ",
            [GoogleCredentialProvider.VoiceEnvironmentVariable] = " voice-key "
        };
        var provider = new GoogleCredentialProvider(name => values.GetValueOrDefault(name));

        Assert.Equal("text-key", provider.GetRequiredCredential(GoogleCredentialPurpose.TextGeneration));
        Assert.Equal("voice-key", provider.GetRequiredCredential(GoogleCredentialPurpose.VoiceTranscription));
    }

    [Fact]
    public void Missing_voice_key_never_falls_back_to_text_key()
    {
        var provider = new GoogleCredentialProvider(name =>
            name == GoogleCredentialProvider.TextEnvironmentVariable ? "text-key" : null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredCredential(GoogleCredentialPurpose.VoiceTranscription));

        Assert.Contains(GoogleCredentialProvider.VoiceEnvironmentVariable, exception.Message);
        Assert.DoesNotContain("text-key", exception.Message);
    }
}
