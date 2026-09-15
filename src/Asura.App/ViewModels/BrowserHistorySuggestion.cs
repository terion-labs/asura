using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>Readable address-bar text with the original navigation target intact.</summary>
public sealed record BrowserHistorySuggestion
{
    public BrowserHistorySuggestion(BrowserHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Address = entry.Address;
        DisplayAddress = Uri.TryCreate(entry.Address, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery,
                UriFormat.UriEscaped)
            : entry.Address;
        Title = string.IsNullOrWhiteSpace(entry.Title)
            ? uri?.Host ?? entry.Address
            : entry.Title.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    public string Address { get; }

    public string Title { get; }

    public string DisplayAddress { get; }
}
