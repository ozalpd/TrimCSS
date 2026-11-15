using TrimCSS.Engine;
using Xunit;

namespace TrimCSS.Tests;

public class ProjectScannerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ProjectScanner _scanner;

    public ProjectScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"TrimCSS_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _scanner = new ProjectScanner();
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
    public async Task ScanProjectAsync_EndToEndMockProject_CategorizesSelectorsCorrectly()
    {
        // Arrange
        // 1. Stylesheets
        var cssDir = Path.Combine(_testRoot, "css");
        Directory.CreateDirectory(cssDir);

        await File.WriteAllTextAsync(Path.Combine(cssDir, "site.css"), @"
            /* 1. Used exact match via HTML */
            .header-nav { display: flex; }

            /* 2. Used exact match via Razor */
            .btn-active { background: blue; }

            /* 3. Used exact match via JS */
            .modal-visible { opacity: 1; }

            /* 4. Ambiguous via JS template literal prefix (`alert-${type}`) */
            .alert-danger { color: red; }

            /* 5. Definitively unused */
            .dead-promo-banner { display: none; }
            .legacy-sidebar-widget { float: left; }

            /* 6. Pure ID/element selector (no target classes) */
            #main-layout-root { margin: 0; }
            h1 { font-size: 2rem; }
        ");

        // Minified CSS - should be ignored
        await File.WriteAllTextAsync(Path.Combine(cssDir, "bundle.min.css"), @"
            .should-be-ignored-minified { color: yellow; }
        ");

        // 2. HTML file
        var htmlDir = Path.Combine(_testRoot, "public");
        Directory.CreateDirectory(htmlDir);

        await File.WriteAllTextAsync(Path.Combine(htmlDir, "index.html"), @"
            <nav class=""header-nav"">
                <span>Brand</span>
            </nav>
        ");

        // 3. Razor file
        var viewsDir = Path.Combine(_testRoot, "Views", "Home");
        Directory.CreateDirectory(viewsDir);

        await File.WriteAllTextAsync(Path.Combine(viewsDir, "Index.cshtml"), @"
            <button class=""@(isActive ? ""btn-active"" : ""btn-disabled"")"">
                Click
            </button>
        ");

        // 4. JavaScript file
        var jsDir = Path.Combine(_testRoot, "js");
        Directory.CreateDirectory(jsDir);

        await File.WriteAllTextAsync(Path.Combine(jsDir, "app.js"), @"
            modal.classList.add('modal-visible');
            const alertClass = `alert-${severity}`;
        ");

        // 5. Ignored directories (bin, obj, node_modules)
        var binDir = Path.Combine(_testRoot, "bin", "Debug");
        Directory.CreateDirectory(binDir);
        await File.WriteAllTextAsync(Path.Combine(binDir, "leak.css"), ".leak-from-bin { color: red; }");

        var objDir = Path.Combine(_testRoot, "obj");
        Directory.CreateDirectory(objDir);
        await File.WriteAllTextAsync(Path.Combine(objDir, "leak.css"), ".leak-from-obj { color: red; }");

        var nodeModulesDir = Path.Combine(_testRoot, "node_modules", "pkg");
        Directory.CreateDirectory(nodeModulesDir);
        await File.WriteAllTextAsync(Path.Combine(nodeModulesDir, "vendor.css"), ".leak-from-nm { color: red; }");

        // Act
        var result = await _scanner.ScanProjectAsync(_testRoot);

        // Assert
        // Verified 8 selectors in site.css (bundle.min.css, bin, obj, node_modules ignored)
        Assert.Equal(8, result.TotalCssSelectors);
        Assert.True(result.ScannedFilesCount >= 4); // site.css, index.html, Index.cshtml, app.js
        Assert.True(result.ExecutionTime > TimeSpan.Zero);

        // Unused selectors
        var unused = result.UnusedSelectors.Select(s => s.SelectorText).ToList();
        Assert.Contains(".dead-promo-banner", unused);
        Assert.Contains(".legacy-sidebar-widget", unused);
        Assert.DoesNotContain(".header-nav", unused);
        Assert.DoesNotContain(".btn-active", unused);
        Assert.DoesNotContain(".modal-visible", unused);
        Assert.DoesNotContain(".alert-danger", unused);
        Assert.DoesNotContain("#main-layout-root", unused);

        // Ambiguous selectors (matched via dynamic JS template prefix `alert-`)
        var ambiguous = result.AmbiguousSelectors.Select(s => s.SelectorText).ToList();
        Assert.Contains(".alert-danger", ambiguous);
        Assert.DoesNotContain(".dead-promo-banner", ambiguous);

        // Neither unused nor ambiguous contains pure ID or tag selectors
        Assert.DoesNotContain(result.UnusedSelectors, s => s.SelectorText == "#main-layout-root");
        Assert.DoesNotContain(result.AmbiguousSelectors, s => s.SelectorText == "#main-layout-root");
        Assert.DoesNotContain(result.UnusedSelectors, s => s.SelectorText == "h1");
        Assert.DoesNotContain(result.AmbiguousSelectors, s => s.SelectorText == "h1");
    }

    [Fact]
    public async Task ScanProjectAsync_WithMissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _scanner.ScanProjectAsync(nonExistent));
    }

    [Fact]
    public async Task ScanProjectAsync_WithNullOrEmptyPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _scanner.ScanProjectAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _scanner.ScanProjectAsync(null!));
    }

    [Fact]
    public async Task ScanProjectAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _scanner.ScanProjectAsync(_testRoot, cts.Token));
    }
}
