using System.Windows;
using System.Windows.Media;
using ScopeTrace.Hpgl;

namespace ScopeTrace.Rendering;

/// <summary>
/// Builds a retained WPF drawing of a plot and extends it incrementally as the interpreter adds
/// primitives, so a plot that is still being received can be redrawn at frame rate no matter
/// how many segments it already has. Must be used on the UI thread.
/// </summary>
public sealed class PlotDrawingBuilder
{
    private readonly PlotDocument _doc;
    private readonly DrawingGroup _glow = new();
    private readonly DrawingGroup _main = new();
    private readonly DrawingGroup _text = new();
    private readonly Dictionary<(Color, double), Pen> _pens = [];
    private readonly Dictionary<Color, Brush> _brushes = [];

    private int _segmentsDone;
    private int _labelsDone;
    private bool _multiPen;
    private double _referenceSize;

    public PlotDrawingBuilder(PlotDocument doc, PlotTheme theme, int rotationDegrees)
    {
        _doc = doc;
        Theme = theme;
        Orientation = new PlotOrientation(rotationDegrees);
        Drawing = new DrawingGroup();
        Drawing.Children.Add(_glow);
        Drawing.Children.Add(_main);
        Drawing.Children.Add(_text);
        Update();
    }

    public PlotTheme Theme { get; }
    public PlotOrientation Orientation { get; }

    /// <summary>The plot in view space (plotter units, Y down, rotated). Background not included.</summary>
    public DrawingGroup Drawing { get; }

    public Rect ViewBounds => Orientation.Map(_doc.Bounds);
    public Rect PageRect => PlotScene.PageRect(ViewBounds);

    /// <summary>Adds primitives that arrived since the last call. Returns true if anything changed.</summary>
    public bool Update()
    {
        bool multiPen = PlotScene.IsMultiPen(_doc);
        double reference = PlotScene.ReferenceSize(ViewBounds);
        bool strokeDrifted = _referenceSize > 0 && (reference > _referenceSize * 1.3 || reference < _referenceSize / 1.3);

        if (multiPen != _multiPen || strokeDrifted || (_referenceSize == 0 && reference > 0 && _segmentsDone > 0))
        {
            // Colors or stroke width changed: redraw everything (still cheap, and rare).
            _glow.Children.Clear();
            _main.Children.Clear();
            _text.Children.Clear();
            _segmentsDone = 0;
            _labelsDone = 0;
        }
        _multiPen = multiPen;
        if (_segmentsDone == 0)
            _referenceSize = reference;

        bool changed = false;
        int segCount = _doc.Segments.Count;
        if (segCount > _segmentsDone)
        {
            AddSegments(_segmentsDone, segCount);
            _segmentsDone = segCount;
            changed = true;
        }

        int labelCount = _doc.Labels.Count;
        if (labelCount > _labelsDone)
        {
            for (int i = _labelsDone; i < labelCount; i++)
                AddLabel(_doc.Labels[i]);
            _labelsDone = labelCount;
            changed = true;
        }
        return changed;
    }

    private void AddSegments(int from, int to)
    {
        double width = PlotScene.StrokeWidth(Theme, _referenceSize);
        var matrix = Orientation.Matrix;

        foreach (var group in PlotScene.Polylines(_doc.Segments, from, to).GroupBy(p => p.Pen))
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var line in group)
                {
                    ctx.BeginFigure(matrix.Transform(line.Points[0]), false, false);
                    for (int i = 1; i < line.Points.Count; i++)
                        ctx.LineTo(matrix.Transform(line.Points[i]), true, true);
                }
            }
            geometry.Freeze();

            var color = PlotScene.PenColor(Theme, group.Key, _multiPen);
            if (Theme.HasGlow)
            {
                var glowColor = Color.FromArgb((byte)(Theme.GlowOpacity * 255), color.R, color.G, color.B);
                _glow.Children.Add(new GeometryDrawing(null, GetPen(glowColor, width * Theme.GlowWidthFactor), geometry));
            }
            _main.Children.Add(new GeometryDrawing(null, GetPen(color, width), geometry));
        }
    }

    private void AddLabel(PlotLabel label)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var g in PlotScene.PlaceLabel(label, Orientation))
        {
            var transformed = g.Glyph.Outline.Clone();
            transformed.Transform = new MatrixTransform(g.Transform);
            group.Children.Add(transformed);
        }
        if (group.Children.Count == 0)
            return;
        group.Freeze();
        _text.Children.Add(new GeometryDrawing(GetBrush(PlotScene.PenColor(Theme, label.Pen, _multiPen)), null, group));
    }

    private Pen GetPen(Color color, double width)
    {
        if (!_pens.TryGetValue((color, width), out var pen))
        {
            pen = new Pen(GetBrush(color), width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            _pens[(color, width)] = pen;
        }
        return pen;
    }

    private Brush GetBrush(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushes[color] = brush;
        }
        return brush;
    }

    /// <summary>Draws the page (background + plot) scaled to fill the given pixel size exactly.</summary>
    public void DrawPage(DrawingContext dc, double pixelWidth, double pixelHeight)
    {
        var page = PageRect;
        dc.DrawRectangle(GetBrush(Theme.Background), null, new Rect(0, 0, pixelWidth, pixelHeight));
        double scale = Math.Min(pixelWidth / page.Width, pixelHeight / page.Height);
        var m = Matrix.Identity;
        m.Translate(-page.X, -page.Y);
        m.Scale(scale, scale);
        m.Translate((pixelWidth - page.Width * scale) / 2, (pixelHeight - page.Height * scale) / 2);
        dc.PushTransform(new MatrixTransform(m));
        dc.DrawDrawing(Drawing);
        dc.Pop();
    }
}
