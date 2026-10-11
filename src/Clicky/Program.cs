using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Simple;
using Avalonia.Threading;

namespace Clicky;

public static class Program
{
    private const string Usage = """
        clicky: press a hotkey, type a cell label, get a click there.

        Usage:
          clicky             run the background daemon
          clicky show        open the grid in the running daemon
          clicky install     start clicky automatically when you log in
          clicky uninstall   remove the autostart entry
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        switch (args)
        {
            case []:
                return RunDaemon();
            case ["show"]:
                if (Ipc.TrySend(Ipc.Show))
                    return 0;
                Log.Error("clicky daemon is not running");
                return 1;
            case ["install"]:
                using (var platform = Platform.Create())
                    platform.InstallAutostart(Environment.ProcessPath!);
                Console.WriteLine("autostart installed");
                return 0;
            case ["uninstall"]:
                using (var platform = Platform.Create())
                    platform.UninstallAutostart();
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

    private static int RunDaemon()
    {
        if (Ipc.TrySend(Ipc.Ping))
        {
            Log.Error("clicky is already running");
            return 1;
        }
        try
        {
            App.Config = Config.Load();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException
                                      or System.Text.Json.JsonException)
        {
            Log.Error($"bad config {Paths.ConfigFile}: {e.Message}");
            return 1;
        }
        try
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception e)
        {
            // Typically no display to connect to (e.g. started outside a desktop session).
            Log.Error($"cannot start UI: {e.Message}");
            return 1;
        }
    }
}

/// <summary>The daemon: wires the hotkey and `clicky show` to the overlay.</summary>
public sealed class App : Application
{
    /// <summary>Set by <see cref="Program"/> before Avalonia constructs the app.</summary>
    internal static Config Config { get; set; } = new();

    private readonly IPlatform _platform = Platform.Create();
    private Ipc.Server? _server;

    public override void Initialize() => Styles.Add(new SimpleTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var overlay = new Overlay(Config, _platform);
        void Show() => Dispatcher.UIThread.Post(overlay.Show);

        _server = new Ipc.Server(command =>
        {
            if (command == Ipc.Show)
                Show();
        });
        _ = StartAsync(Show);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit += (_, _) =>
            {
                _server.Dispose();
                _platform.Dispose();
            };
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(Action show)
    {
        if (!await _platform.RegisterHotkeyAsync(Hotkey.Parse(Config.Hotkey), show))
            Log.Warn("global shortcut unavailable; bind `clicky show` to a key in your desktop's shortcut settings");
        await _platform.InitializeClickAsync();
        Log.Info("clicky running");
    }
}
