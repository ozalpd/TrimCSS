using System.Diagnostics;
using TrimCSS.Contracts;
using TrimCSS.Models;
using TrimCSS.Parsers;

namespace TrimCSS.Engine;

/// <summary>
/// Orchestrates the project-wide scanning process by coordinating CSS parsing,
/// source code scanning across HTML/Razor/JavaScript, and selector usage classification.
/// </summary>
public class ProjectScanner : ICssScanner
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".vs",
        "dist",
        "build"
    };

    private readonly ICssParser _cssParser;
    private readonly IReadOnlyList<ISourceScanner> _sourceScanners;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectScanner"/> class using default parser and scanner implementations.
    /// </summary>
    public ProjectScanner()
        : this(new CssParser(), [new HtmlRazorScanner(), new JsScanner()])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectScanner"/> class with custom dependencies.
    /// </summary>
    /// <param name="cssParser">The CSS parser to parse stylesheet files.</param>
    /// <param name="sourceScanners">The source code scanners to extract tokens from markup and scripts.</param>
    public ProjectScanner(ICssParser cssParser, IEnumerable<ISourceScanner> sourceScanners)
    {
        _cssParser = cssParser ?? throw new ArgumentNullException(nameof(cssParser));
        ArgumentNullException.ThrowIfNull(sourceScanners);
        _sourceScanners = sourceScanners.ToList();
    }

    /// <inheritdoc/>
    public async Task<ScanResult> ScanProjectAsync(string projectFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);

        if (!Directory.Exists(projectFolderPath))
        {
            throw new DirectoryNotFoundException($"The project directory '{projectFolderPath}' was not found.");
        }

        var stopwatch = Stopwatch.StartNew();

        // 1. Discover all non-ignored project files
        var allFiles = DiscoverProjectFiles(projectFolderPath);
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Separate CSS files from source markup/script files
        var cssFiles = allFiles
            .Where(f => f.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sourceFiles = allFiles
            .Where(f => !f.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 3. Parse all CSS stylesheets
        var allSelectors = new List<CssSelectorInfo>();
        foreach (var cssFile in cssFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectors = await _cssParser.ParseCssFileAsync(cssFile, cancellationToken);
            allSelectors.AddRange(selectors);
        }

        // 4. Scan all source files for CSS token candidates
        var exactTokens = new HashSet<string>(StringComparer.Ordinal);
        var dynamicPatterns = new HashSet<string>(StringComparer.Ordinal);
        int scannedSourceFileCount = 0;

        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(sourceFile);

            var matchingScanners = _sourceScanners
                .Where(s => s.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (matchingScanners.Count == 0)
            {
                continue;
            }

            scannedSourceFileCount++;

            foreach (var scanner in matchingScanners)
            {
                var tokens = await scanner.ScanFileAsync(sourceFile, cancellationToken);
                foreach (var token in tokens)
                {
                    if (string.IsNullOrWhiteSpace(token.Value))
                    {
                        continue;
                    }

                    if (token.TokenType is TokenType.HtmlClassAttribute or TokenType.RazorExpression or TokenType.JsStringLiteral)
                    {
                        exactTokens.Add(token.Value);
                    }
                    else if (token.TokenType is TokenType.JsTemplateLiteral)
                    {
                        dynamicPatterns.Add(token.Value);
                    }
                }
            }
        }

        // 5. Match and classify selectors
        var unusedSelectors = new List<CssSelectorInfo>();
        var ambiguousSelectors = new List<CssSelectorInfo>();

        foreach (var selector in allSelectors)
        {
            // Skip selectors without class targets (e.g. pure ID or tag selectors like `h1`, `#header`)
            if (selector.TargetClasses.Count == 0)
            {
                continue;
            }

            bool allExactMatches = selector.TargetClasses.All(exactTokens.Contains);

            if (allExactMatches)
            {
                // Definitively used: all classes exist in the exact token set
                continue;
            }

            bool hasAnyMatch = selector.TargetClasses.Any(cls =>
                exactTokens.Contains(cls) ||
                dynamicPatterns.Any(pattern => MatchesDynamicPattern(cls, pattern)));

            if (!hasAnyMatch)
            {
                // Definitively unused: none of the target classes match any exact token or dynamic pattern
                unusedSelectors.Add(selector);
            }
            else
            {
                // Ambiguous: some classes match exact or dynamic pattern, but not all are exact matches
                ambiguousSelectors.Add(selector);
            }
        }

        stopwatch.Stop();

        return new ScanResult
        {
            TotalCssSelectors = allSelectors.Count,
            UnusedSelectors = unusedSelectors,
            AmbiguousSelectors = ambiguousSelectors,
            ScannedFilesCount = cssFiles.Count + scannedSourceFileCount,
            ExecutionTime = stopwatch.Elapsed
        };
    }

    private static List<string> DiscoverProjectFiles(string rootPath)
    {
        var result = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        while (queue.Count > 0)
        {
            string currentDir = queue.Dequeue();

            try
            {
                // Enqueue subdirectories that are not in the ignore list
                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (!IgnoredDirectoryNames.Contains(dirName))
                    {
                        queue.Enqueue(subDir);
                    }
                }

                // Add files in current directory
                foreach (var file in Directory.GetFiles(currentDir))
                {
                    result.Add(file);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories that cannot be accessed
            }
        }

        return result;
    }

    private static bool MatchesDynamicPattern(string targetClass, string pattern)
    {
        if (string.IsNullOrWhiteSpace(targetClass) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        // Pattern ending with '-' or '_' indicates a prefix (e.g. "btn-", "alert-", "theme_")
        if (pattern.EndsWith('-') || pattern.EndsWith('_'))
        {
            return targetClass.StartsWith(pattern, StringComparison.Ordinal);
        }

        // Pattern starting with '-' or '_' indicates a suffix (e.g. "-lg", "-active")
        if (pattern.StartsWith('-') || pattern.StartsWith('_'))
        {
            return targetClass.EndsWith(pattern, StringComparison.Ordinal);
        }

        // General prefix or suffix match
        return targetClass.StartsWith(pattern, StringComparison.Ordinal) ||
               targetClass.EndsWith(pattern, StringComparison.Ordinal);
    }
}
