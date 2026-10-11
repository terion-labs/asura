using System.Security.Cryptography;
using System.Text.Json;
using Asura.Application;

namespace Asura.Infrastructure;

public sealed partial class WorkspaceSdkIsolationProvider
{
    // These refusals are shown as written, so each ends with the step that gets past it.
    private const string MissingDisk = "The persistent workspace disk is missing. "
        + "Restore it from a backup, or choose Recreate Environment in the workspace's settings.";

    private const string IncompleteBootSettings = "This workspace's saved boot settings are incomplete. "
        + "Restore them from a backup, or choose Recreate Environment in the workspace's settings.";

    private const string InvalidBootIdentity = "This workspace's saved boot image identity is invalid. "
        + "Restore it from a backup, or choose Recreate Environment in the workspace's settings.";

    // Boot files belong to the environment, just like its writable root disk.
    // Only initial creation can consult the app's current descriptor or download.
    private async Task<string> SelectWorkspaceBootImagesAsync(string directory,
        IProgress<WorkspaceIsolationProgress>? progress, CancellationToken cancellationToken)
    {
        var pinPath = Path.Combine(directory, "boot-images.json");
        try
        {
            if (File.Exists(pinPath))
            {
                var pinned = JsonSerializer.Deserialize(await File.ReadAllTextAsync(pinPath, cancellationToken).ConfigureAwait(false),
                    WorkspaceSdkJsonContext.Default.WorkspaceSdkBootImages)
                    ?? throw new WorkspaceEnvironmentRefusalException("This workspace's saved boot image identity is missing. "
                        + "Restore it from a backup, or choose Recreate Environment in the workspace's settings.");
                progress?.Report(new WorkspaceIsolationProgress("Verifying this workspace's saved boot images…"));
                await VerifyWorkspaceBootImagesAsync(pinned, cancellationToken).ConfigureAwait(false);
                return pinned.Directory;
            }

            string bootDirectory;
            var runtimePath = Path.Combine(directory, "runtime.json");
            if (File.Exists(runtimePath))
            {
                // Older versions kept the selected paths only in runtime.json. Adopt
                // those previously provisioned local files, never the new app's image.
                // Legacy files have no retained per-file digest: establish their local
                // baseline once, then check it on every subsequent start.
                using var runtime = JsonDocument.Parse(await File.ReadAllTextAsync(runtimePath, cancellationToken).ConfigureAwait(false));
                var config = runtime.RootElement;
                if (config.GetProperty("initialArguments").GetArrayLength() == 0)
                {
                    throw new WorkspaceEnvironmentRefusalException(IncompleteBootSettings);
                }
                var kernel = config.GetProperty("kernelPath").GetString();
                var initfs = config.GetProperty("initfsPath").GetString();
                bootDirectory = Path.GetDirectoryName(kernel) ?? throw new WorkspaceEnvironmentRefusalException(IncompleteBootSettings);
                if (!Path.IsPathFullyQualified(bootDirectory)
                    || !string.Equals(kernel, Path.Combine(bootDirectory, "kernel.bin"), StringComparison.Ordinal)
                    || !string.Equals(initfs, Path.Combine(bootDirectory, "initfs.ext4"), StringComparison.Ordinal)
                    || !(string.Equals(config.GetProperty("rootfsPath").GetString(), Path.Combine(directory, "rootfs.ext4"), StringComparison.Ordinal)
                        || string.Equals(config.GetProperty("rootfsPath").GetString(), Path.Combine(directory, "preparing.ext4"), StringComparison.Ordinal)))
                {
                    throw new WorkspaceEnvironmentRefusalException("This workspace's saved boot settings don't match its environment. "
                        + "Restore them from a backup, or choose Recreate Environment in the workspace's settings.");
                }
                if (string.Equals(config.GetProperty("initialArguments")[0].GetString(), "/sbin/init", StringComparison.Ordinal)
                    && !File.Exists(Path.Combine(directory, "rootfs.ext4")))
                {
                    throw new WorkspaceEnvironmentRefusalException(MissingDisk);
                }
            }
            else if (File.Exists(Path.Combine(directory, "rootfs.ext4"))
                || File.Exists(Path.Combine(directory, "preparing.ext4"))
                || File.Exists(Path.Combine(directory, "disk.ready")))
            {
                throw new WorkspaceEnvironmentRefusalException("The existing workspace's boot image identity is missing. "
                    + "Restore its saved boot files from a backup, or choose Recreate Environment in the workspace's settings.");
            }
            else
            {
                bootDirectory = await _prepareBootAssets(progress, cancellationToken).ConfigureAwait(false);
            }

            var images = new WorkspaceSdkBootImages(bootDirectory,
                await ReadBootImagePinAsync(Path.Combine(bootDirectory, "kernel.bin"), cancellationToken).ConfigureAwait(false),
                await ReadBootImagePinAsync(Path.Combine(bootDirectory, "initfs.ext4"), cancellationToken).ConfigureAwait(false));
            var pending = Path.Combine(directory, $"boot-images-{Guid.NewGuid():N}.tmp");
            try
            {
                await WritePrivateAsync(pending, JsonSerializer.SerializeToUtf8Bytes(images,
                    WorkspaceSdkJsonContext.Default.WorkspaceSdkBootImages), cancellationToken).ConfigureAwait(false);
                File.Move(pending, pinPath, overwrite: false);
            }
            finally
            {
                File.Delete(pending);
            }
            return bootDirectory;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new WorkspaceEnvironmentRefusalException(InvalidBootIdentity, exception);
        }
    }

    private static async Task VerifyWorkspaceBootImagesAsync(WorkspaceSdkBootImages images, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(images.Directory) || !Path.IsPathFullyQualified(images.Directory)
            || images.Kernel is null || images.Initfs is null)
        {
            throw new WorkspaceEnvironmentRefusalException(InvalidBootIdentity);
        }
        var kernel = await ReadBootImagePinAsync(Path.Combine(images.Directory, "kernel.bin"), cancellationToken).ConfigureAwait(false);
        var initfs = await ReadBootImagePinAsync(Path.Combine(images.Directory, "initfs.ext4"), cancellationToken).ConfigureAwait(false);
        if (images.Kernel != kernel || images.Initfs != initfs)
        {
            throw new WorkspaceEnvironmentRefusalException("This workspace's saved boot images have changed. "
                + "Restore them from a backup, or choose Recreate Environment in the workspace's settings.");
        }
    }

    private static async Task<WorkspaceSdkBootImagePin> ReadBootImagePinAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length is <= 0 or > 1024L * 1024 * 1024)
        {
            throw new WorkspaceEnvironmentRefusalException("This workspace's saved boot images are missing or invalid. "
                + "Restore them from a backup, or choose Recreate Environment in the workspace's settings.");
        }
        await using var stream = File.OpenRead(path);
        return new(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)), file.Length);
    }
}
