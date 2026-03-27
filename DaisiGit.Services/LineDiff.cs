namespace DaisiGit.Services;

/// <summary>
/// Simple line-level diff using the Myers diff algorithm (O(ND) approach).
/// Produces a list of diff lines with context.
/// </summary>
public static class LineDiff
{
    public enum LineType { Context, Added, Removed }

    public record DiffLine(LineType Type, string Content, int? OldLineNum, int? NewLineNum);

    /// <summary>
    /// Computes a unified diff between old and new text with context lines.
    /// </summary>
    public static List<DiffLine> Compute(string? oldText, string? newText, int contextLines = 3)
    {
        var oldLines = (oldText ?? "").Split('\n');
        var newLines = (newText ?? "").Split('\n');

        // Compute LCS-based edit script
        var edits = ComputeEdits(oldLines, newLines);

        // Add context around changes
        return AddContext(edits, contextLines);
    }

    private static List<DiffLine> ComputeEdits(string[] oldLines, string[] newLines)
    {
        var n = oldLines.Length;
        var m = newLines.Length;

        // Build LCS table
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                if (oldLines[i] == newLines[j])
                    lcs[i, j] = lcs[i + 1, j + 1] + 1;
                else
                    lcs[i, j] = Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        // Trace through LCS to produce diff
        var result = new List<DiffLine>();
        int oi = 0, ni = 0;

        while (oi < n || ni < m)
        {
            if (oi < n && ni < m && oldLines[oi] == newLines[ni])
            {
                result.Add(new DiffLine(LineType.Context, oldLines[oi], oi + 1, ni + 1));
                oi++;
                ni++;
            }
            else if (ni < m && (oi >= n || lcs[oi, ni + 1] >= lcs[oi + 1, ni]))
            {
                result.Add(new DiffLine(LineType.Added, newLines[ni], null, ni + 1));
                ni++;
            }
            else if (oi < n)
            {
                result.Add(new DiffLine(LineType.Removed, oldLines[oi], oi + 1, null));
                oi++;
            }
        }

        return result;
    }

    private static List<DiffLine> AddContext(List<DiffLine> allLines, int contextLines)
    {
        if (allLines.Count == 0) return allLines;

        // Find ranges around changes
        var changeIndices = new HashSet<int>();
        for (var i = 0; i < allLines.Count; i++)
        {
            if (allLines[i].Type != LineType.Context)
            {
                for (var c = Math.Max(0, i - contextLines); c <= Math.Min(allLines.Count - 1, i + contextLines); c++)
                    changeIndices.Add(c);
            }
        }

        // If no changes, return empty (identical files)
        if (changeIndices.Count == 0) return [];

        // Build result with only lines near changes, adding separators for gaps
        var result = new List<DiffLine>();
        var lastIncluded = -2;

        for (var i = 0; i < allLines.Count; i++)
        {
            if (changeIndices.Contains(i))
            {
                if (lastIncluded >= 0 && i - lastIncluded > 1)
                {
                    // Add separator for skipped context
                    result.Add(new DiffLine(LineType.Context, "...", null, null));
                }
                result.Add(allLines[i]);
                lastIncluded = i;
            }
        }

        return result;
    }
}
