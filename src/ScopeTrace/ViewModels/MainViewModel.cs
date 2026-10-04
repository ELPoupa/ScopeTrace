using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using ScopeTrace.Capture;
using ScopeTrace.Export;
using ScopeTrace.Rendering;
using ScopeTrace.Serial;
using ScopeTrace.Storage;

namespace ScopeTrace.ViewModels;

public sealed record HandshakeOption(ScopeHandshake Value, string Label);

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ThumbnailInterval = TimeSpan.FromMilliseconds(300);

    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly CaptureStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly ScopeListener _listener = new();
    private readonly CaptureSession _session = new();
    private readonly DispatcherTimer _timer;

    private CaptureItem? _selected;
    private CaptureItem? _live;
    private bool _liveDirty;
    private DateTime _lastLiveThumbnail;
    private PlotDrawingBuilder? _plotBuilder;
    private string? _selectedPort;
    private int _selectedBaud;
    private HandshakeOption _selectedHandshake;
    private bool _isListening;
    private ThemeKind _theme;
    private ListenerState _state = ListenerState.Stopped;
    private string _connectionText = "Stopped";
    private string _activityText = "";
    private ReplayFeeder? _replay;

    public MainViewModel(AppSettings settings, string settingsPath, CaptureStore store, Dispatcher dispatcher)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _store = store;
        _dispatcher = dispatcher;

        _theme = settings.Theme;
        _selectedBaud = SerialSettings.SupportedBaudRates.Contains(settings.BaudRate) ? settings.BaudRate : 9600;
        _selectedHandshake = Handshakes.First(h => h.Value == settings.Handshake);
        _selectedPort = string.IsNullOrWhiteSpace(settings.PortName) ? null : settings.PortName;
        _session.IdleTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.IdleTimeoutSeconds, 0.5, 10));

        _session.Started += OnCaptureStarted;
        _session.Updated += _ => _liveDirty = true;
        _session.Completed += OnCaptureCompleted;
        _session.Discarded += OnCaptureDiscarded;
        _session.Resumed += OnCaptureResumed;

        _listener.DataReceived += data => _dispatcher.BeginInvoke(() => _session.Feed(data, DateTime.UtcNow));
        _listener.StateChanged += (state, text) => _dispatcher.BeginInvoke(() => OnListenerState(state, text));

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => OnTick(), dispatcher);
        _timer.Start();

        RefreshPorts();
    }

    // ------------------------------------------------------------------ bindable state

    public ObservableCollection<CaptureItem> Captures { get; } = [];
    public ObservableCollection<string> Ports { get; } = [];
    public IReadOnlyList<int> BaudRates => SerialSettings.SupportedBaudRates;

    public IReadOnlyList<HandshakeOption> Handshakes { get; } =
    [
        new(ScopeHandshake.XonXoff, "XON/XOFF"),
        new(ScopeHandshake.Dtr, "DTR (hardware)"),
    ];

    /// <summary>Raised when the plot shown in the main view grew and must be redrawn.</summary>
    public event Action? PlotUpdated;

    public CaptureItem? SelectedCapture
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                UpdatePlotBuilder();
        }
    }

    public PlotDrawingBuilder? PlotBuilder
    {
        get => _plotBuilder;
        private set => Set(ref _plotBuilder, value);
    }

    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (Set(ref _selectedPort, value) && value is not null)
                SerialSettingsChanged();
        }
    }

    public int SelectedBaud
    {
        get => _selectedBaud;
        set
        {
            if (Set(ref _selectedBaud, value))
                SerialSettingsChanged();
        }
    }

    public HandshakeOption SelectedHandshake
    {
        get => _selectedHandshake;
        set
        {
            if (value is not null && Set(ref _selectedHandshake, value))
                SerialSettingsChanged();
        }
    }

    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (value)
                StartListening();
            else
                StopListening();
        }
    }

    /// <summary>Port, baud rate, handshake and app settings are locked while listening.</summary>
    public bool CanChangeSettings => !_isListening;

    public ThemeKind Theme
    {
        get => _theme;
        set
        {
            if (!Set(ref _theme, value))
                return;
            _settings.Theme = value;
            SaveSettings();
            OnPropertyChanged(nameof(IsCrtTheme));
            OnPropertyChanged(nameof(IsPaperTheme));
            OnPropertyChanged(nameof(CurrentTheme));
            UpdatePlotBuilder();
            foreach (var c in Captures)
            {
                c.InvalidateDrawing();
                QueueThumbnail(c);
            }
        }
    }

    public bool IsCrtTheme
    {
        get => _theme == ThemeKind.Crt;
        set { if (value) Theme = ThemeKind.Crt; }
    }

    public bool IsPaperTheme
    {
        get => _theme == ThemeKind.Paper;
        set { if (value) Theme = ThemeKind.Paper; }
    }

    public PlotTheme CurrentTheme => PlotTheme.Get(_theme);

    public ListenerState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public string ConnectionText
    {
        get => _connectionText;
        private set => Set(ref _connectionText, value);
    }

    public string ActivityText
    {
        get => _activityText;
        private set => Set(ref _activityText, value);
    }

    public string CountText => Captures.Count == 1 ? "1 capture" : $"{Captures.Count} captures";

    public string Placeholder => _selectedPort is null
        ? "Choose the serial port the scope is connected to, set the baud rate and handshake\nto match the scope (Print/Utility → I/O Menu), then press Listen."
        : _isListening
        ? $"Waiting for the scope on {_selectedPort}…\nPress Print Screen on the 54600B (Print/Utility menu)."
        : "Not listening.\nChoose the port, baud rate and handshake that match the scope, then press Listen.";

    public AppSettings Settings => _settings;

    // ------------------------------------------------------------------ startup / shutdown

    public void LoadSavedCaptures()
    {
        foreach (var (record, raw) in _store.LoadAll())
        {
            var item = CaptureItem.FromBytes(record, raw);
            if (item.Document.IsEmpty)
                continue;
            Captures.Add(item);
            QueueThumbnail(item);
        }
        OnPropertyChanged(nameof(CountText));
        SelectedCapture = Captures.FirstOrDefault();
        ActivityText = Captures.Count > 0 ? $"Loaded {CountText}" : "";
    }

    public void SaveSettings()
    {
        _settings.PortName = _selectedPort ?? _settings.PortName;
        _settings.BaudRate = _selectedBaud;
        _settings.Handshake = _selectedHandshake.Value;
        _settings.Theme = _theme;
        try
        {
            _settings.Save(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _listener.Dispose();
        _session.EndCurrent();
    }

    // ------------------------------------------------------------------ serial

    public void RefreshPorts()
    {
        var names = System.IO.Ports.SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n.Length).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? keep = _selectedPort;
        if (keep is not null && !names.Contains(keep, StringComparer.OrdinalIgnoreCase))
            names.Add(keep); // remember the configured port even while it is unplugged
        Ports.Clear();
        foreach (var n in names)
            Ports.Add(n);
        _selectedPort = keep; // no port is chosen until the user picks one
        OnPropertyChanged(nameof(SelectedPort));
    }

    public void StartListening()
    {
        if (string.IsNullOrWhiteSpace(_selectedPort))
        {
            ConnectionText = "Choose a serial port first";
            OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(CanChangeSettings));
            return;
        }
        _isListening = true;
        _listener.Start(new SerialSettings(_selectedPort, _selectedBaud, _selectedHandshake.Value));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(CanChangeSettings));
        OnPropertyChanged(nameof(Placeholder));
    }

    public void StopListening()
    {
        _isListening = false;
        _listener.Stop();
        _session.EndCurrent();
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(CanChangeSettings));
        OnPropertyChanged(nameof(Placeholder));
    }

    private void SerialSettingsChanged()
    {
        SaveSettings(); // remember the last choice right away
        if (_isListening)
            _listener.Start(new SerialSettings(_selectedPort!, _selectedBaud, _selectedHandshake.Value));
        OnPropertyChanged(nameof(Placeholder));
    }

    private void OnListenerState(ListenerState state, string text)
    {
        // Ignore the "Stopped" event that is raised while switching settings.
        if (state == ListenerState.Stopped && _isListening)
            return;
        State = state;
        ConnectionText = text;
    }

    // ------------------------------------------------------------------ captures

    private void OnCaptureStarted(CaptureStream stream)
    {
        var record = new CaptureRecord
        {
            Name = NextCaptureName(),
            CreatedAt = DateTimeOffset.Now,
            Source = _replay is not null
                ? $"Replay of {Path.GetFileName(_replay.Path)}"
                : _listener.Settings?.ToString() ?? "",
        };
        var item = CaptureItem.FromLive(record, stream);
        item.AutoRotationHint = Captures.FirstOrDefault(c => !c.IsReceiving)?.RotationDegrees ?? 0;
        _live = item;
        _liveDirty = true;
        Captures.Insert(0, item);
        OnPropertyChanged(nameof(CountText));
        SelectedCapture = item; // every new transfer is shown immediately
        ActivityText = $"Receiving {record.Name}…";
    }

    private void OnCaptureCompleted(CaptureStream stream)
    {
        var item = _live;
        if (item is null)
            return;
        _live = null;
        item.Complete();
        try
        {
            _store.Save(item.Record, item.RawBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityText = $"Received {item.Name}, but it could not be saved: {ex.Message}";
        }
        if (!ActivityText.Contains("could not be saved"))
            ActivityText = $"Received {item.Name} · {FormatBytes(item.ByteCount)} · {item.Document.Segments.Count} strokes";
        item.RefreshThumbnail(CurrentTheme);
        if (SelectedCapture == item)
            UpdatePlotBuilder();
    }

    private void OnCaptureResumed(CaptureStream stream)
    {
        var item = Captures.FirstOrDefault(c => c.Stream == stream);
        if (item is null)
        {
            OnCaptureStarted(stream); // the paused capture was deleted meanwhile
            return;
        }
        item.Resume();
        _live = item;
        _liveDirty = true;
        SelectedCapture = item;
        ActivityText = $"The scope paused; continuing {item.Name}…";
    }

    private void OnCaptureDiscarded(CaptureStream stream)
    {
        if (_live is { } item)
        {
            _live = null;
            int index = Captures.IndexOf(item);
            Captures.Remove(item);
            OnPropertyChanged(nameof(CountText));
            if (SelectedCapture is null && Captures.Count > 0)
                SelectedCapture = Captures[Math.Min(index, Captures.Count - 1)];
            ActivityText = "Ignored a transfer that contained no drawing";
        }
    }

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        _replay?.Pump(now, _session);
        if (_replay is { IsFinished: true })
        {
            _replay = null;
            OnPropertyChanged(nameof(IsReplaying));
        }
        _session.Tick(now);

        if (_live is { } live && _liveDirty)
        {
            _liveDirty = false;
            live.NotifyLiveChanged();
            if (SelectedCapture == live)
            {
                var builder = live.GetBuilder(CurrentTheme);
                if (builder != PlotBuilder)
                    PlotBuilder = builder;
                PlotUpdated?.Invoke();
            }
            if (now - _lastLiveThumbnail > ThumbnailInterval)
            {
                _lastLiveThumbnail = now;
                live.RefreshThumbnail(CurrentTheme);
            }
            ActivityText = $"Receiving {live.Name} · {FormatBytes(live.ByteCount)}";
        }
    }

    private void UpdatePlotBuilder()
    {
        PlotBuilder = _selected?.GetBuilder(CurrentTheme);
        PlotUpdated?.Invoke();
    }

    private void QueueThumbnail(CaptureItem item) =>
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => item.RefreshThumbnail(CurrentTheme));

    [GeneratedRegex(@"^Capture (\d+)$")]
    private static partial Regex CaptureNumber();

    private string NextCaptureName()
    {
        int max = 0;
        foreach (var c in Captures)
            if (CaptureNumber().Match(c.Name) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int n))
                max = Math.Max(max, n);
        return $"Capture {max + 1:000}";
    }

    public void Rename(CaptureItem item, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == item.Name)
            return;
        item.Name = name;
        SaveMetadata(item);
    }

    public void SetRotation(IEnumerable<CaptureItem> items, int? degrees)
    {
        foreach (var item in items)
        {
            item.RotationSetting = degrees;
            item.RefreshThumbnail(CurrentTheme);
            SaveMetadata(item);
        }
        UpdatePlotBuilder();
    }

    public void Rotate(IEnumerable<CaptureItem> items, int delta)
    {
        foreach (var item in items)
        {
            item.RotationSetting = ((item.RotationDegrees + delta) % 360 + 360) % 360;
            item.RefreshThumbnail(CurrentTheme);
            SaveMetadata(item);
        }
        UpdatePlotBuilder();
    }

    private void SaveMetadata(CaptureItem item)
    {
        if (item.IsReceiving)
            return; // saved when the transfer completes
        try
        {
            _store.SaveMetadata(item.Record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityText = $"Could not save changes to {item.Name}: {ex.Message}";
        }
    }

    public void Delete(IReadOnlyList<CaptureItem> items)
    {
        int firstIndex = items.Select(i => Captures.IndexOf(i)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        foreach (var item in items)
        {
            if (item == _live)
                _session.EndCurrent(); // abandon the running transfer first
            if (item.Stream is not null)
                _session.ForgetPrevious(item.Stream);
            Captures.Remove(item);
            try
            {
                _store.Delete(item.Record.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActivityText = $"Could not delete the files of {item.Name}: {ex.Message}";
            }
        }
        OnPropertyChanged(nameof(CountText));
        if (SelectedCapture is null || !Captures.Contains(SelectedCapture))
            SelectedCapture = Captures.Count > 0 ? Captures[Math.Min(firstIndex, Captures.Count - 1)] : null;
        ActivityText = items.Count == 1 ? $"Deleted {items[0].Name}" : $"Deleted {items.Count} captures";
    }

    /// <summary>Imports HP-GL files; a file containing several plots becomes several captures.</summary>
    public int Import(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActivityText = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
                continue;
            }

            var session = new CaptureSession();
            var finished = new List<CaptureStream>();
            session.Completed += finished.Add;
            session.Feed(data, DateTime.UtcNow);
            session.EndCurrent();

            var created = File.GetLastWriteTime(path);
            for (int i = 0; i < finished.Count; i++)
            {
                string name = Path.GetFileNameWithoutExtension(path) + (finished.Count > 1 ? $" ({i + 1})" : "");
                var record = new CaptureRecord
                {
                    Name = name,
                    CreatedAt = created,
                    Source = $"Imported from {Path.GetFileName(path)}",
                };
                var item = CaptureItem.FromBytes(record, finished[i].RawBytes);
                try
                {
                    _store.Save(record, item.RawBytes);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ActivityText = $"Imported {name}, but it could not be saved: {ex.Message}";
                }
                Captures.Insert(0, item);
                QueueThumbnail(item);
                added++;
            }
        }
        OnPropertyChanged(nameof(CountText));
        if (added > 0)
        {
            SelectedCapture = Captures[0];
            ActivityText = added == 1 ? "Imported 1 plot" : $"Imported {added} plots";
        }
        else if (!ActivityText.StartsWith("Could not"))
            ActivityText = "No HP-GL drawing found in the selected file(s)";
        return added;
    }

    public void SetIdleTimeout(double seconds)
    {
        _settings.IdleTimeoutSeconds = Math.Clamp(seconds, 0.5, 10);
        _session.IdleTimeout = TimeSpan.FromSeconds(_settings.IdleTimeoutSeconds);
    }

    // ------------------------------------------------------------------ replay (scope simulator)

    public bool IsReplaying => _replay is not null;

    public void StartReplay(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        _replay = new ReplayFeeder(path, data, _selectedBaud);
        OnPropertyChanged(nameof(IsReplaying));
        ActivityText = $"Replaying {Path.GetFileName(path)} at {_selectedBaud} baud…";
    }

    public void StopReplay()
    {
        _replay = null;
        OnPropertyChanged(nameof(IsReplaying));
    }

    // ------------------------------------------------------------------ export

    public List<string> Export(IReadOnlyList<CaptureItem> items, ExportFormats formats, ExportTheme exportTheme, int pngLongSide, string folder)
    {
        var theme = exportTheme switch
        {
            ExportTheme.Crt => PlotTheme.Crt,
            ExportTheme.Paper => PlotTheme.Paper,
            _ => CurrentTheme,
        };
        var files = new List<string>();
        foreach (var item in items)
            files.AddRange(CaptureExporter.Export(item.ToExportItem(), formats, theme, pngLongSide, folder));
        return files;
    }

    public void ReportActivity(string text) => ActivityText = text;

    public static string FormatBytes(long n) => n < 1024 ? $"{n} B" : $"{n / 1024.0:0.0} KB";
}
