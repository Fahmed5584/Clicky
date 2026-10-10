using Clicky.Core;
using Clicky.Platform.Linux;
using Clicky.Platform.Windows;

namespace Clicky.Platform;

public interface IHotkeyService : IDisposable
{
    /// <summary>Raised on a background thread when the shortcut fires.</summary>
    event Action? Pressed;

    /// <summary>Registers the shortcut; false (with a logged reason) if that failed.</summary>
    Task<bool> StartAsync();
}

/// <param name="Order">Monitor position in <see cref="GridLayout"/> order.</param>
/// <param name="ScreenCount">Number of monitors the layout was built for.</param>
/// <param name="Fx">Horizontal position as a fraction of the monitor width.</param>
/// <param name="Fy">Vertical position as a fraction of the monitor height.</param>
public sealed record ClickTarget(int Order, int ScreenCount, ScreenBounds Bounds, double Fx, double Fy);

public interface IPointerService : IDisposable
{
    /// <summary>Prepares the backend (may show a one-time permission prompt).</summary>
    Task InitializeAsync();

    Task ClickAsync(ClickTarget target);
}

public interface IAutostart
{
    void Install(string executable);
    void Uninstall();
}

public static class PlatformServices
{
    public static IHotkeyService CreateHotkey(Hotkey hotkey)
    {
        if (OperatingSystem.IsWindows()) return new Win32Hotkey(hotkey);
        if (OperatingSystem.IsLinux()) return new KGlobalAccelHotkey(hotkey);
        throw new PlatformNotSupportedException();
    }

    public static IPointerService CreatePointer()
    {
        if (OperatingSystem.IsWindows()) return new Win32Pointer();
        if (OperatingSystem.IsLinux()) return new PortalPointer();
        throw new PlatformNotSupportedException();
    }

    public static IAutostart CreateAutostart()
    {
        if (OperatingSystem.IsWindows()) return new RunKeyAutostart();
        if (OperatingSystem.IsLinux()) return new XdgAutostart();
        throw new PlatformNotSupportedException();
    }
}
