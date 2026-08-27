using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiWritingAssistant;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string _filePath;
    private readonly AppLogger? _logger;

    public AppSettingsStore(string filePath, AppLogger? logger = null)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return AppSettings.CreateDefault();

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? AppSettings.CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to load settings from '{_filePath}'. Falling back to defaults.", ex);
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            settings.Normalize();

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to save settings to '{_filePath}'.", ex);
            throw;
        }
    }
}
