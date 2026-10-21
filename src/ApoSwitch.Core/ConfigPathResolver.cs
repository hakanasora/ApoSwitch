namespace ApoSwitch.Core;

public sealed record ApoInstallationPaths(string? ConfigPath, string? InstallPath);

public static class ConfigPathResolver
{
    // The caller supplies registry views in priority order. File existence is checked when reading,
    // never used to replace an explicit APO path with an unrelated default configuration.
    public static string ResolveAutomatic(IReadOnlyList<ApoInstallationPaths> installations, string programFiles)
    {
        string? directory = installations.Select(p => p.ConfigPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (directory is not null) return Path.Combine(Absolute(directory), "config.txt");
        directory = installations.Select(p => p.InstallPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (directory is not null) return Path.Combine(Absolute(directory), "config", "config.txt");
        return Path.Combine(programFiles, "EqualizerAPO", "config", "config.txt");
    }

    private static string Absolute(string value)
    {
        string path = Environment.ExpandEnvironmentVariables(value.Trim());
        if (!Path.IsPathFullyQualified(path)) throw new IOException("APO 注册表中的配置路径不是绝对路径：" + path);
        return Path.GetFullPath(path);
    }
}
