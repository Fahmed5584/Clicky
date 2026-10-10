using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clicky.Core;

namespace Clicky.Platform.Windows;

/// <summary>RegisterHotKey on a dedicated message-loop thread.</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32Hotkey(Hotkey hotkey) : IHotkeyService
{
    private const int HotkeyId = 1;
    private uint _threadId;
    private Thread? _thread;

    public event Action? Pressed;

    public Task<bool> StartAsync()
    {
        var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(registered)) { IsBackground = true, Name = "clicky-hotkey" };
        _thread.Start();
        return registered.Task;
    }

    private void Run(TaskCompletionSource<bool> registered)
    {
        _threadId = Win32.GetCurrentThreadId();
        var (mods, vk) = hotkey.ToWin32();
        if (!Win32.RegisterHotKey(IntPtr.Zero, HotkeyId, mods, vk))
        {
            Log.Error($"RegisterHotKey({hotkey}) failed (error {Marshal.GetLastWin32Error()}); " +
                      "another app probably owns this shortcut");
            registered.SetResult(false);
            return;
        }
        Log.Info($"registered shortcut {hotkey}");
        registered.SetResult(true);

        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == Win32.WM_HOTKEY && msg.wParam == HotkeyId)
                Pressed?.Invoke();
        }
        Win32.UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }

    public void Dispose()
    {
        if (_threadId != 0)
            Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
