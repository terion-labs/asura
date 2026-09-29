using System.Security.Cryptography;
using System.Text;
using Asura.Application;
using Asura.Core;

namespace Asura.Desktop;

internal sealed partial class DesktopAgentAttachmentService
{
    private readonly object _workingCopiesGate = new();
    private readonly Dictionary<WorkspaceInstanceId, WorkspaceWorkingCopies> _workingCopies = [];

    internal IAsyncDisposable CreateWorkspaceLifetime(WorkspaceInstanceId workspace)
    {
        if (!routes.TryGetAttachmentRuntime(workspace, out var runtime))
        {
            throw new InvalidOperationException("Attachment working copies require a live workspace.");
        }
        var copies = new WorkspaceWorkingCopies(this, workspace, runtime);
        lock (_workingCopiesGate)
        {
            if (!_workingCopies.TryAdd(workspace, copies))
            {
                throw new InvalidOperationException("This workspace already owns attachment working copies.");
            }
        }
        return copies;
    }

    private WorkspaceWorkingCopies WorkingCopiesFor(WorkspaceInstanceId workspace)
    {
        lock (_workingCopiesGate)
        {
            return _workingCopies.GetValueOrDefault(workspace)
                ?? throw new InvalidOperationException("The workspace is closed. Reopen it before accessing attachments.");
        }
    }

    private void RetireWorkingCopies(WorkspaceInstanceId workspace, WorkspaceWorkingCopies copies)
    {
        lock (_workingCopiesGate)
        {
            if (_workingCopies.TryGetValue(workspace, out var current) && ReferenceEquals(current, copies))
            {
                _workingCopies.Remove(workspace);
            }
        }
    }

    private static string StagedName(AgentConversationScopeId scope, AgentFileAttachment attachment)
    {
        // NUL cannot occur in a scope or GUID, so it separates the identities unambiguously.
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.Value + "\0" + attachment.Id)));
        var extension = Path.GetExtension(attachment.FileName);
        if (extension.Length > 32) { extension = ".bin"; }
        return "attachment-" + identity + string.Concat(extension.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '.' ? character : '_'));
    }

    private sealed class WorkspaceWorkingCopies(DesktopAgentAttachmentService owner, WorkspaceInstanceId workspace,
        IConnectionCommandRuntime? runtime) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string? _directory;
        private bool _closed;

        internal bool IsIsolated => runtime is not null;

        internal async Task<string> StageAsync(AgentConversationScopeId scope, AgentFileAttachment attachment,
            byte[] bytes, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_closed) { throw new InvalidOperationException("The workspace is closing. Reopen it before accessing attachments."); }
                _directory ??= await CreateStagingDirectoryAsync(runtime, token).ConfigureAwait(false);
                var name = StagedName(scope, attachment);
                return runtime is null
                    ? await StageLocalAtAsync(_directory, name, bytes, token).ConfigureAwait(false)
                    : await StageIsolatedAtAsync(runtime, _directory, name, bytes, token).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _closed = true;
                if (_directory is { } directory)
                {
                    await DeleteStagingDirectoryAsync(runtime, directory).ConfigureAwait(false);
                    _directory = null;
                }
                owner.RetireWorkingCopies(workspace, this);
            }
            finally { _gate.Release(); }
        }
    }
}
