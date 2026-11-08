namespace LightSharp.Services;

/// <summary>Exports a recovered draft without recreating or overwriting its missing source file.</summary>
public static class MissingFileRecoveryExporter
{
    public static async Task ExportAsync(string destination, RecoverySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(snapshot.Path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Recovery export must not overwrite the original source path.");
        // CreateNew prevents accidental overwrites even if the destination appears after selection.
        await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(snapshot.DraftText.AsMemory(), cancellationToken);
    }
}
