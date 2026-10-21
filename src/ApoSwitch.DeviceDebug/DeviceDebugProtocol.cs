using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ApoSwitch.Core;

namespace ApoSwitch.DeviceDebug;

// The protocol intentionally contains no configuration or application commands.
public sealed record DeviceDebugMessage(string State, string DeviceName = "模拟播放设备");
public sealed record DeviceDebugReply(bool Ok, string Message);

public static class DeviceDebugProtocol
{
    public static string PipeName
    {
        get
        {
            var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            string user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
            return $"ApoSwitch.DeviceState.{user}.{Process.GetCurrentProcess().SessionId}";
        }
    }
}
