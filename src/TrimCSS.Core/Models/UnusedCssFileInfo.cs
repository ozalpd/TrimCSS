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
    /// Gets or sets the reference status of this CSS file (Unused or Ambiguous).
    /// </summary>
    public CssFileReferenceStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the list of project files that ambiguously matched this stylesheet.
    /// Empty when Status is Unused; populated for diagnostics when Ambiguous.
    /// </summary>
    public List<string> MatchedByFiles { get; set; } = [];
}

/// <summary>
/// Alias for <see cref="UnusedCssFileInfo"/> providing semantic naming for general CSS file audit results.
/// </summary>
public class CssFileAuditInfo : UnusedCssFileInfo
{
}
