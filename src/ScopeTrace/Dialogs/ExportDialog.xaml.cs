using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ScopeTrace.Export;
using ScopeTrace.Storage;

namespace ScopeTrace.Dialogs;

public partial class ExportDialog : Window
{
    private static readonly int[] Sizes = [2000, 4000, 8000];

    public ExportDialog(int count, AppSettings settings)
    {
        InitializeComponent();
        HeaderText.Text = count == 1 ? "Export 1 capture" : $"Export {count} captures";
        PngBox.IsChecked = settings.ExportFormats.HasFlag(ExportFormats.Png);
        SvgBox.IsChecked = settings.ExportFormats.HasFlag(ExportFormats.Svg);
        HpglBox.IsChecked = settings.ExportFormats.HasFlag(ExportFormats.Hpgl);
        ThemeBox.SelectedIndex = (int)settings.ExportTheme;
        int sizeIndex = Array.IndexOf(Sizes, settings.PngLongSide);
        SizeBox.SelectedIndex = sizeIndex >= 0 ? sizeIndex : 1;
        FolderBox.Text = string.IsNullOrWhiteSpace(settings.ExportFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ScopeTrace")
            : settings.ExportFolder;
    }

    public ExportFormats Formats =>
        (PngBox.IsChecked == true ? ExportFormats.Png : 0)
        | (SvgBox.IsChecked == true ? ExportFormats.Svg : 0)
        | (HpglBox.IsChecked == true ? ExportFormats.Hpgl : 0);

    public ExportTheme Theme => (ExportTheme)Math.Max(0, ThemeBox.SelectedIndex);
    public int PngLongSide => Sizes[Math.Max(0, SizeBox.SelectedIndex)];
    public string Folder => FolderBox.Text.Trim();
    public bool OpenFolderAfterwards => OpenFolderBox.IsChecked == true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Export to folder" };
        if (Directory.Exists(Folder))
            dialog.InitialDirectory = Folder;
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Formats == ExportFormats.None)
        {
            MessageBox.Show(this, "Choose at least one format.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (Folder.Length == 0 || !Path.IsPathFullyQualified(Folder))
        {
            MessageBox.Show(this, "Choose a folder to export to.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
