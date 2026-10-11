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
