using ApoSwitch;
using ApoSwitch.Core;

internal static partial class Checks
{
    private static void MenuChecks()
    {
        File.WriteAllText(Path.Combine(root, "config.txt"), "Include: A.txt\n# Include: B.txt\n");
        var config = ConfigDocument.Load(Path.Combine(root, "config.txt"));
        var settings = UserSettings.Initial(config.Presets);
        foreach (var state in Enum.GetValues<DeviceAvailability>())
        {
            var menu = TrayMenuBuilder.Build(new(state), config, null, settings, false, false, _ => { }, _ => false, () => { }, () => { }, () => { }, () => { });
            Assert(menu[1].Text == "-", "标题独立分组：" + state);
            Assert(menu.Where(e => config.Presets.Any(p => p.Name == e.Text)).All(e => e.Enabled == (state == DeviceAvailability.Installed)),
                "一级应用菜单门控：" + state);
            var cycle = menu.Single(e => e.Text == "循环以下配置");
            Assert(cycle.Children!.All(e => e.ToggleChecked is not null), "循环条目使用常驻勾选：" + state);
            Assert(cycle.Enabled && cycle.Children!.All(e => e.Enabled) && menu.Single(e => e.Text == "开机自启动").Enabled,
                "循环与自启动始终可用：" + state);
            var location = menu.Single(e => e.Text == "配置文件已自动识别");
            Assert(location.Enabled && location.Children!.Single(e => e.Text == "手动配置 config.txt…").Enabled,
                "所有设备状态均可手选配置位置：" + state);
            settings.ConfigFilePath = "C:\\selected\\config.txt";
            var manual = TrayMenuBuilder.Build(new(state), null, "文件缺失", settings, false, false,
                _ => { }, _ => false, () => { }, () => { }, () => { }, () => { });
            Assert(manual.Single(e => e.Text == "配置文件位置").Children!.All(e => e.Enabled),
                "文件缺失或设备不适用时仍可选路径、恢复自动：" + state);
            settings.ConfigFilePath = null;
        }
    }
}
