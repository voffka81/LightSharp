using Avalonia.Controls;
using LightSharp.Services;
namespace LightSharp.Views;

public partial class PropertiesView : UserControl
{
    private IMessageBus? _messageBus;

    public PropertiesView()
    {
        InitializeComponent();
        OpenFolderButton.Click += (_, _) => _messageBus?.Publish(new OpenFolderRequested());
        BuildButton.Click += (_, _) => _messageBus?.Publish(new BuildRequested());
    }

    public void AttachMessageBus(IMessageBus messageBus) => _messageBus = messageBus;

    public void SetWorkspacePath(string path) => WorkspacePathText.Text = path;

    public void SetStartupProject(string? name) => StartupProjectText.Text = name ?? "Select a project";
}
