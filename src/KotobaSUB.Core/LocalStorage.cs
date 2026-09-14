using System.Text.Json;

namespace KotobaSUB.Core;

public sealed class SettingsStore(string path, Action<string> report)
{
    public OverlaySettings Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path)) ?? throw new JsonException("Empty settings");
            if (settings.Version != 1) throw new JsonException("Unsupported settings version");
            return settings.Validate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            report($"Settings could not be loaded; using defaults: {ex.Message}");
            return new();
        }
    }
    public void Save(OverlaySettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Validate(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}

public sealed class LocalLog(string directory)
{
    private readonly object gate = new();
    public void Write(string message)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "kotobasub.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
    }
}
