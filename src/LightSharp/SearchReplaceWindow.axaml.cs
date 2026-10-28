using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LightSharp.Models;
using LightSharp.Services;

namespace LightSharp;

public sealed partial class SearchReplaceWindow : Window
{
    private readonly Func<SearchRequest, bool, Task<IReadOnlyList<SearchHit>>> _search;
    private readonly Func<SearchRequest, Task<SearchHit?>> _findNext;
    private readonly Func<IReadOnlyList<SearchHit>, Task<int>> _apply;
    private readonly Func<Task<int>> _undo;
    private readonly Func<SearchHit, Task> _open;

    public ObservableCollection<SearchResultItem> Results { get; } = [];

    public SearchReplaceWindow()
        : this(string.Empty, false,
            (_, _) => Task.FromResult<IReadOnlyList<SearchHit>>([]),
            _ => Task.FromResult<SearchHit?>(null),
            _ => Task.FromResult(0),
            () => Task.FromResult(0),
            _ => Task.CompletedTask)
    {
    }

    public SearchReplaceWindow(
        string initialQuery,
        bool searchEntireSolution,
        Func<SearchRequest, bool, Task<IReadOnlyList<SearchHit>>> search,
        Func<SearchRequest, Task<SearchHit?>> findNext,
        Func<IReadOnlyList<SearchHit>, Task<int>> apply,
        Func<Task<int>> undo,
        Func<SearchHit, Task> open,
        bool replaceMode = false,
        bool hasUndo = false)
    {
        InitializeComponent();
        DataContext = this;
        QueryBox.Text = initialQuery;
        ScopeBox.SelectedIndex = searchEntireSolution ? 1 : 0;
        FindButton.Content = searchEntireSolution ? "Find First" : "Find Next";
        UndoButton.IsEnabled = hasUndo;
        Title = searchEntireSolution ? "Find in Files" : replaceMode ? "Find and Replace" : "Find";
        _search = search;
        _findNext = findNext;
        _apply = apply;
        _undo = undo;
        _open = open;
        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        };
        ScopeBox.SelectionChanged += (_, _) =>
            FindButton.Content = ScopeBox.SelectedIndex == 0 ? "Find Next" : "Find First";
    }

    private async void Find_Click(object? sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = "Enter text to find.";
            return;
        }

        try
        {
            Results.Clear();
            ApplyButton.IsEnabled = false;
            CountText.Text = string.Empty;
            var request = new SearchRequest(query, ReplacementBox.Text ?? string.Empty,
                MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true, RegexBox.IsChecked == true);
            if (ScopeBox.SelectedIndex == 0)
            {
                var hit = await _findNext(request);
                CountText.Text = string.Empty;
                StatusText.Text = hit is null ? "No matches found." : $"Found in {hit.FilePath} ({hit.Line}, {hit.Column}).";
                if (hit is not null) await _open(hit);
            }
            else
            {
                var hits = await LoadResultsAsync(request);
                if (hits.Count > 0) await _open(hits[0]);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            CountText.Text = string.Empty;
        }
    }

    private async void FindAll_Click(object? sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = "Enter text to find.";
            return;
        }

        try
        {
            var request = new SearchRequest(query, ReplacementBox.Text ?? string.Empty,
                MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true, RegexBox.IsChecked == true);
            await LoadResultsAsync(request);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            CountText.Text = string.Empty;
        }
    }

    private async Task<IReadOnlyList<SearchHit>> LoadResultsAsync(SearchRequest request)
    {
        StatusText.Text = "Searching...";
        ApplyButton.IsEnabled = false;
        var hits = await _search(request, ScopeBox.SelectedIndex == 1);
        Results.Clear();
        foreach (var hit in hits) Results.Add(new SearchResultItem(hit));
        CountText.Text = $"{hits.Count} result{(hits.Count == 1 ? string.Empty : "s")}";
        StatusText.Text = hits.Count == 0 ? "No matches found." : "Review the before and after preview, then apply selected replacements.";
        ApplyButton.IsEnabled = hits.Count > 0;
        return hits;
    }

    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        var selected = Results.Where(result => result.IsSelected).Select(result => result.Hit).ToArray();
        if (selected.Length == 0) return;
        try
        {
            var count = await _apply(selected);
            StatusText.Text = $"Applied {count} replacement{(count == 1 ? string.Empty : "s")}.";
            ApplyButton.IsEnabled = false;
            UndoButton.IsEnabled = count > 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not apply replacements: {ex.Message}";
        }
    }

    private async void Undo_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var count = await _undo();
            StatusText.Text = $"Restored {count} file{(count == 1 ? string.Empty : "s")}.";
            UndoButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not undo replacements: {ex.Message}";
        }
    }

    private async void Results_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultItem result)
            await _open(result.Hit);
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var result in Results) result.IsSelected = true;
    }

    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Find_Click(sender, new RoutedEventArgs());
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
