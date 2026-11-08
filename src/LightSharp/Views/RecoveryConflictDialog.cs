using Avalonia.Controls;
using Avalonia.Layout;

namespace LightSharp.Views;

public enum RecoveryConflictChoice { KeepBackup, RestoreDraft, DiscardBackup }

/// <summary>Resolve a startup recovery conflict without modifying the file on disk.</summary>
public sealed class RecoveryConflictDialog : Window
{
    public RecoveryConflictDialog(string fileName)
    {
        Title = "Unsaved changes found";
        Width = 550;
        Height = 190;
        MinWidth = 500;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock
        {
            Text = $"A recovered draft of '{fileName}' conflicts with the version on disk. " +
                   "Restore opens the draft as unsaved editor changes without overwriting the disk. " +
                   "Keep backup asks again next launch.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var (label, choice) in new[]
        {
            ("Keep backup", RecoveryConflictChoice.KeepBackup),
            ("Restore draft", RecoveryConflictChoice.RestoreDraft),
            ("Discard backup", RecoveryConflictChoice.DiscardBackup)
        })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => Close(choice);
            buttons.Children.Add(button);
        }
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 20,
            Children = { message, buttons }
        };
    }
}
