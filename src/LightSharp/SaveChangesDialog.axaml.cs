using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LightSharp;

public enum SaveChangesChoice
{
    Cancel,
    Save,
    Discard
}

public sealed partial class SaveChangesDialog : Window
{
    public SaveChangesDialog() : this(string.Empty) { }

    public SaveChangesDialog(string fileName)
    {
        InitializeComponent();
        MessageText.Text = string.IsNullOrEmpty(fileName)
            ? "This file has unsaved changes."
            : $"Save changes to '{fileName}' before closing?";
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(SaveChangesChoice.Save);
    private void Discard_Click(object? sender, RoutedEventArgs e) => Close(SaveChangesChoice.Discard);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(SaveChangesChoice.Cancel);
}
