using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Clicky.Platform.Windows;

/// <summary>Autostart via HKCU\...\Run (per user, no admin rights).</summary>
[SupportedOSPlatform("windows")]
public sealed class RunKeyAutostart : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "clicky";

    public void Install(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{executable}\"");
    }

    public void Uninstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
