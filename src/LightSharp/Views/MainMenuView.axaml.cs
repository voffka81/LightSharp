using Avalonia.Controls;
using Avalonia.Interactivity;
using LightSharp.Services;

namespace LightSharp.Views;

public partial class MainMenuView : UserControl, IDisposable
{
    private MainWindowMenuController? _controller;
    private Window? _shortcutOwner;
    private IDisposable? _gitAvailabilitySubscription;

    public MainMenuView()
    {
        InitializeComponent();
        GitMenuItem.IsEnabled = false;
    }

    public void AttachController(MainWindowMenuController controller, IMessageBus messageBus, Window shortcutOwner)
    {
        _controller = controller;
        if (_shortcutOwner is not null) _shortcutOwner.KeyDown -= ShortcutOwner_KeyDown;
        _shortcutOwner = shortcutOwner;
        _shortcutOwner.KeyDown += ShortcutOwner_KeyDown;
        _gitAvailabilitySubscription?.Dispose();
        _gitAvailabilitySubscription = messageBus.Observe<GitRepositoryAvailabilityChanged>()
            .Subscribe(UpdateGitMenuAvailability);
    }

    public void Dispose()
    {
        if (_shortcutOwner is not null) _shortcutOwner.KeyDown -= ShortcutOwner_KeyDown;
        _shortcutOwner = null;
        _gitAvailabilitySubscription?.Dispose();
        _gitAvailabilitySubscription = null;
    }

    private async void ShortcutOwner_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_controller is not null) await _controller.HandleWindowKeyDownAsync(e);
    }

    private void UpdateGitMenuAvailability(GitRepositoryAvailabilityChanged message)
    {
        GitMenuItem.IsEnabled = message.HasWorkspace;
        GitChangesMenuItem.IsVisible = message.HasRepository;
        RefreshGitMenuItem.IsVisible = message.HasRepository;
        GitMenuSeparator.IsVisible = message.HasRepository;
        InitializeGitMenuItem.IsVisible = message.HasWorkspace && !message.HasRepository;
        StageAllGitMenuItem.IsVisible = message.HasRepository;
        CommitGitMenuItem.IsVisible = message.HasRepository;
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e) => await _controller!.NewProjectAsync();
    private async void NewFile_Click(object? sender, RoutedEventArgs e) => await _controller!.NewFileAsync();
    private async void OpenFile_Click(object? sender, RoutedEventArgs e) => await _controller!.OpenFileAsync();
    private async void OpenSolution_Click(object? sender, RoutedEventArgs e) => await _controller!.OpenSolutionAsync();
    private async void OpenFolder_Click(object? sender, RoutedEventArgs e) => await _controller!.OpenFolderAsync();
    private async void Save_Click(object? sender, RoutedEventArgs e) => await _controller!.SaveAsync();
    private async void SaveAll_Click(object? sender, RoutedEventArgs e) => await _controller!.SaveAllAsync();
    private void Find_Click(object? sender, RoutedEventArgs e) => _controller!.Find();
    private void Replace_Click(object? sender, RoutedEventArgs e) => _controller!.Replace();
    private void FindInFiles_Click(object? sender, RoutedEventArgs e) => _controller!.FindInFiles();
    private async void FormatDocument_Click(object? sender, RoutedEventArgs e) => await _controller!.FormatDocumentAsync();
    private async void FormatSelection_Click(object? sender, RoutedEventArgs e) => await _controller!.FormatSelectionAsync();
    private void Comment_Click(object? sender, RoutedEventArgs e) => _controller!.Comment();
    private void Uncomment_Click(object? sender, RoutedEventArgs e) => _controller!.Uncomment();
    private void SelectAll_Click(object? sender, RoutedEventArgs e) => _controller!.SelectAll();
    private void FocusOutput_Click(object? sender, RoutedEventArgs e) => _controller!.FocusOutput();
    private async void Build_Click(object? sender, RoutedEventArgs e) => await _controller!.BuildAsync();
    private async void Start_Click(object? sender, RoutedEventArgs e) => await _controller!.StartAsync();
    private void Stop_Click(object? sender, RoutedEventArgs e) => _controller!.Stop();
    private async void Test_Click(object? sender, RoutedEventArgs e) => await _controller!.TestAsync();
    private void ShowGitChanges_Click(object? sender, RoutedEventArgs e) => _controller!.ShowGitChanges();
    private async void RefreshGit_Click(object? sender, RoutedEventArgs e) => await _controller!.RefreshGitAsync();
    private async void InitializeGit_Click(object? sender, RoutedEventArgs e) => await _controller!.InitializeGitAsync();
    private async void StageAllGit_Click(object? sender, RoutedEventArgs e) => await _controller!.StageAllGitAsync();
}
