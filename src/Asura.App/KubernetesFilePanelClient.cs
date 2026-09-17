using System.Globalization;
using System.Text;
using Asura.Application;

namespace Asura.App;

/// <summary>
/// Reads one UID-bound container through the owned workspace exec transport. Fixed POSIX commands
/// take paths as separate arguments; remote paths are never interpreted as local paths or shell source.
/// This provider is read-only, like the existing Docker File Viewer adapter.
/// </summary>
public sealed class KubernetesFilePanelClient : IFilePanelClient
{
    private const int MaximumEntries = 10_000;
    private const int MaximumBytes = 1024 * 1024;
    private const string ListCommand = "[ -d \"$1\" ] || exit 44; [ -r \"$1\" ] && [ -x \"$1\" ] || exit 45; n=0; for p in \"$1\"/* \"$1\"/.[!.]* \"$1\"/..?*; do [ -e \"$p\" ] || [ -L \"$p\" ] || continue; n=$((n+1)); [ \"$n\" -le 10000 ] || exit 46; if [ -L \"$p\" ]; then k=l; elif [ -d \"$p\" ]; then k=d; elif [ -f \"$p\" ]; then k=f; else k=o; fi; printf '%s\\000%s\\000' \"$k\" \"${p##*/}\"; done";
    private const string StatCommand = "if [ -L \"$1\" ]; then k=l; elif [ -d \"$1\" ]; then k=d; elif [ -f \"$1\" ]; then k=f; elif [ -e \"$1\" ]; then k=o; else exit 44; fi; printf '%s' \"$k\"";
    private const string ReadCommand = "[ -f \"$1\" ] || exit 44; [ -r \"$1\" ] || exit 45; command -v head >/dev/null 2>&1 || exit 127; exec head -c \"$2\" \"$1\"";
    private readonly IKubernetesPanelSessionFactory _factory;
    private readonly string _profileId = $"kubernetes-pod-{Guid.NewGuid():N}";

