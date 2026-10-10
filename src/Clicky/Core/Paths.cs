namespace Clicky.Core;

public static class Paths
{
    // DoNotVerify: GetFolderPath otherwise returns "" when the folder doesn't exist yet,
    // which would silently turn every path below into a relative one.

    /// <summary>%APPDATA% on Windows, $XDG_CONFIG_HOME (~/.config) on Linux.</summary>
    public static string ConfigHome { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>%LOCALAPPDATA% on Windows, $XDG_DATA_HOME (~/.local/share) on Linux.</summary>
    public static string DataHome { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    public static string ConfigDir => Path.Combine(ConfigHome, "clicky");
    public static string DataDir => Path.Combine(DataHome, "clicky");
}
