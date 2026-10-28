namespace ApoSwitch.Core;

public sealed class ConfigFileWatcher(Action changed) : IDisposable
{
    private volatile FileSystemWatcher? watcher;
    private volatile bool disposed;
    private int restartRequested;
    public string? FilePath { get; private set; }
    public void Watch(string? path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bool restart = Interlocked.Exchange(ref restartRequested, 0) != 0;
        if (!restart && watcher is not null && StringComparer.OrdinalIgnoreCase.Equals(FilePath, path)) return;
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
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Polling still refreshes the root file; retry the watcher on the next refresh.
            watcher?.Dispose();
            watcher = null;
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!disposed && ReferenceEquals(sender, watcher)) changed();
    }
    private void OnError(object sender, ErrorEventArgs e)
    {
        if (disposed || !ReferenceEquals(sender, watcher)) return;
        Interlocked.Exchange(ref restartRequested, 1);
        changed();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        watcher?.Dispose();
        watcher = null;
    }
}
