using LightSharp.Models;
using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class EditorRecoveryStoreTests
{
    [Fact]
    public void RoundTripRetainsDirtyBufferAndOriginalBaseline()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = new EditorDocument("sample.cs", "original");
            document.Text = "unsaved";
            var store = new EditorRecoveryStore(path);
            store.Save([document]);
            var snapshot = Assert.Single(store.Load());
            Assert.Equal("sample.cs", snapshot.Path);
            Assert.Equal("original", snapshot.SavedText);
            Assert.Equal("unsaved", snapshot.DraftText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CleanDocumentsAreNotBackedUp()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new EditorRecoveryStore(path);
            store.Save([new EditorDocument("sample.cs", "clean")]);
            Assert.Empty(store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
