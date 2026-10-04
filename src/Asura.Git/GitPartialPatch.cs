using System.Globalization;
using System.Text;

namespace Asura.Git;

/// <summary>Builds a selected edit against the actual source side, retaining literal bytes and newline annotations.</summary>
public static class GitPartialPatch
{
    public static string Build(string patch, IReadOnlyList<GitPatchLineSelection> selection, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(selection);
        var selected = selection.ToHashSet();
        var rows = patch.Split('\n');
        var firstHunk = Array.FindIndex(rows, row => row.StartsWith("@@ ", StringComparison.Ordinal));
        if (firstHunk < 0 || selected.Count == 0)
        {
            throw new ArgumentException("Choose changed lines from a text diff.", nameof(selection));
        }

        if (rows.Take(firstHunk).Any(row => row.StartsWith("old mode ", StringComparison.Ordinal) || row.StartsWith("new mode ", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Use whole-file actions for executable mode changes.", nameof(patch));
        }

        var output = new StringBuilder();
        var hunk = -1;
        var delta = 0;
        var totalNewLines = 0;
        var cursor = firstHunk;
        while (cursor < rows.Length)
        {
            hunk++;
            var header = rows[cursor++];
            var ranges = header.Split(' ');
            var sourceStart = Start(ranges[reverse ? 2 : 1][1..]);
            var body = new StringBuilder();
            var oldCount = 0;
            var newCount = 0;
            var line = 0;
            var hasChange = false;
            var previousIncluded = false;
            while (cursor < rows.Length && !rows[cursor].StartsWith("@@ ", StringComparison.Ordinal))
            {
                var row = rows[cursor++];
                if (row.Length == 0)
                {
                    continue;
                }

                if (row[0] == '\\')
                {
                    if (previousIncluded)
                    {
                        body.Append(row).Append('\n');
                    }

                    continue;
                }
                var marker = reverse ? row[0] switch { '+' => '-', '-' => '+', var other => other } : row[0];
                var keep = selected.Contains(new GitPatchLineSelection(hunk, line++));
                previousIncluded = marker != '+' || keep;
                if (!previousIncluded)
                {
                    continue;
                }

                if (marker == '-' && !keep)
                {
                    marker = ' ';
                }

                if (marker != '+')
                {
                    oldCount++;
                }

                if (marker != '-')
                {
                    newCount++;
                }

                hasChange |= marker is '+' or '-';
                body.Append(marker).Append(row.AsSpan(1)).Append('\n');
            }
            if (!hasChange)
            {
                continue;
            }

            var targetStart = sourceStart + delta + (oldCount == 0 ? 1 : newCount == 0 ? -1 : 0);
            output.Append(CultureInfo.InvariantCulture, $"@@ -{sourceStart},{oldCount} +{targetStart},{newCount} @@\n");
            output.Append(body);
            delta += newCount - oldCount;
            totalNewLines += newCount;
        }

        if (output.Length == 0)
        {
            throw new ArgumentException("Choose at least one changed line.", nameof(selection));
        }

        return BuildHeaders(rows[..firstHunk], reverse, totalNewLines) + output;
    }

    private static int Start(string range) => int.Parse(range.Split(',')[0], CultureInfo.InvariantCulture);

    private static string BuildHeaders(IReadOnlyList<string> headers, bool reverse, int totalNewLines)
    {
        var old = headers.First(row => row.StartsWith("--- ", StringComparison.Ordinal))[4..];
        var next = headers.First(row => row.StartsWith("+++ ", StringComparison.Ordinal))[4..];
        var addition = headers.FirstOrDefault(row => row.StartsWith("new file mode ", StringComparison.Ordinal));
        var deletion = headers.FirstOrDefault(row => row.StartsWith("deleted file mode ", StringComparison.Ordinal));
        var sourceMissing = reverse ? deletion is not null : addition is not null;
        var targetMissing = (reverse ? addition is not null : deletion is not null) && totalNewLines == 0;
        var oldPath = old is "/dev/null" ? Prefix(next, 'a') : old;
        var newPath = next is "/dev/null" ? Prefix(old, 'b') : next;
        if (reverse)
        {
            (oldPath, newPath) = (Prefix(newPath, 'a'), Prefix(oldPath, 'b'));
        }

        var output = new StringBuilder();
        output.Append("diff --git ").Append(oldPath).Append(' ').Append(newPath).Append('\n');
        foreach (var header in headers)
        {
            if (header.StartsWith("diff --git ", StringComparison.Ordinal)
                || header.StartsWith("index ", StringComparison.Ordinal)
                || header.StartsWith("--- ", StringComparison.Ordinal)
                || header.StartsWith("+++ ", StringComparison.Ordinal)
                || header.StartsWith("rename ", StringComparison.Ordinal)
                || header.StartsWith("new file mode ", StringComparison.Ordinal)
                || header.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                continue;
            }

            output.Append(header).Append('\n');
        }

        var mode = addition is not null ? addition[14..] : deletion?[18..];
        if (sourceMissing)
        {
            output.Append("new file mode ").Append(mode).Append('\n');
        }
        else if (targetMissing)
        {
            output.Append("deleted file mode ").Append(mode).Append('\n');
        }

        var renameFrom = headers.FirstOrDefault(row => row.StartsWith("rename from ", StringComparison.Ordinal));
        var renameTo = headers.FirstOrDefault(row => row.StartsWith("rename to ", StringComparison.Ordinal));
        if (renameFrom is not null && renameTo is not null)
        {
            var source = reverse ? renameTo[10..] : renameFrom[12..];
            var target = reverse ? renameFrom[12..] : renameTo[10..];
            output.Append("rename from ").Append(source).Append('\n');
            output.Append("rename to ").Append(target).Append('\n');
        }

        output.Append("--- ").Append(sourceMissing ? "/dev/null" : oldPath).Append('\n');
        output.Append("+++ ").Append(targetMissing ? "/dev/null" : newPath).Append('\n');
        return output.ToString();
    }

    private static string Prefix(string path, char prefix)
    {
        var offset = path.StartsWith('"') ? 1 : 0;
        return path.Length > offset + 1 && path[offset + 1] == '/'
            ? path[..offset] + prefix + path[(offset + 1)..] : path;
    }
}
