param(
    [string] $SessionId = 'integrated-20260828-02',
    [string] $FixtureId = 'F-01',
    [int] $Take = 98,
    [switch] $PreflightOnly
)

$ErrorActionPreference = 'Stop'

$proofDll = Join-Path $env:TEMP 'AiWritingAssistant\voice-overlay-build\bin\VoiceOverlayProof\debug\VoiceOverlayProof.dll'
$apiProofScript = Join-Path $PSScriptRoot 'voice-transcription-proof.ps1'
$voiceKeyName = 'AI_WRITING_ASSISTANT_GOOGLE_VOICE_API_KEY'
$proofRoot = [IO.Path]::GetFullPath((Join-Path $env:TEMP "AiWritingAssistant\voice-proof\$SessionId"))
$expectedAudioPath = [IO.Path]::GetFullPath((Join-Path $proofRoot "$FixtureId-$($Take.ToString('00')).wav"))

if (-not $expectedAudioPath.StartsWith(
    $proofRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Integrated proof audio path escaped the owned proof root.'
}

$voiceKeyPresent = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        $voiceKeyName,
        [EnvironmentVariableTarget]::Process))

if ($PreflightOnly) {
    "PROOF_DLL_PRESENT=$(Test-Path -LiteralPath $proofDll -PathType Leaf)"
    "API_PROOF_SCRIPT_PRESENT=$(Test-Path -LiteralPath $apiProofScript -PathType Leaf)"
    "VOICE_KEY_PRESENT=$voiceKeyPresent"
    "CLIPBOARD_COMMAND_PRESENT=$($null -ne (Get-Command Set-Clipboard -ErrorAction SilentlyContinue))"
    'SAFE_API_FAILURE_CLASSIFICATION=True'
    'EXPECTED_AUDIO_OWNED=True'
    exit 0
}

if (-not (Test-Path -LiteralPath $proofDll -PathType Leaf)) {
    throw 'Verified VoiceOverlayProof build is missing.'
}

if (-not (Test-Path -LiteralPath $apiProofScript -PathType Leaf)) {
    throw 'Voice API proof adapter is missing.'
}

if (-not $voiceKeyPresent) {
    throw "$voiceKeyName is unavailable in the current process."
}

if (Test-Path -LiteralPath $expectedAudioPath) {
    throw 'Expected integrated proof audio path is not clean before recording.'
}

$clipboardBefore = [string] (Get-Clipboard -Raw -ErrorAction Stop)
$clipboardWritten = $false
$remoteDeleteOk = $false

try {
    $recorderOutput = @(
        & dotnet $proofDll `
            --fixture $FixtureId `
            --take $Take `
            --session $SessionId `
            --confirm-local-only)

    if ($LASTEXITCODE -ne 0) {
        throw "Recorder exited with code $LASTEXITCODE."
    }

    $audioPathLine = $recorderOutput |
        Where-Object { [string] $_ -like 'FIXTURE_AUDIO_PATH=*' } |
        Select-Object -First 1
    if ($null -eq $audioPathLine) {
        throw 'Recorder completed without a preserved canonical WAV.'
    }

    $reportedAudioPath = [IO.Path]::GetFullPath(
        ([string] $audioPathLine).Substring('FIXTURE_AUDIO_PATH='.Length))
    if (-not [string]::Equals(
        $reportedAudioPath,
        $expectedAudioPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Recorder returned an unexpected audio path.'
    }

    $apiOutput = @(
        & pwsh -NoProfile -File $apiProofScript `
            -AudioPath $reportedAudioPath `
            -Mode smart `
            -ShowTranscript `
            -DeleteInput 2>&1)
    $apiExitCode = $LASTEXITCODE
    $remoteDeleteOk = $apiOutput -contains 'REMOTE_DELETE_OK=True'

    if ($apiExitCode -ne 0) {
        $safeFailureText = ($apiOutput | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
        $httpMatch = [regex]::Match($safeFailureText, 'failed:\s+HTTP\s+(\d{3})')
        $failureClass = if ($httpMatch.Success) {
            "HTTP_$($httpMatch.Groups[1].Value)"
        }
        else {
            'OTHER'
        }

        "API_FAILURE_CLASS=$failureClass"
        throw "Voice API proof adapter exited with code $apiExitCode."
    }

    if (-not $remoteDeleteOk) {
        throw 'Remote cleanup was not confirmed.'
    }

    $transcriptLine = $apiOutput |
        Where-Object { ([string] $_).StartsWith('TRANSCRIPT=', [StringComparison]::Ordinal) } |
        Select-Object -First 1
    if ($null -eq $transcriptLine) {
        throw 'Successful API proof returned no transcript field.'
    }

    $transcript = ([string] $transcriptLine).Substring('TRANSCRIPT='.Length)
    if ([string]::IsNullOrWhiteSpace($transcript)) {
        throw 'Successful API proof returned an empty transcript.'
    }

    Set-Clipboard -Value $transcript -ErrorAction Stop
    $clipboardWritten = $true
    $clipboardAfter = [string] (Get-Clipboard -Raw -ErrorAction Stop)
    if (-not [string]::Equals($clipboardAfter, $transcript, [StringComparison]::Ordinal)) {
        throw 'Clipboard readback did not match the transcription result.'
    }

    $canonicalBytes = $recorderOutput |
        Where-Object { [string] $_ -like 'CANONICAL_WAV_BYTES=*' } |
        Select-Object -First 1
    $totalMilliseconds = $apiOutput |
        Where-Object { [string] $_ -like 'TOTAL_MS=*' } |
        Select-Object -First 1

    'INTEGRATED_PROOF_OK=True'
    'TRANSCRIPTION_ATTEMPTED=True'
    "TRANSCRIPT_LENGTH=$($transcript.Length)"
    'CLIPBOARD_UPDATED=True'
    'CLIPBOARD_READBACK_MATCH=True'
    "REMOTE_DELETE_OK=$remoteDeleteOk"
    "LOCAL_CLEANUP_OK=$(-not (Test-Path -LiteralPath $reportedAudioPath))"
    if ($canonicalBytes) { [string] $canonicalBytes }
    if ($totalMilliseconds) { [string] $totalMilliseconds }
}
catch {
    if (-not $clipboardWritten) {
        $clipboardAfterFailure = [string] (Get-Clipboard -Raw -ErrorAction SilentlyContinue)
        "CLIPBOARD_PRESERVED_ON_FAILURE=$([string]::Equals($clipboardAfterFailure, $clipboardBefore, [StringComparison]::Ordinal))"
    }

    "REMOTE_DELETE_OK=$remoteDeleteOk"
    'INTEGRATED_PROOF_OK=False'
    throw
}
finally {
    if (Test-Path -LiteralPath $expectedAudioPath) {
        Remove-Item -LiteralPath $expectedAudioPath -Force
    }

    "FINAL_LOCAL_CLEANUP_OK=$(-not (Test-Path -LiteralPath $expectedAudioPath))"
}
