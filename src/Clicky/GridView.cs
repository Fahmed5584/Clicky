using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Clicky;

/// <summary>
/// Draws one monitor's part of the grid for the current input:
/// nothing typed → all labels; a row typed → that row highlighted;
/// a cell typed → only that cell, split into numbered sub-cells.
/// </summary>
public sealed class GridView(Grid grid, GridMonitor monitor, Func<string> typed, double fontSize) : Control
{
    private static readonly IBrush Wash = Brush(40, 0, 0, 0);
    private static readonly IBrush RowFill = Brush(45, 255, 196, 0);
    private static readonly IPen GridPen = new Pen(Brush(35, 255, 255, 255), 1);
    private static readonly IBrush Label = Brush(128, 255, 255, 255);
    private static readonly IBrush LabelDim = Brush(40, 255, 255, 255);
    private static readonly IBrush LabelShadow = Brush(90, 0, 0, 0);
    private static readonly IBrush Accent = Brush(255, 255, 210, 60);
    private static readonly IBrush AccentShadow = Brush(200, 0, 0, 0);
    private static readonly IPen CellPen = new Pen(Accent, 1.5);
    private static readonly IPen CellShadowPen = new Pen(Brush(140, 0, 0, 0), 3.5);
    private static readonly IPen SubPen = new Pen(Brush(110, 255, 210, 60), 1);
    private static readonly IBrush SubLabel = Brush(200, 255, 210, 60);
    private static readonly IBrush SubShadow = Brush(150, 0, 0, 0);
    private static readonly IBrush KeyFill = Brush(200, 20, 20, 20);

    private static readonly Typeface Font = new(
        new FontFamily("Consolas, DejaVu Sans Mono, Noto Sans Mono, Liberation Mono, monospace"),
        FontStyle.Normal, FontWeight.Bold);

    /// <summary>Width of a two-character label per pixel of font size (the font is monospace).</summary>
    private static readonly Lazy<double> LabelWidthPerPx = new(() => Text("WW", 100, Label).Width / 100);

    /// <summary>Sub-cell labels smaller than this go in a magnified key beside the cell instead.</summary>
    private const double MinSubFont = 8;

    /// <summary>Side of one square in the magnified key, in pixels.</summary>
    private const double KeySquare = 28;

    public override void Render(DrawingContext ctx)
    {
        var screen = new Rect(Bounds.Size);
        double cellW = screen.Width / monitor.Cols.Length, cellH = screen.Height / monitor.Rows.Length;
        ctx.FillRectangle(Wash, screen);

        string text = typed();
        if (text.Length >= Grid.CellLabelLength && grid.Resolve(text[..Grid.CellLabelLength]) is { } cell)
        {
            // Only the chosen cell's monitor shows anything beyond the tint.
            if (cell.Monitor == monitor)
                DrawSubGrid(ctx, new Rect(cell.Col * cellW, cell.Row * cellH, cellW, cellH),
                    text[Grid.CellLabelLength..], screen);
        }
        else
        {
            DrawCells(ctx, screen, cellW, cellH, text.Length > 0 ? grid.FindRow(text[0]) : null);
        }
    }

    /// <summary>
    /// Draws every cell label. Once a row is typed it lights up on its monitor and every other
    /// label dims.
    /// </summary>
    private void DrawCells(DrawingContext ctx, Rect screen, double cellW, double cellH,
        (GridMonitor Monitor, int Row)? typedRow)
    {
        bool anyRowTyped = typedRow is not null;
        int? selectedRow = typedRow is var (m, row) && m == monitor ? row : null;
        if (selectedRow is int sr)
            ctx.FillRectangle(RowFill, new Rect(0, sr * cellH, screen.Width, cellH));

        DrawLines(ctx, GridPen, screen, monitor.Cols.Length, monitor.Rows.Length);
        double size = fontSize > 0 ? fontSize : FitFont(cellW, cellH, widthShare: 0.65, heightShare: 0.45);
        for (int r = 0; r < monitor.Rows.Length; r++)
        {
            var (brush, shadow) = r == selectedRow ? (Accent, AccentShadow)
                : anyRowTyped ? (LabelDim, null)
                : (Label, LabelShadow);
            for (int c = 0; c < monitor.Cols.Length; c++)
            {
                string label = string.Concat(monitor.Rows[r], monitor.Cols[c]).ToUpperInvariant();
                DrawLabel(ctx, label, size, brush, shadow, new Rect(c * cellW, r * cellH, cellW, cellH));
            }
        }
    }

