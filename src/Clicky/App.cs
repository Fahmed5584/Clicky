using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Clicky.Core;
using Clicky.Overlay;
using Clicky.Platform;

namespace Clicky;

public sealed class App : Application
{
    private IHotkeyService? _hotkey;
    private IPointerService? _pointer;
    private SingleInstanceServer? _server;

    public override void Initialize() => Styles.Add(new SimpleTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var config = Program.Config;
        _pointer = PlatformServices.CreatePointer();
        var overlay = new OverlayController(config, _pointer);
        void Show() => Dispatcher.UIThread.Post(overlay.Show);

        _server = new SingleInstanceServer(command =>
        {
            if (command == "show")
                Show();
        });
        _server.Start();

        _hotkey = PlatformServices.CreateHotkey(Hotkey.Parse(config.Hotkey));
        _hotkey.Pressed += Show;
        _ = StartAsync(_hotkey, _pointer);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit += (_, _) => Shutdown();

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartAsync(IHotkeyService hotkey, IPointerService pointer)
    {
        if (!await hotkey.StartAsync())
            Log.Warn("global shortcut unavailable; bind `clicky show` to a key in your desktop's shortcut settings");
        await pointer.InitializeAsync();
        Log.Info("clicky running");
    }

    private void Shutdown()
    {
        _hotkey?.Dispose();
        _server?.Dispose();
        _pointer?.Dispose();
    }
}
