param(
    [Parameter(Mandatory = $true)]
    [string] $AudioPath,

    [ValidateSet('smart', 'verbatim')]
    [string] $Mode = 'smart',

    [switch] $ShowTranscript,

    [switch] $DeleteInput
)

$ErrorActionPreference = 'Stop'

$voiceKeyVariable = 'AI_WRITING_ASSISTANT_GOOGLE_VOICE_API_KEY'
$model = 'gemini-3.5-transcribe'
$apiBase = 'https://generativelanguage.googleapis.com'
$resolvedAudioPath = (Resolve-Path -LiteralPath $AudioPath).Path
$audioFile = Get-Item -LiteralPath $resolvedAudioPath

if ($audioFile.Extension -ne '.wav') {
    throw 'The proof tool currently accepts WAV input only.'
}

$voiceKey = [Environment]::GetEnvironmentVariable(
    $voiceKeyVariable,
    [EnvironmentVariableTarget]::Process)

if ([string]::IsNullOrWhiteSpace($voiceKey)) {
    throw "$voiceKeyVariable is not available in the current process."
}

function New-ApiRequest {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpMethod] $Method,

        [Parameter(Mandatory = $true)]
        [string] $Uri
    )

    $request = [System.Net.Http.HttpRequestMessage]::new($Method, $Uri)
    $request.Headers.Add('x-goog-api-key', $voiceKey)
    return $request
}

function Read-JsonResponse {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpResponseMessage] $Response,

        [Parameter(Mandatory = $true)]
        [string] $Operation
    )

    $body = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    if (-not $Response.IsSuccessStatusCode) {
        $safeStatus = "HTTP $([int] $Response.StatusCode)"
        try {
            $errorEnvelope = $body | ConvertFrom-Json
            if ($errorEnvelope.error.status) {
                $safeStatus = "$safeStatus / $($errorEnvelope.error.status)"
            }
        }
        catch {
            # Raw response bodies are intentionally not printed.
        }

        throw "$Operation failed: $safeStatus"
    }

    if ([string]::IsNullOrWhiteSpace($body)) {
        return $null
    }

    return $body | ConvertFrom-Json
}

