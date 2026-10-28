using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using LightSharp.Models;
using LightSharp.Services;
using LightSharp.Views;

namespace LightSharp;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceService _workspace = new();
    private readonly MessageBus _messageBus = new();
    private readonly GitChangesController _gitChangesController;
    private readonly PropertiesPanelController _propertiesPanelController;
    private readonly OutputPanelController _outputPanelController;
    private readonly SolutionExplorerController _solutionExplorerController;
    private readonly EditorWorkspaceController _editorWorkspaceController;
    private readonly SearchReplaceController _searchController;
    private DotNetCommandController _dotNetCommandController = null!;
    private WorkspaceMenuController _workspaceMenuController = null!;
    private MainWindowMenuController _menuController = null!;
    private readonly ObservableCollection<FileNode> _roots = [];
    private readonly ObservableCollection<FileNode> _projects = [];
    private readonly List<IDisposable> _messageSubscriptions = [];
    private string? _workspacePath;
    private string? _solutionPath;
    private ToolDock? _solutionExplorerDock;
    private Tool? _gitChangesTool;
    private readonly DispatcherTimer _diagnosticsTimer;

    public MainWindow()
    {
        InitializeComponent();
        SolutionExplorerPanel.AttachMessageBus(_messageBus);
        EditorSurface.AttachMessageBus(_messageBus);
        PropertiesPanel.AttachMessageBus(_messageBus);
        OutputPanel.AttachMessageBus(_messageBus);
        GitChangesPanel.AttachMessageBus(_messageBus);
        _gitChangesController = new GitChangesController(GitChangesPanel, new GitService(), _messageBus);
        _editorWorkspaceController = new EditorWorkspaceController(
            EditorSurface, _projects, OutputPanel, _messageBus, this,
            SolutionExplorerPanel.SelectAndReveal);
        _searchController = new SearchReplaceController(
            this, _editorWorkspaceController, EditorSurface, _roots, _projects,
            () => _solutionPath, () => _ = RefreshGitStatusAsync(), OutputPanel);
        using (var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://LightSharp/Assets/LightSharp.png")))
            Icon = new WindowIcon(iconStream);
        InitializeDockLayout();
        _dotNetCommandController = new DotNetCommandController(
            new DotNetService(), OutputPanel, StopButton,
            () => _workspacePath, () => SelectedConfiguration,
            () => StartupProjectSelector.SelectedItem as FileNode, AppendOutput);
        _workspaceMenuController = new WorkspaceMenuController(
            this, _dotNetCommandController, _editorWorkspaceController, OutputPanel,
            _roots, _projects, StartupProjectSelector,
            () => _workspacePath, () => _solutionPath, LoadWorkspaceAsync);
        _menuController = new MainWindowMenuController(
            _messageBus, _editorWorkspaceController, _searchController, _gitChangesController,
            OutputPanel, new InputHandlerService(), _workspaceMenuController, _dotNetCommandController,
            RefreshGitStatusAsync,
            _solutionExplorerDock!, _gitChangesTool!, GitMenuItem, GitChangesMenuItem,
            RefreshGitMenuItem, GitMenuSeparator, InitializeGitMenuItem, StageAllGitMenuItem,
            CommitGitMenuItem);
        _propertiesPanelController = new PropertiesPanelController(
            _messageBus, () => _menuController.OpenFolderAsync(), () => _menuController.BuildAsync());
        _outputPanelController = new OutputPanelController(
            _messageBus, Problem_Activated, () => _menuController.TestAsync());
        _solutionExplorerController = new SolutionExplorerController(
            _messageBus,
            new SolutionExplorerActionHandlers(
                OpenDocumentAsync,
                _workspaceMenuController.CreateFileInNodeAsync,
                _workspaceMenuController.NewProjectAsync,
                _workspaceMenuController.AddExistingProjectAsync,
                () => _ = _menuController.BuildAsync(),
                () => _ = _menuController.TestAsync(),
                node => StartupProjectSelector.SelectedItem = node,
                node => _ = _dotNetCommandController.ExecuteAsync("build", "--configuration", SelectedConfiguration, node.FullPath),
                node => _ = _dotNetCommandController.ExecuteAsync("test", "--configuration", SelectedConfiguration, node.FullPath),
                OpenContainingFolder,
                _workspaceMenuController.RefreshWorkspaceAsync));
        SolutionExplorerPanel.SetRoots(_roots);
        SolutionExplorerPanel.SetWorkspaceContext(_solutionPath, hasWorkspace: false);
        StartupProjectSelector.ItemsSource = _projects;
        _messageSubscriptions.Add(_messageBus.Observe<StatusMessageChanged>().Subscribe(message => OutputPanel.StatusMessage = message.Message));
        _messageSubscriptions.Add(_messageBus.Observe<OutputLineRequested>().Subscribe(message => AppendOutput(message.Line)));
        _messageSubscriptions.Add(_messageBus.Observe<GitBranchChanged>().Subscribe(message =>
        {
            if (!string.IsNullOrEmpty(message.Branch)) BranchStatusText.Text = message.Branch;
        }));
        _messageSubscriptions.Add(_messageBus.Observe<EditorStatusChanged>().Subscribe(message => EditorStatusText.Text = message.Message));
        _messageSubscriptions.Add(_messageBus.Observe<EditorCursorChanged>().Subscribe(message => CursorStatusText.Text = $"Ln {message.Line}, Col {message.Column}"));
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _diagnosticsTimer.Tick += (_, _) => UpdateDiagnostics();
        _diagnosticsTimer.Start();
        Closed += (_, _) =>
        {
            _diagnosticsTimer.Stop();
            _menuController.Dispose();
            _dotNetCommandController.Dispose();
            _editorWorkspaceController.Dispose();
            _gitChangesController.Dispose();
            _propertiesPanelController.Dispose();
            _outputPanelController.Dispose();
            _solutionExplorerController.Dispose();
            foreach (var subscription in _messageSubscriptions) subscription.Dispose();
        };
        UpdateDiagnostics();
    }

    private void InitializeDockLayout()
    {
        WorkspaceArea.Children.Remove(SolutionExplorerPanel);
        WorkspaceArea.Children.Remove(GitChangesPanel);
        WorkspaceArea.Children.Remove(EditorSurface);
        WorkspaceArea.Children.Remove(PropertiesPanel);
        ShellGrid.Children.Remove(WorkspaceArea);
        ShellGrid.Children.Remove(OutputPanel);

        var factory = new Factory();
        var editorDocument = new Document
        {
            Id = "EditorWorkspace",
            Title = "Editor",
            CanClose = false,
            Content = new Func<IServiceProvider, object>(_ => EditorSurface)
        };
        var documents = new DocumentDock
        {
            Id = "Documents",
            Proportion = 0.78,
            CanCreateDocument = false,
            CanCloseLastDockable = false,
            VisibleDockables = factory.CreateList<IDockable>(editorDocument),
            ActiveDockable = editorDocument
        };

        var solutionExplorer = new Tool
        {
            Id = "SolutionExplorer",
            Title = "Solution Explorer",
            CanClose = false,
            Content = new Func<IServiceProvider, object>(_ => SolutionExplorerPanel)
        };
        var gitChanges = new Tool
        {
            Id = "GitChanges",
            Title = "Git Changes",
            CanClose = false,
            Content = new Func<IServiceProvider, object>(_ => GitChangesPanel)
        };
        _gitChangesTool = gitChanges;
        var properties = new Tool
        {
            Id = "Properties",
            Title = "Properties",
            Content = new Func<IServiceProvider, object>(_ => PropertiesPanel)
        };
        var solutionExplorerDock = new ToolDock
        {
            Id = "SolutionExplorerDock",
            Title = "Solution Explorer",
            Alignment = Alignment.Right,
            Proportion = 0.56,
            VisibleDockables = factory.CreateList<IDockable>(solutionExplorer, gitChanges),
            ActiveDockable = solutionExplorer
        };
        _solutionExplorerDock = solutionExplorerDock;
        var propertiesDock = new ToolDock
        {
            Id = "PropertiesDock",
            Title = "Properties",
            Alignment = Alignment.Right,
            Proportion = 0.44,
            VisibleDockables = factory.CreateList<IDockable>(properties),
            ActiveDockable = properties
        };
        var rightColumn = new ProportionalDock
        {
            Id = "RightColumn",
            Proportion = 0.24,
            Orientation = Dock.Model.Core.Orientation.Vertical,
            VisibleDockables = factory.CreateList<IDockable>(
                solutionExplorerDock,
                new ProportionalDockSplitter(),
                propertiesDock)
        };

        var outputTool = new Tool
        {
            Id = "Output",
            Title = "Output",
            Content = new Func<IServiceProvider, object>(_ => OutputPanel)
        };
        var outputDock = new ToolDock
        {
            Id = "OutputDock",
            Title = "Output",
            Alignment = Alignment.Bottom,
            Proportion = 0.22,
            VisibleDockables = factory.CreateList<IDockable>(outputTool),
            ActiveDockable = outputTool
        };
        var leftColumn = new ProportionalDock
        {
            Id = "LeftColumn",
            Proportion = 0.76,
            Orientation = Dock.Model.Core.Orientation.Vertical,
            VisibleDockables = factory.CreateList<IDockable>(
                documents,
                new ProportionalDockSplitter(),
                outputDock)
        };
        var layout = new ProportionalDock
        {
            Id = "WorkspaceLayout",
            Orientation = Dock.Model.Core.Orientation.Horizontal,
            VisibleDockables = factory.CreateList<IDockable>(
                leftColumn,
                new ProportionalDockSplitter(),
                rightColumn)
        };
        var root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(layout);
        root.DefaultDockable = layout;
        factory.InitLayout(root);

        var dockHost = new DockControl
        {
            Factory = factory,
            Layout = root,
            InitializeFactory = true
        };
        Grid.SetRow(dockHost, 2);
        Grid.SetRowSpan(dockHost, 2);
        ShellGrid.Children.Add(dockHost);
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e) => await _menuController.OpenFolderAsync();
    private async void OpenSolution_Click(object? sender, RoutedEventArgs e) => await _menuController.OpenSolutionAsync();
    private async void OpenFile_Click(object? sender, RoutedEventArgs e) => await _menuController.OpenFileAsync();

    private async Task LoadWorkspaceAsync(string path, string? selectedSolutionPath = null)
    {
        _workspacePath = path;
        var solutionName = selectedSolutionPath is null ? null : Path.GetFileName(selectedSolutionPath);
        WorkspaceText.Text = solutionName ?? Path.GetFileName(path);
        PropertiesPanel.SetWorkspacePath(path);
        OutputPanel.StatusMessage = "Loading workspace...";
        _roots.Clear();
        _projects.Clear();
        var nodes = await _workspace.LoadAsync(path);
        var solution = nodes.FirstOrDefault(node =>
            (selectedSolutionPath is null || string.Equals(node.FullPath, selectedSolutionPath, StringComparison.OrdinalIgnoreCase)) &&
            (Path.GetExtension(node.Name).Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(node.Name).Equals(".slnx", StringComparison.OrdinalIgnoreCase)));
        _solutionPath = solution?.FullPath;
        SolutionExplorerPanel.SetWorkspaceContext(_solutionPath, hasWorkspace: true);

        if (solution is not null)
        {
            var solutionRoot = await _workspace.LoadSolutionAsync(solution.FullPath);
            _roots.Add(solutionRoot);
            foreach (var project in solutionRoot.Children)
                _projects.Add(project);
            WorkspaceText.Text = solution.Name;
        }
        else
        {
            var root = new FileNode { Name = Path.GetFileName(path), FullPath = path, IsDirectory = true };
            foreach (var node in nodes)
            {
                root.Children.Add(node);
                AddProjects(node);
            }
            _roots.Add(root);
        }
        StartupProjectSelector.SelectedItem = _projects.FirstOrDefault();
        if (_editorWorkspaceController.ActiveDocument is { } active) SolutionExplorerPanel.SelectAndReveal(active.FullPath);
        OutputPanel.StatusMessage = "Ready";
        await RefreshGitStatusAsync();
    }

    private async void NewFile_Click(object? sender, RoutedEventArgs e) => await _menuController.NewFileAsync();

    private static void OpenContainingFolder(FileNode node)
    {
        var path = node.IsDirectory && Directory.Exists(node.FullPath)
            ? node.FullPath
            : Path.GetDirectoryName(node.FullPath);
        if (path is null || !Directory.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private Task OpenDocumentAsync(FileNode node) => _editorWorkspaceController.OpenDocumentAsync(node);
    private async void Save_Click(object? sender, RoutedEventArgs e) => await _menuController.SaveAsync();
    private async void SaveAll_Click(object? sender, RoutedEventArgs e) => await _menuController.SaveAllAsync();

    private string SelectedConfiguration =>
        (ConfigurationSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Debug";

    private async void NewProject_Click(object? sender, RoutedEventArgs e) => await _menuController.NewProjectAsync();

    private async void Build_Click(object? sender, RoutedEventArgs e) => await _menuController.BuildAsync();
    private async void Test_Click(object? sender, RoutedEventArgs e) => await _menuController.TestAsync();
    private async void Start_Click(object? sender, RoutedEventArgs e) => await _menuController.StartAsync();
    private void Stop_Click(object? sender, RoutedEventArgs e) => _menuController.Stop();
    private void SelectAll_Click(object? sender, RoutedEventArgs e) => _menuController.SelectAll();
    private void Comment_Click(object? sender, RoutedEventArgs e) => _menuController.Comment();
    private void Uncomment_Click(object? sender, RoutedEventArgs e) => _menuController.Uncomment();
    private async void FormatDocument_Click(object? sender, RoutedEventArgs e) => await _menuController.FormatDocumentAsync();
    private async void FormatSelection_Click(object? sender, RoutedEventArgs e) => await _menuController.FormatSelectionAsync();
    private void FocusOutput_Click(object? sender, RoutedEventArgs e) => _menuController.FocusOutput();
    private void ShowProblems_Click(object? sender, RoutedEventArgs e) => _menuController.ShowProblems();
    private void ShowOutput_Click(object? sender, RoutedEventArgs e) => _menuController.ShowOutput();
    private void ShowTests_Click(object? sender, RoutedEventArgs e) => _menuController.ShowTests();
    private void ShowGitChanges_Click(object? sender, RoutedEventArgs e) => _menuController.ShowGitChanges();
    private async void RefreshGit_Click(object? sender, RoutedEventArgs e) => await _menuController.RefreshGitAsync();

    private Task RefreshGitStatusAsync() => _gitChangesController.SetWorkspaceAsync(_workspacePath);

    private async void InitializeGit_Click(object? sender, RoutedEventArgs e) => await _menuController.InitializeGitAsync();
    private async void StageAllGit_Click(object? sender, RoutedEventArgs e) => await _menuController.StageAllGitAsync();
    private void CommitGit_Click(object? sender, RoutedEventArgs e) => _menuController.ShowGitChanges();
    private void FetchGit_Click(object? sender, RoutedEventArgs e) => _menuController.FetchGit();
    private void PullGit_Click(object? sender, RoutedEventArgs e) => _menuController.PullGit();
    private void PushGit_Click(object? sender, RoutedEventArgs e) => _menuController.PushGit();

    private void Problem_Activated(EditorProblem problem)
    {
        _editorWorkspaceController.NavigateTo(problem.Line, problem.Column);
    }
    private async void Window_KeyDown(object? sender, KeyEventArgs e) => await _menuController.HandleWindowKeyDownAsync(e);
    private void Find_Click(object? sender, RoutedEventArgs e) => _menuController.Find();
    private void Replace_Click(object? sender, RoutedEventArgs e) => _menuController.Replace();
    private void FindInFiles_Click(object? sender, RoutedEventArgs e) => _menuController.FindInFiles();
    private async void Search_KeyDown(object? sender, KeyEventArgs e) => await _menuController.HandleSearchKeyDownAsync(e, SearchBox.Text);

    private void AppendOutput(string line) => Dispatcher.UIThread.Post(() => OutputPanel.AppendOutput(line));

    private void UpdateDiagnostics()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        DiagnosticsText.Text = $"RAM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

    private void AddProjects(FileNode node)
    {
        if (Path.GetExtension(node.Name).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            _projects.Add(node);
        foreach (var child in node.Children) AddProjects(child);
    }

}
