namespace Asura.App.Controls;

/// <summary>
/// Type-to-select for a table that has no text search of its own: the letters
/// typed in quick succession, which name the row to jump to. A pause longer
/// than the timeout starts a new name, as it does in a Finder list.
/// </summary>
internal sealed class TypeSelectBuffer(TimeSpan timeout)
{
    private string _prefix = string.Empty;
    private DateTimeOffset _lastInput = DateTimeOffset.MinValue;

    public TypeSelectBuffer()
        : this(TimeSpan.FromSeconds(1))
    {
    }

    /// <summary>The name typed so far.</summary>
    public string Prefix => _prefix;

    /// <summary>
    /// Adds typed text and returns the name to look for, or null when the
    /// text is not something a name is made of.
    /// </summary>
    public string? Append(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(text) || text.Any(char.IsControl))
        {
            return null;
        }

        if (now - _lastInput > timeout)
        {
            _prefix = string.Empty;
        }

        _lastInput = now;
        _prefix += text;
        return _prefix;
    }

    /// <summary>
    /// The first item, from the top, whose name starts with the prefix — case
    /// and accents as typed are compared the way people read them.
    /// </summary>
    public static T? FindMatch<T>(IEnumerable<T> items, Func<T, string> name, string prefix)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);
        return items.FirstOrDefault(item =>
            name(item).StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));
    }
}
