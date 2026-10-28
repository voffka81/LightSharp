using System.Diagnostics;

namespace LightSharp.Services;

public sealed class GitService
{
    public string? FindRepositoryRoot(string path)
    {
        var current = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    public async Task<GitStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryPath, cancellationToken, "status", "--short", "--branch", "--untracked-files=all");
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.StandardError.Trim().Length > 0 ? result.StandardError.Trim() : result.StandardOutput.Trim());

        var lines = result.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var branch = lines.FirstOrDefault()?.StartsWith("## ", StringComparison.Ordinal) == true
            ? lines[0][3..].Split("...", 2, StringSplitOptions.None)[0]
            : "Unknown";
        if (branch.StartsWith("No commits yet on ", StringComparison.Ordinal))
            branch = branch["No commits yet on ".Length..];

        var changes = lines.Skip(1)
            .Where(line => line.Length >= 4)
            .Select(line => new GitChange(line[..2].Trim(), line[3..]))
            .ToArray();
        return new GitStatus(branch, changes);
    }

    public async Task<string> InitializeAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(path, cancellationToken, "init");
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim());
        return result.StandardOutput.Trim();
    }

    public async Task StageAllAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryPath, cancellationToken, "add", "--all");
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim());
    }

    public async Task<string> CommitAllAsync(string repositoryPath, string message, CancellationToken cancellationToken = default)
    {
        await StageAllAsync(repositoryPath, cancellationToken);
        var result = await RunAsync(repositoryPath, cancellationToken, "commit", "-m", message);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim().Length > 0 ? result.StandardError.Trim() : result.StandardOutput.Trim());
        return result.StandardOutput.Trim();
    }

    private static async Task<GitCommandResult> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Git could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new GitCommandResult(process.ExitCode, await stdout, await stderr);
    }
}

public sealed record GitChange(string Status, string Path)
{
    public string DisplayText => $"{Status,-2}  {Path}";
}

public sealed record GitStatus(string Branch, IReadOnlyList<GitChange> Changes);
internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
