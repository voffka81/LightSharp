using System.Collections.ObjectModel;

namespace LightSharp.Models;

public sealed class FileNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsExpanded { get; set; }
    public bool IsFile => !IsDirectory;
    public ObservableCollection<FileNode> Children { get; } = [];
    public string Glyph => Path.GetExtension(Name).ToLowerInvariant() switch
    {
        ".sln" or ".slnx" => "SLN",
        ".csproj" or ".cs" => "C#",
        ".json" => "{}",
        ".xml" or ".axaml" or ".config" => "XML",
        _ => "FILE"
    };
}
