using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ApoSwitch.Core;
using ApoSwitch.DeviceDebug;

internal static partial class Checks
{
    private static async Task DebugReliabilityChecks()
    {
        var failures = new List<Exception>();
        foreach (var check in new Func<Task>[] { DebugFramingChecks, InvalidDebugMessageChecks, OversizedDebugFrameChecks, DebugDisposalChecks })
        {
            try { await check(); }
            catch (Exception ex) { failures.Add(new Exception(check.Method.Name + ": " + ex.Message, ex)); }
        }
        if (failures.Count != 0) throw new AggregateException("Debug reliability regressions", failures);
    }

    private static async Task DebugFramingChecks()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        foreach (string ending in new[] { "\n", "\r\n", "" })
        {
            using var reader = new StringReader(new string('x', 4096) + ending);
            Assert((await DeviceDebugProtocol.ReadLineAsync(reader, timeout.Token))?.Length == 4096,
                "消息上限不包含行结束符：" + (ending.Length == 0 ? "EOF" : ending.Length.ToString()));
        }
        using var oversized = new StringReader(new string('x', 4097));
        try { await DeviceDebugProtocol.ReadLineAsync(oversized, timeout.Token); throw new Exception("未拒绝超长消息"); }
        catch (InvalidDataException) { Assert(true, "协议读取在分配完整超长行前拒绝消息"); }
    }

    private static async Task InvalidDebugMessageChecks()
    {
        await using var source = new DebugDeviceStateSource(new TestDeviceSource());
        using var connection = await DebugConnection.ConnectAsync();
        var installed = await connection.SendAsync("{\"State\":\"Installed\",\"DeviceName\":null}");
        Assert(installed.Ok && source.Current.IsSimulated && source.Current.Name == "模拟播放设备",
            "缺省模拟设备名称不传播 null 到设备状态");
        foreach (string invalid in new[] { "{broken", "null", "[]", "{\"State\":null}", "{\"State\":\"ActivatePreset\"}" })
        {
            var reply = await connection.SendAsync(invalid);
            Assert(!reply.Ok && source.Current.IsSimulated && source.Current.Availability == DeviceAvailability.Installed,
                "非法设备消息明确拒绝并保留当前状态：" + invalid);
        }
        var real = await connection.SendAsync("{\"State\":\"Real\"}");
        Assert(real.Ok && !source.Current.IsSimulated, "非法消息后仍可恢复真实检测");
    }

    private static async Task OversizedDebugFrameChecks()
    {
        await using var source = new DebugDeviceStateSource(new TestDeviceSource());
        using var connection = await DebugConnection.ConnectAsync();
        await connection.SendAsync("{\"State\":\"Installed\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await connection.Writer.WriteAsync(new string('x', 4097).AsMemory(), timeout.Token);
        await connection.Writer.FlushAsync(timeout.Token);
        bool closed;
        try { closed = await connection.Reader.ReadLineAsync(timeout.Token) is null; }
        catch (IOException) { closed = true; }
        Assert(closed, "超长且无换行消息在有界时间内断开，不无限缓存或等待");
        for (int i = 0; i < 30 && source.Current.IsSimulated; i++) await Task.Delay(10);
        Assert(!source.Current.IsSimulated, "超长消息断开后清除模拟状态");
        connection.Dispose();
        using var reconnected = await DebugConnection.ConnectAsync();
        Assert((await reconnected.SendAsync("{\"State\":\"NoDevice\"}")).Ok,
            "拒绝超长消息后允许重新连接");
    }

    private static async Task DebugDisposalChecks()
    {
        var real = new TrackingDeviceSource();
        var source = new DebugDeviceStateSource(real);
        using var connection = await DebugConnection.ConnectAsync();
        await connection.SendAsync("{\"State\":\"Installed\"}");
        source.Dispose();
        source.Dispose();
        source.Refresh();
        Assert(real.DisposeCount == 1 && real.RefreshCount == 0, "调试设备源重复释放和释放后刷新均安全");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        bool closed;
        try { closed = await connection.Reader.ReadLineAsync(timeout.Token) is null; }
        catch (IOException) { closed = true; }
        Assert(closed && !source.Current.IsSimulated, "关闭调试源取消正在等待的读取并清除模拟状态");
        await source.DisposeAsync();
    }

    private sealed class TrackingDeviceSource : IDeviceStateSource
    {
        public int DisposeCount { get; private set; }
        public int RefreshCount { get; private set; }
        public DeviceState Current => new(DeviceAvailability.NotInstalled, "真实设备");
        public event EventHandler? Changed { add { } remove { } }
        public void Refresh() => RefreshCount++;
        public void Dispose() => DisposeCount++;
    }

    private sealed class DebugConnection : IDisposable
    {
        private readonly NamedPipeClientStream pipe = new(".", DeviceDebugProtocol.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        public StreamReader Reader { get; private set; } = null!;
        public StreamWriter Writer { get; private set; } = null!;

        public static async Task<DebugConnection> ConnectAsync()
        {
            var connection = new DebugConnection();
            try
            {
                await connection.pipe.ConnectAsync(5000);
                connection.Reader = new StreamReader(connection.pipe, Encoding.UTF8, false, 1024, true);
                connection.Writer = new StreamWriter(connection.pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                return connection;
            }
            catch { connection.Dispose(); throw; }
        }

        public async Task<DeviceDebugReply> SendAsync(string message)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Writer.WriteLineAsync(message.AsMemory(), timeout.Token);
            return JsonSerializer.Deserialize<DeviceDebugReply>(await Reader.ReadLineAsync(timeout.Token)
                ?? throw new IOException("调试连接已断开。")) ?? throw new IOException("调试回复为空。");
        }

        public void Dispose()
        {
            try { Writer?.Dispose(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            Reader?.Dispose();
            pipe.Dispose();
        }
    }
}
