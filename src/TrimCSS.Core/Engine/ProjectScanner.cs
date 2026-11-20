using System.Diagnostics;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
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
    public Task<ScanResult> ScanProjectAsync(string projectFolderPath, CancellationToken cancellationToken)
    {
        return ScanProjectAsync(projectFolderPath, options: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ScanResult> ScanProjectAsync(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"The project directory '{rootPath}' was not found.");
        }

        var stopwatch = Stopwatch.StartNew();
        options ??= new ScanOptions();

        IEnumerable<string> excludedFolders = options.ExcludedFolders ?? (IEnumerable<string>)ScanOptions.DefaultExcludedFolders;
        IEnumerable<string> excludedFilePatterns = options.ExcludedFilePatterns ?? (IEnumerable<string>)ScanOptions.DefaultExcludedFilePatterns;

        // 1. Discover all non-ignored project files
        var allFiles = DiscoverProjectFiles(rootPath, excludedFolders, excludedFilePatterns);
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Separate CSS files from source markup/script files
        var cssFiles = allFiles
            .Where(f => f.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sourceFiles = allFiles
            .Where(f => !f.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 3. Parse all CSS stylesheets and extract @import references
        var allSelectors = new List<CssSelectorInfo>();
        var cssImports = new Dictionary<string, List<StylesheetReference>>(StringComparer.OrdinalIgnoreCase);

        foreach (var cssFile in cssFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectors = await _cssParser.ParseCssFileAsync(cssFile, cancellationToken);
            allSelectors.AddRange(selectors);

            var imports = await _cssParser.ExtractImportReferencesAsync(cssFile, cancellationToken);
            cssImports[cssFile] = imports.ToList();
        }

        // 4. Scan all source files for CSS token candidates and stylesheet references
        var exactTokens = new HashSet<string>(StringComparer.Ordinal);
        var dynamicPatterns = new HashSet<string>(StringComparer.Ordinal);
        var allStylesheetReferences = new List<StylesheetReference>();
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

                if (scanner is HtmlRazorScanner htmlScanner)
                {
                    var refs = await htmlScanner.ExtractStylesheetReferencesFileAsync(sourceFile, cancellationToken);
                    allStylesheetReferences.AddRange(refs);
                }
                else if (scanner is JsScanner jsScanner)
                {
                    var refs = await jsScanner.ExtractStylesheetReferencesFileAsync(sourceFile, cancellationToken);
                    allStylesheetReferences.AddRange(refs);
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

        // 6. Match and classify CSS files
        var (unusedCssFiles, ambiguousCssFiles) = ClassifyCssFiles(
            rootPath,
            cssFiles,
            allSelectors,
            allStylesheetReferences,
            cssImports);

        stopwatch.Stop();

        return new ScanResult
        {
            TotalCssSelectors = allSelectors.Count,
            UnusedSelectors = unusedSelectors,
            AmbiguousSelectors = ambiguousSelectors,
            TotalCssFiles = cssFiles.Count,
            UnusedCssFiles = unusedCssFiles,
            AmbiguousCssFiles = ambiguousCssFiles,
            ScannedFilesCount = cssFiles.Count + scannedSourceFileCount,
            ExecutionTime = stopwatch.Elapsed
        };
    }

    private static List<string> DiscoverProjectFiles(
        string rootPath,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns)
    {
        var result = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        var excludedFolderList = excludedFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToList();

        var patternList = excludedFilePatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        while (queue.Count > 0)
        {
            string currentDir = queue.Dequeue();

            try
            {
                // Enqueue subdirectories that are not in the ignore list
                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    string dirName = Path.GetFileName(subDir);
                    string relativeDir = Path.GetRelativePath(rootPath, subDir).Replace('\\', '/');

                    bool isExcluded = excludedFolderList.Any(f =>
                    {
                        string trimmed = f.TrimEnd('/', '\\');
                        return string.Equals(trimmed, dirName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(trimmed.Replace('\\', '/'), relativeDir, StringComparison.OrdinalIgnoreCase);
                    });

                    if (!isExcluded)
                    {
                        queue.Enqueue(subDir);
                    }
                }

                // Add files in current directory that do not match exclusion patterns
                foreach (var file in Directory.GetFiles(currentDir))
                {
                    if (!MatchesFilePattern(file, rootPath, patternList))
                    {
                        result.Add(file);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories that cannot be accessed
            }
        }

        return result;
    }

    private static bool MatchesFilePattern(string filePath, string rootPath, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return false;
        }

        string fileName = Path.GetFileName(filePath);
        string relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');

        foreach (var pattern in patterns)
        {
            if (pattern.Contains('/') || pattern.Contains('\\'))
            {
                string normalizedPattern = pattern.Replace('\\', '/');
                if (FileSystemName.MatchesSimpleExpression(normalizedPattern, relativePath, ignoreCase: true))
                {
                    return true;
                }
            }
            else
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                {
                    return true;
                }
            }
        }

        return false;
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

    private static (List<UnusedCssFileInfo> Unused, List<UnusedCssFileInfo> Ambiguous) ClassifyCssFiles(
        string rootPath,
        IReadOnlyList<string> cssFiles,
        IReadOnlyList<CssSelectorInfo> allSelectors,
        IReadOnlyList<StylesheetReference> references,
        Dictionary<string, List<StylesheetReference>> cssImports)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var cssFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cssFilesByBasename = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in cssFiles)
        {
            string full = Path.GetFullPath(file);
            cssFileMap[full] = file;

            string fileName = Path.GetFileName(file);
            if (!cssFilesByBasename.TryGetValue(fileName, out var list))
            {
                list = [];
                cssFilesByBasename[fileName] = list;
            }
            list.Add(full);
        }

        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void MarkAmbiguous(string fullCssPath, string sourceFile)
        {
            if (!ambiguousMap.TryGetValue(fullCssPath, out var sources))
            {
                sources = [];
                ambiguousMap[fullCssPath] = sources;
            }
            if (!string.IsNullOrWhiteSpace(sourceFile) && !sources.Contains(sourceFile))
            {
                sources.Add(sourceFile);
            }
        }

        // 1. Process source file references
        foreach (var refItem in references)
        {
            if (refItem.Kind == StylesheetReferenceKind.Glob)
            {
                ResolveGlobReference(refItem, normalizedRoot, cssFiles, referencedFiles);
            }
            else if (refItem.Kind == StylesheetReferenceKind.Dynamic)
            {
                ResolveDynamicReference(refItem, normalizedRoot, cssFiles, MarkAmbiguous);
            }
            else // Exact
            {
                ResolveExactReference(refItem, normalizedRoot, cssFileMap, cssFilesByBasename, referencedFiles, MarkAmbiguous);
            }
        }

        // 2. Build transitive import graph
        var importGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (importerFile, importRefs) in cssImports)
        {
            string importerFull = Path.GetFullPath(importerFile);
            var targets = new List<string>();

            foreach (var importRef in importRefs)
            {
                string? target = ResolveCssTarget(importRef.ReferencePath, importerFile, normalizedRoot, cssFileMap, cssFilesByBasename);
                if (target != null && !targets.Contains(target))
                {
                    targets.Add(target);
                }
            }

            importGraph[importerFull] = targets;
        }

        // 3. Transitive propagation of Referenced status
        var queue = new Queue<string>(referencedFiles);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (importGraph.TryGetValue(current, out var targets))
            {
                foreach (string target in targets)
                {
                    if (referencedFiles.Add(target))
                    {
                        queue.Enqueue(target);
                    }
                }
            }
        }

        // 4. Transitive propagation of Ambiguous status (only for files not definitively referenced)
        var ambQueue = new Queue<string>(ambiguousMap.Keys.Where(f => !referencedFiles.Contains(f)));
        while (ambQueue.Count > 0)
        {
            string current = ambQueue.Dequeue();
            if (referencedFiles.Contains(current)) continue;

            if (importGraph.TryGetValue(current, out var targets))
            {
                foreach (string target in targets)
                {
                    if (!referencedFiles.Contains(target))
                    {
                        if (!ambiguousMap.ContainsKey(target))
                        {
                            ambiguousMap[target] = [current];
                            ambQueue.Enqueue(target);
                        }
                    }
                }
            }
        }

        var unusedList = new List<UnusedCssFileInfo>();
        var ambiguousList = new List<UnusedCssFileInfo>();

        foreach (var file in cssFiles)
        {
            string full = Path.GetFullPath(file);
            if (referencedFiles.Contains(full))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
            long fileSize = 0;
            try
            {
                fileSize = new FileInfo(file).Length;
            }
            catch
            {
            }

            int selectorCount = allSelectors.Count(s => string.Equals(Path.GetFullPath(s.FilePath), full, StringComparison.OrdinalIgnoreCase));

            if (ambiguousMap.TryGetValue(full, out var matchedBy) && matchedBy.Count > 0)
            {
                ambiguousList.Add(new UnusedCssFileInfo
                {
                    FilePath = file,
                    RelativePath = relativePath,
                    FileSizeBytes = fileSize,
                    SelectorCount = selectorCount,
                    Status = CssFileReferenceStatus.Ambiguous,
                    MatchedByFiles = matchedBy.Distinct().ToList()
                });
            }
            else
            {
                unusedList.Add(new UnusedCssFileInfo
                {
                    FilePath = file,
                    RelativePath = relativePath,
                    FileSizeBytes = fileSize,
                    SelectorCount = selectorCount,
                    Status = CssFileReferenceStatus.Unused,
                    MatchedByFiles = []
                });
            }
        }

        unusedList.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        ambiguousList.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

        return (unusedList, ambiguousList);
    }

    private static void ResolveGlobReference(
        StylesheetReference refItem,
        string rootPath,
        IReadOnlyList<string> cssFiles,
        HashSet<string> referencedFiles)
    {
        string rawPattern = refItem.ReferencePath.TrimStart('~').TrimStart('/').Replace('\\', '/');
        string? excludePattern = refItem.ExcludePattern?.TrimStart('~').TrimStart('/').Replace('\\', '/');

        foreach (var cssPath in cssFiles)
        {
            string relRoot = Path.GetRelativePath(rootPath, cssPath).Replace('\\', '/');
            string? relWwwroot = relRoot.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase) ? relRoot.Substring(8) : null;
            string? relPublic = relRoot.StartsWith("public/", StringComparison.OrdinalIgnoreCase) ? relRoot.Substring(7) : null;
            string fileName = Path.GetFileName(cssPath);

            bool matches = MatchesWildcard(rawPattern, relRoot) ||
                           (relWwwroot != null && MatchesWildcard(rawPattern, relWwwroot)) ||
                           (relPublic != null && MatchesWildcard(rawPattern, relPublic)) ||
                           MatchesWildcard(rawPattern, fileName);

            if (matches)
            {
                bool isExcluded = !string.IsNullOrWhiteSpace(excludePattern) &&
                                  (MatchesWildcard(excludePattern, relRoot) ||
                                   (relWwwroot != null && MatchesWildcard(excludePattern, relWwwroot)) ||
                                   (relPublic != null && MatchesWildcard(excludePattern, relPublic)) ||
                                   MatchesWildcard(excludePattern, fileName));

                if (!isExcluded)
                {
                    referencedFiles.Add(Path.GetFullPath(cssPath));
                }
            }
        }
    }

    private static void ResolveDynamicReference(
        StylesheetReference refItem,
        string rootPath,
        IReadOnlyList<string> cssFiles,
        Action<string, string> markAmbiguous)
    {
        string pattern = refItem.ReferencePath.Trim().Replace('\\', '/');
        string[] parts = Regex.Split(pattern, @"\$\{[^}]*\}");
        string prefix = parts.Length > 0 ? parts[0].TrimStart('~').TrimStart('/') : "";
        string suffix = parts.Length > 1 ? parts[^1] : ".css";

        foreach (var cssPath in cssFiles)
        {
            string relRoot = Path.GetRelativePath(rootPath, cssPath).Replace('\\', '/');
            string? relWwwroot = relRoot.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase) ? relRoot.Substring(8) : null;
            string? relPublic = relRoot.StartsWith("public/", StringComparison.OrdinalIgnoreCase) ? relRoot.Substring(7) : null;
            string fileName = Path.GetFileName(cssPath);

            bool matches = MatchesDynamicPath(relRoot, prefix, suffix) ||
                           (relWwwroot != null && MatchesDynamicPath(relWwwroot, prefix, suffix)) ||
                           (relPublic != null && MatchesDynamicPath(relPublic, prefix, suffix)) ||
                           MatchesDynamicPath(fileName, prefix, suffix);

            if (matches)
            {
                markAmbiguous(Path.GetFullPath(cssPath), refItem.SourceFile);
            }
        }
    }

    private static bool MatchesDynamicPath(string path, string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(path)) return false;
        bool hasPrefix = string.IsNullOrEmpty(prefix) || path.Contains(prefix, StringComparison.OrdinalIgnoreCase);
        bool hasSuffix = string.IsNullOrEmpty(suffix) || path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        return hasPrefix && hasSuffix;
    }

    private static void ResolveExactReference(
        StylesheetReference refItem,
        string rootPath,
        Dictionary<string, string> cssFileMap,
        Dictionary<string, List<string>> cssFilesByBasename,
        HashSet<string> referencedFiles,
        Action<string, string> markAmbiguous)
    {
        string clean = StripQueryAndFragment(refItem.ReferencePath).Trim('"', '\'').Replace('\\', '/');
        var candidates = GenerateCandidatePaths(clean, refItem.SourceFile, rootPath);

        string? matched = null;
        foreach (var candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (cssFileMap.ContainsKey(full))
            {
                matched = full;
                break;
            }
        }

        if (matched != null)
        {
            referencedFiles.Add(matched);
        }
        else
        {
            string fileName = Path.GetFileName(clean);
            if (!string.IsNullOrEmpty(fileName) && cssFilesByBasename.TryGetValue(fileName, out var list))
            {
                if (list.Count == 1)
                {
                    referencedFiles.Add(list[0]);
                }
                else if (list.Count > 1)
                {
                    foreach (var candidateFile in list)
                    {
                        markAmbiguous(candidateFile, refItem.SourceFile);
                    }
                }
            }
        }
    }

    private static string? ResolveCssTarget(
        string rawRef,
        string importerFile,
        string rootPath,
        Dictionary<string, string> cssFileMap,
        Dictionary<string, List<string>> cssFilesByBasename)
    {
        string clean = StripQueryAndFragment(rawRef).Trim('"', '\'').Replace('\\', '/');
        var candidates = GenerateCandidatePaths(clean, importerFile, rootPath);

        foreach (var candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (cssFileMap.ContainsKey(full))
            {
                return full;
            }
        }

        string fileName = Path.GetFileName(clean);
        if (!string.IsNullOrEmpty(fileName) && cssFilesByBasename.TryGetValue(fileName, out var list) && list.Count == 1)
        {
            return list[0];
        }

        return null;
    }

    private static List<string> GenerateCandidatePaths(string cleanPath, string sourceFile, string rootPath)
    {
        var candidates = new List<string>();
        if (cleanPath.StartsWith("~/"))
        {
            string sub = cleanPath.Substring(2).TrimStart('/');
            candidates.Add(Path.Combine(rootPath, sub));
            candidates.Add(Path.Combine(rootPath, "wwwroot", sub));
            candidates.Add(Path.Combine(rootPath, "public", sub));
        }
        else if (cleanPath.StartsWith("/"))
        {
            string sub = cleanPath.Substring(1);
            candidates.Add(Path.Combine(rootPath, "wwwroot", sub));
            candidates.Add(Path.Combine(rootPath, "public", sub));
            candidates.Add(Path.Combine(rootPath, sub));
        }
        else
        {
            string sourceDir = !string.IsNullOrEmpty(sourceFile) ? Path.GetDirectoryName(sourceFile) ?? rootPath : rootPath;
            candidates.Add(Path.Combine(sourceDir, cleanPath));
            candidates.Add(Path.Combine(rootPath, cleanPath));
            candidates.Add(Path.Combine(rootPath, "wwwroot", cleanPath));
            candidates.Add(Path.Combine(rootPath, "public", cleanPath));
        }
        return candidates;
    }

    private static bool MatchesWildcard(string pattern, string path)
    {
        if (string.Equals(pattern, path, StringComparison.OrdinalIgnoreCase)) return true;
        if (FileSystemName.MatchesSimpleExpression(pattern, path, ignoreCase: true)) return true;

        try
        {
            string regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*\*", ".*").Replace(@"\*", @"[^/]*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string StripQueryAndFragment(string path)
    {
        int qIdx = path.IndexOfAny(['?', '#']);
        return qIdx >= 0 ? path.Substring(0, qIdx).Trim() : path.Trim();
    }
}
