using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace Clicky;

/// <summary>
/// Shows one <see cref="OverlayWindow"/> per monitor, collects the typed label from whichever
/// window has focus, and clicks the result. Must be used on the UI thread.
/// </summary>
public sealed class Overlay(Config config, IPlatform platform)
{
    private readonly List<OverlayWindow> _windows = [];
    private readonly Window _screenProbe = new(); // never shown; only used to enumerate screens
    private Grid? _grid;

    /// <summary>What has been typed so far: "", a row, a cell ("sk") or a partial sub-cell ("sk1").</summary>
    public string Typed { get; private set; } = "";

    public void Show()
    {
        Close();
        CreateConfigFile();
        var screens = _screenProbe.Screens.All;
        try
        {
            _grid = Grid.Build(ToBounds(screens), config.Alphabet, config.Columns, config.Rows, config.SubGridSize);
        }
        catch (ArgumentException e)
        {
            Log.Error(e.Message);
            return;
        }

        foreach (var monitor in _grid.Monitors)
        {
            var window = new OverlayWindow(this, screens[monitor.ScreenIndex],
                new GridView(_grid, monitor, () => Typed, config.FontSize));
            _windows.Add(window);
            window.Show();
        }
        (_windows.FirstOrDefault(w => w.IsPrimary) ?? _windows[0]).Activate();
    }

    /// <summary>
    /// Writes a missing config file sized to the current monitors. Tried at startup and again on
    /// each show, in case the screens weren't known yet.
    /// </summary>
    public void CreateConfigFile()
    {
        if (!config.IsNew)
            return;
        var screens = ToBounds(_screenProbe.Screens.All);
        if (screens.Count == 0)
            return;
        try
        {
            config.CreateFile(screens);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"cannot write {Paths.ConfigFile}: {e.Message}");
        }
    }

    private static List<ScreenBounds> ToBounds(IEnumerable<Screen> screens) =>
        screens.Select(s => new ScreenBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList();

    public void Close()
    {
        foreach (var w in EndSession())
            w.Close();
    }

    /// <summary>Resets the typed input and hands the open windows to the caller, who must close them.</summary>
    private List<OverlayWindow> EndSession()
    {
        var windows = _windows.ToList();
        _windows.Clear();
        _grid = null;
        Typed = "";
        return windows;
    }

    public void HandleKey(KeyEventArgs e)
    {
        if (_grid is null)
            return;
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Back && Typed.Length > 0)
        {
            Typed = Typed[..^1];
            Redraw();
        }
        else if (e.Key is Key.Enter or Key.Space && Typed.Length >= Grid.CellLabelLength)
        {
            Click(Typed[..Grid.CellLabelLength]); // skip the sub-grid: click the cell centre
        }
        else if (KeyChar(e.Key, e.KeySymbol) is char c && _grid.CanAppend(Typed, c))
        {
            Typed += c;
            if (Typed.Length == Grid.FullLabelLength)
                Click(Typed);
            else
                Redraw();
        }
    }

    private void Click(string label)
    {
        var target = _grid!.Resolve(label);
        int monitorCount = _grid.Monitors.Count;
        if (target is not { } t)
        {
            Close();
            return;
        }

        // Where the platform allows it, the windows stay open (blank and click-through) until the
        // click is sent, so clicky keeps the foreground; see WindowsPlatform. Otherwise close now.
        var windows = EndSession();
        bool keepOpen = windows.All(platform.MakeClickThrough);
        foreach (var w in windows)
        {
            if (keepOpen)
                w.Content = null;
            else
                w.Close();
        }
        _ = ClickAsync(t, monitorCount, keepOpen ? windows : []);
    }

    /// <summary>Clicks after a short delay, then closes the <paramref name="windows"/> that stayed open for it.</summary>
    private async Task ClickAsync(Target target, int monitorCount, List<OverlayWindow> windows)
    {
        await Task.Delay(config.ClickDelayMs); // let the overlay disappear (or go blank) first
        try
        {
            await platform.ClickAsync(target, monitorCount);
        }
        finally
        {
            foreach (var w in windows)
                w.Close();
        }
    }

    /// <summary>
    /// The lower-case character a key press types: the keyboard layout's symbol when there is a
    /// printable one, otherwise the letter or digit printed on the key. Null for any other key.
    /// </summary>
    internal static char? KeyChar(Key key, string? symbol)
    {
        if (symbol is { Length: 1 } && !char.IsControl(symbol[0]))
            return char.ToLowerInvariant(symbol[0]);
        return key switch
        {
            >= Key.A and <= Key.Z => (char)('a' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
            _ => null,
        };
    }

    private void Redraw()
    {
        foreach (var w in _windows)
            (w.Content as GridView)?.InvalidateVisual();
    }
}

/// <summary>A transparent, topmost, full-screen window on one monitor.</summary>
public sealed class OverlayWindow : Window
{
    private readonly Overlay _overlay;

    public OverlayWindow(Overlay overlay, Screen screen, GridView view)
    {
        _overlay = overlay;
        IsPrimary = screen.IsPrimary;
        Title = "clicky";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
        WindowState = WindowState.FullScreen;
        Content = view;
    }

    public bool IsPrimary { get; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _overlay.HandleKey(e);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Escape hatch if keyboard focus never arrived.
        _overlay.Close();
        e.Handled = true;
    }
}
