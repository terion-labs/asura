using System.Text;

namespace Asura.Agent.Providers;

/// <summary>
/// How much of a model's raw reasoning one response shows. Raw reasoning
/// can run far longer than a summary, and the kernel ends a turn whose
/// reasoning outgrows its limit; what is past this cap is only left
/// unshown, so a long think never costs the answer.
/// </summary>
internal sealed class ShownRawReasoning
{
    /// <summary>Half the kernel's default reasoning limit, leaving room for summaries.</summary>
    internal const int MaximumBytes = 128 * 1024;

    internal const string CutMarker = "\n\n…";

    private int _bytes;
    private bool _cut;

    /// <summary>The part of <paramref name="value"/> to show: all of it, a head of it, or nothing.</summary>
    public string Accept(string value)
    {
        if (_cut || value.Length == 0)
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetByteCount(value);
        if (_bytes <= MaximumBytes - bytes)
        {
            _bytes += bytes;
            return value;
        }

        // The head and the marker together stay within the cap, and so are
        // never longer than the fragment they were cut from.
        _cut = true;
        var room = MaximumBytes - _bytes - Encoding.UTF8.GetByteCount(CutMarker);
        var head = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > room)
            {
                break;
            }

            room -= rune.Utf8SequenceLength;
            head.Append(rune.ToString());
        }

        _bytes = MaximumBytes;
        return head.Append(CutMarker).ToString();
    }
}
