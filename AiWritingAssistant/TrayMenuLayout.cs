namespace AiWritingAssistant;

internal enum TrayMenuEntry
{
    Proofread,
    Translate,
    TextModelSettings,
    Separator,
    VoiceDictation,
    Exit
}

internal static class TrayMenuLayout
{
    public const string TextModelSettingsLabel = "Text Model Settings";

    public static IReadOnlyList<TrayMenuEntry> RootEntries { get; } =
    [
        TrayMenuEntry.Proofread,
        TrayMenuEntry.Translate,
        TrayMenuEntry.TextModelSettings,
        TrayMenuEntry.Separator,
        TrayMenuEntry.VoiceDictation,
        TrayMenuEntry.Separator,
        TrayMenuEntry.Exit
    ];
}
