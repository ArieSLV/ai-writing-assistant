namespace AiWritingAssistant;

internal enum LanguageModelProvider
{
    Gemini,
    Ollama,
}

internal sealed class AppSettings
{
    public const string DefaultGeminiModel = "gemini-3.1-flash-lite-preview";
    public const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";

    public LanguageModelProvider SelectedProvider { get; set; } = LanguageModelProvider.Gemini;

    public string GeminiModel { get; set; } = DefaultGeminiModel;

    public string OllamaBaseUrl { get; set; } = DefaultOllamaBaseUrl;

    public string? OllamaModel { get; set; }

    public bool OllamaReasoningEnabled { get; set; }

    public static AppSettings CreateDefault()
    {
        return new AppSettings();
    }

    public void Normalize()
    {
        GeminiModel = NormalizeRequiredValue(GeminiModel, DefaultGeminiModel);
        OllamaBaseUrl = NormalizeRequiredValue(OllamaBaseUrl, DefaultOllamaBaseUrl);
        OllamaModel = NormalizeOptionalValue(OllamaModel);
    }

    private static string NormalizeRequiredValue(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
