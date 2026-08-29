namespace AiWritingAssistant;

internal enum VoiceDictationOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

internal sealed class VoiceDictationOutcomeEventArgs(
    VoiceDictationOutcome outcome,
    string userMessage) : EventArgs
{
    public VoiceDictationOutcome Outcome { get; } = outcome;

    public string UserMessage { get; } = userMessage;
}

internal enum VoiceTrayIconState
{
    Idle,
    Processing,
    Succeeded,
    Failed,
    Cancelled
}

internal readonly record struct VoiceTrayPresentation(
    VoiceTrayIconState IconState,
    string Tooltip,
    string MenuText);

internal static class VoiceTrayPresentationMapper
{
    private const int MaximumTooltipLength = 63;

    public static VoiceTrayPresentation ForState(VoiceDictationState state)
    {
        return state switch
        {
            VoiceDictationState.Idle => Idle(),
            VoiceDictationState.Starting => Processing("Starting voice recording", "Voice Dictation is starting"),
            VoiceDictationState.Recording => Processing("Recording", "Stop Voice Dictation"),
            VoiceDictationState.Stopping => Processing("Finishing voice recording"),
            VoiceDictationState.Normalizing => Processing("Preparing voice audio"),
            VoiceDictationState.Uploading => Processing("Uploading voice audio"),
            VoiceDictationState.WaitingForFile => Processing("Google is preparing voice audio"),
            VoiceDictationState.Transcribing => Processing("Transcribing voice audio"),
            VoiceDictationState.Copying => Processing("Copying dictated text"),
            VoiceDictationState.Cancelling => Processing("Cancelling voice dictation"),
            VoiceDictationState.Disposed => new VoiceTrayPresentation(
                VoiceTrayIconState.Idle,
                "AI Writing Assistant",
                "Voice Dictation unavailable"),
            _ => Idle()
        };
    }

    public static VoiceTrayPresentation ForOutcome(
        VoiceDictationOutcome outcome,
        string? userMessage = null)
    {
        return outcome switch
        {
            VoiceDictationOutcome.Succeeded => new VoiceTrayPresentation(
                VoiceTrayIconState.Succeeded,
                "AI Writing Assistant — Voice text copied",
                "Start Voice Dictation"),
            VoiceDictationOutcome.Failed => new VoiceTrayPresentation(
                VoiceTrayIconState.Failed,
                BuildTooltip(string.IsNullOrWhiteSpace(userMessage)
                    ? "Voice dictation failed"
                    : userMessage),
                "Start Voice Dictation"),
            VoiceDictationOutcome.Cancelled => new VoiceTrayPresentation(
                VoiceTrayIconState.Cancelled,
                "AI Writing Assistant — Voice dictation cancelled",
                "Start Voice Dictation"),
            _ => Idle()
        };
    }

    private static VoiceTrayPresentation Idle()
    {
        return new VoiceTrayPresentation(
            VoiceTrayIconState.Idle,
            "AI Writing Assistant",
            "Start Voice Dictation");
    }

    private static VoiceTrayPresentation Processing(
        string status,
        string menuText = "Voice Dictation is processing")
    {
        return new VoiceTrayPresentation(
            VoiceTrayIconState.Processing,
            BuildTooltip(status),
            menuText);
    }

    private static string BuildTooltip(string status)
    {
        var tooltip = $"AI Writing Assistant — {status.Trim()}";
        return tooltip.Length <= MaximumTooltipLength
            ? tooltip
            : $"{tooltip[..(MaximumTooltipLength - 3)]}...";
    }
}
