using Asura.Application;
using Asura.Core;

namespace Asura.Desktop;

internal sealed partial class DesktopAgentAttachmentService
{
    internal static async Task<string> StageLocalAsync(AgentFileAttachment attachment, byte[] bytes, CancellationToken token)
    {
        var directory = await CreateStagingDirectoryAsync(null, token).ConfigureAwait(false);
        try { return await StageLocalAtAsync(directory, StagedName(new("standalone"), attachment), bytes, token).ConfigureAwait(false); }
        catch
        {
            await DeleteStagingDirectoryAsync(null, directory).ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<string> StageIsolatedAsync(IConnectionCommandRuntime runtime, AgentFileAttachment attachment,
        byte[] bytes, CancellationToken token)
    {
        var directory = await CreateStagingDirectoryAsync(runtime, token).ConfigureAwait(false);
        try { return await StageIsolatedAtAsync(runtime, directory, StagedName(new("standalone"), attachment), bytes, token).ConfigureAwait(false); }
        catch
        {
            await DeleteStagingDirectoryAsync(runtime, directory).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> CreateStagingDirectoryAsync(IConnectionCommandRuntime? runtime, CancellationToken token)
    {
        if (runtime is null)
        {
            var directory = Directory.CreateTempSubdirectory("asura-attachment-");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            return directory.FullName;
        }
        var path = await RunAttachmentCommandAsync(runtime, "umask 077; mktemp -d /tmp/asura-attachment-XXXXXXXX", [], [], token).ConfigureAwait(false);
        const string prefix = "/tmp/asura-attachment-";
        path = path.TrimEnd('\n');
        if (path.Length != prefix.Length + 8 || !path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.Skip(prefix.Length).All(char.IsAsciiLetterOrDigit))
        {
            throw new IOException("The workspace returned an invalid attachment directory. Reopen it and retry.");
        }
        return path;
    }

    private static async Task<string> StageLocalAtAsync(string directory, string name, byte[] bytes, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) { Directory.CreateDirectory(directory); }
        else { Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        var path = Path.Combine(directory, name);
        if (File.Exists(path)) { return path; }
        var temporary = path + ".transfer";
        File.Delete(temporary);
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            }
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { /* Preserve a working copy created while writing. */ }
            return path;
        }
        finally { File.Delete(temporary); }
    }

    private static async Task<string> StageIsolatedAtAsync(IConnectionCommandRuntime runtime, string directory, string name,
        byte[] bytes, CancellationToken token)
    {
        // Paths are argv data and bytes use stdin. Reopening never overwrites an edited working copy.
        const string script = "umask 077; d=$1; p=\"$d/$2\"; t=\"$p.transfer\"; "
            + "mkdir -p -- \"$d\" && rm -f -- \"$t\" || exit 1; "
            + "if [ -f \"$p\" ]; then cat > /dev/null || exit 1; else "
            + "trap 'rm -f -- \"$t\"' EXIT; cat > \"$t\" && chmod 400 \"$t\" || exit 1; "
            + "ln -- \"$t\" \"$p\" 2>/dev/null || [ -f \"$p\" ] || exit 1; rm -f -- \"$t\" || exit 1; trap - EXIT; fi; "
            + "printf '%s' \"$p\"";
        var path = await RunAttachmentCommandAsync(runtime, script, [directory, name], bytes, token).ConfigureAwait(false);
        if (!string.Equals(path, directory + "/" + name, StringComparison.Ordinal))
        {
            throw new IOException("The attachment could not be transferred into this workspace. Retry opening the attachment.");
        }
        return path;
    }

    private static async Task DeleteStagingDirectoryAsync(IConnectionCommandRuntime? runtime, string directory)
    {
        if (runtime is null)
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
            return;
        }
        _ = await RunAttachmentCommandAsync(runtime, "rm -rf -- \"$1\"", [directory], [], CancellationToken.None).ConfigureAwait(false);
    }
}
