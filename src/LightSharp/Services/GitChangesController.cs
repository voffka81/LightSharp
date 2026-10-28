using LightSharp.Views;

namespace LightSharp.Services;

public sealed class GitChangesController : IDisposable
{
    private readonly GitChangesView _view;
    private readonly GitService _git;
    private readonly IMessageBus _messageBus;
    private string? _workspacePath;
    private string? _repositoryPath;
    private bool _operationRunning;
    private readonly List<IDisposable> _subscriptions = [];

    public GitChangesController(GitChangesView view, GitService git, IMessageBus messageBus)
    {
        _view = view;
        _git = git;
        _messageBus = messageBus;
        _subscriptions.Add(messageBus.Observe<GitRefreshRequested>().Subscribe(message => { _ = RefreshAsync(); }));
        _subscriptions.Add(messageBus.Observe<GitStageAllRequested>().Subscribe(message => { _ = StageAllAsync(); }));
        _subscriptions.Add(messageBus.Observe<GitFetchRequested>().Subscribe(message => { _ = FetchAsync(); }));
        _subscriptions.Add(messageBus.Observe<GitPullRequested>().Subscribe(message => { _ = PullAsync(); }));
        _subscriptions.Add(messageBus.Observe<GitPushRequested>().Subscribe(message => { _ = PushAsync(); }));
        _subscriptions.Add(messageBus.Observe<GitCommitRequested>().Subscribe(message => CommitAsync(message.Message)));
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
    }

    public async Task SetWorkspaceAsync(string? workspacePath)
    {
        _workspacePath = workspacePath;
        _repositoryPath = workspacePath is null ? null : _git.FindRepositoryRoot(workspacePath);
        var hasRepository = _repositoryPath is not null;
        _messageBus.Publish(new GitRepositoryAvailabilityChanged(workspacePath is not null, hasRepository));
        _messageBus.Publish(new GitBranchChanged(hasRepository ? string.Empty : "⎇  No repository"));
        _view.SetChanges([]);
        _view.SetRepository(hasRepository ? _repositoryPath! : "Not a Git repository");
        if (!hasRepository)
        {
            _view.SetEmptyMessage(workspacePath is null
                ? "Open a workspace to connect Git."
                : "Use Git > Initialize Repository to create a repository.");
            _view.SetOperationsEnabled(false);
            return;
        }

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        _view.SetChanges([]);
        if (_repositoryPath is null)
        {
            await SetWorkspaceAsync(_workspacePath);
            return;
        }

        _view.SetOperationsEnabled(!_operationRunning);
        try
        {
            var status = await _git.GetStatusAsync(_repositoryPath);
            _messageBus.Publish(new GitBranchChanged($"⎇  {status.Branch}"));
            _view.SetRepository($"{_repositoryPath} · {status.Branch}");
            _view.SetChanges(status.Changes);
            if (status.Changes.Count == 0) _view.SetEmptyMessage("No changes detected.");
        }
        catch (Exception ex)
        {
            _view.SetRepository(_repositoryPath);
            _view.SetEmptyMessage(ex.Message);
            _messageBus.Publish(new OutputLineRequested($"Git status unavailable: {ex.Message}"));
        }
    }

    public async Task InitializeAsync()
    {
        if (_workspacePath is null) return;
        try
        {
            await _git.InitializeAsync(_workspacePath);
            await SetWorkspaceAsync(_workspacePath);
        }
        catch (Exception ex)
        {
            _messageBus.Publish(new OutputLineRequested($"Unable to initialize Git repository: {ex.Message}"));
            _messageBus.Publish(new StatusMessageChanged("Git initialization failed"));
        }
    }

    public async Task StageAllAsync()
    {
        if (_repositoryPath is null) return;
        try
        {
            await _git.StageAllAsync(_repositoryPath);
            await RefreshAsync();
            _messageBus.Publish(new StatusMessageChanged("All changes staged"));
        }
        catch (Exception ex)
        {
            _messageBus.Publish(new OutputLineRequested($"Unable to stage Git changes: {ex.Message}"));
            _messageBus.Publish(new StatusMessageChanged("Git staging failed"));
        }
    }

    private async void CommitAsync(string message)
    {
        if (_repositoryPath is null) return;
        if (string.IsNullOrWhiteSpace(message))
        {
            _messageBus.Publish(new StatusMessageChanged("Enter a commit message"));
            _view.FocusCommitMessage();
            return;
        }

        try
        {
            var result = await _git.CommitAllAsync(_repositoryPath, message);
            _messageBus.Publish(new OutputLineRequested(result));
            _view.ClearCommitMessage();
            await RefreshAsync();
            _messageBus.Publish(new StatusMessageChanged("Commit created"));
        }
        catch (Exception ex)
        {
            _messageBus.Publish(new OutputLineRequested($"Git commit failed: {ex.Message}"));
            _messageBus.Publish(new StatusMessageChanged("Git commit failed"));
        }
    }

    private Task FetchAsync() => RunRemoteOperationAsync("fetch", _git.FetchAsync);
    private Task PullAsync() => RunRemoteOperationAsync("pull", _git.PullAsync);
    private Task PushAsync() => RunRemoteOperationAsync("push", _git.PushAsync);

    private async Task RunRemoteOperationAsync(
        string operation,
        Func<string, CancellationToken, Task<string>> run)
    {
        if (_repositoryPath is null || _operationRunning) return;

        var repositoryPath = _repositoryPath;
        _operationRunning = true;
        _view.SetOperationsEnabled(false);
        _messageBus.Publish(new StatusMessageChanged($"Git {operation} in progress..."));
        try
        {
            var output = await run(repositoryPath, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(output)) _messageBus.Publish(new OutputLineRequested(output));
            await RefreshAsync();
            _messageBus.Publish(new StatusMessageChanged($"Git {operation} completed"));
        }
        catch (Exception ex)
        {
            _messageBus.Publish(new OutputLineRequested($"Git {operation} failed: {ex.Message}"));
            _messageBus.Publish(new StatusMessageChanged($"Git {operation} failed"));
        }
        finally
        {
            _operationRunning = false;
            _view.SetOperationsEnabled(_repositoryPath is not null);
        }
    }
}
