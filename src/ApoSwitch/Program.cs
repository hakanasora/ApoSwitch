
namespace ApoSwitch;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var instance = new Mutex(true, @"Local\ApoSwitch.Tray", out bool created);
        if (!created)
        {
            MessageBox.Show("APO 切换器已经运行。", "APO 切换器",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            using var context = new TrayApplication();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：" + ex.Message, "APO 切换器", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { instance.ReleaseMutex(); }
    }
}
