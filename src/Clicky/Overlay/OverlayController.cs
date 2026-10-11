using Avalonia.Controls;
using Avalonia.Input;
using Clicky.Core;
using Clicky.Platform;

namespace Clicky.Overlay;

/// <summary>
/// Opens one <see cref="GridWindow"/> per monitor, collects the typed label from whichever
/// window has focus, and clicks the resolved cell. Must be used on the UI thread.
/// </summary>
public sealed class OverlayController(Config config, IPointerService pointer)
{
    private readonly List<GridWindow> _windows = [];
    // Never shown; only used to enumerate screens.
    private readonly Window _probe = new();
    private GridLayout? _layout;

    public string Typed { get; private set; } = "";
    public double FontSize => config.FontSize;

    public void Show()
    {
        CloseWindows();
        var screens = _probe.Screens.All;
        if (screens.Count == 0)
        {
            Log.Error("no screens found");
            return;
        }

        try
        {
            _layout = GridLayout.Build(
                screens.Select(s => new ScreenBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList(),
                config.Alphabet);
        }
        catch (ArgumentException e)
        {
            Log.Error(e.Message);
            return;
        }

        Typed = "";
        foreach (var monitor in _layout.Monitors)
        {
            var window = new GridWindow(this, _layout, monitor, screens[monitor.ScreenIndex]);
            _windows.Add(window);
            window.Show();
        }
        (_windows.FirstOrDefault(w => w.IsPrimaryScreen) ?? _windows[0]).Activate();
    }

    public void Cancel() => CloseWindows();

    public void HandleKey(KeyEventArgs e)
    {
        if (_layout is null)
            return;
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                return;
            case Key.Back:
                if (Typed.Length > 0)
                {
                    Typed = Typed[..^1];
                    Redraw();
                }
                return;
            case Key.Enter or Key.Space when Typed.Length >= 2:
                // Skip the sub-grid and click the cell centre.
                Click(Typed[..2]);
                return;
        }

        if (KeyChar(e) is not char ch)
            return;
        bool valid = Typed.Length switch
        {
            0 => _layout.FindRow(ch) is not null,
            1 => _layout.Alphabet.Contains(ch),
            _ => GridLayout.IsSubPrefix(Typed[2..] + ch),
        };
        if (!valid)
            return;

        Typed += ch;
        if (Typed.Length < 2 + GridLayout.SubLabelLength)
            Redraw();
        else
            Click(Typed);
    }

    private void Click(string label)
    {
        var target = _layout!.Resolve(label);
        int screenCount = _layout.Monitors.Count;
        CloseWindows();
        if (target is { } t)
            _ = ClickAsync(new ClickTarget(t.Monitor.Order, screenCount, t.Monitor.Bounds, t.Fx, t.Fy));
    }

    private async Task ClickAsync(ClickTarget target)
    {
        try
        {
            await Task.Delay(config.ClickDelayMs);
            await pointer.ClickAsync(target);
        }
        catch (Exception e)
        {
            Log.Error($"click failed: {e.Message}");
        }
    }

    private static char? KeyChar(KeyEventArgs e)
    {
        if (e.KeySymbol is { Length: 1 } symbol && !char.IsControl(symbol[0]))
            return char.ToLowerInvariant(symbol[0]);
        return e.Key switch
        {
            >= Key.A and <= Key.Z => (char)('a' + (e.Key - Key.A)),
            >= Key.D0 and <= Key.D9 => (char)('0' + (e.Key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (e.Key - Key.NumPad0)),
            _ => null,
        };
    }

    private void Redraw()
    {
        foreach (var w in _windows)
            w.Canvas.InvalidateVisual();
    }

    private void CloseWindows()
    {
        Typed = "";
        foreach (var w in _windows)
            w.Close();
        _windows.Clear();
    }
}
