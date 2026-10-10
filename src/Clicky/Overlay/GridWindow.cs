using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Clicky.Core;

namespace Clicky.Overlay;

/// <summary>Full-screen transparent topmost window covering one monitor.</summary>
public sealed class GridWindow : Window
{
    private readonly OverlayController _owner;

    public GridWindow(OverlayController owner, GridLayout layout, MonitorGrid grid, Screen screen)
    {
        _owner = owner;
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
        Content = Canvas = new GridCanvas(layout, grid, () => owner.Typed, owner.FontSize);
        IsPrimaryScreen = screen.IsPrimary;
    }

    public GridCanvas Canvas { get; }
    public bool IsPrimaryScreen { get; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _owner.HandleKey(e);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Escape hatch if keyboard focus never arrived.
        _owner.Cancel();
        e.Handled = true;
    }
}
