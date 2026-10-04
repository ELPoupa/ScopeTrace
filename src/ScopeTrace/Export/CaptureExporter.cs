using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScopeTrace.Hpgl;
using ScopeTrace.Rendering;

namespace ScopeTrace.Export;

[Flags]
public enum ExportFormats
{
    None = 0,
    Png = 1,
    Svg = 2,
    Hpgl = 4,
}

public enum ExportTheme
{
    Current,
    Crt,
    Paper,
}

/// <summary>Everything an export needs to know about one capture.</summary>
public sealed record ExportItem(string Name, DateTimeOffset CreatedAt, byte[] RawBytes, PlotDocument Document, int RotationDegrees);

/// <summary>
/// Lossless exports: the original HP-GL bytes, a true-vector SVG and a high-resolution PNG
/// rendered from the vectors. Must run on an STA thread (WPF rendering).
/// </summary>
public static class CaptureExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static List<string> Export(ExportItem item, ExportFormats formats, PlotTheme theme, int pngLongSide, string folder)
    {
        Directory.CreateDirectory(folder);
        string baseName = SafeFileName($"{item.Name} {item.CreatedAt:yyyy-MM-dd HH-mm-ss}");
        var written = new List<string>();

        if (formats.HasFlag(ExportFormats.Hpgl))
        {
            string path = UniquePath(folder, baseName, ".plt");
            File.WriteAllBytes(path, item.RawBytes); // byte-for-byte what the scope sent
            written.Add(path);
        }
        if (formats.HasFlag(ExportFormats.Svg))
        {
            string path = UniquePath(folder, baseName, ".svg");
            File.WriteAllText(path, BuildSvg(item.Document, theme, item.RotationDegrees), new UTF8Encoding(false));
            written.Add(path);
        }
        if (formats.HasFlag(ExportFormats.Png))
        {
            string path = UniquePath(folder, baseName, ".png");
            var bitmap = RenderBitmap(item.Document, theme, item.RotationDegrees, pngLongSide);
            using var stream = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            written.Add(path);
        }
        return written;
    }

    /// <summary>Renders the page (background + plot) so its longer side is exactly <paramref name="longSide"/> pixels.</summary>
    public static BitmapSource RenderBitmap(PlotDocument doc, PlotTheme theme, int rotationDegrees, int longSide)
    {
        var builder = new PlotDrawingBuilder(doc, theme, rotationDegrees);
        var page = builder.PageRect;
        int w, h;
        if (page.Width >= page.Height)
        {
            w = longSide;
            h = Math.Max(1, (int)Math.Round(longSide * page.Height / page.Width));
        }
        else
        {
            h = longSide;
            w = Math.Max(1, (int)Math.Round(longSide * page.Width / page.Height));
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            builder.DrawPage(dc, w, h);

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Builds a vector SVG: strokes as paths (one per pen), labels as glyph outlines. The page is
    /// sized in millimetres at the plotter's true scale (1 plotter unit = 0.025 mm).
    /// </summary>
    public static string BuildSvg(PlotDocument doc, PlotTheme theme, int rotationDegrees)
    {
        var orientation = new PlotOrientation(rotationDegrees);
        var viewBounds = orientation.Map(doc.Bounds);
        var page = PlotScene.PageRect(viewBounds);
        double stroke = PlotScene.StrokeWidth(theme, PlotScene.ReferenceSize(viewBounds));
        bool multiPen = PlotScene.IsMultiPen(doc);

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append(Inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(page.Width * 0.025)}mm\" height=\"{F(page.Height * 0.025)}mm\" ");
        sb.Append(Inv, $"viewBox=\"{F(page.X)} {F(page.Y)} {F(page.Width)} {F(page.Height)}\">\n");
        sb.Append("<title>HP ScopeTrace plot</title>\n");
        sb.Append(Inv, $"<rect x=\"{F(page.X)}\" y=\"{F(page.Y)}\" width=\"{F(page.Width)}\" height=\"{F(page.Height)}\" fill=\"{Hex(theme.Background)}\"/>\n");

        // Path data per pen, in view space.
        var penPaths = new SortedDictionary<int, StringBuilder>();
        foreach (var line in PlotScene.Polylines(doc.Segments, 0, doc.Segments.Count))
        {
            if (!penPaths.TryGetValue(line.Pen, out var d))
                penPaths[line.Pen] = d = new StringBuilder();
            for (int i = 0; i < line.Points.Count; i++)
            {
                var p = orientation.Matrix.Transform(line.Points[i]);
                d.Append(i == 0 ? 'M' : 'L').Append(F(p.X)).Append(' ').Append(F(p.Y));
            }
        }

        sb.Append("<g fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">\n");
        if (theme.HasGlow)
        {
            foreach (var (pen, d) in penPaths)
                sb.Append(Inv, $"<path stroke=\"{Hex(PlotScene.PenColor(theme, pen, multiPen))}\" stroke-opacity=\"{F(theme.GlowOpacity)}\" stroke-width=\"{F(stroke * theme.GlowWidthFactor)}\" d=\"{d}\"/>\n");
        }
        foreach (var (pen, d) in penPaths)
            sb.Append(Inv, $"<path stroke=\"{Hex(PlotScene.PenColor(theme, pen, multiPen))}\" stroke-width=\"{F(stroke)}\" d=\"{d}\"/>\n");
        sb.Append("</g>\n");

        // Labels: each character's outline is defined once and placed with a transform.
        var used = new Dictionary<string, string>();
        var uses = new StringBuilder();
        foreach (var label in doc.Labels)
        {
            string fill = Hex(PlotScene.PenColor(theme, label.Pen, multiPen));
            foreach (var g in PlotScene.PlaceLabel(label, orientation))
            {
                if (!used.TryGetValue(g.Glyph.SvgPath, out var id))
                    used[g.Glyph.SvgPath] = id = "g" + used.Count.ToString(Inv);
                var m = g.Transform;
                uses.Append(Inv, $"<use href=\"#{id}\" fill=\"{fill}\" transform=\"matrix({F(m.M11)} {F(m.M12)} {F(m.M21)} {F(m.M22)} {F(m.OffsetX)} {F(m.OffsetY)})\"/>\n");
            }
        }
        if (used.Count > 0)
        {
            sb.Append("<defs>\n");
            foreach (var (path, id) in used)
                sb.Append(Inv, $"<path id=\"{id}\" d=\"{path}\"/>\n");
            sb.Append("</defs>\n");
            sb.Append(uses);
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        string result = sb.ToString().Trim().TrimEnd('.');
        return result.Length == 0 ? "capture" : result;
    }

    /// <summary>Never overwrites: "name.png", then "name (2).png", "name (3).png", ...</summary>
    public static string UniquePath(string folder, string baseName, string extension)
    {
        string path = Path.Combine(folder, baseName + extension);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{baseName} ({i}){extension}");
        return path;
    }

    private static string F(double v) => v.ToString("0.###", Inv);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
