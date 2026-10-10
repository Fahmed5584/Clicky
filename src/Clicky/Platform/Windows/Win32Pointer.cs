using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clicky.Core;

namespace Clicky.Platform.Windows;

/// <summary>
/// SetCursorPos + SendInput. Avalonia makes the process per-monitor DPI aware,
/// so screen bounds and cursor coordinates are both physical pixels.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32Pointer : IPointerService
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task ClickAsync(ClickTarget target)
    {
        var b = target.Bounds;
        int x = b.X + Math.Clamp((int)Math.Round(target.Fx * b.Width), 0, b.Width - 1);
        int y = b.Y + Math.Clamp((int)Math.Round(target.Fy * b.Height), 0, b.Height - 1);

        if (!Win32.SetCursorPos(x, y))
            Log.Error($"SetCursorPos failed (error {Marshal.GetLastWin32Error()})");

        var inputs = new[]
        {
            new Win32.INPUT { type = Win32.INPUT_MOUSE, mi = { dwFlags = Win32.MOUSEEVENTF_LEFTDOWN } },
            new Win32.INPUT { type = Win32.INPUT_MOUSE, mi = { dwFlags = Win32.MOUSEEVENTF_LEFTUP } },
        };
        if (Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>()) != inputs.Length)
            Log.Error($"SendInput failed (error {Marshal.GetLastWin32Error()})");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
