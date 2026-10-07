using Asura.App.ViewModels;
using Asura.Docker;

namespace Asura.App.Tests;

public sealed class DockerResourceItemViewModelTests
{
    /// <summary>
    /// A container's tile reports its state; an image, a volume, and a network
    /// have no state, so their tiles carry their kind instead of a neutral
    /// status grey that would make a mixed list unscannable.
    /// </summary>
    [Fact]
    public void Only_containers_carry_a_status_tone_and_other_kinds_carry_their_kind()
    {
        var container = DockerResourceItemViewModel.From(new DockerContainerSummary(
            "container-id",
            "api",
            "registry.example/api:latest",
            "running",
            "Up 5 minutes",
            "443/tcp",
            "5 minutes ago",
            "1%",
            "10MiB / 1GiB",
            "1kB / 2kB",
            "3kB / 4kB"));
        var image = DockerResourceItemViewModel.From(
            new DockerImageSummary("image-id", "registry.example/api", "latest", "25MB", "today"));
        var volume = DockerResourceItemViewModel.From(
            new DockerVolumeSummary("app-data", "local", "local", "/private/daemon/path"));
        var network = DockerResourceItemViewModel.From(
            new DockerNetworkSummary("network-id", "app-net", "bridge", "local", "today"));

        Assert.Equal(StatusTone.Success, container.StatusTone);
        Assert.False(container.IsImage || container.IsVolume || container.IsNetwork);
        Assert.Null(image.StatusTone);
        Assert.True(image.IsImage);
        Assert.Null(volume.StatusTone);
        Assert.True(volume.IsVolume);
        Assert.Null(network.StatusTone);
        Assert.True(network.IsNetwork);
    }
}