    public KubernetesFilePanelClient(IKubernetesPanelSessionFactory factory, KubernetesFileTarget target)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Container);
        if (target.Pod is not { Group: "", Version: "v1", Resource: "pods", Namespace: not null }
            || string.IsNullOrWhiteSpace(target.Pod.Uid))
        { throw new ArgumentException("A container file view requires an exact namespaced pod.", nameof(target)); }
        Profiles = Array.AsReadOnly([new FileProviderProfileDescriptor(_profileId,
            $"{target.Profile.ContextName} · {target.Pod.Namespace}/{target.Pod.Name} · {target.Container}",
            FileProviderFamily.Posix, Location(FilePanelPath.Root),
            FilePanelCapability.List | FilePanelCapability.Stat | FilePanelCapability.RangedRead | FilePanelCapability.Pagination,
            MaximumEntries, MaximumBytes, RequiresHostTransferForPreview: true)]);
    }

    public KubernetesFileTarget Target { get; }
    public IReadOnlyList<FileProviderProfileDescriptor> Profiles { get; }

    public IAsyncEnumerable<FilePanelResult<FilePanelEntry>> SearchAsync(FilePanelSearchRequest request, CancellationToken cancellationToken) =>
        FilePanelSearch.FindAsync(this, request, cancellationToken);
    public IAsyncEnumerable<FilePanelResult<FilePanelChange>> WatchAsync(FilePanelWatchRequest request, CancellationToken cancellationToken) =>
        FilePanelWatch.ObserveAsync(this, request, cancellationToken);

    public async ValueTask<FilePanelResult<FilePanelPage>> ListAsync(FilePanelListRequest request, CancellationToken cancellationToken)
    {
        if (!TryPath(request.Location, out var path)) { return Invalid<FilePanelPage>(); }
        if (!int.TryParse(request.ContinuationToken ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
        { return Invalid<FilePanelPage>(); }
        var result = await RunAsync(ListCommand, [path], MaximumBytes, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error) { return FilePanelResult<FilePanelPage>.Failure(error); }
        try
        {
            var fields = new UTF8Encoding(false, true).GetString(result.Bytes!).Split('\0');
            if (fields[^1].Length != 0 || fields.Length % 2 != 1 || fields.Length > MaximumEntries * 2 + 1)
            { return Invalid<FilePanelPage>(); }
            var entries = new List<FilePanelEntry>();
            for (var index = 0; index < fields.Length - 1; index += 2)
            {
                var name = fields[index + 1];
                var segment = new FilePanelPathSegment(name);
                if (request.ShowHidden || !name.StartsWith('.'))
                { entries.Add(Entry(request.Location.Child(segment), name, fields[index])); }
            }
            if (offset > entries.Count) { return Invalid<FilePanelPage>(); }
            var page = entries.OrderBy(item => item.Name, StringComparer.Ordinal).Skip(offset)
                .Take(Math.Min(request.PageSize, MaximumEntries)).ToArray();
            return FilePanelResult<FilePanelPage>.Success(new(page,
                offset + page.Length < entries.Count ? (offset + page.Length).ToString(CultureInfo.InvariantCulture) : null));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        { return Invalid<FilePanelPage>(); }
        finally { Array.Clear(result.Bytes!); }
    }

    public async ValueTask<FilePanelResult<FilePanelEntry>> StatAsync(FilePanelLocation location, CancellationToken cancellationToken)
    {
        if (!TryPath(location, out var path)) { return Invalid<FilePanelEntry>(); }
        var result = await RunAsync(StatCommand, [path], 1, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error) { return FilePanelResult<FilePanelEntry>.Failure(error); }
        try
        {
            var name = ((FilePanelAddress.Hierarchical)location.Address).Path.Name?.Value ?? Target.Pod.Name;
            return FilePanelResult<FilePanelEntry>.Success(Entry(location, name, Encoding.UTF8.GetString(result.Bytes!)));
        }
        catch (InvalidDataException) { return Invalid<FilePanelEntry>(); }
        finally { Array.Clear(result.Bytes!); }
    }

    public async ValueTask<FilePanelResult<FilePanelPreview>> PreviewAsync(FilePanelPreviewRequest request, CancellationToken cancellationToken)
    {
        if (!TryPath(request.Location, out var path)) { return Invalid<FilePanelPreview>(); }
        var maximum = (int)Math.Min(request.MaximumBytes, MaximumBytes);
        var result = await RunAsync(ReadCommand, [path, (maximum + 1).ToString(CultureInfo.InvariantCulture)], maximum + 1, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error) { return FilePanelResult<FilePanelPreview>.Failure(error); }
        try
        {
            var bytes = result.Bytes!;
            var (kind, mediaType) = FilePanelPreviewClassifier.Classify(request.Location, bytes.AsSpan(0, Math.Min(bytes.Length, maximum)));
            return FilePanelResult<FilePanelPreview>.Success(new(request.Location, kind, mediaType,
                bytes.AsSpan(0, Math.Min(bytes.Length, maximum)), bytes.Length > maximum));
        }
        finally { Array.Clear(result.Bytes!); }
    }

    private async Task<(byte[]? Bytes, FilePanelError? Error)> RunAsync(string command, string[] arguments, int maximum, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var client = await _factory.OpenAsync(Target.Profile, timeout.Token).ConfigureAwait(false);
            await using var exec = await client.OpenExecAsync(new(Target.Pod, Target.Container,
                ["/bin/sh", "-c", command, "asura-files", .. arguments], Tty: false), timeout.Token).ConfigureAwait(false);
            return await ReadCommandAsync(exec, maximum, timeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return (null, Error(FilePanelErrorCode.IoFailure, "Container file read timed out. Check the container and retry.")); }
        catch (KubernetesRequestException exception)
        {
            return (null, Error(exception.Code == KubernetesErrorCode.Forbidden ? FilePanelErrorCode.AccessDenied : FilePanelErrorCode.Offline,
            "Cannot read this container. Check its pod identity, exec permission, and workspace connection."));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        { return (null, Error(FilePanelErrorCode.IoFailure, "The container file stream failed or exceeded its size limit.")); }
    }

    // Drain stderr concurrently so an error-producing process cannot deadlock stdout. No raw stderr
    // enters the UI because programs may include credentials or arbitrary terminal control sequences.
    private static async Task<(byte[]? Bytes, FilePanelError? Error)> ReadCommandAsync(IKubernetesExecSession exec, int maximum, CancellationTokenSource lifetime)
    {
        var output = ReadBoundedAsync(exec.StandardOutput, maximum, lifetime.Token);
        var error = ReadBoundedAsync(exec.StandardError, 16 * 1024, lifetime.Token);
        try
        {
            await exec.CompleteInputAsync(lifetime.Token).ConfigureAwait(false);
            var completed = await Task.WhenAny(output, error).ConfigureAwait(false);
            if (completed.IsFaulted) { await completed.ConfigureAwait(false); }
            await Task.WhenAll(output, error).ConfigureAwait(false);
            var exit = await exec.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            if (exit != 0) { Array.Clear(await output.ConfigureAwait(false)); }
            return exit switch
            {
                0 => (await output.ConfigureAwait(false), null),
                44 => (null, Error(FilePanelErrorCode.NotFound, "The path is missing or is not a regular file or directory.")),
                45 => (null, Error(FilePanelErrorCode.AccessDenied, "The container user cannot read this path.")),
                46 => (null, Error(FilePanelErrorCode.UnsupportedCapability, "This directory exceeds the 10,000-entry browsing limit.")),
                126 or 127 => (null, Error(FilePanelErrorCode.UnsupportedCapability, "Container files require /bin/sh and head in the selected container.")),
                _ => (null, Error(FilePanelErrorCode.IoFailure, "The container file command failed. Check the path and required container tools.")),
            };
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await output.ConfigureAwait(false); } catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException) { }
            try { var bytes = await error.ConfigureAwait(false); Array.Clear(bytes); } catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException) { }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[Math.Min(maximum + 1, 8192)];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0) { return bytes.ToArray(); }
                if (bytes.Length + read > maximum) { throw new InvalidDataException("Container file output exceeds the limit."); }
                await bytes.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
        }
        finally { Array.Clear(buffer); }
    }

    private bool TryPath(FilePanelLocation location, out string path)
    {
        path = string.Empty;
        if (!string.Equals(location.ProviderProfileId, _profileId, StringComparison.Ordinal) || location.Authority is not null || location.Version is not null
            || location.Address is not FilePanelAddress.Hierarchical hierarchical) { return false; }
        path = $"/{string.Join('/', hierarchical.Path.Segments.Select(segment => segment.Value))}";
        return path.Length <= 4096;
    }

    private FilePanelLocation Location(FilePanelPath path) => new(_profileId, null, new FilePanelAddress.Hierarchical(path));
    private static FilePanelEntry Entry(FilePanelLocation location, string name, string kind) => new(location, name,
        kind switch
        {
            "f" => FilePanelEntryKind.File,
            "d" => FilePanelEntryKind.Directory,
            "l" => FilePanelEntryKind.Link,
            "o" => FilePanelEntryKind.Other,
            _ => throw new InvalidDataException("Invalid container file kind.")
        }, null, null, name.StartsWith('.'));
    private static FilePanelError Error(FilePanelErrorCode code, string message) => new(code, "kubernetes_file_error", message, false);
    private static FilePanelResult<T> Invalid<T>() => FilePanelResult<T>.Failure(Error(FilePanelErrorCode.InvalidLocation, "The file location or response does not belong to this container."));
    private static ValueTask<FilePanelResult<T>> Unsupported<T>() => ValueTask.FromResult(FilePanelResult<T>.Failure(
        Error(FilePanelErrorCode.UnsupportedCapability, "Kubernetes container browsing is read-only.")));
    public ValueTask<FilePanelResult<FilePanelEntry>> CreateDirectoryAsync(FilePanelCreateDirectoryRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelEntry>();
    public ValueTask<FilePanelResult<FilePanelEntry>> RenameAsync(FilePanelRenameRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelEntry>();
    public ValueTask<FilePanelResult<FilePanelDeleteReceipt>> DeleteAsync(FilePanelDeleteRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelDeleteReceipt>();
    public ValueTask<FilePanelResult<FilePanelTextWriteReceipt>> WriteTextAsync(FilePanelTextWriteRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelTextWriteReceipt>();
    public ValueTask<FilePanelResult<FilePanelCopyReceipt>> CopyAsync(FilePanelCopyRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelCopyReceipt>();
    public ValueTask<FilePanelResult<FilePanelAccessControl>> GetAccessControlAsync(FilePanelAccessControlRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelAccessControl>();
    public ValueTask<FilePanelResult<FilePanelAccessControl>> SetAccessControlAsync(FilePanelSetAccessControlRequest request, CancellationToken cancellationToken) => Unsupported<FilePanelAccessControl>();
}
