using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Asura.Infrastructure.Tests;

public sealed class WorkspaceBootAssetsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"asura-boot-{Guid.NewGuid():N}");
    private readonly byte[] _kernel = Encoding.UTF8.GetBytes("test kernel");
    private readonly byte[] _initfs = Encoding.UTF8.GetBytes("test boot filesystem");
    private readonly byte[] _archive;

    public WorkspaceBootAssetsTests()
    {
        Directory.CreateDirectory(_directory);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry("kernel.bin").Open())
            {
                entry.Write(_kernel);
            }
            using var initfs = zip.CreateEntry("initfs.ext4").Open();
            initfs.Write(_initfs);
        }
        _archive = output.ToArray();
        WriteDescriptor(_archive, _kernel, _initfs);
    }

    [Fact]
    public async Task First_use_downloads_once_and_verified_cache_works_offline()
    {
        using var online = new Download(_archive);
        var path = await Assets().EnsureAsync(null, CancellationToken.None, online);
        Assert.Equal(_kernel, await File.ReadAllBytesAsync(Path.Combine(path, "kernel.bin"), CancellationToken.None));
        Assert.Equal(_initfs, await File.ReadAllBytesAsync(Path.Combine(path, "initfs.ext4"), CancellationToken.None));
        Assert.Equal(1, online.Calls);
        using var offline = new Download(null);
        Assert.Equal(path, await Assets().EnsureAsync(null, CancellationToken.None, offline));
        Assert.Equal(0, offline.Calls);
        Assert.Empty(Directory.GetFiles(path, "*.partial"));
        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(path, "kernel.bin")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(path, "initfs.ext4")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(path, "provision.lock")));
        }
    }

    [Fact]
    public async Task Concurrent_workspaces_share_one_download()
    {
        using var download = new Download(_archive);
        var paths = await Task.WhenAll(Assets().EnsureAsync(null, CancellationToken.None, download),
            Assets().EnsureAsync(null, CancellationToken.None, download));
        Assert.Equal(paths[0], paths[1]);
        Assert.Equal(1, download.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Truncated_or_corrupt_download_is_not_installed_and_retry_succeeds(bool truncate)
    {
        var badBytes = _archive.ToArray();
        badBytes[^1] ^= 1;
        using var bad = new Download(truncate ? badBytes[..^1] : badBytes);
        // A refusal is shown as written, so it says to try again for a fresh copy.
        var error = await Assert.ThrowsAsync<WorkspaceEnvironmentRefusalException>(() => Assets().EnsureAsync(null, CancellationToken.None, bad));
        Assert.Contains("Try again to download a fresh copy.", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_directory, "kernel.bin", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_directory, "*.partial", SearchOption.AllDirectories));
        using var good = new Download(_archive);
        _ = await Assets().EnsureAsync(null, CancellationToken.None, good);
        Assert.Equal(1, good.Calls);
    }

    [Fact]
    public async Task Corrupted_cache_is_repaired_instead_of_booted()
    {
        using var download = new Download(_archive);
        var path = await Assets().EnsureAsync(null, CancellationToken.None, download);
        await File.WriteAllBytesAsync(Path.Combine(path, "kernel.bin"), new byte[_kernel.Length], CancellationToken.None);
        using var offline = new Download(null);
        var error = await Assert.ThrowsAsync<WorkspaceEnvironmentRefusalException>(() => Assets().EnsureAsync(null, CancellationToken.None, offline));
        Assert.Contains("Connect to the internet, then try again.", error.Message, StringComparison.Ordinal);
        _ = await Assets().EnsureAsync(null, CancellationToken.None, download);
        Assert.Equal(2, download.Calls);
        Assert.Equal(_kernel, await File.ReadAllBytesAsync(Path.Combine(path, "kernel.bin"), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_cleans_partial_download_and_releases_lock()
    {
        using var cancellation = new CancellationTokenSource();
        using var download = new Download(_archive, cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Assets().EnsureAsync(null, cancellation.Token, download));
        Assert.Empty(Directory.GetFiles(_directory, "*.partial", SearchOption.AllDirectories));
        using var retry = new Download(_archive);
        _ = await Assets().EnsureAsync(null, CancellationToken.None, retry);
    }

    [Fact]
    public async Task Image_checksum_is_checked_independently_of_archive_checksum()
    {
        WriteDescriptor(_archive, Encoding.UTF8.GetBytes("bad kernel!"), _initfs);
        using var download = new Download(_archive);
        await Assert.ThrowsAsync<IOException>(() => Assets().EnsureAsync(null, CancellationToken.None, download));
        Assert.Empty(Directory.GetFiles(_directory, "kernel.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Development_sidecar_uses_identical_validation_without_network()
    {
        var local = Path.Combine(_directory, "local.zip");
        await File.WriteAllBytesAsync(local, _archive, CancellationToken.None);
        using var offline = new Download(null);
        _ = await Assets().EnsureAsync(null, CancellationToken.None, offline, local);
        Assert.Equal(0, offline.Calls);
    }

    [Theory]
    [InlineData("download.partial")]
    [InlineData("kernel.bin.partial")]
    [InlineData("initfs.ext4.partial")]
    [InlineData("kernel.bin")]
    public async Task Guest_planted_cache_links_cannot_overwrite_their_host_target(string name)
    {
        var target = Path.Combine(_directory, "host-file");
        await File.WriteAllTextAsync(target, "preserve host data", CancellationToken.None);
        var cache = Path.Combine(_directory, "cache", Convert.ToHexStringLower(SHA256.HashData(_archive)));
        Directory.CreateDirectory(cache);
        File.CreateSymbolicLink(Path.Combine(cache, name), target);
        using var download = new Download(_archive);

        _ = await Assets().EnsureAsync(null, CancellationToken.None, download);

        Assert.Equal("preserve host data", await File.ReadAllTextAsync(target, CancellationToken.None));
    }

    [Fact]
    public async Task Linked_cache_directory_is_refused_before_download()
    {
        var outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(_directory, "cache"), outside);
        using var download = new Download(_archive);

        await Assert.ThrowsAsync<IOException>(() => Assets().EnsureAsync(null, CancellationToken.None, download));

        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task Linked_cache_ancestor_is_refused_before_creating_outside_directories()
    {
        var outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(outside);
        var parent = Path.Combine(_directory, "linked-parent");
        Directory.CreateSymbolicLink(parent, outside);
        using var download = new Download(_archive);
        var assets = new WorkspaceBootAssets(Path.Combine(_directory, "boot-assets.json"),
            Path.Combine(parent, "cache"), new Uri("https://example.invalid/boot.zip"));

        await Assert.ThrowsAsync<IOException>(() => assets.EnsureAsync(null, CancellationToken.None, download));

        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task Replacing_cache_ancestor_during_download_cannot_redirect_install_or_cleanup()
    {
        if (!OperatingSystem.IsMacOS()) { return; }
        var hash = Convert.ToHexStringLower(SHA256.HashData(_archive));
        var outside = Path.Combine(_directory, "outside");
        var outsideImages = Path.Combine(outside, hash);
        Directory.CreateDirectory(outsideImages);
        var victim = Path.Combine(outsideImages, "kernel.bin");
        await File.WriteAllTextAsync(victim, "preserve host data", CancellationToken.None);
        File.SetUnixFileMode(victim, UnixFileMode.UserRead);
        var cache = Path.Combine(_directory, "cache");
        var moved = Path.Combine(_directory, "moved-cache");
        using var download = new Download(_archive, beforeResponse: () =>
        {
            Directory.Move(cache, moved);
            Directory.CreateSymbolicLink(cache, outside);
        });

        await Assert.ThrowsAsync<IOException>(() => Assets().EnsureAsync(null, CancellationToken.None, download));

        Assert.Equal("preserve host data", await File.ReadAllTextAsync(victim, CancellationToken.None));
        Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(victim));
        Assert.Equal([victim], Directory.GetFileSystemEntries(outsideImages));
        Assert.Empty(Directory.GetFiles(moved, "*.partial", SearchOption.AllDirectories));
        Assert.Equal(_kernel, await File.ReadAllBytesAsync(Path.Combine(moved, hash, "kernel.bin"), CancellationToken.None));
    }

    [Fact]
    public async Task Held_cache_descriptor_keeps_each_mutation_inside_original_directory()
    {
        if (!OperatingSystem.IsMacOS()) { return; }
        var parent = Path.Combine(_directory, "cache");
        var path = Path.Combine(parent, "images");
        using var cache = new WorkspaceBootCacheDirectory(path);
        var outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "images"));
        var victim = Path.Combine(outside, "images", "kernel.bin");
        await File.WriteAllTextAsync(victim, "preserve host data", CancellationToken.None);
        var moved = Path.Combine(_directory, "moved-cache");
        Directory.Move(parent, moved);
        Directory.CreateSymbolicLink(parent, outside);

        await using (var target = cache.CreateNew("image.partial"))
        {
            await target.WriteAsync(_kernel, CancellationToken.None);
            await target.FlushAsync(CancellationToken.None);
            cache.Install("image.partial", "kernel.bin", target);
        }
        await using (var read = cache.Read("kernel.bin"))
        {
            Assert.NotNull(read);
            Assert.Equal(_kernel.Length, read.Length);
        }
        cache.Delete("image.partial");
        Assert.Throws<IOException>(cache.RequireUnchangedPath);

        Assert.Equal("preserve host data", await File.ReadAllTextAsync(victim, CancellationToken.None));
        Assert.Equal([victim], Directory.GetFileSystemEntries(Path.Combine(outside, "images")));
        Assert.Equal(_kernel, await File.ReadAllBytesAsync(Path.Combine(moved, "images", "kernel.bin"), CancellationToken.None));
    }

    private WorkspaceBootAssets Assets() => new(Path.Combine(_directory, "boot-assets.json"),
        Path.Combine(_directory, "cache"), new Uri("https://example.invalid/boot.zip"));

    private void WriteDescriptor(byte[] archive, byte[] kernel, byte[] initfs)
    {
        static object Pin(byte[] bytes) => new { sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), size = bytes.Length };
        File.WriteAllText(Path.Combine(_directory, "boot-assets.json"), JsonSerializer.Serialize(new
        {
            sha256 = Convert.ToHexStringLower(SHA256.HashData(archive)),
            size = archive.Length,
            files = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kernel.bin"] = Pin(kernel),
                ["initfs.ext4"] = Pin(initfs),
            },
        }));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class Download(byte[]? bytes, CancellationTokenSource? cancellation = null, Action? beforeResponse = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            beforeResponse?.Invoke();
            cancellation?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes is null)
            {
                throw new HttpRequestException("Offline fixture");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
