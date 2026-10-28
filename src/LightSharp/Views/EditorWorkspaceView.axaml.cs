using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit;
using LightSharp.Models;
using LightSharp.Services;

namespace LightSharp.Views;

public partial class EditorWorkspaceView : UserControl
{
    private readonly SyntaxErrorRenderer _syntaxErrorRenderer;
    private readonly SemanticColorizingTransformer _semanticColorizer;
    private bool _loadingDocument;
    private IMessageBus? _messageBus;

    public EditorWorkspaceView()
    {
        InitializeComponent();
        _syntaxErrorRenderer = new SyntaxErrorRenderer(TextEditor);
        _semanticColorizer = new SemanticColorizingTransformer(TextEditor.TextArea.TextView);
        TextEditor.Options.IndentationSize = 4;
        TextEditor.Options.ConvertTabsToSpaces = true;
        TextEditor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(TextEditor.Options);
        TextEditor.TextChanged += (_, _) => { if (!_loadingDocument) _messageBus?.Publish(new EditorTextChanged()); };
        TextEditor.KeyDown += (_, args) => _messageBus?.Publish(new EditorKeyPressed(args));
        TextEditor.TextArea.TextEntered += (_, args) => _messageBus?.Publish(new EditorTextEntered(args));
        TextEditor.TextArea.TextEntering += (_, args) => _messageBus?.Publish(new EditorTextEntering(args));
        TextEditor.TextArea.Caret.PositionChanged += (_, _) => _messageBus?.Publish(new EditorCaretMoved());
        TextEditor.TextArea.TextView.PointerMoved += TextView_PointerMoved;
        TextEditor.TextArea.TextView.PointerExited += (_, _) => _messageBus?.Publish(new EditorPointerExited());
        TextEditor.TextArea.TextView.LineTransformers.Add(_semanticColorizer);
    }

    public string Text
    {
        get => TextEditor.Text ?? string.Empty;
        set => TextEditor.Text = value;
    }

    public int CaretOffset
    {
        get => TextEditor.CaretOffset;
        set => TextEditor.CaretOffset = value;
    }

    public int SelectionStart => TextEditor.SelectionStart;
    public int SelectionLength => TextEditor.SelectionLength;
    public string SelectedText => TextEditor.SelectedText;
    public bool IsEditorFocused => TextEditor.IsFocused;
    public bool HasDocument => TextEditor.Document is not null;
    public int TextLength => TextEditor.Document?.TextLength ?? 0;
    public int LineCount => TextEditor.Document?.LineCount ?? 0;
    public int CaretLine => TextEditor.TextArea.Caret.Line;
    public int CaretColumn => TextEditor.TextArea.Caret.Column;

    public void SetDocuments(IEnumerable<EditorDocument> documents) => Tabs.ItemsSource = documents;

    public void SetActiveDocument(EditorDocument? document)
    {
        _loadingDocument = true;
        _semanticColorizer.SetHighlights([]);
        _syntaxErrorRenderer.SetErrors([]);
        TextEditor.Text = document?.Text ?? string.Empty;
        TextEditor.SyntaxHighlighting = EditorHighlightingService.ForFile(document?.FullPath);
        // The lexical highlighter is installed by SyntaxHighlighting; keep semantic
        // colors after it so semantic runs take precedence.
        TextEditor.TextArea.TextView.LineTransformers.Remove(_semanticColorizer);
        TextEditor.TextArea.TextView.LineTransformers.Add(_semanticColorizer);
        TextEditor.TextArea.TextView.Redraw();
        EmptyState.IsVisible = document is null;
        _loadingDocument = false;
    }

    public void SetEmptyStateVisible(bool visible) => EmptyState.IsVisible = visible;
    public void SetSyntaxErrors(IEnumerable<(int Start, int Length)> errors) => _syntaxErrorRenderer.SetErrors(errors);
    public void SetSemanticHighlights(IEnumerable<(int Start, int Length, string Color)> highlights) => _semanticColorizer.SetHighlights(highlights);
    public void FocusEditor() => TextEditor.Focus();
    public void SelectAll() => TextEditor.SelectAll();
    public void Select(int start, int length) => TextEditor.Select(start, length);
    public void ScrollToLine(int line) => TextEditor.ScrollToLine(line);
    public int GetLineOffset(int lineNumber) => TextEditor.Document!.GetLineByNumber(Math.Clamp(lineNumber, 1, TextEditor.Document.LineCount)).Offset;
    public int GetLineEndOffset(int lineNumber) => TextEditor.Document!.GetLineByNumber(Math.Clamp(lineNumber, 1, TextEditor.Document.LineCount)).EndOffset;
    public int GetLineNumberByOffset(int offset) => TextEditor.Document!.GetLineByOffset(Math.Clamp(offset, 0, TextEditor.Document.TextLength)).LineNumber;
    public int GetLineOffsetByOffset(int offset) => TextEditor.Document!.GetLineByOffset(Math.Clamp(offset, 0, TextEditor.Document.TextLength)).Offset;
    public int GetLineEndOffsetByOffset(int offset) => TextEditor.Document!.GetLineByOffset(Math.Clamp(offset, 0, TextEditor.Document.TextLength)).EndOffset;
    public string GetText(int start, int length) => TextEditor.Document!.GetText(start, length);
    public void ReplaceRange(int start, int length, string text) => TextEditor.Document?.Replace(start, length, text);
    public void ReplaceAllText(string text)
    {
        if (TextEditor.Document is { } document) document.Replace(0, document.TextLength, text);
    }
    public void NavigateTo(int line, int column)
    {
        if (TextEditor.Document is null) return;
        var documentLine = TextEditor.Document.GetLineByNumber(Math.Clamp(line, 1, TextEditor.Document.LineCount));
        TextEditor.CaretOffset = Math.Min(documentLine.Offset + Math.Max(0, column - 1), documentLine.EndOffset);
        TextEditor.ScrollToLine(documentLine.LineNumber);
        TextEditor.Focus();
    }
    public CompletionWindow CreateCompletionWindow() => new(TextEditor.TextArea);
    public void SetQuickInfo(string? text) => ToolTip.SetTip(TextEditor, text);
    public void ShowQuickActionMenu(ContextMenu menu)
    {
        TextEditor.ContextMenu = menu;
        menu.Open(TextEditor);
    }

    public void AttachMessageBus(IMessageBus messageBus) => _messageBus = messageBus;

    private void TextView_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not AvaloniaEdit.Rendering.TextView textView || TextEditor.Document is null)
        {
            _messageBus?.Publish(new EditorPointerMoved(null));
            return;
        }
        var position = textView.GetPosition(e.GetPosition(textView));
        _messageBus?.Publish(new EditorPointerMoved(position is null ? null : TextEditor.Document.GetOffset(position.Value.Location)));
    }

    private void ActivateDocument_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EditorDocument document }) _messageBus?.Publish(new ActivateDocumentRequested(document));
    }

    private void CloseDocument_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EditorDocument document }) _messageBus?.Publish(new CloseDocumentRequested(document));
    }
}
