using AiWritingAssistant.Credentials;

namespace AiWritingAssistant.Tests;

public sealed class LanguageModelServiceFactoryTests
{
    [Fact]
    public void Gemini_text_service_requests_only_text_credential()
    {
        var purposes = new List<GoogleCredentialPurpose>();
        var credentials = new RecordingCredentialProvider(purposes);
        var factory = new LanguageModelServiceFactory(credentials);

        var service = factory.CreateGeminiChatCompletionService(new AppSettings());

        Assert.NotNull(service);
        Assert.Equal([GoogleCredentialPurpose.TextGeneration], purposes);
    }

    private sealed class RecordingCredentialProvider(List<GoogleCredentialPurpose> purposes)
        : IGoogleCredentialProvider
    {
        public string GetRequiredCredential(GoogleCredentialPurpose purpose)
        {
            purposes.Add(purpose);
            return "test-key";
        }
    }
}
