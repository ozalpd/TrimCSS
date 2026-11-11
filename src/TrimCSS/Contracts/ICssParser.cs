using TrimCSS.Models;

namespace TrimCSS.Contracts;

/// <summary>
/// Defines the contract for parsing CSS stylesheets to extract selector metadata.
/// </summary>
public interface ICssParser
{
    /// <summary>
    /// Parses a CSS stylesheet file asynchronously and extracts metadata for all contained selectors.
    /// </summary>
    /// <param name="filePath">The file path of the CSS file to parse.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous parse operation, yielding a collection of <see cref="CssSelectorInfo"/>.</returns>
    Task<IEnumerable<CssSelectorInfo>> ParseCssFileAsync(string filePath, CancellationToken cancellationToken = default);
}
