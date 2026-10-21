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
        if (args.Length is 2 or 3 && args[0] == "--verify-release")
        {
            ReleaseChecks(args[1], args.Length == 3 ? args[2] : AppContext.BaseDirectory);
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
            PathChecks();
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
    private static void ReleaseChecks(string directory, string assemblyDirectory)
    {
        Assert(File.Exists(Path.Combine(directory, "ApoSwitch.exe")), "正式程序存在");
        Assert(Directory.GetFileSystemEntries(directory).Length == 1, "正式目录仅有一个 EXE，没有运行库、语言文件夹和说明图片");
        Assert(new FileInfo(Path.Combine(directory, "ApoSwitch.exe")).Length <= 2_000_000, "正式 EXE 不超过 2 MB");
        string dependencies = File.ReadAllText(Path.Combine(assemblyDirectory, "ApoSwitch.deps.json"));
        Assert(!dependencies.Contains("ApoSwitch.DeviceDebug") && !dependencies.Contains("ApoSwitch.Debug")
            && !dependencies.Contains("ApoSwitch.DeviceSimulator"), "正式依赖清单无调试模块");
        using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(assemblyDirectory, "ApoSwitch.runtimeconfig.json")));
        Assert(!runtime.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _)
            && runtime.RootElement.GetProperty("runtimeOptions").GetProperty("frameworks").EnumerateArray()
                .Any(f => f.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App"), "发布物依赖共享桌面运行时，不内置整套环境");
        foreach (var assembly in new[] { "ApoSwitch.dll", "ApoSwitch.Core.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(assemblyDirectory, assembly));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            Assert(metadata.AssemblyReferences.All(h => !metadata.GetString(metadata.GetAssemblyReference(h).Name)
                .Contains("Debug")), assembly + " 不引用调试程序集");
            Assert(metadata.TypeReferences.All(h => metadata.GetString(metadata.GetTypeReference(h).Name)
                != "NamedPipeServerStream"), assembly + " 无命名管道服务端类型依赖");
        }
    }
    private static void PathChecks()
    {
        string basePath = Path.Combine(root, "paths");
        string a = Path.Combine(basePath, "自定义 配置 A", "config.txt");
        string b = Path.Combine(basePath, "配置 B", "config.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, "Include: A.txt\n");
        File.WriteAllText(b, "# Include: B.txt\n");
        byte[] beforeA = File.ReadAllBytes(a), beforeB = File.ReadAllBytes(b);
        string programFiles = Path.Combine(basePath, "Program Files");
        Assert(ConfigPathResolver.ResolveAutomatic([new(Path.GetDirectoryName(a), null), new(Path.GetDirectoryName(b), null)], programFiles) == a,
            "注册表 64 位配置地址优先，支持自定义路径、空格和中文");
        Assert(ConfigPathResolver.ResolveAutomatic([new(null, "C:\\Unused"), new(Path.GetDirectoryName(b), null)], programFiles) == b,
            "32 位 ConfigPath 优先于安装目录猜测");
        Assert(ConfigPathResolver.ResolveAutomatic([new(null, basePath)], programFiles) == Path.Combine(basePath, "config", "config.txt"),
            "ConfigPath 缺失时使用 InstallPath");
        Assert(ConfigPathResolver.ResolveAutomatic([], programFiles) == Path.Combine(programFiles, "EqualizerAPO", "config", "config.txt"),
            "注册表无路径时尝试系统默认目录");
        try { ConfigPathResolver.ResolveAutomatic([new("relative-path", null)], programFiles); throw new Exception("未拒绝错误注册表地址"); }
        catch (IOException) { Assert(true, "明确错误的注册表地址不会静默回退"); }
        string prefs = Path.Combine(basePath, "settings.json");
        var store = new SettingsStore(prefs);
        string automatic = a;
        var workspace = new ConfigWorkspace(store, () => automatic);
        workspace.Refresh();
        Assert(workspace.Document?.FilePath == a, "自动载入主配置");
        workspace.SelectFile(b);
        Assert(workspace.Document?.FilePath == b && workspace.Settings?.ConfigFilePath == b, "手选更新主文件位置");
        Assert(workspace.Settings!.CyclePresetIds.Count == 1 && !workspace.Settings.Contains(Path.Combine(Path.GetDirectoryName(b)!, "B.txt")),
            "更换主文件保留原循环，新候选不自动加入");
        workspace.ToggleCycle(workspace.Document!.Presets[0].Id);
        Assert(store.LoadSaved()?.ConfigFilePath == b, "调整循环不丢失手选路径");
        var restarted = new ConfigWorkspace(store, () => throw new Exception("手选模式不应调用自动定位"));
        restarted.Refresh();
        Assert(restarted.Document?.FilePath == b, "先读已保存路径，手选重启无需访问注册表");
        byte[] saved = File.ReadAllBytes(prefs);
        try { workspace.SelectFile(Path.Combine(basePath, "missing", "config.txt")); throw new Exception("未拒绝不存在的手选文件"); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        Assert(workspace.Document?.FilePath == b && File.ReadAllBytes(prefs).SequenceEqual(saved), "无效手选不改变配置位置和设置");
        using (new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { workspace.SelectFile(a); throw new Exception("未拒绝不可读的手选文件"); }
            catch (IOException) { }
        }
        Assert(workspace.Document?.FilePath == b && File.ReadAllBytes(prefs).SequenceEqual(saved), "不可读手选保留原状态");
        File.Delete(b);
        workspace.Refresh();
        Assert(workspace.Document is null && workspace.Error is not null && workspace.Settings!.ConfigFilePath == b,
            "失效手选路径报错，不改读自动配置");
        File.WriteAllBytes(b, beforeB);
        workspace.RestoreAutomatic();
        Assert(workspace.Document?.FilePath == a && store.LoadSaved()?.ConfigFilePath is null, "恢复自动定位并持久保存");
        automatic = b;
        workspace.Refresh();
        Assert(workspace.Document?.FilePath == b, "自动模式刷新后跟随新配置目录");
        string missing = Path.Combine(basePath, "configured-but-missing");
        automatic = ConfigPathResolver.ResolveAutomatic([new(missing, Path.GetDirectoryName(a))], programFiles);
        workspace.Refresh();
        Assert(workspace.Document is null && workspace.Store?.FilePath == Path.Combine(missing, "config.txt"),
            "明确 ConfigPath 缺失时不回退到其他配置");
        var selected = new ConfigWorkspace(store, () => a);
        selected.Refresh();
        byte[] oldSettings = File.ReadAllBytes(prefs);
        using (new FileStream(prefs, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { selected.SelectFile(b); throw new Exception("未报告设置保存失败"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Assert(selected.Document?.FilePath == a && File.ReadAllBytes(prefs).SequenceEqual(oldSettings), "设置写入失败时选择不显示成功");
        File.WriteAllText(prefs, "{\"CyclePresetIds\":[\"legacy\"]}");
        var legacy = store.LoadSaved();
        Assert(legacy?.ConfigFilePath is null && legacy!.Contains("legacy"), "旧设置兼容自动定位且保留循环");
        Assert(File.ReadAllBytes(a).SequenceEqual(beforeA) && File.ReadAllBytes(b).SequenceEqual(beforeB),
            "选择、恢复与自动定位均不改写 APO 主文件");
        using var signal = new ManualResetEventSlim();
        using var watcher = new ConfigFileWatcher(signal.Set);
        watcher.Watch(a);
        watcher.Watch(b);
        File.AppendAllText(a, "# old root\n");
        Assert(!signal.Wait(200), "切换监听后旧配置不再触发通知");
        File.AppendAllText(b, "# new root\n");
        Assert(signal.Wait(3000), "新主配置的外部保存触发通知");
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
