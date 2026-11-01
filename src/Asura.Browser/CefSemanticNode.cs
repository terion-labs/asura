using System.Runtime.InteropServices;

namespace Asura.Browser;

internal sealed record CefSemanticNode(
    string Id,
    int? BackendNodeId,
    bool IsIgnored,
    string Role,
    string Name,
    string? ParentId,
    IReadOnlyList<string> ChildIds,
    IReadOnlyDictionary<string, string> Properties,
    string Value);

/// <summary>
/// A page's accessibility tree. <see cref="IsPartial"/> when the page was too
/// large to read whole and only its beginning, in document order, was read.
/// </summary>
internal sealed record CefSemanticTree(
    IReadOnlyList<CefSemanticNode> Nodes,
    bool IsPartial);

[StructLayout(LayoutKind.Auto)]
internal readonly record struct CefSemanticPoint(
    double X,
    double Y,
    double Width = 100,
    double Height = 40)
{
    public double TargetWidth => Math.Clamp(Math.Min(Width, Height), 4, 200);
}
