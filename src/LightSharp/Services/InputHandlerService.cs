using Avalonia.Input;

namespace LightSharp.Services;

public enum InputCommand
{
    Find, GoToDefinition, Replace, FindInFiles, OpenSolution, OpenFile,
    NewProject, Save, SaveAll, Build, Start, Stop, CloseDocument, SelectAll,
    NextDocument, PreviousDocument, Test, ToggleLineComment, UncommentLine,
    FormatDocument, FormatSelection
}

public readonly record struct InputHandlerResult(bool Handled, InputCommand? Command = null);

/// <summary>Routes keyboard events through configurable command bindings.</summary>
public sealed class InputHandlerService
{
    private readonly KeyBindingService _bindings;
    private string? _pending;

    public InputHandlerService(KeyBindingService? bindings = null)
    {
        _bindings = bindings ?? new KeyBindingService();
    }

    public InputHandlerResult HandleWindowKeyDown(Key key, KeyModifiers modifiers)
    {
        var stroke = KeyBindingService.Normalize(key, modifiers);
        if (_pending is not null)
        {
            var sequence = _pending + " " + stroke;
            _pending = null;
            var command = _bindings.Resolve(sequence);
            if (command is not null) return new(true, command);
            if (_bindings.IsPrefix(sequence))
            {
                _pending = sequence;
                return new(true);
            }
        }

        var single = _bindings.Resolve(stroke);
        if (single is not null) return new(true, single);
        if (_bindings.IsPrefix(stroke))
        {
            _pending = stroke;
            return new(true);
        }
        return default;
    }
}
