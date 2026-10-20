namespace Clicky;

/// <summary>Everything OS-specific: the global hotkey, clicking, and autostart.</summary>
public interface IPlatform : IDisposable
{
    /// <summary>Registers the global shortcut. <paramref name="onPressed"/> may run on any thread.</summary>
    /// <returns>False (with the reason logged) if registration failed.</returns>
    Task<bool> RegisterHotkeyAsync(Hotkey hotkey, Action onPressed);

    /// <summary>Prepares clicking ahead of time; on KDE this may show a one-time permission prompt.</summary>
    Task InitializeClickAsync();

    /// <summary>Moves the pointer to <paramref name="target"/> and left-clicks. Errors are logged, not thrown.</summary>
    Task ClickAsync(Target target, int monitorCount);

    /// <summary>Lets clicks pass through <paramref name="window"/> to whatever is underneath, if the platform can.</summary>
    /// <returns>True if the window may stay open (and focused) while clicking; false means close it first.</returns>
    bool MakeClickThrough(Avalonia.Controls.Window window);

    void InstallAutostart(string executable);
    void UninstallAutostart();
}

public static class Platform
{
    public static IPlatform Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatform();
        if (OperatingSystem.IsLinux())
            return new KdePlatform();
        throw new PlatformNotSupportedException();
    }
}
