using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScopeTrace.Serial;

namespace ScopeTrace.Controls;

public sealed class ListenerStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.FindResource(value switch
        {
            ListenerState.Listening => "ListeningBrush",
            ListenerState.Unavailable => "WarningBrush",
            _ => "StoppedBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
