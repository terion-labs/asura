using System.ComponentModel;
using System.Diagnostics;

namespace Asura.App;

public static class NetworkAuthenticationBrowser
{
    public static ValueTask<bool> OpenAsync(Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();
        if (!address.IsAbsoluteUri || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || address.UserInfo.Length != 0)
        {
            return ValueTask.FromResult(false);
        }

        // Authentication must work before the workspace has a network route,
        // so the address opens in the person's own browser, outside it.
        return new ValueTask<bool>(PlatformLauncher.OpenAsync(address));
    }
}
