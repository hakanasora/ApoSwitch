using System.Reflection;
using ApoSwitch.Core;

internal static partial class Checks
{
    private static void CoreReliabilityChecks()
    {
        List<Exception> failures = [];
        foreach (var check in new Action[]
        {
            SettingsNormalizationChecks,
            SettingsRecoveryChecks,
            SettingsAtomicSaveChecks,
            CachedConfigurationChecks,
            WatcherRecoveryChecks,
            ConfigurationEdgeChecks
        })
        {
            try { check(); }
            catch (Exception ex) { failures.Add(new Exception(check.Method.Name + ": " + ex.Message, ex)); }
        }
        if (failures.Count > 0) throw new AggregateException("Core reliability regressions", failures);
    }

    private static void SettingsNormalizationChecks()
    {
        string directory = Path.Combine(root, "settings-normalization");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        File.WriteAllText(path, "{\"CyclePresetIds\":null,\"ConfigFilePath\":\"  \"}");
        var empty = store.LoadSaved()!;
        Assert(empty.CyclePresetIds is { Count: 0 } && empty.ConfigFilePath is null,
            "空循环集合与空白路径安全规范化");
        Assert(!empty.Contains("A") && empty.Copy().Next([]) is null, "空设置可查询、复制及循环");

        File.WriteAllText(path, "{\"CyclePresetIds\":[null,\"\",\"  \",\"A\",\"a\",\"B\"]}");
        var normalized = store.LoadSaved()!;
        Assert(normalized.CyclePresetIds.SequenceEqual(["A", "B"]), "忽略空成员并按路径大小写去重");
        Assert(normalized.ConfigFilePath is null, "旧设置缺失路径字段仍表示自动定位");
    }

    private static void SettingsRecoveryChecks()
    {
        string directory = Path.Combine(root, "settings-recovery");
        Directory.CreateDirectory(directory);
        string automaticPath = Path.Combine(directory, "automatic.txt");
        string selectedPath = Path.Combine(directory, "selected.txt");
        File.WriteAllText(automaticPath, "Include: Auto.txt\n");
        File.WriteAllText(selectedPath, "Include: Selected.txt\n");
        string path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);

        foreach (string invalid in new[] { "{broken", "null", "{\"ConfigFilePath\":\"relative/config.txt\"}" })
        {
            File.WriteAllText(path, invalid);
            byte[] original = File.ReadAllBytes(path);
            int automaticReads = 0;
            List<string> errors = [];
            var workspace = new ConfigWorkspace(store, () => { automaticReads++; return automaticPath; }, errors.Add);
            workspace.Refresh();
            workspace.Refresh();
            Assert(File.ReadAllBytes(path).SequenceEqual(original) && automaticReads == 0,
                "坏设置不猜测自动目录或被默认值覆盖：" + invalid);
            Assert(workspace.Document is null && workspace.Error is not null,
                "坏设置以明确错误阻止不确定的配置应用：" + invalid);
            Assert(errors.Count == 1, "相同设置错误不会每次刷新重复通知：" + invalid);

            store.Save(new UserSettings { ConfigFilePath = selectedPath, CyclePresetIds = ["remembered"] });
            workspace.Refresh();
            Assert(workspace.Document?.FilePath == selectedPath && workspace.Error is null
                && workspace.Settings!.Contains("remembered") && automaticReads == 0,
                "修复设置后重新读取手选路径与循环：" + invalid);
        }

