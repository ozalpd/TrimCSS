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

    [Fact]
    public async Task ScanProjectAsync_MinifiedCounterpartReferenced_MarksSourceCssAsReferenced()
    {
        // Arrange
        var contentDir = Path.Combine(_testRoot, "Content");
        Directory.CreateDirectory(contentDir);
        await File.WriteAllTextAsync(Path.Combine(contentDir, "Site.css"), ".body-content { padding: 15px; }");
        await File.WriteAllTextAsync(Path.Combine(contentDir, "Site.min.css"), ".body-content{padding:15px}");

        var viewsDir = Path.Combine(_testRoot, "Views", "Shared");
        Directory.CreateDirectory(viewsDir);
        await File.WriteAllTextAsync(Path.Combine(viewsDir, "_Layout.cshtml"), @"
            <!DOCTYPE html>
            <html>
            <head>
                <link rel=""stylesheet"" href=""~/Content/Site.min.css?v=1"" />
            </head>
            <body>
                <div class=""body-content"">Hello</div>
            </body>
            </html>
        ");

        // Act - uses default ScanOptions where *.min.css is excluded
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(1, result.TotalCssFiles);
        Assert.Empty(result.UnusedCssFiles);
        Assert.Empty(result.AmbiguousCssFiles);
    }

    [Fact]
    public async Task ScanProjectAsync_MinifiedCounterpartExistsOnDiskButUnreferenced_KeepsSourceCssUnused()
    {
        // Arrange
        var contentDir = Path.Combine(_testRoot, "Content");
        Directory.CreateDirectory(contentDir);
        await File.WriteAllTextAsync(Path.Combine(contentDir, "orphan.css"), ".orphan-class { color: red; }");
        await File.WriteAllTextAsync(Path.Combine(contentDir, "orphan.min.css"), ".orphan-class{color:red}");

        // Act - neither is referenced
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(1, result.TotalCssFiles);
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("Content/orphan.css", result.UnusedCssFiles[0].RelativePath);
        Assert.Equal(CssFileReferenceStatus.Unused, result.UnusedCssFiles[0].Status);
        Assert.Empty(result.AmbiguousCssFiles);
    }

    [Fact]
    public async Task ScanProjectAsync_MinifiedCounterpartImportsPartial_PropagatesReferencedTransitively()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        var partialsDir = Path.Combine(cssDir, "partials");
        Directory.CreateDirectory(partialsDir);

        await File.WriteAllTextAsync(Path.Combine(cssDir, "main.css"), @"
            @import ""partials/tokens.css"";
            .main-theme { font-size: 14px; }
        ");
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "tokens.css"), ":root { --brand: #7c5cfc; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "main.min.css"), ".main-theme{font-size:14px}");

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "index.html"), @"
            <!DOCTYPE html>
            <html>
            <head>
                <link rel=""stylesheet"" href=""css/main.min.css"" />
            </head>
            <body>
                <div class=""main-theme"">Content</div>
            </body>
            </html>
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(2, result.TotalCssFiles);
        Assert.Empty(result.UnusedCssFiles);
        Assert.Empty(result.AmbiguousCssFiles);
    }

    [Fact]
    public async Task ScanProjectAsync_BundleConfigJson_MarksInputCssFilesReferencedWhenOutputReferenced()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        Directory.CreateDirectory(cssDir);

        await File.WriteAllTextAsync(Path.Combine(cssDir, "part1.css"), ".p1 { color: red; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "part2.css"), ".p2 { color: blue; }");
        await File.WriteAllTextAsync(Path.Combine(cssDir, "dead.css"), ".dead { color: gray; }");

        // bundleconfig.json mapping part1.css and part2.css to site.bundle.min.css
        string bundleConfig = @"[
            {
                ""outputFileName"": ""css/site.bundle.min.css"",
                ""inputFiles"": [
                    ""css/part1.css"",
                    ""css/part2.css""
                ]
            }
        ]";
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "bundleconfig.json"), bundleConfig);

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "index.html"), @"
            <!DOCTYPE html>
            <html>
            <head>
                <link rel=""stylesheet"" href=""css/site.bundle.min.css"" />
            </head>
            <body>
                <div class=""p1"">Part 1</div>
            </body>
            </html>
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(3, result.TotalCssFiles);
        Assert.Single(result.UnusedCssFiles);
        Assert.Equal("css/dead.css", result.UnusedCssFiles[0].RelativePath);
        Assert.Equal(CssFileReferenceStatus.Unused, result.UnusedCssFiles[0].Status);
        Assert.Empty(result.AmbiguousCssFiles);
    }

    [Fact]
    public async Task ScanProjectAsync_CalculatesSelectorUsageRatePerCssFile()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        var themesDir = Path.Combine(_testRoot, "themes");
        Directory.CreateDirectory(cssDir);
        Directory.CreateDirectory(themesDir);

        // main.css: 2 used, 1 unused, 1 ambiguous -> 4 total, 50% usage
        await File.WriteAllTextAsync(Path.Combine(cssDir, "main.css"), @"
            .used-one { color: black; }
            .used-two { color: white; }
            .unused-one { color: gray; }
            .alert-danger { color: red; }
        ");

        // empty.css: 0 selectors -> 0% usage (no NaN)
        await File.WriteAllTextAsync(Path.Combine(cssDir, "empty.css"), "/* empty */");

        // orphan.css: 2 unused selectors -> 0% usage
        await File.WriteAllTextAsync(Path.Combine(cssDir, "orphan.css"), @"
            .dead-a { display: none; }
            .dead-b { display: none; }
        ");

        // themes/dark.css: matched ambiguously via JS template literal, 1 used selector -> 100% usage
        await File.WriteAllTextAsync(Path.Combine(themesDir, "dark.css"), ".theme-bg { background: #111; }");

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "index.html"), @"
            <link rel=""stylesheet"" href=""css/main.css"" />
            <link rel=""stylesheet"" href=""css/empty.css"" />
            <div class=""used-one used-two theme-bg""></div>
        ");

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "app.js"), @"
            const cls = `alert-${type}`;
            const themePath = `/themes/${name}.css`;
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(4, result.TotalCssFiles);
        Assert.Equal(4, result.AllCssFiles.Count);

        var mainFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "css/main.css");
        Assert.Equal(CssFileReferenceStatus.Referenced, mainFile.Status);
        Assert.Equal(4, mainFile.TotalSelectors);
        Assert.Equal(4, mainFile.SelectorCount);
        Assert.Equal(2, mainFile.UsedSelectors);
        Assert.Equal(1, mainFile.UnusedSelectors);
        Assert.Equal(1, mainFile.AmbiguousSelectors);
        Assert.Equal(50.0, mainFile.UsagePercentage, precision: 2);

        var emptyFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "css/empty.css");
        Assert.Equal(CssFileReferenceStatus.Referenced, emptyFile.Status);
        Assert.Equal(0, emptyFile.TotalSelectors);
        Assert.Equal(0, emptyFile.UsedSelectors);
        Assert.Equal(0, emptyFile.UnusedSelectors);
        Assert.Equal(0.0, emptyFile.UsagePercentage);

        var orphanFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "css/orphan.css");
        Assert.Equal(CssFileReferenceStatus.Unused, orphanFile.Status);
        Assert.Equal(2, orphanFile.TotalSelectors);
        Assert.Equal(0, orphanFile.UsedSelectors);
        Assert.Equal(2, orphanFile.UnusedSelectors);
        Assert.Equal(0.0, orphanFile.UsagePercentage);

        var darkFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "themes/dark.css");
        Assert.Equal(CssFileReferenceStatus.Ambiguous, darkFile.Status);
        Assert.Equal(1, darkFile.TotalSelectors);
        Assert.Equal(1, darkFile.UsedSelectors);
        Assert.Equal(0, darkFile.UnusedSelectors);
        Assert.Equal(100.0, darkFile.UsagePercentage, precision: 2);
    }

    [Fact]
    public async Task ScanProjectAsync_PureElementSelectors_CountAsUsedInFileUsageRate()
    {
        // Arrange
        var cssDir = Path.Combine(_testRoot, "css");
        Directory.CreateDirectory(cssDir);
        await File.WriteAllTextAsync(Path.Combine(cssDir, "reset.css"), @"
            body { margin: 0; }
            h1 { font-size: 2rem; }
        ");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "index.html"), @"
            <link rel=""stylesheet"" href=""css/reset.css"" />
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        var resetFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "css/reset.css");
        Assert.Equal(CssFileReferenceStatus.Referenced, resetFile.Status);
        Assert.Equal(2, resetFile.TotalSelectors);
        Assert.Equal(2, resetFile.UsedSelectors);
        Assert.Equal(0, resetFile.UnusedSelectors);
        Assert.Equal(100.0, resetFile.UsagePercentage, precision: 2);
    }

    [Fact]
    public void UnusedCssFileInfo_UsagePercentage_HandlesZeroAndFractionalCounts()
    {
        var emptyInfo = new UnusedCssFileInfo { TotalSelectors = 0, UsedSelectors = 0 };
        Assert.Equal(0.0, emptyInfo.UsagePercentage);

        var partialInfo = new UnusedCssFileInfo { TotalSelectors = 3, UsedSelectors = 1 };
        Assert.Equal(100.0 / 3.0, partialInfo.UsagePercentage, precision: 5);
    }

    [Fact]
    public async Task ScanProjectAsync_MvcBundleConfigCs_MarksBundledAndImportedCssAsReferenced()
    {
        // Arrange
        var appStartDir = Path.Combine(_testRoot, "App_Start");
        var baseThemeDir = Path.Combine(_testRoot, "Content", "themes", "base");
        var viewsDir = Path.Combine(_testRoot, "Views", "Shared");
        Directory.CreateDirectory(appStartDir);
        Directory.CreateDirectory(baseThemeDir);
        Directory.CreateDirectory(viewsDir);

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "bootstrap.css"), ".btn { display: inline-block; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "site.css"), ".site { padding: 8px; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "dead.css"), ".dead { color: gray; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "unrendered.css"), ".unrendered { color: red; }");

        await File.WriteAllTextAsync(Path.Combine(baseThemeDir, "all.css"), "@import \"base.css\";");
        await File.WriteAllTextAsync(Path.Combine(baseThemeDir, "base.css"), "@import \"core.css\";");
        await File.WriteAllTextAsync(Path.Combine(baseThemeDir, "core.css"), ".ui-widget { font-size: 1em; }");

        await File.WriteAllTextAsync(Path.Combine(appStartDir, "BundleConfig.cs"), @"
            using System.Web.Optimization;

            public class BundleConfig
            {
                public static void RegisterBundles(BundleCollection bundles)
                {
                    bundles.Add(new StyleBundle(""~/Content/css"").Include(
                              ""~/Content/bootstrap.css"",
                              ""~/Content/site.css"")
                              .Include(""~/Content/themes/base/all.css"", new CssRewriteUrlTransform()));

                    bundles.Add(new StyleBundle(""~/Content/unused-bundle"").Include(
                              ""~/Content/unrendered.css""));

                    // Commented-out bundle must stay Unused:
                    // bundles.Add(new StyleBundle(""~/Content/dead"").Include(""~/Content/dead.css""));
                }
            }
        ");

        await File.WriteAllTextAsync(Path.Combine(viewsDir, "_Layout.cshtml"), @"
            <html>
            <head>
                @Styles.Render(""~/Content/css"")
            </head>
            <body><div class=""btn site ui-widget"">Hi</div></body>
            </html>
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(7, result.TotalCssFiles);
        Assert.Equal(2, result.UnusedCssFiles.Count);
        Assert.Contains(result.UnusedCssFiles, f => f.RelativePath == "Content/dead.css");
        Assert.Contains(result.UnusedCssFiles, f => f.RelativePath == "Content/unrendered.css");

        Assert.Equal(CssFileReferenceStatus.Referenced, Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/bootstrap.css").Status);
        Assert.Equal(CssFileReferenceStatus.Referenced, Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/site.css").Status);
        Assert.Equal(CssFileReferenceStatus.Referenced, Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/themes/base/all.css").Status);
        Assert.Equal(CssFileReferenceStatus.Referenced, Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/themes/base/base.css").Status);
        Assert.Equal(CssFileReferenceStatus.Referenced, Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/themes/base/core.css").Status);
    }

    [Fact]
    public async Task ScanProjectAsync_MvcBundleConfigCs_SupportsIncludeDirectoryVersionWildcardAndVariables()
    {
        // Arrange
        var appStartDir = Path.Combine(_testRoot, "App_Start");
        var themeSubDir = Path.Combine(_testRoot, "Content", "theme", "sub");
        var viewsDir = Path.Combine(_testRoot, "Views");
        Directory.CreateDirectory(appStartDir);
        Directory.CreateDirectory(themeSubDir);
        Directory.CreateDirectory(viewsDir);

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "jquery-ui-1.13.2.css"), ".ui-slider { display: block; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "theme", "theme-root.css"), ".theme-root { color: black; }");
        await File.WriteAllTextAsync(Path.Combine(themeSubDir, "theme-nested.css"), ".theme-nested { color: white; }");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Content", "orphan.css"), ".orphan { display: none; }");

        await File.WriteAllTextAsync(Path.Combine(appStartDir, "BundleConfig.cs"), @"
            public class BundleConfig
            {
                public static void RegisterBundles(BundleCollection bundles)
                {
                    var themeBundle = new StyleBundle(""~/Content/themecss"");
                    themeBundle.Include(""~/Content/jquery-ui-{version}.css"");
                    themeBundle.IncludeDirectory(""~/Content/theme"", ""*.css"", true);
                    bundles.Add(themeBundle);
                }
            }
        ");

        await File.WriteAllTextAsync(Path.Combine(viewsDir, "Index.cshtml"), @"
            @Styles.RenderFormat(""<link href=\""{0}\"" rel=\""stylesheet\"" />"", ""~/Content/themecss"")
        ");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        Assert.Equal(4, result.TotalCssFiles);
        var unused = Assert.Single(result.UnusedCssFiles);
        Assert.Equal("Content/orphan.css", unused.RelativePath);
    }

    [Fact]
    public async Task ScanProjectAsync_PopulatesImportedCssFilesForEachCssFile()
    {
        // Arrange
        var baseDir = Path.Combine(_testRoot, "Content", "themes", "base");
        Directory.CreateDirectory(baseDir);

        await File.WriteAllTextAsync(Path.Combine(baseDir, "all.css"), @"
            @import ""base.css"";
            @import url(""accordion.css"");
        ");
        await File.WriteAllTextAsync(Path.Combine(baseDir, "base.css"), @"
            @import ""core.css"";
        ");
        await File.WriteAllTextAsync(Path.Combine(baseDir, "accordion.css"), ".ui-accordion { display: block; }");
        await File.WriteAllTextAsync(Path.Combine(baseDir, "core.css"), ".ui-helper { margin: 0; }");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        var allFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/themes/base/all.css");
        Assert.Equal(["Content/themes/base/base.css", "Content/themes/base/accordion.css"], allFile.ImportedCssFiles);

        var baseFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/themes/base/base.css");
        Assert.Equal(["Content/themes/base/core.css"], baseFile.ImportedCssFiles);

        var coreFile = Assert.Single(result.AllCssFiles, f => f.RelativePath == "Content/themes/base/core.css");
        Assert.Empty(coreFile.ImportedCssFiles);
    }
}
