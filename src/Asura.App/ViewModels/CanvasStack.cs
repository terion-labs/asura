namespace Asura.App.ViewModels;

/// <summary>
/// A canvas that shares its place on screen with others of its kind: each
/// open workspace has one, and each tab has one within its workspace's.
/// </summary>
internal interface IStackedCanvas
{
    /// <summary>Whether it is on screen, which is not the same as being in front.</summary>
    bool IsCanvasShown { get; set; }

    /// <summary>Where it sits in the stack: one above the canvas it is covering.</summary>
    int CanvasDepth { get; set; }

    /// <summary>Whether its panels have been built and drawn, so showing it draws at once.</summary>
    bool IsCanvasDrawn { get; }
}

/// <summary>
/// Brings one canvas of a stack forward without ever showing an empty one.
///
/// A dock control builds only the layout it is showing — one that is not
/// shown has no visual tree at all, whatever it is given and however long it
/// waits — so the arriving canvas cannot be prepared out of sight. It is
/// shown straight away and the departing one is left on top of it, covering
/// it whole, until it has had frames enough to build. Then the cover comes
/// off and what is underneath is finished. A canvas built before is kept,
/// so coming back to it builds nothing and needs no cover; nor does a canvas
/// that was never drawn make one.
/// </summary>
internal static class CanvasStack
{
    /// <summary>
    /// How long the departing canvas keeps covering the arriving one. Frames
    /// rather than milliseconds, because what is being waited for is a dock
    /// control building its tree, which happens across passes. Three, because
    /// the measurement that found this showed the rebuild landing after the
    /// frame following the swap.
    /// </summary>
    private const int CoveringFrames = 3;

    public static void Bring(IStackedCanvas? arriving, IStackedCanvas? leaving)
    {
        if (arriving is not null)
        {
            arriving.CanvasDepth = 0;
            arriving.IsCanvasShown = true;
        }

        if (leaving is null || ReferenceEquals(leaving, arriving))
        {
            return;
        }

        if (arriving is null || arriving.IsCanvasDrawn || !leaving.IsCanvasDrawn)
        {
            leaving.IsCanvasShown = false;
            leaving.CanvasDepth = 0;
            return;
        }

        leaving.CanvasDepth = 1;
        Retire(leaving, CoveringFrames);
    }

    private static void Retire(IStackedCanvas canvas, int framesRemaining)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                // It came back to the front while it was covering; it is not
                // retiring any more.
                if (canvas.CanvasDepth == 0)
                {
                    return;
                }

                if (framesRemaining > 0)
                {
                    Retire(canvas, framesRemaining - 1);
                    return;
                }

                canvas.IsCanvasShown = false;
                canvas.CanvasDepth = 0;
            },
            Avalonia.Threading.DispatcherPriority.Background);
    }
}
