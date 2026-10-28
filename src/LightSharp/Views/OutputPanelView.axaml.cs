using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LightSharp.Views;

public partial class OutputPanelView : UserControl
{
    public OutputPanelView() => InitializeComponent();

    public TextBox Output => OutputTextBox;
    public ListBox ProblemsList => Problems;
    public TextBlock StatusText => Status;
    public TextBlock TestsSummaryText => TestsSummary;
    public Button ProblemsTabButton => ProblemsTab;
    public Button OutputTabButton => OutputTab;
    public Button TestsTabButton => TestsTab;
    public Control TestsContent => TestsView;

    public event EventHandler<TappedEventArgs>? ProblemDoubleTapped;
    public event EventHandler<RoutedEventArgs>? RunTestsRequested;

    public void ShowView(string view)
    {
        ProblemsTab.Classes.Set("selected", view == "Problems");
        OutputTab.Classes.Set("selected", view == "Output");
        TestsTab.Classes.Set("selected", view == "Tests");
        Problems.IsVisible = view == "Problems";
        OutputTextBox.IsVisible = view == "Output";
        TestsView.IsVisible = view == "Tests";
    }

    private void ShowProblems_Click(object? sender, RoutedEventArgs e) => ShowView("Problems");
    private void ShowOutput_Click(object? sender, RoutedEventArgs e) => ShowView("Output");
    private void ShowTests_Click(object? sender, RoutedEventArgs e) => ShowView("Tests");
    private void Problems_DoubleTapped(object? sender, TappedEventArgs e) => ProblemDoubleTapped?.Invoke(sender, e);
    private void RunTests_Click(object? sender, RoutedEventArgs e) => RunTestsRequested?.Invoke(sender, e);
}
