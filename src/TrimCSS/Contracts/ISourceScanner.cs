using TrimCSS.Models;

namespace TrimCSS.Contracts;

/// <summary>
/// Defines a contract for scanning source files (HTML, Razor, JavaScript, etc.) to extract CSS class and selector candidate tokens.
/// </summary>
public interface ISourceScanner
{
    /// <summary>
    /// Gets the collection of file extensions supported by this scanner (e.g., ".html", ".cshtml"), including leading dots.
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// Scans a source file asynchronously and extracts discovered candidate CSS tokens.
    /// </summary>
    /// <param name="filePath">The path of the source file to scan.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous scan operation, yielding a collection of <see cref="ScannedToken"/> objects.</returns>
    Task<IEnumerable<ScannedToken>> ScanFileAsync(string filePath, CancellationToken cancellationToken = default);
}
