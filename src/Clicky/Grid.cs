using System.Globalization;

namespace Clicky;

/// <summary>A screen rectangle in the platform's native coordinates.</summary>
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>One monitor's share of the grid.</summary>
/// <param name="Index">Position in left-to-right, then top-to-bottom monitor order.</param>
/// <param name="ScreenIndex">Index into the screen list passed to <see cref="Grid.Build"/>.</param>
/// <param name="Rows">Row characters owned by this monitor, top to bottom.</param>
/// <param name="Cols">Column characters, left to right (always the full alphabet).</param>
public sealed record GridMonitor(int Index, int ScreenIndex, ScreenBounds Bounds, string Rows, string Cols);

/// <summary>A chosen cell and the point to click, as fractions of the monitor's width and height.</summary>
public readonly record struct Target(GridMonitor Monitor, int Row, int Col, double Fx, double Fy);

/// <summary>
/// The labelled grid across all monitors. A label is a row character, a column character
/// and optionally a two-digit sub-cell ("00".."15"). Rows are split between monitors so
/// every label is unique; every monitor gets all the columns.
/// </summary>
public sealed class Grid
{
    /// <summary>The chosen cell is split into SubSize x SubSize sub-cells.</summary>
    public const int SubSize = 4;
    public const int CellLabelLength = 2;
    public const int FullLabelLength = CellLabelLength + 2;

    public IReadOnlyList<GridMonitor> Monitors { get; }
    public string Alphabet { get; }

    private Grid(IReadOnlyList<GridMonitor> monitors, string alphabet)
    {
        Monitors = monitors;
        Alphabet = alphabet;
    }

    public static Grid Build(IReadOnlyList<ScreenBounds> screens, string alphabet)
    {
        if (screens.Count == 0)
            throw new ArgumentException("at least one screen is required", nameof(screens));
        if (alphabet.Length < screens.Count)
            throw new ArgumentException(
                $"alphabet has {alphabet.Length} characters but there are {screens.Count} monitors", nameof(alphabet));

        // Earlier monitors take one extra row each while the remainder lasts.
        var monitors = new List<GridMonitor>();
        int offset = 0;
        foreach (var (screenIndex, i) in Order(screens).Select((s, i) => (s, i)))
        {
            int rows = alphabet.Length / screens.Count + (i < alphabet.Length % screens.Count ? 1 : 0);
            monitors.Add(new GridMonitor(i, screenIndex, screens[screenIndex], alphabet.Substring(offset, rows), alphabet));
            offset += rows;
        }
        return new Grid(monitors, alphabet);
    }

    /// <summary>Indices of <paramref name="screens"/> sorted left to right, then top to bottom.</summary>
    public static int[] Order(IReadOnlyList<ScreenBounds> screens) =>
        Enumerable.Range(0, screens.Count).OrderBy(i => screens[i].X).ThenBy(i => screens[i].Y).ToArray();

    public (GridMonitor Monitor, int Row)? FindRow(char c)
    {
        foreach (var m in Monitors)
            if (m.Rows.IndexOf(char.ToLowerInvariant(c)) is var row and >= 0)
                return (m, row);
        return null;
    }

    /// <summary>True if typing <paramref name="c"/> after <paramref name="typed"/> still leads to a valid label.</summary>
    public bool CanAppend(string typed, char c) => typed.Length switch
    {
        0 => FindRow(c) is not null,
        1 => Alphabet.Contains(char.ToLowerInvariant(c)),
        < FullLabelLength => Enumerable.Range(0, SubSize * SubSize)
            .Any(i => SubLabel(i).StartsWith(typed[CellLabelLength..] + c, StringComparison.Ordinal)),
        _ => false,
    };

    /// <summary>
    /// Resolves a cell label ("sk") to the cell centre, or a full label ("sk05") to the
    /// centre of that sub-cell. Sub-cells are numbered in reading order from "00" top-left.
    /// </summary>
    public Target? Resolve(string label)
    {
        if (label.Length is not (CellLabelLength or FullLabelLength) || FindRow(label[0]) is not var (monitor, row))
            return null;
        int col = monitor.Cols.IndexOf(char.ToLowerInvariant(label[1]));
        if (col < 0)
            return null;

        double subX = 0.5, subY = 0.5;
        if (label.Length == FullLabelLength)
        {
            if (!int.TryParse(label[CellLabelLength..], NumberStyles.None, CultureInfo.InvariantCulture, out int sub)
                || sub >= SubSize * SubSize)
                return null;
            subX = (sub % SubSize + 0.5) / SubSize;
            subY = (sub / SubSize + 0.5) / SubSize;
        }
        return new Target(monitor, row, col, (col + subX) / monitor.Cols.Length, (row + subY) / monitor.Rows.Length);
    }

    public static string SubLabel(int index) => index.ToString("D2", CultureInfo.InvariantCulture);
}
