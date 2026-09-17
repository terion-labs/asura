using System.Text;
using Asura.Application;

namespace Asura.Terminal;

/// <summary>
/// Adds the close-safety information libghostty cannot derive when the host foreground process
/// permanently wraps another interactive shell. SSH keeps <c>ssh</c> in the foreground, and an
/// inline container shell keeps <c>docker exec</c> there, so Ghostty's semantic-prompt signal is
/// unavailable unless the wrapped shell happens to install integration.
/// This classifier is deliberately conservative: it recognizes only common shell prompt shapes
/// at the canonical cursor and otherwise preserves the confirmation.
/// </summary>
internal static class RemoteTerminalIdleClassifier
{
    public static bool AppliesTo(TerminalLaunchRequest launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        // Only the exact interactive shell actions offered by the Kubernetes UI
        // qualify. An arbitrary pod command (including sh -c) remains unknown.
        if (launch.KubernetesTarget is { } pod)
        {
            return pod.Request.Command is ["/bin/sh" or "/bin/bash"];
        }

        return launch.ConnectionMetadata?.ConnectionBoundary.StartsWith(
                   "SSH:",
                   StringComparison.OrdinalIgnoreCase) == true
               || launch.ShellActivityFallback == TerminalShellActivityFallback.PromptShape;
    }

    public static bool IsAtShellPrompt(TerminalScreenSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // The alternate screen and mouse tracking mean something has taken the
        // terminal over, which is the opposite of sitting at a prompt.
        //
        // Bracketed paste does not, and treating it as though it did is what
        // made every modern remote shell look busy: bash and zsh turn it on at
        // the prompt precisely because that is where a paste needs protecting.
        // It is evidence of a shell waiting for input, not of a program using
        // the screen — and a program using the screen still has to fail the
        // prompt shape below to be called idle.
        _ = snapshot.IsBracketedPasteEnabled;
        if (snapshot.IsAlternateScreen
            || snapshot.IsMouseTrackingEnabled
            || snapshot.CursorColumn <= 0)
        {
            return false;
        }

        var cursorText = ReadLogicalCursorLine(snapshot).TrimEnd();
        if (cursorText.Length == 0)
        {
            return false;
        }

        var prompt = cursorText[^1];
        var prefix = cursorText[..^1];
        var trimmedPrefix = prefix.TrimEnd();
        return prompt switch
        {
            '$' or '#' or '%' =>
                prefix.Length == 0
                || prefix.Contains('@', StringComparison.Ordinal)
                || prefix.EndsWith(']')
                || prefix.EndsWith(':')
                || HasHostPathPrefix(prefix)
                || (prompt is '$' or '#' && HasVersionedShellPrefix(prefix))
                || (prefix.EndsWith(' ')
                    && (trimmedPrefix.StartsWith('/')
                        || trimmedPrefix.StartsWith('~'))),
            '>' =>
                prefix.StartsWith("PS ", StringComparison.OrdinalIgnoreCase)
                || prefix.Contains('@', StringComparison.Ordinal)
                || prefix.EndsWith(']'),
            _ => false,
        };
    }

    private static string ReadLogicalCursorLine(TerminalScreenSnapshot snapshot)
    {
        if (snapshot.CursorRow >= snapshot.StructuredRows.Count)
        {
            return string.Empty;
        }

        var firstRow = snapshot.CursorRow;
        while (firstRow > 0 && snapshot.StructuredRows[firstRow - 1].IsWrapped)
        {
            firstRow--;
        }

        var text = new StringBuilder(
            checked(((snapshot.CursorRow - firstRow) * snapshot.Columns) + snapshot.CursorColumn));
        for (var rowIndex = firstRow; rowIndex <= snapshot.CursorRow; rowIndex++)
        {
            var columnLimit = rowIndex == snapshot.CursorRow
                ? snapshot.CursorColumn
                : snapshot.Columns;
            AppendTextThroughColumn(text, snapshot.StructuredRows[rowIndex], columnLimit);
        }

        return text.ToString();
    }

    private static void AppendTextThroughColumn(
        StringBuilder text,
        TerminalScreenRow row,
        int columnLimit)
    {
        var column = 0;
        foreach (var cell in row.Cells)
        {
            if (cell.Width == 0)
            {
                continue;
            }

            if (column >= columnLimit)
            {
                break;
            }

            text.Append(cell.Text.Length == 0 ? ' ' : cell.Text);
            column += cell.Width;
        }
    }

    private static bool HasVersionedShellPrefix(string prefix)
    {
        // Bash's default \s-\v\$ prompt becomes sh-5.1# when invoked as /bin/sh.
        // Accept only the complete shell name and dotted ASCII version, not text
        // merely containing a shell name or ending in a prompt character.
        ReadOnlySpan<char> version = prefix.StartsWith("sh-", StringComparison.Ordinal) ? prefix.AsSpan(3)
            : prefix.StartsWith("bash-", StringComparison.Ordinal) ? prefix.AsSpan(5) : [];
        if (version.Length is < 3 or > 32) { return false; }
        bool hasDot = false;
        bool hasDigit = false;
        foreach (char character in version)
        {
            if (char.IsAsciiDigit(character)) { hasDigit = true; }
            else if (character == '.' && hasDigit) { hasDot = true; hasDigit = false; }
            else { return false; }
        }
        return hasDot && hasDigit;
    }

    private static bool HasHostPathPrefix(string prefix)
    {
        var separator = prefix.LastIndexOf(':');
        if (separator <= 0 || separator == prefix.Length - 1)
        {
            return false;
        }

        var path = prefix.AsSpan(separator + 1);
        return path[0] is '~' or '/';
    }
}
