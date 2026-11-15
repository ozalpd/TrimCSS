using System.IO;
using System.Text;
using System.Text.Json;
using TrimCSS.GUI.Models;

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

    public static async Task ExportJsonAsync(
        string filePath,
        string? projectFolder,
        string activeStatusFilter,
        string searchQuery,
        IReadOnlyList<SelectorRowItem> items)
    {
        var report = new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ProjectFolder = projectFolder,
            Filters = new
            {
                Status = activeStatusFilter,
                SearchQuery = searchQuery
            },
            TotalExported = items.Count,
            Selectors = items.Select(item => new
            {
                item.Status,
                Selector = item.SelectorText,
                item.Info.TargetClasses,
                item.RelativeFilePath,
                item.FullFilePath,
                item.Info.LineNumber,
                item.Info.ColumnNumber,
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