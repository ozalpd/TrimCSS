namespace TrimCSS.Models;

/// <summary>
/// Specifies the type of stylesheet reference discovered during source code scanning.
/// </summary>
public enum StylesheetReferenceKind
{
    /// <summary>
    /// An exact static path or URL reference to a stylesheet.
    /// </summary>
    Exact,

    /// <summary>
    /// A glob pattern (e.g. ASP.NET Core tag helpers asp-href-include="~/css/*.css").
    /// </summary>
    Glob,

    /// <summary>
    /// A dynamic template literal or expression (e.g. `/css/themes/${theme}.css`).
    /// </summary>
    Dynamic
}

/// <summary>
/// Represents a reference to a CSS stylesheet discovered in an HTML, Razor, JavaScript, or CSS file.
/// </summary>
public class StylesheetReference
{
    /// <summary>
    /// Gets or sets the raw reference path or pattern extracted from the source file.
    /// </summary>
    public string ReferencePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file path of the source file where this reference was found.
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of reference (Exact, Glob, or Dynamic).
    /// </summary>
    public StylesheetReferenceKind Kind { get; set; } = StylesheetReferenceKind.Exact;

    /// <summary>
    /// Gets or sets an optional exclusion pattern (e.g. from asp-href-exclude).
    /// </summary>
    public string? ExcludePattern { get; set; }
}
