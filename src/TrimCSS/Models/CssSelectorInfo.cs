namespace TrimCSS.Models;

/// <summary>
/// Represents information about a CSS selector parsed from a stylesheet.
/// </summary>
public class CssSelectorInfo
{
    /// <summary>
    /// Gets or sets the raw CSS selector text (e.g., ".btn-primary", "#main-nav .menu-item").
    /// </summary>
    public string SelectorText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection of CSS class names targeted by this selector.
    /// </summary>
    public List<string> TargetClasses { get; set; } = [];

    /// <summary>
    /// Gets or sets the path of the stylesheet containing the selector.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the 1-based line number where the selector begins in the source stylesheet.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Gets or sets the 1-based column number where the selector begins in the source stylesheet.
    /// </summary>
    public int ColumnNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this selector contains or targets an ID selector.
    /// </summary>
    public bool IsIdSelector { get; set; }
}
