using TrimCSS.Models;
using TrimCSS.Parsers;
using Xunit;

namespace TrimCSS.Tests;

public class HtmlRazorScannerTests
{
    private readonly HtmlRazorScanner _scanner = new();

    [Fact]
    public void SupportedExtensions_ContainsHtmlAndRazorExtensions()
    {
        var extensions = _scanner.SupportedExtensions.ToList();

        Assert.Contains(".html", extensions);
        Assert.Contains(".htm", extensions);
        Assert.Contains(".cshtml", extensions);
    }

    [Fact]
    public void ScanContent_StandardHtml_ExtractsMultipleSpaceSeparatedClasses()
    {
        // Arrange
        var html = @"
            <!DOCTYPE html>
            <html>
            <body>
                <header class=""navbar navbar-expand-lg bg-light"">
                    <a class=""navbar-brand text-primary"" href=""#"">Brand</a>
                </header>
                <main class=""container my-5"">
                    <div class=""card shadow-sm p-4"">
                        <p class=""lead mb-0 text-dark"">Welcome to TrimCSS</p>
                    </div>
                </main>
            </body>
            </html>
        ";

        // Act
        var tokens = _scanner.ScanContent(html, "index.html").ToList();

        // Assert
        Assert.All(tokens, t => Assert.Equal(TokenType.HtmlClassAttribute, t.TokenType));
        Assert.All(tokens, t => Assert.Equal("index.html", t.SourceFile));

        var values = tokens.Select(t => t.Value).ToHashSet();
        Assert.Contains("navbar", values);
        Assert.Contains("navbar-expand-lg", values);
        Assert.Contains("bg-light", values);
        Assert.Contains("navbar-brand", values);
        Assert.Contains("text-primary", values);
        Assert.Contains("container", values);
        Assert.Contains("my-5", values);
        Assert.Contains("card", values);
        Assert.Contains("shadow-sm", values);
        Assert.Contains("p-4", values);
        Assert.Contains("lead", values);
        Assert.Contains("mb-0", values);
        Assert.Contains("text-dark", values);
    }

    [Fact]
    public void ScanContent_RazorCshtml_ExtractsConditionalTernaryExpressions()
    {
        // Arrange
        var razor = @"
            @page
            @model MyModel
            <div class=""btn @(isActive ? ""btn-active"" : ""btn-disabled"")"">
                <span class=""badge @(hasError ? ""bg-danger text-white"" : ""bg-success"")"">
                    Status
                </span>
            </div>
            <aside class=""sidebar @(isCollapsed ? ""collapsed"" : """")"">
                Navigation
            </aside>
        ";

        // Act
        var tokens = _scanner.ScanContent(razor, "Views/Home/Index.cshtml").ToList();

        // Assert
        var htmlTokens = tokens.Where(t => t.TokenType == TokenType.HtmlClassAttribute).Select(t => t.Value).ToHashSet();
        var razorTokens = tokens.Where(t => t.TokenType == TokenType.RazorExpression).Select(t => t.Value).ToHashSet();

        // Static classes extracted via HTML pass
        Assert.Contains("btn", htmlTokens);
        Assert.Contains("badge", htmlTokens);
        Assert.Contains("sidebar", htmlTokens);

        // Dynamic classes extracted via Razor expression pass
        Assert.Contains("btn-active", razorTokens);
        Assert.Contains("btn-disabled", razorTokens);
        Assert.Contains("bg-danger", razorTokens);
        Assert.Contains("text-white", razorTokens);
        Assert.Contains("bg-success", razorTokens);
        Assert.Contains("collapsed", razorTokens);

        // Ensure empty string literal ("") is ignored
        Assert.DoesNotContain("", razorTokens);
        Assert.DoesNotContain("", htmlTokens);
    }

    [Fact]
    public void ScanContent_DeduplicatesTokensPerFileTypePair()
    {
        // Arrange
        var markup = @"
            <div class=""item active""></div>
            <div class=""item active""></div>
            <span class=""item""></span>
            <div class=""@(isSelected ? ""active"" : """")""></div>
        ";

        // Act
        var tokens = _scanner.ScanContent(markup, "test.cshtml").ToList();

        // Assert
        Assert.Single(tokens, t => t.Value == "item" && t.TokenType == TokenType.HtmlClassAttribute);
        Assert.Single(tokens, t => t.Value == "active" && t.TokenType == TokenType.HtmlClassAttribute);
        Assert.Single(tokens, t => t.Value == "active" && t.TokenType == TokenType.RazorExpression);
    }

    [Fact]
    public void ScanContent_HtmlFile_DoesNotRunRazorExpressionPass()
    {
        // Arrange - in standard .html, Razor @ expressions should not produce RazorExpression tokens
        var html = @"
            <div class=""test-container"">
                @(someCSharpCode ? ""ignored"" : """")
            </div>
        ";

        // Act
        var tokens = _scanner.ScanContent(html, "page.html").ToList();

        // Assert
        Assert.Contains(tokens, t => t.Value == "test-container" && t.TokenType == TokenType.HtmlClassAttribute);
        Assert.DoesNotContain(tokens, t => t.TokenType == TokenType.RazorExpression);
    }

    [Fact]
    public async Task ScanFileAsync_WithValidFile_ReadsAndExtractsTokens()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.cshtml");
        try
        {
            await File.WriteAllTextAsync(tempFile, @"
                <div class=""alert @(isSuccess ? ""alert-success"" : ""alert-warning"")"">
                    Message
                </div>
            ");

            // Act
            var tokens = (await _scanner.ScanFileAsync(tempFile)).ToList();

            // Assert
            Assert.Contains(tokens, t => t.Value == "alert" && t.TokenType == TokenType.HtmlClassAttribute && t.SourceFile == tempFile);
            Assert.Contains(tokens, t => t.Value == "alert-success" && t.TokenType == TokenType.RazorExpression && t.SourceFile == tempFile);
            Assert.Contains(tokens, t => t.Value == "alert-warning" && t.TokenType == TokenType.RazorExpression && t.SourceFile == tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ScanFileAsync_WithMissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.html");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _scanner.ScanFileAsync(nonExistentPath));
    }

    [Fact]
    public async Task ScanFileAsync_WithNullOrEmptyPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _scanner.ScanFileAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _scanner.ScanFileAsync(null!));
    }
}
