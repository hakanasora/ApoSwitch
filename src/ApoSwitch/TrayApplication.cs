using ApoSwitch.Core;

namespace ApoSwitch;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly bool debug;
    private readonly Control dispatcher = new();
    private readonly OwnerWindow owner = new();
    private readonly NotifyIcon tray;
    private Icon normalIcon;
    private Icon warningIcon;
    private (int Number, bool Multiple, bool Unavailable, int Size, bool Dark)? iconAppearance;
    private readonly ConfigWorkspace configuration;
    private readonly IDeviceStateSource device;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer singleClick = new() { Interval = SystemInformation.DoubleClickTime };
    private readonly ConfigFileWatcher watcher;
    private ConfigStore? configStore => configuration.Store;
    private ConfigDocument? config => configuration.Document;
    private UserSettings? settings => configuration.Settings;
    private string? configError => configuration.Error;
    private bool disposed;
    private bool menuOpen;
    private bool suppressNextLeftMouseUp;

    public TrayApplication(Func<Action<Action>, IDeviceStateSource>? createDevice = null, bool debug = false)
    {
        this.debug = debug;
        _ = dispatcher.Handle;
        normalIcon = TrayIcons.Create(false);
        warningIcon = TrayIcons.Create(true);
        tray = new NotifyIcon { Icon = normalIcon, Text = "APO 切换器", Visible = true };
        singleClick.Tick += (_, _) => { singleClick.Stop(); ShowMenu(); };
        tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            singleClick.Stop();
            suppressNextLeftMouseUp = true;
            Cycle();
        };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { singleClick.Stop(); ShowMenu(); }
            else if (e.Button == MouseButtons.Left)
            {
                if (suppressNextLeftMouseUp) { suppressNextLeftMouseUp = false; return; }
                // Wait for the system double-click interval so a double click only cycles configurations.
                singleClick.Stop(); singleClick.Start();
            }
        };
        owner.SettingsChanged += (_, _) => Post(UpdateIcons);
        var settingsStore = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApoSwitch", "settings.json"));
        configuration = new ConfigWorkspace(settingsStore, WindowsSettings.ConfigPath, ShowError);
        watcher = new ConfigFileWatcher(() => Post(ScheduleReload));
        device = createDevice is null ? new WindowsDeviceStateSource(Post) : createDevice(Post);
        device.Changed += DeviceChanged;
        ReloadConfig();
        debounce.Tick += (_, _) => { debounce.Stop(); ReloadConfig(); };
        refreshTimer.Tick += (_, _) => { device.Refresh(); ReloadConfig(); };
        refreshTimer.Start();
        UpdatePresentation();
    }

    private void Post(Action action)
    {
        if (disposed || dispatcher.IsDisposed) return;
        try { dispatcher.BeginInvoke(() => { if (!disposed) action(); }); }
        catch (InvalidOperationException) { /* Shutdown raced an audio/file callback. */ }
    }
    private void DeviceChanged(object? sender, EventArgs e) => Post(UpdatePresentation);
    private void ScheduleReload() { debounce.Stop(); debounce.Start(); }

    private void ReloadConfig()
    {
        configuration.Refresh();
        watcher.Watch(configStore?.FilePath);
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        bool unavailable = device.Current.Availability == DeviceAvailability.NotInstalled;
        bool warning = configError is not null
            || device.Current.Availability is DeviceAvailability.NoDevice or DeviceAvailability.Error;
        int activeNumber = 0;
        int activeCount = 0;
        if (config is not null)
        {
            for (int i = 0; i < config.Presets.Count; i++)
            {
                if (!config.Presets[i].IsEnabled) continue;
                activeNumber = i + 1;
                activeCount++;
            }
        }
        // External edits may enable multiple presets; applying a preset through the tray still selects only that preset.
        var appearance = (Number: activeCount == 1 ? activeNumber : 0, Multiple: activeCount > 1, Unavailable: unavailable,
            Size: TrayIcons.PixelSize, Dark: WindowsSettings.TaskbarDark);
        if (iconAppearance != appearance)
        {
            var nextNormal = TrayIcons.Create(false, appearance.Number, appearance.Size, appearance.Dark,
                appearance.Multiple, appearance.Unavailable);
            var nextWarning = TrayIcons.Create(true, pixelSize: appearance.Size, taskbarDark: appearance.Dark);
            var oldNormal = normalIcon;
            var oldWarning = warningIcon;
            normalIcon = nextNormal;
            warningIcon = nextWarning;
            iconAppearance = appearance;
            tray.Icon = warning ? warningIcon : normalIcon;
            oldNormal.Dispose(); oldWarning.Dispose();
        }
        else tray.Icon = warning ? warningIcon : normalIcon;
        string text;
        if (!device.Current.CanApply) text = device.Current.ToolTip;
        else if (configError is not null) text = "无法读取 APO 配置";
        else
        {
            var names = config?.Presets.Where(p => p.IsEnabled).Select(p => p.Name).ToArray() ?? [];
            text = names.Length switch
            {
                0 => "未启用校准配置",
                1 => names[0],
                _ => $"同时启用 {names.Length} 套配置\n" + string.Join(" + ", names)
            };
        }
        if (debug) text += device.Current.IsSimulated ? "\n调试模式 · 模拟设备" : "\n调试模式 · 真实设备";
        if (!string.IsNullOrWhiteSpace(device.Current.Name)) text += "\n" + device.Current.Name;
        tray.Text = text.Length > 63 ? text[..62] + "…" : text;
    }

    private void UpdateIcons()
    {
        iconAppearance = null;
        UpdatePresentation();
    }

    private void ShowMenu()
    {
        if (menuOpen || disposed) return;
        menuOpen = true;
        try
        {
            device.Refresh();
            ReloadConfig();
            var entries = TrayMenuBuilder.Build(device.Current, config, configError, settings, debug,
                WindowsSettings.AutoStartEnabled, Apply, ToggleCycle, ToggleAutoStart, ExitThread,
                ChooseConfigFile, RestoreAutomaticConfig);
            using var menu = new NativeMenu(entries);
            menu.Show(owner.Handle, Cursor.Position);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { menuOpen = false; }
    }

    private void Cycle()
    {
        device.Refresh();
        ReloadConfig();
        if (!device.Current.CanApply || config is null || settings is null) return;
        string? next = settings.Next(config.Presets);
        if (next is not null) Apply(next);
    }
    private void Apply(string id)
    {
        device.Refresh();
        if (!device.Current.CanApply) return;
        try
        {
            ReloadConfig();
            if (configError is not null || configStore is null) throw new IOException(configError ?? "未找到主配置文件。");
            configStore.Activate(id);
            ReloadConfig();
        }
        catch (Exception ex) { ReloadConfig(); ShowError("切换失败：" + ex.Message); }
    }
    private bool ToggleCycle(string id)
    {
        if (settings is null) return false;
        try { return configuration.ToggleCycle(id); }
        catch (Exception ex) { ShowError("循环设置保存失败：" + ex.Message); }
        return settings.Contains(id);
    }
    private void ChooseConfigFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 Equalizer APO 主 config.txt",
            Filter = "APO 主配置 (config.txt)|config.txt",
            FileName = "config.txt", CheckFileExists = true, Multiselect = false, RestoreDirectory = true
        };
        string? directory = Path.GetDirectoryName(configStore?.FilePath);
        if (directory is not null && Directory.Exists(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            configuration.SelectFile(dialog.FileName);
            watcher.Watch(configStore?.FilePath);
            UpdatePresentation();
        }
        catch (Exception ex) { ShowError("配置文件选择失败：" + ex.Message); }
    }
    private void RestoreAutomaticConfig()
    {
        try { configuration.RestoreAutomatic(); ReloadConfig(); }
        catch (Exception ex) { ShowError("无法恢复自动定位：" + ex.Message); }
    }
    private void ToggleAutoStart()
    {
        try { WindowsSettings.AutoStartEnabled = !WindowsSettings.AutoStartEnabled; }
        catch (Exception ex) { ShowError("自启动设置失败：" + ex.Message); }
    }
    private void ShowError(string message)
    {
        tray.BalloonTipTitle = "APO 切换器";
        tray.BalloonTipText = message.Length > 230 ? message[..230] + "…" : message;
        tray.BalloonTipIcon = ToolTipIcon.Warning;
        tray.ShowBalloonTip(5000);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            refreshTimer.Stop(); debounce.Stop(); singleClick.Stop();
            refreshTimer.Dispose(); debounce.Dispose(); singleClick.Dispose();
            watcher.Dispose();
            device.Changed -= DeviceChanged;
            device.Dispose();
            tray.Visible = false; tray.Dispose();
            normalIcon.Dispose(); warningIcon.Dispose();
            owner.Dispose(); dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class OwnerWindow : NativeWindow, IDisposable
    {
        public event EventHandler? SettingsChanged;
        public OwnerWindow() => CreateHandle(new CreateParams { Caption = "APO Switch Menu Owner", Style = unchecked((int)0x80000000),
            ExStyle = 0x80, Width = 1, Height = 1, X = -32000, Y = -32000 });
        protected override void WndProc(ref Message m)
        {
            if (m.Msg is 0x1A or 0x31A) SettingsChanged?.Invoke(this, EventArgs.Empty);
            base.WndProc(ref m);
        }
        public void Dispose() => DestroyHandle();
    }
}
