namespace TrimCSS.Models;

/// <summary>
/// Indicates the reference status of a CSS file within the scanned project.
/// </summary>
public enum CssFileReferenceStatus
{
    /// <summary>
    /// Explicitly linked or imported by at least one markup, script, or parent stylesheet file.
    /// </summary>
    Referenced,

    /// <summary>
    /// Matched via dynamic template literal, partial filename pattern, or ambiguous shared basename.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// No references found anywhere in scanned project files.
    /// </summary>
    Unused
}
