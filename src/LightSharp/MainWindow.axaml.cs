using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using LightSharp.Models;
using LightSharp.Services;
using Microsoft.CodeAnalysis.CSharp;

namespace LightSharp;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceService _workspace = new();
    private readonly DotNetService _dotnet = new();
    private readonly GitService _git = new();
    private readonly CSharpCompletionService _completionService = new();
    private readonly SyntaxErrorRenderer _syntaxErrorRenderer;
    private readonly SemanticColorizingTransformer _semanticColorizer;
    private readonly ObservableCollection<FileNode> _roots = [];
    private readonly ObservableCollection<FileNode> _projects = [];
    private readonly ObservableCollection<EditorDocument> _documents = [];
    private readonly ObservableCollection<EditorProblem> _problems = [];
    private readonly ObservableCollection<GitChange> _gitChanges = [];
    private string? _workspacePath;
    private string? _solutionPath;
    private string? _gitRepositoryPath;
    private ToolDock? _solutionExplorerDock;
    private Tool? _gitChangesTool;
    private EditorDocument? _activeDocument;
    private bool _loadingDocument;
    private bool _gitOperationRunning;
    private CancellationTokenSource? _commandCts;
    private CancellationTokenSource? _completionRequestCts;
    private CancellationTokenSource? _semanticDiagnosticsCts;
    private CancellationTokenSource? _quickInfoCts;
    private CompletionWindow? _completionWindow;
    private readonly DispatcherTimer _diagnosticsTimer;
    private readonly DispatcherTimer _syntaxDiagnosticsTimer;
    private readonly DispatcherTimer _quickInfoTimer;
    private string? _shortcutPrefix;
    private int _hoverOffset = -1;
    private bool _suppressNextCompletionNewline;
    private bool _suppressNextCompletionPeriod;

    public MainWindow()
    {
        InitializeComponent();
        using (var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://LightSharp/Assets/LightSharp.png")))
            Icon = new WindowIcon(iconStream);
        _syntaxErrorRenderer = new SyntaxErrorRenderer(Editor);
        _semanticColorizer = new SemanticColorizingTransformer();
        Editor.TextArea.TextView.LineTransformers.Add(_semanticColorizer);
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.TextArea.IndentationStrategy = new AvaloniaEdit.Indentation.CSharp.CSharpIndentationStrategy(Editor.Options);
        InitializeDockLayout();
        FileTree.ItemsSource = _roots;
        StartupProjectSelector.ItemsSource = _projects;
        DocumentTabs.ItemsSource = _documents;
        ProblemsList.ItemsSource = _problems;
        GitChangesList.ItemsSource = _gitChanges;
        GitMenuItem.IsEnabled = false;
        GitEmptyText.Text = "Open a workspace to connect Git.";
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCursorStatus();
        Editor.TextArea.TextEntered += Editor_TextEntered;
        Editor.TextArea.TextEntering += Editor_TextEntering;
        Closed += (_, _) =>
        {
            _completionRequestCts?.Cancel();
            _semanticDiagnosticsCts?.Cancel();
            _quickInfoCts?.Cancel();
            _quickInfoTimer?.Stop();
            _completionWindow?.Hide();
            _completionService.Dispose();
        };
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _diagnosticsTimer.Tick += (_, _) => UpdateDiagnostics();
        _diagnosticsTimer.Start();
        _syntaxDiagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _syntaxDiagnosticsTimer.Tick += (_, _) =>
        {
            _syntaxDiagnosticsTimer.Stop();
            UpdateSyntaxDiagnostics();
        };
        _quickInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _quickInfoTimer.Tick += async (_, _) => await ShowQuickInfoAsync();
        Editor.TextArea.TextView.PointerMoved += EditorTextView_PointerMoved;
        Editor.TextArea.TextView.PointerExited += EditorTextView_PointerExited;
        UpdateDiagnostics();
        UpdateCursorStatus();
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
        WorkspaceDetailText.Text = path;
        StatusText.Text = "Loading workspace...";
        _roots.Clear();
        _projects.Clear();
        var nodes = await _workspace.LoadAsync(path);
        var solution = nodes.FirstOrDefault(node =>
            (selectedSolutionPath is null || string.Equals(node.FullPath, selectedSolutionPath, StringComparison.OrdinalIgnoreCase)) &&
            (Path.GetExtension(node.Name).Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(node.Name).Equals(".slnx", StringComparison.OrdinalIgnoreCase)));
        _solutionPath = solution?.FullPath;

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
        SyncSolutionExplorerToActiveDocument();
        StatusText.Text = "Ready";
        await RefreshGitStatusAsync();
    }

    private async void FileTree_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FileTree.SelectedItem is not FileNode { IsDirectory: false } node) return;
        await OpenDocumentAsync(node);
    }

    private void FileTreeItem_RightTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control { DataContext: FileNode node } control) return;
        FileTree.SelectedItem = node;

        var menu = new ContextMenu();
        void Add(string header, EventHandler<RoutedEventArgs> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, args) => action(s ?? item, args);
            menu.Items.Add(item);
        }
        void Separator() => menu.Items.Add(new Separator());

        var isSolution = string.Equals(node.FullPath, _solutionPath, StringComparison.OrdinalIgnoreCase);
        var isProject = Path.GetExtension(node.Name).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

        if (!node.IsDirectory)
            Add("Open", async (_, _) => await OpenDocumentAsync(node));

        Add("Add > New File...", async (_, _) => await CreateFileInNodeAsync(node));

        if (isSolution)
        {
            if (menu.Items.Count > 0) Separator();
            Add("Add > New Project...", (_, _) => NewProject_Click(this, new RoutedEventArgs()));
            Add("Add > Existing Project...", async (_, _) => await AddExistingProjectAsync());
            Separator();
            Add("Build Solution", (_, _) => Build_Click(this, new RoutedEventArgs()));
            Add("Run Tests", (_, _) => Test_Click(this, new RoutedEventArgs()));
        }
        else if (isProject)
        {
            Separator();
            Add("Set as Startup Project", (_, _) => StartupProjectSelector.SelectedItem = node);
            Add("Build", (_, _) => Execute("build", "--configuration", SelectedConfiguration, node.FullPath));
            Add("Run Tests", (_, _) => Execute("test", "--configuration", SelectedConfiguration, node.FullPath));
        }

        Separator();
        Add("Open Containing Folder", (_, _) => OpenContainingFolder(node));
        if (_workspacePath is not null)
        {
            Separator();
            Add("Refresh", async (_, _) => await RefreshWorkspaceAsync());
        }

        menu.Placement = PlacementMode.Pointer;
        control.ContextMenu = menu;
        menu.Open(control);
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
        else StatusText.Text = "Open a workspace or project first";
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
        if (_activeDocument is not null)
            return Path.GetDirectoryName(_activeDocument.FullPath);
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
            StatusText.Text = "The selected folder no longer exists";
            return;
        }

        var dialog = new NewFileDialog();
        if (await dialog.ShowDialog<bool>(this) is false) return;
        var filePath = Path.GetFullPath(Path.Combine(directory, dialog.FileName));
        if (File.Exists(filePath) || Directory.Exists(filePath))
        {
            StatusText.Text = $"A file named {dialog.FileName} already exists";
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
            StatusText.Text = $"Created {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to create file: {ex.Message}");
            StatusText.Text = "File creation failed";
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

    private async Task OpenDocumentAsync(FileNode node)
    {
        try
        {
            var document = _documents.FirstOrDefault(item =>
                string.Equals(item.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
            if (document is null)
            {
                document = new EditorDocument(node.FullPath, await File.ReadAllTextAsync(node.FullPath));
                _documents.Add(document);
            }
            ActivateDocument(document);
        }
        catch (Exception ex) { AppendOutput(ex.Message); }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeDocument is null) return;
        try
        {
            await File.WriteAllTextAsync(_activeDocument.FullPath, _activeDocument.Text);
            _activeDocument.MarkSaved();
            StatusText.Text = $"Saved {_activeDocument.Name}";
        }
        catch (Exception ex) { AppendOutput(ex.Message); }
    }

    private async Task SaveAllAsync()
    {
        foreach (var document in _documents.Where(document => document.IsDirty).ToArray())
        {
            try
            {
                await File.WriteAllTextAsync(document.FullPath, document.Text);
                document.MarkSaved();
            }
            catch (Exception ex)
            {
                AppendOutput($"Unable to save {document.Name}: {ex.Message}");
                StatusText.Text = $"Could not save {document.Name}";
                return;
            }
        }
        StatusText.Text = "All files saved";
    }

    private async void SaveAll_Click(object? sender, RoutedEventArgs e) => await SaveAllAsync();

    private string SelectedConfiguration =>
        (ConfigurationSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Debug";

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
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
            StatusText.Text = "Project creation failed";
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
            StatusText.Text = "Project created, solution setup failed";
            return;
        }

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? dialog.Location;
        var added = await RunDotNetCommandAsync(solutionDirectory, "sln", solutionPath, "add", projectFile);
        if (added != 0) return;

        await LoadWorkspaceAsync(solutionDirectory, solutionPath);
        StartupProjectSelector.SelectedItem = _projects.FirstOrDefault(project =>
            string.Equals(project.FullPath, projectFile, StringComparison.OrdinalIgnoreCase));
        StatusText.Text = $"Created {dialog.ProjectName}";
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
        StatusText.Text = "Stopped";
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
        Output.Text = string.Empty;
        var isTestRun = arguments.FirstOrDefault()?.Equals("test", StringComparison.OrdinalIgnoreCase) == true;
        ShowOutputView(isTestRun ? "Tests" : "Output");
        OutputPanel.IsVisible = true;
        var command = string.Join(" ", arguments);
        StatusText.Text = $"dotnet {command}...";
        StopButton.IsEnabled = true;
        var exitCode = await _dotnet.ExecuteAsync(workingDirectory, arguments, AppendOutput, commandCts.Token);
        if (!ReferenceEquals(_commandCts, commandCts)) return exitCode;
        _commandCts.Dispose();
        _commandCts = null;
        StopButton.IsEnabled = false;
        StatusText.Text = $"dotnet {command} exited with code {exitCode}";
        if (isTestRun)
            TestsSummaryText.Text = exitCode == 0
                ? "Test run completed successfully. See Output for detailed results."
                : $"Test run failed (exit code {exitCode}). See Output for details.";
        return exitCode;
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e) => Editor.SelectAll();

    private void FocusOutput_Click(object? sender, RoutedEventArgs e)
    {
        OutputPanel.IsVisible = true;
        ShowOutputView("Output");
        Output.Focus();
    }

    private void ShowProblems_Click(object? sender, RoutedEventArgs e) => ShowOutputView("Problems");
    private void ShowOutput_Click(object? sender, RoutedEventArgs e) => ShowOutputView("Output");
    private void ShowTests_Click(object? sender, RoutedEventArgs e) => ShowOutputView("Tests");

    private void ShowOutputView(string view)
    {
        ProblemsTabButton.Classes.Set("selected", view == "Problems");
        OutputTabButton.Classes.Set("selected", view == "Output");
        TestsTabButton.Classes.Set("selected", view == "Tests");
        ProblemsList.IsVisible = view == "Problems";
        Output.IsVisible = view == "Output";
        TestsContent.IsVisible = view == "Tests";
    }

    private void ShowGitChanges_Click(object? sender, RoutedEventArgs e)
    {
        if (_solutionExplorerDock is not null && _gitChangesTool is not null)
            _solutionExplorerDock.ActiveDockable = _gitChangesTool;
        _ = RefreshGitStatusAsync();
    }

    private async void RefreshGit_Click(object? sender, RoutedEventArgs e) => await RefreshGitStatusAsync();

    private async Task RefreshGitStatusAsync()
    {
        _gitChanges.Clear();
        _gitRepositoryPath = _workspacePath is null ? null : _git.FindRepositoryRoot(_workspacePath);
        var hasRepository = _gitRepositoryPath is not null;
        GitMenuItem.IsEnabled = _workspacePath is not null;
        GitChangesMenuItem.IsVisible = hasRepository;
        RefreshGitMenuItem.IsVisible = hasRepository;
        GitMenuSeparator.IsVisible = hasRepository;
        InitializeGitMenuItem.IsVisible = _workspacePath is not null && !hasRepository;
        StageAllGitMenuItem.IsVisible = hasRepository;
        CommitGitMenuItem.IsVisible = hasRepository;
        SetGitOperationButtonsEnabled(hasRepository && !_gitOperationRunning);

        if (!hasRepository)
        {
            BranchStatusText.Text = "⎇  No repository";
            GitRepositoryText.Text = "Not a Git repository";
            GitEmptyText.Text = _workspacePath is null ? "Open a workspace to connect Git." : "Use Git > Initialize Repository to create a repository.";
            GitEmptyText.IsVisible = true;
            return;
        }

        try
        {
            var status = await _git.GetStatusAsync(_gitRepositoryPath!);
            BranchStatusText.Text = $"⎇  {status.Branch}";
            GitRepositoryText.Text = $"{_gitRepositoryPath} · {status.Branch}";
            foreach (var change in status.Changes) _gitChanges.Add(change);
            GitEmptyText.IsVisible = _gitChanges.Count == 0;
            GitEmptyText.Text = _gitChanges.Count == 0 ? "No changes detected." : string.Empty;
        }
        catch (Exception ex)
        {
            GitRepositoryText.Text = _gitRepositoryPath!;
            GitEmptyText.Text = ex.Message;
            GitEmptyText.IsVisible = true;
            AppendOutput($"Git status unavailable: {ex.Message}");
        }
    }

    private async void InitializeGit_Click(object? sender, RoutedEventArgs e)
    {
        if (_workspacePath is null) return;
        try
        {
            await _git.InitializeAsync(_workspacePath);
            await RefreshGitStatusAsync();
            ShowGitChanges_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to initialize Git repository: {ex.Message}");
            StatusText.Text = "Git initialization failed";
        }
    }

    private async void StageAllGit_Click(object? sender, RoutedEventArgs e)
    {
        if (_gitRepositoryPath is null) return;
        try
        {
            await _git.StageAllAsync(_gitRepositoryPath);
            await RefreshGitStatusAsync();
            StatusText.Text = "All changes staged";
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to stage Git changes: {ex.Message}");
            StatusText.Text = "Git staging failed";
        }
    }

    private async void CommitGit_Click(object? sender, RoutedEventArgs e)
    {
        if (_gitRepositoryPath is null) return;
        var message = GitCommitMessageBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = "Enter a commit message";
            GitCommitMessageBox.Focus();
            return;
        }

        try
        {
            var result = await _git.CommitAllAsync(_gitRepositoryPath, message);
            AppendOutput(result);
            GitCommitMessageBox.Clear();
            await RefreshGitStatusAsync();
            StatusText.Text = "Commit created";
        }
        catch (Exception ex)
        {
            AppendOutput($"Git commit failed: {ex.Message}");
            StatusText.Text = "Git commit failed";
        }
    }

    private async void FetchGit_Click(object? sender, RoutedEventArgs e) =>
        await RunGitRemoteOperationAsync("fetch", _git.FetchAsync);

    private async void PullGit_Click(object? sender, RoutedEventArgs e) =>
        await RunGitRemoteOperationAsync("pull", _git.PullAsync);

    private async void PushGit_Click(object? sender, RoutedEventArgs e) =>
        await RunGitRemoteOperationAsync("push", _git.PushAsync);

    private async Task RunGitRemoteOperationAsync(string operation, Func<string, CancellationToken, Task<string>> run)
    {
        if (_gitRepositoryPath is null || _gitOperationRunning) return;

        var repositoryPath = _gitRepositoryPath;
        _gitOperationRunning = true;
        SetGitOperationButtonsEnabled(false);
        StatusText.Text = $"Git {operation} in progress...";
        try
        {
            var output = await run(repositoryPath, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(output)) AppendOutput(output);
            await RefreshGitStatusAsync();
            StatusText.Text = $"Git {operation} completed";
        }
        catch (Exception ex)
        {
            AppendOutput($"Git {operation} failed: {ex.Message}");
            StatusText.Text = $"Git {operation} failed";
        }
        finally
        {
            _gitOperationRunning = false;
            SetGitOperationButtonsEnabled(_gitRepositoryPath is not null);
        }
    }

    private void SetGitOperationButtonsEnabled(bool enabled)
    {
        GitRefreshButton.IsEnabled = enabled;
        GitStageAllButton.IsEnabled = enabled;
        GitFetchButton.IsEnabled = enabled;
        GitPullButton.IsEnabled = enabled;
        GitPushButton.IsEnabled = enabled;
    }

    private void Problem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProblemsList.SelectedItem is not EditorProblem problem || Editor.Document is null) return;
        var line = Editor.Document.GetLineByNumber(Math.Clamp(problem.Line, 1, Editor.Document.LineCount));
        Editor.CaretOffset = Math.Min(line.Offset + problem.Column - 1, line.EndOffset);
        Editor.ScrollToLine(line.LineNumber);
        Editor.Focus();
    }

    private void DocumentTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EditorDocument document }) ActivateDocument(document);
    }

    private async void CloseDocument_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: EditorDocument document }) return;
        await CloseDocumentAsync(document);
    }

    private async Task CloseDocumentAsync(EditorDocument document)
    {
        if (document.IsDirty)
        {
            var decision = await new SaveChangesDialog(document.Name).ShowDialog<SaveChangesChoice>(this);
            if (decision == SaveChangesChoice.Cancel) return;
            if (decision == SaveChangesChoice.Save)
            {
                try
                {
                    await File.WriteAllTextAsync(document.FullPath, document.Text);
                    document.MarkSaved();
                }
                catch (Exception ex)
                {
                    AppendOutput($"Unable to save {document.Name}: {ex.Message}");
                    StatusText.Text = $"Could not save {document.Name}";
                    return;
                }
            }
        }

        var wasActive = ReferenceEquals(_activeDocument, document);
        _documents.Remove(document);
        if (wasActive) ActivateDocument(_documents.LastOrDefault());
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.Key;
        var modifiers = e.KeyModifiers;
        var ctrl = (modifiers & KeyModifiers.Control) != 0;
        var shift = (modifiers & KeyModifiers.Shift) != 0;

        if (_shortcutPrefix is not null)
        {
            var prefix = _shortcutPrefix;
            _shortcutPrefix = null;
            if (prefix == "Ctrl+R" && key == Key.T && ctrl)
            {
                e.Handled = true;
                Test_Click(this, new RoutedEventArgs());
                return;
            }
            if (prefix == "Ctrl+K" && ctrl && key is Key.C or Key.U or Key.D or Key.F)
            {
                e.Handled = true;
                if (key is Key.C or Key.U)
                    ToggleLineComment(uncomment: key == Key.U);
                else
                    _ = FormatActiveDocumentAsync(selectionOnly: key == Key.F);
                return;
            }
        }

        if (ctrl && key == Key.K && !shift)
        {
            _shortcutPrefix = "Ctrl+K";
            e.Handled = true;
            return;
        }
        if (ctrl && key == Key.R && !shift)
        {
            _shortcutPrefix = "Ctrl+R";
            e.Handled = true;
            return;
        }

        if (ctrl && shift && key == Key.O)
        {
            e.Handled = true;
            OpenSolution_Click(this, new RoutedEventArgs());
        }
        else if (ctrl && key == Key.O)
        {
            e.Handled = true;
            await OpenFileAsync();
        }
        else if (ctrl && shift && key == Key.N)
        {
            e.Handled = true;
            NewProject_Click(this, new RoutedEventArgs());
        }
        else if (ctrl && shift && key == Key.S)
        {
            e.Handled = true;
            await SaveAllAsync();
        }
        else if (ctrl && key == Key.S)
        {
            e.Handled = true;
            Save_Click(this, new RoutedEventArgs());
        }
        else if (ctrl && shift && key == Key.B)
        {
            e.Handled = true;
            Build_Click(this, new RoutedEventArgs());
        }
        else if (key == Key.F5 && shift)
        {
            e.Handled = true;
            Stop_Click(this, new RoutedEventArgs());
        }
        else if (key == Key.F5 && ctrl)
        {
            e.Handled = true;
            Start_Click(this, new RoutedEventArgs());
        }
        else if (key == Key.F5)
        {
            e.Handled = true;
            Start_Click(this, new RoutedEventArgs());
        }
        else if (ctrl && key == Key.F4)
        {
            e.Handled = true;
            if (_activeDocument is not null) await CloseDocumentAsync(_activeDocument);
        }
        else if (ctrl && key == Key.A)
        {
            e.Handled = true;
            if (Editor.IsFocused) Editor.SelectAll();
        }
        else if (ctrl && key == Key.Tab)
        {
            e.Handled = true;
            ActivateAdjacentDocument(shift ? -1 : 1);
        }
    }

    private void ActivateAdjacentDocument(int direction)
    {
        if (_documents.Count == 0) return;
        var currentIndex = _activeDocument is null ? -1 : _documents.IndexOf(_activeDocument);
        var nextIndex = (currentIndex + direction + _documents.Count) % _documents.Count;
        ActivateDocument(_documents[nextIndex]);
    }

    private void ToggleLineComment(bool uncomment)
    {
        var document = Editor.Document;
        var selectionStart = Editor.SelectionStart;
        var selectionLength = Editor.SelectionLength;
        var startLine = document.GetLineByOffset(selectionLength == 0 ? Editor.CaretOffset : selectionStart);
        var endOffset = selectionLength == 0 ? Editor.CaretOffset : Math.Max(selectionStart, selectionStart + selectionLength - 1);
        var endLine = document.GetLineByOffset(endOffset);
        var lines = Enumerable.Range(startLine.LineNumber, endLine.LineNumber - startLine.LineNumber + 1)
            .Select(number => document.GetLineByNumber(number)).ToArray();

        document.BeginUpdate();
        try
        {
            foreach (var line in lines.Reverse())
            {
                var text = document.GetText(line);
                var indentation = text.Length - text.TrimStart().Length;
                var commentOffset = line.Offset + indentation;
                if (uncomment)
                {
                    if (text.AsSpan(indentation).StartsWith("//", StringComparison.Ordinal))
                        document.Remove(commentOffset, 2);
                }
                else if (!text.AsSpan(indentation).StartsWith("//", StringComparison.Ordinal))
                    document.Insert(commentOffset, "//");
            }
        }
        finally { document.EndUpdate(); }
    }

    private void Comment_Click(object? sender, RoutedEventArgs e) => ToggleLineComment(uncomment: false);
    private void Uncomment_Click(object? sender, RoutedEventArgs e) => ToggleLineComment(uncomment: true);
    private async void FormatDocument_Click(object? sender, RoutedEventArgs e) => await FormatActiveDocumentAsync(selectionOnly: false);
    private async void FormatSelection_Click(object? sender, RoutedEventArgs e) => await FormatActiveDocumentAsync(selectionOnly: true);

    private async Task FormatActiveDocumentAsync(bool selectionOnly)
    {
        if (_activeDocument is null) return;
        var activeDocument = _activeDocument;
        var selectionStart = Editor.SelectionStart;
        var selectionLength = Editor.SelectionLength;
        if (selectionOnly && selectionLength == 0)
        {
            StatusText.Text = "Select code to format";
            return;
        }

        var source = Editor.Text ?? string.Empty;
        var caretOffset = Editor.CaretOffset;
        var projectPath = FindProjectForFile(activeDocument.FullPath);
        try
        {
            var formatted = await _completionService.FormatAsync(
                projectPath,
                activeDocument.FullPath,
                source,
                selectionOnly ? new Microsoft.CodeAnalysis.Text.TextSpan(selectionStart, selectionLength) : null,
                CancellationToken.None);
            if (!ReferenceEquals(_activeDocument, activeDocument) || !string.Equals(Editor.Text, source, StringComparison.Ordinal)) return;

            Editor.Document.Replace(0, Editor.Document.TextLength, formatted);
            Editor.CaretOffset = Math.Min(caretOffset, Editor.Document.TextLength);
            StatusText.Text = "Formatting complete";
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to format document: {ex.Message}");
            StatusText.Text = "Formatting failed";
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingDocument || _activeDocument is null) return;
        _activeDocument.Text = Editor.Text ?? string.Empty;
        _syntaxDiagnosticsTimer.Stop();
        _syntaxDiagnosticsTimer.Start();
    }

    private void Editor_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text.Length != 1) return;
        var character = e.Text[0];
        var source = Editor.Text ?? string.Empty;
        var afterAssignment = source[..Math.Clamp(Editor.CaretOffset, 0, source.Length)].TrimEnd().EndsWith('=');
        if (afterAssignment && (character == '=' || char.IsWhiteSpace(character)))
        {
            if (_completionWindow is not null)
            {
                _completionWindow.Hide();
                _completionWindow = null;
            }
            _ = ShowCompletionAsync(null, explicitInvocation: true);
            return;
        }

        if (_completionWindow is not null) return;
        if (character == '.' || char.IsLetterOrDigit(character) || character == '_')
            _ = ShowCompletionAsync(character, explicitInvocation: false);
    }

    private void Editor_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (_suppressNextCompletionNewline && e.Text is "\r" or "\n" or "\r\n")
        {
            _suppressNextCompletionNewline = false;
            e.Handled = true;
        }
        else if (_suppressNextCompletionPeriod && e.Text == ".")
        {
            _suppressNextCompletionPeriod = false;
            e.Handled = true;
        }
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) _suppressNextCompletionNewline = false;
        if (e.Key is not (Key.OemPeriod or Key.Decimal)) _suppressNextCompletionPeriod = false;

        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            e.Handled = true;
            _ = ShowAddUsingActionsAsync();
            return;
        }

        if (_completionWindow is not null && e.Key is Key.Enter or Key.Tab or Key.OemPeriod or Key.Decimal)
        {
            var list = _completionWindow.CompletionList;
            if (list.SelectedItem is null && list.CompletionData.Count > 0)
                list.SelectedItem = list.CompletionData[0];
            if (list.SelectedItem is not null)
            {
                PrepareCompletionCommit(e.Key);
                e.Handled = true;
                list.RequestInsertion(e);
                return;
            }
        }

        if (e.Key != Key.Space || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
        e.Handled = true;
        _ = ShowCompletionAsync(null, explicitInvocation: true);
    }

    private char? PrepareCompletionCommit(EventArgs request)
    {
        return request is KeyEventArgs keyEvent ? PrepareCompletionCommit(keyEvent.Key) : null;
    }

    private char? PrepareCompletionCommit(Key key)
    {
        if (key == Key.Enter)
            _suppressNextCompletionNewline = true;
        else if (key is Key.OemPeriod or Key.Decimal)
        {
            _suppressNextCompletionPeriod = true;
            return "."[0];
        }
        return null;
    }

    private void CompleteTextItem(
        AvaloniaEdit.Editing.TextArea textArea,
        AvaloniaEdit.Document.ISegment segment,
        string completionText,
        EventArgs insertionRequest)
    {
        var commitCharacter = PrepareCompletionCommit(insertionRequest);
        textArea.Document.Replace(segment.Offset, segment.Length, completionText);
        textArea.Caret.Offset = segment.Offset + completionText.Length;
        if (commitCharacter is char character)
        {
            textArea.Document.Insert(textArea.Caret.Offset, character.ToString());
            textArea.Caret.Offset++;
        }
    }

    private void EditorTextView_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not AvaloniaEdit.Rendering.TextView textView || Editor.Document is null) return;
        var position = textView.GetPosition(e.GetPosition(textView));
        if (position is null)
        {
            ClearQuickInfo();
            return;
        }

        var offset = Editor.Document.GetOffset(position.Value.Location);
        var source = Editor.Text ?? string.Empty;
        if (offset >= source.Length || !IsIdentifierPart(source[offset])) offset--;
        if (offset < 0 || !IsIdentifierPart(source[offset]))
        {
            ClearQuickInfo();
            return;
        }

        if (_hoverOffset == offset) return;
        _hoverOffset = offset;
        ToolTip.SetTip(Editor, null);
        _quickInfoTimer.Stop();
        _quickInfoTimer.Start();
    }

    private void EditorTextView_PointerExited(object? sender, PointerEventArgs e) => ClearQuickInfo();

    private void ClearQuickInfo()
    {
        _hoverOffset = -1;
        _quickInfoTimer.Stop();
        _quickInfoCts?.Cancel();
        ToolTip.SetTip(Editor, null);
    }

    private async Task ShowQuickInfoAsync()
    {
        _quickInfoTimer.Stop();
        var document = _activeDocument;
        var offset = _hoverOffset;
        var projectPath = document is null ? null : FindProjectForFile(document.FullPath);
        if (document is null || projectPath is null || offset < 0) return;

        _quickInfoCts?.Cancel();
        var request = new CancellationTokenSource();
        _quickInfoCts = request;
        var source = Editor.Text ?? string.Empty;
        try
        {
            var info = await _completionService.GetQuickInfoAsync(projectPath, document.FullPath, source, offset, request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_activeDocument, document) ||
                !string.Equals(Editor.Text, source, StringComparison.Ordinal) || _hoverOffset != offset)
                return;
            ToolTip.SetTip(Editor, info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"C# quick info unavailable: {ex.Message}"); }
        finally
        {
            if (ReferenceEquals(_quickInfoCts, request)) _quickInfoCts = null;
            request.Dispose();
        }
    }

    private async Task ShowAddUsingActionsAsync()
    {
        var document = _activeDocument;
        if (document is null) return;
        var projectPath = FindProjectForFile(document.FullPath);
        if (projectPath is null)
        {
            StatusText.Text = "Open this file in a C# project to find using suggestions";
            return;
        }

        var source = Editor.Text ?? string.Empty;
        var caretOffset = Editor.CaretOffset;
        try
        {
            var suggestions = await _completionService.GetMissingUsingSuggestionsAsync(
                projectPath, document.FullPath, source, caretOffset, CancellationToken.None);
            if (!ReferenceEquals(_activeDocument, document) || !string.Equals(Editor.Text, source, StringComparison.Ordinal)) return;
            if (suggestions.Count == 0)
            {
                StatusText.Text = "No matching using directive found";
                return;
            }
            if (suggestions.Count == 1)
            {
                await ApplyUsingAsync(document, source, suggestions[0], caretOffset);
                return;
            }

            var menu = new ContextMenu { Placement = PlacementMode.Pointer };
            foreach (var suggestion in suggestions)
            {
                var item = new MenuItem { Header = $"using {suggestion};" };
                item.Click += async (_, _) => await ApplyUsingAsync(document, source, suggestion, caretOffset);
                menu.Items.Add(item);
            }
            Editor.ContextMenu = menu;
            menu.Open(Editor);
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to find using suggestions: {ex.Message}");
            StatusText.Text = "Quick action failed";
        }
    }

    private async Task ApplyUsingAsync(EditorDocument document, string source, string namespaceName, int caretOffset)
    {
        var projectPath = FindProjectForFile(document.FullPath);
        if (projectPath is null) return;
        try
        {
            var updatedSource = await _completionService.AddUsingAsync(
                projectPath, document.FullPath, source, namespaceName, CancellationToken.None);
            if (updatedSource is null || !ReferenceEquals(_activeDocument, document) ||
                !string.Equals(Editor.Text, source, StringComparison.Ordinal)) return;

            var offsetAdjustment = updatedSource.Length - source.Length;
            Editor.Document.Replace(0, Editor.Document.TextLength, updatedSource);
            Editor.CaretOffset = Math.Clamp(caretOffset + offsetAdjustment, 0, Editor.Document.TextLength);
            Editor.Focus();
            StatusText.Text = $"Added using {namespaceName}";
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to add using {namespaceName}: {ex.Message}");
            StatusText.Text = "Quick action failed";
        }
    }

    private async Task ShowCompletionAsync(char? triggerCharacter, bool explicitInvocation)
    {
        if (_activeDocument is null || _completionWindow is not null ||
            !Path.GetExtension(_activeDocument.FullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return;

        _completionRequestCts?.Cancel();
        _completionRequestCts?.Dispose();
        var requestCts = new CancellationTokenSource();
        _completionRequestCts = requestCts;

        var activeDocument = _activeDocument;
        var source = Editor.Text ?? string.Empty;
        var caretOffset = Editor.CaretOffset;
        var startOffset = triggerCharacter == '.' ? caretOffset : GetIdentifierStart(source, caretOffset);
        var projectPath = FindProjectForFile(activeDocument.FullPath);
        var semanticItems = Array.Empty<CSharpCompletionCandidate>();
        string? expectedTypeName = null;

        try
        {
            if (!explicitInvocation && triggerCharacter != '.')
                await Task.Delay(90, requestCts.Token);

            if (projectPath is not null)
            {
                semanticItems = (await _completionService.GetSuggestionsAsync(
                    projectPath,
                    activeDocument.FullPath,
                    source,
                    caretOffset,
                    triggerCharacter,
                    requestCts.Token)).ToArray();
                var leftOfCaret = source[..Math.Clamp(caretOffset, 0, source.Length)].TrimEnd();
                if (leftOfCaret.EndsWith('='))
                    expectedTypeName = await _completionService.GetExpectedTypeNameAsync(
                        projectPath,
                        activeDocument.FullPath,
                        source,
                        caretOffset,
                        requestCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppendOutput($"C# completion unavailable: {ex.Message}");
        }

        if (requestCts.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
            if (!ReferenceEquals(_activeDocument, activeDocument) || Editor.Text != source ||
                requestCts.IsCancellationRequested || _completionWindow is not null)
                return;

            var prefix = source[startOffset..caretOffset];
            var matchingSemanticItems = semanticItems
                .Where(item => MatchesCompletionPrefix(item.FilterText, prefix))
                .ToArray();
            var useWordFallback = matchingSemanticItems.Length == 0 &&
                                  (semanticItems.Length == 0 || explicitInvocation) &&
                                  (explicitInvocation || (triggerCharacter != '.' && prefix.Length > 0));
            if (matchingSemanticItems.Length == 0 && !useWordFallback) return;

            var completionWindow = new CompletionWindow(Editor.TextArea)
            {
                StartOffset = startOffset,
                EndOffset = caretOffset,
                CloseAutomatically = true,
                CloseWhenCaretAtBeginning = triggerCharacter != '.'
            };
            completionWindow.CompletionList.CompletionAcceptKeys = [Key.Enter, Key.Tab, Key.OemPeriod, Key.Decimal];

            if (matchingSemanticItems.Length > 0)
            {
                var insertionRequested = false;
                ICompletionData? preferredItem = null;
                foreach (var semanticItem in matchingSemanticItems)
                {
                    var item = semanticItem;
                    var completionItem = new EditorCompletionItem(
                        item.DisplayText,
                        item.Description,
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            var commitCharacter = PrepareCompletionCommit(insertionEventArgs);
                            _ = ApplySemanticCompletionAsync(item, textArea, segment, commitCharacter);
                        });
                    completionWindow.CompletionList.CompletionData.Add(completionItem);
                    if (expectedTypeName is not null &&
                        (string.Equals(item.FilterText, expectedTypeName, StringComparison.Ordinal) ||
                         string.Equals(item.DisplayText, expectedTypeName, StringComparison.Ordinal)))
                        preferredItem = completionItem;
                }
                if (expectedTypeName is not null && preferredItem is null)
                {
                    preferredItem = new EditorCompletionItem(
                        expectedTypeName,
                        "Expected type for this expression",
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            CompleteTextItem(textArea, segment, expectedTypeName, insertionEventArgs);
                        });
                    completionWindow.CompletionList.CompletionData.Add(preferredItem);
                }
                completionWindow.CompletionList.SelectedItem = preferredItem ?? completionWindow.CompletionList.CompletionData.FirstOrDefault();
            }
            else
            {
                var insertionRequested = false;
                foreach (var word in GetWordSuggestions(source, startOffset, caretOffset))
                {
                    var completionText = word;
                    completionWindow.CompletionList.CompletionData.Add(new EditorCompletionItem(
                        completionText,
                        "Word from open documents",
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            CompleteTextItem(textArea, segment, completionText, insertionEventArgs);
                        }));
                }
                if (expectedTypeName is not null)
                {
                    var expectedTypeItem = new EditorCompletionItem(
                        expectedTypeName,
                        "Expected type for this expression",
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            CompleteTextItem(textArea, segment, expectedTypeName, insertionEventArgs);
                        });
                    completionWindow.CompletionList.CompletionData.Insert(0, expectedTypeItem);
                    completionWindow.CompletionList.SelectedItem = expectedTypeItem;
                }
                else if (completionWindow.CompletionList.CompletionData.Count > 0)
                    completionWindow.CompletionList.SelectedItem = completionWindow.CompletionList.CompletionData[0];
            }

            if (completionWindow.CompletionList.CompletionData.Count == 0) return;
            _completionWindow = completionWindow;
            completionWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_completionWindow, completionWindow)) _completionWindow = null;
            };
            completionWindow.Show();
            }
            catch (Exception ex)
            {
                _completionWindow?.Hide();
                _completionWindow = null;
                AppendOutput($"Unable to show completion: {ex.Message}");
            }
        });
    }

    private async Task ApplySemanticCompletionAsync(
        CSharpCompletionCandidate candidate,
        AvaloniaEdit.Editing.TextArea textArea,
        AvaloniaEdit.Document.ISegment segment,
        char? commitCharacter)
    {
        try
        {
            var change = await candidate.CompletionService.GetChangeAsync(
                candidate.Document,
                candidate.Item,
                cancellationToken: CancellationToken.None);
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeDocument?.FullPath != candidate.Document.FilePath ||
                    Editor.Text != candidate.SourceSnapshot)
                    return;

                try
                {
                    foreach (var textChange in change.TextChanges.OrderByDescending(item => item.Span.Start))
                        textArea.Document.Replace(textChange.Span.Start, textChange.Span.Length, textChange.NewText ?? string.Empty);

                    textArea.Caret.Offset = change.NewPosition ?? (segment.Offset + candidate.DisplayText.Length);
                    if (commitCharacter is char character)
                    {
                        textArea.Document.Insert(textArea.Caret.Offset, character.ToString());
                        textArea.Caret.Offset++;
                    }
                }
                catch (Exception ex)
                {
                    AppendOutput($"Unable to apply completion: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to apply completion: {ex.Message}");
        }
    }

    private IEnumerable<string> GetWordSuggestions(string source, int prefixStart, int caretOffset)
    {
        var prefix = source[prefixStart..Math.Clamp(caretOffset, prefixStart, source.Length)];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var documentText in _documents.Select(document => document.Text).Append(source))
        {
            var position = 0;
            while (position < documentText.Length)
            {
                if (!IsIdentifierStart(documentText[position]))
                {
                    position++;
                    continue;
                }

                var start = position++;
                while (position < documentText.Length && IsIdentifierPart(documentText[position])) position++;
                var word = documentText[start..position];
                if (word.Length > prefix.Length && word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && seen.Add(word))
                    yield return word;
            }
        }
    }

    private static bool MatchesCompletionPrefix(string candidate, string prefix)
    {
        if (prefix.Length == 0 || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        var initials = new string(candidate.Where(char.IsUpper).ToArray());
        return initials.Length > 0 && initials.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetIdentifierStart(string source, int position)
    {
        var start = Math.Clamp(position, 0, source.Length);
        while (start > 0 && IsIdentifierPart(source[start - 1])) start--;
        return start;
    }

    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);

    private void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        var query = SearchBox.Text.Trim();
        var match = Flatten(_roots).FirstOrDefault(node => !node.IsDirectory &&
            node.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            StatusText.Text = $"No file matching '{query}'";
            return;
        }

        _ = OpenDocumentAsync(match);
        e.Handled = true;
    }

    private void AppendOutput(string line) => Dispatcher.UIThread.Post(() =>
    {
        Output.Text = (Output.Text ?? string.Empty) + line + Environment.NewLine;
        Output.CaretIndex = Output.Text.Length;
    });

    private void UpdateDiagnostics()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        DiagnosticsText.Text = $"RAM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

    private void UpdateSyntaxDiagnostics()
    {
        var document = _activeDocument;
        if (document is null || !Path.GetExtension(document.FullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            _problems.Clear();
            _syntaxErrorRenderer.SetErrors([]);
            _semanticColorizer.SetHighlights([]);
            ProblemsTabButton.Content = "PROBLEMS";
            EditorStatusText.Text = "✓  Ready";
            return;
        }

        var source = Editor.Text ?? string.Empty;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            document.FullPath);
        var parseDiagnostics = tree.GetDiagnostics().ToArray();
        DisplayDiagnostics(parseDiagnostics);
        _ = UpdateSemanticDiagnosticsAsync(document, source, parseDiagnostics);
    }

    private void DisplayDiagnostics(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        var visibleDiagnostics = diagnostics
            .Where(item => item.Location.IsInSource &&
                           item.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .DistinctBy(item => (item.Id, item.Location.SourceSpan.Start, item.Location.SourceSpan.Length))
            .OrderBy(item => item.Location.SourceSpan.Start)
            .ToArray();
        _problems.Clear();
        foreach (var diagnostic in visibleDiagnostics)
        {
            var span = diagnostic.Location.GetLineSpan();
            var line = span.StartLinePosition.Line + 1;
            var column = span.StartLinePosition.Character + 1;
            _problems.Add(new EditorProblem(
                diagnostic.Severity,
                line,
                column,
                diagnostic.GetMessage()));
        }
        _syntaxErrorRenderer.SetErrors(visibleDiagnostics
            .Where(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && item.Location.IsInSource)
            .Select(item => (item.Location.SourceSpan.Start, item.Location.SourceSpan.Length)));

        var errors = _problems.Count(problem => problem.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var warnings = _problems.Count - errors;
        EditorStatusText.Text = errors > 0
            ? $"✖  {errors} error(s), {warnings} warning(s)"
            : warnings > 0 ? $"⚠  {warnings} warning(s)" : "✓  No syntax errors";
        ProblemsTabButton.Content = errors + warnings > 0 ? $"PROBLEMS ({_problems.Count})" : "PROBLEMS";
    }

    private async Task UpdateSemanticDiagnosticsAsync(
        EditorDocument document,
        string source,
        IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> parseDiagnostics)
    {
        _semanticDiagnosticsCts?.Cancel();
        _semanticDiagnosticsCts?.Dispose();
        var request = new CancellationTokenSource();
        _semanticDiagnosticsCts = request;

        var projectPath = FindProjectForFile(document.FullPath);
        if (projectPath is null)
        {
            _semanticColorizer.SetHighlights([]);
            return;
        }
        try
        {
            var diagnostics = await _completionService.GetDiagnosticsAsync(
                projectPath,
                document.FullPath,
                source,
                request.Token);
            var semanticHighlights = await _completionService.GetSemanticHighlightsAsync(
                projectPath,
                document.FullPath,
                source,
                request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_activeDocument, document) ||
                !string.Equals(Editor.Text, source, StringComparison.Ordinal)) return;

            _semanticColorizer.SetHighlights(semanticHighlights);
            DisplayDiagnostics(diagnostics.Count > 0 ? diagnostics : parseDiagnostics);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Live C# diagnostics unavailable: {ex.Message}");
        }
    }

    private string? FindProjectForFile(string filePath)
    {
        var candidates = _projects
            .Select(project => new { Project = project, Directory = Path.GetDirectoryName(project.FullPath) })
            .Where(item => item.Directory is not null && IsPathInside(item.Directory, filePath))
            .OrderByDescending(item => item.Directory!.Length);
        return candidates.FirstOrDefault()?.Project.FullPath;
    }

    private static bool IsPathInside(string directory, string filePath)
    {
        var relative = Path.GetRelativePath(directory, filePath);
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private void UpdateCursorStatus()
    {
        var caret = Editor.TextArea.Caret;
        CursorStatusText.Text = $"Ln {caret.Line}, Col {caret.Column}";
    }

    private void ActivateDocument(EditorDocument? document)
    {
        _completionRequestCts?.Cancel();
        ClearQuickInfo();
        _completionWindow?.Hide();
        _completionWindow = null;
        _semanticColorizer.SetHighlights([]);
        foreach (var item in _documents) item.IsActive = ReferenceEquals(item, document);
        _activeDocument = document;
        _loadingDocument = true;
        Editor.SyntaxHighlighting = EditorHighlightingService.ForFile(document?.FullPath);
        Editor.Text = document?.Text ?? string.Empty;
        _loadingDocument = false;
        SyncSolutionExplorerToActiveDocument();
        EmptyEditorText.IsVisible = document is null;
        _syntaxDiagnosticsTimer.Stop();
        UpdateSyntaxDiagnostics();
        UpdateCursorStatus();
        if (document is not null) Editor.Focus();
    }

    private void SyncSolutionExplorerToActiveDocument()
    {
        if (_activeDocument is null) return;
        var branch = new List<FileNode>();
        if (!TryFindNode(_roots, _activeDocument.FullPath, branch)) return;

        foreach (var ancestor in branch.Take(branch.Count - 1))
            ancestor.IsExpanded = true;

        var selectedNode = branch[^1];
        FileTree.SelectedItem = selectedNode;
        Dispatcher.UIThread.Post(() =>
        {
            FileTree.UpdateLayout();
            FindTreeItemContainer(FileTree, selectedNode)?.BringIntoView();
        });
    }

    private static bool TryFindNode(IEnumerable<FileNode> nodes, string fullPath, List<FileNode> branch)
    {
        foreach (var node in nodes)
        {
            branch.Add(node);
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase) ||
                TryFindNode(node.Children, fullPath, branch))
                return true;
            branch.RemoveAt(branch.Count - 1);
        }
        return false;
    }

    private static TreeViewItem? FindTreeItemContainer(ItemsControl owner, FileNode target)
    {
        foreach (var item in owner.Items.OfType<FileNode>())
        {
            if (owner.ContainerFromItem(item) is not TreeViewItem container)
                continue;
            if (ReferenceEquals(item, target)) return container;
            var nested = FindTreeItemContainer(container, target);
            if (nested is not null) return nested;
        }
        return null;
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

public sealed record EditorProblem(Microsoft.CodeAnalysis.DiagnosticSeverity Severity, int Line, int Column, string Message)
{
    public string DisplayText => $"{Severity}  {Message}   [{Line}, {Column}]";
}
