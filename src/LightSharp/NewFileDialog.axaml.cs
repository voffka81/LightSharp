using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LightSharp;

public sealed partial class NewFileDialog : Window
{
    public NewFileDialog()
        : this("NewFile.cs")
    {
    }

    public NewFileDialog(string defaultName)
    {
        InitializeComponent();
        FileNameBox.Text = defaultName;
        FileNameBox.SelectAll();
        FileNameBox.Focus();
    }

    public string FileName
    {
        get
        {
            var name = FileNameBox.Text?.Trim() ?? string.Empty;
            return Path.HasExtension(name) ? name : $"{name}.cs";
        }
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var name = FileNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ValidationText.Text = "Enter a valid file name.";
            ValidationText.IsVisible = true;
            return;
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
