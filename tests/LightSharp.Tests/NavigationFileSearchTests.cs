using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class NavigationFileSearchTests
{
    [Fact]
    public void ExactNameRanksAheadOfPrefixAndSubstring()
    {
        var paths = new[] { "/src/MyEditor.cs", "/src/Editor.cs", "/other/EditorTools.cs" };
        var results = NavigationFileSearch.Find(paths, "Editor.cs");
        Assert.Equal("/src/Editor.cs", results[0]);
        Assert.Equal("/src/MyEditor.cs", results[1]);
    }

    [Fact]
    public void SearchIgnoresCaseAndRemovesDuplicates()
    {
        var results = NavigationFileSearch.Find(
            new[] { "/src/Program.cs", "/src/PROGRAM.cs", "/src/Other.cs" }, "program");
        Assert.Single(results);
    }

    [Fact]
    public void CancelledSearchThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            NavigationFileSearch.Find(new[] { "/src/Program.cs" }, "program", cancellationToken: cancellation.Token));
    }

    [Fact]
    public void LimitIsRespected()
    {
        Assert.Single(NavigationFileSearch.Find(new[] { "One.cs", "Two.cs" }, ".cs", limit: 1));
    }
}
