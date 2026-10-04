using System.Windows.Input;

namespace ScopeTrace;

public static class AppCommands
{
    private static RoutedUICommand Make(string text, string name, params InputGesture[] gestures) =>
        new(text, name, typeof(AppCommands), new InputGestureCollection(gestures));

    public static readonly RoutedUICommand Import = Make("Import HP-GL files…", nameof(Import), new KeyGesture(Key.O, ModifierKeys.Control));
    public static readonly RoutedUICommand ExportSelected = Make("Export selected…", nameof(ExportSelected), new KeyGesture(Key.E, ModifierKeys.Control));
    public static readonly RoutedUICommand ExportAll = Make("Export all…", nameof(ExportAll), new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift));
    public static readonly RoutedUICommand SelectAll = Make("Select all", nameof(SelectAll), new KeyGesture(Key.A, ModifierKeys.Control));
    public static readonly RoutedUICommand Rename = Make("Rename…", nameof(Rename), new KeyGesture(Key.F2));
    public static readonly RoutedUICommand Delete = Make("Delete", nameof(Delete), new KeyGesture(Key.Delete));
    public static readonly RoutedUICommand CopyImage = Make("Copy image", nameof(CopyImage), new KeyGesture(Key.C, ModifierKeys.Control));
    public static readonly RoutedUICommand RotateLeft = Make("Rotate left", nameof(RotateLeft), new KeyGesture(Key.L, ModifierKeys.Control));
    public static readonly RoutedUICommand RotateRight = Make("Rotate right", nameof(RotateRight), new KeyGesture(Key.R, ModifierKeys.Control));
    public static readonly RoutedUICommand RotateAuto = Make("Automatic rotation", nameof(RotateAuto));
    public static readonly RoutedUICommand FullScreen = Make("Full screen", nameof(FullScreen), new KeyGesture(Key.F11));
    public static readonly RoutedUICommand ExitFullScreen = Make("Leave full screen", nameof(ExitFullScreen), new KeyGesture(Key.Escape));
    public static readonly RoutedUICommand Replay = Make("Replay HP-GL file (simulate scope)…", nameof(Replay));
    public static readonly RoutedUICommand Settings = Make("Settings…", nameof(Settings));
    public static readonly RoutedUICommand ScopeSetup = Make("Scope setup…", nameof(ScopeSetup), new KeyGesture(Key.F1));
    public static readonly RoutedUICommand About = Make("About HP ScopeTrace", nameof(About));
    public static readonly RoutedUICommand Exit = Make("Exit", nameof(Exit));
}
