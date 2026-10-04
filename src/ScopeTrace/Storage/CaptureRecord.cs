namespace ScopeTrace.Storage;

/// <summary>Metadata saved next to each capture's raw HP-GL bytes.</summary>
public sealed class CaptureRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Clockwise view rotation in degrees, or null to pick it automatically from the label direction.</summary>
    public int? Rotation { get; set; }

    /// <summary>Where it came from, e.g. "COM3 · 9600 baud · XON/XOFF" or "Imported from x.plt".</summary>
    public string Source { get; set; } = "";
}
