using Avalonia.Media.Imaging;
using FluentIcons.Common;

namespace Asura.App.Views.Components;

/// <summary>
/// The small, non-interactive summary that follows an in-app data drag.
/// The actual transfer payload stays separate so drop targets never depend on
/// presentation data.
/// </summary>
/// <param name="Symbol">The glyph drawn when there is no system icon.</param>
/// <param name="Title">What is being dragged.</param>
/// <param name="Detail">Where it comes from or what dropping it does.</param>
/// <param name="Icon">
/// The icon Finder shows for a single dragged file, when the system has one.
/// </param>
internal sealed record DragGhostPayload(
    Symbol Symbol,
    string Title,
    string Detail,
    Bitmap? Icon = null)
{
    public bool HasIcon => Icon is not null;
}
