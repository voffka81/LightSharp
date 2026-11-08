namespace LightSharp.Services;

public sealed record CodeReferenceLocation(string FilePath, int Line, int Column, int Length);
