namespace AiWritingAssistant.Tests;

public sealed class VoiceSettingsTests
{
    [Fact]
    public void Normalize_applies_voice_defaults_and_bounds()
    {
        var settings = new AppSettings
        {
            VoiceTranscriptionModel = "  ",
            VoiceMaxRecordingSeconds = 0
        };

        settings.Normalize();

        Assert.Equal(AppSettings.DefaultVoiceTranscriptionModel, settings.VoiceTranscriptionModel);
        Assert.Equal(AppSettings.DefaultVoiceMaxRecordingSeconds, settings.VoiceMaxRecordingSeconds);
    }

    [Fact]
    public void Existing_settings_file_loads_with_voice_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AiWritingAssistant-settings-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                                    {
                                      "SelectedProvider": "Gemini",
                                      "GeminiModel": "gemini-existing",
                                      "OllamaBaseUrl": "http://127.0.0.1:11434"
                                    }
                                    """);

            var settings = new AppSettingsStore(path).Load();

            Assert.Equal("gemini-existing", settings.GeminiModel);
            Assert.Equal(AppSettings.DefaultVoiceTranscriptionModel, settings.VoiceTranscriptionModel);
            Assert.Equal(AppSettings.DefaultVoiceMaxRecordingSeconds, settings.VoiceMaxRecordingSeconds);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
