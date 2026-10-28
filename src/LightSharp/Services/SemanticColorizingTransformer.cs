using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace LightSharp.Services;

public sealed class SemanticColorizingTransformer : DocumentColorizingTransformer
{
    private IReadOnlyList<(int Start, int Length, string Color)> _highlights = [];

    public void SetHighlights(IEnumerable<(int Start, int Length, string Color)> highlights)
    {
        var updated = highlights.Where(item => item.Length > 0).OrderBy(item => item.Start).ToArray();
        if (_highlights.SequenceEqual(updated)) return;
        _highlights = updated;
        CurrentContext?.TextView.Redraw();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var lineEnd = line.EndOffset;
        foreach (var highlight in _highlights)
        {
            var start = Math.Max(line.Offset, highlight.Start);
            var end = Math.Min(lineEnd, highlight.Start + highlight.Length);
            if (start >= end) continue;

            var brush = new SolidColorBrush(Color.Parse(highlight.Color));
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
