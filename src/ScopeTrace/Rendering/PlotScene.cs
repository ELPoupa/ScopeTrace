using System.Windows;
using System.Windows.Media;
using ScopeTrace.Hpgl;

namespace ScopeTrace.Rendering;

/// <summary>Geometry shared by the screen renderer and the exporters, so both look identical.</summary>
public static class PlotScene
{
    public sealed record Polyline(int Pen, List<Point> Points);

    public readonly record struct GlyphPlacement(GlyphCache.Glyph Glyph, Matrix Transform, int Pen);

    /// <summary>
    /// Joins consecutive segments that share a pen and an end point into polylines (in plotter
    /// coordinates), so strokes get proper joins instead of overlapping caps.
    /// </summary>
    public static IEnumerable<Polyline> Polylines(IReadOnlyList<PlotSegment> segments, int from, int to)
    {
        Polyline? current = null;
        for (int i = from; i < to; i++)
        {
            var s = segments[i];
            if (current is not null && current.Pen == s.Pen)
            {
                var last = current.Points[^1];
                if (last.X == s.X1 && last.Y == s.Y1)
                {
                    current.Points.Add(new Point(s.X2, s.Y2));
                    continue;
                }
            }
            if (current is not null)
                yield return current;
            current = new Polyline(s.Pen, [new Point(s.X1, s.Y1), new Point(s.X2, s.Y2)]);
        }
        if (current is not null)
            yield return current;
    }

    /// <summary>Positions every printable character of a label, mapping 1-em glyph outlines to view space.</summary>
    public static IEnumerable<GlyphPlacement> PlaceLabel(PlotLabel label, PlotOrientation orientation)
    {
        double cos = Math.Cos(label.Direction), sin = Math.Sin(label.Direction);
        // Glyph advance fills the HP-GL character cell (1.5 x width); cap height = character height.
        double sx = label.Advance / GlyphCache.AdvanceEm;
        double sy = label.CharHeight / GlyphCache.CapHeightEm;

        for (int i = 0; i < label.Text.Length; i++)
        {
            var glyph = GlyphCache.Get(label.Text[i]);
            if (glyph is null)
                continue;
            double px = label.X + i * label.Advance * cos;
            double py = label.Y + i * label.Advance * sin;
            // Glyph space is Y-down with the baseline at 0; plotter space is Y-up.
            var toPlot = new Matrix(sx * cos, sx * sin, sy * sin, -sy * cos, px, py);
            yield return new GlyphPlacement(glyph, toPlot * orientation.Matrix, label.Pen);
        }
    }

    /// <summary>Half-bright (pen 1) only exists in multi-pen plots; a single-pen plot is drawn full-bright.</summary>
    public static Color PenColor(PlotTheme theme, int pen, bool multiPen) =>
        multiPen && pen == 1 ? theme.HalfBright : theme.FullBright;

    public static bool IsMultiPen(PlotDocument doc) => doc.PensUsed.Count > 1;

    /// <summary>Stroke width in plotter units for a plot of the given size.</summary>
    public static double StrokeWidth(PlotTheme theme, double referenceSize) =>
        theme.StrokeFactor * Math.Max(referenceSize, 2000);

    public static double ReferenceSize(Rect viewBounds) =>
        viewBounds.IsEmpty ? 0 : Math.Max(viewBounds.Width, viewBounds.Height);

    /// <summary>The visible page: plot extents plus a margin.</summary>
    public static Rect PageRect(Rect viewBounds)
    {
        if (viewBounds.IsEmpty)
            return new Rect(0, 0, 1000, 1000);
        double margin = Math.Max(ReferenceSize(viewBounds) * 0.04, 20);
        var r = viewBounds;
        r.Inflate(margin, margin);
        return r;
    }
}
