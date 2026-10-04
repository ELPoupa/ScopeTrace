namespace ScopeTrace.Hpgl;

/// <summary>A straight pen stroke in plotter units (1 pu = 0.025 mm, Y axis up).</summary>
public readonly record struct PlotSegment(double X1, double Y1, double X2, double Y2, int Pen);

/// <summary>
/// One line of label text. Characters sit on a fixed HP-GL cell grid: character i starts at
/// (X, Y) + i * advance along <see cref="Direction"/>, where advance = 1.5 * CharWidth.
/// </summary>
public sealed record PlotLabel(
    double X,
    double Y,
    string Text,
    int Pen,
    double CharWidth,
    double CharHeight,
    double Direction)
{
    public double Advance => CharWidth * 1.5;
}

/// <summary>Axis-aligned bounding box in plotter units.</summary>
public readonly record struct PlotBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly PlotBounds Empty = new(double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;

    public PlotBounds Include(double x, double y) =>
        new(Math.Min(MinX, x), Math.Min(MinY, y), Math.Max(MaxX, x), Math.Max(MaxY, y));
}
