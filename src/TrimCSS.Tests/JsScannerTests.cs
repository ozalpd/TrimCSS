using TrimCSS.Models;
using TrimCSS.Parsers;
using Xunit;

namespace TrimCSS.Tests;

public class JsScannerTests
{
    private readonly JsScanner _scanner = new();

    [Fact]
    public void SupportedExtensions_ContainsJsExtension()
    {
        var extensions = _scanner.SupportedExtensions.ToList();

        Assert.Contains(".js", extensions);
    }

    [Fact]
    public void ScanContent_ClassListAdd_ExtractsIndividualStringLiterals()
    {
        // Arrange
        var js = @"
            const element = document.querySelector('#btn');
            element.classList.add('active', 'fade-in');
            element.classList.remove('disabled');
        ";

        // Act
        var tokens = _scanner.ScanContent(js, "app.js").ToList();

        // Assert
        Assert.All(tokens, t => Assert.Equal(TokenType.JsStringLiteral, t.TokenType));
        Assert.All(tokens, t => Assert.Equal("app.js", t.SourceFile));

        var values = tokens.Select(t => t.Value).ToHashSet();
        Assert.Contains("active", values);
        Assert.Contains("fade-in", values);
        Assert.Contains("disabled", values);
    }

    [Fact]
    public void ScanContent_ClassNameAssignment_SplitsSpaceSeparatedClasses()
    {
        // Arrange
        var js = @"
            const el = document.getElementById('card');
            el.className = ""card shadow-sm p-3"";
        ";

        // Act
        var tokens = _scanner.ScanContent(js, "card.js").ToList();

        // Assert
        var values = tokens.Select(t => t.Value).ToHashSet();
        Assert.Contains("card", values);
        Assert.Contains("shadow-sm", values);
        Assert.Contains("p-3", values);
        Assert.All(tokens, t => Assert.Equal(TokenType.JsStringLiteral, t.TokenType));
    }

    [Fact]
    public void ScanContent_TemplateLiterals_ExtractsQuasiChunks()
    {
        // Arrange
        var js = @"
            function showAlert(type) {
                const alertClass = `alert-${type}`;
                const complex = `modal-header ${isLarge ? 'modal-lg' : 'modal-sm'} d-flex`;
                return alertClass;
            }
        ";

        // Act
        var tokens = _scanner.ScanContent(js, "alert.js").ToList();

        // Assert
        var templateTokens = tokens.Where(t => t.TokenType == TokenType.JsTemplateLiteral).Select(t => t.Value).ToHashSet();
        var stringTokens = tokens.Where(t => t.TokenType == TokenType.JsStringLiteral).Select(t => t.Value).ToHashSet();

        // Template quasis
        Assert.Contains("alert-", templateTokens);
        Assert.Contains("modal-header", templateTokens);
        Assert.Contains("d-flex", templateTokens);

        // String literals inside ternary expression within template literal
        Assert.Contains("modal-lg", stringTokens);
        Assert.Contains("modal-sm", stringTokens);
    }

    [Fact]
    public void ScanContent_FiltersOutUrlsAndFilePaths()
    {
        // Arrange
        var js = @"
            import { service } from './services/api.js';
            const apiUrl = 'https://api.example.com/v1/users';
            const iconPath = '/assets/images/icon.svg';
            const validClass = 'user-card';
        ";

        // Act
        var tokens = _scanner.ScanContent(js, "service.js").ToList();

        // Assert
        var values = tokens.Select(t => t.Value).ToHashSet();
        Assert.Contains("user-card", values);
        Assert.DoesNotContain("https://api.example.com/v1/users", values);
        Assert.DoesNotContain("./services/api.js", values);
        Assert.DoesNotContain("/assets/images/icon.svg", values);
        Assert.DoesNotContain("v1", values);
    }

    [Fact]
    public void ScanContent_HandlesMalformedJs_RecoversGracefully()
    {
        // Arrange
        var malformedJs = @"
            function broken() {
                var x = 'card-valid';
                element.classList.add(""active"", ""fade-in"");
                const template = `alert-${type}`;
                var y = {{{ ;;; syntax error (((
            }
        ";

        // Act
        var tokens = _scanner.ScanContent(malformedJs, "broken.js").ToList();

        // Assert - Valid candidate tokens are still successfully extracted
        var stringValues = tokens.Where(t => t.TokenType == TokenType.JsStringLiteral).Select(t => t.Value).ToHashSet();
        var templateValues = tokens.Where(t => t.TokenType == TokenType.JsTemplateLiteral).Select(t => t.Value).ToHashSet();

        Assert.Contains("card-valid", stringValues);
        Assert.Contains("active", stringValues);
        Assert.Contains("fade-in", stringValues);
        Assert.Contains("alert-", templateValues);
    }

    [Fact]
    public void ScanContent_DeduplicatesTokensPerFileAndType()
    {
        // Arrange
        var js = @"
            el.classList.add('active');
            btn.classList.add('active');
            const str = 'active';
            const template = `active-${type}`;
        ";

        // Act
        var tokens = _scanner.ScanContent(js, "dup.js").ToList();

        // Assert
        Assert.Single(tokens, t => t.Value == "active" && t.TokenType == TokenType.JsStringLiteral);
        Assert.Single(tokens, t => t.Value == "active-" && t.TokenType == TokenType.JsTemplateLiteral);
    }

    [Fact]
    public async Task ScanFileAsync_WithValidFile_ReadsAndExtractsTokens()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.js");
        try
        {
            await File.WriteAllTextAsync(tempFile, @"
                element.classList.add('button-primary');
                const badge = `badge-${status}`;
            ");

            // Act
            var tokens = (await _scanner.ScanFileAsync(tempFile)).ToList();

            // Assert
            Assert.Contains(tokens, t => t.Value == "button-primary" && t.TokenType == TokenType.JsStringLiteral && t.SourceFile == tempFile);
            Assert.Contains(tokens, t => t.Value == "badge-" && t.TokenType == TokenType.JsTemplateLiteral && t.SourceFile == tempFile);
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
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.js");

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
