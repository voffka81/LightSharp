using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LightSharp.Models;

public sealed class EditorDocument : INotifyPropertyChanged
{
    private string _text;
    private bool _isDirty;
    private bool _isActive;

    public EditorDocument(string fullPath, string text)
    {
        FullPath = fullPath;
        _text = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }
    public string Name => Path.GetFileName(FullPath);
    public string TabTitle => IsDirty ? $"{Name} *" : Name;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
            IsDirty = true;
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabTitle));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public void MarkSaved() => IsDirty = false;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
