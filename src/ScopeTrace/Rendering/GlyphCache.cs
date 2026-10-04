using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace ScopeTrace.Rendering;

/// <summary>
/// Vector outlines of a monospace font, used to draw HP-GL labels as exact geometry on screen
/// and in SVG/PNG exports (so text looks identical everywhere and never depends on fonts
/// installed on the viewer's machine).
/// </summary>
public static class GlyphCache
{
    public sealed record Glyph(Geometry Outline, string SvgPath);

    private static readonly object Gate = new();
    private static readonly Dictionary<char, Glyph?> Cache = [];
    private static readonly GlyphTypeface Face = LoadFace();

    /// <summary>Advance width of every glyph (monospace), in em.</summary>
    public static double AdvanceEm { get; } = Face.AdvanceWidths[Face.CharacterToGlyphMap['M']];

    /// <summary>Capital letter height, in em.</summary>
    public static double CapHeightEm { get; } = Face.CapsHeight > 0 ? Face.CapsHeight : 0.64;

    private static GlyphTypeface LoadFace()
    {
        foreach (var family in new[] { "Consolas", "Lucida Console", "Courier New" })
        {
            if (new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
                    .TryGetGlyphTypeface(out var face))
                return face;
        }
        throw new InvalidOperationException("No monospace font available to draw plot labels.");
    }

    /// <summary>Outline of one character at 1 em, baseline at y=0, Y down. Null for blanks.</summary>
    public static Glyph? Get(char c)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(c, out var cached))
                return cached;

            Glyph? glyph = null;
            var map = Face.CharacterToGlyphMap;
            if (!char.IsWhiteSpace(c) && (map.TryGetValue(c, out ushort index) || map.TryGetValue('?', out index)))
            {
                var outline = Face.GetGlyphOutline(index, 1.0, 1.0);
                if (!outline.IsEmpty())
                {
                    var path = PathGeometry.CreateFromGeometry(outline);
                    path.Freeze();
                    glyph = new Glyph(path, ToSvgPath(path));
                }
            }
            Cache[c] = glyph;
            return glyph;
        }
    }

    /// <summary>Serializes a geometry as SVG path data (invariant culture, full precision).</summary>
    public static string ToSvgPath(PathGeometry geometry)
    {
        var sb = new StringBuilder();
        foreach (var figure in geometry.Figures)
        {
            sb.Append('M').Append(P(figure.StartPoint));
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment l:
                        sb.Append('L').Append(P(l.Point));
                        break;
                    case PolyLineSegment pl:
                        foreach (var p in pl.Points)
                            sb.Append('L').Append(P(p));
                        break;
                    case BezierSegment b:
                        sb.Append('C').Append(P(b.Point1)).Append(' ').Append(P(b.Point2)).Append(' ').Append(P(b.Point3));
                        break;
                    case PolyBezierSegment pb:
                        for (int i = 0; i + 2 < pb.Points.Count; i += 3)
                            sb.Append('C').Append(P(pb.Points[i])).Append(' ').Append(P(pb.Points[i + 1])).Append(' ').Append(P(pb.Points[i + 2]));
                        break;
                    case QuadraticBezierSegment q:
                        sb.Append('Q').Append(P(q.Point1)).Append(' ').Append(P(q.Point2));
                        break;
                    case PolyQuadraticBezierSegment pq:
                        for (int i = 0; i + 1 < pq.Points.Count; i += 2)
                            sb.Append('Q').Append(P(pq.Points[i])).Append(' ').Append(P(pq.Points[i + 1]));
                        break;
                    default:
                        // Anything exotic (arcs) is flattened to lines.
                        var flat = new PathGeometry([new PathFigure(figure.StartPoint, [segment.CloneCurrentValue()], false)])
                            .GetFlattenedPathGeometry(1e-4, ToleranceType.Absolute);
                        foreach (var f in flat.Figures)
                            foreach (var s in f.Segments.OfType<PolyLineSegment>())
                                foreach (var p in s.Points)
                                    sb.Append('L').Append(P(p));
                        break;
                }
            }
            if (figure.IsClosed)
                sb.Append('Z');
        }
        return sb.ToString();
    }

    private static string P(Point p) =>
        p.X.ToString("0.#####", CultureInfo.InvariantCulture) + " " + p.Y.ToString("0.#####", CultureInfo.InvariantCulture);
}
