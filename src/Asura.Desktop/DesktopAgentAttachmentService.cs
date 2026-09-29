using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Asura.Desktop;

/// <summary>Transfers only a user-selected snapshot, through the live workspace's execution boundary.</summary>
internal sealed partial class DesktopAgentAttachmentService(SqliteAgentAttachmentStore store,
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
        var copies = WorkingCopiesFor(workspace);
        if (!routes.TryGetAttachmentRuntime(workspace, out _))
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
        var path = offset != 0 ? null
            : await copies.StageAsync(scope, attachment, bytes, cancellationToken).ConfigureAwait(false);
        return new(path, copies.IsIsolated ? "workspace local terminal in isolation" : "workspace local terminal on host",
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
}
