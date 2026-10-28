using LightSharp.Models;

namespace LightSharp.Services;

public sealed class WorkspaceService
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { ".git", "bin", "obj", "node_modules", ".idea", ".vs" };

    public Task<List<FileNode>> LoadAsync(string root, CancellationToken ct = default) =>
        Task.Run(() => LoadDirectory(root, ct), ct);

    private static List<FileNode> LoadDirectory(string path, CancellationToken ct)
    {
        var result = new List<FileNode>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path).OrderBy(Path.GetFileName))
            {
                ct.ThrowIfCancellationRequested();
                if (Ignored.Contains(Path.GetFileName(directory))) continue;
                var node = new FileNode { Name = Path.GetFileName(directory), FullPath = directory, IsDirectory = true };
                foreach (var child in LoadDirectory(directory, ct)) node.Children.Add(child);
                result.Add(node);
            }
            foreach (var file in Directory.EnumerateFiles(path).OrderBy(Path.GetFileName))
            {
                ct.ThrowIfCancellationRequested();
                result.Add(new FileNode { Name = Path.GetFileName(file), FullPath = file, IsDirectory = false });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return result;
    }
}
