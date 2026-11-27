using System.Text;
using System.Text.RegularExpressions;

namespace TrimCSS.Parsers;

/// <summary>
/// Represents a <c>.IncludeDirectory(...)</c> registration within an ASP.NET MVC <c>StyleBundle</c>.
/// </summary>
public sealed record StyleBundleDirectoryInclude(
    string DirectoryVirtualPath,
    string SearchPattern,
    bool SearchSubdirectories);

/// <summary>
/// Represents a parsed ASP.NET MVC <c>StyleBundle</c> definition from <c>BundleConfig.cs</c>.
/// </summary>
public sealed record StyleBundleDefinition(
    string VirtualPath,
    List<string> IncludedPaths,
    List<StyleBundleDirectoryInclude> IncludedDirectories);

/// <summary>
/// Parses ASP.NET MVC <c>BundleConfig.cs</c> files to extract <c>StyleBundle</c> virtual path mappings.
/// </summary>
public static partial class BundleConfigParser
{
    [GeneratedRegex(@"(?:(?:var|[\w.<>]+)\s+(?<varName>\w+)\s*=\s*)?\bnew\s+(?:[\w.]+\.)?StyleBundle\s*\(", RegexOptions.Compiled)]
    private static partial Regex StyleBundleCtorRegex();

    [GeneratedRegex(@"\G\s*\.\s*(IncludeDirectory|Include)\s*\(", RegexOptions.Compiled)]
    private static partial Regex ChainedIncludeRegex();

    [GeneratedRegex(@"(?:@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*"")", RegexOptions.Compiled)]
    private static partial Regex CSharpStringLiteralRegex();

