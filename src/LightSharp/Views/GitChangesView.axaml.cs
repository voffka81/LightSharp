using Avalonia.Controls;

namespace LightSharp.Views;

public partial class GitChangesView : UserControl
{
    public GitChangesView() => InitializeComponent();

    public TextBlock RepositoryText => RepoText;
    public Button RefreshAction => RefreshButton;
    public Button StageAllAction => StageAllButton;
    public Button FetchAction => FetchButton;
    public Button PullAction => PullButton;
    public Button PushAction => PushButton;
    public Button CommitAction => CommitButton;
    public ListBox Changes => ChangesList;
    public TextBlock EmptyStateText => EmptyText;
    public TextBox MessageInput => CommitMessageBox;
}
