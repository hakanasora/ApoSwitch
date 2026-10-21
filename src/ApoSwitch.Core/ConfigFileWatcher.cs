namespace ApoSwitch.Core;

public sealed class ConfigFileWatcher(Action changed) : IDisposable
{
    private FileSystemWatcher? watcher;
    public string? FilePath { get; private set; }
    public void Watch(string? path)
    {
        if (watcher is not null && StringComparer.OrdinalIgnoreCase.Equals(FilePath, path)) return;
        watcher?.Dispose();
        watcher = null;
        FilePath = path;
        if (path is null || !Directory.Exists(Path.GetDirectoryName(path))) return;
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
                { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Error += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Polling still refreshes the root file; retry the watcher on the next refresh.
            watcher?.Dispose();
            watcher = null;
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs e) => changed();
    public void Dispose() { watcher?.Dispose(); watcher = null; }
}
