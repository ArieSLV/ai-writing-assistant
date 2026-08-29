namespace AiWritingAssistant.Tests;

public sealed class VoiceTrayPresentationMapperTests
{
    [Theory]
    [InlineData((int)VoiceDictationState.Stopping, "Finishing voice recording")]
    [InlineData((int)VoiceDictationState.Normalizing, "Preparing voice audio")]
    [InlineData((int)VoiceDictationState.Uploading, "Uploading voice audio")]
    [InlineData((int)VoiceDictationState.WaitingForFile, "Google is preparing voice audio")]
    [InlineData((int)VoiceDictationState.Transcribing, "Transcribing voice audio")]
    [InlineData((int)VoiceDictationState.Copying, "Copying dictated text")]
    public void Post_stop_states_use_processing_icon_and_English_tooltip(
        int stateValue,
        string expectedStatus)
    {
        var state = (VoiceDictationState)stateValue;
        var presentation = VoiceTrayPresentationMapper.ForState(state);

        Assert.Equal(VoiceTrayIconState.Processing, presentation.IconState);
        Assert.Contains(expectedStatus, presentation.Tooltip, StringComparison.Ordinal);
        Assert.DoesNotMatch("[А-Яа-яЁё]", presentation.Tooltip);
        Assert.True(presentation.Tooltip.Length <= 63);
    }

    [Theory]
    [InlineData((int)VoiceDictationOutcome.Succeeded, (int)VoiceTrayIconState.Succeeded, "Voice text copied")]
    [InlineData((int)VoiceDictationOutcome.Failed, (int)VoiceTrayIconState.Failed, "Voice dictation failed")]
    [InlineData((int)VoiceDictationOutcome.Cancelled, (int)VoiceTrayIconState.Cancelled, "Voice dictation cancelled")]
    public void Terminal_outcomes_have_distinct_tray_presentations(
        int outcomeValue,
        int expectedIconValue,
        string expectedStatus)
    {
        var outcome = (VoiceDictationOutcome)outcomeValue;
        var expectedIcon = (VoiceTrayIconState)expectedIconValue;
        var presentation = VoiceTrayPresentationMapper.ForOutcome(outcome);

        Assert.Equal(expectedIcon, presentation.IconState);
        Assert.Contains(expectedStatus, presentation.Tooltip, StringComparison.Ordinal);
        Assert.DoesNotMatch("[А-Яа-яЁё]", presentation.Tooltip);
        Assert.True(presentation.Tooltip.Length <= 63);
    }

    [Fact]
    public void Failure_detail_is_bounded_for_NotifyIcon_tooltip()
    {
        var presentation = VoiceTrayPresentationMapper.ForOutcome(
            VoiceDictationOutcome.Failed,
            new string('x', 200));

        Assert.Equal(63, presentation.Tooltip.Length);
        Assert.EndsWith("...", presentation.Tooltip, StringComparison.Ordinal);
    }
}
