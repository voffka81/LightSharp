using Avalonia.Input;
using Dock.Model.Avalonia.Controls;
using LightSharp.Models;
using LightSharp.Views;

namespace LightSharp.Services;

/// <summary>Handles commands initiated from the main menu, toolbar, and window shortcuts.</summary>
public sealed class MainWindowMenuController
{
    private readonly EditorWorkspaceController _editor;
    private readonly SearchReplaceController _search;
    private readonly GitChangesController _git;
    private readonly IMessageBus _messageBus;
    private readonly OutputPanelView _output;
    private readonly InputHandlerService _input;
    private readonly WorkspaceMenuController _workspace;
    private readonly DotNetCommandController _commands;
    private readonly Func<Task> _refreshGitStatus;
    private readonly ToolDock _solutionExplorerDock;
    private readonly Tool _gitChangesTool;

    public MainWindowMenuController(
        IMessageBus messageBus,
        EditorWorkspaceController editor,
        SearchReplaceController search,
        GitChangesController git,
        OutputPanelView output,
        InputHandlerService input,
        WorkspaceMenuController workspace,
        DotNetCommandController commands,
        Func<Task> refreshGitStatus,
        ToolDock solutionExplorerDock,
        Tool gitChangesTool)
    {
        _editor = editor;
        _search = search;
        _git = git;
        _messageBus = messageBus;
        _output = output;
        _input = input;
        _workspace = workspace;
        _commands = commands;
        _refreshGitStatus = refreshGitStatus;
        _solutionExplorerDock = solutionExplorerDock;
        _gitChangesTool = gitChangesTool;
    }

    public Task OpenFolderAsync() => _workspace.OpenFolderAsync();
    public Task OpenSolutionAsync() => _workspace.OpenSolutionAsync();
    public Task OpenFileAsync() => _workspace.OpenFileAsync();
    public Task NewFileAsync() => _workspace.NewFileAsync();
    public Task NewProjectAsync() => _workspace.NewProjectAsync();
    public Task SaveAsync() => _editor.SaveActiveDocumentAsync();
    public Task SaveAllAsync() => _editor.SaveAllAsync();
    public void Find() => _search.ShowSearchWindow(searchEntireSolution: false);
    public void Replace() => _search.ShowSearchWindow(searchEntireSolution: false, replaceMode: true);
    public void FindInFiles() => _search.ShowSearchWindow(searchEntireSolution: true);
    public Task OpenFileMatchingAsync(string? text) => _search.OpenFileMatchingAsync(text);
    public async Task HandleSearchKeyDownAsync(KeyEventArgs e, string? text)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(text)) return;
        await OpenFileMatchingAsync(text);
        e.Handled = true;
    }
    public void SelectAll() => _editor.SelectAllIfFocused();
    public void Comment() => _editor.ToggleLineComment(uncomment: false);
    public void Uncomment() => _editor.ToggleLineComment(uncomment: true);
    public Task FormatDocumentAsync() => _editor.FormatActiveDocumentAsync(selectionOnly: false);
    public Task FormatSelectionAsync() => _editor.FormatActiveDocumentAsync(selectionOnly: true);
    public Task BuildAsync() => _commands.BuildAsync();
    public Task TestAsync() => _commands.TestAsync();
    public Task StartAsync() => _commands.StartAsync();
    public void Stop() => _commands.Stop();
    public Task RefreshGitAsync() => _git.RefreshAsync();
    public Task StageAllGitAsync() => _git.StageAllAsync();
    public Task InitializeGitAsync() => InitializeGitCoreAsync();
    public void FetchGit() => _messageBus.Publish(new GitFetchRequested());
    public void PullGit() => _messageBus.Publish(new GitPullRequested());
    public void PushGit() => _messageBus.Publish(new GitPushRequested());

    public void FocusOutput()
    {
        _output.IsVisible = true;
        _output.ShowView("Output");
        _output.FocusOutput();
    }

    public void ShowProblems() => _output.ShowView("Problems");
    public void ShowOutput() => _output.ShowView("Output");
    public void ShowTests() => _output.ShowView("Tests");

    public void ShowGitChanges()
    {
        _solutionExplorerDock.ActiveDockable = _gitChangesTool;
        _ = _refreshGitStatus();
    }

    public async Task HandleWindowKeyDownAsync(KeyEventArgs e)
    {
        var result = _input.HandleWindowKeyDown(e.Key, e.KeyModifiers);
        if (!result.Handled) return;
        e.Handled = true;
        switch (result.Command)
        {
            case InputCommand.Find: Find(); break;
            case InputCommand.Replace: Replace(); break;
            case InputCommand.FindInFiles: FindInFiles(); break;
            case InputCommand.OpenSolution: await OpenSolutionAsync(); break;
            case InputCommand.OpenFile: await OpenFileAsync(); break;
            case InputCommand.NewProject: await NewProjectAsync(); break;
            case InputCommand.Save: await SaveAsync(); break;
            case InputCommand.SaveAll: await SaveAllAsync(); break;
            case InputCommand.Build: await BuildAsync(); break;
            case InputCommand.Start: await StartAsync(); break;
            case InputCommand.Stop: Stop(); break;
            case InputCommand.CloseDocument:
                if (_editor.ActiveDocument is { } activeDocument)
                    await _editor.CloseDocumentAsync(activeDocument);
                break;
            case InputCommand.SelectAll: SelectAll(); break;
            case InputCommand.NextDocument: _editor.ActivateAdjacentDocument(1); break;
            case InputCommand.PreviousDocument: _editor.ActivateAdjacentDocument(-1); break;
            case InputCommand.Test: await TestAsync(); break;
            case InputCommand.ToggleLineComment: Comment(); break;
            case InputCommand.UncommentLine: Uncomment(); break;
            case InputCommand.FormatDocument: await FormatDocumentAsync(); break;
            case InputCommand.FormatSelection: await FormatSelectionAsync(); break;
        }
    }

    private async Task InitializeGitCoreAsync()
    {
        await _git.InitializeAsync();
        ShowGitChanges();
    }

}
