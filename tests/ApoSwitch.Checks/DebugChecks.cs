using System.Text.Json;
using ApoSwitch.Core;
using ApoSwitch.DeviceDebug;

internal static partial class Checks
{
    private static async Task DebugChecks()
    {
        await using var source = new DebugDeviceStateSource(new TestDeviceSource());
        using (var connection = await DebugConnection.ConnectAsync())
        {
            foreach (var state in Enum.GetValues<DeviceAvailability>())
            {
                var reply = await connection.SendAsync(JsonSerializer.Serialize(new DeviceDebugMessage(state.ToString(), "模拟设备")));
                Assert(reply.Ok && source.Current.Availability == state && source.Current.IsSimulated
                    && source.Current.CanApply == (state == DeviceAvailability.Installed), "管道状态及切换门控：" + state);
            }
            Assert(!(await connection.SendAsync("{\"State\":\"ActivatePreset\"}")).Ok, "模拟协议拒绝配置操作");
            await connection.SendAsync(JsonSerializer.Serialize(new DeviceDebugMessage("Real")));
            Assert(!source.Current.IsSimulated && source.Current.Name == "真实设备", "恢复真实检测");
            await connection.SendAsync(JsonSerializer.Serialize(new DeviceDebugMessage("Installed")));
        }
        for (int i = 0; i < 30 && source.Current.IsSimulated; i++) await Task.Delay(30);
        Assert(!source.Current.IsSimulated, "模拟器断开清除覆盖");
    }
    private sealed class TestDeviceSource : IDeviceStateSource
    {
        public DeviceState Current => new(DeviceAvailability.NotInstalled, "真实设备");
        public event EventHandler? Changed { add { } remove { } }
        public void Refresh() { }
        public void Dispose() { }
    }
}
