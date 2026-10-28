using ApoSwitch;
using ApoSwitch.Core;

internal static partial class Checks
{
    private static void WindowsReliabilityChecks()
    {
        string directory = Path.Combine(root, "windows-reliability");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.txt");
        File.WriteAllText(path, "Include: ReliabilityPreset.txt\n");
        var config = ConfigDocument.Load(path);
        var settings = new UserSettings();

        IReadOnlyList<MenuEntry> Menu(DeviceState device, string? error = null) => TrayMenuBuilder.Build(
            device, config, error, settings, false, false, _ => { }, _ => false,
            () => { }, () => { }, () => { }, () => { });

        var failures = Menu(new(DeviceAvailability.Error, "测试设备", Detail: "device\r\nunavailable\tdetails"),
            "config\r\nunreadable\tdetails");
        Assert(failures.Any(entry => entry.Text == "device  unavailable details" && !entry.Enabled),
            "设备检测失败展示具体原因并清理换行和制表符");
        Assert(failures.Any(entry => entry.Text == "config  unreadable details" && !entry.Enabled),
            "配置与设备错误使用相同的单行文本处理");
        Assert(failures.All(entry => !entry.Text.Contains('\r') && !entry.Text.Contains('\n') && !entry.Text.Contains('\t')),
            "错误详情不会产生多行或带制表符的菜单项");

        string longDeviceError = new('D', 180);
        string longConfigError = new('C', 180);
        var longFailures = Menu(new(DeviceAvailability.Error, Detail: longDeviceError), longConfigError);
        Assert(longFailures.Any(entry => entry.Text == new string('D', 139) + "…" && !entry.Enabled),
            "设备错误详情最多显示140字符并保留省略号");
        Assert(longFailures.Any(entry => entry.Text == new string('C', 139) + "…" && !entry.Enabled),
            "配置错误详情采用相同长度限制");
        Assert(Menu(new(DeviceAvailability.Error, Detail: " \r\n\t ")).All(entry => !string.IsNullOrWhiteSpace(entry.Text)),
            "空白设备详情不增加空菜单项");

        foreach (var state in Enum.GetValues<DeviceAvailability>())
        {
            var menu = Menu(new(state, "测试设备", Detail: state == DeviceAvailability.Error ? "检测失败原因" : null));
            Assert(menu.Single(entry => entry.Text == "ReliabilityPreset").Enabled == (state == DeviceAvailability.Installed),
                "设备详情不改变配置应用门控：" + state);
            Assert(menu.Single(entry => entry.Text == "循环以下配置").Children!.All(entry => entry.Enabled)
                && menu.Single(entry => entry.Text == "配置文件已自动识别").Children![0].Enabled
                && menu.Single(entry => entry.Text == "开机自启动").Enabled,
                "设备详情不影响循环、手动配置和自启动：" + state);
        }
        Assert(!Menu(new(DeviceAvailability.Installed), "配置不可读").Single(entry => entry.Text == "ReliabilityPreset").Enabled,
            "设备正常时配置错误仍阻止应用");
        NativeMenuLifecycleChecks();
    }

    private static void WindowsDeviceSmokeChecks()
    {
        var source = new WindowsDeviceStateSource(_ => { });
        try
        {
            source.Refresh();
            source.Refresh();
            Assert(source.Current.Availability != DeviceAvailability.Error, "真实设备连续查询成功（只读）");
            source.Dispose();
            var state = source.Current;
            int notifications = 0;
            source.Changed += (_, _) => notifications++;
            source.Dispose();
            source.Refresh();
            Assert(source.Current == state && notifications == 0, "设备源重复释放安全且释放后刷新不再工作");
        }
        finally { source.Dispose(); }
    }
}
