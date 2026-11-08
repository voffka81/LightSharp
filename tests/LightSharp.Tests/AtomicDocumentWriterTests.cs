using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class AtomicDocumentWriterTests
{
    [Fact]
    public async Task ReplacesExistingFileAndLeavesNoTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lightsharp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "sample.cs");
            await File.WriteAllTextAsync(file, "old");
            await AtomicDocumentWriter.WriteAsync(file, "new contents");
            Assert.Equal("new contents", await File.ReadAllTextAsync(file));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CancelledWritePreservesOriginalFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lightsharp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "sample.cs");
            await File.WriteAllTextAsync(file, "original");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicDocumentWriter.WriteAsync(file, "replacement", cts.Token));
            Assert.Equal("original", await File.ReadAllTextAsync(file));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
