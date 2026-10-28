using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LightSharp.Models;
using LightSharp.Services;

namespace LightSharp.Views;

public partial class OutputPanelView : UserControl
{
    private readonly System.Collections.ObjectModel.ObservableCollection<EditorProblem> _problems = [];
    private IMessageBus? _messageBus;

    public OutputPanelView()
    {
        InitializeComponent();
        Problems.ItemsSource = _problems;
    }

    public void AttachMessageBus(IMessageBus messageBus) => _messageBus = messageBus;

    public string StatusMessage
    {
        get => Status.Text ?? string.Empty;
        set => Status.Text = value;
    }

    public string TestSummary
    {
        get => TestsSummary.Text ?? string.Empty;
        set => TestsSummary.Text = value;
    }

    public void SetProblems(IEnumerable<EditorProblem> problems)
    {
        _problems.Clear();
        foreach (var problem in problems) _problems.Add(problem);
        ProblemsTab.Content = _problems.Count > 0 ? $"PROBLEMS ({_problems.Count})" : "PROBLEMS";
    }

    public void AppendOutput(string line)
    {
        OutputTextBox.Text = (OutputTextBox.Text ?? string.Empty) + line + Environment.NewLine;
        OutputTextBox.CaretIndex = OutputTextBox.Text.Length;
    }

    public void ClearOutput() => OutputTextBox.Clear();

    public void FocusOutput()
    {
        ShowView("Output");
        OutputTextBox.Focus();
    }

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
    private void Problems_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Problems.SelectedItem is EditorProblem problem) _messageBus?.Publish(new ProblemActivated(problem));
    }

    private void RunTests_Click(object? sender, RoutedEventArgs e) => _messageBus?.Publish(new RunTestsRequested());
}
