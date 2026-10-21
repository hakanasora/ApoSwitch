using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ApoSwitch;
using ApoSwitch.Core;
using ApoSwitch.DeviceDebug;
using Microsoft.Win32;

internal static class Checks
{
    private static int passed;
    private static string root = "";
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-release")
        {
            ReleaseChecks(args[1]);
            Console.WriteLine($"PASS: {passed} release isolation checks");
            return 0;
        }
        if (args.Contains("--startup-check"))
        {
            StartupChecks();
            return 0;
        }
        if (args.Contains("--menu-preview"))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.Run(new MenuPreview());
            return 0;
        }
        root = Path.Combine(Path.GetTempPath(), "ApoSwitch-Checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ConfigChecks();
            CycleChecks();
            MenuChecks();
            DebugChecks().GetAwaiter().GetResult();
            using var real = new WindowsDeviceStateSource(action => action());
            Console.WriteLine("真实设备（只读）：" + JsonSerializer.Serialize(real.Current));
            Assert(real.Current.Availability != DeviceAvailability.Error, "Core Audio 查询成功");
            if (args.Contains("--live-config")) LiveConfigChecks();
            Console.WriteLine($"PASS: {passed} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            string resolved = Path.GetFullPath(root);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("ApoSwitch-Checks-"))
                Directory.Delete(resolved, true);
        }
    }
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("OK: " + name); passed++;
    }
    private static void ReleaseChecks(string directory)
    {
        Assert(File.Exists(Path.Combine(directory, "ApoSwitch.exe")), "正式程序存在");
        Assert(!Directory.EnumerateFiles(directory, "ApoSwitch.*", SearchOption.TopDirectoryOnly)
            .Any(p => Path.GetFileName(p).Contains("Debug") || Path.GetFileName(p).Contains("Simulator")),
            "正式目录没有调试或模拟器文件");
        string dependencies = File.ReadAllText(Path.Combine(directory, "ApoSwitch.deps.json"));
        Assert(!dependencies.Contains("ApoSwitch.DeviceDebug") && !dependencies.Contains("ApoSwitch.Debug")
            && !dependencies.Contains("ApoSwitch.DeviceSimulator"), "正式依赖清单无调试模块");
        foreach (var assembly in new[] { "ApoSwitch.dll", "ApoSwitch.Core.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(directory, assembly));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            Assert(metadata.AssemblyReferences.All(h => !metadata.GetString(metadata.GetAssemblyReference(h).Name)
                .Contains("Debug")), assembly + " 不引用调试程序集");
            Assert(metadata.TypeReferences.All(h => metadata.GetString(metadata.GetTypeReference(h).Name)
                != "NamedPipeServerStream"), assembly + " 无命名管道服务端类型依赖");
        }
    }
    private static void ConfigChecks()
    {
        string nested = Path.Combine(root, "Nested.txt");
        File.WriteAllText(nested, "Include: Deeper.txt\r\nFilter: anything");
        File.WriteAllText(Path.Combine(root, "New Door Open.txt"), "Include: Nested.txt");
        File.WriteAllText(Path.Combine(root, "New Door Closed.txt"), "Include: New Door Open.txt");
        File.WriteAllText(Path.Combine(root, "GSR_BK_filters.txt"), "Convolution: deliberately-unchecked.wav");
        File.WriteAllText(Path.Combine(root, "not-in-root.txt"), "must never be discovered");
        string path = Path.Combine(root, "config.txt");
        string original = "# 中文说明\r\nInclude: New Door Open.txt\n  #\tInclude: New Door Closed.txt\r\n# GSonic 校准\r\n# Include: GSR_BK_filters.txt\r\nPreamp: -1 dB";
        var encodings = new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) };
        foreach (var encoding in encodings)
        {
            File.WriteAllText(path, original, encoding);
            var store = new ConfigStore(path);
            var doc = store.Read();
            Assert(doc.Presets.Count == 3 && doc.Presets.All(p => !p.Name.Contains("Nested") && !p.Name.Contains("not-in-root")), "只识别根文件一级 Include：" + encoding.WebName);
            byte[] before = File.ReadAllBytes(path);
            Assert(store.Activate(doc.Presets[2].Id), "真实原子文件替换");
            Assert(File.ReadAllBytes(store.BackupPath).SequenceEqual(before), "上一版备份正确");
            var switched = store.Read();
            Assert(switched.Presets.Count(p => p.IsEnabled) == 1 && switched.Presets[2].IsEnabled, "配置互斥切换");
            Assert(File.ReadAllText(path, encoding) == original.Replace("Include: New Door Open.txt", "# Include: New Door Open.txt").Replace("# Include: GSR_BK_filters.txt", "Include: GSR_BK_filters.txt"), "注释、混合换行和无末尾换行保留");
            Assert(!store.Activate(doc.Presets[2].Id), "当前配置不重复写入");
        }
        Assert(File.ReadAllText(nested) == "Include: Deeper.txt\r\nFilter: anything", "嵌套文件内容未修改");
        File.WriteAllText(path, "Include: New Door Open.txt\n# Include: Missing.txt");
        byte[] missingBefore = File.ReadAllBytes(path);
        try { new ConfigStore(path).Activate(Path.Combine(root, "Missing.txt")); throw new Exception("未拒绝缺失目标"); }
        catch (FileNotFoundException) { }
        Assert(File.ReadAllBytes(path).SequenceEqual(missingBefore), "缺失目标不会改坏主配置");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { new ConfigStore(path).Activate(Path.Combine(root, "New Door Open.txt")); throw new Exception("未报告文件占用"); }
            catch (IOException) { passed++; Console.WriteLine("OK: 文件占用明确失败"); }
        }
        File.WriteAllText(Path.Combine(root, "Added.txt"), "Include: Nested.txt");
        File.WriteAllText(path, "Include: New Door Open.txt\nInclude: New Door Closed.txt\n# Include: GSR_BK_filters.txt\n# Include: Added.txt");
        var expanded = new ConfigStore(path);
        Assert(expanded.Read().Presets.Count == 4, "外部新增一级配置可识别");
        Assert(!UserSettings.Initial(expanded.Read().Presets).Contains(Path.Combine(root, "Added.txt")), "新增配置默认不加入循环");
        expanded.Activate(Path.Combine(root, "Added.txt"));
        Assert(expanded.Read().Presets.Count(p => p.IsEnabled) == 1 && expanded.Read().Presets[3].IsEnabled,
            "从外部多启用状态切换后仅启用所选配置");
        File.WriteAllText(path, "# Include: New Door Open.txt\n# Include: New Door Closed.txt\n# Include: Added.txt");
        Assert(expanded.Read().Presets.All(p => !p.IsEnabled), "正确读取全部未启用状态");
        expanded.Activate(Path.Combine(root, "New Door Closed.txt"));
        Assert(expanded.Read().Presets.Count(p => p.IsEnabled) == 1 && expanded.Read().Presets[1].IsEnabled,
            "从全部未启用状态切换后仅启用所选配置");
        Assert(File.ReadAllText(nested) == "Include: Deeper.txt\r\nFilter: anything", "新增配置切换不修改嵌套文件");
    }
    private static void CycleChecks()
    {
        var a = new Preset("A", "New Door Open", "A", true);
        var b = new Preset("B", "New Door Closed", "B", false);
        var c = new Preset("C", "GSR_BK_filters", "C", false);
        var settings = UserSettings.Initial([a, b, c]);
        Assert(settings.Contains("A") && !settings.Contains("B") && settings.Contains("C"), "初始循环为红圈两套");
        Assert(settings.Next([a, b, c]) == "C", "按文件顺序循环");
        Assert(settings.Next([a with { IsEnabled = false }, b with { IsEnabled = true }, c]) == "A", "当前不在循环时从首项开始");
        settings.CyclePresetIds.Clear();
        Assert(settings.Next([a, b, c]) is null, "允许空循环");
        settings.CyclePresetIds.Add("C");
        Assert(settings.Next([a, b, c]) == "C", "单成员循环");
        var store = new SettingsStore(Path.Combine(root, "settings", "settings.json"));
        store.Save(settings);
        Assert(store.Load([a, b, c]).CyclePresetIds.SequenceEqual(["C"]), "循环成员重启记忆");
    }
    private static async Task DebugChecks()
    {
        using var real = new TestDeviceSource();
        using var source = new DebugDeviceStateSource(real);
        using (var pipe = new NamedPipeClientStream(".", DeviceDebugProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await pipe.ConnectAsync(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            foreach (var state in Enum.GetValues<DeviceAvailability>())
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new DeviceDebugMessage(state.ToString(), "模拟设备")));
                var reply = JsonSerializer.Deserialize<DeviceDebugReply>((await reader.ReadLineAsync())!);
                Assert(reply?.Ok == true && source.Current.Availability == state && source.Current.IsSimulated
                    && source.Current.CanApply == (state == DeviceAvailability.Installed), "管道状态及切换门控：" + state);
            }
            await writer.WriteLineAsync("{\"State\":\"ActivatePreset\"}");
            Assert(JsonSerializer.Deserialize<DeviceDebugReply>((await reader.ReadLineAsync())!)?.Ok == false, "模拟协议拒绝配置操作");
            await writer.WriteLineAsync(JsonSerializer.Serialize(new DeviceDebugMessage("Real")));
            await reader.ReadLineAsync();
            Assert(!source.Current.IsSimulated && source.Current.Name == "真实设备", "恢复真实检测");
            await writer.WriteLineAsync(JsonSerializer.Serialize(new DeviceDebugMessage("Installed")));
            await reader.ReadLineAsync();
        }
        for (int i = 0; i < 30 && source.Current.IsSimulated; i++) await Task.Delay(30);
        Assert(!source.Current.IsSimulated, "模拟器断开清除覆盖");
    }
    private static void MenuChecks()
    {
        var config = ConfigDocument.Load(Path.Combine(root, "config.txt"));
        var settings = UserSettings.Initial(config.Presets);
        foreach (var state in Enum.GetValues<DeviceAvailability>())
        {
            var menu = TrayMenuBuilder.Build(new(state), config, null, settings, false, false, _ => { }, _ => false, () => { }, () => { });
            Assert(menu[1].Text == "-", "标题独立分组：" + state);
            Assert(menu.Where(e => config.Presets.Any(p => p.Name == e.Text)).All(e => e.Enabled == (state == DeviceAvailability.Installed)),
                "一级应用菜单门控：" + state);
            var cycle = menu.Single(e => e.Text == "循环以下配置");
            Assert(cycle.Children!.All(e => e.ToggleChecked is not null), "循环条目使用常驻勾选：" + state);
            Assert(cycle.Enabled && cycle.Children!.All(e => e.Enabled) && menu.Single(e => e.Text == "开机自启动").Enabled,
                "循环与自启动始终可用：" + state);
        }
    }
    private static void LiveConfigChecks()
    {
        string path = WindowsSettings.ConfigPath();
        byte[] original = File.ReadAllBytes(path);
        var store = new ConfigStore(path);
        bool backupExisted = File.Exists(store.BackupPath);
        byte[]? oldBackup = backupExisted ? File.ReadAllBytes(store.BackupPath) : null;
        var doc = store.Read();
        var nestedBefore = doc.Presets.ToDictionary(p => p.Id, p => File.ReadAllBytes(p.Id));
        try
        {
            foreach (var preset in doc.Presets)
            {
                store.Activate(preset.Id);
                var after = store.Read();
                Assert(after.Presets.Count(p => p.IsEnabled) == 1 && after.Presets.Single(p => p.IsEnabled).Id == preset.Id,
                    "实际 APO 配置切换：" + preset.Name);
            }
            Assert(nestedBefore.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "实际一级配置内容未改变");
        }
        finally
        {
            string restore = path + ".restore-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(restore, original);
            File.Replace(restore, path, null);
            if (backupExisted) File.WriteAllBytes(store.BackupPath, oldBackup!);
            else File.Delete(store.BackupPath);
        }
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "实际主配置恢复到测试前字节内容");
    }
    private static void StartupChecks()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        object? previous = key.GetValue("ApoSwitch", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var previousKind = previous is null ? RegistryValueKind.String : key.GetValueKind("ApoSwitch");
        try
        {
            WindowsSettings.AutoStartEnabled = true;
            string command = (string)key.GetValue("ApoSwitch")!;
            Assert(WindowsSettings.AutoStartEnabled && command.StartsWith('"') && command.EndsWith('"')
                && !command.Contains("--device-debug"), "自启动命令加引号且不包含调试参数");
            WindowsSettings.AutoStartEnabled = false;
            Assert(!WindowsSettings.AutoStartEnabled && key.GetValue("ApoSwitch") is null, "关闭自启动移除当前用户启动项");
        }
        finally
        {
            if (previous is null) key.DeleteValue("ApoSwitch", false);
            else key.SetValue("ApoSwitch", previous, previousKind);
        }
        Console.WriteLine("原自启动设置已恢复。");
    }
    private sealed class TestDeviceSource : IDeviceStateSource
    {
        public DeviceState Current => new(DeviceAvailability.NotInstalled, "真实设备");
        public event EventHandler? Changed { add { } remove { } }
        public void Refresh() { }
        public void Dispose() { }
    }
}
