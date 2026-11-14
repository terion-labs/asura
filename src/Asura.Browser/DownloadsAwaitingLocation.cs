namespace Asura.Browser;

/// <summary>
/// Downloads waiting for the user to choose where they are saved. CEF asks
/// for that location through the same save dialog a page could open, so a
/// save dialog is shown only while a download is waiting for one; any other
/// file dialog stays blocked.
/// </summary>
internal sealed class DownloadsAwaitingLocation
{
    private readonly List<int> _waiting = [];

    public void Add(int downloadId)
    {
        if (!_waiting.Contains(downloadId))
        {
            _waiting.Add(downloadId);
        }
    }

    /// <summary>Hands the next save dialog to the oldest waiting download, if any.</summary>
    public bool TryTake()
    {
        if (_waiting.Count == 0)
        {
            return false;
        }

        _waiting.RemoveAt(0);
        return true;
    }

    /// <summary>A download that ended before asking for its location stops waiting.</summary>
    public void Forget(int downloadId) => _waiting.Remove(downloadId);
}
