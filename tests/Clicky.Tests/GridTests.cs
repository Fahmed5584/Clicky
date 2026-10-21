namespace Clicky.Tests;

public class GridTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly ScreenBounds Hd = new(0, 0, 1920, 1080);

    private static (double X, double Y) Pixel(Grid grid, string label)
    {
        var t = grid.Resolve(label) ?? throw new Xunit.Sdk.XunitException($"{label} did not resolve");
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
        var grid = Grid.Build([Hd], Alphabet);
        Assert.Equal(36, grid.Monitors[0].Rows.Length);
        var (px, py) = Pixel(grid, label);
        Assert.Equal(x, px, 2);
        Assert.Equal(y, py, 2);
    }

    [Fact]
    public void TwoMonitorsSplitRowsLeftToRight()
    {
        // Listed right-then-left to check ordering doesn't depend on input order.
        var right = new ScreenBounds(1920, 0, 2560, 1440);
        var grid = Grid.Build([right, Hd], Alphabet);

        Assert.Equal("abcdefghijklmnopqr", grid.Monitors[0].Rows);
        Assert.Equal("stuvwxyz0123456789", grid.Monitors[1].Rows);
        Assert.Equal(1, grid.Monitors[0].ScreenIndex);
        Assert.Equal(0, grid.Monitors[1].ScreenIndex);

        var ra = grid.Resolve("ra")!.Value;
        Assert.Equal(0, ra.Monitor.Index);
        Assert.Equal(17, ra.Row);

        var sa = grid.Resolve("sa")!.Value;
        Assert.Equal(1, sa.Monitor.Index);
        Assert.Equal(0, sa.Row);
        var (x, y) = Pixel(grid, "sa");
        Assert.Equal(1920 + 2560 / 72.0, x, 3);
        Assert.Equal(1440 / 36.0, y, 3);
    }

    [Theory]
    [InlineData(3, new[] { 12, 12, 12 })]
    [InlineData(5, new[] { 8, 7, 7, 7, 7 })]
    public void RowsAreSplitEvenlyWithRemainderFirst(int count, int[] expected)
    {
        var screens = Enumerable.Range(0, count).Select(i => new ScreenBounds(i * 1920, 0, 1920, 1080)).ToList();
        var grid = Grid.Build(screens, Alphabet);
        Assert.Equal(expected, grid.Monitors.Select(m => m.Rows.Length));
        Assert.Equal(Alphabet, string.Concat(grid.Monitors.Select(m => m.Rows)));
    }

    [Fact]
    public void StackedMonitorsOrderTopBeforeBottom()
    {
        var bottom = new ScreenBounds(0, 1080, 1920, 1080);
        var grid = Grid.Build([bottom, Hd], Alphabet);
        Assert.Equal(Hd, grid.Monitors[0].Bounds);
        Assert.Equal(bottom, grid.Monitors[1].Bounds);
    }

    [Fact]
    public void MonitorLeftOfPrimaryComesFirst()
    {
        var left = new ScreenBounds(-1280, 200, 1280, 1024);
        Assert.Equal(left, Grid.Build([Hd, left], Alphabet).Monitors[0].Bounds);
    }

    [Fact]
    public void RejectsMoreMonitorsThanAlphabetCharacters() =>
        Assert.Throws<ArgumentException>(() => Grid.Build([Hd, Hd, Hd], "ab"));

    [Fact]
    public void CustomCountsTakeAlphabetPrefixes()
    {
        var grid = Grid.Build([Hd], Alphabet, columns: 32, rows: 18);
        var m = grid.Monitors[0];
        Assert.Equal(Alphabet[..32], m.Cols);
        Assert.Equal(Alphabet[..18], m.Rows);
        var (x, y) = Pixel(grid, "aa");
        Assert.Equal(30, x, 3);
        Assert.Equal(30, y, 3);
        Assert.Null(grid.Resolve("a6")); // column 33 is outside the grid
        Assert.False(grid.CanAppend("a", '6'));
        Assert.Null(grid.Resolve("sa")); // row 19 too
    }

    [Fact]
    public void CustomRowsAreTakenInMonitorOrder()
    {
        var right = new ScreenBounds(1920, 0, 2560, 1440);
        var grid = Grid.Build([right, Hd], Alphabet, columns: 20, rows: 12);
        Assert.Equal("abcdefghijkl", grid.Monitors[0].Rows);
        Assert.Equal("mnopqrstuvwx", grid.Monitors[1].Rows);
        Assert.All(grid.Monitors, m => Assert.Equal(Alphabet[..20], m.Cols));
        Assert.Equal(1, grid.Resolve("ma")!.Value.Monitor.Index);
    }

    [Theory]
    [InlineData(-1, 0, 4)]
    [InlineData(37, 0, 4)]
    [InlineData(0, 19, 4)] // 19 rows x 2 monitors > 36 characters
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, 11)]
    public void RejectsImpossibleSizes(int columns, int rows, int subSize) =>
        Assert.Throws<ArgumentException>(() => Grid.Build([Hd, Hd], Alphabet, columns, rows, subSize));

    [Theory]
    [InlineData(3, "ck08", true)]
    [InlineData(3, "ck09", false)]
    [InlineData(10, "ck99", true)]
    [InlineData(2, "ck04", false)]
    public void SubGridSizeLimitsSubLabels(int subSize, string label, bool valid)
    {
        var grid = Grid.Build([Hd], Alphabet, subSize: subSize);
        Assert.Equal(valid, grid.Resolve(label) is not null);
        Assert.Equal(valid, grid.CanAppend(label[..3], label[3]));
    }

    [Fact]
    public void SubGridOf3SplitsIntoThirds()
    {
        var grid = Grid.Build([Hd], Alphabet, subSize: 3);
        var cell = grid.Resolve("ck")!.Value;
        var target = grid.Resolve("ck05")!.Value; // third column, middle row
        Assert.Equal((cell.Col + 2.5 / 3) / 36, target.Fx, 9);
        Assert.Equal((cell.Row + 1.5 / 3) / 36, target.Fy, 9);
    }

    [Theory]
    [InlineData("00", 0, 0)]
    [InlineData("03", 3, 0)]
    [InlineData("04", 0, 1)]
    [InlineData("10", 2, 2)]
    [InlineData("15", 3, 3)]
    public void SubCellsRefineWithinTheCell(string sub, int subCol, int subRow)
    {
        var grid = Grid.Build([Hd], Alphabet);
        var cell = grid.Resolve("ck")!.Value;
        var target = grid.Resolve("ck" + sub)!.Value;
        Assert.Equal((cell.Col + (subCol + 0.5) / 4) / 36, target.Fx, 9);
        Assert.Equal((cell.Row + (subRow + 0.5) / 4) / 36, target.Fy, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    [InlineData("a!")]
    [InlineData("!a")]
    [InlineData("ck16")]
    [InlineData("ck99")]
    [InlineData("ck1a")]
    [InlineData("ck+1")]
    [InlineData("ck123")]
    public void InvalidLabelsDoNotResolve(string label) =>
        Assert.Null(Grid.Build([Hd], Alphabet).Resolve(label));

    [Theory]
    [InlineData("", 'a', true)]
    [InlineData("", 'A', true)]
    [InlineData("", '!', false)]
    [InlineData("s", 'k', true)]
    [InlineData("s", '!', false)]
    [InlineData("sk", '0', true)]
    [InlineData("sk", '1', true)]
    [InlineData("sk", '2', false)]
    [InlineData("sk", 'a', false)]
    [InlineData("sk0", '9', true)]
    [InlineData("sk1", '5', true)]
    [InlineData("sk1", '6', false)]
    [InlineData("sk15", '0', false)]
    public void TypingRules(string typed, char next, bool allowed) =>
        Assert.Equal(allowed, Grid.Build([Hd], Alphabet).CanAppend(typed, next));
}
