using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clicky.Core;

namespace Clicky.Overlay;

/// <summary>Draws one monitor's part of the grid.</summary>
public sealed class GridCanvas(GridLayout layout, MonitorGrid grid, Func<string> typed, double fontSize) : Control
{
    private static readonly IBrush Wash = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
    private static readonly IBrush RowFill = new SolidColorBrush(Color.FromArgb(45, 255, 196, 0));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), 1);
    private static readonly IBrush Label = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
    private static readonly IBrush LabelDim = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(255, 210, 60));
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
    private static readonly IBrush LabelShadow = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
    private static readonly IPen CellPen = new Pen(Accent, 1.5);
    private static readonly IPen CellShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 3.5);
    private static readonly IPen SubPen = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 210, 60)), 1);
    private static readonly IBrush SubDigit = new SolidColorBrush(Color.FromArgb(200, 255, 210, 60));
    private static readonly IBrush SubShadow = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush KeyFill = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));

    /// <summary>Size of one square in the magnified sub-grid key, in pixels.</summary>
    private const double KeySquare = 28;

    private static readonly Typeface Font = new(
        new FontFamily("Consolas, DejaVu Sans Mono, Noto Sans Mono, Liberation Mono, monospace"),
        FontStyle.Normal, FontWeight.Bold);

    public override void Render(DrawingContext ctx)
    {
        double width = Bounds.Width, height = Bounds.Height;
        int rows = grid.Rows.Length, cols = grid.Cols.Length;
        double cellW = width / cols, cellH = height / rows;

        ctx.FillRectangle(Wash, new Rect(0, 0, width, height));

        // After the second keystroke only the chosen cell's sub-grid is drawn, so the area
        // being fine-tuned stays visible.
        string text = typed();
        if (text.Length >= 2 && layout.Resolve(text[..2]) is { } t)
        {
            if (t.Monitor == grid)
                DrawSubGrid(ctx, new Rect(t.Col * cellW, t.Row * cellH, cellW, cellH), text[2..], new Rect(0, 0, width, height));
            return;
        }

        // After the first keystroke: highlight the chosen row on its monitor, dim everything else.
        int? selectedRow = null;
        bool dimAll = false;
        if (text.Length > 0 && layout.FindRow(text[0]) is var (monitor, row))
        {
            if (monitor == grid)
                selectedRow = row;
            else
                dimAll = true;
        }
        if (selectedRow is int sr)
            ctx.FillRectangle(RowFill, new Rect(0, sr * cellH, width, cellH));

        for (int c = 1; c < cols; c++)
            ctx.DrawLine(GridPen, new Point(c * cellW, 0), new Point(c * cellW, height));
        for (int r = 1; r < rows; r++)
            ctx.DrawLine(GridPen, new Point(0, r * cellH), new Point(width, r * cellH));

        double size = fontSize > 0 ? fontSize : FitFontSize(cellW, cellH);
        for (int r = 0; r < rows; r++)
        {
            IBrush brush = dimAll || (selectedRow is not null && selectedRow != r) ? LabelDim
                : selectedRow == r ? Accent
                : Label;
            for (int c = 0; c < cols; c++)
            {
                string label = string.Concat(grid.Rows[r], grid.Cols[c]).ToUpperInvariant();
                var cell = new Rect(c * cellW, r * cellH, cellW, cellH);
                if (brush != LabelDim)
                    DrawCentered(ctx, label, size, brush == Accent ? Shadow : LabelShadow, cell.Translate(new Vector(1, 1)));
                DrawCentered(ctx, label, size, brush, cell);
            }
        }
    }

    /// <summary>Smallest sub-cell label font drawn inside the cell; below this a key is used.</summary>
    private const double MinInCellFont = 8;

    /// <summary>
    /// Splits the chosen cell into sub-cells labelled "00".."15" in reading order. Labels go
    /// inside the cell when they fit at a readable size; otherwise the cell gets lines only
    /// and a magnified key beside it carries the labels. Once the first digit is typed,
    /// sub-cells that no longer match are dimmed.
    /// </summary>
    private static void DrawSubGrid(DrawingContext ctx, Rect cell, string typedSub, Rect screen)
    {
        const int n = GridLayout.SubDivisions;
        DrawLattice(ctx, cell, n);
        ctx.DrawRectangle(null, CellShadowPen, cell.Inflate(2));
        ctx.DrawRectangle(null, CellPen, cell.Inflate(1));

        double subW = cell.Width / n, subH = cell.Height / n;
        double inCell = FitFontSize(subW, subH, widthShare: 0.9, heightShare: 0.7, min: 1);
        if (inCell >= MinInCellFont)
        {
            DrawSubLabels(ctx, cell, typedSub, inCell, shadow: true);
            return;
        }

        // Put the key to the right of the cell, or to the left if it would run off-screen,
        // vertically centred on the cell and kept on-screen.
        const double gap = 10;
        double side = KeySquare * n;
        double x = cell.Right + gap + side <= screen.Right ? cell.Right + gap : cell.X - gap - side;
        double y = Math.Clamp(cell.Center.Y - side / 2, screen.Y, screen.Bottom - side);
        var key = new Rect(x, y, side, side);

        ctx.FillRectangle(KeyFill, key);
        DrawLattice(ctx, key, n);
        ctx.DrawRectangle(null, CellPen, key);
        DrawSubLabels(ctx, key, typedSub, FitFontSize(KeySquare, KeySquare, widthShare: 0.8, heightShare: 0.6), shadow: false);
    }

    private static void DrawSubLabels(DrawingContext ctx, Rect area, string typedSub, double size, bool shadow)
    {
        const int n = GridLayout.SubDivisions;
        double w = area.Width / n, h = area.Height / n;
        for (int i = 0; i < n * n; i++)
        {
            var sub = new Rect(area.X + i % n * w, area.Y + i / n * h, w, h);
            string label = GridLayout.SubLabel(i);
            if (!label.StartsWith(typedSub, StringComparison.Ordinal))
            {
                DrawCentered(ctx, label, size, LabelDim, sub);
                continue;
            }
            if (shadow)
                DrawCentered(ctx, label, size, SubShadow, sub.Translate(new Vector(1, 1)));
            DrawCentered(ctx, label, size, SubDigit, sub);
        }
    }

    private static void DrawLattice(DrawingContext ctx, Rect area, int n)
    {
        double w = area.Width / n, h = area.Height / n;
        for (int i = 1; i < n; i++)
        {
            ctx.DrawLine(SubPen, new Point(area.X + i * w, area.Y), new Point(area.X + i * w, area.Bottom));
            ctx.DrawLine(SubPen, new Point(area.X, area.Y + i * h), new Point(area.Right, area.Y + i * h));
        }
    }

    private static void DrawCentered(DrawingContext ctx, string text, double size, IBrush brush, Rect cell)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush);
        ctx.DrawText(ft, new Point(cell.X + (cell.Width - ft.Width) / 2, cell.Y + (cell.Height - ft.Height) / 2));
    }

    private static double FitFontSize(double cellW, double cellH,
        double widthShare = 0.65, double heightShare = 0.45, double min = 6)
    {
        double size = Math.Max(Math.Floor(cellH * heightShare), min);
        while (size > min)
        {
            var ft = new FormattedText("WW", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Label);
            if (ft.Width <= cellW * widthShare)
                break;
            size--;
        }
        return size;
    }
}
