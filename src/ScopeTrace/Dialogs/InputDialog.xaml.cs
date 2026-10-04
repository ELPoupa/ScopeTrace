using System.Windows;

namespace ScopeTrace.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string value)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = value;
        Loaded += (_, _) => ValueBox.SelectAll();
    }

    public string Value => ValueBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
            return;
        DialogResult = true;
    }
}
