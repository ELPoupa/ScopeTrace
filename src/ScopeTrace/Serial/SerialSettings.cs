namespace ScopeTrace.Serial;

/// <summary>Handshake modes offered by the 54600-series RS-232 modules (I/O Menu, "Handshake").</summary>
public enum ScopeHandshake
{
    /// <summary>Software flow control (XON/XOFF characters).</summary>
    XonXoff,

    /// <summary>Hardware flow control ("DTR" on the scope; the PC keeps DTR asserted and drives RTS).</summary>
    Dtr,
}

public sealed record SerialSettings(string PortName, int BaudRate, ScopeHandshake Handshake)
{
    /// <summary>Baud rates the scope supports (scope default: 9600).</summary>
    public static IReadOnlyList<int> SupportedBaudRates { get; } = [1200, 2400, 9600, 19200];

    public static string Describe(ScopeHandshake h) => h == ScopeHandshake.Dtr ? "DTR (hardware)" : "XON/XOFF";

    public override string ToString() => $"{PortName} · {BaudRate} baud · {Describe(Handshake)}";
}
