using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using TrimCSS.Models;

namespace TrimCSS.GUI.Models;

public sealed class SelectorRowItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectorRowItem(CssSelectorInfo info, bool isUnused, string projectRoot)
    {
        Info = info;
        IsUnused = isUnused;
        Status = isUnused ? "UNUSED" : "AMBIGUOUS";
        TargetClassesDisplay = info.TargetClasses.Count > 0
            ? string.Join("  ", info.TargetClasses.Select(c => $".{c}"))
            : "—";
        RelativeFilePath = !string.IsNullOrEmpty(projectRoot) && !string.IsNullOrEmpty(info.FilePath)
            ? Path.GetRelativePath(projectRoot, info.FilePath)
            : info.FilePath;
        Location = $"{info.LineNumber}:{info.ColumnNumber}";
    }

    public CssSelectorInfo Info { get; }
    public bool IsUnused { get; }
    public string Status { get; }
    public string SelectorText => Info.SelectorText;
    public string TargetClassesDisplay { get; }
    public string FullFilePath => Info.FilePath;
    public string RelativeFilePath { get; }
    public string Location { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}