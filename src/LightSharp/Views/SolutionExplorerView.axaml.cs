using Avalonia.Controls;
using Avalonia.Input;

namespace LightSharp.Views;

public partial class SolutionExplorerView : UserControl
{
    public SolutionExplorerView() => InitializeComponent();

    public TreeView FileTree => Tree;

    public event EventHandler<TappedEventArgs>? FileDoubleTapped;
    public event EventHandler<TappedEventArgs>? FileItemRightTapped;

    private void Tree_DoubleTapped(object? sender, TappedEventArgs e) => FileDoubleTapped?.Invoke(sender, e);

    private void Item_RightTapped(object? sender, TappedEventArgs e) => FileItemRightTapped?.Invoke(sender, e);
}
