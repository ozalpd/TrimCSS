using System.IO;
using System.Text;
using System.Text.Json;
using TrimCSS.GUI.Models;
using TrimCSS.Models;

namespace TrimCSS.GUI.Services;

public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task ExportCsvAsync(string filePath, IReadOnlyList<SelectorRowItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Status,Selector,TargetClasses,RelativeFilePath,FullFilePath,Line,Column,Selected");

        foreach (SelectorRowItem item in items)
        {
            string classes = item.Info.TargetClasses.Count > 0
                ? string.Join(" ", item.Info.TargetClasses.Select(c => $".{c}"))
                : string.Empty;

            sb.Append(EscapeCsv(item.Status)).Append(',')
              .Append(EscapeCsv(item.SelectorText)).Append(',')
              .Append(EscapeCsv(classes)).Append(',')
              .Append(EscapeCsv(item.RelativeFilePath)).Append(',')
              .Append(EscapeCsv(item.FullFilePath)).Append(',')
              .Append(item.Info.LineNumber).Append(',')
              .Append(item.Info.ColumnNumber).Append(',')
              .Append(item.IsSelected ? "true" : "false")
              .AppendLine();
        }

        // UTF-8 with BOM ensures Excel renders Unicode file paths properly
        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static async Task ExportCssFilesCsvAsync(string filePath, IReadOnlyList<CssFileRowItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Status,RelativePath,FullFilePath,SelectorCount,FileSizeBytes,FormattedSize,Details,Selected");

        foreach (CssFileRowItem item in items)
        {
            sb.Append(EscapeCsv(item.Status)).Append(',')
              .Append(EscapeCsv(item.RelativePath)).Append(',')
              .Append(EscapeCsv(item.FullFilePath)).Append(',')
              .Append(item.SelectorCount).Append(',')
              .Append(item.FileSizeBytes).Append(',')
              .Append(EscapeCsv(item.FormattedSize)).Append(',')
              .Append(EscapeCsv(item.Details)).Append(',')
              .Append(item.IsSelected ? "true" : "false")
              .AppendLine();
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static async Task ExportJsonAsync(
        string filePath,
        string? projectFolder,
        string activeStatusFilter,
        string searchQuery,
        IReadOnlyList<SelectorRowItem>? selectorItems = null,
        IReadOnlyList<CssFileRowItem>? cssFileItems = null,
        ScanResult? scanResult = null,
        string? activeView = null)
    {
        var report = new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ProjectFolder = projectFolder,
            ActiveView = activeView ?? (cssFileItems != null && (selectorItems == null || selectorItems.Count == 0) ? "CSS Files" : "Selectors"),
            Filters = new
            {
                Status = activeStatusFilter,
                SearchQuery = searchQuery
            },
            Summary = new
            {
                TotalSelectors = scanResult?.TotalCssSelectors ?? selectorItems?.Count ?? 0,
                TotalCssFiles = scanResult?.TotalCssFiles ?? cssFileItems?.Count ?? 0,
                UnusedCssFilesCount = scanResult?.UnusedCssFiles.Count ?? cssFileItems?.Count(f => f.IsUnused) ?? 0,
                AmbiguousCssFilesCount = scanResult?.AmbiguousCssFiles.Count ?? cssFileItems?.Count(f => !f.IsUnused) ?? 0,
                ScannedFilesCount = scanResult?.ScannedFilesCount ?? 0,
                ExecutionTimeMs = scanResult?.ExecutionTime.TotalMilliseconds ?? 0
            },
            TotalSelectorsExported = selectorItems?.Count ?? 0,
            TotalCssFilesExported = cssFileItems?.Count ?? 0,
            Selectors = (selectorItems ?? []).Select(item => new
            {
                item.Status,
                Selector = item.SelectorText,
                item.Info.TargetClasses,
                item.RelativeFilePath,
                item.FullFilePath,
                item.Info.LineNumber,
                item.Info.ColumnNumber,
                item.IsSelected
            }),
            CssFiles = (cssFileItems ?? []).Select(item => new
            {
                item.Status,
                item.RelativePath,
                item.FullFilePath,
                item.SelectorCount,
                item.FileSizeBytes,
                item.FormattedSize,
                item.Details,
                MatchedByFiles = item.Info.MatchedByFiles,
                item.IsSelected
            })
        };

        string json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
