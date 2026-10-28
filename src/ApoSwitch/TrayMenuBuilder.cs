using ApoSwitch.Core;

namespace ApoSwitch;

internal static class TrayMenuBuilder
{
    public static IReadOnlyList<MenuEntry> Build(DeviceState device, ConfigDocument? config, string? configError,
        UserSettings? settings, bool debug, bool autoStart, Action<string> apply, Func<string, bool> toggleCycle,
        Action toggleAutoStart, Action exit, Action chooseConfigFile, Action restoreAutomaticConfig)
    {
        var entries = new List<MenuEntry> { new(debug ? "APO 切换器 · 调试模式" : "APO 切换器", false), MenuEntry.Separator };
        if (!device.CanApply || configError is not null)
        {
            entries.Add(new(!device.CanApply ? device.StatusText : "无法读取 config.txt", false));
            string name = !string.IsNullOrWhiteSpace(device.Name) ? device.Name
                : device.Availability == DeviceAvailability.NoDevice ? "无播放设备" : "未知设备";
            entries.Add(new("设备：" + name, false));
            if (device.Availability == DeviceAvailability.Error && !string.IsNullOrWhiteSpace(device.Detail))
                entries.Add(new(SingleLineDetail(device.Detail), false));
            if (!device.CanApply && configError is not null) entries.Add(new("无法读取 config.txt", false));
            if (configError is not null)
                entries.Add(new(SingleLineDetail(configError), false));
            entries.Add(MenuEntry.Separator);
        }
        var presets = config?.Presets ?? [];
        foreach (var preset in presets)
        {
            string id = preset.Id;
            entries.Add(new(preset.Name, device.CanApply && configError is null, preset.IsEnabled, () => apply(id)));
        }
        if (presets.Count == 0) entries.Add(new("未找到一级配置", false));
        entries.Add(MenuEntry.Separator);
        var cycleEntries = presets.Select(p => new MenuEntry(p.Name, Checked: settings?.Contains(p.Id) == true,
            ToggleChecked: () => toggleCycle(p.Id))).ToList();
        if (cycleEntries.Count == 0) cycleEntries.Add(new("未找到一级配置", false));
        entries.Add(new("循环以下配置", Children: cycleEntries));
        entries.Add(MenuEntry.Separator);
        string locationText = settings?.ConfigFilePath is null && config is not null && configError is null
            ? "配置文件已自动识别" : "配置文件位置";
        entries.Add(new(locationText, Children:
        [
            new("手动配置 config.txt…", Action: chooseConfigFile),
            new("恢复自动定位", Enabled: settings?.ConfigFilePath is not null, Action: restoreAutomaticConfig)
        ]));
        entries.Add(MenuEntry.Separator);
        entries.Add(new("开机自启动", Checked: autoStart, Action: toggleAutoStart));
        entries.Add(new("退出", Action: exit));
        return entries;
    }

    private static string SingleLineDetail(string text)
    {
        string detail = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return detail.Length > 140 ? detail[..139] + "…" : detail;
    }
}
