using System.Text.Json;
using System.Text.Json.Serialization;
using ScopeTrace.Export;
using ScopeTrace.Rendering;
using ScopeTrace.Serial;

namespace ScopeTrace.Storage;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Last port used; empty until the user picks one.</summary>
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    public ScopeHandshake Handshake { get; set; } = ScopeHandshake.XonXoff;
    public ThemeKind Theme { get; set; } = ThemeKind.Crt;
    public double IdleTimeoutSeconds { get; set; } = 1.5;
    public bool ListenOnStartup { get; set; }

    /// <summary>Format version; null for files written before 2 (which auto-listened by default).</summary>
    public int? SettingsVersion { get; set; }

    public string ExportFolder { get; set; } = "";
    public ExportFormats ExportFormats { get; set; } = ExportFormats.Png;
    public int PngLongSide { get; set; } = 4000;
    public ExportTheme ExportTheme { get; set; } = ExportTheme.Current;

    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public double SidebarWidth { get; set; } = 270;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScopeTrace", "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings(); // ReadAllText also accepts a BOM
                if (s.SettingsVersion is null)
                    s.ListenOnStartup = false; // v2: never listen on launch unless the user turns it on
                if (s.SettingsVersion is null or < 3)
                    s.ExportFormats = ExportFormats.Png; // v3: export only the PNG unless the user picks more
                s.SettingsVersion = 3;
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        SettingsVersion = 3;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, Json));
    }
}
