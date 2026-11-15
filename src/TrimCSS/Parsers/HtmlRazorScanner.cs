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
}
