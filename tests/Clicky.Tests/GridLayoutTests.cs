using Clicky.Core;

namespace Clicky.Tests;

public class GridLayoutTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly ScreenBounds Hd = new(0, 0, 1920, 1080);

    private static (double X, double Y) Pixel(GridLayout layout, string label)
    {
        var t = layout.Resolve(label) ?? throw new Xunit.Sdk.XunitException($"{label} did not resolve");
        var b = t.Monitor.Bounds;
        return (b.X + t.Fx * b.Width, b.Y + t.Fy * b.Height);
    }

    [Theory]
    [InlineData("aa", 26.667, 15)]
    [InlineData("cb", 80, 75)]
    [InlineData("a9", 1893.333, 15)]
    [InlineData("99", 1893.333, 1065)]
    [InlineData("CB", 80, 75)]
    public void SingleMonitorIs36x36(string label, double x, double y)
    {
        var layout = GridLayout.Build([Hd], Alphabet);
        Assert.Equal(36, layout.Monitors[0].Rows.Length);
        var (px, py) = Pixel(layout, label);
        Assert.Equal(x, px, 2);
        Assert.Equal(y, py, 2);
    }

    [Fact]
    public void TwoMonitorsSplitRowsLeftToRight()
    {
        // Listed right-then-left to check ordering doesn't depend on input order.
        var right = new ScreenBounds(1920, 0, 2560, 1440);
        var layout = GridLayout.Build([right, Hd], Alphabet);

        Assert.Equal("abcdefghijklmnopqr", layout.Monitors[0].Rows);
        Assert.Equal("stuvwxyz0123456789", layout.Monitors[1].Rows);
        Assert.Equal(1, layout.Monitors[0].ScreenIndex);
        Assert.Equal(0, layout.Monitors[1].ScreenIndex);

        var ra = layout.Resolve("ra")!.Value;
        Assert.Equal(0, ra.Monitor.Order);
        Assert.Equal(17, ra.Row);

        var sa = layout.Resolve("sa")!.Value;
        Assert.Equal(1, sa.Monitor.Order);
        Assert.Equal(0, sa.Row);
        var (x, y) = Pixel(layout, "sa");
        Assert.Equal(1920 + 2560 / 72.0, x, 3);
        Assert.Equal(1440 / 36.0, y, 3);
    }

    [Theory]
    [InlineData(3, new[] { 12, 12, 12 })]
    [InlineData(5, new[] { 8, 7, 7, 7, 7 })]
    public void RowsAreSplitEvenlyWithRemainderFirst(int count, int[] expected)
    {
        var screens = Enumerable.Range(0, count).Select(i => new ScreenBounds(i * 1920, 0, 1920, 1080)).ToList();
        var layout = GridLayout.Build(screens, Alphabet);
        Assert.Equal(expected, layout.Monitors.Select(m => m.Rows.Length));
        Assert.Equal(Alphabet, string.Concat(layout.Monitors.Select(m => m.Rows)));
    }

    [Fact]
    public void StackedMonitorsOrderTopBeforeBottom()
    {
        var bottom = new ScreenBounds(0, 1080, 1920, 1080);
        var layout = GridLayout.Build([bottom, Hd], Alphabet);
        Assert.Equal(Hd, layout.Monitors[0].Bounds);
        Assert.Equal(bottom, layout.Monitors[1].Bounds);
    }

    [Fact]
    public void MonitorLeftOfPrimaryComesFirst()
    {
        var left = new ScreenBounds(-1280, 200, 1280, 1024);
        var layout = GridLayout.Build([Hd, left], Alphabet);
        Assert.Equal(left, layout.Monitors[0].Bounds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    [InlineData("a!")]
    [InlineData("!a")]
    public void InvalidLabelsDoNotResolve(string label) =>
        Assert.Null(GridLayout.Build([Hd], Alphabet).Resolve(label));

    [Fact]
    public void RejectsMoreMonitorsThanAlphabetCharacters() =>
        Assert.Throws<ArgumentException>(() => GridLayout.Build([Hd, Hd, Hd], "ab"));

    [Theory]
    [InlineData("00", 0, 0)]
    [InlineData("03", 3, 0)]
    [InlineData("04", 0, 1)]
    [InlineData("10", 2, 2)]
    [InlineData("15", 3, 3)]
    public void SubCellsRefineWithinTheCell(string sub, int subCol, int subRow)
    {
        var layout = GridLayout.Build([Hd], Alphabet);
        var cell = layout.Resolve("ck")!.Value;
        var target = layout.Resolve("ck" + sub)!.Value;
        Assert.Equal((cell.Col + (subCol + 0.5) / 4) / 36, target.Fx, 9);
        Assert.Equal((cell.Row + (subRow + 0.5) / 4) / 36, target.Fy, 9);
    }

    [Theory]
    [InlineData("ck16")]
    [InlineData("ck99")]
    [InlineData("ck1")]
    [InlineData("ck1a")]
    [InlineData("ck123")]
    public void InvalidSubCellsDoNotResolve(string label) =>
        Assert.Null(GridLayout.Build([Hd], Alphabet).Resolve(label));

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("2", false)]
    [InlineData("09", true)]
    [InlineData("15", true)]
    [InlineData("16", false)]
    public void SubPrefixes(string digits, bool expected) =>
        Assert.Equal(expected, GridLayout.IsSubPrefix(digits));
}
