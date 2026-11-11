using TrimCSS.Parsers;
using Xunit;

namespace TrimCSS.Tests;

public class CssParserTests
{
    private readonly CssParser _parser = new();

    [Fact]
    public void ParseCssContent_WithClassSelectors_ExtractsClassNamesCorrectly()
    {
        // Arrange
        var css = @"
            .btn {
                display: inline-block;
                padding: 6px 12px;
            }

            .btn-primary {
                background-color: blue;
                color: white;
            }
        ";

        // Act
        var selectors = _parser.ParseCssContent(css).ToList();

        // Assert
        Assert.Equal(2, selectors.Count);

        var first = selectors[0];
        Assert.Equal(".btn", first.SelectorText);
        Assert.Contains("btn", first.TargetClasses);
        Assert.False(first.IsIdSelector);
        Assert.Equal(2, first.LineNumber);

        var second = selectors[1];
        Assert.Equal(".btn-primary", second.SelectorText);
        Assert.Contains("btn-primary", second.TargetClasses);
        Assert.False(second.IsIdSelector);
        Assert.Equal(7, second.LineNumber);
    }

    [Fact]
    public void ParseCssContent_WithIdSelectors_FlagsIsIdSelectorCorrectly()
    {
        // Arrange
        var css = @"
            #main-header {
                height: 60px;
            }

            header#navbar.sticky {
                position: fixed;
            }
        ";

        // Act
        var selectors = _parser.ParseCssContent(css).ToList();

        // Assert
        Assert.Equal(2, selectors.Count);

        var first = selectors[0];
        Assert.Equal("#main-header", first.SelectorText);
        Assert.True(first.IsIdSelector);
        Assert.Empty(first.TargetClasses);
        Assert.Equal(2, first.LineNumber);

        var second = selectors[1];
        Assert.Equal("header#navbar.sticky", second.SelectorText);
        Assert.True(second.IsIdSelector);
        Assert.Contains("sticky", second.TargetClasses);
        Assert.Equal(6, second.LineNumber);
    }

    [Fact]
    public void ParseCssContent_WithCompoundRules_ExtractsAllTargetClasses()
    {
        // Arrange
        var css = @"
            div.card-body > ul.nav-list {
                list-style: none;
                margin: 0;
            }

            aside.sidebar .widget-title + p.lead {
                font-size: 1.25rem;
            }
        ";

        // Act
        var selectors = _parser.ParseCssContent(css).ToList();

        // Assert
        Assert.Equal(2, selectors.Count);

        var compound1 = selectors[0];
        Assert.Equal("div.card-body > ul.nav-list", compound1.SelectorText);
        Assert.Equal(2, compound1.TargetClasses.Count);
        Assert.Contains("card-body", compound1.TargetClasses);
        Assert.Contains("nav-list", compound1.TargetClasses);
        Assert.False(compound1.IsIdSelector);
        Assert.Equal(2, compound1.LineNumber);

        var compound2 = selectors[1];
        Assert.Equal("aside.sidebar .widget-title + p.lead", compound2.SelectorText);
        Assert.Equal(3, compound2.TargetClasses.Count);
        Assert.Contains("sidebar", compound2.TargetClasses);
        Assert.Contains("widget-title", compound2.TargetClasses);
        Assert.Contains("lead", compound2.TargetClasses);
        Assert.False(compound2.IsIdSelector);
        Assert.Equal(7, compound2.LineNumber);
    }

    [Fact]
    public void ParseCssContent_IgnoresCommentsAndKeyframes()
    {
        // Arrange
        var css = @"
            /* Top-level banner comment */
            @keyframes pulse {
                0% { opacity: 1; }
                50% { opacity: 0.5; }
                100% { opacity: 1; }
            }

            @-webkit-keyframes slideIn {
                from { transform: translateX(-100%); }
                to { transform: translateX(0); }
            }

            /* Another block comment */
            .alert-box {
                border: 1px solid red;
            }
        ";

        // Act
        var selectors = _parser.ParseCssContent(css).ToList();

        // Assert
        Assert.Single(selectors);
        var selector = selectors[0];
        Assert.Equal(".alert-box", selector.SelectorText);
        Assert.Contains("alert-box", selector.TargetClasses);
        Assert.Equal(15, selector.LineNumber);
    }

    [Fact]
    public void ParseCssContent_WithinMediaQueries_ExtractsSelectorsCorrectly()
    {
        // Arrange
        var css = @"
            @media (min-width: 768px) {
                .container-fluid {
                    width: 750px;
                }

                .row > .col-md-6 {
                    flex: 0 0 50%;
                }
            }
        ";

        // Act
        var selectors = _parser.ParseCssContent(css).ToList();

        // Assert
        Assert.Equal(2, selectors.Count);
        Assert.Equal(".container-fluid", selectors[0].SelectorText);
        Assert.Contains("container-fluid", selectors[0].TargetClasses);

        Assert.Equal(".row > .col-md-6", selectors[1].SelectorText);
        Assert.Contains("row", selectors[1].TargetClasses);
        Assert.Contains("col-md-6", selectors[1].TargetClasses);
    }

    [Fact]
    public async Task ParseCssFileAsync_WithValidFile_ParsesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".css";
        try
        {
            await File.WriteAllTextAsync(tempFile, @"
                .hero-banner {
                    height: 400px;
                }
            ");

            // Act
            var selectors = (await _parser.ParseCssFileAsync(tempFile)).ToList();

            // Assert
            Assert.Single(selectors);
            Assert.Equal(".hero-banner", selectors[0].SelectorText);
            Assert.Contains("hero-banner", selectors[0].TargetClasses);
            Assert.Equal(tempFile, selectors[0].FilePath);
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
    public async Task ParseCssFileAsync_WithMissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.css");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _parser.ParseCssFileAsync(nonExistentPath));
    }

    [Fact]
    public async Task ParseCssFileAsync_WithEmptyOrNullPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _parser.ParseCssFileAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _parser.ParseCssFileAsync(null!));
    }

    [Fact]
    public void ParseCssContent_WithMalformedCss_RecoversGracefully()
    {
        // Arrange
        var malformedCss = @"
            .valid-first { color: blue; }
            .broken-syntax {{{ ;;; }}}
            div.card-body > ul.nav-list { color: green; }
        ";

        // Act
        var selectors = _parser.ParseCssContent(malformedCss).ToList();

        // Assert - Valid selectors are still successfully parsed and extracted
        Assert.Contains(selectors, s => s.SelectorText == ".valid-first" && s.TargetClasses.Contains("valid-first"));
        Assert.Contains(selectors, s => s.TargetClasses.Contains("card-body") && s.TargetClasses.Contains("nav-list"));
    }
}
