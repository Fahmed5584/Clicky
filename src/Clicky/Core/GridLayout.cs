namespace Clicky.Core;

/// <summary>A screen rectangle in the platform's native coordinates.</summary>
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>The grid shown on one monitor.</summary>
/// <param name="ScreenIndex">Index into the screen list passed to <see cref="GridLayout.Build"/>.</param>
/// <param name="Order">Position in left-to-right, top-to-bottom monitor order.</param>
/// <param name="Rows">Row characters owned by this monitor, top to bottom.</param>
/// <param name="Cols">Column characters, left to right (the full alphabet).</param>
public sealed record MonitorGrid(int ScreenIndex, int Order, ScreenBounds Bounds, string Rows, string Cols);

/// <summary>A resolved cell; Fx/Fy are the click point as fractions of the monitor.</summary>
public readonly record struct CellTarget(MonitorGrid Monitor, int Row, int Col, double Fx, double Fy);

/// <summary>
/// Splits the alphabet's row characters across monitors so every label is unique:
/// each monitor gets a contiguous block of rows and all columns.
/// </summary>
public sealed class GridLayout
{
    public IReadOnlyList<MonitorGrid> Monitors { get; }
    public string Alphabet { get; }

    private GridLayout(IReadOnlyList<MonitorGrid> monitors, string alphabet)
    {
        Monitors = monitors;
        Alphabet = alphabet;
    }

    public static GridLayout Build(IReadOnlyList<ScreenBounds> screens, string alphabet)
    {
        if (screens.Count == 0)
            throw new ArgumentException("at least one screen is required", nameof(screens));
        if (alphabet.Length < screens.Count)
            throw new ArgumentException(
                $"alphabet has {alphabet.Length} characters but there are {screens.Count} monitors", nameof(alphabet));

        var order = Order(screens);
        int baseRows = alphabet.Length / screens.Count;
        int extra = alphabet.Length % screens.Count;
        var monitors = new List<MonitorGrid>(screens.Count);
        int offset = 0;
        for (int k = 0; k < order.Length; k++)
        {
            int rows = baseRows + (k < extra ? 1 : 0);
            int index = order[k];
            monitors.Add(new MonitorGrid(index, k, screens[index], alphabet.Substring(offset, rows), alphabet));
            offset += rows;
        }
        return new GridLayout(monitors, alphabet);
    }

    /// <summary>Indices of <paramref name="screens"/> sorted left to right, then top to bottom.</summary>
    public static int[] Order(IReadOnlyList<ScreenBounds> screens) =>
        Enumerable.Range(0, screens.Count)
            .OrderBy(i => screens[i].X)
            .ThenBy(i => screens[i].Y)
            .ToArray();

    public (MonitorGrid Monitor, int Row)? FindRow(char c)
    {
        c = char.ToLowerInvariant(c);
        foreach (var m in Monitors)
        {
            int row = m.Rows.IndexOf(c);
            if (row >= 0)
                return (m, row);
        }
        return null;
    }

    /// <summary>Side length of the sub-grid shown inside a chosen cell.</summary>
    public const int SubDivisions = 4;

    /// <summary>Characters in a sub-cell label ("00" to "15").</summary>
    public const int SubLabelLength = 2;

    /// <summary>
    /// Resolves a two-character label to its cell centre, or a label plus a two-digit
    /// sub-cell number ("00" top-left to "15" bottom-right, in reading order) to that
    /// sub-cell's centre.
    /// </summary>
    public CellTarget? Resolve(string label)
    {
        if (label.Length is not (2 or 2 + SubLabelLength) || FindRow(label[0]) is not var (monitor, row))
            return null;
        int col = monitor.Cols.IndexOf(char.ToLowerInvariant(label[1]));
        if (col < 0)
            return null;

        double subX = 0.5, subY = 0.5;
        if (label.Length > 2)
        {
            if (SubCell(label[2..]) is not int sub)
                return null;
            subX = (sub % SubDivisions + 0.5) / SubDivisions;
            subY = (sub / SubDivisions + 0.5) / SubDivisions;
        }
        return new CellTarget(monitor, row, col,
            (col + subX) / monitor.Cols.Length,
            (row + subY) / monitor.Rows.Length);
    }

    /// <summary>Sub-cell index for a two-digit label "00".."15", or null.</summary>
    public static int? SubCell(string digits) =>
        digits.Length == SubLabelLength && digits.All(char.IsAsciiDigit)
        && int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) is var n
        && n < SubDivisions * SubDivisions ? n : null;

    /// <summary>Two-digit label for sub-cell <paramref name="index"/>.</summary>
    public static string SubLabel(int index) =>
        index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True if <paramref name="digits"/> is the start of some sub-cell label.</summary>
    public static bool IsSubPrefix(string digits) =>
        Enumerable.Range(0, SubDivisions * SubDivisions).Any(i => SubLabel(i).StartsWith(digits, StringComparison.Ordinal));
}
