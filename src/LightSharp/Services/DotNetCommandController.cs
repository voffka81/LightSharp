using LightSharp.Models;
using LightSharp.Views;

namespace LightSharp.Services;

public sealed class DotNetCommandController : IDisposable
{
    private readonly DotNetService _dotnet;
    private readonly OutputPanelView _output;
    private readonly Action<bool> _setStopEnabled;
    private readonly Func<string?> _workspacePath;
    private readonly Func<string> _configuration;
    private readonly Func<FileNode?> _startupProject;
    private readonly Action<string> _appendOutput;
    private CancellationTokenSource? _commandCts;

    public DotNetCommandController(
        DotNetService dotnet,
        OutputPanelView output,
        Action<bool> setStopEnabled,
        Func<string?> workspacePath,
        Func<string> configuration,
        Func<FileNode?> startupProject,
        Action<string> appendOutput)
    {
        _dotnet = dotnet;
        _output = output;
        _setStopEnabled = setStopEnabled;
        _workspacePath = workspacePath;
        _configuration = configuration;
        _startupProject = startupProject;
        _appendOutput = appendOutput;
    }

    public Task BuildAsync() => ExecuteAsync("build", "--configuration", _configuration());

    public Task TestAsync() => ExecuteAsync("test", "--configuration", _configuration());

    public Task StartAsync()
    {
        if (_startupProject() is { } project)
            return ExecuteAsync("run", "--configuration", _configuration(), "--project", project.FullPath);
        return ExecuteAsync("run", "--configuration", _configuration());
    }

    public void Stop()
    {
        _commandCts?.Cancel();
        _dotnet.Stop();
        _setStopEnabled(false);
        _output.StatusMessage = "Stopped";
    }

    public Task ExecuteAsync(params string[] arguments)
    {
        if (_workspacePath() is not { } workspacePath)
        {
            _appendOutput("Open a workspace first.");
            return Task.CompletedTask;
        }
        return RunAsync(workspacePath, arguments);
    }

    public async Task<int> RunAsync(string workingDirectory, params string[] arguments)
    {
        _commandCts?.Cancel();
        _commandCts?.Dispose();
        var commandCts = new CancellationTokenSource();
        _commandCts = commandCts;
        _output.ClearOutput();
        var isTestRun = arguments.FirstOrDefault()?.Equals("test", StringComparison.OrdinalIgnoreCase) == true;
        _output.ShowView(isTestRun ? "Tests" : "Output");
        _output.IsVisible = true;
        var command = string.Join(" ", arguments);
        _output.StatusMessage = $"dotnet {command}...";
        _setStopEnabled(true);
        var exitCode = await _dotnet.ExecuteAsync(workingDirectory, arguments, _appendOutput, commandCts.Token);
        if (!ReferenceEquals(_commandCts, commandCts)) return exitCode;
        _commandCts.Dispose();
        _commandCts = null;
        _setStopEnabled(false);
        _output.StatusMessage = $"dotnet {command} exited with code {exitCode}";
        if (isTestRun)
            _output.TestSummary = exitCode == 0
                ? "Test run completed successfully. See Output for detailed results."
                : $"Test run failed (exit code {exitCode}). See Output for details.";
        return exitCode;
    }

    public void Dispose()
    {
        _commandCts?.Cancel();
        _commandCts?.Dispose();
        _commandCts = null;
        _dotnet.Stop();
    }
}
