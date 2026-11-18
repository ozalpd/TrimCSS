namespace TrimCSS.Models;

/// <summary>
/// Configuration options to customize directory and file exclusions during a scan.
/// </summary>
public class ScanOptions
{
    /// <summary>
    /// Default list of directory names skipped during project scanning.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExcludedFolders =
    [
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".vs",
        "dist",
        "build"
    ];

    /// <summary>
    /// Default list of file name patterns skipped during project scanning.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExcludedFilePatterns =
    [
        "*.min.css"
    ];

    /// <summary>
    /// Gets or sets the folder names to exclude from directory traversal.
    /// Matched case-insensitively against folder names or relative paths.
    /// </summary>
    public IList<string> ExcludedFolders { get; set; } = new List<string>(DefaultExcludedFolders);

    /// <summary>
    /// Gets or sets wildcard or exact file name patterns to exclude from scanning (e.g., "*.min.css", "bootstrap*.css").
    /// Matched case-insensitively against file names.
    /// </summary>
    public IList<string> ExcludedFilePatterns { get; set; } = new List<string>(DefaultExcludedFilePatterns);
}
