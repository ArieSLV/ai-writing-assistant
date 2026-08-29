namespace AiWritingAssistant.Tests;

public sealed class TrayMenuLayoutTests
{
    [Fact]
    public void Text_actions_and_settings_are_grouped_while_voice_is_separate()
    {
        Assert.Equal(
            new[]
            {
                TrayMenuEntry.Proofread,
                TrayMenuEntry.Translate,
                TrayMenuEntry.TextModelSettings,
                TrayMenuEntry.Separator,
                TrayMenuEntry.VoiceDictation,
                TrayMenuEntry.Separator,
                TrayMenuEntry.Exit
            },
            TrayMenuLayout.RootEntries);
        Assert.Equal("Text Model Settings", TrayMenuLayout.TextModelSettingsLabel);
    }
}
