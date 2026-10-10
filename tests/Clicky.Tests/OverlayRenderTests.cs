using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Themes.Simple;
using Clicky.Core;
using Clicky.Overlay;

[assembly: AvaloniaTestApplication(typeof(Clicky.Tests.TestApp))]

namespace Clicky.Tests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class OverlayRenderTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Renders a two-monitor layout after typing "s" (row highlight) or "sk" (sub-grid) and saves PNGs for visual inspection
    /// (set CLICKY_SNAPSHOT_DIR to keep them).
    /// </summary>
    [AvaloniaTheory]
    [InlineData("s")]
    [InlineData("sk")]
    public void RendersTwoMonitorLayout(string typed)
    {
        var layout = GridLayout.Build([new ScreenBounds(0, 0, 1920, 1080), new ScreenBounds(1920, 0, 1920, 1080)],
            Alphabet);
        var dir = Environment.GetEnvironmentVariable("CLICKY_SNAPSHOT_DIR") ?? Path.GetTempPath();

        foreach (var monitor in layout.Monitors)
        {
            var window = new Window
            {
                Width = 1920,
                Height = 1080,
                Background = new SolidColorBrush(Colors.SteelBlue),
                Content = new GridCanvas(layout, monitor, () => typed, fontSize: 0),
            };
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(dir, $"clicky-{typed}-monitor{monitor.Order}.png"));
            window.Close();
        }
    }
}
