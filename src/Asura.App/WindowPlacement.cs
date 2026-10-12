using System.Text.Json.Serialization;
using Avalonia;

namespace Asura.App;

public enum WindowPlacementState
{
    Normal,
    Maximized,
    FullScreen,
}

/// <summary>
/// Where one main window was, so the next launch can put it back. Position is
/// in desktop pixels, as the platform reports it; size is in device-independent
/// units, as the window is sized. <see cref="Slot"/> is the window's recovery
/// slot, which keys its workspace snapshot.
/// </summary>
public sealed record WindowPlacement(
    int Slot,
    int X,
    int Y,
    double Width,
    double Height,
    WindowPlacementState State)
{
    /// <summary>More windows than this are not worth a recovery slot each.</summary>
    public const int MaximumSlots = 16;

    [JsonIgnore]
    public bool IsValid =>
        Slot is >= 0 and < MaximumSlots
        && double.IsFinite(Width)
        && double.IsFinite(Height)
        && Width > 0
        && Height > 0
        && Enum.IsDefined(State);
}

/// <summary>
/// The windows that were open, in the order they are reopened — the one that
/// was in front last — and the frame of the last window closed, which a new
/// window starts from when no other window is open to cascade from.
/// </summary>
public sealed record WindowPlacementSet(
    IReadOnlyList<WindowPlacement> Windows,
    WindowPlacement? LastClosed)
{
    public static WindowPlacementSet Empty { get; } = new([], null);
}

/// <summary>Persists window placements outside the profile database.</summary>
/// <remarks>
/// The database opens only after a startup PIN unlocks it, and a window has to
/// know where it goes before it is shown.
/// </remarks>
public interface IWindowPlacementStore
{
    WindowPlacementSet Load();

    void Save(WindowPlacementSet placements);
}

/// <summary>
/// Decides where windows go: back where they were when that is still on a
/// screen, and one title bar down and across from the window in front when
/// they are new.
/// </summary>
public static class WindowPlacementPolicy
{
    /// <summary>
    /// How much of a restored frame must remain on a screen for it to be put
    /// back where it was, in desktop pixels. Enough of the title bar to grab.
    /// </summary>
    private const int MinimumVisibleWidth = 120;
    private const int MinimumVisibleHeight = 40;

    /// <summary>
    /// Brings a saved frame onto the screens that exist now. A frame that is
    /// still mostly reachable keeps its position, nudged so its title bar is
    /// on screen and sized to fit; one left on a disconnected display is
    /// centred on the primary screen instead.
    /// </summary>
    public static PixelRect Clamp(
        PixelRect saved,
        IReadOnlyList<PixelRect> workingAreas,
        PixelRect primaryWorkingArea)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        var area = workingAreas
            .Select(candidate => (Area: candidate, Overlap: candidate.Intersect(saved)))
            .Where(item => item.Overlap.Width >= Math.Min(MinimumVisibleWidth, saved.Width)
                && item.Overlap.Height >= Math.Min(MinimumVisibleHeight, saved.Height))
            .OrderByDescending(item => (long)item.Overlap.Width * item.Overlap.Height)
            .Select(item => (PixelRect?)item.Area)
            .FirstOrDefault();
        if (area is not { } screen)
        {
            var width = Math.Min(saved.Width, primaryWorkingArea.Width);
            var height = Math.Min(saved.Height, primaryWorkingArea.Height);
            return new PixelRect(
                primaryWorkingArea.X + ((primaryWorkingArea.Width - width) / 2),
                primaryWorkingArea.Y + ((primaryWorkingArea.Height - height) / 2),
                width,
                height);
        }

        var fittedWidth = Math.Min(saved.Width, screen.Width);
        var fittedHeight = Math.Min(saved.Height, screen.Height);
        var x = Math.Clamp(saved.X, screen.X, screen.Right - fittedWidth);
        var y = Math.Clamp(saved.Y, screen.Y, screen.Bottom - fittedHeight);
        return new PixelRect(x, y, fittedWidth, fittedHeight);
    }

    /// <summary>
    /// Where a new window goes relative to the one in front: down and across
    /// by one title bar, as AppKit cascades, back to the top-left corner of
    /// the screen when that would run off it.
    /// </summary>
    public static PixelPoint Cascade(
        PixelRect from,
        int step,
        PixelRect workingArea)
    {
        var next = new PixelPoint(from.X + step, from.Y + step);
        if (next.X + from.Width > workingArea.Right
            || next.Y + from.Height > workingArea.Bottom)
        {
            return new PixelPoint(workingArea.X + step, workingArea.Y + step);
        }

        return next;
    }

    /// <summary>The lowest recovery slot no open window is using.</summary>
    public static int FirstFreeSlot(IEnumerable<int> used)
    {
        ArgumentNullException.ThrowIfNull(used);
        var taken = used.ToHashSet();
        for (var slot = 0; slot < WindowPlacement.MaximumSlots; slot++)
        {
            if (!taken.Contains(slot))
            {
                return slot;
            }
        }

        return -1;
    }
}
