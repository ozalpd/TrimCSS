namespace TrimCSS.Engine;

/// <summary>
/// Specifies the classification of a line in a CSS diff.
/// </summary>
public enum DiffLineKind
{
    /// <summary>
    /// Line exists in both original and modified content.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Line was removed from original content.
    /// </summary>
    Removed,

    /// <summary>
    /// Line was added in modified content.
    /// </summary>
    Added,

    /// <summary>
    /// Summary marker representing collapsed unchanged lines.
    /// </summary>
    CollapsedMarker
}

/// <summary>
/// Represents a single line entry within a CSS diff result.
/// </summary>
public record CssDiffLine(
    int? OriginalLineNumber = null,
    int? ModifiedLineNumber = null,
    DiffLineKind Kind = DiffLineKind.Unchanged,
    string Text = "")
{
    public int? OriginalLineNumber { get; init; } = OriginalLineNumber;
    public int? ModifiedLineNumber { get; init; } = ModifiedLineNumber;
    public DiffLineKind Kind { get; init; } = Kind;
    public string Text { get; init; } = Text;
}

/// <summary>
/// Represents the result of generating a diff between original and modified CSS stylesheet content.
/// </summary>
public class CssDiffResult
{
    /// <summary>
    /// Gets the collection of diff lines, including unchanged lines, additions, removals, and collapsed markers.
    /// </summary>
    public IReadOnlyList<CssDiffLine> Lines { get; init; } = Array.Empty<CssDiffLine>();

    /// <summary>
    /// Gets the total number of added lines.
    /// </summary>
    public int AddedLineCount { get; init; }

    /// <summary>
    /// Gets the total number of removed lines.
    /// </summary>
    public int RemovedLineCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether any changes (additions or removals) were detected.
    /// </summary>
    public bool HasChanges => AddedLineCount > 0 || RemovedLineCount > 0;

    public CssDiffResult()
    {
    }

    public CssDiffResult(IReadOnlyList<CssDiffLine> lines, int addedLineCount, int removedLineCount)
    {
        Lines = lines ?? Array.Empty<CssDiffLine>();
        AddedLineCount = addedLineCount;
        RemovedLineCount = removedLineCount;
    }
}