    [GeneratedRegex(@"\btrue\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrueLiteralRegex();

    /// <summary>
    /// Parses C# source text from <c>BundleConfig.cs</c> and extracts all <c>StyleBundle</c> definitions.
    /// </summary>
    public static IReadOnlyList<StyleBundleDefinition> Parse(string csharpContent)
    {
        if (string.IsNullOrWhiteSpace(csharpContent))
        {
            return [];
        }

        string stripped = StripCSharpComments(csharpContent);
        var results = new List<StyleBundleDefinition>();
        var bundlesByVar = new Dictionary<string, StyleBundleDefinition>(StringComparer.Ordinal);

        foreach (Match match in StyleBundleCtorRegex().Matches(stripped))
        {
            if (IsInsideStringLiteral(stripped, match.Index))
            {
                continue;
            }

            int openParenIndex = match.Index + match.Length - 1;
            var (ctorArgs, closeParenIndex) = ReadBalancedParentheses(stripped, openParenIndex);
            if (closeParenIndex < 0)
            {
                continue;
            }

            string? virtualPath = ExtractFirstStringLiteral(ctorArgs);
            if (string.IsNullOrWhiteSpace(virtualPath))
            {
                continue;
            }

            var definition = new StyleBundleDefinition(StripQueryAndFragment(virtualPath), [], []);
            results.Add(definition);

            string varName = match.Groups["varName"].Value;
            if (!string.IsNullOrWhiteSpace(varName))
            {
                bundlesByVar[varName] = definition;
            }

            ParseChainedIncludes(stripped, closeParenIndex + 1, definition);
        }

        // Handle variable-based .Include(...) / .IncludeDirectory(...) calls
        foreach (var (varName, definition) in bundlesByVar)
        {
            var varCallRegex = new Regex($@"\b{Regex.Escape(varName)}\s*(?=\.)", RegexOptions.Compiled);
            foreach (Match varMatch in varCallRegex.Matches(stripped))
            {
                if (IsInsideStringLiteral(stripped, varMatch.Index))
                {
                    continue;
                }

                ParseChainedIncludes(stripped, varMatch.Index + varMatch.Length, definition);
            }
        }

        return results;
    }

    private static void ParseChainedIncludes(string code, int startIndex, StyleBundleDefinition definition)
    {
        int i = startIndex;
        int n = code.Length;

        while (i < n)
        {
            SkipWhitespace(code, ref i);
            if (i >= n || code[i] == ';')
            {
                break;
            }

            if (code[i] == '.')
            {
                var methodMatch = ChainedIncludeRegex().Match(code, i);
                if (methodMatch.Success && methodMatch.Index == i)
                {
                    string methodName = methodMatch.Groups[1].Value;
                    int openParen = methodMatch.Index + methodMatch.Length - 1;
                    var (args, closeParen) = ReadBalancedParentheses(code, openParen);
                    if (closeParen < 0)
                    {
                        break;
                    }

                    if (string.Equals(methodName, "Include", StringComparison.Ordinal))
                    {
                        ParseIncludeCall(args, definition);
                    }
                    else if (string.Equals(methodName, "IncludeDirectory", StringComparison.Ordinal))
                    {
                        ParseIncludeDirectoryCall(args, definition);
                    }

                    i = closeParen + 1;
                    continue;
                }
            }

            // Advance past unrelated tokens, strings, or balanced blocks until ';'
            if (code[i] == '"' || (code[i] == '@' && i + 1 < n && code[i + 1] == '"'))
            {
                SkipStringLiteral(code, ref i);
            }
            else if (code[i] == '(')
            {
                var (_, closeParen) = ReadBalancedParentheses(code, i);
                i = closeParen >= 0 ? closeParen + 1 : i + 1;
            }
            else if (code[i] == ')' || code[i] == ',' || code[i] == '}')
            {
                break;
            }
            else
            {
                i++;
            }
        }
    }

    private static void ParseIncludeCall(string args, StyleBundleDefinition definition)
    {
        foreach (Match strMatch in CSharpStringLiteralRegex().Matches(args))
        {
            string value = UnquoteCSharpString(strMatch.Value);
            string clean = StripQueryAndFragment(value);
            if (clean.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
                !definition.IncludedPaths.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                definition.IncludedPaths.Add(clean);
            }
        }
    }

    private static void ParseIncludeDirectoryCall(string args, StyleBundleDefinition definition)
    {
        var splitArgs = SplitTopLevelArguments(args);
        if (splitArgs.Count < 2)
        {
            return;
        }

        string? directoryVirtualPath = ExtractFirstStringLiteral(splitArgs[0]);
        string? searchPattern = ExtractFirstStringLiteral(splitArgs[1]);
        bool searchSubdirectories = splitArgs.Count >= 3 && TrueLiteralRegex().IsMatch(splitArgs[2]);

        if (!string.IsNullOrWhiteSpace(directoryVirtualPath) && !string.IsNullOrWhiteSpace(searchPattern))
        {
            definition.IncludedDirectories.Add(new StyleBundleDirectoryInclude(
                directoryVirtualPath.Trim(),
                searchPattern.Trim(),
                searchSubdirectories));
        }
    }

    private static List<string> SplitTopLevelArguments(string args)
    {
        var result = new List<string>();
        int start = 0;
        int parenDepth = 0;
        int i = 0;

        while (i < args.Length)
        {
            char c = args[i];
            if (c == '"' || (c == '@' && i + 1 < args.Length && args[i + 1] == '"'))
            {
                SkipStringLiteral(args, ref i);
                continue;
            }

            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')' && parenDepth > 0)
            {
                parenDepth--;
            }
            else if (c == ',' && parenDepth == 0)
            {
                result.Add(args.Substring(start, i - start));
                start = i + 1;
            }

            i++;
        }

        if (start <= args.Length)
        {
            result.Add(args.Substring(start));
        }

        return result;
    }

    private static (string Content, int CloseParenIndex) ReadBalancedParentheses(string text, int openParenIndex)
    {
        if (openParenIndex < 0 || openParenIndex >= text.Length || text[openParenIndex] != '(')
        {
            return (string.Empty, -1);
        }

        int depth = 1;
        int i = openParenIndex + 1;

        while (i < text.Length && depth > 0)
        {
            char c = text[i];
            if (c == '"' || (c == '@' && i + 1 < text.Length && text[i + 1] == '"'))
            {
                SkipStringLiteral(text, ref i);
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return (text.Substring(openParenIndex + 1, i - openParenIndex - 1), i);
                }
            }

            i++;
        }

        return (string.Empty, -1);
    }

