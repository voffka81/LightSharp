using Avalonia.Controls;
using Avalonia.Layout;

namespace LightSharp.Views;

public enum ExternalChangeChoice { Cancel, Reload, Overwrite }

/// <summary>Explicit user decision when the file on disk changed since opening.</summary>
public sealed class ExternalChangeDialog : Window
{
    public ExternalChangeDialog(string fileName)
    {
        Title = "External file changes";
        Width = 480;
        Height = 190;
        MinWidth = 400;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock
        {
            Text = $"'{fileName}' was changed outside LightSharp.\nReload discards editor changes. Overwrite replaces the external version.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, choice) in new[]
        {
            ("Cancel", ExternalChangeChoice.Cancel),
            ("Reload from disk", ExternalChangeChoice.Reload),
            ("Overwrite disk", ExternalChangeChoice.Overwrite)
        })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => Close(choice);
            buttons.Children.Add(button);
        }
        Content = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 20, Children = { message, buttons } };
    }
}
