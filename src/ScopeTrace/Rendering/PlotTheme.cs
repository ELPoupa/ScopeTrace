using System.Windows.Media;

namespace ScopeTrace.Rendering;

public enum ThemeKind
{
    Crt,
    Paper,
}

/// <summary>Colors and stroke style used to draw a plot.</summary>
public sealed class PlotTheme
{
    public required ThemeKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required Color Background { get; init; }
    public required Color FullBright { get; init; }
    public required Color HalfBright { get; init; }

    /// <summary>Main stroke width as a fraction of the plot's largest dimension.</summary>
    public required double StrokeFactor { get; init; }

    /// <summary>Glow stroke width as a multiple of the main stroke (0 = no glow).</summary>
    public double GlowWidthFactor { get; init; }
    public double GlowOpacity { get; init; }

    public bool HasGlow => GlowWidthFactor > 0 && GlowOpacity > 0;

    public static PlotTheme Crt { get; } = new()
    {
        Kind = ThemeKind.Crt,
        DisplayName = "CRT",
        Background = Color.FromRgb(0x05, 0x0A, 0x06),
        FullBright = Color.FromRgb(0x39, 0xFF, 0x7A),
        HalfBright = Color.FromRgb(0x1F, 0x8F, 0x45),
        StrokeFactor = 1.0 / 750,
        GlowWidthFactor = 3.5,
        GlowOpacity = 0.20,
    };

    public static PlotTheme Paper { get; } = new()
    {
        Kind = ThemeKind.Paper,
        DisplayName = "Paper",
        Background = Colors.White,
        FullBright = Color.FromRgb(0x3A, 0x3A, 0x3A),
        HalfBright = Color.FromRgb(0x8C, 0x8C, 0x8C),
        StrokeFactor = 1.0 / 900,
    };

    public static PlotTheme Get(ThemeKind kind) => kind == ThemeKind.Paper ? Paper : Crt;
}
