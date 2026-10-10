using Avalonia;
using Avalonia.Controls;
using Clicky.Core;
using Clicky.Platform;

namespace Clicky;

public static class Program
{
    private const string Usage = """
        clicky: press a hotkey, type a 2-character cell label, get a click there.

        Usage:
          clicky             run the background daemon
          clicky show        open the grid in the running daemon
          clicky install     start clicky automatically when you log in
          clicky uninstall   remove the autostart entry
        """;

    internal static Config Config { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        switch (args)
        {
            case []:
                return RunDaemon(args);
            case ["show"]:
                if (SingleInstance.TrySend("show"))
                    return 0;
                Log.Error("clicky daemon is not running");
                return 1;
            case ["install"]:
                PlatformServices.CreateAutostart().Install(Environment.ProcessPath!);
                Console.WriteLine("autostart installed");
                return 0;
            case ["uninstall"]:
                PlatformServices.CreateAutostart().Uninstall();
                Console.WriteLine("autostart removed");
                return 0;
            case ["-h" or "--help"]:
                Console.WriteLine(Usage);
                return 0;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static int RunDaemon(string[] args)
    {
        if (SingleInstance.TrySend("ping"))
        {
            Log.Error("clicky is already running");
            return 1;
        }
        try
        {
            Config = Config.Load();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException
                                      or System.Text.Json.JsonException)
        {
            Log.Error($"bad config {Config.DefaultPath}: {e.Message}");
            return 1;
        }
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception e)
        {
            // Typically no display to connect to (e.g. started outside a desktop session).
            Log.Error($"cannot start UI: {e.Message}");
            return 1;
        }
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
