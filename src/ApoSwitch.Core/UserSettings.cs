using System.Text.Json;

namespace ApoSwitch.Core;

public sealed class UserSettings
{
    public List<string> CyclePresetIds { get; set; } = [];
    public string? ConfigFilePath { get; set; }
    public UserSettings Copy() => new() { CyclePresetIds = [.. CyclePresetIds], ConfigFilePath = ConfigFilePath };
    public static UserSettings Initial(IReadOnlyList<Preset> presets)
    {
        var selected = presets.Where(p => p.Name.Equals("New Door Open", StringComparison.OrdinalIgnoreCase)
            || p.Name.Equals("GSR_BK_filters", StringComparison.OrdinalIgnoreCase)).Select(p => p.Id).ToList();
        if (selected.Count == 0) selected = presets.Where(p => p.IsEnabled).Select(p => p.Id).ToList();
        return new() { CyclePresetIds = selected };
    }
    public bool Contains(string id) => CyclePresetIds.Contains(id, StringComparer.OrdinalIgnoreCase);
    public string? Next(IReadOnlyList<Preset> presets)
    {
        var cycle = presets.Where(p => Contains(p.Id)).ToList();
        if (cycle.Count == 0) return null;
        int current = cycle.FindIndex(p => p.IsEnabled);
        return cycle[(current + 1) % cycle.Count].Id;
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object saveGate = new();
    public string FilePath { get; } = Path.GetFullPath(path);

    public UserSettings? LoadSaved()
    {
        string json;
        try { json = File.ReadAllText(FilePath); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        var settings = JsonSerializer.Deserialize<UserSettings>(json)
            ?? throw new IOException("用户设置为空。");
        settings.CyclePresetIds = (settings.CyclePresetIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.IsNullOrWhiteSpace(settings.ConfigFilePath)) settings.ConfigFilePath = null;
        else if (!Path.IsPathFullyQualified(settings.ConfigFilePath))
            throw new IOException("保存的主配置路径不是绝对路径。");
        return settings;
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".aposwitch-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(true);
            }
            lock (saveGate) File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