/// <summary>
/// Provides line-level Myers diff computation and context collapsing between original and modified CSS content.
/// </summary>
public static class CssDiffGenerator
{
    /// <summary>
    /// Computes a minimal line-level diff between original and modified CSS stylesheet content.
    /// </summary>
    /// <param name="originalContent">The original CSS stylesheet text.</param>
    /// <param name="modifiedContent">The modified CSS stylesheet text.</param>
    /// <param name="contextLines">
    /// Number of unchanged lines to retain before and after each change region.
    /// When negative, all unchanged lines are retained without collapsing.
    /// </param>
    /// <returns>A <see cref="CssDiffResult"/> containing classified lines, statistics, and change indicators.</returns>
    public static CssDiffResult GenerateDiff(string originalContent, string modifiedContent, int contextLines = 3)
    {
        string[] orig = SplitLines(originalContent);
        string[] mod = SplitLines(modifiedContent);

        // Edge case: both empty
        if (orig.Length == 0 && mod.Length == 0)
        {
            return new CssDiffResult([], 0, 0);
        }

        // Edge case: full-file addition
        if (orig.Length == 0)
        {
            var addedLines = new List<CssDiffLine>(mod.Length);
            for (int i = 0; i < mod.Length; i++)
            {
                addedLines.Add(new CssDiffLine(null, i + 1, DiffLineKind.Added, mod[i]));
            }
            return new CssDiffResult(addedLines, mod.Length, 0);
        }

        // Edge case: full-file deletion
        if (mod.Length == 0)
        {
            var removedLines = new List<CssDiffLine>(orig.Length);
            for (int i = 0; i < orig.Length; i++)
            {
                removedLines.Add(new CssDiffLine(i + 1, null, DiffLineKind.Removed, orig[i]));
            }
            return new CssDiffResult(removedLines, 0, orig.Length);
        }

        // Strip common prefix
        int start = 0;
        while (start < orig.Length && start < mod.Length && string.Equals(orig[start], mod[start], StringComparison.Ordinal))
        {
            start++;
        }

        // Strip common suffix
        int endOrig = orig.Length - 1;
        int endMod = mod.Length - 1;
        while (endOrig >= start && endMod >= start && string.Equals(orig[endOrig], mod[endMod], StringComparison.Ordinal))
        {
            endOrig--;
            endMod--;
        }

        // Edge case: identical inputs
        if (start > endOrig && start > endMod)
        {
            var identicalLines = new List<CssDiffLine>(orig.Length);
            for (int i = 0; i < orig.Length; i++)
            {
                identicalLines.Add(new CssDiffLine(i + 1, i + 1, DiffLineKind.Unchanged, orig[i]));
            }
            return new CssDiffResult(identicalLines, 0, 0);
        }

        // Assemble uncollapsed diff lines
        var rawLines = new List<CssDiffLine>();

        // 1. Common prefix lines
        for (int i = 0; i < start; i++)
        {
            rawLines.Add(new CssDiffLine(i + 1, i + 1, DiffLineKind.Unchanged, orig[i]));
        }

        // 2. Middle slice
        if (start > endOrig)
        {
            // Pure insertions in mod
            for (int j = start; j <= endMod; j++)
            {
                rawLines.Add(new CssDiffLine(null, j + 1, DiffLineKind.Added, mod[j]));
            }
        }
        else if (start > endMod)
        {
            // Pure deletions in orig
            for (int i = start; i <= endOrig; i++)
            {
                rawLines.Add(new CssDiffLine(i + 1, null, DiffLineKind.Removed, orig[i]));
            }
        }
        else
        {
            // Compute Myers diff on the middle slices
            int sliceN = endOrig - start + 1;
            int sliceM = endMod - start + 1;

            string[] a = new string[sliceN];
            Array.Copy(orig, start, a, 0, sliceN);

            string[] b = new string[sliceM];
            Array.Copy(mod, start, b, 0, sliceM);

            var ops = ComputeMyersDiff(a, b);

            int opIdx = 0;
            while (opIdx < ops.Count)
            {
                if (ops[opIdx].Kind == DiffLineKind.Unchanged)
                {
                    var op = ops[opIdx];
                    rawLines.Add(new CssDiffLine(start + op.AIndex + 1, start + op.BIndex + 1, DiffLineKind.Unchanged, a[op.AIndex]));
                    opIdx++;
                }
                else
                {
                    // Group contiguous changes so Removed precedes Added
                    var hunkRemoved = new List<int>();
                    var hunkAdded = new List<int>();

                    while (opIdx < ops.Count && ops[opIdx].Kind != DiffLineKind.Unchanged)
                    {
                        if (ops[opIdx].Kind == DiffLineKind.Removed)
                        {
                            hunkRemoved.Add(ops[opIdx].AIndex);
                        }
                        else if (ops[opIdx].Kind == DiffLineKind.Added)
                        {
                            hunkAdded.Add(ops[opIdx].BIndex);
                        }
                        opIdx++;
                    }

                    foreach (int aIdx in hunkRemoved)
                    {
                        rawLines.Add(new CssDiffLine(start + aIdx + 1, null, DiffLineKind.Removed, a[aIdx]));
                    }

                    foreach (int bIdx in hunkAdded)
                    {
                        rawLines.Add(new CssDiffLine(null, start + bIdx + 1, DiffLineKind.Added, b[bIdx]));
                    }
                }
            }
        }

        // 3. Common suffix lines
        int suffixLen = orig.Length - 1 - endOrig;
        for (int i = 0; i < suffixLen; i++)
        {
            int origIdx = endOrig + 1 + i;
            int modIdx = endMod + 1 + i;
            rawLines.Add(new CssDiffLine(origIdx + 1, modIdx + 1, DiffLineKind.Unchanged, orig[origIdx]));
        }

        int addedCount = rawLines.Count(l => l.Kind == DiffLineKind.Added);
        int removedCount = rawLines.Count(l => l.Kind == DiffLineKind.Removed);

        // When contextLines < 0, return all lines uncollapsed
        if (contextLines < 0)
        {
            return new CssDiffResult(rawLines, addedCount, removedCount);
        }

        // Apply context collapsing
        var collapsedLines = ApplyContextCollapsing(rawLines, contextLines);
        return new CssDiffResult(collapsedLines, addedCount, removedCount);
    }

