using System.Reflection;
using ApoSwitch;
using ApoSwitch.Core;

internal static partial class Checks
{
    private static void TrayIntegrationChecks()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        string directory = Path.Combine(root, "tray-integration");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.txt");
        string one = Path.Combine(directory, "One.txt"), three = Path.Combine(directory, "Three.txt");
        File.WriteAllText(one, "Include: Nested.txt\n");
        File.WriteAllText(three, "# unchanged nested content\n");
        File.WriteAllText(path, "Include: One.txt\n# Include: Two.txt\n# Include: Three.txt\n");
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new UserSettings { ConfigFilePath = path, CyclePresetIds = [one, three] });
        var workspace = new ConfigWorkspace(store, () => throw new Exception("测试不应读取真实APO注册表。"));
        var device = new MutableDeviceSource();
        using var context = new TrayApplication(_ => device, configuration: workspace);
        var icon = (NotifyIcon)typeof(TrayApplication).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        Assert(icon.Text.StartsWith("One") && workspace.Document!.Presets.Count(p => p.IsEnabled) == 1,
            "托盘上下文启动只读取隔离配置并显示当前项");
        byte[] nested = File.ReadAllBytes(one);

        void DoubleClick()
        {
            var mouse = new MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0);
            typeof(NotifyIcon).GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icon, [mouse]);
            typeof(NotifyIcon).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icon, [mouse]);
        }

        DoubleClick();
        Assert(workspace.Document!.Presets.Single(p => p.IsEnabled).Id == three && icon.Text.StartsWith("Three"),
            "真实托盘双击事件只启用下一循环成员并刷新提示");
        Assert(device.RefreshCount == 1, "一次双击不重复查询设备状态");
        Assert(File.ReadAllBytes(one).SequenceEqual(nested), "托盘切换不改动被包含文件内容");

        byte[] active = File.ReadAllBytes(path);
        device.Set(DeviceAvailability.NotInstalled);
        Application.DoEvents();
        DoubleClick();
        Assert(File.ReadAllBytes(path).SequenceEqual(active) && icon.Text.StartsWith("当前设备不适用"),
            "不适用设备的真实双击事件不修改配置");
        device.Set(DeviceAvailability.Installed);
        Application.DoEvents();
        DoubleClick();
        Assert(workspace.Document!.Presets.Single(p => p.IsEnabled).Id == one, "设备恢复后双击继续按原循环顺序切换");

        workspace.ToggleCycle(one);
        workspace.ToggleCycle(three);
        byte[] emptyCycle = File.ReadAllBytes(path);
        DoubleClick();
        Assert(File.ReadAllBytes(path).SequenceEqual(emptyCycle), "真实托盘空循环不写入");
        context.Dispose();
        context.Dispose();
        Application.DoEvents();
        Assert(device.DisposeCount == 1, "托盘上下文重复退出只释放一次设备源");
    }

    private sealed class MutableDeviceSource : IDeviceStateSource
    {
        public DeviceState Current { get; private set; } = new(DeviceAvailability.Installed, "隔离测试设备");
        public event EventHandler? Changed;
        public int RefreshCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void Set(DeviceAvailability state) { Current = new(state, "隔离测试设备"); Changed?.Invoke(this, EventArgs.Empty); }
        public void Refresh() => RefreshCount++;
        public void Dispose() => DisposeCount++;
    }
}
