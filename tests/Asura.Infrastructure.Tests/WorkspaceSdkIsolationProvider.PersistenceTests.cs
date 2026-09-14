using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed partial class WorkspaceSdkIsolationProviderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task App_update_reuses_workspace_boot_files_and_disk_without_calling_current_image_provider(bool legacy)
    {
        var disk = await SeedDiskAsync();
        var request = new WorkspaceIsolationPrepareRequest(_workspace);
        if (!legacy)
        {
            var oldProvider = Provider();
            var old = Success(await oldProvider.PrepareAsync(request, CancellationToken.None));
            _ = Success(await oldProvider.StopAsync(old, CancellationToken.None));
        }
        var saved = "user-installed packages and guest-only files";
        await File.WriteAllTextAsync(disk, saved, CancellationToken.None);
        var updatedProvider = Provider((_, _) => throw new InvalidOperationException("An app update must not select or download new boot images."));

        var binding = Success(await updatedProvider.PrepareAsync(request, CancellationToken.None));

        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(disk)!, "runtime.json"), CancellationToken.None));
        Assert.Equal(Path.Combine(BootDirectory, "kernel.bin"), config.RootElement.GetProperty("kernelPath").GetString());
        Assert.Equal(Path.Combine(BootDirectory, "initfs.ext4"), config.RootElement.GetProperty("initfsPath").GetString());
        Assert.Equal(disk, config.RootElement.GetProperty("rootfsPath").GetString());
        Assert.DoesNotContain(_runner.Commands, static command => command.Arguments[0] == "prepare");
        _ = Success(await updatedProvider.StopAsync(binding, CancellationToken.None));
        Assert.Equal(saved, await File.ReadAllTextAsync(disk, CancellationToken.None));
    }

    [Fact]
    public async Task Updated_default_image_does_not_change_existing_workspace_image()
    {
        var disk = await SeedDiskAsync();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(disk)!, "image.txt"), "ubuntu:previous-default", CancellationToken.None);
        var provider = Provider();
        var request = new WorkspaceIsolationPrepareRequest(_workspace);
        var binding = Success(await provider.PrepareAsync(request, CancellationToken.None));
        Assert.Equal("ubuntu:previous-default", binding.RuntimeImageReference);
        Assert.Null(binding.ImageReference);
        var shared = Success(await provider.PrepareAsync(request, CancellationToken.None));
        _ = Success(await provider.StopAsync(binding, CancellationToken.None));
        _ = Success(await provider.StopAsync(shared, CancellationToken.None));
    }

    [Theory]
    [InlineData("kernel.bin", false)]
    [InlineData("initfs.ext4", false)]
    [InlineData("kernel.bin", true)]
    [InlineData("initfs.ext4", true)]
    public async Task Missing_or_changed_pinned_boot_files_fail_without_download_or_disk_replacement(string name, bool corrupt)
    {
        var disk = await SeedDiskAsync();
        var provider = Provider();
        var request = new WorkspaceIsolationPrepareRequest(_workspace);
        var original = Success(await provider.PrepareAsync(request, CancellationToken.None));
        _ = Success(await provider.StopAsync(original, CancellationToken.None));
        if (corrupt)
        {
            await File.WriteAllTextAsync(Path.Combine(BootDirectory, name), "changed boot file", CancellationToken.None);
        }
        else
        {
            File.Delete(Path.Combine(BootDirectory, name));
        }
        var starts = _runner.Starts.Count;
        var updated = Provider((_, _) => throw new InvalidOperationException("Existing workspace must not download."));
        var failed = Assert.IsType<WorkspaceIsolationResult<WorkspaceIsolationBinding>.Failure>(
            await updated.PrepareAsync(request, CancellationToken.None));
        Assert.Contains("saved boot images", failed.Error.Message, StringComparison.Ordinal);
        Assert.Equal(starts, _runner.Starts.Count);
        Assert.Equal("persistent disk", await File.ReadAllTextAsync(disk, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_persistent_disk_is_not_silently_recreated(bool legacy)
    {
        var disk = await SeedDiskAsync();
        var provider = Provider();
        var request = new WorkspaceIsolationPrepareRequest(_workspace);
        if (!legacy)
        {
            var binding = Success(await provider.PrepareAsync(request, CancellationToken.None));
            _ = Success(await provider.StopAsync(binding, CancellationToken.None));
        }
        File.Delete(disk);
        var commands = _runner.Commands.Count;
        var failed = Assert.IsType<WorkspaceIsolationResult<WorkspaceIsolationBinding>.Failure>(
            await provider.PrepareAsync(request, CancellationToken.None));
        Assert.Contains("persistent workspace disk is missing", failed.Error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(disk));
        Assert.Equal(commands, _runner.Commands.Count);
    }

    [Fact]
    public async Task Only_creation_and_explicit_recreation_select_current_boot_images()
    {
        EnableDiskCreation();
        var selected = 0;
        var latest = BootDirectory;
        var provider = Provider((_, _) => { selected++; return Task.FromResult(latest); });
        var request = new WorkspaceIsolationPrepareRequest(_workspace, imageReference: "custom:1");
        var first = Success(await provider.PrepareAsync(request, CancellationToken.None));
        _ = Success(await provider.StopAsync(first, CancellationToken.None));
        var disk = Path.Combine(_directory, "state", first.ResourceName, "rootfs.ext4");
        await File.WriteAllTextAsync(disk, "installed tooling", CancellationToken.None);
        latest = Path.Combine(_directory, "new-boot");
        Directory.CreateDirectory(latest);
        await File.WriteAllTextAsync(Path.Combine(latest, "kernel.bin"), "new kernel", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(latest, "initfs.ext4"), "new initfs", CancellationToken.None);
        var restarted = Success(await provider.PrepareAsync(request, CancellationToken.None));
        Assert.Equal(1, selected);
        _ = Success(await provider.StopAsync(restarted, CancellationToken.None));
        Assert.Equal("installed tooling", await File.ReadAllTextAsync(disk, CancellationToken.None));

        _ = Assert.IsType<WorkspaceIsolationResult<Unit>.Success>(await provider.RecreateAsync(request, null, CancellationToken.None));
        var retired = Assert.Single(Directory.GetDirectories(Path.Combine(_directory, "state"), first.ResourceName + ".retired-*"));
        Assert.Equal("installed tooling", await File.ReadAllTextAsync(Path.Combine(retired, "rootfs.ext4"), CancellationToken.None));
        var recreated = Success(await provider.PrepareAsync(request, CancellationToken.None));
        Assert.Equal(2, selected);
        Assert.Equal("fresh disk", await File.ReadAllTextAsync(disk, CancellationToken.None));
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(disk)!, "runtime.json"), CancellationToken.None));
        Assert.Equal(Path.Combine(latest, "kernel.bin"), config.RootElement.GetProperty("kernelPath").GetString());
        _ = Success(await provider.StopAsync(recreated, CancellationToken.None));
    }

    private void EnableDiskCreation() => _runner.Respond = command =>
    {
        if (command.Arguments[0] == "prepare")
        {
            File.WriteAllText(command.Arguments[4], "fresh disk");
        }
        return new WorkspaceGatewayCommandResult(0, "20 999");
    };

    [Fact]
    public async Task Existing_disk_without_boot_identity_fails_without_downloading()
    {
        var disk = await SeedDiskAsync();
        File.Delete(Path.Combine(Path.GetDirectoryName(disk)!, "runtime.json"));
        var provider = Provider((_, _) => throw new InvalidOperationException("Existing disks cannot select new images."));
        var failed = Assert.IsType<WorkspaceIsolationResult<WorkspaceIsolationBinding>.Failure>(
            await provider.PrepareAsync(new WorkspaceIsolationPrepareRequest(_workspace), CancellationToken.None));
        Assert.Contains("boot image identity is missing", failed.Error.Message, StringComparison.Ordinal);
        Assert.Equal("persistent disk", await File.ReadAllTextAsync(disk, CancellationToken.None));
        Assert.Empty(_runner.Commands);
    }

    [Fact]
    public async Task Interrupted_creation_keeps_its_original_boot_selection_on_retry()
    {
        var selections = 0;
        var provider = Provider((_, _) => { selections++; return Task.FromResult(BootDirectory); });
        var request = new WorkspaceIsolationPrepareRequest(_workspace, imageReference: "custom:1");
        _runner.CommandResult = new WorkspaceGatewayCommandResult(1, "interrupted unpack");
        _ = Assert.IsType<WorkspaceIsolationResult<WorkspaceIsolationBinding>.Failure>(
            await provider.PrepareAsync(request, CancellationToken.None));
        Assert.Equal(1, selections);
        EnableDiskCreation();
        var updatedProvider = Provider((_, _) => throw new InvalidOperationException("Retry must retain the selected boot images."));
        var binding = Success(await updatedProvider.PrepareAsync(request, CancellationToken.None));
        _ = Success(await updatedProvider.StopAsync(binding, CancellationToken.None));
        Assert.Equal(1, selections);
    }

    [Fact]
    public async Task Creating_another_workspace_does_not_change_existing_workspace_boot_files()
    {
        var disk = await SeedDiskAsync();
        EnableDiskCreation();
        var latest = Path.Combine(_directory, "new-boot");
        Directory.CreateDirectory(latest);
        await File.WriteAllTextAsync(Path.Combine(latest, "kernel.bin"), "new kernel", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(latest, "initfs.ext4"), "new initfs", CancellationToken.None);
        var selected = 0;
        var provider = Provider((_, _) => { selected++; return Task.FromResult(latest); });
        var firstRequest = new WorkspaceIsolationPrepareRequest(_workspace);
        var first = Success(await provider.PrepareAsync(firstRequest, CancellationToken.None));
        _ = Success(await provider.StopAsync(first, CancellationToken.None));
        var second = Success(await provider.PrepareAsync(
            new WorkspaceIsolationPrepareRequest(new WorkspaceId("another-workspace"), imageReference: "custom:1"), CancellationToken.None));
        var restarted = Success(await provider.PrepareAsync(firstRequest, CancellationToken.None));
        Assert.Equal(1, selected);
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(disk)!, "runtime.json"), CancellationToken.None));
        Assert.Equal(Path.Combine(BootDirectory, "kernel.bin"), config.RootElement.GetProperty("kernelPath").GetString());
        _ = Success(await provider.StopAsync(second, CancellationToken.None));
        _ = Success(await provider.StopAsync(restarted, CancellationToken.None));
    }
}
