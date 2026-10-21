namespace ApoSwitch.Core;

public enum DeviceAvailability { Installed, NotInstalled, NoDevice, Error }

public sealed record DeviceState(DeviceAvailability Availability, string Name = "", string? EndpointId = null,
    string? Detail = null, bool IsSimulated = false)
{
    public bool CanApply => Availability == DeviceAvailability.Installed;
    public string StatusText => Availability switch
    {
        DeviceAvailability.Installed => "已安装 APO",
        DeviceAvailability.NotInstalled => "此设备未安装 APO 服务",
        DeviceAvailability.NoDevice => "未检测到播放设备",
        _ => "无法读取设备状态"
    };
    public string ToolTip => Availability == DeviceAvailability.NotInstalled ? "当前设备不适用" : StatusText;
}

public interface IDeviceStateSource : IDisposable
{
    DeviceState Current { get; }
    event EventHandler? Changed;
    void Refresh();
}
