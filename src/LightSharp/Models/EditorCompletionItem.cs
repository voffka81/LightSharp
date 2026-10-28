using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace LightSharp.Models;

public sealed class EditorCompletionItem : ICompletionData
{
    private readonly Action<TextArea, ISegment, EventArgs> _complete;

    public EditorCompletionItem(
        string text,
        string? description,
        Action<TextArea, ISegment, EventArgs> complete,
        double priority = 0)
    {
        Text = text;
        Description = description ?? string.Empty;
        _complete = complete;
        Priority = priority;
    }

    public IImage Image => null!;
    public string Text { get; }
    public object Content => Text;
    public object Description { get; }
    public double Priority { get; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        _complete(textArea, completionSegment, insertionRequestEventArgs);
}
