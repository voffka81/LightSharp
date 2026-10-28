namespace LightSharp;

public sealed record EditorProblem(Microsoft.CodeAnalysis.DiagnosticSeverity Severity, int Line, int Column, string Message)
{
    public string DisplayText => $"{Severity}  {Message}   [{Line}, {Column}]";
}
