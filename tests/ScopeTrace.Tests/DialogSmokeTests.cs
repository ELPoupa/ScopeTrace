using System.Runtime.ExceptionServices;
using ScopeTrace.Dialogs;
using ScopeTrace.Export;
using ScopeTrace.Storage;

namespace ScopeTrace.Tests;

/// <summary>Loads every dialog's XAML with the app's resources, so markup errors fail here and not in front of the user.</summary>
public class DialogSmokeTests
{
    [Fact]
    public void AllDialogsLoad()
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();

                var settings = new AppSettings { PngLongSide = 8000, ExportFormats = ExportFormats.Svg, ExportTheme = ExportTheme.Paper };
                var export = new ExportDialog(3, settings);
                Assert.Equal(ExportFormats.Svg, export.Formats);
                Assert.Equal(8000, export.PngLongSide);
                Assert.Equal(ExportTheme.Paper, export.Theme);
                Assert.EndsWith("ScopeTrace", export.Folder);

                var settingsDialog = new SettingsDialog(2.5, false);
                Assert.Equal(2.5, settingsDialog.IdleTimeoutSeconds);
                Assert.False(settingsDialog.ListenOnStartup);

                var input = new InputDialog("Rename capture", "Name:", "Capture 007");
                Assert.Equal("Capture 007", input.Value);

                app.Shutdown();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }
}
