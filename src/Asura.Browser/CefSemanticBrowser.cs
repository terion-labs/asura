using System.Text.Json;
using Asura.Application;
using Exclr8Cef;
using Exclr8Cef.Cdp;

namespace Asura.Browser;

/// <summary>
/// Executes semantic browser operations through acknowledged CDP round trips.
/// No page-realm selector, script, or DOM event shortcut is used for input.
/// </summary>
internal sealed class CefSemanticBrowser : ICefSemanticBrowser
{
    private const int MaximumRevealAttempts = 6;

    /// <summary>
    /// How much of a page too large to read whole is read: far more than a
    /// snapshot returns, so a filter still finds what is near the start.
    /// </summary>
    private const int PartialTreeNodeBudget = 6_000;
    private const int PartialTreeBatchSize = 64;

    /// <summary>The error the host puts in place of an oversized DevTools reply.</summary>
    private const string HostByteLimitMessage = "CDP response exceeds the host byte limit.";
    private const double ViewportMarginCss = 12;
    private readonly CefBrowser _browser;
    private readonly CefHumanizedInput _humanizedInput;
    private bool _domainsEnabled;
    private readonly HashSet<int> _customClickTargets = [];

    public CefSemanticBrowser(
        CefBrowser browser,
        CefHumanizedInput humanizedInput)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _humanizedInput = humanizedInput
            ?? throw new ArgumentNullException(nameof(humanizedInput));
    }

    public async Task<CefSemanticTree> ReadAccessibilityTreeAsync()
    {
        await EnsureDomainsEnabledAsync().ConfigureAwait(false);
        var reply = await _browser.ExecuteDevToolsMethodAsync(
                "Accessibility.getFullAXTree",
                "{\"depth\":64}")
            .ConfigureAwait(false);
        IReadOnlyList<CefSemanticNode> nodes;
        bool isPartial;
        using (var document = JsonDocument.Parse(reply))
        {
            if (Result(document, "Accessibility.getFullAXTree") is { } result)
            {
                nodes = [.. result.GetProperty("nodes").EnumerateArray().Select(Project)];
                isPartial = false;
            }
            else
            {
                nodes = await ReadAccessibilityTreeBeginningAsync().ConfigureAwait(false);
                isPartial = true;
            }
        }

        var domReply = await _browser.ExecuteDevToolsMethodAsync(
                "DOMSnapshot.captureSnapshot", "{\"computedStyles\":[]}")
            .ConfigureAwait(false);
        using var dom = JsonDocument.Parse(domReply);
        // A page too large to snapshot keeps its semantic controls; only
        // elements that are clickable through a script listener go unnamed.
        var projected = Result(dom, "DOMSnapshot.captureSnapshot") is { } snapshot
            ? CefClickableNodes.Augment(nodes, snapshot)
            : nodes;
        _customClickTargets.Clear();
        foreach (var node in projected)
        {
            if (string.Equals(node.Role, "clickable", StringComparison.Ordinal) && node.BackendNodeId is { } id) { _customClickTargets.Add(id); }
        }
        return new CefSemanticTree(projected, isPartial);
    }

    /// <summary>
    /// The beginning of a page whose whole tree is larger than the host
    /// accepts in one reply: nodes are read a parent's children at a time,
    /// nearest the start of the document first, until the budget is spent.
    /// Each reply stays bounded by the host's limit, and a parent whose
    /// children are themselves too many to read is left without them.
    /// </summary>
    private async Task<IReadOnlyList<CefSemanticNode>> ReadAccessibilityTreeBeginningAsync()
    {
        var rootReply = await _browser.ExecuteDevToolsMethodAsync(
                "Accessibility.getRootAXNode", "{}")
            .ConfigureAwait(false);
        using var rootDocument = JsonDocument.Parse(rootReply);
        var root = Project(
            (Result(rootDocument, "Accessibility.getRootAXNode")
                ?? throw new InvalidOperationException(
                    "Chromium did not return the page's accessibility root."))
            .GetProperty("node"));
        var nodes = new List<CefSemanticNode> { root };
        // Chromium lists a parent's children by the ids of its full tree;
        // the children it returns for that parent are what the walk links.
        var childrenRead = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // An ignored node's children also come back with its parent's, so one
        // node can arrive more than once; it is read, and read into, once.
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var unread = new List<CefSemanticNode>();
        if (root.ChildIds.Count > 0)
        {
            unread.Add(root);
        }

        while (unread.Count > 0 && nodes.Count < PartialTreeNodeBudget)
        {
            var parents = unread.Take(PartialTreeBatchSize).ToArray();
            unread.RemoveRange(0, parents.Length);
            var replies = await Task.WhenAll(parents.Select(parent =>
                    _browser.ExecuteDevToolsMethodAsync(
                        "Accessibility.getChildAXNodes",
                        "{\"id\":\"" + JsonEncodedText.Encode(parent.Id) + "\"}")))
                .ConfigureAwait(false);
            var children = new List<CefSemanticNode>();
            for (var index = 0; index < parents.Length; index++)
            {
                using var document = JsonDocument.Parse(replies[index]);
                if (Result(document, "Accessibility.getChildAXNodes") is not { } result)
                {
                    continue;
                }

                var read = new List<string>();
                foreach (var child in result.GetProperty("nodes").EnumerateArray())
                {
                    var node = Project(child) with { ParentId = parents[index].Id };
                    read.Add(node.Id);
                    if (!seen.Add(node.Id))
                    {
                        continue;
                    }

                    nodes.Add(node);
                    if (node.ChildIds.Count > 0)
                    {
                        children.Add(node);
                    }
                }

                childrenRead[parents[index].Id] = read;
            }

            // Read deeper into what came first before moving on, as the
            // document reads.
            unread.InsertRange(0, children);
        }

        var kept = nodes.Count > PartialTreeNodeBudget
            ? nodes[..PartialTreeNodeBudget]
            : nodes;
        return [.. kept.Select(node => childrenRead.TryGetValue(node.Id, out var read)
            ? node with { ChildIds = read }
            : node with { ChildIds = [] })];
    }

    /// <summary>
    /// A DevTools reply's result; null when the reply was larger than the
    /// host accepts, which the host reports in its place. Any other error is
    /// a failure of <paramref name="method"/>.
    /// </summary>
    private static JsonElement? Result(JsonDocument reply, string method)
    {
        if (reply.RootElement.TryGetProperty("result", out var result))
        {
            return result;
        }

        if (reply.RootElement.TryGetProperty("error", out var error)
            && error.TryGetProperty("message", out var message)
            && string.Equals(message.GetString(), HostByteLimitMessage, StringComparison.Ordinal))
        {
            return null;
        }

        throw new InvalidOperationException($"Chromium refused {method}.");
    }

    public async Task<CefSemanticNode?> ReadAccessibilityNodeAsync(
        int backendNodeId)
    {
        if (_customClickTargets.Contains(backendNodeId))
        {
            var tree = await ReadAccessibilityTreeAsync().ConfigureAwait(false);
            return tree.Nodes.FirstOrDefault(node => node.BackendNodeId == backendNodeId);
        }
        await EnsureDomainsEnabledAsync().ConfigureAwait(false);
        var nodes = await _browser.Accessibility
            .GetPartialTreeAsync(backendNodeId, fetchRelatives: true)
            .ConfigureAwait(false);
        var node = nodes.FirstOrDefault(
            candidate => candidate.BackendDomNodeId == backendNodeId);
        return node is null ? null : Project(node);
    }

    public async Task<CefSemanticPoint?> PrepareClickPointAsync(
        int backendNodeId,
        CancellationToken cancellationToken = default)
    {
        await EnsureDomainsEnabledAsync().ConfigureAwait(false);
        for (var attempt = 0; attempt < MaximumRevealAttempts; attempt++)
        {
            var quads = await _browser.Dom
                .GetContentQuadsAsync(backendNodeId)
                .ConfigureAwait(false);
            ScreenshotClip? usableClip = null;
            foreach (var quad in quads)
            {
                var candidate = quad.ToBoundingClip();
                if (IsUsableClip(candidate))
                {
                    usableClip = candidate;
                    break;
                }
            }

            if (usableClip is not { } clip)
            {
                return null;
            }

            var viewport = await _humanizedInput.ReadViewportAsync(cancellationToken)
                .ConfigureAwait(false);
            var point = new CefSemanticPoint(
                clip.X + clip.Width / 2,
                clip.Y + clip.Height / 2,
                clip.Width,
                clip.Height);
            if (IsInsideViewport(point, viewport))
            {
                return point;
            }

            var deltaX = RevealDelta(
                point.X,
                viewport.WidthCss,
                clip.Width);
            var deltaY = RevealDelta(
                point.Y,
                viewport.HeightCss,
                clip.Height);
            await _humanizedInput.ScrollAsync(
                    viewport.WidthCss / 2,
                    viewport.HeightCss / 2,
                    deltaX,
                    deltaY,
                    BrowserInputModifiers.None, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(35), cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    public async Task<bool> HitTestIncludesAsync(
        CefSemanticPoint point,
        int backendNodeId)
    {
        await EnsureDomainsEnabledAsync().ConfigureAwait(false);
        var hitNodeId = await _browser.Dom
            .GetNodeForLocationAsync(
                checked((int)Math.Round(point.X)),
                checked((int)Math.Round(point.Y)))
            .ConfigureAwait(false);
        if (hitNodeId is null)
        {
            return false;
        }

        if (hitNodeId.Value == backendNodeId)
        {
            return true;
        }

        var relatives = await _browser.Accessibility
            .GetPartialTreeAsync(hitNodeId.Value, fetchRelatives: true)
            .ConfigureAwait(false);
        return relatives.Any(
            node => node.BackendDomNodeId == backendNodeId);
    }

    public async Task<bool> DispatchClickAsync(
        CefSemanticPoint point,
        int backendNodeId,
        CancellationToken cancellationToken = default)
    {
        await _humanizedInput.MoveAsync(
                point.X,
                point.Y,
                targetWidth: point.TargetWidth, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!await HitTestIncludesAsync(point, backendNodeId)
                .ConfigureAwait(false))
        {
            return false;
        }

        await _humanizedInput.ClickAsync(
                point.X,
                point.Y,
                BrowserMouseButton.Left,
                BrowserMouseButtons.None,
                BrowserInputModifiers.None,
                clickCount: 1,
                targetWidth: point.TargetWidth, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task ReplaceFocusedTextAsync(
        int backendNodeId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await EnsureDomainsEnabledAsync().ConfigureAwait(false);
        await _humanizedInput.EnsureCursorVisibleAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _browser.Dom.FocusAsync(backendNodeId).WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await DispatchKeyAsync("rawKeyDown", "a", "KeyA", 65, commands: ["SelectAll"])
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DispatchKeyAsync("keyUp", "a", "KeyA", 65).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await DispatchKeyAsync("rawKeyDown", "Backspace", "Backspace", 8)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DispatchKeyAsync("keyUp", "Backspace", "Backspace", 8).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length != 0)
        {
            // Input.insertText is Chromium's acknowledged, trusted input path;
            // it fires editing/input behavior without assigning page state.
            await _humanizedInput.TypeTextAsync(text, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> IsVisibleAsync(int backendNodeId)
    {
        await EnsureDomainsEnabledAsync().ConfigureAwait(false);
        var quads = await _browser.Dom
            .GetContentQuadsAsync(backendNodeId)
            .ConfigureAwait(false);
        foreach (var quad in quads)
        {
            var clip = quad.ToBoundingClip();
            if (clip.Width <= 1
                || clip.Height <= 1
                || !double.IsFinite(clip.X)
                || !double.IsFinite(clip.Y)
                || !double.IsFinite(clip.Width)
                || !double.IsFinite(clip.Height))
            {
                continue;
            }

            var point = new CefSemanticPoint(
                clip.X + clip.Width / 2,
                clip.Y + clip.Height / 2);
            if (await HitTestIncludesAsync(point, backendNodeId)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private async Task EnsureDomainsEnabledAsync()
    {
        if (_domainsEnabled)
        {
            return;
        }

        await _browser.Accessibility.EnableAsync().ConfigureAwait(false);
        await _browser.Dom.EnableAsync().ConfigureAwait(false);
        _domainsEnabled = true;
    }

    private static bool IsUsableClip(ScreenshotClip clip) =>
        clip.Width > 1
        && clip.Height > 1
        && double.IsFinite(clip.X)
        && double.IsFinite(clip.Y)
        && double.IsFinite(clip.Width)
        && double.IsFinite(clip.Height);

    private static bool IsInsideViewport(
        CefSemanticPoint point,
        NativeBrowserViewport viewport) =>
        point.X >= ViewportMarginCss
        && point.Y >= ViewportMarginCss
        && point.X <= viewport.WidthCss - ViewportMarginCss
        && point.Y <= viewport.HeightCss - ViewportMarginCss;

    private static double RevealDelta(
        double coordinate,
        double viewportExtent,
        double targetExtent)
    {
        if (coordinate >= ViewportMarginCss
            && coordinate <= viewportExtent - ViewportMarginCss)
        {
            return 0;
        }

        var desired = Math.Clamp(
            viewportExtent / 2,
            ViewportMarginCss + targetExtent / 2,
            viewportExtent - ViewportMarginCss - targetExtent / 2);
        return Math.Clamp(coordinate - desired, -800, 800);
    }

    private Task DispatchKeyAsync(
        string type,
        string key,
        string code,
        int windowsVirtualKeyCode,
        string text = "",
        IReadOnlyList<string>? commands = null) =>
        ExecuteAcknowledgedAsync(
            "Input.dispatchKeyEvent",
            SerializeKeyEvent(
                type,
                key,
                code,
                windowsVirtualKeyCode,
                text,
                commands));

    internal static string SerializeKeyEvent(
        string type,
        string key,
        string code,
        int windowsVirtualKeyCode,
        string text = "",
        IReadOnlyList<string>? commands = null) =>
        JsonSerializer.Serialize(
            new CdpSemanticKeyEventParameters(
                type,
                key,
                code,
                text,
                text,
                windowsVirtualKeyCode,
                NativeVirtualKeyCode: 0,
                commands),
            BrowserJsonContext.Default.CdpSemanticKeyEventParameters);

    private async Task ExecuteAcknowledgedAsync(
        string method,
        string parametersJson)
    {
        var reply = await _browser.ExecuteDevToolsMethodAsync(
                method,
                parametersJson)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(reply);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(
                error.TryGetProperty("message", out var message)
                    ? message.GetString()
                        ?? "CEF rejected an input command."
                    : "CEF rejected an input command.");
        }

        if (!document.RootElement.TryGetProperty("result", out _))
        {
            throw new InvalidOperationException(
                "CEF did not acknowledge an input command.");
        }
    }

    private static CefSemanticNode Project(AxNode node) =>
        new(
            node.Id,
            node.BackendDomNodeId,
            node.Ignored,
            node.Role,
            node.Name,
            node.ParentId,
            node.ChildIds,
            node.Properties,
            node.Value);

    private static CefSemanticNode Project(JsonElement node)
    {
        var childIds = node.TryGetProperty("childIds", out var children)
            ? children.EnumerateArray()
                .Select(static child => child.GetString() ?? string.Empty)
                .Where(static child => child.Length > 0)
                .ToArray()
            : [];
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.TryGetProperty("properties", out var sourceProperties))
        {
            foreach (var property in sourceProperties.EnumerateArray())
            {
                if (property.TryGetProperty("name", out var name)
                    && name.GetString() is { Length: > 0 } propertyName)
                {
                    properties[propertyName] = ReadAccessibilityValue(property);
                }
            }
        }

        return new CefSemanticNode(
            node.GetProperty("nodeId").GetString() ?? string.Empty,
            node.TryGetProperty("backendDOMNodeId", out var backendNodeId)
                ? backendNodeId.GetInt32()
                : null,
            node.TryGetProperty("ignored", out var ignored) && ignored.GetBoolean(),
            ReadAccessibilityProperty(node, "role"),
            ReadAccessibilityProperty(node, "name"),
            node.TryGetProperty("parentId", out var parentId)
                ? parentId.GetString()
                : null,
            childIds,
            properties,
            ReadAccessibilityProperty(node, "value"));
    }

    private static string ReadAccessibilityProperty(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value)
            ? ReadAccessibilityValue(value)
            : string.Empty;

    private static string ReadAccessibilityValue(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("value", out var value))
        {
            return string.Empty;
        }

        while (value.ValueKind == JsonValueKind.Object
               && value.TryGetProperty("value", out var nested))
        {
            value = nested;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.Array or JsonValueKind.Object =>
                value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }
}
