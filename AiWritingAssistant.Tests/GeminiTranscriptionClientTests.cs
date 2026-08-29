using System.Net;
using System.Text;
using System.Text.Json;
using AiWritingAssistant.Credentials;
using AiWritingAssistant.Transcription;

namespace AiWritingAssistant.Tests;

public sealed class GeminiTranscriptionClientTests : IDisposable
{
    private readonly string _audioPath = Path.Combine(
        Path.GetTempPath(),
        $"AiWritingAssistant-transcription-{Guid.NewGuid():N}.wav");

    [Fact]
    public async Task Successful_flow_uses_voice_key_smart_mode_and_deletes_remote_file()
    {
        File.WriteAllBytes(_audioPath, new byte[64]);
        var handler = new SequenceHandler(
            Response(HttpStatusCode.OK, headers: new() { ["X-Goog-Upload-URL"] = "https://upload.test/session" }),
            Response(HttpStatusCode.OK, """{"file":{"name":"files/abc","uri":"https://files.test/abc","mimeType":"audio/wav"}}"""),
            Response(HttpStatusCode.OK, """{"state":"ACTIVE"}"""),
            Response(HttpStatusCode.OK, """{"output_text":"готовый текст"}"""),
            Response(HttpStatusCode.OK));
        var credentialNames = new List<string>();
        var credentials = new GoogleCredentialProvider(name =>
        {
            credentialNames.Add(name);
            return name == GoogleCredentialProvider.VoiceEnvironmentVariable ? "voice-secret" : null;
        });
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new GeminiTranscriptionClient(httpClient, credentials);

        var result = await client.TranscribeAsync(
            _audioPath,
            TranscriptionOptions.CreateDefault("gemini-3.5-transcribe"),
            progress: null,
            CancellationToken.None);

        Assert.Equal("готовый текст", result.Text);
        Assert.Equal([GoogleCredentialProvider.VoiceEnvironmentVariable], credentialNames);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(HttpMethod.Delete, handler.Requests[^1].Method);
        Assert.EndsWith("/v1beta/files/abc", handler.Requests[^1].Uri.AbsoluteUri);
        Assert.Contains("\"mode\":\"smart\"", handler.Requests[3].Body);
        Assert.Contains("\"model\":\"gemini-3.5-transcribe\"", handler.Requests[3].Body);
        Assert.DoesNotContain("voice-secret", handler.Requests[3].Body);
        Assert.Equal("voice-secret", handler.Requests[3].ApiKey);
    }

    [Fact]
    public async Task Permanent_interaction_failure_still_deletes_uploaded_file()
    {
        File.WriteAllBytes(_audioPath, new byte[64]);
        var handler = new SequenceHandler(
            Response(HttpStatusCode.OK, headers: new() { ["X-Goog-Upload-URL"] = "https://upload.test/session" }),
            Response(HttpStatusCode.OK, """{"file":{"name":"files/abc","uri":"https://files.test/abc"}}"""),
            Response(HttpStatusCode.OK, """{"state":"ACTIVE"}"""),
            Response(HttpStatusCode.BadRequest, """{"error":{"message":"sensitive raw body"}}"""),
            Response(HttpStatusCode.OK));
        var credentials = new GoogleCredentialProvider(name =>
            name == GoogleCredentialProvider.VoiceEnvironmentVariable ? "voice-secret" : null);
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new GeminiTranscriptionClient(httpClient, credentials);

        var exception = await Assert.ThrowsAsync<TranscriptionClientException>(() =>
            client.TranscribeAsync(
                _audioPath,
                TranscriptionOptions.CreateDefault("gemini-3.5-transcribe"),
                progress: null,
                CancellationToken.None));

        Assert.Equal(400, exception.HttpStatusCode);
        Assert.DoesNotContain("sensitive raw body", exception.Message);
        Assert.Equal(HttpMethod.Delete, handler.Requests[^1].Method);
    }

    [Fact]
    public void Extract_text_supports_structured_outputs_without_duplicates()
    {
        using var json = JsonDocument.Parse("""
                                            {
                                              "outputs": [
                                                { "type": "text", "text": "first" },
                                                { "type": "text", "text": "first" }
                                              ],
                                              "steps": [
                                                { "content": [{ "type": "text", "text": "second" }] }
                                              ]
                                            }
                                            """);

        var text = GeminiTranscriptionClient.ExtractText(json.RootElement);

        Assert.Equal($"first{Environment.NewLine}second", text);
    }

    public void Dispose()
    {
        if (File.Exists(_audioPath))
            File.Delete(_audioPath);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string body = "{}",
        Dictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
                response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var apiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.Single()
                : null;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, apiKey));

            if (_responses.Count == 0)
                throw new InvalidOperationException("No fake response remains.");

            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body, string? ApiKey);
}
