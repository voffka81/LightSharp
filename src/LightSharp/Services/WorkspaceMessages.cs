using Avalonia.Input;
using LightSharp.Models;

namespace LightSharp.Services;

public sealed record SolutionExplorerFileOpenRequested(FileNode Node);
public sealed record CloseDocumentRequested(EditorDocument Document);
public sealed record ActivateDocumentRequested(EditorDocument Document);
public sealed record EditorTextChanged;
public sealed record EditorKeyPressed(KeyEventArgs Args);
public sealed record EditorTextEntered(TextInputEventArgs Args);
public sealed record EditorTextEntering(TextInputEventArgs Args);
public sealed record EditorPointerMoved(int? Offset);
public sealed record EditorPointerExited;
public sealed record EditorCaretMoved;
public sealed record ProblemActivated(EditorProblem Problem);
public sealed record RunTestsRequested;
public sealed record OpenFolderRequested;
public sealed record BuildRequested;
public sealed record SolutionExplorerCommandRequested(SolutionExplorerActionRequest Request);
public sealed record GitRefreshRequested;
public sealed record GitStageAllRequested;
public sealed record GitFetchRequested;
public sealed record GitPullRequested;
public sealed record GitPushRequested;
public sealed record GitCommitRequested(string Message);
public sealed record StatusMessageChanged(string Message);
public sealed record OutputLineRequested(string Line);
public sealed record GitBranchChanged(string Branch);
public sealed record GitRepositoryAvailabilityChanged(bool HasWorkspace, bool HasRepository);
public sealed record EditorStatusChanged(string Message);
public sealed record EditorCursorChanged(int Line, int Column);

public enum SolutionExplorerAction
{
    OpenFile,
    AddNewFile,
    AddNewProject,
    AddExistingProject,
    BuildSolution,
    TestSolution,
    SetStartupProject,
    BuildProject,
    TestProject,
    OpenContainingFolder,
    RefreshWorkspace
}

public sealed record SolutionExplorerActionRequest(FileNode Node, SolutionExplorerAction Action);
