using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using TrimCSS.Models;

namespace TrimCSS.GUI.Models;

public sealed class CssFileRowItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public CssFileRowItem(UnusedCssFileInfo info)
    {
        Info = info;
        IsUnused = info.Status == CssFileReferenceStatus.Unused;
        Status = IsUnused ? "UNUSED" : "AMBIGUOUS";
        RelativePath = info.RelativePath;
        SelectorCount = info.SelectorCount;
        FileSizeBytes = info.FileSizeBytes;
        FormattedSize = FormatBytes(info.FileSizeBytes);
        Details = IsUnused
            ? "No references found in views, pages, CSS, or JS"
            : (info.MatchedByFiles.Count > 0
                ? $"Matched ambiguously in: {string.Join(", ", info.MatchedByFiles.Select(Path.GetFileName))}"
                : "Ambiguous reference or shared basename match");
    }

    public UnusedCssFileInfo Info { get; }
    public bool IsUnused { get; }
    public string Status { get; }
    public string RelativePath { get; }
    public string FullFilePath => Info.FilePath;
    public int SelectorCount { get; }
    public long FileSizeBytes { get; }
    public string FormattedSize { get; }
    public string Details { get; }

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

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
