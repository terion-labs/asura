using System.Net.Sockets;
using Asura.Application;
using Asura.Core;
using Renci.SshNet.Common;

namespace Asura.Files;

/// <summary>
/// Turns an SSH tunnel failure into a sentence people can act on. The library's
/// own message names sockets, algorithms, and error numbers, so it goes to
/// diagnostics instead of the screen.
/// </summary>
internal static class SshTunnelFailureText
{
    public static string Describe(string what, ConnectionProfile connection, Exception exception)
    {
        SecretSafeDiagnosticProjection.WriteTrace("ssh.tunnel.open-failed", exception);
        var reason = exception switch
        {
            SshAuthenticationException =>
                "The server didn't accept the credentials. Check the username and password or key in the connection, then try again.",
            SshOperationTimeoutException or SocketException or SshConnectionException =>
                "The server didn't respond. Check that the host is reachable, then try again.",
            _ => "Try again. If it keeps failing, test the connection in Settings.",
        };
        return $"Asura couldn't open {what} through “{connection.Name}”. {reason}";
    }
}