        store.Save(new UserSettings { ConfigFilePath = selectedPath, CyclePresetIds = ["remembered"] });
        byte[] beforeLock = File.ReadAllBytes(path);
        int fallbackReads = 0;
        ConfigWorkspace locked;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            locked = new ConfigWorkspace(store, () => { fallbackReads++; return automaticPath; });
            locked.Refresh();
            Assert(locked.Document is null && locked.Error is not null && fallbackReads == 0,
                "设置暂时不可读时不回退到另一主文件");
        }
        Assert(File.ReadAllBytes(path).SequenceEqual(beforeLock), "不可读设置保持原字节");
        locked.Refresh();
        Assert(locked.Document?.FilePath == selectedPath && locked.Settings!.Contains("remembered"),
            "解除设置占用后可恢复读取");

        File.Delete(path);
        var firstRun = new ConfigWorkspace(store, () => automaticPath);
        firstRun.Refresh();
        Assert(firstRun.Document?.FilePath == automaticPath && store.LoadSaved() is not null,
            "不存在的设置仍按首次启动创建");
    }

    private static void SettingsAtomicSaveChecks()
    {
        string directory = Path.Combine(root, "settings-atomic");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        string occupiedTemporary = path + ".tmp";
        File.WriteAllText(occupiedTemporary, "unrelated temporary content");
        var store = new SettingsStore(path);
        store.Save(new UserSettings { CyclePresetIds = ["original"] });
        Assert(File.Exists(occupiedTemporary) && File.ReadAllText(occupiedTemporary) == "unrelated temporary content",
            "设置保存使用唯一临时文件，不覆盖或删除其他写入者的临时文件");

        byte[] before = File.ReadAllBytes(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { store.Save(new UserSettings { CyclePresetIds = ["replacement"] }); throw new Exception("未报告写入占用"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "设置替换失败保留原文件");
        Assert(Directory.GetFiles(directory, "*.tmp").SequenceEqual([occupiedTemporary]), "设置替换失败清理自己的临时文件");

        Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            store.Save(new UserSettings { CyclePresetIds = ["writer-" + index] })))).GetAwaiter().GetResult();
        Assert(store.LoadSaved()?.CyclePresetIds is [var member] && member.StartsWith("writer-"),
            "并发保存得到完整有效的一份设置");
        Assert(Directory.GetFiles(directory, "*.tmp").SequenceEqual([occupiedTemporary]), "并发保存无遗留临时文件");
    }

    private static void CachedConfigurationChecks()
    {
        string directory = Path.Combine(root, "configuration-cache");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.txt");
        const string firstText = "Include: A.txt\n";
        const string secondText = "Include: B.txt\n";
        File.WriteAllText(path, firstText);
        var store = new ConfigStore(path);
        var first = store.Read();
        Assert(ReferenceEquals(first, store.Read()), "未变化的配置复用解析结果");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, secondText);
        File.SetLastWriteTimeUtc(path, timestamp);
        var second = store.Read();
        Assert(second.Presets.Single().Name == "B" && !ReferenceEquals(first, second),
            "缓存不会漏掉长度和时间戳均相同的外部修改");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { store.Read(); throw new Exception("缓存掩盖了主文件读取失败"); }
            catch (IOException) { }
        }
        Assert(store.Read().Presets.Single().Name == "B", "缓存不掩盖读取错误，解除占用后恢复");
        File.Delete(path);
        try { store.Read(); throw new Exception("缓存掩盖了主文件删除"); }
        catch (FileNotFoundException) { }
        File.WriteAllText(path, firstText);
        Assert(store.Read().Presets.Single().Name == "A", "主文件删除并重建后刷新缓存");

        var workspace = new ConfigWorkspace(new SettingsStore(Path.Combine(directory, "settings.json")), () => path);
        workspace.Refresh();
        var document = workspace.Document;
        var configurationStore = workspace.Store;
        workspace.Refresh();
        Assert(ReferenceEquals(configurationStore, workspace.Store) && ReferenceEquals(document, workspace.Document),
            "同路径工作区刷新复用读取器与已解析文档");
    }

    private static void WatcherRecoveryChecks()
    {
        string directory = Path.Combine(root, "watcher-recovery");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.txt");
        File.WriteAllText(path, "# initial\n");
        using var signal = new ManualResetEventSlim();
        using var watcher = new ConfigFileWatcher(signal.Set);
        watcher.Watch(path);

        // Inject the native watcher's fatal-error event after stopping it, then check real file notifications.
        var nativeWatcher = (FileSystemWatcher)typeof(ConfigFileWatcher)
            .GetField("watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(watcher)!;
        nativeWatcher.EnableRaisingEvents = false;
        typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(nativeWatcher, [new ErrorEventArgs(new IOException("simulated watcher failure"))]);
        signal.Reset();
        watcher.Watch(path);
        File.AppendAllText(path, "# after recovery\n");
        Assert(signal.Wait(TimeSpan.FromSeconds(3)), "监听错误后同路径重新建立监听并接收真实保存事件");

        watcher.Dispose();
        watcher.Dispose();
        try { watcher.Watch(path); throw new Exception("Dispose 后重新启用了文件监听"); }
        catch (ObjectDisposedException) { Assert(true, "监听重复释放安全，释放后拒绝重开"); }
    }
}
