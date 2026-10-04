using System.Runtime.ExceptionServices;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using ScopeTrace.Export;
using ScopeTrace.Rendering;
using ScopeTrace.Storage;
using ScopeTrace.ViewModels;

namespace ScopeTrace.Tests;

public sealed class ExportAndStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScopeTraceTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>WPF rendering needs a single-threaded apartment.</summary>
    private static void Sta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    private static ExportItem SampleItem()
    {
        byte[] raw = SampleData.Load();
        return new ExportItem("Capture 001", new DateTimeOffset(2026, 10, 4, 15, 30, 0, TimeSpan.Zero),
            raw, CaptureItem.Parse(raw), 90);
    }

    [Fact]
    public void HpglExportIsByteIdentical()
    {
        var item = SampleItem();
        var files = CaptureExporter.Export(item, ExportFormats.Hpgl, PlotTheme.Crt, 2000, _dir);

        var file = Assert.Single(files);
        Assert.Equal(".plt", Path.GetExtension(file));
        Assert.Equal(item.RawBytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void SvgIsWellFormedVectorWithAllStrokesAndGlyphs()
    {
        var item = SampleItem();
        var files = CaptureExporter.Export(item, ExportFormats.Svg, PlotTheme.Paper, 2000, _dir);

        var svg = XDocument.Load(Assert.Single(files));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var root = svg.Root!;
        Assert.Equal(ns + "svg", root.Name);
        Assert.EndsWith("mm", root.Attribute("width")!.Value);

        // Paper theme: no glow, so exactly one stroke path per pen (1 and 2).
        var strokes = root.Descendants(ns + "g").Single().Elements(ns + "path").ToList();
        Assert.Equal(2, strokes.Count);
        int moves = strokes.Sum(p => p.Attribute("d")!.Value.Count(c => c == 'M'));
        int points = strokes.Sum(p => p.Attribute("d")!.Value.Count(c => c is 'M' or 'L'));
        Assert.True(moves > 0);
        // Every segment end point is present: points - moves == number of segments.
        Assert.Equal(item.Document.Segments.Count, points - moves);

        int printable = item.Document.Labels.Sum(l => l.Text.Count(c => !char.IsWhiteSpace(c)));
        Assert.Equal(printable, root.Elements(ns + "use").Count());
        Assert.Equal("#FFFFFF", root.Element(ns + "rect")!.Attribute("fill")!.Value);
    }

    [Fact]
    public void CrtSvgHasGlowLayer()
    {
        string svg = CaptureExporter.BuildSvg(SampleItem().Document, PlotTheme.Crt, 0);
        Assert.Contains("stroke-opacity", svg);
        Assert.Contains("#050A06", svg);
        Assert.Contains("#39FF7A", svg);
        Assert.Contains("#1F8F45", svg); // pen 1 is half-bright in a two-pen plot
    }

    [Fact]
    public void PngHasRequestedResolutionAndRotationSwapsAspect()
    {
        var item = SampleItem();
        Sta(() =>
        {
            var files = CaptureExporter.Export(item, ExportFormats.Png, PlotTheme.Crt, 4000, _dir);
            using var stream = File.OpenRead(Assert.Single(files));
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(4000, Math.Max(frame.PixelWidth, frame.PixelHeight));

            // The sample is taller than wide in plotter space; rotated 90 degrees it is wider than tall.
            Assert.True(frame.PixelWidth > frame.PixelHeight);
            var upright = CaptureExporter.RenderBitmap(item.Document, PlotTheme.Crt, 0, 1000);
            Assert.True(upright.PixelHeight > upright.PixelWidth);

            // Background is the CRT color and some pixels are bright green.
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0)
                .CopyPixels(pixels, frame.PixelWidth * 4, 0);
            Assert.Equal((byte)0x06, pixels[0]); // B (BGRA order)
            Assert.Equal((byte)0x0A, pixels[1]); // G
            Assert.Equal((byte)0x05, pixels[2]); // R
            bool hasTrace = false;
            for (int i = 0; i < pixels.Length && !hasTrace; i += 4)
                hasTrace = pixels[i + 1] > 0xF0 && pixels[i + 2] < 0x60;
            Assert.True(hasTrace);
        });
    }

    [Fact]
    public void ExportNeverOverwrites()
    {
        var item = SampleItem();
        var a = CaptureExporter.Export(item, ExportFormats.Hpgl, PlotTheme.Crt, 2000, _dir);
        var b = CaptureExporter.Export(item, ExportFormats.Hpgl, PlotTheme.Crt, 2000, _dir);

        Assert.NotEqual(a[0], b[0]);
        Assert.EndsWith(" (2).plt", b[0]);
        Assert.Equal("Capture 001 2026-10-04 15-30-00.plt", Path.GetFileName(a[0]));
    }

    [Fact]
    public void SafeFileNameReplacesInvalidCharacters()
    {
        Assert.Equal("a_b_c", CaptureExporter.SafeFileName("a/b:c"));
        Assert.Equal("capture", CaptureExporter.SafeFileName("  ..."));
    }

    [Fact]
    public void StoreRoundTripsBytesAndMetadataNewestFirst()
    {
        var store = new CaptureStore(_dir);
        byte[] raw = SampleData.Load();
        var older = new CaptureRecord { Name = "Old", CreatedAt = DateTimeOffset.Now.AddHours(-1), Source = "COM3" };
        var newer = new CaptureRecord { Name = "New", CreatedAt = DateTimeOffset.Now, Rotation = 270 };
        store.Save(older, raw);
        store.Save(newer, [1, 2, 3]);
        newer.Name = "Renamed";
        store.SaveMetadata(newer);

        var loaded = new CaptureStore(_dir).LoadAll();

        Assert.Equal(["Renamed", "Old"], loaded.Select(l => l.Record.Name));
        Assert.Equal(270, loaded[0].Record.Rotation);
        Assert.Null(loaded[1].Record.Rotation);
        Assert.Equal(raw, loaded[1].Raw);
        Assert.Equal("COM3", loaded[1].Record.Source);

        store.Delete(newer.Id);
        Assert.Single(store.LoadAll());
        Assert.Equal(2, Directory.GetFiles(_dir).Length); // one .plt + one .json left, no temp files
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        string path = Path.Combine(_dir, "settings.json");
        var s = new AppSettings { PortName = "COM7", BaudRate = 19200, Handshake = Serial.ScopeHandshake.Dtr, Theme = ThemeKind.Paper };
        s.Save(path);

        var loaded = AppSettings.Load(path);
        Assert.Equal("COM7", loaded.PortName);
        Assert.Equal(19200, loaded.BaudRate);
        Assert.Equal(Serial.ScopeHandshake.Dtr, loaded.Handshake);
        Assert.Equal(ThemeKind.Paper, loaded.Theme);
        Assert.Contains("\"Dtr\"", File.ReadAllText(path));
        var fresh = AppSettings.Load(Path.Combine(_dir, "missing.json"));
        Assert.Equal("", fresh.PortName);       // no port chosen by default
        Assert.False(fresh.ListenOnStartup);   // not listening by default
    }

    [Fact]
    public void OldSettingsStopAutoListeningButKeepChoices()
    {
        string path = Path.Combine(_dir, "old.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ \"PortName\": \"COM3\", \"BaudRate\": 19200, \"ListenOnStartup\": true }");

        var s = AppSettings.Load(path);
        Assert.Equal("COM3", s.PortName);
        Assert.Equal(19200, s.BaudRate);
        Assert.False(s.ListenOnStartup);

        // Once saved by this version, an explicit choice to listen on startup is kept.
        s.ListenOnStartup = true;
        s.Save(path);
        Assert.True(AppSettings.Load(path).ListenOnStartup);
    }
}

public sealed class ExportDefaultsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScopeTraceTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ExportDefaultsToPngOnlyAndOlderSettingsAreResetOnce()
    {
        Assert.Equal(ExportFormats.Png, new AppSettings().ExportFormats);

        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "v2.json");
        File.WriteAllText(path, "{ \"SettingsVersion\": 2, \"ExportFormats\": \"Png, Svg, Hpgl\" }");
        var s = AppSettings.Load(path);
        Assert.Equal(ExportFormats.Png, s.ExportFormats);

        // A choice made with this version is remembered.
        s.ExportFormats = ExportFormats.Png | ExportFormats.Svg;
        s.Save(path);
        Assert.Equal(ExportFormats.Png | ExportFormats.Svg, AppSettings.Load(path).ExportFormats);
    }
}
