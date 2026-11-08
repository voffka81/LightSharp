using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class EditorSessionStoreTests
{
    [Fact]
    public void RoundTripPreservesOpenFileOrderAndActiveFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new EditorSessionStore(path);
            store.Save(new EditorSession(["one.cs", "two.cs"], "two.cs"));
            var loaded = store.Load();
            Assert.Equal(["one.cs", "two.cs"], loaded.OpenFiles);
            Assert.Equal("two.cs", loaded.ActiveFile);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void InvalidSessionFallsBackToEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{invalid");
            var session = new EditorSessionStore(path).Load();
            Assert.Empty(session.OpenFiles);
            Assert.Null(session.ActiveFile);
        }
        finally { File.Delete(path); }
    }
}
