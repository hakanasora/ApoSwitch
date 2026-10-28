using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ApoSwitch.Core;

namespace ApoSwitch.DeviceDebug;

/// <summary>Only constructed by the separate ApoSwitch.Debug host.</summary>
public sealed class DebugDeviceStateSource : IDeviceStateSource, IAsyncDisposable
{
    private readonly IDeviceStateSource real;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly Task listener;
    private DeviceState? simulated;
    private string? listenerError;
    private bool disposed;
    public event EventHandler? Changed;
    public DeviceState Current
    {
        get
        {
            lock (gate) return simulated ?? (listenerError is null ? real.Current
                : new(DeviceAvailability.Error, real.Current.Name, Detail: "调试设备通道不可用：" + listenerError));
        }
    }

    public DebugDeviceStateSource(IDeviceStateSource real)
    {
        this.real = real;
        real.Changed += RealChanged;
        listener = Task.Run(ListenAsync);
    }
    private void RealChanged(object? sender, EventArgs e)
    {
        lock (gate) { if (disposed) return; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Refresh()
    {
        lock (gate) { if (disposed) return; }
        real.Refresh();
    }
    private void SetOverride(DeviceState? value)
    {
        lock (gate)
        {
            if (disposed || simulated == value) return;
            simulated = value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void SetListenerError(string? value)
    {
        lock (gate)
        {
            if (disposed || listenerError == value) return;
            listenerError = value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ListenAsync()
    {
        try { await ListenConnectionsAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { SetListenerError(ex.Message); }
        finally { SetOverride(null); }
    }

    private async Task ListenConnectionsAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(DeviceDebugProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                SetListenerError(null);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                while (!stop.IsCancellationRequested && server.IsConnected)
                {
                    string? line = await DeviceDebugProtocol.ReadLineAsync(reader, stop.Token);
                    if (line is null) break;
                    DeviceDebugReply reply;
                    try
                    {
                        var message = JsonSerializer.Deserialize<DeviceDebugMessage>(line)
                            ?? throw new InvalidDataException("缺少设备状态。");
                        if (message.State == "Real") SetOverride(null);
                        else if (Enum.TryParse<DeviceAvailability>(message.State, false, out var state) && Enum.IsDefined(state))
                            SetOverride(new(state, message.DeviceName ?? "模拟播放设备", IsSimulated: true));
                        else throw new InvalidDataException("未知设备状态。");
                        reply = new(true, Current.StatusText);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException)
                    { reply = new(false, ex.Message); }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetListenerError(ex.Message);
                await Task.Delay(150, stop.Token);
            }
            finally { SetOverride(null); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            simulated = null;
        }
        stop.Cancel();
        real.Changed -= RealChanged;
        try { real.Dispose(); }
        finally
        {
            // Observe completion and release the token without blocking the UI thread on its listener.
            _ = listener.ContinueWith(task => { _ = task.Exception; stop.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await listener.ConfigureAwait(false);
    }
}
