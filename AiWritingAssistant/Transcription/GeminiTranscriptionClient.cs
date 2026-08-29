using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiWritingAssistant.Credentials;

namespace AiWritingAssistant.Transcription;

internal sealed class GeminiTranscriptionClient : ITranscriptionClient, IDisposable
{
    private static readonly Uri DefaultApiBase = new("https://generativelanguage.googleapis.com/");
    private readonly HttpClient _httpClient;
    private readonly IGoogleCredentialProvider _credentialProvider;
    private readonly AppLogger? _logger;
    private readonly bool _ownsHttpClient;

    public GeminiTranscriptionClient(
        IGoogleCredentialProvider credentialProvider,
        AppLogger? logger = null)
        : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, credentialProvider, logger, ownsHttpClient: true)
    {
    }

    internal GeminiTranscriptionClient(
        HttpClient httpClient,
        IGoogleCredentialProvider credentialProvider,
        AppLogger? logger = null,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _logger = logger;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        TranscriptionOptions options,
        IProgress<TranscriptionStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentNullException.ThrowIfNull(options);

        var fullPath = Path.GetFullPath(audioPath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("The recording file was not found.", fullPath);
        if (!string.Equals(fileInfo.Extension, ".wav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Voice transcription accepts WAV files only.");
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException("Set a Voice transcription model.");

        var apiKey = _credentialProvider.GetRequiredCredential(GoogleCredentialPurpose.VoiceTranscription);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var token = timeout.Token;
        var totalStopwatch = Stopwatch.StartNew();
        string? uploadedFileName = null;
        var uploadDuration = TimeSpan.Zero;
        var readyDuration = TimeSpan.Zero;
        var interactionDuration = TimeSpan.Zero;

        try
        {
            progress?.Report(TranscriptionStage.Uploading);
            var uploadStopwatch = Stopwatch.StartNew();
            var upload = await UploadAsync(fullPath, fileInfo.Length, apiKey, token).ConfigureAwait(false);
            uploadStopwatch.Stop();
            uploadDuration = uploadStopwatch.Elapsed;
            uploadedFileName = upload.Name;

            progress?.Report(TranscriptionStage.WaitingForFile);
            var readyStopwatch = Stopwatch.StartNew();
            await WaitUntilActiveAsync(upload.Name, apiKey, options.FileReadyTimeout, token).ConfigureAwait(false);
            readyStopwatch.Stop();
            readyDuration = readyStopwatch.Elapsed;

            progress?.Report(TranscriptionStage.Transcribing);
            var interactionStopwatch = Stopwatch.StartNew();
            var text = await InteractAsync(upload.Uri, upload.MimeType, options.Model, apiKey, token)
                .ConfigureAwait(false);
            interactionStopwatch.Stop();
            interactionDuration = interactionStopwatch.Elapsed;

            totalStopwatch.Stop();
            _logger?.Info(
                $"Voice transcription succeeded. Model='{options.Model}', AudioBytes={fileInfo.Length}, " +
                $"UploadMs={uploadDuration.TotalMilliseconds:F0}, ReadyMs={readyDuration.TotalMilliseconds:F0}, " +
                $"InteractionMs={interactionDuration.TotalMilliseconds:F0}, ResultLength={text.Length}.");

            return new TranscriptionResult(
                text,
                uploadDuration,
                readyDuration,
                interactionDuration,
                totalStopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranscriptionClientException("Transcription timed out.");
        }
        finally
        {
            totalStopwatch.Stop();
            if (!string.IsNullOrWhiteSpace(uploadedFileName))
                await DeleteRemoteFileAsync(uploadedFileName, apiKey).ConfigureAwait(false);
        }
    }

    private async Task<UploadedFile> UploadAsync(
        string path,
        long length,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            file = new { display_name = $"voice-dictation-{Guid.NewGuid():N}" }
        });

        using var startResponse = await SendWithRetryAsync(
            () =>
            {
                var request = CreateApiRequest(HttpMethod.Post, "upload/v1beta/files", apiKey);
                request.Headers.Add("X-Goog-Upload-Protocol", "resumable");
                request.Headers.Add("X-Goog-Upload-Command", "start");
                request.Headers.Add("X-Goog-Upload-Header-Content-Length", length.ToString(CultureInfo.InvariantCulture));
                request.Headers.Add("X-Goog-Upload-Header-Content-Type", "audio/wav");
                request.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
                return request;
            },
            "Upload session start",
            cancellationToken).ConfigureAwait(false);

        if (!startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var values))
            throw new TranscriptionClientException("Google did not return an audio upload URL.");

        var uploadUrl = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(uploadUrl))
            throw new TranscriptionClientException("Google returned an empty audio upload URL.");

        using var uploadResponse = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                request.Headers.Add("X-Goog-Upload-Offset", "0");
                request.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
                var content = new StreamContent(File.OpenRead(path));
                content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                content.Headers.ContentLength = length;
                request.Content = content;
                return request;
            },
            "Audio upload",
            cancellationToken).ConfigureAwait(false);

        using var document = await ReadJsonAsync(uploadResponse, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("file", out var file))
            throw new TranscriptionClientException("Google returned no uploaded file metadata.");

        var name = GetString(file, "name");
        var uri = GetString(file, "uri");
        var mimeType = GetString(file, "mimeType") ?? GetString(file, "mime_type") ?? "audio/wav";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uri))
            throw new TranscriptionClientException("Google returned incomplete uploaded file metadata.");

        return new UploadedFile(name, uri, mimeType);
    }

    private async Task WaitUntilActiveAsync(
        string fileName,
        string apiKey,
        TimeSpan readyTimeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + readyTimeout;

        while (true)
        {
            using var response = await SendWithRetryAsync(
                () => CreateApiRequest(HttpMethod.Get, $"v1beta/{fileName}", apiKey),
                "File readiness check",
                cancellationToken).ConfigureAwait(false);
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var state = GetString(document.RootElement, "state");

            if (string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase))
                throw new TranscriptionClientException("Google could not process the uploaded audio.");
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TranscriptionClientException("Uploaded audio did not become ready in time.");

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> InteractAsync(
        string fileUri,
        string mimeType,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            input = new[]
            {
                new { type = "audio", uri = fileUri, mime_type = mimeType }
            },
            generation_config = new
            {
                transcription_config = new
                {
                    language_codes = Array.Empty<string>(),
                    mode = "smart"
                }
            }
        });

        using var response = await SendWithRetryAsync(
            () =>
            {
                var request = CreateApiRequest(HttpMethod.Post, "v1beta/interactions", apiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return request;
            },
            "Smart transcription",
            cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var text = ExtractText(document.RootElement);

        if (string.IsNullOrWhiteSpace(text))
            throw new TranscriptionClientException("No speech could be transcribed.");

        return text.Trim();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        string operation,
        CancellationToken cancellationToken,
        int maximumAttempts = 3)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            using var request = requestFactory();

            try
            {
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return response;

                if (!IsTransient(response.StatusCode) || attempt == maximumAttempts)
                {
                    var exception = CreateHttpException(operation, response.StatusCode);
                    response.Dispose();
                    throw exception;
                }

                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TranscriptionClientException)
            {
                throw;
            }
            catch (HttpRequestException exception) when (attempt < maximumAttempts)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new TranscriptionClientException("A network error interrupted transcription.", innerException: exception);
            }
        }

        throw new TranscriptionClientException("A network error interrupted transcription.", innerException: lastException);
    }

    private async Task DeleteRemoteFileAsync(string fileName, string apiKey)
    {
        using var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            using var response = await SendWithRetryAsync(
                () => CreateApiRequest(HttpMethod.Delete, $"v1beta/{fileName}", apiKey),
                "Remote audio cleanup",
                cleanupCancellation.Token,
                maximumAttempts: 2).ConfigureAwait(false);
            _logger?.Info("Voice remote audio cleanup succeeded.");
        }
        catch (Exception exception)
        {
            _logger?.Warning("Voice remote audio cleanup failed; Google retention policy may apply.", exception);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || code >= 500;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta <= TimeSpan.FromSeconds(10) ? delta : TimeSpan.FromSeconds(10);
        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay <= TimeSpan.FromSeconds(10) ? delay : TimeSpan.FromSeconds(10);
        }

        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private HttpRequestMessage CreateApiRequest(HttpMethod method, string relativePath, string apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri(DefaultApiBase, relativePath));
        request.Headers.Add("x-goog-api-key", apiKey);
        return request;
    }

    private static TranscriptionClientException CreateHttpException(string operation, HttpStatusCode statusCode)
    {
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Google Voice authentication failed.",
            (HttpStatusCode)429 => "Transcription is temporarily rate-limited or the Voice API quota is unavailable.",
            HttpStatusCode.RequestTimeout => "Transcription timed out.",
            _ when (int)statusCode >= 500 => "Google transcription is temporarily unavailable.",
            _ => $"{operation} failed with HTTP {(int)statusCode}."
        };

        return new TranscriptionClientException(message, (int)statusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    internal static string ExtractText(JsonElement root)
    {
        var parts = new List<string>();
        Add(GetString(root, "output_text"));
        Add(GetString(root, "outputText"));

        if (root.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
        {
            foreach (var output in outputs.EnumerateArray())
            {
                if (string.Equals(GetString(output, "type"), "text", StringComparison.OrdinalIgnoreCase))
                    Add(GetString(output, "text"));
            }
        }

        if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in content.EnumerateArray())
                {
                    if (string.Equals(GetString(item, "type"), "text", StringComparison.OrdinalIgnoreCase))
                        Add(GetString(item, "text"));
                }
            }
        }

        return string.Join(Environment.NewLine, parts);

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !parts.Contains(value, StringComparer.Ordinal))
                parts.Add(value);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed record UploadedFile(string Name, string Uri, string MimeType);
}
