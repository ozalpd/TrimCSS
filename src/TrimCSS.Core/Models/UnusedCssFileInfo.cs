namespace TrimCSS.Models;

/// <summary>
/// Represents audit information about an unreferenced or ambiguously referenced CSS file in the project.
/// </summary>
public class UnusedCssFileInfo
{
    /// <summary>
    /// Gets or sets the absolute file path of the stylesheet.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project-relative file path normalized with forward slashes ('/').
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the total number of CSS selectors parsed from this stylesheet.
    /// </summary>
    public int SelectorCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of CSS selectors parsed from this stylesheet.
    /// </summary>
    public int TotalSelectors { get; set; }

    /// <summary>
    /// Gets or sets the number of CSS selectors in this stylesheet that are confirmed used.
    /// </summary>
    public int UsedSelectors { get; set; }

    /// <summary>
    /// Gets or sets the number of CSS selectors in this stylesheet that are confirmed unused.
    /// </summary>
    public int UnusedSelectors { get; set; }

    /// <summary>
    /// Gets or sets the number of CSS selectors in this stylesheet whose usage is ambiguous.
    /// </summary>
    public int AmbiguousSelectors { get; set; }

    /// <summary>
    /// Gets the percentage of selectors in this stylesheet that are used (0 when <see cref="TotalSelectors"/> is 0).
    /// </summary>
    public double UsagePercentage => TotalSelectors == 0 ? 0.0 : (UsedSelectors / (double)TotalSelectors) * 100.0;

    /// <summary>
    /// Gets or sets the reference status of this CSS file (Referenced, Ambiguous, or Unused).
    /// </summary>
    public CssFileReferenceStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the list of project files that ambiguously matched this stylesheet.
    /// Empty when Status is Unused; populated for diagnostics when Ambiguous.
    /// </summary>
    public List<string> MatchedByFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the project-relative paths of other CSS files imported via @import by this stylesheet.
    /// </summary>
    public List<string> ImportedCssFiles { get; set; } = [];
}

/// <summary>
/// Alias for <see cref="UnusedCssFileInfo"/> providing semantic naming for general CSS file audit results.
/// </summary>
public class CssFileAuditInfo : UnusedCssFileInfo
{
}
