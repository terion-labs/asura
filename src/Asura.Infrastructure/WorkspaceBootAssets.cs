using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Asura.Application;

namespace Asura.Infrastructure;

/// <summary>
/// Provisions immutable boot images on first isolation use. Only the descriptor
/// ships in the signed app; downloaded bytes must match its hashes before boot.
/// This host bootstrap download precedes the workspace and carries no guest traffic.
/// </summary>
internal sealed class WorkspaceBootAssets(string descriptorPath, string cacheRoot, Uri downloadUri)
{
    internal const string ArchiveName = "Asura-workspace-boot-arm64.zip";
    private static readonly string[] Names = ["kernel.bin", "initfs.ext4"];

    // The descriptor ships inside the app bundle; only a fresh install restores it.
    private const string DamagedDescriptor =
        "Asura's workspace boot settings are damaged. Reinstall Asura, then try again.";

    internal async Task<string> EnsureAsync(
        IProgress<WorkspaceIsolationProgress>? progress,
        CancellationToken cancellationToken,
        HttpMessageHandler? handler = null,
        string? localArchive = null)
    {
        try
        {
            using var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(descriptorPath, cancellationToken).ConfigureAwait(false));
            var archivePin = ReadPin(descriptor.RootElement, 256L * 1024 * 1024);
            var pins = Names.ToDictionary(name => name,
                name => ReadPin(descriptor.RootElement.GetProperty("files").GetProperty(name), 1024L * 1024 * 1024), StringComparer.Ordinal);
            var directory = Path.Combine(cacheRoot, archivePin.Hash);
            using var cache = new WorkspaceBootCacheDirectory(directory);
            // A filesystem lock also serializes independent windows/processes. No
            // completion marker can outlive a corrupt or partially written image.
            await using var lease = await cache.LockAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(new WorkspaceIsolationProgress("Verifying cached workspace boot images…"));
            if (await ValidImagesAsync(cache, pins, cancellationToken).ConfigureAwait(false))
            {
                cache.RequireUnchangedPath();
                return directory;
            }

            var pendingName = $"download-{Guid.NewGuid():N}.partial";
            try
            {
                await using var pending = cache.CreateNew(pendingName);
                if (localArchive is not null)
                {
                    await using var source = File.OpenRead(localArchive);
                    await CopyAsync(source, pending, archivePin.Size, progress, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(15));
                    progress?.Report(new WorkspaceIsolationProgress("Downloading workspace boot images for first use…"));
                    using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    await CopyAsync(source, pending, archivePin.Size, progress, timeout.Token).ConfigureAwait(false);
                }

                if (!await ValidStreamAsync(pending, archivePin, cancellationToken).ConfigureAwait(false))
                {
                    throw new WorkspaceEnvironmentRefusalException("The workspace's boot images failed their integrity check after downloading. Try again to download a fresh copy.");
                }

                progress?.Report(new WorkspaceIsolationProgress("Installing verified workspace boot images…"));
                pending.Position = 0;
                using var archive = new ZipArchive(pending, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count != Names.Length)
                {
                    throw new IOException("The workspace boot archive has unexpected contents.");
                }
                foreach (var name in Names)
                {
                    var entry = archive.GetEntry(name) ?? throw new IOException("The workspace boot archive is incomplete.");
                    var temporary = $"{name}-{Guid.NewGuid():N}.partial";
                    try
                    {
                        await using var target = cache.CreateNew(temporary);
                        await using var source = entry.Open();
                        await CopyAsync(source, target, pins[name].Size, null, cancellationToken).ConfigureAwait(false);
                        if (!await ValidStreamAsync(target, pins[name], cancellationToken).ConfigureAwait(false))
                        {
                            throw new IOException("A workspace boot image failed its integrity check.");
                        }
                        cache.Install(temporary, name, target);
                    }
                    finally
                    {
                        cache.Delete(temporary);
                    }
                }
                cache.RequireUnchangedPath();
                return directory;
            }
            finally
            {
                cache.Delete(pendingName);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new WorkspaceEnvironmentRefusalException("Asura couldn't download the workspace's boot images. Connect to the internet, then try again. "
                + "Once downloaded, they work offline.", exception);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new WorkspaceEnvironmentRefusalException(DamagedDescriptor, exception);
        }
    }

    private static (string Hash, long Size) ReadPin(JsonElement element, long maximum)
    {
        var hash = element.GetProperty("sha256").GetString();
        var size = element.GetProperty("size").GetInt64();
        if (hash is not { Length: 64 } || hash.AsSpan().IndexOfAnyExcept("0123456789abcdef") >= 0 || size <= 0 || size > maximum)
        {
            throw new WorkspaceEnvironmentRefusalException(DamagedDescriptor);
        }
        return (hash, size);
    }

    private static async Task<bool> ValidImagesAsync(WorkspaceBootCacheDirectory directory,
        Dictionary<string, (string Hash, long Size)> pins, CancellationToken cancellationToken)
    {
        foreach (var name in Names)
        {
            await using var stream = directory.Read(name);
            if (stream is null || !await ValidStreamAsync(stream, pins[name], cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }
        return true;
    }

    private static async Task<bool> ValidStreamAsync(FileStream stream, (string Hash, long Size) pin, CancellationToken cancellationToken)
    {
        if (stream.Length != pin.Size) { return false; }
        stream.Position = 0;
        return string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)),
            pin.Hash, StringComparison.Ordinal);
    }

    private static async Task CopyAsync(Stream source, FileStream target, long size,
        IProgress<WorkspaceIsolationProgress>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        var lastPercent = -1;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > size)
            {
                throw new IOException("The workspace boot download exceeds its expected size.");
            }
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            var percent = (int)(total * 100 / size);
            if (percent != lastPercent)
            {
                progress?.Report(new WorkspaceIsolationProgress(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Downloading workspace boot images… {percent}%"),
                    Math.Clamp(percent / 100.0, 0, 1)));
                lastPercent = percent;
            }
        }
        if (total != size)
        {
            throw new WorkspaceEnvironmentRefusalException("The download of the workspace's boot images was interrupted. Try again to download a fresh copy.");
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static void ValidateDirectoryPath(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
        {
            if (directory.LinkTarget is null) { continue; }
            // macOS exposes its root-owned temporary directories through these
            // fixed system links. No user-controlled ancestor receives an exception.
            if (OperatingSystem.IsMacOS()
                && ((string.Equals(directory.FullName, "/var", StringComparison.Ordinal) && string.Equals(directory.ResolveLinkTarget(true)?.FullName, "/private/var", StringComparison.Ordinal))
                    || (string.Equals(directory.FullName, "/tmp", StringComparison.Ordinal) && string.Equals(directory.ResolveLinkTarget(true)?.FullName, "/private/tmp", StringComparison.Ordinal))))
            {
                continue;
            }
            throw new IOException("Workspace storage directories must not contain symbolic links.");
        }
    }

}
