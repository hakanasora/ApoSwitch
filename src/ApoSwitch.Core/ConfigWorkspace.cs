using System.Security;
using System.Text.Json;

namespace ApoSwitch.Core;

/// <summary>Settings and active root file; selecting a location never writes an APO configuration.</summary>
public sealed class ConfigWorkspace
{
    private readonly SettingsStore settingsStore;
    private readonly Func<string> automaticPath;
    private readonly Action<string> reportSettingsError;
    private bool settingsLoaded;
    private string? settingsError;
    public UserSettings? Settings { get; private set; }
    public ConfigStore? Store { get; private set; }
    public ConfigDocument? Document { get; private set; }
    public string? Error { get; private set; }

    public ConfigWorkspace(SettingsStore settingsStore, Func<string> automaticPath, Action<string>? reportSettingsError = null)
    {
        this.settingsStore = settingsStore;
        this.automaticPath = automaticPath;
        this.reportSettingsError = reportSettingsError ?? (_ => { });
        TryLoadSettings();
    }

    public void Refresh()
    {
        if (!TryLoadSettings()) return;
        try
        {
            string path = Settings?.ConfigFilePath ?? automaticPath();
            if (Store is null || !StringComparer.OrdinalIgnoreCase.Equals(Store.FilePath, Path.GetFullPath(path)))
                Store = new ConfigStore(path);
            Document = Store.Read();
            Error = null;
        }
        catch (Exception ex) { Document = null; Error = ex.Message; }
        if (Document is not null && Settings is null)
        {
            Settings = UserSettings.Initial(Document.Presets);
            try { settingsStore.Save(Settings); }
            catch (Exception ex) { reportSettingsError("无法保存循环设置：" + ex.Message); }
        }
    }

    private bool TryLoadSettings()
    {
        if (settingsLoaded) return true;
        try
        {
            Settings = settingsStore.LoadSaved();
            settingsLoaded = true;
            settingsError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            string message = "无法读取用户设置：" + ex.Message;
            Document = null;
            Error = message;
            if (settingsError != message) reportSettingsError(message);
            settingsError = message;
            return false;
        }
    }

    public void SelectFile(string path)
    {
        var candidate = new ConfigStore(path);
        var document = candidate.Read(); // Validate first; failed or cancelled selections leave all prior state intact.
        var changed = Settings?.Copy() ?? UserSettings.Initial(document.Presets);
        changed.ConfigFilePath = candidate.FilePath;
        settingsStore.Save(changed);
        Settings = changed;
        settingsLoaded = true;
        settingsError = null;
        Store = candidate;
        Document = document;
        Error = null;
    }

    public void RestoreAutomatic()
    {
        if (Settings is not null)
        {
            var changed = Settings.Copy();
            changed.ConfigFilePath = null;
            settingsStore.Save(changed);
            Settings = changed;
        }
        Refresh();
    }

    public bool ToggleCycle(string id)
    {
        if (Settings is null) return false;
        var changed = Settings.Copy();
        if (changed.Contains(id)) changed.CyclePresetIds.RemoveAll(p => StringComparer.OrdinalIgnoreCase.Equals(p, id));
        else changed.CyclePresetIds.Add(id);
        settingsStore.Save(changed);
        Settings = changed;
        return Settings.Contains(id);
    }
}
