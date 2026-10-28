using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LightSharp.Models;
using LightSharp.Services;

namespace LightSharp.Views;

public partial class MainToolbarView : UserControl
{
    private MainWindowMenuController? _controller;

    public MainToolbarView() => InitializeComponent();

    public string SelectedConfiguration =>
        (ConfigurationSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Debug";

    public FileNode? SelectedStartupProject => StartupProjectSelector.SelectedItem as FileNode;

    public void AttachController(MainWindowMenuController controller) => _controller = controller;

    public void SetProjects(ObservableCollection<FileNode> projects) => StartupProjectSelector.ItemsSource = projects;

    public void SetStartupProject(FileNode? project) => StartupProjectSelector.SelectedItem = project;

    public void SetWorkspaceName(string name) => WorkspaceText.Text = name;

    public void SetStopEnabled(bool enabled) => StopButton.IsEnabled = enabled;

    private async void Save_Click(object? sender, RoutedEventArgs e) => await _controller!.SaveAsync();
    private async void Start_Click(object? sender, RoutedEventArgs e) => await _controller!.StartAsync();
    private async void Build_Click(object? sender, RoutedEventArgs e) => await _controller!.BuildAsync();
    private async void Test_Click(object? sender, RoutedEventArgs e) => await _controller!.TestAsync();
    private void Stop_Click(object? sender, RoutedEventArgs e) => _controller!.Stop();
    private async void Search_KeyDown(object? sender, KeyEventArgs e) => await _controller!.HandleSearchKeyDownAsync(e, SearchBox.Text);
}
