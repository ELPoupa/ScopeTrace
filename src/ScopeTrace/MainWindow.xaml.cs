using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ScopeTrace.Dialogs;
using ScopeTrace.Export;
using ScopeTrace.Storage;
using ScopeTrace.ViewModels;

namespace ScopeTrace;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        // Optional: --data-dir <folder> keeps settings and captures elsewhere (handy for testing),
        // --replay <file.plt> plays a file as if the scope sent it.
        string[] args = Environment.GetCommandLineArgs();
        string? dataDir = ArgValue(args, "--data-dir");
        string settingsPath = dataDir is null ? AppSettings.DefaultPath : Path.Combine(dataDir, "settings.json");
        string capturesDir = dataDir is null ? CaptureStore.DefaultDirectory : Path.Combine(dataDir, "Captures");
        string? replay = ArgValue(args, "--replay");

        var settings = AppSettings.Load(settingsPath);
        _vm = new MainViewModel(settings, settingsPath, new CaptureStore(capturesDir), Dispatcher);
        _vm.PlotUpdated += () => Plot.Refresh();
        // Toolbar buttons (Export...) only re-check their state on input; nudge them when captures change.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedCapture) or nameof(MainViewModel.CountText))
                CommandManager.InvalidateRequerySuggested();
        };
        DataContext = _vm;

        RestoreWindowPlacement(settings);
        Loaded += (_, _) =>
        {
            _vm.LoadSavedCaptures();
            if (settings.ListenOnStartup)
                _vm.StartListening();
            if (replay is not null)
                _vm.StartReplay(replay);
        };
        Closing += (_, _) =>
        {
            SetFullScreen(false);
            SaveWindowPlacement(settings);
            _vm.SaveSettings();
            _vm.Dispose();
        };
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private List<CaptureItem> SelectedItems =>
        CapturesList.SelectedItems.Cast<CaptureItem>().OrderBy(c => _vm.Captures.IndexOf(c)).ToList();

    // ------------------------------------------------------------------ window placement

    private void RestoreWindowPlacement(AppSettings s)
    {
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop)
            && s.WindowLeft >= SystemParameters.VirtualScreenLeft - 50
            && s.WindowTop >= SystemParameters.VirtualScreenTop - 50
            && s.WindowLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && s.WindowTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
        SidebarColumn.Width = new GridLength(Math.Clamp(s.SidebarWidth, 180, 520));
    }

    private void SaveWindowPlacement(AppSettings s)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        s.SidebarWidth = SidebarColumn.ActualWidth;
    }

    // ------------------------------------------------------------------ list behaviour

    private void CapturesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Right-clicking an item that is not part of the selection selects just that item,
        // like Explorer; right-clicking inside the selection keeps it (for multi-item actions).
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        if (element is ListBoxItem { IsSelected: false } item)
        {
            CapturesList.SelectedItems.Clear();
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void HasSelection_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CapturesList is { SelectedItems.Count: > 0 };

    private void HasCaptures_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _vm is { Captures.Count: > 0 };

    private void NotListening_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _vm is { CanChangeSettings: true };

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => _vm.RefreshPorts();

    // ------------------------------------------------------------------ commands

    private void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CapturesList.SelectAll();
        CapturesList.Focus();
    }

    private void Rename_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var item = SelectedItems.FirstOrDefault();
        if (item is null)
            return;
        var dialog = new InputDialog("Rename capture", "Name:", item.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
            _vm.Rename(item, dialog.Value);
    }

    private void Delete_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0)
            return;
        string what = items.Count == 1 ? $"\"{items[0].Name}\"" : $"these {items.Count} captures";
        var answer = MessageBox.Show(this, $"Delete {what}?\n\nThis removes them from the app and from disk. Exported files are not affected.",
            "Delete captures", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
            _vm.Delete(items);
    }

    private void CopyImage_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var item = _vm.SelectedCapture ?? SelectedItems.FirstOrDefault();
        if (item is null || item.Document.IsEmpty)
            return;
        try
        {
            Clipboard.SetImage(CaptureExporter.RenderBitmap(item.Document, _vm.CurrentTheme, item.RotationDegrees, 2000));
            _vm.ReportActivity($"Copied {item.Name} to the clipboard");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _vm.ReportActivity($"Could not copy to the clipboard: {ex.Message}");
        }
    }

    private void RotateLeft_Executed(object sender, ExecutedRoutedEventArgs e) => _vm.Rotate(SelectedItems, -90);
    private void RotateRight_Executed(object sender, ExecutedRoutedEventArgs e) => _vm.Rotate(SelectedItems, 90);
    private void RotateAuto_Executed(object sender, ExecutedRoutedEventArgs e) => _vm.SetRotation(SelectedItems, null);

    // ------------------------------------------------------------------ full screen

    private bool _fullScreen;
    private WindowState _stateBeforeFullScreen;
    private GridLength _sidebarBeforeFullScreen;

    private void FullScreen_Executed(object sender, ExecutedRoutedEventArgs e) => SetFullScreen(!_fullScreen);

    private void ExitFullScreen_Executed(object sender, ExecutedRoutedEventArgs e) => SetFullScreen(false);

    /// <summary>Shows only the plot, covering the whole screen (taskbar included).</summary>
    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen)
            return;
        _fullScreen = on;
        var collapsed = on ? Visibility.Collapsed : Visibility.Visible;
        MenuBar.Visibility = ToolbarBar.Visibility = StatusStrip.Visibility = collapsed;
        Sidebar.Visibility = Splitter.Visibility = collapsed;

        if (on)
        {
            _stateBeforeFullScreen = WindowState;
            _sidebarBeforeFullScreen = SidebarColumn.Width;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            PlotFrame.Margin = new Thickness(0);
            PlotFrame.BorderThickness = new Thickness(0);
            PlotFrame.CornerRadius = new CornerRadius(0);
            WindowState = WindowState.Normal; // re-maximizing without a frame also covers the taskbar
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullScreen;
            SidebarColumn.MinWidth = 180;
            SidebarColumn.Width = _sidebarBeforeFullScreen;
            PlotFrame.Margin = new Thickness(12);
            PlotFrame.BorderThickness = new Thickness(1);
            PlotFrame.CornerRadius = new CornerRadius(6);
        }
        FullScreenHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Plot.Refresh();
    }

    private void ExportSelected_Executed(object sender, ExecutedRoutedEventArgs e) => ExportItems(SelectedItems);
    private void ExportAll_Executed(object sender, ExecutedRoutedEventArgs e) => ExportItems(_vm.Captures.ToList());

    private void ExportItems(List<CaptureItem> items)
    {
        items = items.Where(i => !i.IsReceiving).ToList();
        if (items.Count == 0)
        {
            _vm.ReportActivity("Nothing to export yet (a capture that is still arriving is exported once it is complete)");
            return;
        }

        var s = _vm.Settings;
        var dialog = new ExportDialog(items.Count, s) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        s.ExportFolder = dialog.Folder;
        s.ExportFormats = dialog.Formats;
        s.PngLongSide = dialog.PngLongSide;
        s.ExportTheme = dialog.Theme;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var files = _vm.Export(items, dialog.Formats, dialog.Theme, dialog.PngLongSide, dialog.Folder);
            _vm.ReportActivity($"Exported {items.Count} capture(s) as {files.Count} file(s) to {dialog.Folder}");
            if (dialog.OpenFolderAfterwards)
                Process.Start(new ProcessStartInfo { FileName = dialog.Folder, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Export failed:\n\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Import_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import HP-GL plot files",
            Filter = "HP-GL plots (*.plt;*.hpgl;*.hgl;*.hpg;*.gl)|*.plt;*.hpgl;*.hgl;*.hpg;*.gl|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
            _vm.Import(dialog.FileNames);
    }

    private void Replay_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Replay an HP-GL file as if the scope sent it ({_vm.SelectedBaud} baud)",
            Filter = "HP-GL plots (*.plt;*.hpgl;*.hgl;*.hpg;*.gl)|*.plt;*.hpgl;*.hgl;*.hpg;*.gl|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            _vm.StartReplay(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Replay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var s = _vm.Settings;
        var dialog = new SettingsDialog(s.IdleTimeoutSeconds, s.ListenOnStartup) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _vm.SetIdleTimeout(dialog.IdleTimeoutSeconds);
        s.ListenOnStartup = dialog.ListenOnStartup;
        _vm.SaveSettings();
    }

    private void ScopeSetup_Executed(object sender, ExecutedRoutedEventArgs e) =>
        MessageBox.Show(this,
            "On the HP 54600-series scope (with an RS-232 interface module):\n\n" +
            "1. Press Print/Utility, then Hardcopy Menu, and press Format until it shows \"Plotter\".\n" +
            "   (Plotter Setup → Colors 2 plots half-bright traces with pen 1 and full-bright with pen 2.\n" +
            "    Factors On adds the scale factors below the screen.)\n" +
            "2. Back in Print/Utility, open I/O Menu and set Baud Rate (1200, 2400, 9600 or 19200)\n" +
            "   and Handshake (XON or DTR).\n" +
            "3. In ScopeTrace pick the same port, baud rate and handshake, and press Listen.\n" +
            "4. Press Print Screen on the scope. The plot appears here as it is received; every new\n" +
            "   Print Screen becomes a new capture automatically.\n\n" +
            "Cable: the scope's 25-pin RS-232 port to the PC through a null-modem cable/adapter.\n" +
            "If a plot ends up sideways, use the rotate buttons; Automatic rotation follows the text.",
            "Scope setup", MessageBoxButton.OK, MessageBoxImage.Information);

    private void About_Executed(object sender, ExecutedRoutedEventArgs e) =>
        MessageBox.Show(this,
            "HP ScopeTrace\n\nReceives HP-GL plots from HP 54600-series oscilloscopes over RS-232 and draws them live.\n\n" +
            $"Captures are stored in:\n{CaptureStore.DefaultDirectory}",
            "About HP ScopeTrace", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
}
