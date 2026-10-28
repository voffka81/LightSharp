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
        _active = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _active.OutputDataReceived += (_, e) => { if (e.Data is not null) output(e.Data); };
        _active.ErrorDataReceived += (_, e) => { if (e.Data is not null) output(e.Data); };
        try
        {
            _active.Start();
            _active.BeginOutputReadLine();
            _active.BeginErrorReadLine();
            await _active.WaitForExitAsync(ct);
            return _active.ExitCode;
        }
        catch (Exception ex)
        {
            output($"Unable to run dotnet {command}: {ex.Message}");
            return -1;
        }
        finally { _active?.Dispose(); _active = null; }
    }

    public void Stop()
    {
        try { if (_active is { HasExited: false }) _active.Kill(true); } catch { }
    }
}
