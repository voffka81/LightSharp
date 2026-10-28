using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
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
    private readonly DotNetService _dotnet = new();
    private readonly MessageBus _messageBus = new();
    private readonly GitChangesController _gitChangesController;
    private readonly PropertiesPanelController _propertiesPanelController;
    private readonly OutputPanelController _outputPanelController;
    private readonly SolutionExplorerController _solutionExplorerController;
    private readonly EditorWorkspaceController _editorWorkspaceController;
    private readonly SearchReplaceController _searchController;
    private readonly InputHandlerService _inputHandler = new();
    private readonly ObservableCollection<FileNode> _roots = [];
    private readonly ObservableCollection<FileNode> _projects = [];
    private readonly List<IDisposable> _messageSubscriptions = [];
    private string? _workspacePath;
    private string? _solutionPath;
    private ToolDock? _solutionExplorerDock;
    private Tool? _gitChangesTool;
    private CancellationTokenSource? _commandCts;
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
        _propertiesPanelController = new PropertiesPanelController(
            _messageBus,
            () => OpenFolder_Click(this, new RoutedEventArgs()),
            () => Build_Click(this, new RoutedEventArgs()));
        _outputPanelController = new OutputPanelController(
            _messageBus, Problem_Activated,
            () => Test_Click(this, new RoutedEventArgs()));
        _solutionExplorerController = new SolutionExplorerController(
            _messageBus,
            new SolutionExplorerActionHandlers(
                OpenDocumentAsync,
                CreateFileInNodeAsync,
                NewProjectAsync,
                AddExistingProjectAsync,
                () => Build_Click(this, new RoutedEventArgs()),
                () => Test_Click(this, new RoutedEventArgs()),
                node => StartupProjectSelector.SelectedItem = node,
                node => Execute("build", "--configuration", SelectedConfiguration, node.FullPath),
                node => Execute("test", "--configuration", SelectedConfiguration, node.FullPath),
                OpenContainingFolder,
                RefreshWorkspaceAsync));
        _editorWorkspaceController = new EditorWorkspaceController(
            EditorSurface, _projects, OutputPanel, _messageBus, this,
            SolutionExplorerPanel.SelectAndReveal);
        _searchController = new SearchReplaceController(
            this, _editorWorkspaceController, EditorSurface, _roots, _projects,
            () => _solutionPath, () => _ = RefreshGitStatusAsync(), OutputPanel);
        using (var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://LightSharp/Assets/LightSharp.png")))
            Icon = new WindowIcon(iconStream);
        InitializeDockLayout();
        SolutionExplorerPanel.SetRoots(_roots);
        SolutionExplorerPanel.SetWorkspaceContext(_solutionPath, hasWorkspace: false);
        StartupProjectSelector.ItemsSource = _projects;
        GitMenuItem.IsEnabled = false;
        _messageSubscriptions.Add(_messageBus.Observe<StatusMessageChanged>().Subscribe(message => OutputPanel.StatusMessage = message.Message));
        _messageSubscriptions.Add(_messageBus.Observe<OutputLineRequested>().Subscribe(message => AppendOutput(message.Line)));
        _messageSubscriptions.Add(_messageBus.Observe<GitBranchChanged>().Subscribe(message =>
        {
            if (!string.IsNullOrEmpty(message.Branch)) BranchStatusText.Text = message.Branch;
        }));
        _messageSubscriptions.Add(_messageBus.Observe<GitRepositoryAvailabilityChanged>().Subscribe(message =>
        {
            GitMenuItem.IsEnabled = message.HasWorkspace;
            GitChangesMenuItem.IsVisible = message.HasRepository;
            RefreshGitMenuItem.IsVisible = message.HasRepository;
            GitMenuSeparator.IsVisible = message.HasRepository;
            InitializeGitMenuItem.IsVisible = message.HasWorkspace && !message.HasRepository;
            StageAllGitMenuItem.IsVisible = message.HasRepository;
            CommitGitMenuItem.IsVisible = message.HasRepository;
        }));
        _messageSubscriptions.Add(_messageBus.Observe<EditorStatusChanged>().Subscribe(message => EditorStatusText.Text = message.Message));
        _messageSubscriptions.Add(_messageBus.Observe<EditorCursorChanged>().Subscribe(message => CursorStatusText.Text = $"Ln {message.Line}, Col {message.Column}"));
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _diagnosticsTimer.Tick += (_, _) => UpdateDiagnostics();
        _diagnosticsTimer.Start();
        Closed += (_, _) =>
        {
            _diagnosticsTimer.Stop();
            _commandCts?.Cancel();
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

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open .NET workspace", AllowMultiple = false });
        var folder = folders.FirstOrDefault();
        if (folder is null) return;
        var path = folder.TryGetLocalPath();
        if (path is null) return;
        await LoadWorkspaceAsync(path);
    }

    private async void OpenSolution_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Visual Studio solution",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Visual Studio solution (*.sln, *.slnx)") { Patterns = ["*.sln", "*.slnx"] }
            ]
        });
        var solutionFile = files.FirstOrDefault();
        var solutionPath = solutionFile?.TryGetLocalPath();
        if (solutionPath is null) return;

        var directory = Path.GetDirectoryName(solutionPath);
        if (directory is null) return;
        await LoadWorkspaceAsync(directory, solutionPath);
    }

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open file",
            AllowMultiple = false
        });
        var file = files.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        await OpenDocumentAsync(new FileNode { Name = Path.GetFileName(path), FullPath = path });
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e) => await OpenFileAsync();

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

    private async Task AddExistingProjectAsync()
    {
        if (_solutionPath is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add existing C# project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("C# project (*.csproj)") { Patterns = ["*.csproj"] }]
        });
        var projectPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (projectPath is null) return;
        var solutionDirectory = Path.GetDirectoryName(_solutionPath) ?? _workspacePath!;
        if (await RunDotNetCommandAsync(solutionDirectory, "sln", _solutionPath, "add", projectPath) == 0)
            await LoadWorkspaceAsync(solutionDirectory, _solutionPath);
    }

    private async Task RefreshWorkspaceAsync()
    {
        if (_workspacePath is null) return;
        await LoadWorkspaceAsync(_workspacePath, _solutionPath);
    }

    private async void NewFile_Click(object? sender, RoutedEventArgs e)
    {
        var directory = GetDefaultNewFileDirectory();
        if (directory is not null) await CreateNewFileAsync(directory);
        else OutputPanel.StatusMessage = "Open a workspace or project first";
    }

    private Task CreateFileInNodeAsync(FileNode node)
    {
        var directory = GetNewFileDirectory(node);
        return directory is null
            ? Task.CompletedTask
            : CreateNewFileAsync(directory);
    }

    private string? GetDefaultNewFileDirectory()
    {
        if (_editorWorkspaceController.ActiveDocument is { } activeDocument)
            return Path.GetDirectoryName(activeDocument.FullPath);
        if (StartupProjectSelector.SelectedItem is FileNode startupProject)
            return Path.GetDirectoryName(startupProject.FullPath);
        if (_projects.FirstOrDefault() is FileNode project)
            return Path.GetDirectoryName(project.FullPath);
        return _workspacePath;
    }

    private string? GetNewFileDirectory(FileNode node)
    {
        if (string.Equals(node.FullPath, _solutionPath, StringComparison.OrdinalIgnoreCase))
        {
            var project = StartupProjectSelector.SelectedItem as FileNode ?? _projects.FirstOrDefault();
            return project is not null
                ? Path.GetDirectoryName(project.FullPath)
                : Path.GetDirectoryName(node.FullPath);
        }
        if (Path.GetExtension(node.FullPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(node.FullPath);
        return node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
    }

    private async Task CreateNewFileAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            OutputPanel.StatusMessage = "The selected folder no longer exists";
            return;
        }

        var dialog = new NewFileDialog();
        if (await dialog.ShowDialog<bool>(this) is false) return;
        var filePath = Path.GetFullPath(Path.Combine(directory, dialog.FileName));
        if (File.Exists(filePath) || Directory.Exists(filePath))
        {
            OutputPanel.StatusMessage = $"A file named {dialog.FileName} already exists";
            return;
        }

        try
        {
            await File.WriteAllTextAsync(filePath, string.Empty);
            if (_workspacePath is not null)
                await LoadWorkspaceAsync(_workspacePath, _solutionPath);

            var node = Flatten(_roots).FirstOrDefault(item =>
                string.Equals(item.FullPath, filePath, StringComparison.OrdinalIgnoreCase))
                ?? new FileNode { Name = Path.GetFileName(filePath), FullPath = filePath };
            await OpenDocumentAsync(node);
            OutputPanel.StatusMessage = $"Created {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to create file: {ex.Message}");
            OutputPanel.StatusMessage = "File creation failed";
        }
    }

    private static void OpenContainingFolder(FileNode node)
    {
        var path = node.IsDirectory && Directory.Exists(node.FullPath)
            ? node.FullPath
            : Path.GetDirectoryName(node.FullPath);
        if (path is null || !Directory.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private Task OpenDocumentAsync(FileNode node) => _editorWorkspaceController.OpenDocumentAsync(node);

    private async void Save_Click(object? sender, RoutedEventArgs e) => await _editorWorkspaceController.SaveActiveDocumentAsync();

    private Task SaveAllAsync() => _editorWorkspaceController.SaveAllAsync();
    private async void SaveAll_Click(object? sender, RoutedEventArgs e) => await SaveAllAsync();

    private string SelectedConfiguration =>
        (ConfigurationSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Debug";

    private async void NewProject_Click(object? sender, RoutedEventArgs e) => await NewProjectAsync();

    private async Task NewProjectAsync()
    {
        var defaultLocation = _solutionPath is not null
            ? Path.GetDirectoryName(_solutionPath)
            : _workspacePath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(defaultLocation))
            defaultLocation = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new NewProjectDialog(defaultLocation, _solutionPath is not null);
        if (await dialog.ShowDialog<bool>(this) is false) return;

        var projectDirectory = Path.Combine(dialog.Location, dialog.ProjectName);
        var created = await RunDotNetCommandAsync(dialog.Location,
            "new", dialog.TemplateShortName, "--name", dialog.ProjectName, "--output", projectDirectory);
        if (created != 0) return;

        var projectFile = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (projectFile is null)
        {
            AppendOutput("The selected template did not create a C# project file.");
            OutputPanel.StatusMessage = "Project creation failed";
            return;
        }

        var solutionPath = _solutionPath;
        if (solutionPath is null || !dialog.AddToSolution)
        {
            var createSolution = await RunDotNetCommandAsync(dialog.Location,
                "new", "sln", "--name", dialog.ProjectName, "--output", dialog.Location);
            if (createSolution != 0) return;
            solutionPath = Directory.EnumerateFiles(dialog.Location, $"{dialog.ProjectName}.sln*")
                .FirstOrDefault(file => Path.GetExtension(file) is ".sln" or ".slnx");
        }

        if (solutionPath is null)
        {
            AppendOutput("The solution file could not be found after creation.");
            OutputPanel.StatusMessage = "Project created, solution setup failed";
            return;
        }

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? dialog.Location;
        var added = await RunDotNetCommandAsync(solutionDirectory, "sln", solutionPath, "add", projectFile);
        if (added != 0) return;

        await LoadWorkspaceAsync(solutionDirectory, solutionPath);
        StartupProjectSelector.SelectedItem = _projects.FirstOrDefault(project =>
            string.Equals(project.FullPath, projectFile, StringComparison.OrdinalIgnoreCase));
        OutputPanel.StatusMessage = $"Created {dialog.ProjectName}";
    }

    private void Build_Click(object? s, RoutedEventArgs e) => Execute("build", "--configuration", SelectedConfiguration);
    private void Test_Click(object? s, RoutedEventArgs e) => Execute("test", "--configuration", SelectedConfiguration);
    private void Start_Click(object? s, RoutedEventArgs e)
    {
        if (StartupProjectSelector.SelectedItem is FileNode project)
            Execute("run", "--configuration", SelectedConfiguration, "--project", project.FullPath);
        else
            Execute("run", "--configuration", SelectedConfiguration);
    }

    private void Stop_Click(object? s, RoutedEventArgs e)
    {
        _commandCts?.Cancel();
        _dotnet.Stop();
        StopButton.IsEnabled = false;
        OutputPanel.StatusMessage = "Stopped";
    }

    private async void Execute(params string[] arguments)
    {
        if (_workspacePath is null) { AppendOutput("Open a workspace first."); return; }
        await RunDotNetCommandAsync(_workspacePath, arguments);
    }

    private async Task<int> RunDotNetCommandAsync(string workingDirectory, params string[] arguments)
    {
        _commandCts?.Cancel();
        _commandCts?.Dispose();
        var commandCts = new CancellationTokenSource();
        _commandCts = commandCts;
        OutputPanel.ClearOutput();
        var isTestRun = arguments.FirstOrDefault()?.Equals("test", StringComparison.OrdinalIgnoreCase) == true;
        ShowOutputView(isTestRun ? "Tests" : "Output");
        OutputPanel.IsVisible = true;
        var command = string.Join(" ", arguments);
        OutputPanel.StatusMessage = $"dotnet {command}...";
        StopButton.IsEnabled = true;
        var exitCode = await _dotnet.ExecuteAsync(workingDirectory, arguments, AppendOutput, commandCts.Token);
        if (!ReferenceEquals(_commandCts, commandCts)) return exitCode;
        _commandCts.Dispose();
        _commandCts = null;
        StopButton.IsEnabled = false;
        OutputPanel.StatusMessage = $"dotnet {command} exited with code {exitCode}";
        if (isTestRun)
            OutputPanel.TestSummary = exitCode == 0
                ? "Test run completed successfully. See Output for detailed results."
                : $"Test run failed (exit code {exitCode}). See Output for details.";
        return exitCode;
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e) => _editorWorkspaceController.SelectAllIfFocused();
    private void Comment_Click(object? sender, RoutedEventArgs e) => _editorWorkspaceController.ToggleLineComment(uncomment: false);
    private void Uncomment_Click(object? sender, RoutedEventArgs e) => _editorWorkspaceController.ToggleLineComment(uncomment: true);
    private async void FormatDocument_Click(object? sender, RoutedEventArgs e) => await _editorWorkspaceController.FormatActiveDocumentAsync(selectionOnly: false);
    private async void FormatSelection_Click(object? sender, RoutedEventArgs e) => await _editorWorkspaceController.FormatActiveDocumentAsync(selectionOnly: true);

    private void FocusOutput_Click(object? sender, RoutedEventArgs e)
    {
        OutputPanel.IsVisible = true;
        ShowOutputView("Output");
        OutputPanel.FocusOutput();
    }

    private void ShowProblems_Click(object? sender, RoutedEventArgs e) => ShowOutputView("Problems");
    private void ShowOutput_Click(object? sender, RoutedEventArgs e) => ShowOutputView("Output");
    private void ShowTests_Click(object? sender, RoutedEventArgs e) => ShowOutputView("Tests");

    private void ShowOutputView(string view) => OutputPanel.ShowView(view);

    private void ShowGitChanges_Click(object? sender, RoutedEventArgs e)
    {
        if (_solutionExplorerDock is not null && _gitChangesTool is not null)
            _solutionExplorerDock.ActiveDockable = _gitChangesTool;
        _ = RefreshGitStatusAsync();
    }

    private async void RefreshGit_Click(object? sender, RoutedEventArgs e) => await _gitChangesController.RefreshAsync();

    private Task RefreshGitStatusAsync() => _gitChangesController.SetWorkspaceAsync(_workspacePath);

    private async void InitializeGit_Click(object? sender, RoutedEventArgs e)
    {
        await _gitChangesController.InitializeAsync();
        ShowGitChanges_Click(this, new RoutedEventArgs());
    }

    private async void StageAllGit_Click(object? sender, RoutedEventArgs e) => await _gitChangesController.StageAllAsync();
    private void CommitGit_Click(object? sender, RoutedEventArgs e) => ShowGitChanges_Click(sender, e);
    private void FetchGit_Click(object? sender, RoutedEventArgs e) => _messageBus.Publish(new GitFetchRequested());
    private void PullGit_Click(object? sender, RoutedEventArgs e) => _messageBus.Publish(new GitPullRequested());
    private void PushGit_Click(object? sender, RoutedEventArgs e) => _messageBus.Publish(new GitPushRequested());

    private void Problem_Activated(EditorProblem problem)
    {
        _editorWorkspaceController.NavigateTo(problem.Line, problem.Column);
    }
    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var result = _inputHandler.HandleWindowKeyDown(e.Key, e.KeyModifiers);
        if (!result.Handled) return;

        e.Handled = true;
        switch (result.Command)
        {
            case InputCommand.Find: _searchController.ShowSearchWindow(searchEntireSolution: false); break;
            case InputCommand.Replace: _searchController.ShowSearchWindow(searchEntireSolution: false, replaceMode: true); break;
            case InputCommand.FindInFiles: _searchController.ShowSearchWindow(searchEntireSolution: true); break;
            case InputCommand.OpenSolution: OpenSolution_Click(this, new RoutedEventArgs()); break;
            case InputCommand.OpenFile: await OpenFileAsync(); break;
            case InputCommand.NewProject: NewProject_Click(this, new RoutedEventArgs()); break;
            case InputCommand.Save: Save_Click(this, new RoutedEventArgs()); break;
            case InputCommand.SaveAll: await SaveAllAsync(); break;
            case InputCommand.Build: Build_Click(this, new RoutedEventArgs()); break;
            case InputCommand.Start: Start_Click(this, new RoutedEventArgs()); break;
            case InputCommand.Stop: Stop_Click(this, new RoutedEventArgs()); break;
            case InputCommand.CloseDocument:
                if (_editorWorkspaceController.ActiveDocument is { } activeDocument)
                    await _editorWorkspaceController.CloseDocumentAsync(activeDocument);
                break;
            case InputCommand.SelectAll:
                _editorWorkspaceController.SelectAllIfFocused();
                break;
            case InputCommand.NextDocument: _editorWorkspaceController.ActivateAdjacentDocument(1); break;
            case InputCommand.PreviousDocument: _editorWorkspaceController.ActivateAdjacentDocument(-1); break;
            case InputCommand.Test: Test_Click(this, new RoutedEventArgs()); break;
            case InputCommand.ToggleLineComment: _editorWorkspaceController.ToggleLineComment(uncomment: false); break;
            case InputCommand.UncommentLine: _editorWorkspaceController.ToggleLineComment(uncomment: true); break;
            case InputCommand.FormatDocument: _ = _editorWorkspaceController.FormatActiveDocumentAsync(selectionOnly: false); break;
            case InputCommand.FormatSelection: _ = _editorWorkspaceController.FormatActiveDocumentAsync(selectionOnly: true); break;
        }
    }

    private void Find_Click(object? sender, RoutedEventArgs e) => _searchController.ShowSearchWindow(searchEntireSolution: false);
    private void Replace_Click(object? sender, RoutedEventArgs e) => _searchController.ShowSearchWindow(searchEntireSolution: false, replaceMode: true);
    private void FindInFiles_Click(object? sender, RoutedEventArgs e) => _searchController.ShowSearchWindow(searchEntireSolution: true);
    private async void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        await _searchController.OpenFileMatchingAsync(SearchBox.Text);
        e.Handled = true;
    }
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

    private static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
