using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ApoSwitch.Core;

namespace ApoSwitch.DeviceDebug;

/// <summary>Only constructed after an explicit --device-debug startup argument.</summary>
public sealed class DebugDeviceStateSource : IDeviceStateSource
{
    private readonly IDeviceStateSource real;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly Task listener;
    private DeviceState? simulated;
    public event EventHandler? Changed;
    public DeviceState Current { get { lock (gate) return simulated ?? real.Current; } }

    public DebugDeviceStateSource(IDeviceStateSource real)
    {
        this.real = real;
        real.Changed += RealChanged;
        listener = Task.Run(ListenAsync);
    }
    private void RealChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    public void Refresh() => real.Refresh();
    private void SetOverride(DeviceState? value)
    {
        lock (gate) simulated = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(DeviceDebugProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                while (!stop.IsCancellationRequested && server.IsConnected)
                {
                    string? line = await reader.ReadLineAsync(stop.Token);
                    if (line is null) break;
                    DeviceDebugReply reply;
                    try
                    {
                        if (line.Length > 4096) throw new InvalidDataException("设备状态消息过长。");
                        var message = JsonSerializer.Deserialize<DeviceDebugMessage>(line)
                            ?? throw new InvalidDataException("缺少设备状态。");
                        if (message.State == "Real") SetOverride(null);
                        else if (Enum.TryParse<DeviceAvailability>(message.State, false, out var state) && Enum.IsDefined(state))
                            SetOverride(new(state, message.DeviceName, IsSimulated: true));
                        else throw new InvalidDataException("未知设备状态。");
                        reply = new(true, Current.StatusText);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException)
                    { reply = new(false, ex.Message); }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (IOException) { await Task.Delay(150, stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
            finally { SetOverride(null); }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        real.Changed -= RealChanged;
        real.Dispose();
        // ReadLineAsync and WaitForConnectionAsync observe this token; no UI-thread blocking.
    }
}
