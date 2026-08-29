namespace AiWritingAssistant.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Normalize_preserves_existing_text_defaults()
    {
        var settings = new AppSettings
        {
            GeminiModel = "  ",
            OllamaBaseUrl = "  ",
            OllamaModel = "  "
        };

        settings.Normalize();

        Assert.Equal(AppSettings.DefaultGeminiModel, settings.GeminiModel);
        Assert.Equal(AppSettings.DefaultOllamaBaseUrl, settings.OllamaBaseUrl);
        Assert.Null(settings.OllamaModel);
    }
}
