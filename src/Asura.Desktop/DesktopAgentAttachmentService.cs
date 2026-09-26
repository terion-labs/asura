using System.Diagnostics;
using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Asura.Desktop;

/// <summary>Transfers only a user-selected snapshot, through the live workspace's execution boundary.</summary>
internal sealed class DesktopAgentAttachmentService(SqliteAgentAttachmentStore store,
    WorkspaceNetworkRouteRegistry routes) : IAgentAttachmentService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int PreviewCharacters = 8192;

    public async ValueTask<AgentFileAttachment> ImportAsync(AgentConversationScopeId scope, string fileName,
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        try
        {
            // SQLite's async calls can perform synchronous disk work. Keep large imports off the dispatcher.
            return await Task.Run(async () => await store.ImportAsync(scope, fileName, content, cancellationToken)
                .ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception)
        {
            throw new IOException("The attachment could not be saved. Check available disk space and try attaching it again.", exception);
        }
    }

    public async ValueTask<AgentOpenedAttachment> OpenAsync(AgentConversationScopeId scope, WorkspaceInstanceId workspace,
        AgentFileAttachment attachment, int offset, CancellationToken cancellationToken)
    {
        if (!routes.TryGetAttachmentRuntime(workspace, out var isolatedRuntime))
        {
            throw new InvalidOperationException("The workspace is closed. Reopen it before accessing attachments.");
        }
        byte[] bytes;
        try { bytes = await store.ReadAsync(scope, attachment, cancellationToken).ConfigureAwait(false); }
        catch (SqliteException exception)
        {
            throw new IOException("The saved attachment could not be read. Retry, or attach the original file again.", exception);
        }
        var text = TextPreview(bytes, offset, out var next, out var more);
        var path = offset != 0 ? null : isolatedRuntime is null
            ? await StageLocalAsync(attachment, bytes, cancellationToken).ConfigureAwait(false)
            : await StageIsolatedAsync(isolatedRuntime, attachment, bytes, cancellationToken).ConfigureAwait(false);
        return new(path, isolatedRuntime is null ? "workspace local terminal on host" : "workspace local terminal in isolation",
            text, next, more);
    }

    internal static string? TextPreview(byte[] bytes, int offset, out int next, out bool more)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        next = 0;
        more = false;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, bytes.Length);
        Encoding encoding = StrictUtf8;
        var preamble = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            encoding = new UnicodeEncoding(false, true, true);
            preamble = 2;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            encoding = new UnicodeEncoding(true, true, true);
            preamble = 2;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { preamble = 3; }
        var start = offset == 0 ? preamble : offset;
        var characters = new char[PreviewCharacters];
        try
        {
            // Decode one page, preserving byte offsets and complete Unicode characters.
            encoding.GetDecoder().Convert(bytes.AsSpan(start), characters, flush: true,
                out _, out var usedCharacters, out _);
            var text = new string(characters, 0, usedCharacters);
            if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t' or '\f')))
            {
                return null;
            }
            // UTF-16 decoders may buffer a high surrogate beyond the emitted page.
            // Derive the next position from emitted text because each page gets a fresh decoder.
            next = start + encoding.GetByteCount(text);
            more = next < bytes.Length;
            return text;
        }
        catch (DecoderFallbackException) { return null; }
    }

    internal static async Task<string> StageLocalAsync(AgentFileAttachment attachment, byte[] bytes, CancellationToken token)
    {
        var directory = Directory.CreateTempSubdirectory("asura-attachment-");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var path = Path.Combine(directory.FullName, StagedName(attachment));
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
            await using var stream = new FileStream(path, options);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            return path;
        }
        catch
        {
            directory.Delete(recursive: true);
            throw;
        }
    }

    internal static async Task<string> StageIsolatedAsync(IConnectionCommandRuntime runtime, AgentFileAttachment attachment,
        byte[] bytes, CancellationToken token)
    {
        // The name is argv data. Byte transfer uses stdin; neither passes through model-generated shell code.
        const string script = "umask 077; d=$(mktemp -d /tmp/asura-attachment-XXXXXXXX) || exit 1; "
            + "trap 'rm -rf -- \"$d\"' EXIT; cat > \"$d/$1\" || exit 1; "
            + "chmod 400 \"$d/$1\" || exit 1; printf '%s' \"$d/$1\"; trap - EXIT";
        var planned = await runtime.PlanDuplexCommandAsync(BuiltInConnections.Local, "/bin/sh",
            ["-c", script, "asura-attachment", StagedName(attachment)], token).ConfigureAwait(false);
        if (planned is not ConnectionRuntimeResult<TerminalLaunchRequest>.Success { Value: var launch })
        {
            throw new IOException("The attachment could not be transferred into this workspace. Reopen the workspace and retry.");
        }
        var start = new ProcessStartInfo(launch.Executable ?? throw new InvalidOperationException("No workspace executable."))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in launch.Arguments) { start.ArgumentList.Add(argument); }
        foreach (var (name, value) in launch.Environment) { start.Environment[name] = value; }
        if (launch.WorkingDirectory is { } directory) { start.WorkingDirectory = directory; }
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (!await Task.Run(process.Start, timeout.Token).ConfigureAwait(false)) { throw new IOException("Attachment transfer did not start."); }
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error).ConfigureAwait(false);
            var path = await output.ConfigureAwait(false);
            if (process.ExitCode != 0 || !path.StartsWith("/tmp/asura-attachment-", StringComparison.Ordinal)
                || path.Any(char.IsControl))
            {
                throw new IOException("The attachment could not be transferred into this workspace. Retry opening the attachment.");
            }
            return path;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new IOException("The workspace could not start the attachment transfer. Reopen the workspace and retry.", exception);
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) { /* The transfer may not have started. */ }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        var count = await reader.ReadBlockAsync(buffer, token).ConfigureAwait(false);
        if (count == buffer.Length) { throw new IOException("Attachment transfer returned too much output."); }
        return new string(buffer, 0, count);
    }

    private static string StagedName(AgentFileAttachment attachment)
    {
        // Keep the type suffix for file tools; long names and platform-specific characters
        // cannot make an otherwise valid attachment fail while creating its working copy.
        var extension = Path.GetExtension(attachment.FileName);
        if (extension.Length > 32) { extension = ".bin"; }
        return "attachment" + string.Concat(extension.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '.' ? character : '_'));
    }
}
