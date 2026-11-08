using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using LightSharp.Services;

namespace LightSharp.Views;

/// <summary>Interactive shortcut editor; captures key presses instead of requiring shortcut syntax.</summary>
public sealed class KeybindingsWindow : Window
{
    private readonly KeyBindingService _bindings;
    private readonly ListBox _commands = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Search commands..." };
    private readonly TextBlock _current = new();
    private readonly TextBlock _message = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _capture = new() { Content = "Record shortcut" };
    private string? _firstStroke;
    private bool _recording;
    private bool _appendShortcut;

    public KeybindingsWindow(KeyBindingService bindings)
    {
        _bindings = bindings;
        Title = "Keyboard Shortcuts";
        Width = 610;
        Height = 550;
        MinWidth = 450;
        MinHeight = 400;
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 10 };
        root.Children.Add(_filter);
        _commands.SelectionChanged += (_, _) => UpdateSelection();
        Grid.SetRow(_commands, 1);
        root.Children.Add(_commands);
        Grid.SetRow(_current, 2);
        root.Children.Add(_current);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _capture.Click += (_, _) => BeginRecording(false);
        actions.Children.Add(_capture);
        var add = new Button { Content = "Add alternative" };
        add.Click += (_, _) => BeginRecording(true);
        actions.Children.Add(add);
        var remove = new Button { Content = "Unassign" };
        remove.Click += (_, _) => Apply([]);
        actions.Children.Add(remove);
        var reset = new Button { Content = "Reset selected" };
        reset.Click += (_, _) =>
        {
            if (_commands.SelectedItem is not InputCommand command) return;
            var defaults = new KeyBindingService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            if (defaults.GetBindings().TryGetValue(command.ToString(), out var keys)) Apply(keys);
        };
        actions.Children.Add(reset);
        var resetAll = new Button { Content = "Reset all" };
        resetAll.Click += (_, _) =>
        {
            try { _bindings.ResetAll(); _message.Text = "Defaults restored."; UpdateSelection(); }
            catch (Exception ex) { _message.Text = ex.Message; }
        };
        actions.Children.Add(resetAll);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        Grid.SetRow(_message, 4);
        root.Children.Add(_message);
        Content = root;
        _filter.TextChanged += (_, _) => RefreshCommands();
        AddHandler(KeyDownEvent, OnCaptureKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        var previous = _commands.SelectedItem;
        var search = _filter.Text ?? "";
        var visible = Enum.GetValues<InputCommand>()
            .Where(c => c.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        _commands.ItemsSource = visible;
        if (previous is InputCommand selected && visible.Contains(selected)) _commands.SelectedItem = selected;
        else _commands.SelectedIndex = 0;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (_commands.SelectedItem is not InputCommand command) { _current.Text = ""; return; }
        var keys = _bindings.GetBindings().GetValueOrDefault(command.ToString(), []);
        _current.Text = $"{command}: {(keys.Length == 0 ? "(unassigned)" : string.Join(", ", keys))}";
    }

    private void Apply(string[] gestures)
    {
        if (_commands.SelectedItem is not InputCommand command) return;
        if (_bindings.TrySetBindings(command, gestures, out var error))
        {
            _message.Text = "Shortcut saved and applied immediately.";
            UpdateSelection();
        }
        else _message.Text = error ?? "Could not save shortcut.";
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { FinishRecording(); return; }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var stroke = KeyBindingService.Normalize(e.Key, e.KeyModifiers);
        if (_firstStroke is null && (stroke.Equals("Ctrl+K", StringComparison.OrdinalIgnoreCase)
            || stroke.Equals("Ctrl+R", StringComparison.OrdinalIgnoreCase)))
        {
            _firstStroke = stroke;
            _message.Text = "Press the second key...";
            return;
        }
        var gesture = _firstStroke is null ? stroke : _firstStroke + " " + stroke;
        if (_appendShortcut && _commands.SelectedItem is InputCommand command)
        {
            var existing = _bindings.GetBindings().GetValueOrDefault(command.ToString(), []);
            Apply([.. existing, gesture]);
        }
        else Apply([gesture]);
        FinishRecording();
    }

    private void BeginRecording(bool append)
    {
        if (_commands.SelectedItem is not InputCommand) return;
        _appendShortcut = append;
        _recording = true;
        _firstStroke = null;
        _capture.Content = "Press shortcut (Esc to cancel)";
        _message.Text = "Press a shortcut, or Ctrl+K / Ctrl+R followed by a second key.";
        Focus();
    }

    private void FinishRecording()
    {
        _recording = false;
        _firstStroke = null;
        _capture.Content = "Record shortcut";
    }
}
