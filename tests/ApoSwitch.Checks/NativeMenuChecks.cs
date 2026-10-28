using System.Diagnostics;
using System.Runtime.InteropServices;
using ApoSwitch;

internal static partial class Checks
{
    private static void NativeMenuLifecycleChecks()
    {
        IReadOnlyList<MenuEntry> entries = [new("标题", false), MenuEntry.Separator,
            new("子菜单", Children: [new("循环项", ToggleChecked: () => true)]), new("退出", Action: () => { })];
        using var process = Process.GetCurrentProcess();
        using (var warmup = new NativeMenu(entries)) { }
        uint before = GetGuiResources(process.Handle, 1);
        for (int i = 0; i < 300; i++)
        {
            using var menu = new NativeMenu(entries);
            menu.Dispose();
            menu.Dispose();
        }
        uint after = GetGuiResources(process.Handle, 1);
        Assert(after <= before + 2, "反复创建与释放原生子菜单不累积USER对象");
        using var disposed = new NativeMenu(entries);
        disposed.Dispose();
        try { disposed.Show(0, Point.Empty); throw new Exception("已释放菜单仍可显示"); }
        catch (ObjectDisposedException) { Assert(true, "原生菜单重复释放安全，释放后拒绝显示"); }
    }

    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}
