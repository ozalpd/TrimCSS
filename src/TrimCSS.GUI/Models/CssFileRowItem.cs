using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TrimCSS.Models;

namespace TrimCSS.GUI.Models;

public sealed class CssFileRowItem : INotifyPropertyChanged
{
    private static readonly SolidColorBrush FallbackRedBrush = CreateFrozenBrush(0xEF, 0x44, 0x44);
    private static readonly SolidColorBrush FallbackAmberBrush = CreateFrozenBrush(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush FallbackGreenBrush = CreateFrozenBrush(0x10, 0xB9, 0x81);

    private bool _isSelected;

    public CssFileRowItem(UnusedCssFileInfo info)
    {
        Info = info;
        IsUnused = info.Status == CssFileReferenceStatus.Unused;
        IsAmbiguous = info.Status == CssFileReferenceStatus.Ambiguous;
        IsReferenced = info.Status == CssFileReferenceStatus.Referenced;
        Status = info.Status switch
        {
            CssFileReferenceStatus.Unused => "NOT REFERENCED",
            CssFileReferenceStatus.Ambiguous => "AMBIGUOUS",
            CssFileReferenceStatus.Referenced => "REFERENCED",
            _ => "NOT REFERENCED"
        };
        RelativePath = info.RelativePath;
        TotalSelectors = info.TotalSelectors > 0 ? info.TotalSelectors : info.SelectorCount;
        UsedSelectors = info.UsedSelectors;
        UnusedSelectors = info.UnusedSelectors;
        AmbiguousSelectors = info.AmbiguousSelectors;
        SelectorCount = TotalSelectors;
        UsagePercentage = info.UsagePercentage;
        UsageText = $"{info.UsagePercentage:0}%";
        UsageTooltip = TotalSelectors == 0
            ? "0 selectors parsed from this file"
            : $"{UsedSelectors:N0} used, {UnusedSelectors:N0} unused, {AmbiguousSelectors:N0} ambiguous ({TotalSelectors:N0} total)";
        FileSizeBytes = info.FileSizeBytes;
        FormattedSize = FormatBytes(info.FileSizeBytes);
        ImportedCssFiles = info.ImportedCssFiles ?? [];
        ImportedCssFilesCount = ImportedCssFiles.Count;
        string? rowDir = NormalizeDirectory(info.RelativePath);
        ImportedFilesDisplay = ImportedCssFiles.Count == 0
            ? "—"
            : string.Join(", ", ImportedCssFiles.Select(imp =>
            {
                string? impDir = NormalizeDirectory(imp);
                return string.Equals(rowDir, impDir, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(imp)
                    : imp;
            }));
        ImportedFilesTooltip = ImportedCssFiles.Count == 0
            ? null
            : $"Imports {ImportedCssFiles.Count} CSS {(ImportedCssFiles.Count == 1 ? "file" : "files")}:\n{string.Join("\n", ImportedCssFiles)}";
        Details = info.Status switch
        {
            CssFileReferenceStatus.Unused => "No references found in views, pages, CSS, or JS",
            CssFileReferenceStatus.Ambiguous => info.MatchedByFiles.Count > 0
                ? $"Matched ambiguously in: {string.Join(", ", info.MatchedByFiles.Select(Path.GetFileName))}"
                : "Ambiguous reference or shared basename match",
            CssFileReferenceStatus.Referenced => TotalSelectors > 0
                ? $"{UsedSelectors:N0} of {TotalSelectors:N0} selectors used ({UnusedSelectors:N0} unused)"
                : "Referenced stylesheet (0 selectors)",
            _ => string.Empty
        };
    }

    public UnusedCssFileInfo Info { get; }
    public bool IsUnused { get; }
    public bool IsAmbiguous { get; }
    public bool IsReferenced { get; }
    public string Status { get; }
    public string RelativePath { get; }
    public string FullFilePath => Info.FilePath;
    public int SelectorCount { get; }
    public int TotalSelectors { get; }
    public int UsedSelectors { get; }
    public int UnusedSelectors { get; }
    public int AmbiguousSelectors { get; }
    public double UsagePercentage { get; }
    public string UsageText { get; }
    public string UsageTooltip { get; }
    public long FileSizeBytes { get; }
    public string FormattedSize { get; }
    public IReadOnlyList<string> ImportedCssFiles { get; }
    public int ImportedCssFilesCount { get; }
    public string ImportedFilesDisplay { get; }
    public string? ImportedFilesTooltip { get; }
    public string Details { get; }

    public Brush UsageColor
    {
        get
        {
            if (UsagePercentage < 20.0)
            {
                return Application.Current?.TryFindResource("RedBrush") as Brush ?? FallbackRedBrush;
            }

            if (UsagePercentage <= 80.0)
            {
                return Application.Current?.TryFindResource("YellowBrush") as Brush ?? FallbackAmberBrush;
            }

            return Application.Current?.TryFindResource("SuccessBrush") as Brush ?? FallbackGreenBrush;
        }
    }

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(UsageColor));

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

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static string? NormalizeDirectory(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(relativePath.Replace('\\', '/'));
        return string.IsNullOrEmpty(dir) ? string.Empty : dir.Replace('\\', '/');
    }

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
