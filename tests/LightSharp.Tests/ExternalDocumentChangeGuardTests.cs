using LightSharp.Models;
using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class ExternalDocumentChangeGuardTests
{
    [Fact]
    public async Task UnchangedFileCanBeSaved()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await ExternalDocumentChangeGuard.EnsureUnchangedAsync(path, "original");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExternalEditIsDetectedBeforeSave()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "original");
            var document = new EditorDocument(path, "original");
            document.Text = "my edits";
            await File.WriteAllTextAsync(path, "external edits");
            await Assert.ThrowsAsync<IOException>(() =>
                ExternalDocumentChangeGuard.EnsureUnchangedAsync(path, document.SavedText));
            Assert.Equal("external edits", await File.ReadAllTextAsync(path));
            Assert.True(document.IsDirty);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MarkSavedUpdatesExpectedContents()
    {
        var document = new EditorDocument("file.cs", "old");
        document.Text = "new";
        Assert.Equal("old", document.SavedText);
        document.MarkSaved();
        Assert.Equal("new", document.SavedText);
        Assert.False(document.IsDirty);
    }
}
