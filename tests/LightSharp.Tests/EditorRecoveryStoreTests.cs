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

    [Fact]
    public void UnresolvedConflictSurvivesRepeatedAutosaveAndRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new EditorRecoveryStore(path);
            var conflict = new RecoverySnapshot("conflict.cs", "old disk", "important unsaved edits");
            store.Save([], [conflict]);
            store.Save([new EditorDocument("other.cs", "clean")], store.Load());
            store.Save([], store.Load());

            Assert.Equal(conflict, Assert.Single(new EditorRecoveryStore(path).Load()));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DirtyBufferSupersedesOldRecoveryForSamePath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new EditorRecoveryStore(path);
            var old = new RecoverySnapshot("sample.cs", "old", "old draft");
            var document = new EditorDocument("sample.cs", "new disk");
            document.Text = "new draft";
            store.Save([document], [old]);

            var snapshot = Assert.Single(store.Load());
            Assert.Equal("new disk", snapshot.SavedText);
            Assert.Equal("new draft", snapshot.DraftText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void UnrelatedDirtyDocumentDoesNotRemoveConflictedBackup()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new EditorRecoveryStore(path);
            var conflict = new RecoverySnapshot("missing.cs", "baseline", "recover me");
            var edited = new EditorDocument("other.cs", "baseline");
            edited.Text = "new edits";
            store.Save([edited], [conflict]);

            var snapshots = store.Load();
            Assert.Equal(2, snapshots.Length);
            Assert.Contains(snapshots, s => s == conflict);
            Assert.Contains(snapshots, s => s.Path == "other.cs" && s.DraftText == "new edits");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void InvalidRecoveryJsonDoesNotCrashStartup()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{broken json");
            Assert.Empty(new EditorRecoveryStore(path).Load());
        }
        finally { File.Delete(path); }
    }
}
