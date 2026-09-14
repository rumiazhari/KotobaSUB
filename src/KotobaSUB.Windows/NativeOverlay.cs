using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace KotobaSUB.Windows;

internal sealed class NativeOverlay : IDisposable
{
    internal const int Transparent = 0x20, ToolWindow = 0x80, Layered = 0x80000, NoActivate = 0x8000000;
    private readonly nint hwnd;
    private readonly HwndSource source;
    private readonly List<int> hotkeys = new();
    private bool locked = true;
    public event Action<int>? Hotkey;
    public NativeOverlay(Window window)
    {
        hwnd = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd)!;
        source.AddHook(Hook);
        SetLocked(true);
    }
    public int Styles => GetWindowLong(hwnd, -20);
    public void SetLocked(bool value)
    {
        locked = value;
        int styles = Styles | ToolWindow | NoActivate;
        styles = value ? styles | Transparent : styles & ~Transparent;
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLong(hwnd, -20, styles) == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception();
        if (!SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20)) throw new Win32Exception();
    }
    public bool Register(int id, uint key)
    {
        if (!RegisterHotKey(hwnd, id, 0x1 | 0x2 | 0x4000, key)) return false; // Ctrl+Alt, no repeat
        hotkeys.Add(id);
        return true;
    }
    private nint Hook(nint h, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x312) { Hotkey?.Invoke((int)w); handled = true; }
        if (msg == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
        if (msg == 0x84 && locked) { handled = true; return -1; } // HTTRANSPARENT
        return 0;
    }
    public void Dispose()
    {
        foreach (int id in hotkeys) UnregisterHotKey(hwnd, id);
        source.RemoveHook(Hook);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(nint h, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint hwnd, int message, nint wparam, nint lparam);
}
