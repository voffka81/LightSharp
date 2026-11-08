using Avalonia.Controls;
using Avalonia.Layout;

namespace LightSharp.Views;

public enum MissingFileRecoveryChoice { KeepBackup, ExportDraft, DiscardBackup }

public sealed class MissingFileRecoveryDialog : Window
{
    public MissingFileRecoveryDialog(string name)
    {
        Title = "Missing source file recovery";
        Width = 530;
        Height = 180;
        MinWidth = 480;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var (label, choice) in new[]
        {
            ("Keep backup", MissingFileRecoveryChoice.KeepBackup),
            ("Export draft...", MissingFileRecoveryChoice.ExportDraft),
            ("Discard backup", MissingFileRecoveryChoice.DiscardBackup)
        })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => Close(choice);
            buttons.Children.Add(button);
        }
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = $"'{name}' is missing or was moved. Export its unsaved draft to a NEW file, or keep the backup for later.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                buttons
            }
        };
    }
}
