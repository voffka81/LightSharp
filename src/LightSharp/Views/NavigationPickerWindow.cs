using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace LightSharp.Views;

/// <summary>Keyboard-friendly navigation picker for files, symbols and references.</summary>
public sealed class NavigationPickerWindow<T> : Window
{
    private readonly TextBox _filter = new() { Watermark = "Filter results..." };
    private readonly ListBox _results = new();
    private readonly IReadOnlyList<T> _items;
    private readonly Func<T, string> _label;
    private readonly Func<T, string, bool> _matches;
    private readonly Func<T, Task> _navigate;

    public NavigationPickerWindow(string title, IReadOnlyList<T> items, Func<T, string> label,
        Func<T, string, bool> matches, Func<T, Task> navigate)
    {
        Title = title;
        Width = 650;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _items = items;
        _label = label;
        _matches = matches;
        _navigate = navigate;
        _filter.TextChanged += (_, _) => Refresh();
        _filter.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Down) { _results.Focus(); _results.SelectedIndex = 0; e.Handled = true; }
            else if (e.Key == Key.Enter) { await OpenSelectedAsync(); e.Handled = true; }
            else if (e.Key == Key.Escape) Close();
        };
        _results.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        _results.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { await OpenSelectedAsync(); e.Handled = true; }
            else if (e.Key == Key.Escape) Close();
        };
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 8,
            Children = { _filter, _results }
        };
        Refresh();
        Opened += (_, _) => _filter.Focus();
    }

    private void Refresh()
    {
        var term = _filter.Text?.Trim() ?? "";
        _results.ItemsSource = _items.Where(item => _matches(item, term))
            .Take(250).Select(item => new NavigationEntry(item, _label(item))).ToArray();
        _results.SelectedIndex = _results.ItemCount > 0 ? 0 : -1;
    }

    private async Task OpenSelectedAsync()
    {
        if (_results.SelectedItem is not NavigationEntry entry) return;
        await _navigate((T)entry.Item);
        Close();
    }

    private sealed record NavigationEntry(object Item, string Label)
    {
        public override string ToString() => Label;
    }
}
