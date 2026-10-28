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

    public Task<string> FetchAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        RunRemoteOperationAsync(repositoryPath, cancellationToken, "fetch", "--all", "--prune");

    public Task<string> PullAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        RunRemoteOperationAsync(repositoryPath, cancellationToken, "pull", "--ff-only");

    public async Task<string> PushAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var upstream = await RunAsync(repositoryPath, cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (upstream.ExitCode == 0)
            return await RunRemoteOperationAsync(repositoryPath, cancellationToken, "push");

        var remotes = await RunAsync(repositoryPath, cancellationToken, "remote");
        var remote = remotes.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (remote is null)
            throw new InvalidOperationException("This repository has no remote configured.");

        return await RunRemoteOperationAsync(repositoryPath, cancellationToken, "push", "--set-upstream", remote, "HEAD");
    }

    private static async Task<string> RunRemoteOperationAsync(string repositoryPath, CancellationToken cancellationToken, params string[] arguments)
    {
        var remotes = await RunAsync(repositoryPath, cancellationToken, "remote");
        if (remotes.ExitCode != 0 || string.IsNullOrWhiteSpace(remotes.StandardOutput))
            throw new InvalidOperationException("This repository has no remote configured.");

        var result = await RunAsync(repositoryPath, cancellationToken, arguments);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(error.Trim());
        }
        return string.Join(Environment.NewLine, new[] { result.StandardOutput.Trim(), result.StandardError.Trim() }.Where(text => text.Length > 0));
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync();
            try { await Task.WhenAll(stdout, stderr); } catch (IOException) { }
            throw;
        }
        return new GitCommandResult(process.ExitCode, await stdout, await stderr);
    }
}

public sealed record GitChange(string Status, string Path)
{
    public string DisplayText => $"{Status,-2}  {Path}";
}

public sealed record GitStatus(string Branch, IReadOnlyList<GitChange> Changes);
internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
