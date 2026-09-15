using Asura.Application;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Exclr8Cef;

namespace Asura.Browser;

internal sealed partial class CefBrowserView
{
    private void OnContextMenu(object? sender, ContextMenuEventArgs args)
    {
        if (_disposed || _contentPolicy != CefBrowserContentPolicy.Ordinary)
        {
            return;
        }

        if (BrowserAddress.TryParse(args.LinkUrl, out var link))
        {
            args.InsertCommand(1, "Open Link in New Panel", () =>
            {
                RequestNewTab(link.Value.AbsoluteUri, true, BrowserOpenTarget.NewPanel);
                return Task.CompletedTask;
            });
            args.InsertCommand(2, "Copy Link Address", () => CopyAddressAsync(args.LinkUrl));
        }

        if (BrowserAddress.TryParse(args.SourceUrl, out var image))
        {
            args.InsertCommand(0, "Open Image in New Tab", () =>
            {
                RequestNewTab(image.Value.AbsoluteUri, true);
                return Task.CompletedTask;
            });
            args.InsertCommand(1, "Copy Image Address", () => CopyAddressAsync(args.SourceUrl));
        }

        args.InsertCommand(args.Items.Count, "Developer Tools", () =>
        {
            if (!_disposed)
            {
                _ = OpenDeveloperTools();
            }
            return Task.CompletedTask;
        });
    }

    private async Task CopyAddressAsync(string address)
    {
        if (!_disposed && TopLevel.GetTopLevel(_view)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(address);
        }
    }
}
