using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using var mutex = new Mutex(true, "CenterWindow_SingleInstance", out bool createdNew);
if (!createdNew)
{
    return;
}

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

var hotkeyWindow = new HotkeyWindow();
hotkeyWindow.CreateHandle(new CreateParams());

const int HOTKEY_ID = 1;
const uint MOD_WIN = 0x0008;
const uint MOD_ALT = 0x0001;
const uint VK_C = 0x43;

if (!Native.RegisterHotKey(hotkeyWindow.Handle, HOTKEY_ID, MOD_WIN | MOD_ALT, VK_C))
{
    MessageBox.Show("Failed to register Win+Alt+C hotkey.\nAnother application may already be using it.",
                    "Center Window",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
    return;
}

var trayIcon = new NotifyIcon
{
    Icon = LoadIcon(),
    Text = "Center Window — Win+Alt+C",
    Visible = true,
    ContextMenuStrip = new ContextMenuStrip()
};

var autoStartItem = new ToolStripMenuItem(AutoStart.IsEnabled ? "Disable Run At Startup" : "Enable Run At Startup");
autoStartItem.Click += (_, _) =>
{
    AutoStart.Toggle();
    autoStartItem.Text = AutoStart.IsEnabled ? "Disable Run At Startup" : "Enable Run At Startup";
};

trayIcon.ContextMenuStrip.Items.Add("Center Window", null, (_, _) => CenterForegroundWindow());
trayIcon.ContextMenuStrip.Items.Add("-");
trayIcon.ContextMenuStrip.Items.Add(autoStartItem);
trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.Exit());

Application.ApplicationExit += (_, _) =>
{
    Native.UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID);
    trayIcon.Visible = false;
    trayIcon.Dispose();
    hotkeyWindow.DestroyHandle();
};

hotkeyWindow.HotkeyPressed += CenterForegroundWindow;

Application.Run();

// ---------------------------------------------------------------------------

static Icon LoadIcon()
{
    try
    {
        var stream = typeof(HotkeyWindow).Assembly.GetManifestResourceStream("CenterWindow.CenterWindow.ico")!;
        return new Icon(stream);
    }
    catch
    {
        return SystemIcons.Application;
    }
}

// ---------------------------------------------------------------------------

static void CenterForegroundWindow()
{
    nint hwnd = Native.GetForegroundWindow();
    if (hwnd == 0) return;

    var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
    if (!Native.GetWindowPlacement(hwnd, ref placement)) return;

    const int SW_SHOWNORMAL = 1;
    if (placement.showCmd != SW_SHOWNORMAL) return;

    if (!Native.GetWindowRect(hwnd, out var windowRect)) return;

    int windowWidth = windowRect.Right - windowRect.Left;
    int windowHeight = windowRect.Bottom - windowRect.Top;

    const uint MONITOR_DEFAULTTONEAREST = 2;
    nint monitor = Native.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    if (monitor == 0) return;

    var monitorInfo = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
    if (!Native.GetMonitorInfo(monitor, ref monitorInfo)) return;

    var work = monitorInfo.rcWork;
    int workWidth = work.Right - work.Left;
    int workHeight = work.Bottom - work.Top;

    int newX = work.Left + (workWidth - windowWidth) / 2;
    int newY = work.Top + (workHeight - windowHeight) / 2;

    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;

    Native.SetWindowPos(hwnd, 0, newX, newY, 0, 0, SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);
}

// ---------------------------------------------------------------------------

class HotkeyWindow : NativeWindow
{
    private const int WM_HOTKEY = 0x0312;

#nullable enable
    public event Action? HotkeyPressed;
#nullable restore

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
    }
}

// ---------------------------------------------------------------------------

static class AutoStart
{
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "CenterWindow";

    static string ExePath => Environment.ProcessPath!;

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is string value
                && string.Equals(value, ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Toggle()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)!;
        if (IsEnabled)
            key.DeleteValue(AppName, false);
        else
            key.SetValue(AppName, ExePath);
    }
}

// ---------------------------------------------------------------------------

static class Native
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
