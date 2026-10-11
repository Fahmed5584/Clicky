using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Themes.Simple;

[assembly: AvaloniaTestApplication(typeof(Clicky.Tests.TestApp))]

namespace Clicky.Tests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class RenderTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Renders two 1080p monitors for each input stage and saves PNGs for visual inspection
    /// (set CLICKY_SNAPSHOT_DIR to keep them): nothing typed, a row ("s"), a cell ("sk")
    /// and a partial sub-cell ("sk1"). A single monitor ("ak", 36 rows) shows the key fallback.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("", 2)]
    [InlineData("s", 2)]
    [InlineData("sk", 2)]
    [InlineData("sk1", 2)]
    [InlineData("ak", 1)]
    public void Renders(string typed, int monitors)
    {
        var screens = Enumerable.Range(0, monitors).Select(i => new ScreenBounds(i * 1920, 0, 1920, 1080)).ToList();
        var grid = Grid.Build(screens, Alphabet);
        var dir = Environment.GetEnvironmentVariable("CLICKY_SNAPSHOT_DIR") ?? Path.GetTempPath();

        foreach (var monitor in grid.Monitors)
        {
            var window = new Window
            {
                Width = 1920,
                Height = 1080,
                Background = new SolidColorBrush(Colors.SteelBlue),
                Content = new GridView(grid, monitor, () => typed, fontSize: 0),
            };
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(dir, $"clicky-{monitors}mon-'{typed}'-monitor{monitor.Index}.png"));
            window.Close();
        }
    }
}
