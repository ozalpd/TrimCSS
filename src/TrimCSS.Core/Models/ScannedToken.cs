namespace TrimCSS.Models;

/// <summary>
/// Specifies the type of token extracted during source code scanning.
/// </summary>
public enum TokenType
{
    /// <summary>
    /// A class name found within an HTML element's class attribute.
    /// </summary>
    HtmlClassAttribute,

    /// <summary>
    /// A class reference embedded within a Razor code block or conditional expression.
    /// </summary>
    RazorExpression,

    /// <summary>
    /// A string literal found in JavaScript or TypeScript code.
    /// </summary>
    JsStringLiteral,

    /// <summary>
    /// A template literal found in JavaScript or TypeScript code.
    /// </summary>
    JsTemplateLiteral
}

/// <summary>
/// Represents a token scanned from an HTML, Razor, or JavaScript source file.
/// </summary>
public class ScannedToken
{
    /// <summary>
    /// Gets or sets the value of the scanned token.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file path of the source file where the token was located.
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the scanned token indicating its source context.
    /// </summary>
    public TokenType TokenType { get; set; }
}
