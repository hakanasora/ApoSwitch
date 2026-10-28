using System.Diagnostics;
using System.Reflection;
using ApoSwitch.DeviceDebug;

internal static partial class Checks
{
    private static void SimulatorLifecycleChecks()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        foreach (bool closeDuringConnect in new[] { false, true })
        {
            var source = new DebugDeviceStateSource(new TestDeviceSource());
            try
            {
                var type = Assembly.Load("ApoSwitch.DeviceSimulator").GetType("ApoSwitch.DeviceSimulator.SimulatorForm", true)!;
                using var form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                Exception? failure = null;
                using var watchdog = new System.Windows.Forms.Timer { Interval = 10000 };
                watchdog.Tick += (_, _) => { failure ??= new TimeoutException("模拟器生命周期检查超时。"); form.Dispose(); };
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var buttons = ControlsWithin(form).OfType<Button>().ToArray();
                        buttons.Single(button => button.Text == "连接").PerformClick();
                        if (!closeDuringConnect)
                        {
                            await WaitForAsync(() => buttons.All(button => button.Enabled), TimeSpan.FromSeconds(5));
                            buttons.Single(button => button.Text == "已安装 APO").PerformClick();
                            await WaitForAsync(() => source.Current.IsSimulated, TimeSpan.FromSeconds(5));
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { form.Dispose(); }
                };
                watchdog.Start();
                Application.Run(form);
                watchdog.Stop();
                if (failure is not null) throw failure;
                var elapsed = Stopwatch.StartNew();
                while (source.Current.IsSimulated && elapsed.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(10);
                Assert(!source.Current.IsSimulated, closeDuringConnect
                    ? "模拟器连接过程中直接释放可取消连接，无残留模拟状态"
                    : "模拟器直接Dispose立即断开并恢复真实设备状态");
                form.Dispose();
            }
            finally { source.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
    }

    private static IEnumerable<Control> ControlsWithin(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in ControlsWithin(child)) yield return descendant;
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate())
        {
            if (elapsed.Elapsed >= timeout) throw new TimeoutException("等待模拟器状态超时。");
            await Task.Delay(20);
        }
    }
}
