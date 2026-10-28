using System.Text;
using ApoSwitch.Core;

internal static partial class Checks
{
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
}
