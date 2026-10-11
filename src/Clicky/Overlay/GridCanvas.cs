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
        if (text.Length == 2 && layout.Resolve(text) is { } t)
        {
            if (t.Monitor == grid)
                DrawSubGrid(ctx, new Rect(t.Col * cellW, t.Row * cellH, cellW, cellH));
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

    /// <summary>
    /// Outlines the cell and its 3x3 sub-cells with no fill. Each digit sits in its sub-cell's
    /// top-left corner and a dot marks the click point, so the content underneath stays visible.
    /// </summary>
    private static void DrawSubGrid(DrawingContext ctx, Rect cell)
    {
        const int n = GridLayout.SubDivisions;
        double subW = cell.Width / n, subH = cell.Height / n;
        for (int i = 1; i < n; i++)
        {
            ctx.DrawLine(SubPen, new Point(cell.X + i * subW, cell.Y), new Point(cell.X + i * subW, cell.Bottom));
            ctx.DrawLine(SubPen, new Point(cell.X, cell.Y + i * subH), new Point(cell.Right, cell.Y + i * subH));
        }
        ctx.DrawRectangle(null, CellShadowPen, cell.Inflate(2));
        ctx.DrawRectangle(null, CellPen, cell.Inflate(1));

        double size = Math.Max(Math.Floor(Math.Min(subW, subH) * 0.5), 7);
        for (int i = 0; i < n * n; i++)
        {
            var sub = new Rect(cell.X + i % n * subW, cell.Y + i / n * subH, subW, subH);
            string digit = (i + 1).ToString(CultureInfo.InvariantCulture);
            var ft = new FormattedText(digit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, SubDigit);
            var origin = new Point(sub.X + 1.5, sub.Y);
            ctx.DrawText(new FormattedText(digit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, SubShadow),
                origin + new Vector(1, 1));
            ctx.DrawText(ft, origin);
            ctx.DrawEllipse(SubShadow, null, sub.Center, 2, 2);
            ctx.DrawEllipse(SubDigit, null, sub.Center, 1.2, 1.2);
        }
    }

    private static void DrawCentered(DrawingContext ctx, string text, double size, IBrush brush, Rect cell)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush);
        ctx.DrawText(ft, new Point(cell.X + (cell.Width - ft.Width) / 2, cell.Y + (cell.Height - ft.Height) / 2));
    }

    private static double FitFontSize(double cellW, double cellH)
    {
        double size = Math.Max(Math.Floor(cellH * 0.45), 6);
        while (size > 6)
        {
            var ft = new FormattedText("WW", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Label);
            if (ft.Width <= cellW * 0.65)
                break;
            size--;
        }
        return size;
    }
}