$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds(90)
$cancellationSource = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(90))
$proofCancellationToken = $cancellationSource.Token
$uploadedFileName = $null
$uploadedFileUri = $null
$remoteDeleteOk = $false
$uploadMilliseconds = 0L
$readyWaitMilliseconds = 0L
$interactionMilliseconds = 0L
$deleteMilliseconds = 0L
$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $uploadStartRequest = New-ApiRequest `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Uri "$apiBase/upload/v1beta/files"

    try {
        $uploadStartRequest.Headers.Add('X-Goog-Upload-Protocol', 'resumable')
        $uploadStartRequest.Headers.Add('X-Goog-Upload-Command', 'start')
        $uploadStartRequest.Headers.Add(
            'X-Goog-Upload-Header-Content-Length',
            $audioFile.Length.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        $uploadStartRequest.Headers.Add('X-Goog-Upload-Header-Content-Type', 'audio/wav')

        $displayName = "voice-proof-$([Guid]::NewGuid().ToString('N'))"
        $metadata = @{ file = @{ display_name = $displayName } } | ConvertTo-Json -Compress
        $uploadStartRequest.Content = [System.Net.Http.StringContent]::new(
            $metadata,
            [System.Text.Encoding]::UTF8,
            'application/json')

        $uploadStartResponse = $httpClient.SendAsync(
            $uploadStartRequest,
            $proofCancellationToken).GetAwaiter().GetResult()
        try {
            if (-not $uploadStartResponse.IsSuccessStatusCode) {
                [void] (Read-JsonResponse -Response $uploadStartResponse -Operation 'Upload session start')
            }

            $uploadUrls = $null
            if (-not $uploadStartResponse.Headers.TryGetValues('X-Goog-Upload-URL', [ref] $uploadUrls)) {
                throw 'Upload session start succeeded without X-Goog-Upload-URL.'
            }

            $uploadUrl = @($uploadUrls)[0]
        }
        finally {
            $uploadStartResponse.Dispose()
        }
    }
    finally {
        $uploadStartRequest.Dispose()
    }

    $uploadStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $uploadRequest = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        $uploadUrl)

    try {
        $uploadRequest.Headers.Add('X-Goog-Upload-Offset', '0')
        $uploadRequest.Headers.Add('X-Goog-Upload-Command', 'upload, finalize')
        $audioStream = [System.IO.File]::OpenRead($resolvedAudioPath)
        $uploadRequest.Content = [System.Net.Http.StreamContent]::new($audioStream)
        $uploadRequest.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('audio/wav')
        $uploadRequest.Content.Headers.ContentLength = $audioFile.Length

        $uploadResponse = $httpClient.SendAsync(
            $uploadRequest,
            $proofCancellationToken).GetAwaiter().GetResult()
        try {
            $uploadEnvelope = Read-JsonResponse -Response $uploadResponse -Operation 'File upload'
        }
        finally {
            $uploadResponse.Dispose()
        }
    }
    finally {
        $uploadRequest.Dispose()
        $uploadStopwatch.Stop()
        $uploadMilliseconds = $uploadStopwatch.ElapsedMilliseconds
    }

    $uploadedFileName = $uploadEnvelope.file.name
    $uploadedFileUri = $uploadEnvelope.file.uri
    $uploadedMimeType = $uploadEnvelope.file.mimeType

    if ([string]::IsNullOrWhiteSpace($uploadedMimeType)) {
        $uploadedMimeType = 'audio/wav'
    }

    if ([string]::IsNullOrWhiteSpace($uploadedFileName) -or
        [string]::IsNullOrWhiteSpace($uploadedFileUri)) {
        throw 'File upload response did not contain a file name and URI.'
    }

    $readyStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $metadataRequest = New-ApiRequest `
                -Method ([System.Net.Http.HttpMethod]::Get) `
                -Uri "$apiBase/v1beta/$uploadedFileName"

            try {
                $metadataResponse = $httpClient.SendAsync(
                    $metadataRequest,
                    $proofCancellationToken).GetAwaiter().GetResult()
                try {
                    $fileMetadata = Read-JsonResponse -Response $metadataResponse -Operation 'File readiness check'
                }
                finally {
                    $metadataResponse.Dispose()
                }
            }
            finally {
                $metadataRequest.Dispose()
            }

            $fileState = [string] $fileMetadata.state
            if ($fileState -eq 'FAILED') {
                throw 'Uploaded file entered FAILED state.'
            }

            if ($fileState -ne 'ACTIVE') {
                Start-Sleep -Milliseconds 250
            }
        }
        while ($fileState -ne 'ACTIVE' -and [DateTimeOffset]::UtcNow -lt $readyDeadline)

        if ($fileState -ne 'ACTIVE') {
            throw 'Uploaded file did not become ACTIVE within 30 seconds.'
        }
    }
    finally {
        $readyStopwatch.Stop()
        $readyWaitMilliseconds = $readyStopwatch.ElapsedMilliseconds
    }

    $modeConfig = if ($Mode -eq 'smart') {
        'smart'
    }
    else {
        @{ type = 'verbatim' }
    }

    $interactionBody = @{
        model = $model
        input = @(
            @{
                type = 'audio'
                uri = $uploadedFileUri
                mime_type = $uploadedMimeType
            }
        )
        generation_config = @{
            transcription_config = @{
                language_codes = @()
                mode = $modeConfig
            }
        }
    } | ConvertTo-Json -Compress -Depth 10

    $interactionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $interactionRequest = New-ApiRequest `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Uri "$apiBase/v1beta/interactions"

    try {
        $interactionRequest.Content = [System.Net.Http.StringContent]::new(
            $interactionBody,
            [System.Text.Encoding]::UTF8,
            'application/json')

        $interactionResponse = $httpClient.SendAsync(
            $interactionRequest,
            $proofCancellationToken).GetAwaiter().GetResult()
        try {
            $interactionEnvelope = Read-JsonResponse `
                -Response $interactionResponse `
                -Operation 'Smart transcription interaction'
        }
        finally {
            $interactionResponse.Dispose()
        }
    }
    finally {
        $interactionRequest.Dispose()
        $interactionStopwatch.Stop()
        $interactionMilliseconds = $interactionStopwatch.ElapsedMilliseconds
    }

    $transcriptParts = [System.Collections.Generic.List[string]]::new()

    foreach ($candidate in @(
        [string] $interactionEnvelope.output_text,
        [string] $interactionEnvelope.outputText)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $transcriptParts.Add($candidate)
        }
    }

    foreach ($output in @($interactionEnvelope.outputs)) {
        if ($output.type -eq 'text' -and
            -not [string]::IsNullOrWhiteSpace([string] $output.text)) {
            $transcriptParts.Add([string] $output.text)
        }
    }

    foreach ($step in @($interactionEnvelope.steps)) {
        foreach ($content in @($step.content)) {
            if ($content.type -eq 'text' -and
                -not [string]::IsNullOrWhiteSpace([string] $content.text)) {
                $transcriptParts.Add([string] $content.text)
            }
        }
    }

    $transcript = ($transcriptParts | Select-Object -Unique) -join [Environment]::NewLine

    if ([string]::IsNullOrWhiteSpace($transcript)) {
        $responseProperties = @($interactionEnvelope.PSObject.Properties.Name) -join ','
        throw "Interaction succeeded without extractable text. Response properties: $responseProperties"
    }

    "MODEL=$model"
    "MODE=$Mode"
    "AUDIO_BYTES=$($audioFile.Length)"
    "UPLOAD_MS=$uploadMilliseconds"
    "READY_WAIT_MS=$readyWaitMilliseconds"
    "INTERACTION_MS=$interactionMilliseconds"
    "TRANSCRIPT_LENGTH=$($transcript.Length)"

    if ($ShowTranscript) {
        "TRANSCRIPT=$transcript"
    }
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($uploadedFileName)) {
        $deleteStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $deleteRequest = New-ApiRequest `
                -Method ([System.Net.Http.HttpMethod]::Delete) `
                -Uri "$apiBase/v1beta/$uploadedFileName"

            try {
                $deleteResponse = $httpClient.SendAsync(
                    $deleteRequest,
                    $proofCancellationToken).GetAwaiter().GetResult()
                try {
                    $remoteDeleteOk = $deleteResponse.IsSuccessStatusCode
                    if (-not $remoteDeleteOk) {
                        "REMOTE_DELETE_STATUS=HTTP_$([int] $deleteResponse.StatusCode)"
                    }
                }
                finally {
                    $deleteResponse.Dispose()
                }
            }
            finally {
                $deleteRequest.Dispose()
            }
        }
        catch {
            'REMOTE_DELETE_STATUS=EXCEPTION'
        }
        finally {
            $deleteStopwatch.Stop()
            $deleteMilliseconds = $deleteStopwatch.ElapsedMilliseconds
        }
    }

    $totalStopwatch.Stop()
    "DELETE_MS=$deleteMilliseconds"
    "REMOTE_DELETE_OK=$remoteDeleteOk"
    "TOTAL_MS=$($totalStopwatch.ElapsedMilliseconds)"

    $httpClient.Dispose()
    $cancellationSource.Dispose()

    if ($DeleteInput -and (Test-Path -LiteralPath $resolvedAudioPath)) {
        Remove-Item -LiteralPath $resolvedAudioPath -Force
        'LOCAL_INPUT_DELETED=True'
    }
}
