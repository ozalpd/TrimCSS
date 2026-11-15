using System.Text.RegularExpressions;
using Esprima;
using Esprima.Ast;
using TrimCSS.Contracts;
using TrimCSS.Models;
using TokenType = TrimCSS.Models.TokenType;

namespace TrimCSS.Parsers;

/// <summary>
/// Scans JavaScript source files (.js) to extract candidate CSS class tokens from string literals and template literals.
/// </summary>
public partial class JsScanner : ISourceScanner
{
    private static readonly string[] DefaultSupportedExtensions = [".js"];

    private readonly ParserOptions _parserOptions = new()
    {
        Tolerant = true,
        AllowReturnOutsideFunction = true
    };

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
    /// Scans JavaScript source code and extracts potential CSS class tokens from string and template literals.
    /// </summary>
    /// <param name="content">The raw JavaScript code to parse.</param>
    /// <param name="filePath">The file path associated with the source code.</param>
    /// <returns>A collection of distinct <see cref="ScannedToken"/> instances discovered in the JavaScript code.</returns>
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

        var parser = new JavaScriptParser(_parserOptions);
        Program? program = null;

        try
        {
            // First attempt parsing as an ES Module (supports modern import/export statements)
            program = parser.ParseModule(content);
        }
        catch
        {
            try
            {
                // Fall back to script parsing (supports standard scripts)
                program = parser.ParseScript(content);
            }
            catch
            {
                // Parser failed on malformed syntax; fallback scanner will be used
            }
        }

        if (program != null)
        {
            foreach (var node in program.DescendantNodes())
            {
                if (node is Literal literal && literal.Value is string stringValue)
                {
                    ExtractStringLiteralTokens(stringValue, AddToken);
                }
                else if (node is TemplateLiteral templateLiteral)
                {
                    ExtractTemplateLiteralTokens(templateLiteral, AddToken);
                }
            }
        }
        else
        {
            // Fallback token extraction for malformed JavaScript
            ExtractFallbackTokens(content, AddToken);
        }

        return results;
    }

    private static void ExtractStringLiteralTokens(string rawValue, Action<string, TokenType> addToken)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        // Filter out URLs, file paths, or directory paths
        if (rawValue.Contains("://") || rawValue.Contains('/') || rawValue.Contains('\\'))
        {
            return;
        }

        var parts = rawValue.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (IsValidCssClassCandidate(part))
            {
                addToken(part, TokenType.JsStringLiteral);
            }
        }
    }

    private static void ExtractTemplateLiteralTokens(TemplateLiteral templateLiteral, Action<string, TokenType> addToken)
    {
        foreach (var quasi in templateLiteral.Quasis)
        {
            string raw = quasi.Value.Cooked ?? quasi.Value.Raw;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // A template quasi might contain multiple space-separated words (e.g. `card shadow-sm ${variant}`)
            var parts = raw.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (IsValidTemplateQuasiCandidate(part))
                {
                    addToken(part, TokenType.JsTemplateLiteral);
                }
            }
        }
    }

    private static void ExtractFallbackTokens(string code, Action<string, TokenType> addToken)
    {
        // 1. Extract string literals: "..." or '...'
        foreach (Match match in FallbackStringRegex().Matches(code))
        {
            string raw = match.Value;
            if (raw.Length >= 2)
            {
                string inner = raw.Substring(1, raw.Length - 2);
                ExtractStringLiteralTokens(inner, addToken);
            }
        }

        // 2. Extract template literal quasis: `...`
        foreach (Match match in FallbackTemplateRegex().Matches(code))
        {
            string raw = match.Value;
            if (raw.Length >= 2)
            {
                string inner = raw.Substring(1, raw.Length - 2);
                var quasis = TemplateExpressionRegex().Split(inner);
                foreach (var quasi in quasis)
                {
                    if (string.IsNullOrWhiteSpace(quasi))
                    {
                        continue;
                    }

                    var parts = quasi.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (IsValidTemplateQuasiCandidate(part))
                        {
                            addToken(part, TokenType.JsTemplateLiteral);
                        }
                    }
                }
            }
        }
    }

    private static bool IsValidCssClassCandidate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        // Filter out URLs, paths, HTML tags, or code operators
        if (token.Contains("://") || token.Contains('/') || token.Contains('\\') ||
            token.Contains('<') || token.Contains('>') || token.Contains(';') ||
            token.Contains(',') || token.Contains('=') || token.Contains('(') ||
            token.Contains(')') || token.Contains('{') || token.Contains('}') ||
            token.Contains('[') || token.Contains(']') || token.Contains('"') ||
            token.Contains('\'') || token.Contains('`'))
        {
            return false;
        }

        // Pure numbers (e.g. "100", "0") are not CSS class names
        if (int.TryParse(token, out _))
        {
            return false;
        }

        // Allow standard class identifiers, hyphens, underscores, and Tailwind colons (e.g. md:flex)
        return ValidClassRegex().IsMatch(token);
    }

    private static bool IsValidTemplateQuasiCandidate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (token.Contains("://") || token.Contains('/') || token.Contains('\\') ||
            token.Contains('<') || token.Contains('>') || token.Contains(';') ||
            token.Contains(',') || token.Contains('=') || token.Contains('(') ||
            token.Contains(')') || token.Contains('{') || token.Contains('}') ||
            token.Contains('[') || token.Contains(']') || token.Contains('"') ||
            token.Contains('\'') || token.Contains('`'))
        {
            return false;
        }

        if (int.TryParse(token, out _))
        {
            return false;
        }

        // Template quasis often end with a hyphen (e.g. "btn-", "alert-", "text-")
        return ValidTemplateQuasiRegex().IsMatch(token);
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

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_\-:]*$", RegexOptions.Compiled)]
    private static partial Regex ValidClassRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_\-:]*-?$", RegexOptions.Compiled)]
    private static partial Regex ValidTemplateQuasiRegex();

    [GeneratedRegex(@"""([^""\\]|\\.)*""|'([^'\\]|\\.)*'", RegexOptions.Compiled)]
    private static partial Regex FallbackStringRegex();

    [GeneratedRegex(@"`([^`\\]|\\.)*`", RegexOptions.Compiled)]
    private static partial Regex FallbackTemplateRegex();

    [GeneratedRegex(@"\$\{[^}]*\}", RegexOptions.Compiled)]
    private static partial Regex TemplateExpressionRegex();
}
