using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LightSharp.Views;

public partial class PropertiesView : UserControl
{
    public PropertiesView()
    {
        InitializeComponent();
        OpenFolderButton.Click += (sender, args) => OpenFolderRequested?.Invoke(sender, args);
        BuildButton.Click += (sender, args) => BuildRequested?.Invoke(sender, args);
    }

    public TextBlock WorkspaceDetailText => WorkspacePathText;
    public event EventHandler<RoutedEventArgs>? OpenFolderRequested;
    public event EventHandler<RoutedEventArgs>? BuildRequested;
}
