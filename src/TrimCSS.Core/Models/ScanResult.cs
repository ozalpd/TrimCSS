namespace TrimCSS.Models;

/// <summary>
/// Represents the result of a project-wide scan for unused and ambiguous CSS selectors.
/// </summary>
public class ScanResult
{
    /// <summary>
    /// Gets or sets the total number of CSS selectors discovered in parsed stylesheets.
    /// </summary>
    public int TotalCssSelectors { get; set; }

    /// <summary>
    /// Gets or sets the list of CSS selectors that were confirmed to have no references in any scanned source files.
    /// </summary>
    public List<CssSelectorInfo> UnusedSelectors { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of CSS selectors whose usage is ambiguous (e.g., dynamic Razor expressions or complex script templates).
    /// </summary>
    public List<CssSelectorInfo> AmbiguousSelectors { get; set; } = [];

    /// <summary>
    /// Gets or sets the total number of source files scanned across the project.
    /// </summary>
    public int ScannedFilesCount { get; set; }

    /// <summary>
    /// Gets or sets the total execution time taken to complete the scan operation.
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }
}
