using LightSharp.Models;
namespace LightSharp.Services;

public sealed class PropertiesPanelController : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public PropertiesPanelController(IMessageBus messageBus, Action openFolder, Action buildSolution)
    {
        _subscriptions.Add(messageBus.Observe<OpenFolderRequested>().Subscribe(_ => openFolder()));
        _subscriptions.Add(messageBus.Observe<BuildRequested>().Subscribe(_ => buildSolution()));
    }

    public void Dispose() => SubscriptionDisposal.DisposeAll(_subscriptions);
}

public sealed class OutputPanelController : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public OutputPanelController(IMessageBus messageBus, Action<EditorProblem> activateProblem, Action runTests)
    {
        _subscriptions.Add(messageBus.Observe<ProblemActivated>().Subscribe(message => activateProblem(message.Problem)));
        _subscriptions.Add(messageBus.Observe<RunTestsRequested>().Subscribe(_ => runTests()));
    }

    public void Dispose() => SubscriptionDisposal.DisposeAll(_subscriptions);
}

public sealed class SolutionExplorerController : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public SolutionExplorerController(
        IMessageBus messageBus,
        SolutionExplorerActionHandlers actions)
    {
        _subscriptions.Add(messageBus.Observe<SolutionExplorerFileOpenRequested>().Subscribe(message => _ = actions.OpenFileAsync(message.Node)));
        _subscriptions.Add(messageBus.Observe<SolutionExplorerCommandRequested>().Subscribe(message => _ = ExecuteAsync(message.Request, actions)));
    }

    public void Dispose() => SubscriptionDisposal.DisposeAll(_subscriptions);

    private static async Task ExecuteAsync(SolutionExplorerActionRequest request, SolutionExplorerActionHandlers actions)
    {
        switch (request.Action)
        {
            case SolutionExplorerAction.OpenFile:
                await actions.OpenFileAsync(request.Node);
                break;
            case SolutionExplorerAction.AddNewFile:
                await actions.AddNewFileAsync(request.Node);
                break;
            case SolutionExplorerAction.AddNewProject:
                await actions.AddNewProjectAsync();
                break;
            case SolutionExplorerAction.AddExistingProject:
                await actions.AddExistingProjectAsync();
                break;
            case SolutionExplorerAction.BuildSolution:
                actions.BuildSolution();
                break;
            case SolutionExplorerAction.TestSolution:
                actions.TestSolution();
                break;
            case SolutionExplorerAction.SetStartupProject:
                actions.SetStartupProject(request.Node);
                break;
            case SolutionExplorerAction.BuildProject:
                actions.BuildProject(request.Node);
                break;
            case SolutionExplorerAction.TestProject:
                actions.TestProject(request.Node);
                break;
            case SolutionExplorerAction.OpenContainingFolder:
                actions.OpenContainingFolder(request.Node);
                break;
            case SolutionExplorerAction.RefreshWorkspace:
                await actions.RefreshWorkspaceAsync();
                break;
        }
    }
}

internal static class SubscriptionDisposal
{
    public static void DisposeAll(List<IDisposable> subscriptions)
    {
        foreach (var subscription in subscriptions) subscription.Dispose();
        subscriptions.Clear();
    }
}


public sealed record SolutionExplorerActionHandlers(
    Func<FileNode, Task> OpenFileAsync,
    Func<FileNode, Task> AddNewFileAsync,
    Func<Task> AddNewProjectAsync,
    Func<Task> AddExistingProjectAsync,
    Action BuildSolution,
    Action TestSolution,
    Action<FileNode> SetStartupProject,
    Action<FileNode> BuildProject,
    Action<FileNode> TestProject,
    Action<FileNode> OpenContainingFolder,
    Func<Task> RefreshWorkspaceAsync);
