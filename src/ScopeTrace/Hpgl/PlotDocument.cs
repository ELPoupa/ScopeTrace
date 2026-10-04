namespace ScopeTrace.Hpgl;

/// <summary>
/// Append-only list of everything an HP-GL stream has drawn so far. Primitives are only ever
/// added, so renderers can draw incrementally by remembering how many they already consumed.
/// </summary>
public sealed class PlotDocument
{
    private readonly List<PlotSegment> _segments = [];
    private readonly List<PlotLabel> _labels = [];
    private readonly HashSet<int> _pens = [];

    public IReadOnlyList<PlotSegment> Segments => _segments;
    public IReadOnlyList<PlotLabel> Labels => _labels;
    public PlotBounds Bounds { get; private set; } = PlotBounds.Empty;
    public bool IsEmpty => _segments.Count == 0 && _labels.Count == 0;

    /// <summary>Pens actually used for drawing (a 1-pen plot is rendered full-bright).</summary>
    public IReadOnlyCollection<int> PensUsed => _pens;

    public void AddSegment(PlotSegment s)
    {
        _segments.Add(s);
        _pens.Add(s.Pen);
        Bounds = Bounds.Include(s.X1, s.Y1).Include(s.X2, s.Y2);
    }

    public void AddLabel(PlotLabel label)
    {
        _labels.Add(label);
        _pens.Add(label.Pen);

        // Include the corners of the text box (baseline origin, along the direction, cap height up).
        double cos = Math.Cos(label.Direction), sin = Math.Sin(label.Direction);
        double len = label.Text.Length * label.Advance;
        double h = label.CharHeight;
        var b = Bounds.Include(label.X, label.Y);
        b = b.Include(label.X + len * cos, label.Y + len * sin);
        b = b.Include(label.X - h * sin, label.Y + h * cos);
        b = b.Include(label.X + len * cos - h * sin, label.Y + len * sin + h * cos);
        Bounds = b;
    }

    /// <summary>
    /// Direction most labels are written in, snapped to a multiple of 90 degrees (0, 90, 180, 270).
    /// Returns null when the plot has no labels.
    /// </summary>
    public int? DominantLabelQuadrant()
    {
        if (_labels.Count == 0)
            return null;

        var counts = new int[4];
        foreach (var l in _labels)
        {
            double deg = l.Direction * 180 / Math.PI;
            int q = (int)Math.Round(deg / 90.0);
            counts[((q % 4) + 4) % 4] += Math.Max(1, l.Text.Length);
        }

        int best = 0;
        for (int i = 1; i < 4; i++)
            if (counts[i] > counts[best])
                best = i;
        return best * 90;
    }
}
