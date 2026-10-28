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
    public const int MaximumMessageLength = 4096;
    public static string PipeName { get; } = CreatePipeName();

    private static string CreatePipeName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        string user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User?.Value ?? Environment.UserName)))[..16];
        return $"ApoSwitch.DeviceState.{user}.{process.SessionId}";
    }

    public static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= MaximumMessageLength && !(line.Length == MaximumMessageLength && character[0] == '\r'))
                throw new InvalidDataException("设备状态消息过长。");
            line.Append(character[0]);
        }
        return line.Length == 0 ? null : line.ToString();
    }
}
