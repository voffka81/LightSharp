using Avalonia.Controls;
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
    private readonly MessageBus _messageBus = new();
    private readonly WorkspaceController _workspaceController;
    private readonly GitChangesController _gitChangesController;
    private readonly PropertiesPanelController _propertiesPanelController;
    private readonly OutputPanelController _outputPanelController;
    private readonly SolutionExplorerController _solutionExplorerController;
    private readonly EditorWorkspaceController _editorWorkspaceController;
    private readonly SearchReplaceController _searchController;
    private DotNetCommandController _dotNetCommandController = null!;
    private WorkspaceMenuController _workspaceMenuController = null!;
    private MainWindowMenuController _menuController = null!;
    private readonly List<IDisposable> _messageSubscriptions = [];
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
        _workspaceController = new WorkspaceController(
            new WorkspaceService(), OutputPanel, PropertiesPanel, SolutionExplorerPanel,
            MainToolbar, _gitChangesController,
            () => _editorWorkspaceController?.ActiveDocument?.FullPath);
        _editorWorkspaceController = new EditorWorkspaceController(
            EditorSurface, _workspaceController.Projects, OutputPanel, _messageBus, this,
            SolutionExplorerPanel.SelectAndReveal);
        _searchController = new SearchReplaceController(
            this, _editorWorkspaceController, EditorSurface, _workspaceController.Roots,
            _workspaceController.Projects, () => _workspaceController.SolutionPath,
            () => _ = RefreshGitStatusAsync(), OutputPanel);
        using (var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://LightSharp/Assets/LightSharp.png")))
            Icon = new WindowIcon(iconStream);
        InitializeDockLayout();
        _dotNetCommandController = new DotNetCommandController(
            new DotNetService(), OutputPanel, MainToolbar.SetStopEnabled,
            () => _workspaceController.WorkspacePath, () => SelectedConfiguration,
            () => MainToolbar.SelectedStartupProject, AppendOutput);
        _workspaceMenuController = new WorkspaceMenuController(
            this, _dotNetCommandController, _workspaceController, _editorWorkspaceController,
            OutputPanel, () => MainToolbar.SelectedStartupProject, MainToolbar.SetStartupProject);
        _menuController = new MainWindowMenuController(
            _messageBus, _editorWorkspaceController, _searchController, _gitChangesController,
            OutputPanel, new InputHandlerService(), _workspaceMenuController, _dotNetCommandController,
            RefreshGitStatusAsync,
            _solutionExplorerDock!, _gitChangesTool!);
        MainMenu.AttachController(_menuController, _messageBus, this);
        MainToolbar.AttachController(_menuController);
        _propertiesPanelController = new PropertiesPanelController(
            _messageBus, () => _menuController.OpenFolderAsync(), () => _menuController.BuildAsync());
        _outputPanelController = new OutputPanelController(
            _messageBus, Problem_Activated, () => _menuController.TestAsync());
        _solutionExplorerController = new SolutionExplorerController(
            _messageBus,
            new SolutionExplorerActionHandlers(
                _editorWorkspaceController.OpenDocumentAsync,
                _workspaceMenuController.CreateFileInNodeAsync,
                _workspaceMenuController.NewProjectAsync,
                _workspaceMenuController.AddExistingProjectAsync,
                () => _ = _menuController.BuildAsync(),
                () => _ = _menuController.TestAsync(),
                MainToolbar.SetStartupProject,
                node => _ = _dotNetCommandController.ExecuteAsync("build", "--configuration", SelectedConfiguration, node.FullPath),
                node => _ = _dotNetCommandController.ExecuteAsync("test", "--configuration", SelectedConfiguration, node.FullPath),
                WorkspaceMenuController.OpenContainingFolder,
                _workspaceMenuController.RefreshWorkspaceAsync));
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
            MainMenu.Dispose();
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

    private string SelectedConfiguration =>
        MainToolbar.SelectedConfiguration;

    private Task RefreshGitStatusAsync() => _gitChangesController.SetWorkspaceAsync(_workspaceController.WorkspacePath);

    private void Problem_Activated(EditorProblem problem)
    {
        _editorWorkspaceController.NavigateTo(problem.Line, problem.Column);
    }

    private void AppendOutput(string line) => Dispatcher.UIThread.Post(() => OutputPanel.AppendOutput(line));

    private void UpdateDiagnostics()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        DiagnosticsText.Text = $"RAM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

}
