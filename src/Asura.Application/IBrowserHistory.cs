namespace Asura.Application;

/// <summary>Local address suggestions, isolated by named profile and workspace partition.</summary>
public interface IBrowserHistory
{
    ValueTask RecordAsync(BrowserProfileSelection profile, BrowserAddress address,
        string title, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<BrowserHistoryEntry>> SearchAsync(BrowserProfileSelection profile,
        string query, CancellationToken cancellationToken);

    ValueTask ClearAsync(BrowserProfileSelection profile, CancellationToken cancellationToken);
}

public sealed record BrowserHistoryEntry(string Address, string Title);
