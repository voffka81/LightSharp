using Avalonia.Input;
using LightSharp.Services;
using Xunit;

namespace LightSharp.Tests;

public sealed class KeyBindingTests
{
    [Fact]
    public void DefaultShortcutsIncludeF12AndSave()
    {
        var handler = new InputHandlerService(new KeyBindingService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        Assert.Equal(InputCommand.GoToDefinition, handler.HandleWindowKeyDown(Key.F12, KeyModifiers.None).Command);
        Assert.Equal(InputCommand.Save, handler.HandleWindowKeyDown(Key.S, KeyModifiers.Control).Command);
    }

    [Fact]
    public void ChordResolvesCommand()
    {
        var handler = new InputHandlerService(new KeyBindingService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        var first = handler.HandleWindowKeyDown(Key.K, KeyModifiers.Control);
        Assert.True(first.Handled);
        Assert.Null(first.Command);
        Assert.Equal(InputCommand.ToggleLineComment, handler.HandleWindowKeyDown(Key.C, KeyModifiers.Control).Command);
    }

    [Fact]
    public void UserOverrideReplacesOnlySelectedCommand()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Save\":[\"Ctrl+Alt+S\"]}");
            var handler = new InputHandlerService(new KeyBindingService(path));
            Assert.False(handler.HandleWindowKeyDown(Key.S, KeyModifiers.Control).Handled);
            Assert.Equal(InputCommand.Save, handler.HandleWindowKeyDown(Key.S, KeyModifiers.Control | KeyModifiers.Alt).Command);
            Assert.Equal(InputCommand.Find, handler.HandleWindowKeyDown(Key.F, KeyModifiers.Control).Command);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidUserJsonFallsBackToDefaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{invalid");
            var bindings = new KeyBindingService(path);
            Assert.NotEmpty(bindings.Warnings);
            Assert.Equal(InputCommand.Save, new InputHandlerService(bindings).HandleWindowKeyDown(Key.S, KeyModifiers.Control).Command);
        }
        finally { File.Delete(path); }
    }
}
