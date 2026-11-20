using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using TrimCSS.Contracts;
using TrimCSS.Models;

namespace TrimCSS.Parsers;

/// <summary>
/// Scans HTML (.html, .htm) and Razor (.cshtml) source files to extract candidate CSS class tokens.
/// </summary>
public partial class HtmlRazorScanner : ISourceScanner
{
    private static readonly string[] DefaultSupportedExtensions = [".html", ".htm", ".cshtml"];
    private readonly HtmlParser _htmlParser = new();

    /// <inheritdoc/>
    public IEnumerable<string> SupportedExtensions => DefaultSupportedExtensions;

    /// <inheritdoc/>
    public async Task<IEnumerable<ScannedToken>> ScanFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The source file '{filePath}' was not found.", filePath);
        }

        string content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ScanContent(content, filePath);
    }

    /// <summary>
    /// Scans HTML or Razor source content and extracts all discovered CSS class tokens.
    /// </summary>
    /// <param name="content">The text content of the HTML or Razor document.</param>
    /// <param name="filePath">The path of the source file, used to determine format (.cshtml vs .html) and track token source.</param>
    /// <returns>A collection of distinct <see cref="ScannedToken"/> instances discovered in the content.</returns>
    public IEnumerable<ScannedToken> ScanContent(string content, string filePath = "")
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var results = new List<ScannedToken>();
        var seen = new HashSet<(string Value, TokenType Type)>(new TokenEqualityComparer());

        void AddToken(string value, TokenType type)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (seen.Add((trimmed, type)))
            {
                results.Add(new ScannedToken
                {
                    Value = trimmed,
                    TokenType = type,
                    SourceFile = filePath
                });
            }
        }

        bool isRazor = string.IsNullOrEmpty(filePath) ||
                       filePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

        // 1. Static HTML Pass
        // In Razor documents, sanitize dynamic Razor expressions so AngleSharp parses clean HTML attributes
        string htmlToParse = isRazor ? SanitizeRazorExpressions(content) : content;

        try
        {
            var document = _htmlParser.ParseDocument(htmlToParse);
            var elements = document.QuerySelectorAll("[class]");

            foreach (var element in elements)
            {
                string? classAttr = element.GetAttribute("class");
                if (string.IsNullOrWhiteSpace(classAttr))
                {
                    continue;
                }

                var classNames = classAttr.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var className in classNames)
                {
                    if (IsValidClassToken(className))
                    {
                        AddToken(className, TokenType.HtmlClassAttribute);
                    }
                }
            }
        }
        catch
        {
            // If AngleSharp fails on severely malformed input, fall back to regex extraction
            ExtractFallbackHtmlClasses(htmlToParse, AddToken);
        }

        // 2. Razor Expression Pass (for .cshtml files)
        if (isRazor)
        {
            ExtractRazorStringLiterals(content, AddToken);
        }

        return results;
    }

    private static string SanitizeRazorExpressions(string content)
    {
        // Replace @( ... ) expressions with spaces
        string sanitized = RazorParenthesizedRegex().Replace(content, " ");

        // Replace @{ ... } or @code { ... } blocks with spaces
        sanitized = RazorBlockRegex().Replace(sanitized, " ");

        return sanitized;
    }

    private static void ExtractRazorStringLiterals(string content, Action<string, TokenType> addToken)
    {
        void ExtractStringsFrom(string snippet)
        {
            foreach (Match match in CSharpStringLiteralRegex().Matches(snippet))
            {
                string raw = match.Value;
                string value;

                if (raw.StartsWith("@\""))
                {
                    value = raw.Substring(2, raw.Length - 3).Replace("\"\"", "\"");
                }
                else if (raw.StartsWith("\"") && raw.EndsWith("\""))
                {
                    value = Regex.Unescape(raw.Substring(1, raw.Length - 2));
                }
                else
                {
                    value = raw;
                }

                var tokens = value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    if (IsValidClassToken(token))
                    {
                        addToken(token, TokenType.RazorExpression);
                    }
                }
            }
        }

        // 1. Inline @( ... ) expressions (e.g., class="@(isActive ? "btn-active" : "btn-disabled")")
        foreach (Match match in RazorParenthesizedRegex().Matches(content))
        {
            ExtractStringsFrom(match.Value);
        }

        // 2. Razor code blocks: @{ ... }, @code { ... }, @functions { ... }
        foreach (Match match in RazorBlockRegex().Matches(content))
        {
            ExtractStringsFrom(match.Value);
        }

        // 3. Control flow expressions: @if (...), @else if (...)
        foreach (Match match in RazorControlFlowRegex().Matches(content))
        {
            ExtractStringsFrom(match.Value);
        }
    }

    private static bool IsValidClassToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        // Reject lingering Razor markers or syntax quotes
        if (token.StartsWith('@') ||
            token.Contains('?') ||
            token.Contains('"') ||
            token.Contains('\'') ||
            token.Contains('<') ||
            token.Contains('>'))
        {
            return false;
        }

        return true;
    }

    private static void ExtractFallbackHtmlClasses(string content, Action<string, TokenType> addToken)
    {
        var regex = FallbackClassAttributeRegex();
        foreach (Match match in regex.Matches(content))
        {
            string classValue = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var tokens = classValue.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (IsValidClassToken(token))
                {
                    addToken(token, TokenType.HtmlClassAttribute);
                }
            }
        }
    }

    private sealed class TokenEqualityComparer : IEqualityComparer<(string Value, TokenType Type)>
    {
        public bool Equals((string Value, TokenType Type) x, (string Value, TokenType Type) y)
        {
            return string.Equals(x.Value, y.Value, StringComparison.Ordinal) && x.Type == y.Type;
        }

        public int GetHashCode((string Value, TokenType Type) obj)
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Value), obj.Type);
        }
    }

    [GeneratedRegex(@"@\((?>[^()]+|\((?<DEPTH>)|\)(?<-DEPTH>))*(?(DEPTH)(?!))\)", RegexOptions.Compiled)]
    private static partial Regex RazorParenthesizedRegex();

    [GeneratedRegex(@"@(?:code|functions)?\s*\{(?>[^{}]+|\{(?<DEPTH>)|\}(?<-DEPTH>))*(?(DEPTH)(?!))\}", RegexOptions.Compiled)]
    private static partial Regex RazorBlockRegex();

    [GeneratedRegex(@"@(?:if|else\s+if)\s*\((?>[^()]+|\((?<DEPTH>)|\)(?<-DEPTH>))*(?(DEPTH)(?!))\)", RegexOptions.Compiled)]
    private static partial Regex RazorControlFlowRegex();

    [GeneratedRegex(@"(?:@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*"")", RegexOptions.Compiled)]
    private static partial Regex CSharpStringLiteralRegex();

    [GeneratedRegex(@"class\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FallbackClassAttributeRegex();

    /// <summary>
    /// Scans an HTML or Razor file asynchronously and extracts discovered stylesheet references.
    /// </summary>
    public async Task<IEnumerable<StylesheetReference>> ExtractStylesheetReferencesFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The source file '{filePath}' was not found.", filePath);
        }

        string content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ExtractStylesheetReferences(content, filePath);
    }

    /// <summary>
    /// Scans HTML or Razor source content and extracts all discovered stylesheet references.
    /// </summary>
    /// <param name="content">The text content of the HTML or Razor document.</param>
    /// <param name="filePath">The path of the source file.</param>
    /// <returns>A collection of discovered <see cref="StylesheetReference"/> instances.</returns>
    public IEnumerable<StylesheetReference> ExtractStylesheetReferences(string content, string filePath = "")
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var results = new List<StylesheetReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRef(string refPath, StylesheetReferenceKind kind, string? exclude = null)
        {
            if (string.IsNullOrWhiteSpace(refPath)) return;
            string trimmed = refPath.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(trimmed)) return;
            if (seen.Add($"{kind}:{trimmed}:{exclude}"))
            {
                results.Add(new StylesheetReference
                {
                    ReferencePath = trimmed,
                    SourceFile = filePath,
                    Kind = kind,
                    ExcludePattern = exclude
                });
            }
        }

        bool isRazor = string.IsNullOrEmpty(filePath) ||
                       filePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

        // 1. Static HTML Pass using AngleSharp
        string htmlToParse = isRazor ? SanitizeRazorExpressions(content) : content;
        try
        {
            var document = _htmlParser.ParseDocument(htmlToParse);
            var linkElements = document.QuerySelectorAll("link");
            foreach (var link in linkElements)
            {
                string? href = link.GetAttribute("href");
                string? rel = link.GetAttribute("rel");
                string? aspInclude = link.GetAttribute("asp-href-include");
                string? aspExclude = link.GetAttribute("asp-href-exclude");

                if (!string.IsNullOrWhiteSpace(aspInclude))
                {
                    var patterns = aspInclude.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var pattern in patterns)
                    {
                        AddRef(pattern, StylesheetReferenceKind.Glob, aspExclude);
                    }
                }

                if (!string.IsNullOrWhiteSpace(href))
                {
                    string clean = StripQueryAndFragment(href);
                    bool isStylesheet = (rel != null && rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase)) ||
                                        clean.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
                    if (isStylesheet)
                    {
                        AddRef(href, StylesheetReferenceKind.Exact);
                    }
                }
            }
        }
        catch
        {
            // Fallback handled in regex pass below
        }

        // 2. Regex Pass on raw content (catches Razor-embedded links, Tag Helpers, @Styles.Render, Url.Content, and C# strings)
        foreach (Match match in LinkTagRegex().Matches(content))
        {
            string linkTag = match.Value;

            // asp-href-include and asp-href-exclude
            var includeMatch = AspHrefIncludeRegex().Match(linkTag);
            if (includeMatch.Success)
            {
                string includeValue = includeMatch.Groups[1].Success ? includeMatch.Groups[1].Value : includeMatch.Groups[2].Value;
                var excludeMatch = AspHrefExcludeRegex().Match(linkTag);
                string? excludeValue = excludeMatch.Success ? (excludeMatch.Groups[1].Success ? excludeMatch.Groups[1].Value : excludeMatch.Groups[2].Value) : null;

                var patterns = includeValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var pattern in patterns)
                {
                    AddRef(pattern, StylesheetReferenceKind.Glob, excludeValue);
                }
            }

            // href in link tag
            var hrefMatch = HrefAttributeRegex().Match(linkTag);
            if (hrefMatch.Success)
            {
                string hrefRaw = hrefMatch.Groups[1].Success ? hrefMatch.Groups[1].Value : hrefMatch.Groups[2].Value;
                var urlContentMatch = UrlContentCallRegex().Match(hrefRaw);
                if (urlContentMatch.Success)
                {
                    string innerUrl = urlContentMatch.Groups[1].Value;
                    AddRef(innerUrl, StylesheetReferenceKind.Exact);
                }
                else
                {
                    string clean = StripQueryAndFragment(hrefRaw);
                    bool isStylesheet = linkTag.Contains("stylesheet", StringComparison.OrdinalIgnoreCase) ||
                                        clean.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
                    if (isStylesheet)
                    {
                        AddRef(hrefRaw, StylesheetReferenceKind.Exact);
                    }
                }
            }
        }

        // 3. Razor bundle & path helpers: @Styles.Render(...)
        foreach (Match match in StylesRenderRegex().Matches(content))
        {
            string args = match.Groups[1].Value;
            foreach (Match strMatch in CSharpStringLiteralRegex().Matches(args))
            {
                string raw = strMatch.Value.Trim('"', '@');
                AddRef(raw, StylesheetReferenceKind.Exact);
            }
        }

        // 4. Url.Content(...) calls outside link tags
        foreach (Match match in UrlContentGlobalRegex().Matches(content))
        {
            string url = match.Groups[1].Value;
            string clean = StripQueryAndFragment(url);
            if (clean.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                AddRef(url, StylesheetReferenceKind.Exact);
            }
        }

        // 5. C# string literals ending in .css in Razor blocks
        if (isRazor)
        {
            void ScanSnippetForCssStrings(string snippet)
            {
                foreach (Match strMatch in CSharpStringLiteralRegex().Matches(snippet))
                {
                    string raw = strMatch.Value;
                    string unquoted = raw.StartsWith("@\"") ? raw.Substring(2, raw.Length - 3).Replace("\"\"", "\"")
                        : (raw.StartsWith("\"") && raw.EndsWith("\"")) ? Regex.Unescape(raw.Substring(1, raw.Length - 2))
                        : raw;
                    string clean = StripQueryAndFragment(unquoted);
                    if (clean.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        AddRef(unquoted, StylesheetReferenceKind.Exact);
                    }
                }
            }

            foreach (Match match in RazorParenthesizedRegex().Matches(content)) ScanSnippetForCssStrings(match.Value);
            foreach (Match match in RazorBlockRegex().Matches(content)) ScanSnippetForCssStrings(match.Value);
            foreach (Match match in RazorControlFlowRegex().Matches(content)) ScanSnippetForCssStrings(match.Value);
        }

        // 6. @import in raw content (e.g. within <style> tags)
        foreach (Match match in ImportRuleRegex().Matches(content))
        {
            string raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            string clean = StripQueryAndFragment(raw);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                AddRef(clean, StylesheetReferenceKind.Exact);
            }
        }

        return results;
    }

    private static string StripQueryAndFragment(string path)
    {
        int qIdx = path.IndexOfAny(['?', '#']);
        return qIdx >= 0 ? path.Substring(0, qIdx).Trim() : path.Trim();
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex(@"asp-href-include\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AspHrefIncludeRegex();

    [GeneratedRegex(@"asp-href-exclude\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AspHrefExcludeRegex();

    [GeneratedRegex(@"href\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HrefAttributeRegex();

    [GeneratedRegex(@"@Styles\.Render\s*\(([^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StylesRenderRegex();

    [GeneratedRegex(@"Url\.Content\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlContentGlobalRegex();

    [GeneratedRegex(@"Url\.Content\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlContentCallRegex();

    [GeneratedRegex(@"@import\s+(?:url\(\s*['""]?([^'"")]+)['""]?\s*\)|['""]([^'""]+)['""])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ImportRuleRegex();
}
