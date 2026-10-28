using System.ComponentModel;
using System.Runtime.CompilerServices;
using LightSharp.Services;

namespace LightSharp.Models;

public sealed class SearchResultItem(SearchHit hit) : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    public SearchHit Hit { get; } = hit;
    public string DisplayText => $"{Hit.FilePath}  ({Hit.Line}, {Hit.Column}){Environment.NewLine}  {Hit.BeforeLine}{Environment.NewLine}  → {Hit.AfterLine}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
