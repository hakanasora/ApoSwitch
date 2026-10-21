using Microsoft.Win32;
using ApoSwitch.Core;

namespace ApoSwitch;

public static class WindowsSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ApoSwitch";
    public static string ConfigPath()
    {
        var installations = new List<ApoInstallationPaths>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                installations.Add(new(key?.GetValue("ConfigPath") as string, key?.GetValue("InstallPath") as string));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { /* Try the other registry view; manual selection remains available. */ }
        }
        return ConfigPathResolver.ResolveAutomatic(installations, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
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
