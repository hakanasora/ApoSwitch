using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

namespace ApoSwitch;

public sealed record MenuEntry(string Text, bool Enabled = true, bool Checked = false,
    Action? Action = null, IReadOnlyList<MenuEntry>? Children = null, Func<bool>? ToggleChecked = null)
{
    public static MenuEntry Separator => new("-");
}

/// <summary>Real HMENU / TrackPopupMenuEx; Windows draws all menu rows and submenus.</summary>
internal sealed class NativeMenu : IDisposable
{
    private readonly Dictionary<uint, Action> actions = [];
    private sealed record ToggleItem(nint Menu, uint Position, Func<bool> Toggle);
    private readonly List<ToggleItem> toggles = [];
    private uint nextId = 100;
    private readonly nint handle;
    public NativeMenu(IReadOnlyList<MenuEntry> entries) => handle = Build(entries);
    private nint Build(IReadOnlyList<MenuEntry> entries)
    {
        nint menu = CreatePopupMenu();
        if (menu == 0) throw new Win32Exception();
        try
        {
            for (int position = 0; position < entries.Count; position++)
            {
                var entry = entries[position];
                uint flags = (entry.Enabled ? 0u : 0x3u) | (entry.Checked ? 0x8u : 0u);
                nuint id;
                if (entry.Text == "-") { flags = 0x800; id = 0; }
                else if (entry.Children is not null) { flags |= 0x10; id = (nuint)Build(entry.Children); }
                else
                {
                    id = nextId++;
                    if (entry.Action is not null && entry.Enabled) actions[(uint)id] = entry.Action;
                    if (entry.ToggleChecked is not null && entry.Enabled)
                    {
                        var toggle = new ToggleItem(menu, (uint)position, entry.ToggleChecked);
                        toggles.Add(toggle);
                        actions[(uint)id] = () => toggle.Toggle();
                    }
                }
                if (!AppendMenu(menu, flags, id, entry.Text.Replace("&", "&&"))) throw new Win32Exception();
            }
            return menu;
        }
        catch { DestroyMenu(menu); throw; }
    }
    public void Show(nint owner, Point location, bool? darkTheme = null)
    {
        NativeTheme.Apply(owner, darkTheme);
        SetForegroundWindow(owner);
        uint chosen;
        Exception? callbackError = null;
        HookProc filter = (code, wParam, lParam) =>
        {
            // Thread-local, and only active inside this menu's native tracking loop.
            if (code == 2) // MSGF_MENU
            {
                try
                {
                    var message = Marshal.PtrToStructure<MenuMessage>(lParam);
                    bool mouseUp = message.Message is 0x202 or 0x205;
                    bool keyDown = message.Message == 0x100 && message.WParam is 0x20 or 0x0D;
                    bool character = message.Message == 0x102 && message.WParam is 0x20 or 0x0D;
                    if (mouseUp || keyDown || character)
                    {
                        var item = toggles.FirstOrDefault(t => mouseUp
                            ? MenuItemFromPoint(0, t.Menu, message.Point) == (int)t.Position
                            : (GetMenuState(t.Menu, t.Position, 0x400) & 0x80) != 0
                              && GetMenuItemRect(0, t.Menu, t.Position, out _));
                        if (item is not null)
                        {
                            if (!character && (!keyDown || (message.LParam.ToInt64() & 0x40000000) == 0))
                            {
                                bool check = item.Toggle();
                                var info = new MenuItemInfo
                                {
                                    Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 1, // MIIM_STATE
                                    State = (GetMenuState(item.Menu, item.Position, 0x400) & ~8u) | (check ? 8u : 0)
                                };
                                if (!SetMenuItemInfo(item.Menu, item.Position, true, ref info)) throw new Win32Exception();
                                if (GetMenuItemRect(0, item.Menu, item.Position, out var bounds))
                                {
                                    var popup = WindowFromPoint(new NativePoint((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2));
                                    InvalidateRect(popup, 0, false);
                                    UpdateWindow(popup);
                                }
                            }
                            return 1; // Consume selection without ending the native menu or closing its submenu.
                        }
                    }
                }
                catch (Exception ex) { callbackError = ex; EndMenu(); return 1; }
            }
            return CallNextHookEx(0, code, wParam, lParam);
        };
        nint hook = 0;
        try
        {
            if (toggles.Count > 0)
            {
                hook = SetWindowsHookEx(-1, filter, 0, GetCurrentThreadId()); // WH_MSGFILTER
                if (hook == 0) throw new Win32Exception();
            }
            chosen = TrackPopupMenuEx(handle, 0x100 | 0x2, location.X, location.Y, owner, 0); // RETURNCMD | RIGHTBUTTON
        }
        finally
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
            GC.KeepAlive(filter);
        }
        PostMessage(owner, 0, 0, 0);
        if (callbackError is not null) ExceptionDispatchInfo.Capture(callbackError).Throw();
        if (actions.TryGetValue(chosen, out var action)) action();
    }
    public void Dispose() => DestroyMenu(handle);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint HookProc(int code, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuMessage
    {
        public nint Window; public uint Message; public nuint WParam; public nint LParam;
        public uint Time; public NativePoint Point; public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint SubMenu, CheckedBitmap, UncheckedBitmap;
        public nuint Data; public nint Text; public uint TextLength; public nint Bitmap;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int MenuItemFromPoint(nint window, nint menu, NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMenuItemRect(nint window, nint menu, uint item, out NativeRect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetMenuItemInfoW")]
    private static extern bool SetMenuItemInfo(nint menu, uint item, bool byPosition, ref MenuItemInfo info);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint bounds, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] private static extern bool EndMenu();
}

// Windows still exposes native popup-menu dark mode through UXTheme ordinals.
// Keep that compatibility surface here; no custom drawing or theme logic enters application behavior.
internal static class NativeTheme
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetPreferredAppMode(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void FlushMenuThemes();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool AllowDarkModeForWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool allow);
    private static readonly nint Library = LoadLibrary("uxtheme.dll");
    private static readonly SetPreferredAppMode? AppMode = Load<SetPreferredAppMode>(135);
    private static readonly AllowDarkModeForWindow? WindowMode = Load<AllowDarkModeForWindow>(133);
    private static readonly FlushMenuThemes? Flush = Load<FlushMenuThemes>(136);
    public static void Apply(nint window, bool? darkOverride = null)
    {
        bool dark = (darkOverride ?? WindowsSettings.AppDark) && !SystemInformation.HighContrast;
        AppMode?.Invoke(dark ? 2 : 3); // ForceDark / ForceLight; re-read every time the menu opens.
        WindowMode?.Invoke(window, dark);
        Flush?.Invoke();
    }
    private static T? Load<T>(int ordinal) where T : Delegate
    {
        nint pointer = Library == 0 ? 0 : GetProcAddress(Library, (nint)ordinal);
        return pointer == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string file);
    [DllImport("kernel32.dll")] private static extern nint GetProcAddress(nint module, nint ordinal);
}
