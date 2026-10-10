using Clicky.Core;

namespace Clicky.Platform.Linux;

/// <summary>Autostart via ~/.config/autostart/clicky.desktop.</summary>
public sealed class XdgAutostart : IAutostart
{
    private static string DesktopFile => Path.Combine(Paths.ConfigHome, "autostart", "clicky.desktop");

    public void Install(string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
        File.WriteAllText(DesktopFile, $"""
            [Desktop Entry]
            Type=Application
            Name=clicky
            Comment=Keyboard-driven grid clicker
            Exec="{executable}"
            Icon=input-mouse
            Terminal=false
            NoDisplay=true

            """);
    }

    public void Uninstall() => File.Delete(DesktopFile);
}
