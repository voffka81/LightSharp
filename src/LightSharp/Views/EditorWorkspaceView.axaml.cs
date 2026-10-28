using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using LightSharp.Models;

namespace LightSharp.Views;

public partial class EditorWorkspaceView : UserControl
{
    public EditorWorkspaceView()
    {
        InitializeComponent();
        TextEditor.TextChanged += (sender, args) => DocumentTextChanged?.Invoke(sender, args);
        TextEditor.KeyDown += (sender, args) => EditorKeyDown?.Invoke(sender, args);
    }

    public TextEditor Editor => TextEditor;
    public ItemsControl DocumentTabs => Tabs;
    public Control EmptyEditorText => EmptyState;

    public event EventHandler<EventArgs>? DocumentTextChanged;
    public event EventHandler<KeyEventArgs>? EditorKeyDown;
    public event EventHandler<RoutedEventArgs>? DocumentActivated;
    public event EventHandler<RoutedEventArgs>? DocumentCloseRequested;

    private void ActivateDocument_Click(object? sender, RoutedEventArgs e) => DocumentActivated?.Invoke(sender, e);
    private void CloseDocument_Click(object? sender, RoutedEventArgs e) => DocumentCloseRequested?.Invoke(sender, e);
}
