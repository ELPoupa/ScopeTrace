using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScopeTrace.Rendering;

namespace ScopeTrace.Controls;

/// <summary>
/// Displays one plot, always fitted to the control. Call <see cref="Refresh"/> when the plot grows.
/// </summary>
public sealed class PlotView : FrameworkElement
{
    public static readonly DependencyProperty BuilderProperty = DependencyProperty.Register(
        nameof(Builder), typeof(PlotDrawingBuilder), typeof(PlotView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((PlotView)d).InvalidateVisual()));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(PlotView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(PlotTheme), typeof(PlotView),
        new FrameworkPropertyMetadata(PlotTheme.Crt, FrameworkPropertyMetadataOptions.AffectsRender));

    public PlotView()
    {
        ClipToBounds = true;
    }

    public PlotDrawingBuilder? Builder
    {
        get => (PlotDrawingBuilder?)GetValue(BuilderProperty);
        set => SetValue(BuilderProperty, value);
    }

    /// <summary>Text shown when there is no plot.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Theme used for the empty background (the plot itself carries its own theme).</summary>
    public PlotTheme Theme
    {
        get => (PlotTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Rect(0, 0, ActualWidth, ActualHeight);
        var builder = Builder;
        var theme = builder?.Theme ?? Theme;
        dc.DrawRectangle(new SolidColorBrush(theme.Background), null, size);

        if (builder is null || builder.ViewBounds.IsEmpty)
        {
            if (!string.IsNullOrEmpty(Placeholder))
            {
                var color = theme.HalfBright;
                var text = new FormattedText(Placeholder, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 15, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = Math.Max(50, ActualWidth - 40),
                };
                dc.DrawText(text, new Point(20, (ActualHeight - text.Height) / 2));
            }
            return;
        }

        dc.PushTransform(new MatrixTransform(ViewMatrix(builder)));
        dc.DrawDrawing(builder.Drawing);
        dc.Pop();
    }

    private Matrix ViewMatrix(PlotDrawingBuilder builder)
    {
        var page = builder.PageRect;
        double fit = Math.Min(ActualWidth / page.Width, ActualHeight / page.Height);
        if (double.IsNaN(fit) || double.IsInfinity(fit) || fit <= 0)
            fit = 1;
        var m = Matrix.Identity;
        m.Translate(-(page.X + page.Width / 2), -(page.Y + page.Height / 2));
        m.Scale(fit, fit);
        m.Translate(ActualWidth / 2, ActualHeight / 2);
        return m;
    }

}
