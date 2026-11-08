using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class MissingFileRecoveryExporterTests
{
    [Fact]
    public async Task ExportsDraftWithoutRecreatingMissingSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "missing.cs");
            var destination = Path.Combine(directory, "recovered.cs");
            await MissingFileRecoveryExporter.ExportAsync(destination,
                new RecoverySnapshot(original, "saved", "unsaved edits"));
            Assert.Equal("unsaved edits", await File.ReadAllTextAsync(destination));
            Assert.False(File.Exists(original));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RefusesToOverwriteExistingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "missing.cs");
            var destination = Path.Combine(directory, "existing.cs");
            await File.WriteAllTextAsync(destination, "keep me");
            await Assert.ThrowsAsync<IOException>(() =>
                MissingFileRecoveryExporter.ExportAsync(destination,
                    new RecoverySnapshot(original, "saved", "draft")));
            Assert.Equal("keep me", await File.ReadAllTextAsync(destination));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RefusesOriginalSourcePathEvenIfItIsMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "missing.cs");
            await Assert.ThrowsAsync<IOException>(() =>
                MissingFileRecoveryExporter.ExportAsync(original,
                    new RecoverySnapshot(original, "saved", "draft")));
            Assert.False(File.Exists(original));
        }
        finally { Directory.Delete(directory, true); }
    }
}
