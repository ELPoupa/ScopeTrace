using System.Windows;

namespace ScopeTrace.Dialogs;

public partial class SettingsDialog : Window
{
    public SettingsDialog(double idleTimeoutSeconds, bool listenOnStartup)
    {
        InitializeComponent();
        IdleSlider.Value = idleTimeoutSeconds;
        ListenOnStartupBox.IsChecked = listenOnStartup;
    }

    public double IdleTimeoutSeconds => IdleSlider.Value;
    public bool ListenOnStartup => ListenOnStartupBox.IsChecked == true;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
