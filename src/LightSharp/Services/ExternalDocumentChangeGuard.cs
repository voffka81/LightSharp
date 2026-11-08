namespace LightSharp.Services;

public static class ExternalDocumentChangeGuard
{
    /// <summary>Reject saving when disk contents differ from the version opened or last saved.</summary>
    public static async Task EnsureUnchangedAsync(string path, string expectedText, CancellationToken cancellationToken = default)
    {
        var current = await File.ReadAllTextAsync(path, cancellationToken);
        if (!string.Equals(current, expectedText, StringComparison.Ordinal))
            throw new IOException("File was modified outside LightSharp. Save cancelled to protect external changes.");
    }
}
