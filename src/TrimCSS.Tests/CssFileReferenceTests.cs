using TrimCSS.Contracts;
using TrimCSS.Engine;
using TrimCSS.Models;
using TrimCSS.Parsers;
using Xunit;

namespace TrimCSS.Tests;

public class CssFileReferenceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ProjectScanner _scanner;
    private readonly HtmlRazorScanner _htmlScanner;
    private readonly JsScanner _jsScanner;
    private readonly CssParser _cssParser;

    public CssFileReferenceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"TrimCSS_FileRefTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _scanner = new ProjectScanner();
        _htmlScanner = new HtmlRazorScanner();
        _jsScanner = new JsScanner();
        _cssParser = new CssParser();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task ScanProjectAsync_HtmlLink_DetectsUsedVsOrphanCssFile()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        Directory.CreateDirectory(cssDir);

        await File.WriteAllTextAsync(Path.Combine(cssDir, "used.css"), ".used-rule { color: green; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "orphan.css"), ".orphan-rule { color: red; }");

        var htmlDir = Path.Combine(_testRoot, "public");
        Directory.CreateDirectory(htmlDir);
        await File.WriteAllTextAsync(Path.Combine(htmlDir, "index.html"), @"
            <!DOCTYPE html>
            <html>
            <head>
                <link rel=""stylesheet"" href=""/css/used.css"" />
            </head>
            <body>
                <div class=""used-rule"">Hello</div>
            </body>
            </html>
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(2, result.TotalCssFiles);
        Assert.Single(result.UnusedCssFiles);
        Assert.Empty(result.AmbiguousCssFiles);

        var orphan = result.UnusedCssFiles[0];
        Assert.Equal("css/orphan.css", orphan.RelativePath);
        Assert.Equal(CssFileReferenceStatus.Unused, orphan.Status);
        Assert.True(orphan.FileSizeBytes > 0);
        Assert.Equal(1, orphan.SelectorCount);
    }

    [Fact]
    public async Task ScanProjectAsync_RazorConstructs_DetectsSiteCssAndTagHelperGlobs()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "wwwroot", "css");
        Directory.CreateDirectory(cssDir);

        await File.WriteAllTextAsync(Path.Combine(cssDir, "site.css"), ".site-rule { color: blue; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "bundle1.css"), ".b1 { margin: 0; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "bundle2.css"), ".b2 { margin: 0; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "unreferenced.css"), ".unref { display: none; }");

        var viewsDir = Path.Combine(_testRoot, "Views", "Shared");
        Directory.CreateDirectory(viewsDir);

        await File.WriteAllTextAsync(Path.Combine(viewsDir, "_Layout.cshtml"), @"
            <!DOCTYPE html>
            <html>
            <head>
                <!-- 1. Href with query string -->
                <link rel=""stylesheet"" href=""~/css/site.css?v=123"" />
                <!-- 2. Tag helper glob include with exclude -->
                <link rel=""stylesheet"" asp-href-include=""~/css/bundle*.css"" />
                <!-- 3. Razor bundle helper -->
                @Styles.Render(""~/css/site.css"")
            </head>
            <body>
                @RenderBody()
            </body>
            </html>
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(4, result.TotalCssFiles);
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("wwwroot/css/unreferenced.css", result.UnusedCssFiles[0].RelativePath);
        Assert.Empty(result.AmbiguousCssFiles);
    }

    [Fact]
    public async Task ScanProjectAsync_CssImports_MarksImportedPartialsAsReferenced()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        var partialsDir = Path.Combine(cssDir, "partials");
        Directory.CreateDirectory(partialsDir);

        // main.css imports variables.css and reset.css
        await File.WriteAllTextAsync(Path.Combine(cssDir, "main.css"), @"
            @import ""partials/variables.css"";
            @import url('../css/reset.css');
            .main-content { font-size: 16px; }
        ");
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "variables.css"), ":root { --color: red; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "reset.css"), "* { box-sizing: border-box; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "dead-partial.css"), ".dead { color: gray; }");

        // HTML references only main.css
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "index.html"), @"
            <link rel=""stylesheet"" href=""css/main.css"" />
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(4, result.TotalCssFiles);
        // Only dead-partial.css should be unused
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("css/dead-partial.css", result.UnusedCssFiles[0].RelativePath);
        Assert.Empty(result.AmbiguousCssFiles);
    }

    [Fact]
    public async Task ScanProjectAsync_JavaScriptReferences_ResolvesExactAndDynamicPatterns()
    {
        // Arrange
        var assetsDir = Path.Combine(_testRoot, "assets");
        var themesDir = Path.Combine(_testRoot, "themes");
        Directory.CreateDirectory(assetsDir);
        Directory.CreateDirectory(themesDir);

        await File.WriteAllTextAsync(Path.Combine(assetsDir, "dynamic.css"), ".dynamic-box { display: flex; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "component.css"), ".component-btn { padding: 4px; }");
        await File.WriteAllTextAsync(Path.Combine(themesDir, "dark.css"), ".theme-dark { background: #000; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "dead.css"), ".dead-file { opacity: 0; }");

        var jsDir = Path.Combine(_testRoot, "js");
        Directory.CreateDirectory(jsDir);

        await File.WriteAllTextAsync(Path.Combine(jsDir, "app.js"), @"
            // 1. ES import
            import './component.css';

            // 2. String literal ending in .css
            const sheet = 'assets/dynamic.css';

            // 3. Dynamic template literal
            const themePath = `/themes/${themeName}.css`;
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(4, result.TotalCssFiles);

        // dead.css is unused
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("dead.css", result.UnusedCssFiles[0].RelativePath);

        // dark.css was matched dynamically via template literal -> Ambiguous!
        Assert.Single(result.AmbiguousCssFiles);
        var ambiguous = result.AmbiguousCssFiles[0];
        Assert.Equal("themes/dark.css", ambiguous.RelativePath);
        Assert.Equal(CssFileReferenceStatus.Ambiguous, ambiguous.Status);
        Assert.Contains(ambiguous.MatchedByFiles, f => f.EndsWith("app.js"));
    }

    [Fact]
    public async Task ScanProjectAsync_ZeroClassSelectors_DetectedAsUnusedWhenUnreferencedAndReferencedWhenLinked()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        Directory.CreateDirectory(cssDir);

        // CSS with zero class selectors (only tag/element selector)
        await File.WriteAllTextAsync(Path.Combine(cssDir, "reset.css"), "body { margin: 0; padding: 0; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "empty.css"), "/* empty stylesheet */");

        // HTML references only reset.css
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "index.html"), @"
            <link rel=""stylesheet"" href=""css/reset.css"" />
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(2, result.TotalCssFiles);
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("css/empty.css", result.UnusedCssFiles[0].RelativePath);
        Assert.Equal(0, result.UnusedCssFiles[0].SelectorCount);
    }

    [Fact]
    public async Task ScanProjectAsync_RespectsScanOptions_ExcludesVendorAndPatternFiles()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        var vendorDir = Path.Combine(_testRoot, "node_modules", "pkg");
        Directory.CreateDirectory(cssDir);
        Directory.CreateDirectory(vendorDir);

        await File.WriteAllTextAsync(Path.Combine(cssDir, "site.css"), ".site { color: green; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "bootstrap.css"), ".btn { color: white; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "bundle.min.css"), ".minified { color: red; }");
        await File.WriteAllTextAsync(Path.Combine(vendorDir, "vendor.css"), ".vendor { color: yellow; }");

        var options = new ScanOptions
        {
            ExcludedFolders = ["node_modules"],
            ExcludedFilePatterns = ["bootstrap*.css", "*.min.css"]
        };

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot, options);

        // Assert
        // Only site.css should be counted in TotalCssFiles
        Assert.Equal(1, result.TotalCssFiles);
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("css/site.css", result.UnusedCssFiles[0].RelativePath);
    }

    [Fact]
    public async Task ScanProjectAsync_SharedBasenamesAcrossFolders_ClassifiedAsAmbiguousWhenReferencedAmbiguously()
    {
        // Arrange
        var folderA = Path.Combine(_testRoot, "admin", "css");
        var folderB = Path.Combine(_testRoot, "portal", "css");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);

        // Two distinct files sharing the same basename "shared-theme.css"
        await File.WriteAllTextAsync(Path.Combine(folderA, "shared-theme.css"), ".admin-theme { color: purple; }");
        await File.WriteAllTextAsync(Path.Combine(folderB, "shared-theme.css"), ".portal-theme { color: teal; }");

        // Script references "shared-theme.css" by basename only without path
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "app.js"), @"
            const themeName = 'shared-theme.css';
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(2, result.TotalCssFiles);
        Assert.Empty(result.UnusedCssFiles);
        // Both are flagged as Ambiguous because basename cannot be resolved to a single file
        Assert.Equal(2, result.AmbiguousCssFiles.Count);
        Assert.All(result.AmbiguousCssFiles, f => Assert.Equal(CssFileReferenceStatus.Ambiguous, f.Status));
    }

    [Fact]
    public void HtmlRazorScanner_ExtractStylesheetReferences_DetectsLinkAndRazorHelpers()
    {
        // Arrange
        string markup = @"
            <link rel=""stylesheet"" href=""/css/main.css?v=1.0"" />
            <link href=""~/css/theme.css"" rel=""stylesheet"" />
            <link asp-href-include=""~/css/*.css"" asp-href-exclude=""~/css/*.min.css"" />
            @Styles.Render(""~/Content/bundle.css"")
            <a href=""@Url.Content(""~/css/inline.css"")"">Link</a>
        ";

        // Act
        var refs = _htmlScanner.ExtractStylesheetReferences(markup, "test.cshtml").ToList();

        // Assert
        Assert.Contains(refs, r => r.ReferencePath == "/css/main.css?v=1.0" && r.Kind == StylesheetReferenceKind.Exact);
        Assert.Contains(refs, r => r.ReferencePath == "~/css/theme.css" && r.Kind == StylesheetReferenceKind.Exact);
        Assert.Contains(refs, r => r.ReferencePath == "~/css/*.css" && r.Kind == StylesheetReferenceKind.Glob && r.ExcludePattern == "~/css/*.min.css");
        Assert.Contains(refs, r => r.ReferencePath == "~/Content/bundle.css" && r.Kind == StylesheetReferenceKind.Exact);
        Assert.Contains(refs, r => r.ReferencePath == "~/css/inline.css" && r.Kind == StylesheetReferenceKind.Exact);
    }

    [Fact]
    public void CssParser_ExtractImportReferences_ExtractsAllImportVariants()
    {
        // Arrange
        string css = @"
            @import ""reset.css"";
            @import 'variables.css';
            @import url(""layout.css?v=2"");
            @import url('theme.css');
            @import url(utilities.css);
            @import ""responsive.css"" screen and (max-width: 600px);
        ";

        // Act
        var imports = _cssParser.ExtractImportReferences(css, "style.css").ToList();

        // Assert
        var paths = imports.Select(i => i.ReferencePath).ToList();
        Assert.Contains("reset.css", paths);
        Assert.Contains("variables.css", paths);
        Assert.Contains("layout.css", paths);
        Assert.Contains("theme.css", paths);
        Assert.Contains("utilities.css", paths);
        Assert.Contains("responsive.css", paths);
    }

    [Fact]
    public void JsScanner_ExtractStylesheetReferences_ExtractsImportsAndLiterals()
    {
        // Arrange
        string js = @"
            import './components/modal.css';
            const css1 = 'styles/header.css';
            const dynamic = `/themes/${theme}.css`;
        ";

        // Act
        var refs = _jsScanner.ExtractStylesheetReferences(js, "app.js").ToList();

        // Assert
        Assert.Contains(refs, r => r.ReferencePath == "./components/modal.css" && r.Kind == StylesheetReferenceKind.Exact);
        Assert.Contains(refs, r => r.ReferencePath == "styles/header.css" && r.Kind == StylesheetReferenceKind.Exact);
        Assert.Contains(refs, r => r.Kind == StylesheetReferenceKind.Dynamic);
    }
}
