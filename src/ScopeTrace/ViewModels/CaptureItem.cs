using System.Windows.Media;
using ScopeTrace.Capture;
using ScopeTrace.Export;
using ScopeTrace.Hpgl;
using ScopeTrace.Rendering;
using ScopeTrace.Storage;

namespace ScopeTrace.ViewModels;

/// <summary>One entry of the captures list: a plot being received or a finished one.</summary>
public sealed class CaptureItem : ObservableObject
{
    private CaptureStream? _live;
    private readonly CaptureStream? _stream;
    private byte[] _raw;
    private PlotDocument _document;
    private ImageSource? _thumbnail;
    private PlotDrawingBuilder? _builder;

    private CaptureItem(CaptureRecord record, PlotDocument document, byte[] raw, CaptureStream? live)
    {
        Record = record;
        _document = document;
        _raw = raw;
        _live = live;
        _stream = live;
    }

    /// <summary>A capture that is still arriving.</summary>
    public static CaptureItem FromLive(CaptureRecord record, CaptureStream stream) =>
        new(record, stream.Document, [], stream);

    /// <summary>A finished capture (loaded from disk or imported).</summary>
    public static CaptureItem FromBytes(CaptureRecord record, byte[] raw) => new(record, Parse(raw), raw, null);

    public static PlotDocument Parse(byte[] raw)
    {
        var interpreter = new HpglInterpreter();
        interpreter.Feed(raw);
        interpreter.Flush();
        return interpreter.Document;
    }

    public CaptureRecord Record { get; }

    /// <summary>The serial transfer this capture came from (null for loaded or imported captures).</summary>
    public CaptureStream? Stream => _stream;
    public PlotDocument Document => _document;
    public byte[] RawBytes => _live?.RawBytes ?? _raw;
    public long ByteCount => _live?.ByteCount ?? _raw.LongLength;
    public bool IsReceiving => _live is not null;

    public string Name
    {
        get => Record.Name;
        set
        {
            if (Record.Name == value)
                return;
            Record.Name = value;
            OnPropertyChanged();
        }
    }

    public string Details
    {
        get
        {
            string size = ByteCount < 1024 ? $"{ByteCount} B" : $"{ByteCount / 1024.0:0.0} KB";
            return IsReceiving ? $"Receiving… {size}" : $"{Record.CreatedAt:dd MMM yyyy HH:mm:ss} · {size}";
        }
    }

    public string ToolTipText => $"{Record.Name}\n{Record.CreatedAt:F}\n{Record.Source}\n{Document.Segments.Count} strokes, {Document.Labels.Count} labels";

    /// <summary>Null = automatic (follows the label direction).</summary>
    public int? RotationSetting
    {
        get => Record.Rotation;
        set
        {
            Record.Rotation = value;
            _builder = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotationDegrees));
        }
    }

    /// <summary>Rotation to use for automatic mode before any label has arrived (from the previous capture).</summary>
    public int AutoRotationHint { get; set; }

    public int RotationDegrees => Record.Rotation ?? Document.DominantLabelQuadrant() ?? AutoRotationHint;

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => Set(ref _thumbnail, value);
    }

    /// <summary>Drawing for the main view, created on demand and extended while receiving.</summary>
    public PlotDrawingBuilder GetBuilder(PlotTheme theme)
    {
        if (_builder is null || _builder.Theme != theme || _builder.Orientation.Degrees != RotationDegrees)
            _builder = new PlotDrawingBuilder(Document, theme, RotationDegrees);
        else
            _builder.Update();
        return _builder;
    }

    public void InvalidateDrawing() => _builder = null;

    public void RefreshThumbnail(PlotTheme theme)
    {
        Thumbnail = Document.IsEmpty ? null : CaptureExporter.RenderBitmap(Document, theme, RotationDegrees, 320);
    }

    /// <summary>Called when the transfer ends: keeps the final bytes and drops the live stream.</summary>
    public void Complete()
    {
        if (_live is null)
            return;
        _raw = _live.RawBytes;
        _document = _live.Document;
        _live = null;
        _builder = null; // rebuild once with final stroke widths
        NotifyLiveChanged();
    }

    /// <summary>The scope paused mid-plot and is sending the rest: receive into this capture again.</summary>
    public void Resume()
    {
        if (_stream is null || _live is not null)
            return;
        _live = _stream;
        NotifyLiveChanged();
    }

    public void NotifyLiveChanged()
    {
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(IsReceiving));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(RotationDegrees));
    }

    public ExportItem ToExportItem() => new(Name, Record.CreatedAt, RawBytes, Document, RotationDegrees);
}
