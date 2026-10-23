using System.Text.Json;
using Asura.Core;

namespace Asura.Infrastructure;

/// <summary>
/// Which tailnet a Tailscale connection signed in to. The browser signs in
/// with whatever Tailscale account it holds, so a connection could join a
/// different tailnet than the one it was set up for without any sign of it.
/// The connected status names the tailnet, and says so when it differs from
/// the one this connection used before.
/// </summary>
internal sealed partial class HostUserspaceVpnTransport
{
    private const int MaximumTailnetNameLength = 128;

    internal static string? ReadTailnetName(string status)
    {
        try
        {
            using var document = JsonDocument.Parse(status);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("CurrentTailnet", out var tailnet)
                || tailnet.ValueKind != JsonValueKind.Object
                || !tailnet.TryGetProperty("Name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            // The name comes from the control server: keep it short and printable.
            var value = new string([.. name.GetString()!.Where(character => !char.IsControl(character))]).Trim();
            return value.Length is 0 or > MaximumTailnetNameLength ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? RecordTailnet(NetworkConnectionId connectionId, string? tailnet)
    {
        if (tailnet is null)
        {
            return null;
        }

        var path = TailnetRecordPath(connectionId);
        string? previous = null;
        try
        {
            if (File.Exists(path))
            {
                previous = File.ReadAllText(path).Trim();
            }

            if (!string.Equals(previous, tailnet, StringComparison.Ordinal))
            {
                File.WriteAllText(path, tailnet);
                ProtectFile(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The note is informative; an unreadable record only loses the comparison.
            previous = null;
        }

        return string.IsNullOrEmpty(previous) || string.Equals(previous, tailnet, StringComparison.Ordinal)
            ? $"Signed in to tailnet “{tailnet}”."
            : $"Signed in to tailnet “{tailnet}”, not “{previous}”, which this connection used before.";
    }

    private string TailnetRecordPath(NetworkConnectionId connectionId)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_persistentStateRoot, "tailnets"));
        if (!OperatingSystem.IsWindows())
        {
            directory.UnixFileMode = UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute;
        }

        return Path.Combine(directory.FullName, TokenFor(connectionId.Value));
    }
}
