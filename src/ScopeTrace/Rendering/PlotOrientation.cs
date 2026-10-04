using System.Windows;
using System.Windows.Media;
using ScopeTrace.Hpgl;

namespace ScopeTrace.Rendering;

/// <summary>
/// Maps HP-GL plotter coordinates (Y up) to "view space": WPF coordinates (Y down) in plotter
/// units, rotated clockwise by a multiple of 90 degrees.
/// </summary>
public readonly struct PlotOrientation
{
    public PlotOrientation(int clockwiseDegrees)
    {
        Degrees = ((clockwiseDegrees % 360) + 360) % 360 / 90 * 90;
        // Flip Y (plotter Y up -> screen Y down), then rotate clockwise on screen.
        var m = new Matrix(1, 0, 0, -1, 0, 0);
        m.Rotate(Degrees);
        Matrix = m;
    }

    public int Degrees { get; }
    public Matrix Matrix { get; }

    public Point Map(double x, double y) => Matrix.Transform(new Point(x, y));

    public Rect Map(PlotBounds b)
    {
        if (b.IsEmpty)
            return Rect.Empty;
        var r = new Rect(Map(b.MinX, b.MinY), Map(b.MaxX, b.MaxY));
        r.Union(Map(b.MinX, b.MaxY));
        r.Union(Map(b.MaxX, b.MinY));
        return r;
    }

    /// <summary>
    /// Rotation that makes most labels read left to right. A plot whose text runs "up" the page
    /// (90 degrees, the usual landscape plotter layout) is turned 90 degrees clockwise.
    /// </summary>
    public static int AutoDegrees(PlotDocument doc) => doc.DominantLabelQuadrant() ?? 0;
}
