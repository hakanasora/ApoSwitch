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
    public string FilePath { get; } = path;
    public UserSettings? LoadSaved() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? throw new IOException("循环设置为空。")
        : null;
    public UserSettings Load(IReadOnlyList<Preset> presets) => LoadSaved() ?? UserSettings.Initial(presets);
    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
