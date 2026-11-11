using TrimCSS.Models;

namespace TrimCSS.Contracts;

/// <summary>
/// Defines the contract for scanning a project to detect unused and ambiguous CSS selectors.
/// </summary>
public interface ICssScanner
{
    /// <summary>
    /// Scans a target project folder asynchronously to identify unused and ambiguous CSS selectors.
    /// </summary>
    /// <param name="projectFolderPath">The root directory path of the project to scan.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous scan operation, yielding the overall <see cref="ScanResult"/>.</returns>
    Task<ScanResult> ScanProjectAsync(string projectFolderPath, CancellationToken cancellationToken = default);
}
