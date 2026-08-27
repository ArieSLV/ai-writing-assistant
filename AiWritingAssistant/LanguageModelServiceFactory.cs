#pragma warning disable SKEXP0070

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using OllamaSharp;

namespace AiWritingAssistant;

internal sealed class LanguageModelServiceFactory
{
    private const string GoogleApiKeyEnvironmentVariable = "AI_WRITING_ASSISTANT_GOOGLE_API_KEY";

    public IChatCompletionService CreateGeminiChatCompletionService(AppSettings settings)
    {
        settings.Normalize();
        return CreateGeminiService(settings);
    }

    public PromptExecutionSettings CreateGeminiExecutionSettings()
    {
        return new GeminiPromptExecutionSettings { MaxTokens = 4096 * 1024 };
    }

    public OllamaApiClient CreateOllamaApiClient(AppSettings settings)
    {
        settings.Normalize();

        if (string.IsNullOrWhiteSpace(settings.OllamaModel))
            throw new InvalidOperationException("Select an Ollama model before using the Ollama provider.");

        return CreateOllamaApiClient(settings.OllamaBaseUrl, settings.OllamaModel);
    }

    public OllamaApiClient CreateOllamaApiClient(string baseUrl, string? modelId = null)
    {
        return new OllamaApiClient(CreateOllamaUri(baseUrl), modelId ?? string.Empty);
    }

    private static IChatCompletionService CreateGeminiService(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GeminiModel))
            throw new InvalidOperationException("Set a Gemini model name before using the Gemini provider.");

        var apiKey = Environment.GetEnvironmentVariable(GoogleApiKeyEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Set the {GoogleApiKeyEnvironmentVariable} environment variable before using the Gemini provider.");
        }

        return new GoogleAIGeminiChatCompletionService(
            settings.GeminiModel,
            apiKey,
            GoogleAIVersion.V1_Beta,
            null!,
            null!);
    }

    private static Uri CreateOllamaUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Enter a valid Ollama server URL.");
        }

        return uri;
    }
}
