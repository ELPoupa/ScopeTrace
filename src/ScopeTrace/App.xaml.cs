using System.Windows;
using System.Windows.Threading;

namespace ScopeTrace;

public partial class App : Application
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScopeTrace");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        new MainWindow().Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string log = Path.Combine(DataDirectory, "error.log");
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(log, $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch (IOException)
        {
        }
        MessageBox.Show(MainWindow, $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to {log}.",
            "HP ScopeTrace", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
