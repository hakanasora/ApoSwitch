using Microsoft.Win32;

namespace ApoSwitch;

public static class WindowsSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ApoSwitch";
    public static string ConfigPath()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"SOFTWARE\EqualizerAPO");
            if (key?.GetValue("ConfigPath") is string directory && directory.Length > 0)
                return Path.Combine(Environment.ExpandEnvironmentVariables(directory), "config.txt");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "config", "config.txt");
    }
    public static bool AutoStartEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) is string; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            // Both hosts always start the production executable at sign-in.
            if (value) key.SetValue(ValueName, '"' + Path.Combine(AppContext.BaseDirectory, "ApoSwitch.exe") + '"', RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
        }
    }
    public static bool AppDark => ReadTheme("AppsUseLightTheme") == 0;
    public static bool TaskbarDark => ReadTheme("SystemUsesLightTheme") == 0;
    private static int ReadTheme(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(name) is int value ? value : 1;
    }
}
