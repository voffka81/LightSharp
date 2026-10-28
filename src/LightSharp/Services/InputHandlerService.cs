using Avalonia.Input;

namespace LightSharp.Services;

public enum InputCommand
{
    Find,
    Replace,
    FindInFiles,
    OpenSolution,
    OpenFile,
    NewProject,
    Save,
    SaveAll,
    Build,
    Start,
    Stop,
    CloseDocument,
    SelectAll,
    NextDocument,
    PreviousDocument,
    Test,
    ToggleLineComment,
    UncommentLine,
    FormatDocument,
    FormatSelection
}

public readonly record struct InputHandlerResult(bool Handled, InputCommand? Command = null);

/// <summary>Maps keyboard gestures to editor commands without depending on window controls.</summary>
public sealed class InputHandlerService
{
    private enum ChordPrefix { None, CtrlK, CtrlR }

    private ChordPrefix _pendingPrefix;

    public InputHandlerResult HandleWindowKeyDown(Key key, KeyModifiers modifiers)
    {
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

        if (_pendingPrefix is ChordPrefix.CtrlK or ChordPrefix.CtrlR)
        {
            var prefix = _pendingPrefix;
            _pendingPrefix = ChordPrefix.None;
            if (ctrl && prefix == ChordPrefix.CtrlR && key == Key.T)
                return new(true, InputCommand.Test);
            if (ctrl && prefix == ChordPrefix.CtrlK)
            {
                return key switch
                {
                    Key.C => new(true, InputCommand.ToggleLineComment),
                    Key.U => new(true, InputCommand.UncommentLine),
                    Key.D => new(true, InputCommand.FormatDocument),
                    Key.F => new(true, InputCommand.FormatSelection),
                    _ => MapSingleKey(key, modifiers)
                };
            }
        }

        if (ctrl && !shift && key == Key.K)
        {
            _pendingPrefix = ChordPrefix.CtrlK;
            return new(true);
        }
        if (ctrl && !shift && key == Key.R)
        {
            _pendingPrefix = ChordPrefix.CtrlR;
            return new(true);
        }

        return MapSingleKey(key, modifiers);
    }

    private static InputHandlerResult MapSingleKey(Key key, KeyModifiers modifiers)
    {
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && shift)
        {
            return key switch
            {
                Key.F => new(true, InputCommand.FindInFiles),
                Key.O => new(true, InputCommand.OpenSolution),
                Key.N => new(true, InputCommand.NewProject),
                Key.S => new(true, InputCommand.SaveAll),
                Key.B => new(true, InputCommand.Build),
                _ => MapF5(key, shift, ctrl)
            };
        }
        if (ctrl)
        {
            return key switch
            {
                Key.H => new(true, InputCommand.Replace),
                Key.F => new(true, InputCommand.Find),
                Key.O => new(true, InputCommand.OpenFile),
                Key.S => new(true, InputCommand.Save),
                Key.F4 => new(true, InputCommand.CloseDocument),
                Key.A => new(true, InputCommand.SelectAll),
                Key.Tab => new(true, shift ? InputCommand.PreviousDocument : InputCommand.NextDocument),
                _ => MapF5(key, shift, ctrl)
            };
        }
        return MapF5(key, shift, ctrl);
    }

    private static InputHandlerResult MapF5(Key key, bool shift, bool ctrl)
    {
        if (key != Key.F5) return default;
        return new(true, shift ? InputCommand.Stop : InputCommand.Start);
    }
}