    private static string? ExtractFirstStringLiteral(string snippet)
    {
        Match match = CSharpStringLiteralRegex().Match(snippet);
        return match.Success ? UnquoteCSharpString(match.Value) : null;
    }

    private static string UnquoteCSharpString(string raw)
    {
        if (raw.StartsWith("@\"", StringComparison.Ordinal) && raw.EndsWith('"') && raw.Length >= 3)
        {
            return raw.Substring(2, raw.Length - 3).Replace("\"\"", "\"");
        }

        if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2)
        {
            try
            {
                return Regex.Unescape(raw.Substring(1, raw.Length - 2));
            }
            catch
            {
                return raw.Substring(1, raw.Length - 2);
            }
        }

        return raw;
    }

    private static string StripQueryAndFragment(string path)
    {
        int qIdx = path.IndexOfAny(['?', '#']);
        return qIdx >= 0 ? path.Substring(0, qIdx).Trim() : path.Trim();
    }

    private static string StripCSharpComments(string code)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        int n = code.Length;

        while (i < n)
        {
            // Verbatim string literal @"..."
            if (code[i] == '@' && i + 1 < n && code[i + 1] == '"')
            {
                sb.Append("@\"");
                i += 2;
                while (i < n)
                {
                    if (code[i] == '"')
                    {
                        sb.Append('"');
                        i++;
                        if (i < n && code[i] == '"')
                        {
                            sb.Append('"');
                            i++;
                            continue;
                        }
                        break;
                    }
                    sb.Append(code[i++]);
                }
                continue;
            }

            // Regular string literal "..."
            if (code[i] == '"')
            {
                sb.Append('"');
                i++;
                while (i < n)
                {
                    if (code[i] == '\\' && i + 1 < n)
                    {
                        sb.Append(code[i]);
                        sb.Append(code[i + 1]);
                        i += 2;
                        continue;
                    }
                    char ch = code[i++];
                    sb.Append(ch);
                    if (ch == '"')
                    {
                        break;
                    }
                }
                continue;
            }

            // Single-line comment //...
            if (code[i] == '/' && i + 1 < n && code[i + 1] == '/')
            {
                i += 2;
                while (i < n && code[i] != '\n')
                {
                    i++;
                }
                sb.Append(' ');
                continue;
            }

            // Multi-line comment /*...*/
            if (code[i] == '/' && i + 1 < n && code[i + 1] == '*')
            {
                i += 2;
                while (i < n && !(code[i] == '*' && i + 1 < n && code[i + 1] == '/'))
                {
                    if (code[i] == '\n')
                    {
                        sb.Append('\n');
                    }
                    i++;
                }
                if (i < n)
                {
                    i += 2;
                }
                sb.Append(' ');
                continue;
            }

            sb.Append(code[i++]);
        }

        return sb.ToString();
    }

    private static bool IsInsideStringLiteral(string code, int targetIndex)
    {
        int i = 0;
        while (i < targetIndex && i < code.Length)
        {
            if (code[i] == '"' || (code[i] == '@' && i + 1 < code.Length && code[i + 1] == '"'))
            {
                int strStart = i;
                SkipStringLiteral(code, ref i);
                if (targetIndex > strStart && targetIndex < i)
                {
                    return true;
                }
                continue;
            }
            i++;
        }
        return false;
    }

    private static void SkipStringLiteral(string code, ref int i)
    {
        if (code[i] == '@' && i + 1 < code.Length && code[i + 1] == '"')
        {
            i += 2;
            while (i < code.Length)
            {
                if (code[i] == '"')
                {
                    i++;
                    if (i < code.Length && code[i] == '"')
                    {
                        i++;
                        continue;
                    }
                    break;
                }
                i++;
            }
            return;
        }

        if (code[i] == '"')
        {
            i++;
            while (i < code.Length)
            {
                if (code[i] == '\\' && i + 1 < code.Length)
                {
                    i += 2;
                    continue;
                }
                if (code[i++] == '"')
                {
                    break;
                }
            }
        }
    }

    private static void SkipWhitespace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }
    }
}
