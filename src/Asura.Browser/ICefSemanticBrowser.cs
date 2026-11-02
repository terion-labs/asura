namespace Asura.Browser;

/// <summary>
/// The narrow, private CDP seam used by semantic automation. Production uses
/// CEF's acknowledged DevTools methods; tests use a deterministic fake.
/// </summary>
internal interface ICefSemanticBrowser
{
    /// <summary>
    /// The page's accessibility tree, or only the subtree of the element
    /// <paramref name="withinBackendNodeId"/> from its child at
    /// <paramref name="offset"/>; null when that element is gone.
    /// </summary>
    Task<CefSemanticTree?> ReadAccessibilityTreeAsync(
        int? withinBackendNodeId = null,
        int offset = 0);

    Task<CefSemanticNode?> ReadAccessibilityNodeAsync(int backendNodeId);

    Task<CefSemanticPoint?> PrepareClickPointAsync(int backendNodeId, CancellationToken cancellationToken = default);

    Task<bool> HitTestIncludesAsync(
        CefSemanticPoint point,
        int backendNodeId);

    Task<bool> DispatchClickAsync(
        CefSemanticPoint point,
        int backendNodeId,
        CancellationToken cancellationToken = default);

    Task ReplaceFocusedTextAsync(int backendNodeId, string text, CancellationToken cancellationToken = default);

    Task<bool> IsVisibleAsync(int backendNodeId);
}
