using ApoSwitch;
using ApoSwitch.Core;
using Microsoft.Win32;

internal static partial class Checks
{
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
}
