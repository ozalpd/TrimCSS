using TrimCSS.Engine;
using Xunit;

namespace TrimCSS.Tests;

public class CssDiffGeneratorTests
{
    [Fact]
    public void GenerateDiff_IdenticalContent_ReturnsHasChangesFalse()
    {
        // Arrange
        string css = @"
.header {
    display: flex;
    padding: 10px;
}

.footer {
    color: #888;
}
";

        // Act
        var result = CssDiffGenerator.GenerateDiff(css, css);

        // Assert
        Assert.False(result.HasChanges);
        Assert.Equal(0, result.AddedLineCount);
        Assert.Equal(0, result.RemovedLineCount);
        Assert.NotEmpty(result.Lines);
        Assert.DoesNotContain(result.Lines, l => l.Kind == DiffLineKind.Removed);
        Assert.DoesNotContain(result.Lines, l => l.Kind == DiffLineKind.Added);
        Assert.All(result.Lines, l => Assert.Equal(DiffLineKind.Unchanged, l.Kind));
    }

    [Fact]
    public void GenerateDiff_SingleRuleRemoved_EmitsRemovedLinesAndPreservesLineNumbers()
    {
        // Arrange
        string original = @"
.card {
    background: #fff;
}

.dead-rule {
    display: none;
    opacity: 0;
}

.button {
    cursor: pointer;
}
".Trim();

        string modified = @"
.card {
    background: #fff;
}

.button {
    cursor: pointer;
}
".Trim();

        // Act - use negative context lines to inspect full uncollapsed lines
        var result = CssDiffGenerator.GenerateDiff(original, modified, contextLines: -1);

        // Assert
        Assert.True(result.HasChanges);
        Assert.Equal(0, result.AddedLineCount);
        Assert.True(result.RemovedLineCount >= 4);

        // Check that removed lines correspond to .dead-rule block and have null ModifiedLineNumber
        var removedLines = result.Lines.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.Contains(removedLines, l => l.Text.Contains(".dead-rule {"));
        Assert.Contains(removedLines, l => l.Text.Contains("display: none;"));
        Assert.Contains(removedLines, l => l.Text.Contains("opacity: 0;"));
        Assert.All(removedLines, l =>
        {
            Assert.NotNull(l.OriginalLineNumber);
            Assert.Null(l.ModifiedLineNumber);
        });

        // The unchanged line after the removal should show original line shifted to new line
        var buttonLine = result.Lines.First(l => l.Text.Contains(".button {"));
        Assert.Equal(DiffLineKind.Unchanged, buttonLine.Kind);
        Assert.Equal(10, buttonLine.OriginalLineNumber);
        Assert.Equal(5, buttonLine.ModifiedLineNumber);
    }

    [Fact]
    public void GenerateDiff_ModifiedSelectorList_EmitsRemovedThenAdded()
    {
        // Arrange
        string original = @".btn, .dead-btn {
    display: inline-block;
    padding: 6px 12px;
}";

