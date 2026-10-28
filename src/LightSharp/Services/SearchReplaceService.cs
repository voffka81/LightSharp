using System.Text.RegularExpressions;

namespace LightSharp.Services;

public sealed record SearchRequest(
    string Query,
    string Replacement,
    bool MatchCase,
    bool WholeWord,
    bool UseRegularExpressions);

public sealed record SearchHit(
    string FilePath,
    int Start,
    int Length,
    string MatchedText,
    int Line,
    int Column,
    string BeforeLine,
    string AfterLine,
    string ReplacementText);

public static class SearchReplaceService
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(300);

    public static async Task<IReadOnlyList<SearchHit>> SearchAsync(
        IEnumerable<string> filePaths,
        IReadOnlyDictionary<string, string> openDocumentTexts,
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return [];
        var paths = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return await Task.Run(async () =>
        {
            var results = new List<SearchHit>();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text;
                if (!openDocumentTexts.TryGetValue(path, out text!))
                {
                    try { text = await File.ReadAllTextAsync(path, cancellationToken); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                }

                results.AddRange(FindInText(path, text, request));
            }
            return (IReadOnlyList<SearchHit>)results;
        }, cancellationToken);
    }

    public static IReadOnlyList<SearchHit> FindInText(string filePath, string source, SearchRequest request)
    {
        if (string.IsNullOrEmpty(request.Query)) return [];
        var pattern = request.UseRegularExpressions ? request.Query : Regex.Escape(request.Query);
        if (request.WholeWord) pattern = $"(?<![\\p{{L}}\\p{{N}}_])(?:{pattern})(?![\\p{{L}}\\p{{N}}_])";
        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (!request.MatchCase) options |= RegexOptions.IgnoreCase;

        var expression = new Regex(pattern, options, MatchTimeout);
        var hits = new List<SearchHit>();
        var line = 1;
        var lineStart = 0;
        var scanPosition = 0;
        foreach (Match match in expression.Matches(source))
        {
            if (!match.Success || match.Length == 0) continue;
            while (scanPosition < match.Index)
            {
                if (source[scanPosition] == '\n')
                {
                    line++;
                    lineStart = scanPosition + 1;
                }
                scanPosition++;
            }
            var lineEnd = source.IndexOf('\n', match.Index);
            if (lineEnd < 0) lineEnd = source.Length;
            if (lineEnd > lineStart && source[lineEnd - 1] == '\r') lineEnd--;
            var beforeLine = source[lineStart..lineEnd];
            var replacementText = request.UseRegularExpressions
                ? match.Result(request.Replacement)
                : request.Replacement;
            var relativeStart = match.Index - lineStart;
            var afterLine = beforeLine[..relativeStart] + replacementText + beforeLine[(relativeStart + match.Length)..];
            hits.Add(new SearchHit(
                filePath,
                match.Index,
                match.Length,
                match.Value,
                line,
                relativeStart + 1,
                beforeLine,
                afterLine,
                replacementText));
        }
        return hits;
    }
}
