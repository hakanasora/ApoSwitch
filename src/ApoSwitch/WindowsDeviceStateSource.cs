using System.Runtime.InteropServices;
using ApoSwitch.Core;
using Microsoft.Win32;

namespace ApoSwitch;

public sealed class WindowsDeviceStateSource : IDeviceStateSource
{
    private readonly Action<Action> dispatch;
    private IMMDeviceEnumerator? enumerator;
    private NotificationClient? notifications;
    private bool disposed;
    public DeviceState Current { get; private set; } = new(DeviceAvailability.Error);
    public event EventHandler? Changed;
    public WindowsDeviceStateSource(Action<Action> dispatch)
    {
        this.dispatch = dispatch;
        Refresh();
    }

    public void Refresh()
    {
        if (disposed) return;
        DeviceState state;
        try
        {
            if (enumerator is null)
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                notifications = new NotificationClient(() => dispatch(Refresh));
                Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(notifications));
            }
            int result = enumerator.GetDefaultAudioEndpoint(0, 1, out var device); // render, multimedia
            if (result == unchecked((int)0x80070490)) state = new(DeviceAvailability.NoDevice);
            else
            {
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    string name = ReadName(device);
                    state = new(HasEqualizerApo(id) ? DeviceAvailability.Installed : DeviceAvailability.NotInstalled, name, id);
                }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        catch (Exception ex) { state = new(DeviceAvailability.Error, Detail: ex.Message); }
        if (Current != state) { Current = state; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private static string ReadName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out var store) < 0) return "默认播放设备";
        try
        {
            var key = new PropertyKey { Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
            if (store.GetValue(ref key, out var value) < 0) return "默认播放设备";
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "默认播放设备" : "默认播放设备"; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static bool HasEqualizerApo(string endpointId)
    {
        int start = endpointId.LastIndexOf('{');
        if (start < 0 || !Guid.TryParse(endpointId[start..], out Guid id))
            throw new IOException("无法识别播放设备的端点 ID。");
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var effects = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\"
            + id.ToString("B") + @"\FxProperties");
        if (effects is null) return false;
        // Same five installation slots used by Equalizer APO 1.4.2 DeviceAPOInfo.
        foreach (int slot in new[] { 1, 2, 5, 6, 7 })
        {
            var raw = effects.GetValue($"{{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}},{slot}") as string;
            if (Guid.TryParse(raw, out var effect) && (effect == new Guid("EACD2258-FCAC-4FF4-B36D-419E924A6D79")
                || effect == new Guid("EC1CC9CE-FAED-4822-828A-82A81A6F018F"))) return true;
        }
        return false;
    }

    public void Dispose()
    {
        disposed = true;
        if (enumerator is null) return;
        if (notifications is not null) enumerator.UnregisterEndpointNotificationCallback(notifications);
        Marshal.ReleaseComObject(enumerator);
        enumerator = null;
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class NotificationClient(Action notify) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, uint state) { notify(); return 0; }
        public int OnDeviceAdded(string id) { notify(); return 0; }
        public int OnDeviceRemoved(string id) { notify(); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string? id) { if (flow == 0 && role == 1) notify(); return 0; }
        public int OnPropertyValueChanged(string id, PropertyKey key) { notify(); return 0; }
    }
}
