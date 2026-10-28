using Avalonia.Controls;
using Avalonia.Interactivity;
using LightSharp.Services;
using System.Collections.ObjectModel;

namespace LightSharp.Views;

public partial class GitChangesView : UserControl
{
    private readonly ObservableCollection<GitChange> _changes = [];
    private IMessageBus? _messageBus;

    public GitChangesView()
    {
        InitializeComponent();
        ChangesList.ItemsSource = _changes;
        RefreshButton.Click += (_, _) => _messageBus?.Publish(new GitRefreshRequested());
        StageAllButton.Click += (_, _) => _messageBus?.Publish(new GitStageAllRequested());
        FetchButton.Click += (_, _) => _messageBus?.Publish(new GitFetchRequested());
        PullButton.Click += (_, _) => _messageBus?.Publish(new GitPullRequested());
        PushButton.Click += (_, _) => _messageBus?.Publish(new GitPushRequested());
        CommitButton.Click += (_, _) => _messageBus?.Publish(new GitCommitRequested(CommitMessage));
        SetOperationsEnabled(false);
    }

    public void AttachMessageBus(IMessageBus messageBus) => _messageBus = messageBus;

    public string CommitMessage => CommitMessageBox.Text?.Trim() ?? string.Empty;

    public void SetRepository(string summary) => RepoText.Text = summary;

    public void SetEmptyMessage(string message)
    {
        EmptyText.Text = message;
        EmptyText.IsVisible = _changes.Count == 0;
    }

    public void SetChanges(IEnumerable<GitChange> changes)
    {
        _changes.Clear();
        foreach (var change in changes) _changes.Add(change);
        EmptyText.IsVisible = _changes.Count == 0;
        if (_changes.Count > 0) EmptyText.Text = string.Empty;
    }

    public void SetOperationsEnabled(bool enabled)
    {
        RefreshButton.IsEnabled = enabled;
        StageAllButton.IsEnabled = enabled;
        FetchButton.IsEnabled = enabled;
        PullButton.IsEnabled = enabled;
        PushButton.IsEnabled = enabled;
    }

    public void ClearCommitMessage() => CommitMessageBox.Clear();

    public void FocusCommitMessage() => CommitMessageBox.Focus();
}
