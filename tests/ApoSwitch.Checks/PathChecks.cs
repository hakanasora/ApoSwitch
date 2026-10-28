using ApoSwitch.Core;

internal static partial class Checks
{
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
}
