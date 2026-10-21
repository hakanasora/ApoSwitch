namespace ApoSwitch.Core;

public sealed class ConfigStore(string filePath)
{
    public string FilePath { get; } = Path.GetFullPath(filePath);
    public string BackupPath => FilePath + ".aposwitch.bak";
    public ConfigDocument Read() => ConfigDocument.Load(FilePath);

    public bool Activate(string presetId)
    {
        var document = Read();
        var preset = document.Presets.FirstOrDefault(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, presetId))
            ?? throw new IOException("该配置已从 config.txt 移除，请重新选择。");
        if (!File.Exists(preset.Id)) throw new FileNotFoundException("配置文件不存在：" + preset.IncludePath, preset.Id);
        byte[] replacement = document.Activate(preset.Id);
        if (replacement.AsSpan().SequenceEqual(document.OriginalBytes)) return false;
        var temporary = Path.Combine(Path.GetDirectoryName(FilePath)!, ".aposwitch-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                stream.Write(replacement);
                stream.Flush(true);
            }
            if (!File.ReadAllBytes(FilePath).AsSpan().SequenceEqual(document.OriginalBytes))
                throw new IOException("配置刚被其他程序修改，请重新选择。");
            File.Replace(temporary, FilePath, BackupPath);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
