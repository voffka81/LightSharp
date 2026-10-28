using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LightSharp.Models;
using LightSharp.Services;
using System.Collections.ObjectModel;

namespace LightSharp.Views;

public partial class SolutionExplorerView : UserControl
{
    private IMessageBus? _messageBus;
    public SolutionExplorerView() => InitializeComponent();

    public void AttachMessageBus(IMessageBus messageBus) => _messageBus = messageBus;

    private string? _solutionPath;
    private bool _hasWorkspace;

    public void SetRoots(IEnumerable<FileNode> roots) => Tree.ItemsSource = roots;

    public void SetWorkspaceContext(string? solutionPath, bool hasWorkspace)
    {
        _solutionPath = solutionPath;
        _hasWorkspace = hasWorkspace;
    }

    public void SelectAndReveal(string filePath)
    {
        var branch = new List<FileNode>();
        if (!TryFindNode(Tree.Items.OfType<FileNode>(), filePath, branch)) return;

        foreach (var ancestor in branch.Take(branch.Count - 1)) ancestor.IsExpanded = true;
        Tree.SelectedItem = branch[^1];
        Dispatcher.UIThread.Post(() =>
        {
            Tree.UpdateLayout();
            FindTreeItemContainer(Tree, branch[^1])?.BringIntoView();
        });
    }

    private void Tree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Tree.SelectedItem is FileNode { IsDirectory: false } node)
            _messageBus?.Publish(new SolutionExplorerFileOpenRequested(node));
    }

    private void Item_RightTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: FileNode node } control) return;
        Tree.SelectedItem = node;
        var menu = new ContextMenu();
        void Add(string header, SolutionExplorerAction action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => _messageBus?.Publish(new SolutionExplorerCommandRequested(new(node, action)));
            menu.Items.Add(item);
        }
        void Separator() => menu.Items.Add(new Separator());

        var isSolution = string.Equals(node.FullPath, _solutionPath, StringComparison.OrdinalIgnoreCase);
        var isProject = Path.GetExtension(node.Name).Equals(".csproj", StringComparison.OrdinalIgnoreCase);
        if (!node.IsDirectory) Add("Open", SolutionExplorerAction.OpenFile);
        Add("Add > New File...", SolutionExplorerAction.AddNewFile);

        if (isSolution)
        {
            if (menu.Items.Count > 0) Separator();
            Add("Add > New Project...", SolutionExplorerAction.AddNewProject);
            Add("Add > Existing Project...", SolutionExplorerAction.AddExistingProject);
            Separator();
            Add("Build Solution", SolutionExplorerAction.BuildSolution);
            Add("Run Tests", SolutionExplorerAction.TestSolution);
        }
        else if (isProject)
        {
            Separator();
            Add("Set as Startup Project", SolutionExplorerAction.SetStartupProject);
            Add("Build", SolutionExplorerAction.BuildProject);
            Add("Run Tests", SolutionExplorerAction.TestProject);
        }

        Separator();
        Add("Open Containing Folder", SolutionExplorerAction.OpenContainingFolder);
        if (_hasWorkspace)
        {
            Separator();
            Add("Refresh", SolutionExplorerAction.RefreshWorkspace);
        }

        menu.Placement = PlacementMode.Pointer;
        control.ContextMenu = menu;
        menu.Open(control);
    }

    private static bool TryFindNode(IEnumerable<FileNode> nodes, string fullPath, List<FileNode> branch)
    {
        foreach (var node in nodes)
        {
            branch.Add(node);
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase) ||
                TryFindNode(node.Children, fullPath, branch)) return true;
            branch.RemoveAt(branch.Count - 1);
        }
        return false;
    }

    private static TreeViewItem? FindTreeItemContainer(ItemsControl owner, FileNode target)
    {
        foreach (var item in owner.Items.OfType<FileNode>())
        {
            if (owner.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(item, target)) return container;
            var nested = FindTreeItemContainer(container, target);
            if (nested is not null) return nested;
        }
        return null;
    }
}
