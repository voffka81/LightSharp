using LightSharp.Models;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LightSharp.Services;

public sealed class WorkspaceService
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { ".git", "bin", "obj", "node_modules", ".idea", ".vs" };

    public Task<List<FileNode>> LoadAsync(string root, CancellationToken ct = default) =>
        Task.Run(() => LoadDirectory(root, ct), ct);

    public Task<FileNode> LoadSolutionAsync(string solutionPath, CancellationToken ct = default) =>
        Task.Run(() => LoadSolution(solutionPath, ct), ct);

    private static FileNode LoadSolution(string solutionPath, CancellationToken ct)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("The solution must be inside a folder.");
        var root = new FileNode
        {
            Name = $"Solution '{Path.GetFileName(solutionPath)}'",
            FullPath = solutionPath,
            IsDirectory = true,
            IsExpanded = true
        };

        foreach (var projectPath in ReadProjectPaths(solutionPath))
        {
            ct.ThrowIfCancellationRequested();
            var portableProjectPath = projectPath.Replace('\\', Path.DirectorySeparatorChar);
            var fullProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, portableProjectPath));
            if (!File.Exists(fullProjectPath)) continue;

            var project = new FileNode
            {
                Name = Path.GetFileNameWithoutExtension(fullProjectPath),
                FullPath = fullProjectPath,
                IsDirectory = true,
                IsExpanded = true
            };
            var projectDirectory = Path.GetDirectoryName(fullProjectPath);
            if (projectDirectory is not null)
            {
                foreach (var child in LoadDirectory(projectDirectory, ct)
                    .Where(child => !string.Equals(child.FullPath, fullProjectPath, StringComparison.OrdinalIgnoreCase)))
                    project.Children.Add(child);
            }
            root.Children.Add(project);
        }

        return root;
    }

    private static IEnumerable<string> ReadProjectPaths(string solutionPath)
    {
        var extension = Path.GetExtension(solutionPath);
        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            XDocument document;
            try { document = XDocument.Load(solutionPath); }
            catch (System.Xml.XmlException) { yield break; }

            foreach (var project in document.Descendants().Where(element => element.Name.LocalName == "Project"))
            {
                var path = project.Attribute("Path")?.Value;
                if (!string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                    yield return path;
            }
            yield break;
        }

        var content = File.ReadAllText(solutionPath);
        var matches = Regex.Matches(content,
            "Project\\([^)]*\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+\\.csproj)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match match in matches)
            yield return match.Groups[1].Value;
    }

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