    /// <summary>
    /// Outlines the chosen cell and its sub-cells, without filling it so what's underneath
    /// stays visible. Labels go inside the cell when they are readable there, otherwise in a
    /// magnified key beside it. Labels that no longer match the typed digits are dimmed.
    /// </summary>
    private void DrawSubGrid(DrawingContext ctx, Rect cell, string typedSub, Rect screen)
    {
        DrawLines(ctx, SubPen, cell, grid.SubSize, grid.SubSize);
        ctx.DrawRectangle(null, CellShadowPen, cell.Inflate(2));
        ctx.DrawRectangle(null, CellPen, cell.Inflate(1));

        double inCellSize = FitFont(cell.Width / grid.SubSize, cell.Height / grid.SubSize,
            widthShare: 0.9, heightShare: 0.7, min: 0);
        if (inCellSize >= MinSubFont)
        {
            DrawSubLabels(ctx, cell, typedSub, inCellSize, SubShadow);
            return;
        }

        var key = KeyArea(cell, screen, KeySquare * grid.SubSize);
        ctx.FillRectangle(KeyFill, key);
        DrawLines(ctx, SubPen, key, grid.SubSize, grid.SubSize);
        ctx.DrawRectangle(null, CellPen, key);
        DrawSubLabels(ctx, key, typedSub, FitFont(KeySquare, KeySquare, widthShare: 0.8, heightShare: 0.6), shadow: null);
    }

    /// <summary>
    /// Where the magnified key goes: right of the cell (left if that would run off-screen),
    /// vertically centred on the cell and kept on-screen.
    /// </summary>
    private static Rect KeyArea(Rect cell, Rect screen, double side)
    {
        const double gap = 10;
        double x = cell.Right + gap + side <= screen.Right ? cell.Right + gap : cell.X - gap - side;
        double y = Math.Clamp(cell.Center.Y - side / 2, screen.Y, screen.Bottom - side);
        return new Rect(x, y, side, side);
    }

    private void DrawSubLabels(DrawingContext ctx, Rect area, string typedSub, double size, IBrush? shadow)
    {
        double w = area.Width / grid.SubSize, h = area.Height / grid.SubSize;
        for (int i = 0; i < grid.SubSize * grid.SubSize; i++)
        {
            string label = Grid.SubLabel(i);
            var sub = new Rect(area.X + i % grid.SubSize * w, area.Y + i / grid.SubSize * h, w, h);
            if (label.StartsWith(typedSub, StringComparison.Ordinal))
                DrawLabel(ctx, label, size, SubLabel, shadow, sub);
            else
                DrawLabel(ctx, label, size, LabelDim, null, sub);
        }
    }

    /// <summary>Draws the inner lines of a cols x rows lattice over <paramref name="area"/>.</summary>
    private static void DrawLines(DrawingContext ctx, IPen pen, Rect area, int cols, int rows)
    {
        for (int c = 1; c < cols; c++)
        {
            double x = area.X + c * area.Width / cols;
            ctx.DrawLine(pen, new Point(x, area.Y), new Point(x, area.Bottom));
        }
        for (int r = 1; r < rows; r++)
        {
            double y = area.Y + r * area.Height / rows;
            ctx.DrawLine(pen, new Point(area.X, y), new Point(area.Right, y));
        }
    }

    /// <summary>Draws text centred in <paramref name="box"/>, with an optional 1px drop shadow.</summary>
    private static void DrawLabel(DrawingContext ctx, string text, double size, IBrush brush, IBrush? shadow, Rect box)
    {
        if (shadow is not null)
            DrawLabel(ctx, text, size, shadow, null, box.Translate(new Vector(1, 1)));
        var ft = Text(text, size, brush);
        ctx.DrawText(ft, new Point(box.X + (box.Width - ft.Width) / 2, box.Y + (box.Height - ft.Height) / 2));
    }

    /// <summary>Largest whole font size for a two-character label within the given shares of a box.</summary>
    private static double FitFont(double width, double height, double widthShare, double heightShare, double min = 6) =>
        Math.Max(Math.Floor(Math.Min(height * heightShare, width * widthShare / LabelWidthPerPx.Value)), min);

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush);

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));
}
