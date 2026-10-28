using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LightSharp.Models;
using LightSharp.Views;

namespace LightSharp.Services;

public sealed class WorkspaceMenuController
{
    private readonly Window _owner;
    private readonly DotNetCommandController _commands;
    private readonly WorkspaceController _workspace;
    private readonly EditorWorkspaceController _editor;
    private readonly OutputPanelView _output;
    private readonly Func<FileNode?> _startupProject;
    private readonly Action<FileNode?> _setStartupProject;

    public WorkspaceMenuController(
        Window owner,
        DotNetCommandController commands,
        WorkspaceController workspace,
        EditorWorkspaceController editor,
        OutputPanelView output,
        Func<FileNode?> startupProject,
        Action<FileNode?> setStartupProject)
    {
        _owner = owner;
        _commands = commands;
        _workspace = workspace;
        _editor = editor;
        _output = output;
        _startupProject = startupProject;
        _setStartupProject = setStartupProject;
    }

    public async Task OpenFolderAsync()
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open .NET workspace", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await _workspace.LoadAsync(path);
    }

    public static void OpenContainingFolder(FileNode node)
    {
        var path = node.IsDirectory && Directory.Exists(node.FullPath)
            ? node.FullPath
            : Path.GetDirectoryName(node.FullPath);
        if (path is null || !Directory.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    public async Task OpenSolutionAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Visual Studio solution",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Visual Studio solution (*.sln, *.slnx)") { Patterns = ["*.sln", "*.slnx"] }]
        });
        var solutionPath = files.FirstOrDefault()?.TryGetLocalPath();
        var directory = solutionPath is null ? null : Path.GetDirectoryName(solutionPath);
        if (solutionPath is not null && directory is not null)
            await _workspace.LoadAsync(directory, solutionPath);
    }

    public async Task OpenFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = "Open file", AllowMultiple = false });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            await _editor.OpenDocumentAsync(new FileNode { Name = Path.GetFileName(path), FullPath = path });
    }

    public async Task AddExistingProjectAsync()
    {
        var solutionPath = _workspace.SolutionPath;
        if (solutionPath is null) return;
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add existing C# project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("C# project (*.csproj)") { Patterns = ["*.csproj"] }]
        });
        var projectPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (projectPath is null) return;
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? _workspace.WorkspacePath!;
        if (await _commands.RunAsync(solutionDirectory, "sln", solutionPath, "add", projectPath) == 0)
            await _workspace.LoadAsync(solutionDirectory, solutionPath);
    }

    public async Task RefreshWorkspaceAsync()
    {
        if (_workspace.WorkspacePath is { } path)
            await _workspace.LoadAsync(path, _workspace.SolutionPath);
    }

    public async Task NewFileAsync()
    {
        var directory = GetDefaultNewFileDirectory();
        if (directory is not null) await CreateNewFileAsync(directory);
        else _output.StatusMessage = "Open a workspace or project first";
    }

    public Task CreateFileInNodeAsync(FileNode node)
    {
        var directory = GetNewFileDirectory(node);
        return directory is null ? Task.CompletedTask : CreateNewFileAsync(directory);
    }

    public async Task NewProjectAsync()
    {
        var solutionPath = _workspace.SolutionPath;
        var workspacePath = _workspace.WorkspacePath;
        var defaultLocation = solutionPath is not null
            ? Path.GetDirectoryName(solutionPath)
            : workspacePath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(defaultLocation))
            defaultLocation = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new NewProjectDialog(defaultLocation, solutionPath is not null);
        if (await dialog.ShowDialog<bool>(_owner) is false) return;

        var projectDirectory = Path.Combine(dialog.Location, dialog.ProjectName);
        var created = await _commands.RunAsync(dialog.Location,
            "new", dialog.TemplateShortName, "--name", dialog.ProjectName, "--output", projectDirectory);
        if (created != 0) return;

        var projectFile = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (projectFile is null)
        {
            _output.AppendOutput("The selected template did not create a C# project file.");
            _output.StatusMessage = "Project creation failed";
            return;
        }

        if (solutionPath is null || !dialog.AddToSolution)
        {
            var createSolution = await _commands.RunAsync(dialog.Location,
                "new", "sln", "--name", dialog.ProjectName, "--output", dialog.Location);
            if (createSolution != 0) return;
            solutionPath = Directory.EnumerateFiles(dialog.Location, $"{dialog.ProjectName}.sln*")
                .FirstOrDefault(file => Path.GetExtension(file) is ".sln" or ".slnx");
        }

        if (solutionPath is null)
        {
            _output.AppendOutput("The solution file could not be found after creation.");
            _output.StatusMessage = "Project created, solution setup failed";
            return;
        }

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? dialog.Location;
        if (await _commands.RunAsync(solutionDirectory, "sln", solutionPath, "add", projectFile) != 0) return;

        await _workspace.LoadAsync(solutionDirectory, solutionPath);
        _setStartupProject(_workspace.Projects.FirstOrDefault(project =>
            string.Equals(project.FullPath, projectFile, StringComparison.OrdinalIgnoreCase)));
        _output.StatusMessage = $"Created {dialog.ProjectName}";
    }

    private string? GetDefaultNewFileDirectory()
    {
        if (_editor.ActiveDocument is { } activeDocument)
            return Path.GetDirectoryName(activeDocument.FullPath);
        if (_startupProject() is FileNode startupProject)
            return Path.GetDirectoryName(startupProject.FullPath);
        if (_workspace.Projects.FirstOrDefault() is FileNode project)
            return Path.GetDirectoryName(project.FullPath);
        return _workspace.WorkspacePath;
    }

    private string? GetNewFileDirectory(FileNode node)
    {
        var solutionPath = _workspace.SolutionPath;
        if (string.Equals(node.FullPath, solutionPath, StringComparison.OrdinalIgnoreCase))
        {
            var project = _startupProject() ?? _workspace.Projects.FirstOrDefault();
            return project is not null ? Path.GetDirectoryName(project.FullPath) : Path.GetDirectoryName(node.FullPath);
        }
        if (Path.GetExtension(node.FullPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(node.FullPath);
        return node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
    }

    private async Task CreateNewFileAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            _output.StatusMessage = "The selected folder no longer exists";
            return;
        }

        var dialog = new NewFileDialog();
        if (await dialog.ShowDialog<bool>(_owner) is false) return;
        var filePath = Path.GetFullPath(Path.Combine(directory, dialog.FileName));
        if (File.Exists(filePath) || Directory.Exists(filePath))
        {
            _output.StatusMessage = $"A file named {dialog.FileName} already exists";
            return;
        }

        try
        {
            await File.WriteAllTextAsync(filePath, string.Empty);
            if (_workspace.WorkspacePath is { } workspacePath)
                await _workspace.LoadAsync(workspacePath, _workspace.SolutionPath);
            var node = Flatten(_workspace.Roots).FirstOrDefault(item =>
                string.Equals(item.FullPath, filePath, StringComparison.OrdinalIgnoreCase))
                ?? new FileNode { Name = Path.GetFileName(filePath), FullPath = filePath };
            await _editor.OpenDocumentAsync(node);
            _output.StatusMessage = $"Created {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            _output.AppendOutput($"Unable to create file: {ex.Message}");
            _output.StatusMessage = "File creation failed";
        }
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