        string modified = @".btn {
    display: inline-block;
    padding: 6px 12px;
}";

        // Act
        var result = CssDiffGenerator.GenerateDiff(original, modified);

        // Assert
        Assert.True(result.HasChanges);
        Assert.Equal(1, result.RemovedLineCount);
        Assert.Equal(1, result.AddedLineCount);

        // First line is Removed
        var firstLine = result.Lines[0];
        Assert.Equal(DiffLineKind.Removed, firstLine.Kind);
        Assert.Equal(".btn, .dead-btn {", firstLine.Text);
        Assert.Equal(1, firstLine.OriginalLineNumber);
        Assert.Null(firstLine.ModifiedLineNumber);

        // Second line is Added (Removed then Added)
        var secondLine = result.Lines[1];
        Assert.Equal(DiffLineKind.Added, secondLine.Kind);
        Assert.Equal(".btn {", secondLine.Text);
        Assert.Null(secondLine.OriginalLineNumber);
        Assert.Equal(1, secondLine.ModifiedLineNumber);

        // Subsequent lines are Unchanged
        var thirdLine = result.Lines[2];
        Assert.Equal(DiffLineKind.Unchanged, thirdLine.Kind);
        Assert.Equal(2, thirdLine.OriginalLineNumber);
        Assert.Equal(2, thirdLine.ModifiedLineNumber);
    }

    [Fact]
    public void GenerateDiff_ContextCollapsing_CollapsesLongUnchangedSpans()
    {
        // Arrange: 25-line stylesheet with changes near top (line 3) and near bottom (line 22)
        var origLines = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            origLines.Add($".item-{i:D2} {{ padding: {i}px; }}");
        }
        var modLines = new List<string>(origLines);

        // Change at line 3 and line 22 (1-based index)
        modLines[2] = ".item-03-modified { padding: 3px; }";
        modLines[21] = ".item-22-modified { padding: 22px; }";

        string original = string.Join("\n", origLines);
        string modified = string.Join("\n", modLines);

        // Act - retain 2 context lines
        var result = CssDiffGenerator.GenerateDiff(original, modified, contextLines: 2);

        // Assert
        Assert.True(result.HasChanges);
        Assert.Equal(2, result.AddedLineCount);
        Assert.Equal(2, result.RemovedLineCount);

        // A CollapsedMarker must be emitted between the two changed regions
        var marker = result.Lines.FirstOrDefault(l => l.Kind == DiffLineKind.CollapsedMarker);
        Assert.NotNull(marker);
        Assert.Null(marker.OriginalLineNumber);
        Assert.Null(marker.ModifiedLineNumber);

        // Lines 6 to 19 (14 lines) are outside the 2 context lines of change at line 3 and line 22
        Assert.Equal("... 14 unchanged lines ...", marker.Text);

        // Unchanged lines immediately before and after line 3 should be kept
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Unchanged && l.OriginalLineNumber == 2);
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Unchanged && l.OriginalLineNumber == 4);
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Unchanged && l.OriginalLineNumber == 5);

        // Unchanged lines outside context (e.g. line 10) must be collapsed away
        Assert.DoesNotContain(result.Lines, l => l.OriginalLineNumber == 10);
    }

    [Fact]
    public void GenerateDiff_NegativeContextLines_ReturnsFullDiffWithoutCollapsing()
    {
        // Arrange
        var origLines = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            origLines.Add($".item-{i:D2} {{ padding: {i}px; }}");
        }
        var modLines = new List<string>(origLines);
        modLines[2] = ".item-03-mod { color: red; }";
        modLines[21] = ".item-22-mod { color: blue; }";

        string original = string.Join("\n", origLines);
        string modified = string.Join("\n", modLines);

        // Act
        var result = CssDiffGenerator.GenerateDiff(original, modified, contextLines: -1);

        // Assert
        Assert.True(result.HasChanges);
        Assert.DoesNotContain(result.Lines, l => l.Kind == DiffLineKind.CollapsedMarker);
        // All lines from 1 to 25 are represented (23 unchanged + 2 removed + 2 added = 27 lines)
        Assert.Equal(27, result.Lines.Count);
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Unchanged && l.OriginalLineNumber == 10);
    }

    [Fact]
    public void GenerateDiff_EmptyAndCrlfInputs_HandlesEdgeCasesCleanly()
    {
        // 1. Both empty strings
        var emptyResult = CssDiffGenerator.GenerateDiff("", "");
        Assert.False(emptyResult.HasChanges);
        Assert.Empty(emptyResult.Lines);
        Assert.Equal(0, emptyResult.AddedLineCount);
        Assert.Equal(0, emptyResult.RemovedLineCount);

        // 2. Full-file addition
        var addResult = CssDiffGenerator.GenerateDiff("", ".btn { color: red; }");
        Assert.True(addResult.HasChanges);
        Assert.Equal(1, addResult.AddedLineCount);
        Assert.Equal(0, addResult.RemovedLineCount);
        Assert.Single(addResult.Lines);
        Assert.Equal(DiffLineKind.Added, addResult.Lines[0].Kind);
        Assert.Equal(1, addResult.Lines[0].ModifiedLineNumber);
        Assert.Null(addResult.Lines[0].OriginalLineNumber);

        // 3. Full-file deletion
        var delResult = CssDiffGenerator.GenerateDiff(".btn { color: red; }", "");
        Assert.True(delResult.HasChanges);
        Assert.Equal(0, delResult.AddedLineCount);
        Assert.Equal(1, delResult.RemovedLineCount);
        Assert.Single(delResult.Lines);
        Assert.Equal(DiffLineKind.Removed, delResult.Lines[0].Kind);
        Assert.Equal(1, delResult.Lines[0].OriginalLineNumber);
        Assert.Null(delResult.Lines[0].ModifiedLineNumber);

        // 4. Windows CRLF line endings
        string crlfOriginal = ".card {\r\n    color: red;\r\n}\r\n";
        string crlfModified = ".card {\r\n    color: blue;\r\n}\r\n";
        var crlfResult = CssDiffGenerator.GenerateDiff(crlfOriginal, crlfModified);

        Assert.True(crlfResult.HasChanges);
        Assert.Equal(1, crlfResult.RemovedLineCount);
        Assert.Equal(1, crlfResult.AddedLineCount);

        // Ensure no \r or \n character leaks into line text
        Assert.All(crlfResult.Lines, l =>
        {
            Assert.DoesNotContain("\r", l.Text);
            Assert.DoesNotContain("\n", l.Text);
        });
    }
}
