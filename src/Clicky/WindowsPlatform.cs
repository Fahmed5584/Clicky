using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Microsoft.Win32;

namespace Clicky;

/// <summary>
/// Windows: RegisterHotKey on a message-loop thread, SetCursorPos + SendInput to click,
/// and the per-user Run key for autostart (no admin rights needed).
/// </summary>
/// <remarks>
/// Avalonia makes the process per-monitor DPI aware, so screen bounds and cursor
/// coordinates are both physical pixels.
/// <para>
/// UIPI drops injected input while an elevated window is in the foreground, so the overlay
/// stays focused (but invisible and click-through) until the click has been sent.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IPlatform
{
    private const int HotkeyId = 1;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private uint _hotkeyThreadId;

    public Task<bool> RegisterHotkeyAsync(Hotkey hotkey, Action onPressed)
    {
        var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            // The hotkey belongs to this thread, so this thread must also run the message loop.
            _hotkeyThreadId = Native.GetCurrentThreadId();
            var (mods, vk) = hotkey.ToWin32();
            if (!Native.RegisterHotKey(IntPtr.Zero, HotkeyId, mods, vk))
            {
                Log.Error($"RegisterHotKey({hotkey}) failed (error {Marshal.GetLastWin32Error()}); " +
                          "another app probably owns this shortcut");
                registered.SetResult(false);
                return;
            }
            Log.Info($"registered shortcut {hotkey}");
            registered.SetResult(true);

            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.message == Native.WM_HOTKEY && msg.wParam == HotkeyId)
                    onPressed();
            Native.UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }) { IsBackground = true, Name = "clicky-hotkey" }.Start();
        return registered.Task;
    }

    public Task InitializeClickAsync() => Task.CompletedTask;

    public Task ClickAsync(Target target, int monitorCount)
    {
        var (x, y) = target.ToScreenPixel();
        if (!Native.SetCursorPos(x, y))
            Log.Error($"SetCursorPos failed (error {Marshal.GetLastWin32Error()})");
        SendLeftClick();
        return Task.CompletedTask;
    }

    /// <summary>Presses and releases the left button where the cursor is.</summary>
    private static void SendLeftClick()
    {
        Native.INPUT[] inputs =
        [
            new() { type = Native.INPUT_MOUSE, mi = { dwFlags = Native.MOUSEEVENTF_LEFTDOWN } },
            new() { type = Native.INPUT_MOUSE, mi = { dwFlags = Native.MOUSEEVENTF_LEFTUP } },
        ];
        if (Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>()) != inputs.Length)
            Log.Error($"SendInput failed (error {Marshal.GetLastWin32Error()}); foreground app: {ForegroundProcess()} " +
                      "(input to elevated apps is blocked unless clicky runs as admin)");
    }

    public bool MakeClickThrough(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return false;
        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE,
            new IntPtr(style | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT));
        return true;
    }

    private static string ForegroundProcess()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return $"pid {pid}";
        }
    }

    public void InstallAutostart(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue("clicky", $"\"{executable}\"");
    }

    public void UninstallAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue("clicky", throwOnMissingValue: false);
    }

    public void Dispose()
    {
        if (_hotkeyThreadId != 0)
            Native.PostThreadMessage(_hotkeyThreadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    internal static class Native
    {
        public const uint WM_HOTKEY = 0x0312;
        public const uint WM_QUIT = 0x0012;
        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_LAYERED = 0x00080000;
        public const long WS_EX_TRANSPARENT = 0x00000020;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
            public uint lPrivate;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // MOUSEINPUT is the largest member of INPUT's union, so INPUT = type + MOUSEINPUT.
        // Must be 40 bytes on x64 or SendInput silently fails (pinned by a test).
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // The *Ptr variants only exist as exports on 64-bit Windows, which is all we build for.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
