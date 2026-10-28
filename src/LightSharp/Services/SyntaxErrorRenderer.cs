using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace LightSharp.Services;

public sealed class SyntaxErrorRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Pen _pen = new(new SolidColorBrush(Color.Parse(EditorThemeConfiguration.Current.SyntaxErrorColor)), 1.5);
    private IReadOnlyList<(int Offset, int Length)> _errors = [];

    public SyntaxErrorRenderer(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    public KnownLayer Layer => KnownLayer.Text;

    public void SetErrors(IEnumerable<(int Offset, int Length)> errors)
    {
        _errors = errors.ToArray();
        _editor.TextArea.TextView.Redraw();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = _editor.Document;
        if (document is null || _errors.Count == 0 || !textView.VisualLinesValid) return;

        var firstVisibleLine = textView.VisualLines[0].FirstDocumentLine.LineNumber;
        var lastVisibleLine = textView.VisualLines[^1].LastDocumentLine.LineNumber;
        foreach (var error in _errors)
        {
            var startOffset = Math.Clamp(error.Offset, 0, document.TextLength);
            var endOffset = Math.Clamp(startOffset + Math.Max(error.Length, 1), startOffset, document.TextLength);
            var startLocation = document.GetLocation(startOffset);
            var endLocation = document.GetLocation(endOffset);
            var fromLine = Math.Max(startLocation.Line, firstVisibleLine);
            var toLine = Math.Min(endLocation.Line, lastVisibleLine);

            for (var lineNumber = fromLine; lineNumber <= toLine; lineNumber++)
            {
                var line = document.GetLineByNumber(lineNumber);
                var startColumn = lineNumber == startLocation.Line ? startLocation.Column : 1;
                var endColumn = lineNumber == endLocation.Line ? endLocation.Column : line.Length + 1;
                endColumn = Math.Max(startColumn + 1, endColumn);

                var start = textView.GetVisualPosition(new TextViewPosition(lineNumber, startColumn), VisualYPosition.TextBottom);
                var end = textView.GetVisualPosition(new TextViewPosition(lineNumber, endColumn), VisualYPosition.TextBottom);
                DrawWavyUnderline(drawingContext, start.X, end.X, start.Y + 1);
            }
        }
    }

    private void DrawWavyUnderline(DrawingContext context, double startX, double endX, double y)
    {
        if (endX <= startX) endX = startX + 4;
        const double step = 3;
        var previous = new Point(startX, y);
        var up = true;
        for (var x = startX + step; x < endX; x += step)
        {
            var next = new Point(x, y + (up ? -1.5 : 1.5));
            context.DrawLine(_pen, previous, next);
            previous = next;
            up = !up;
        }
        context.DrawLine(_pen, previous, new Point(endX, y + (up ? -1.5 : 1.5)));
    }
}
