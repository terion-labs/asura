using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.ViewModels;

internal static class FileLocationPresentation
{
    /// <summary>
    /// Why a typed location can't be opened. The location model's own checks
    /// name parameters and traversal rules, so they go to diagnostics and the
    /// person reads which folder names a location can't hold.
    /// </summary>
    public static string DescribeLocationRefusal(Exception exception) =>
        FailureReason.Refusal(
            "files.location.parse.refused",
            exception,
            "Asura can’t open that location. A folder in it can’t be named “.” or “..” or contain a null character. "
            + "Check the location, then try again.");

    /// <summary>Why a new or changed file or folder name can't be used.</summary>
    public static string DescribeNameRefusal(Exception exception) =>
        FailureReason.Refusal(
            "files.name.refused",
            exception,
            "That name can’t be used. A name can’t be “.” or “..” or contain “/” or a null character. Choose another name.");

    public static FilePanelLocation Parse(
        FileProviderProfileDescriptor profile,
        string input)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var value = input.Trim();
        return profile.Root.Address switch
        {
            FilePanelAddress.Hierarchical => new FilePanelLocation(
                profile.Id,
                profile.Root.Authority,
                new FilePanelAddress.Hierarchical(FilePanelPath.FromSegments(
                    value.Trim('/').Length == 0
                        ? []
                        : value.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                            .Select(segment => new FilePanelPathSegment(segment))))),
            FilePanelAddress.ObjectKey or FilePanelAddress.ContainerRoot when value.Length == 0 =>
                new FilePanelLocation(
                    profile.Id,
                    profile.Root.Authority,
                    new FilePanelAddress.ContainerRoot()),
            FilePanelAddress.ObjectKey or FilePanelAddress.ContainerRoot => new FilePanelLocation(
                profile.Id,
                profile.Root.Authority,
                new FilePanelAddress.ObjectKey(value)),
            _ => throw FailureReason.Readable(new ArgumentException(
                "Locations for this provider can’t be typed. Choose a folder in the list instead.")),
        };
    }

    public static string Display(FilePanelLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.Address switch
        {
            FilePanelAddress.Hierarchical hierarchical => hierarchical.Path.IsRoot
                ? "/"
                : "/" + string.Join('/', hierarchical.Path.Segments.Select(segment => segment.Value)),
            FilePanelAddress.ObjectKey value => value.Key,
            FilePanelAddress.ContainerRoot => string.Empty,
            _ => string.Empty,
        };
    }

    public static string ChildDisplay(
        FileProviderProfileDescriptor profile,
        string name)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return profile.Root.Address switch
        {
            FilePanelAddress.Hierarchical => $"/{name}",
            FilePanelAddress.ObjectKey or FilePanelAddress.ContainerRoot => name,
            _ => name,
        };
    }

    public static FilePanelLocation Child(
        FilePanelLocation parent,
        string name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return parent.Address switch
        {
            FilePanelAddress.Hierarchical =>
                parent.Child(new FilePanelPathSegment(name)),
            FilePanelAddress.ObjectKey value => new FilePanelLocation(
                parent.ProviderProfileId,
                parent.Authority,
                new FilePanelAddress.ObjectKey(
                    $"{value.Key.TrimEnd('/')}/{name}")),
            FilePanelAddress.ContainerRoot => new FilePanelLocation(
                parent.ProviderProfileId,
                parent.Authority,
                new FilePanelAddress.ObjectKey(name)),
            _ => throw FailureReason.Readable(new ArgumentException(
                "This destination can't hold files or folders. Choose another one.")),
        };
    }
}