    private static List<CssDiffLine> ApplyContextCollapsing(List<CssDiffLine> rawLines, int contextLines)
    {
        int total = rawLines.Count;
        bool[] keep = new bool[total];

        for (int i = 0; i < total; i++)
        {
            if (rawLines[i].Kind != DiffLineKind.Unchanged)
            {
                keep[i] = true;

                // Retain up to contextLines unchanged lines before
                int count = 0;
                for (int j = i - 1; j >= 0 && count < contextLines; j--)
                {
                    if (rawLines[j].Kind == DiffLineKind.Unchanged)
                    {
                        keep[j] = true;
                        count++;
                    }
                }

                // Retain up to contextLines unchanged lines after
                count = 0;
                for (int j = i + 1; j < total && count < contextLines; j++)
                {
                    if (rawLines[j].Kind == DiffLineKind.Unchanged)
                    {
                        keep[j] = true;
                        count++;
                    }
                }
            }
        }

        var result = new List<CssDiffLine>();
        int idx = 0;
        while (idx < total)
        {
            if (keep[idx])
            {
                result.Add(rawLines[idx]);
                idx++;
            }
            else
            {
                int runStart = idx;
                while (idx < total && !keep[idx])
                {
                    idx++;
                }
                int hiddenCount = idx - runStart;
                result.Add(new CssDiffLine(
                    null,
                    null,
                    DiffLineKind.CollapsedMarker,
                    $"#... {hiddenCount} unchanged lines ...#".Trim('#')));
            }
        }

        return result;
    }

    private static List<(int AIndex, int BIndex, DiffLineKind Kind)> ComputeMyersDiff(string[] a, string[] b)
    {
        int max = a.Length + b.Length;
        int[] v = new int[2 * max + 1];
        var trace = new List<int[]>();

        v[1 + max] = 0;

        for (int d = 0; d <= max; d++)
        {
            int[] vCopy = new int[2 * d + 1];
            Array.Copy(v, -d + max, vCopy, 0, 2 * d + 1);
            trace.Add(vCopy);

            for (int k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[k - 1 + max] < v[k + 1 + max]))
                {
                    x = v[k + 1 + max]; // vertical move (insert)
                }
                else
                {
                    x = v[k - 1 + max] + 1; // horizontal move (delete)
                }

                int y = x - k;

                while (x < a.Length && y < b.Length && string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[k + max] = x;

                if (x >= a.Length && y >= b.Length)
                {
                    return BacktrackMyers(trace, a, b, d, k);
                }
            }
        }

        return [];
    }

    private static List<(int AIndex, int BIndex, DiffLineKind Kind)> BacktrackMyers(
        List<int[]> trace,
        string[] a,
        string[] b,
        int finalD,
        int finalK)
    {
        var ops = new List<(int AIndex, int BIndex, DiffLineKind Kind)>();
        int x = a.Length;
        int y = b.Length;
        int k = finalK;

        for (int d = finalD; d > 0; d--)
        {
            int[] vPrev = trace[d];
            int prevK;

            if (k == -d)
            {
                prevK = k + 1;
            }
            else if (k == d)
            {
                prevK = k - 1;
            }
            else
            {
                int vLeft = vPrev[k - 1 + d];
                int vRight = vPrev[k + 1 + d];
                prevK = (vLeft < vRight) ? k + 1 : k - 1;
            }

            int prevX = vPrev[prevK + d];
            int prevY = prevX - prevK;

            int nextX = (prevK == k - 1) ? prevX + 1 : prevX;
            int nextY = (prevK == k + 1) ? prevY + 1 : prevY;

            // Diagonal matches leading up to (x, y)
            while (x > nextX && y > nextY)
            {
                x--;
                y--;
                ops.Add((x, y, DiffLineKind.Unchanged));
            }

            // Edit step
            if (prevK == k - 1)
            {
                x--;
                ops.Add((x, -1, DiffLineKind.Removed));
            }
            else
            {
                y--;
                ops.Add((-1, y, DiffLineKind.Added));
            }

            k = prevK;
        }

        while (x > 0 && y > 0)
        {
            x--;
            y--;
            ops.Add((x, y, DiffLineKind.Unchanged));
        }

        ops.Reverse();
        return ops;
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }
}
