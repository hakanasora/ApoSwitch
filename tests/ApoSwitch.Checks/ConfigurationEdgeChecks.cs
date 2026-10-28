using System.Text;
using ApoSwitch.Core;

internal static partial class Checks
{
    private static void ConfigurationEdgeChecks()
    {
        string directory = Path.Combine(root, "configuration-edges");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.txt");
        string target = Path.Combine(directory, "A.txt");
        File.WriteAllText(target, "Include: Nested.txt");

        File.WriteAllText(path, "");
        var empty = new ConfigStore(path).Read();
        Assert(empty.Presets.Count == 0, "空主配置不产生候选");
        try { empty.Activate(target); throw new Exception("空配置接受了不存在的候选"); }
        catch (IOException) { Assert(File.ReadAllBytes(path).Length == 0, "空配置选择失败不写入"); }

        const string duplicate = "# Include: A.txt\nInclude: a.txt\r\nPreamp: -1 dB\r# Include: A.txt";
        File.WriteAllText(path, duplicate);
        var store = new ConfigStore(path);
        Assert(store.Read().Presets is [var preset] && preset.IsEnabled && preset.Name == "A",
            "同一路径的大小写重复项按首次位置合并并识别任意启用行");
        store.Activate(target);
        Assert(File.ReadAllText(path) == "Include: A.txt\n# Include: a.txt\r\nPreamp: -1 dB\r# Include: A.txt",
            "重复Include只启用第一条，其他指令与混合换行保持原样");

        foreach (var encoding in new Encoding[] { new UTF32Encoding(false, true), new UTF32Encoding(true, true) })
        {
            File.WriteAllText(path, "# 中文\r\n# Include: A.txt\n", encoding);
            var encodedStore = new ConfigStore(path);
            encodedStore.Activate(target);
            byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes("# 中文\r\nInclude: A.txt\n")];
            Assert(File.ReadAllBytes(path).SequenceEqual(expected), "UTF32配置保留BOM、文本与换行：" + encoding.WebName);
        }

        byte[] comment = [0x23, 0x20, 0xD6, 0xD0, 0x0D, 0x0A];
        File.WriteAllBytes(path, [.. comment, .. Encoding.ASCII.GetBytes("# Include: A.txt\r\n")]);
        new ConfigStore(path).Activate(target);
        Assert(File.ReadAllBytes(path).SequenceEqual([.. comment, .. Encoding.ASCII.GetBytes("Include: A.txt\r\n")]),
            "ANSI回退不替换原字节内容");

        File.WriteAllText(path, "Preamp: Include: NotACandidate.txt\nInclude: \n# other comment\n");
        Assert(new ConfigStore(path).Read().Presets.Count == 0, "空Include与其他指令中的Include文本不扩展候选");
        File.WriteAllText(path, "Include: invalid\0path.txt\n");
        byte[] invalid = File.ReadAllBytes(path);
        try { new ConfigStore(path).Read(); throw new Exception("未拒绝非法文件路径"); }
        catch (ArgumentException) { Assert(File.ReadAllBytes(path).SequenceEqual(invalid), "非法路径报错且不修改原文件"); }
    }
}
