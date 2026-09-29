using System.Text;
using System.Text.Json;

namespace Asura.Browser;

/// <summary>Recovers small custom click targets omitted by the accessibility role taxonomy.</summary>
internal static class CefClickableNodes
{
    public static IReadOnlyList<CefSemanticNode> Augment(
        IReadOnlyList<CefSemanticNode> nodes,
        JsonElement snapshot)
    {
        // Chromium can repeat generated InlineTextBox nodes in a full AX tree.
        // Match the semantic adapter's first-node-wins identity handling.
        nodes = [.. nodes.DistinctBy(node => node.Id, StringComparer.Ordinal)];
        var clickable = new HashSet<int>();
        foreach (var document in snapshot.GetProperty("documents").EnumerateArray())
        {
            var tree = document.GetProperty("nodes");
            if (!tree.TryGetProperty("isClickable", out var flags)) { continue; }
            var backendIds = tree.GetProperty("backendNodeId");
            foreach (var index in flags.GetProperty("index").EnumerateArray())
            {
                clickable.Add(backendIds[index.GetInt32()].GetInt32());
            }
        }

        var byId = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        return [.. nodes.Select(node =>
        {
            if (node.BackendNodeId is not { } id || !clickable.Contains(id)
                || CefBrowserSemanticAdapter.IsActionableRole(node.Role)
                || node.Role is "RootWebArea" or "WebArea" or "document" or "StaticText" or "InlineTextBox")
            {
                return node;
            }

            // A delegated listener on a page/container is not a useful element target.
            // Only promote a small text-only subtree, never swallow existing controls.
            var names = new List<string>();
            var pending = new Stack<string>(node.ChildIds.Reverse());
            var visited = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            while (pending.TryPop(out var childId))
            {
                if (!visited.Add(childId) || visited.Count > 32
                    || !byId.TryGetValue(childId, out var child)) { return node; }
                if (child.Role is "StaticText" or "static-text")
                {
                    if (!string.IsNullOrWhiteSpace(child.Name)) { names.Add(child.Name); }
                    continue;
                }
                if (child.Role is not ("generic" or "none" or "GenericContainer"
                    or "InlineTextBox" or "image")) { return node; }
                foreach (var descendant in child.ChildIds.Reverse()) { pending.Push(descendant); }
            }
            var name = string.IsNullOrWhiteSpace(node.Name) ? string.Join(" ", names) : node.Name;
            return string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > 256
                ? node
                : node with { IsIgnored = false, Role = "clickable", Name = name };
        })];
    }
}
