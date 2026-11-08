namespace LightSharp.Services;

/// <summary>A ranked, cancellable filename search independent of the UI.</summary>
public static class NavigationFileSearch
{
    public static IReadOnlyList<string> Find(
        IEnumerable<string> paths, string? query, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) return [];
        var term = (query ?? string.Empty).Trim();
        return paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                var score = name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0
                    : name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1
                    : name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2
                    : path.Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : 4;
                return (Path: path, Score: score);
            })
            .Where(item => item.Score < 4)
            .OrderBy(item => item.Score)
            .ThenBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }
}
