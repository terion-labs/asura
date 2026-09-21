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

        try
        {
            // Authentication must work before the workspace has a network route.
            using var process = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            return ValueTask.FromResult(true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return ValueTask.FromResult(false);
        }
    }
}
