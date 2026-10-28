using System.Diagnostics;

namespace LightSharp.Services;

public sealed class DotNetService
{
    private Process? _active;

    public async Task<int> ExecuteAsync(string workingDirectory, IReadOnlyList<string> arguments, Action<string> output, CancellationToken ct)
    {
        Stop();
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        var command = string.Join(" ", arguments);
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _active = process;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output(e.Data); };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { await process.WaitForExitAsync(); } catch { }
                output($"dotnet {command} was stopped.");
                return -1;
            }
            output($"Unable to run dotnet {command}: {ex.Message}");
            return -1;
        }
        finally
        {
            process.Dispose();
            if (ReferenceEquals(_active, process)) _active = null;
        }
    }

    public void Stop()
    {
        try { if (_active is { HasExited: false }) _active.Kill(true); } catch { }
    }
}
