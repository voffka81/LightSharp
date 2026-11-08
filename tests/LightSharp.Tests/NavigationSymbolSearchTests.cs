using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class NavigationSymbolSearchTests
{
    [Fact]
    public void FindsDeclarationsAndOneBasedLocations()
    {
        var source = "class Editor {\n    void SaveFile() {}\n    int SaveCount { get; set; }\n}";
        var matches = NavigationSymbolSearch.Find([("Editor.cs", source)], "Save");
        Assert.Equal(2, matches.Count);
        Assert.Equal(("SaveCount", 3, 9), (matches[0].Name, matches[0].Line, matches[0].Column));
        Assert.Equal(("SaveFile", 2, 10), (matches[1].Name, matches[1].Line, matches[1].Column));
    }

    [Fact]
    public void ExactMatchRanksFirst()
    {
        var matches = NavigationSymbolSearch.Find([("a.cs", "class Editor {} class EditorTools {}")], "Editor");
        Assert.Equal("Editor", matches[0].Name);
    }

    [Fact]
    public void CancelledSearchThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            NavigationSymbolSearch.Find([("a.cs", "class Editor {}")], "Editor", cancellationToken: cts.Token));
    }

    [Fact]
    public void EmptyQueryReturnsNoResults()
    {
        Assert.Single(NavigationSymbolSearch.Find([("a.cs", "class Editor {}")], " "));
    }
}
