using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ApoSwitch.DeviceDebug;

namespace ApoSwitch.DeviceSimulator;

internal sealed class SimulatorForm : Form
{
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(520, 0), Text = "尚未连接" };
    private readonly TextBox deviceName = new() { Text = "模拟播放设备", Width = 300 };
    private readonly List<Button> commands = [];
    private NamedPipeClientStream? pipe;
    private StreamReader? reader;
    private StreamWriter? writer;
    private bool busy;
    private readonly CancellationTokenSource closing = new();
    private readonly Icon applicationIcon;

    public SimulatorForm()
    {
        using (var stream = typeof(SimulatorForm).Assembly.GetManifestResourceStream("ApoSwitch.ApplicationIcon")!)
            applicationIcon = new Icon(stream, SystemInformation.IconSize);
        Icon = applicationIcon;
        Text = "APO 设备状态模拟器";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9);
        ClientSize = new Size(550, 510);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(18), AutoScroll = true };
        Controls.Add(layout);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(510, 0),
            Text = "只模拟设备状态。托盘中的配置切换会真实修改 Equalizer APO 配置。\n正常托盘若已运行，请先从托盘菜单退出，再启动调试模式。",
            Margin = new Padding(0, 0, 0, 14) });
        var connectionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        var launch = Button("启动调试托盘", () => { Launch(); return Task.CompletedTask; });
        var connect = Button("连接", ConnectAsync);
        var disconnect = Button("断开 / 恢复真实检测", () => { Disconnect(); status.Text = "已断开，托盘恢复真实设备检测。"; return Task.CompletedTask; });
        connectionRow.Controls.AddRange([launch, connect, disconnect]);
        layout.Controls.Add(connectionRow);
        layout.Controls.Add(new Label { AutoSize = true, Text = "模拟设备名称：" });
        layout.Controls.Add(deviceName);
        foreach (var (label, state) in new[] { ("已安装 APO", "Installed"), ("未安装 APO", "NotInstalled"),
                     ("没有播放设备", "NoDevice"), ("检测失败", "Error"), ("恢复真实设备检测", "Real") })
        {
            var button = Button(label, () => SendAsync(state));
            button.Width = 300;
            layout.Controls.Add(button);
        }
        status.Margin = new Padding(0, 16, 0, 0);
        layout.Controls.Add(status);
        FormClosed += (_, _) => { closing.Cancel(); Disconnect(); };
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(70, 30) };
        button.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            commands.ForEach(b => b.Enabled = false);
            try { await action(); }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
            {
                Disconnect();
                if (!IsDisposed) status.Text = ex is TimeoutException
                    ? "未连接到调试托盘。请先退出正常实例，再点击“启动调试托盘”。" : "连接中断：" + ex.Message;
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            finally { busy = false; if (!IsDisposed) commands.ForEach(b => b.Enabled = true); }
        };
        commands.Add(button);
        return button;
    }

    private void Launch()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "ApoSwitch.Debug.exe");
        if (!File.Exists(executable)) { status.Text = "请把模拟器与 ApoSwitch.Debug.exe 放在同一文件夹。"; return; }
        Process.Start(new ProcessStartInfo(executable)
            { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory });
        status.Text = "已请求启动调试托盘；托盘出现后点击“连接”。";
    }
    private async Task ConnectAsync()
    {
        Disconnect();
        pipe = new NamedPipeClientStream(".", DeviceDebugProtocol.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(1200, closing.Token);
        reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        status.Text = "已连接。点击状态按钮即可模拟；尚未覆盖真实检测。";
    }
    private async Task SendAsync(string state)
    {
        if (pipe?.IsConnected != true) await ConnectAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        timeout.CancelAfter(3000);
        await writer!.WriteLineAsync(JsonSerializer.Serialize(new DeviceDebugMessage(state, deviceName.Text)).AsMemory(), timeout.Token);
        string response = await reader!.ReadLineAsync(timeout.Token) ?? throw new IOException("托盘程序已断开。");
        var reply = JsonSerializer.Deserialize<DeviceDebugReply>(response) ?? throw new IOException("托盘响应无效。");
        status.Text = (reply.Ok ? "已更新：" : "未更新：") + reply.Message;
    }
    private void Disconnect()
    {
        foreach (var item in new IDisposable?[] { pipe, writer, reader })
        {
            try { item?.Dispose(); }
            catch (IOException) { /* The tray may have exited before the controller. */ }
            catch (ObjectDisposedException) { }
        }
        writer = null; reader = null; pipe = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) applicationIcon.Dispose();
    }
}
