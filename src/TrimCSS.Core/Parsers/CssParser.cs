using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using TrimCSS.Contracts;
using TrimCSS.Models;
using AngleSharpCssParser = AngleSharp.Css.Parser.CssParser;

namespace TrimCSS.Parsers;

/// <summary>
/// Provides CSS stylesheet parsing and selector extraction using AngleSharp.Css.
/// </summary>
public class CssParser : TrimCSS.Contracts.ICssParser
{
    private readonly AngleSharpCssParser _angleSharpParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="CssParser"/> class.
    /// </summary>
    public CssParser()
    {
        _angleSharpParser = new AngleSharpCssParser(new CssParserOptions
        {
            IsToleratingInvalidSelectors = true,
            IsIncludingUnknownDeclarations = true,
            IsIncludingUnknownRules = true
        });
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<CssSelectorInfo>> ParseCssFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The CSS file '{filePath}' was not found.", filePath);
        }

        string content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ParseCssContent(content, filePath);
    }

    /// <summary>
    /// Parses CSS stylesheet content from a string and extracts metadata for all discovered selectors.
    /// </summary>
    /// <param name="content">The raw CSS text content to parse.</param>
    /// <param name="filePath">Optional file path to associate with the parsed selectors.</param>
    /// <returns>A collection of <see cref="CssSelectorInfo"/> objects representing the extracted selectors.</returns>
    public IEnumerable<CssSelectorInfo> ParseCssContent(string content, string filePath = "")
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var results = new List<CssSelectorInfo>();
        var scannedLocations = ScanSelectorLocations(content);

        foreach (var loc in scannedLocations)
        {
            if (string.IsNullOrWhiteSpace(loc.Selector))
            {
                continue;
            }

            var info = new CssSelectorInfo
            {
                SelectorText = loc.Selector,
                FilePath = filePath,
                LineNumber = loc.Line,
                ColumnNumber = loc.Col
            };

            try
            {
                // Parse individual selector using AngleSharp AST
                var sheet = _angleSharpParser.ParseStyleSheet(loc.Selector + " {}");
                var styleRule = sheet.Rules.OfType<ICssStyleRule>().FirstOrDefault();

                if (styleRule?.Selector != null)
                {
                    var visitor = new SelectorCollectorVisitor();
                    styleRule.Selector.Accept(visitor);

                    info.TargetClasses = visitor.Classes.Distinct().ToList();
                    info.IsIdSelector = visitor.HasId;
                }
                else
                {
                    ExtractFallbackTokens(loc.Selector, info);
                }
            }
            catch
            {
                // Graceful fallback for malformed selectors
                ExtractFallbackTokens(loc.Selector, info);
            }

            results.Add(info);
        }

        return results;
    }

    private static void ExtractFallbackTokens(string selector, CssSelectorInfo info)
    {
        var classes = new List<string>();
        bool hasId = false;

        var tokens = selector.Split([' ', '>', '+', '~', ',', ':', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.StartsWith('.'))
            {
                var cls = token.TrimStart('.').Split(['.', '#', ':', '['])[0];
                if (!string.IsNullOrEmpty(cls))
                {
                    classes.Add(cls);
                }
            }
            else if (token.StartsWith('#'))
            {
                hasId = true;
            }
        }

        info.TargetClasses = classes.Distinct().ToList();
        info.IsIdSelector = hasId;
    }

    private record ScannedLocation(string Selector, int Line, int Col);

    private static List<ScannedLocation> ScanSelectorLocations(string css)
    {
        var results = new List<ScannedLocation>();
        int i = 0;
        int n = css.Length;

        while (i < n)
        {
            SkipWhitespaceAndComments(css, ref i);
            if (i >= n) break;

            if (css[i] == '@')
            {
                int atStart = i;
                while (i < n && !char.IsWhiteSpace(css[i]) && css[i] != '{' && css[i] != ';')
                {
                    i++;
                }
                string atName = css.Substring(atStart, i - atStart);

                // Ignore @keyframes and vendor-prefixed @keyframes completely
                if (atName.Equals("@keyframes", StringComparison.OrdinalIgnoreCase) ||
                    atName.Equals("@-webkit-keyframes", StringComparison.OrdinalIgnoreCase) ||
                    atName.Equals("@-moz-keyframes", StringComparison.OrdinalIgnoreCase) ||
                    atName.Equals("@-o-keyframes", StringComparison.OrdinalIgnoreCase))
                {
                    while (i < n && css[i] != '{')
                    {
                        SkipWhitespaceAndComments(css, ref i);
                        if (i < n && css[i] != '{') i++;
                    }
                    SkipBalancedBraces(css, ref i);
                }
                else if (atName.Equals("@media", StringComparison.OrdinalIgnoreCase) ||
                         atName.Equals("@supports", StringComparison.OrdinalIgnoreCase) ||
                         atName.Equals("@layer", StringComparison.OrdinalIgnoreCase) ||
                         atName.Equals("@container", StringComparison.OrdinalIgnoreCase) ||
                         atName.Equals("@document", StringComparison.OrdinalIgnoreCase))
                {
                    // Grouping rule - skip rule header up to opening brace and step inside
                    while (i < n && css[i] != '{')
                    {
                        SkipWhitespaceAndComments(css, ref i);
                        if (i < n && css[i] != '{') i++;
                    }
                    if (i < n && css[i] == '{')
                    {
                        i++; // Step inside group
                    }
                }
                else
                {
                    // Regular at-rule e.g. @import, @charset, @namespace - skip to ';' or '{'
                    while (i < n && css[i] != ';' && css[i] != '{')
                    {
                        SkipWhitespaceAndComments(css, ref i);
                        if (i < n && css[i] != ';' && css[i] != '{') i++;
                    }
                    if (i < n)
                    {
                        if (css[i] == '{')
                        {
                            SkipBalancedBraces(css, ref i);
                        }
                        else
                        {
                            i++; // Skip ';'
                        }
                    }
                }
                continue;
            }

            if (css[i] == '}')
            {
                i++;
                continue;
            }

            int selectorStart = i;
            var (line, col) = GetLineAndColumn(css, selectorStart);

            while (i < n && css[i] != '{' && css[i] != '}')
            {
                if (css[i] == '/' && i + 1 < n && css[i + 1] == '*')
                {
                    SkipComment(css, ref i);
                }
                else if (css[i] == '"' || css[i] == '\'')
                {
                    SkipString(css, ref i);
                }
                else
                {
                    i++;
                }
            }

            int selectorEnd = i;
            if (i < n && css[i] == '{')
            {
                string rawSelector = CleanSelector(css.Substring(selectorStart, selectorEnd - selectorStart));
                if (!string.IsNullOrWhiteSpace(rawSelector))
                {
                    results.Add(new ScannedLocation(rawSelector, line, col));
                }
                SkipBalancedBraces(css, ref i);
            }
        }

        return results;
    }

    private static string CleanSelector(string raw)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '/' && i + 1 < raw.Length && raw[i + 1] == '*')
            {
                i += 2;
                while (i < raw.Length && !(raw[i] == '*' && i + 1 < raw.Length && raw[i + 1] == '/'))
                {
                    i++;
                }
                if (i < raw.Length) i += 2;
            }
            else
            {
                sb.Append(raw[i]);
                i++;
            }
        }
        return sb.ToString().Trim();
    }

    private static void SkipWhitespaceAndComments(string s, ref int i)
    {
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                SkipComment(s, ref i);
            }
            else
            {
                break;
            }
        }
    }

    private static void SkipComment(string s, ref int i)
    {
        i += 2;
        while (i < s.Length)
        {
            if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')
            {
                i += 2;
                break;
            }
            i++;
        }
    }

    private static void SkipString(string s, ref int i)
    {
        char quote = s[i++];
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i += 2;
            }
            else if (s[i] == quote)
            {
                i++;
                break;
            }
            else
            {
                i++;
            }
        }
    }

    private static void SkipBalancedBraces(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '{') return;
        int depth = 0;
        while (i < s.Length)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                SkipComment(s, ref i);
            }
            else if (s[i] == '"' || s[i] == '\'')
            {
                SkipString(s, ref i);
            }
            else
            {
                if (s[i] == '{')
                {
                    depth++;
                }
                else if (s[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        i++;
                        break;
                    }
                }
                i++;
            }
        }
    }

    private static (int Line, int Col) GetLineAndColumn(string s, int index)
    {
        int line = 1;
        int col = 1;
        for (int k = 0; k < index; k++)
        {
            if (s[k] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
        return (line, col);
    }

    private sealed class SelectorCollectorVisitor : ISelectorVisitor
    {
        public List<string> Classes { get; } = [];
        public bool HasId { get; private set; }

        public void Attribute(string name, string op, string? value) { }
        public void Child(string name, int step, int offset, ISelector selector) => selector.Accept(this);
        public void Class(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                Classes.Add(name);
            }
        }
        public void Combinator(IEnumerable<ISelector> selectors, IEnumerable<string> symbols)
        {
            foreach (var s in selectors) s.Accept(this);
        }
        public void Id(string value) => HasId = true;
        public void List(IEnumerable<ISelector> selectors)
        {
            foreach (var s in selectors) s.Accept(this);
        }
        public void Many(IEnumerable<ISelector> selectors)
        {
            foreach (var s in selectors) s.Accept(this);
        }
        public void PseudoClass(string name) { }
        public void PseudoElement(string name) { }
        public void Type(string name) { }
    }
}
