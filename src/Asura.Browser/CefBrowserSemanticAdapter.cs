using System.Security.Cryptography;
using System.Text;
using Asura.Application;

namespace Asura.Browser;

/// <summary>
/// Converts Chromium's accessibility tree into bounded Asura snapshots
/// and owns the short-lived native leases behind public opaque references.
/// </summary>
internal sealed class CefBrowserSemanticAdapter(ICefSemanticBrowser browser)
{
    private const int VerificationAttempts = 12;
    private const int InteractionGeometryAttempts = 3;

    /// <summary>
    /// The least a child cut short is given of its parent's share: enough
    /// to show a short item, such as one comment of a long discussion, whole.
    /// </summary>
    private const int MinimumChildShare = 8;
    private static readonly TimeSpan VerificationDelay =
        TimeSpan.FromMilliseconds(20);
    private static readonly HashSet<string> ActionableRoles =
        new(StringComparer.Ordinal)
        {
            "clickable",
            "button",
            "checkbox",
            "combobox",
            "link",
            "menuitem",
            "menuitemcheckbox",
            "menuitemradio",
            "option",
            "radio",
            "searchbox",
            "slider",
            "spinbutton",
            "switch",
            "tab",
            "textbox",
        };
    internal static bool IsActionableRole(string role) => ActionableRoles.Contains(NormalizeRole(role));

    private static readonly HashSet<string> FillableRoles =
        new(StringComparer.Ordinal)
        {
            "combobox",
            "searchbox",
            "spinbutton",
            "textbox",
        };
    private static readonly HashSet<string> CheckableRoles =
        new(StringComparer.Ordinal)
        {
            "checkbox",
            "menuitemcheckbox",
            "menuitemradio",
            "radio",
            "switch",
        };
    private static readonly HashSet<string> LeanSemanticRoles =
        new(StringComparer.Ordinal)
        {
            "alert",
            "article",
            "blockquote",
            "caption",
            "cell",
            "column-header",
            "content-info",
            "dialog",
            "document",
            "figure",
            "form",
            "heading",
            "image",
            "list",
            "list-item",
            "main",
            "navigation",
            "paragraph",
            "region",
            "root-web-area",
            "row",
            "row-header",
            "search",
            "static-text",
            "status",
            "table",
            "term",
            "tree-item",
            "web-area",
        };

    private readonly object _gate = new();
    private readonly ICefSemanticBrowser _browser = browser
        ?? throw new ArgumentNullException(nameof(browser));
    private Dictionary<string, ElementLease> _leases = [];
    private string? _snapshotNonce;
    private long _mutationEpoch;

    public async Task<NativeBrowserSnapshotResult> CaptureSnapshotAsync(
        BrowserSnapshotQuery? query = null,
        NativeBrowserElementHandle? within = null)
    {
        query ??= BrowserSnapshotQuery.Lean;
        int? withinBackendNodeId = null;
        if (within is not null)
        {
            if (!TryGetLease(within, out var lease))
            {
                return NativeBrowserSnapshotResult.Stale();
            }

            withinBackendNodeId = lease.BackendNodeId;
        }

        CefSemanticTree? source;
        try
        {
            source = await _browser.ReadAccessibilityTreeAsync(
                    withinBackendNodeId,
                    withinBackendNodeId is null ? 0 : query.Offset)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SecretSafeDiagnosticProjection.WriteTrace(
                "browser.snapshot.accessibility-read-failed", exception);
            return NativeBrowserSnapshotResult.Unavailable();
        }

        if (source is null)
        {
            ConsumeLeases();
            return NativeBrowserSnapshotResult.Stale();
        }

        try
        {
            return BuildSnapshot(
                source,
                query,
                withinBackendNodeId is null ? 0 : query.Offset);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or InvalidOperationException
                or EncoderFallbackException)
        {
            InvalidateDocument();
            return NativeBrowserSnapshotResult.Invalid();
        }
    }

    public async Task<NativeBrowserClickResult> ClickAsync(
        NativeBrowserElementHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!TryGetLease(handle, out var lease))
        {
            return NativeBrowserClickResult.Stale();
        }

        CefSemanticNode? current;
        try
        {
            current = await RevalidateAsync(lease).ConfigureAwait(false);
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserClickResult.Stale();
        }

        if (current is null)
        {
            ConsumeLeases();
            return NativeBrowserClickResult.Stale();
        }

        if (!ActionableRoles.Contains(NormalizeRole(current.Role))
            || IsUnavailable(current))
        {
            ConsumeLeases();
            return NativeBrowserClickResult.NotInteractable();
        }

        CefSemanticPoint? point;
        try
        {
            point = await PrepareVerifiedClickPointAsync(lease, cancellationToken)
                .ConfigureAwait(false);
            if (point is null)
            {
                ConsumeLeases();
                return NativeBrowserClickResult.NotInteractable();
            }
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserClickResult.NotInteractable();
        }

        try
        {
            if (!await _browser.DispatchClickAsync(
                    point.Value,
                    lease.BackendNodeId, cancellationToken)
                    .ConfigureAwait(false))
            {
                ConsumeLeases();
                return NativeBrowserClickResult.NotInteractable();
            }

            ConsumeLeases();
            return NativeBrowserClickResult.Activated();
        }
        catch (Exception)
        {
            // Mouse movement or button-down may already have reached the page.
            ConsumeLeases();
            return NativeBrowserClickResult.OutcomeUnknown();
        }
    }

    public async Task<NativeBrowserFillResult> FillAsync(
        NativeBrowserElementHandle handle,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(text);
        if (!TryGetLease(handle, out var lease))
        {
            return NativeBrowserFillResult.Stale();
        }

        CefSemanticNode? current;
        try
        {
            current = await RevalidateAsync(lease).ConfigureAwait(false);
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserFillResult.Stale();
        }

        if (current is null)
        {
            ConsumeLeases();
            return NativeBrowserFillResult.Stale();
        }

        var role = NormalizeRole(current.Role);
        if (!FillableRoles.Contains(role))
        {
            ConsumeLeases();
            return NativeBrowserFillResult.NotFillable();
        }

        if (IsUnavailable(current) || PropertyIsTrue(current, "readonly"))
        {
            ConsumeLeases();
            return NativeBrowserFillResult.NotInteractable();
        }

        try
        {
            await _browser.ReplaceFocusedTextAsync(
                    lease.BackendNodeId,
                    text, cancellationToken)
                .ConfigureAwait(false);
            var verification = await ReadVerifiedNodeAsync(
                    lease,
                    node => string.Equals(
                        node.Value,
                        text,
                        StringComparison.Ordinal))
                .ConfigureAwait(false);
            ConsumeLeases();
            if (verification is null)
            {
                return NativeBrowserFillResult.OutcomeUnknown();
            }

            return string.Equals(
                    verification.Value,
                    text,
                    StringComparison.Ordinal)
                ? NativeBrowserFillResult.Filled()
                : NativeBrowserFillResult.ValueNotSupported();
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserFillResult.OutcomeUnknown();
        }
    }

    public async Task<NativeBrowserCheckResult> CheckAsync(
        NativeBrowserElementHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!TryGetLease(handle, out var lease))
        {
            return NativeBrowserCheckResult.Stale();
        }

        CefSemanticNode? current;
        try
        {
            current = await RevalidateAsync(lease).ConfigureAwait(false);
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.Stale();
        }

        if (current is null)
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.Stale();
        }

        if (!CheckableRoles.Contains(NormalizeRole(current.Role)))
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.NotCheckable();
        }

        if (IsUnavailable(current))
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.NotInteractable();
        }

        if (PropertyIsTrue(current, "checked"))
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.Checked();
        }

        CefSemanticPoint? point;
        try
        {
            point = await PrepareVerifiedClickPointAsync(lease, cancellationToken)
                .ConfigureAwait(false);
            if (point is null)
            {
                ConsumeLeases();
                return NativeBrowserCheckResult.NotInteractable();
            }
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.NotInteractable();
        }

        try
        {
            // A postcondition operation must issue exactly one toggle. The
            // former pointer-then-keyboard fallback could check the control
            // and immediately uncheck it while Chromium's AX tree was still
            // publishing the first change. Dispatch one verified pointer
            // activation and wait only for its observable postcondition.
            if (!await _browser.DispatchClickAsync(
                    point.Value,
                    lease.BackendNodeId, cancellationToken)
                    .ConfigureAwait(false))
            {
                ConsumeLeases();
                return NativeBrowserCheckResult.NotInteractable();
            }

            var verification = await ReadVerifiedNodeAsync(
                    lease,
                    node => PropertyIsTrue(node, "checked"))
                .ConfigureAwait(false);

            ConsumeLeases();
            if (verification is null)
            {
                return NativeBrowserCheckResult.OutcomeUnknown();
            }

            return PropertyIsTrue(verification, "checked")
                ? NativeBrowserCheckResult.Checked()
                : NativeBrowserCheckResult.Unchecked();
        }
        catch (Exception)
        {
            ConsumeLeases();
            return NativeBrowserCheckResult.OutcomeUnknown();
        }
    }

    public async Task<NativeBrowserElementStateResult> ReadElementStateAsync(
        NativeBrowserElementHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!TryGetLease(handle, out var lease))
        {
            return NativeBrowserElementStateResult.Stale();
        }

        try
        {
            var current = await RevalidateAsync(lease).ConfigureAwait(false);
            if (current is null)
            {
                return NativeBrowserElementStateResult.Stale();
            }

            var disabled = PropertyIsTrue(current, "disabled");
            var readOnly = PropertyIsTrue(current, "readonly");
            var editable = FillableRoles.Contains(NormalizeRole(current.Role))
                && !disabled
                && !readOnly;
            return NativeBrowserElementStateResult.Success(
                new NativeBrowserElementState(
                    await _browser.IsVisibleAsync(lease.BackendNodeId)
                        .ConfigureAwait(false),
                    !disabled,
                    PropertyIsTrue(current, "checked"),
                    PropertyIsTrue(current, "selected"),
                    editable,
                    PropertyIsTrue(current, "focused")));
        }
        catch (Exception)
        {
            return NativeBrowserElementStateResult.Unavailable();
        }
    }

    private async Task<CefSemanticPoint?> PrepareVerifiedClickPointAsync(
        ElementLease lease, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < InteractionGeometryAttempts; attempt++)
        {
            var point = await _browser
                .PrepareClickPointAsync(lease.BackendNodeId, cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (point is not null
                && await _browser.HitTestIncludesAsync(
                        point.Value,
                        lease.BackendNodeId)
                    .ConfigureAwait(false))
            {
                return point;
            }

            if (attempt + 1 < InteractionGeometryAttempts)
            {
                await Task.Delay(VerificationDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    public void InvalidateDocument()
    {
        lock (_gate)
        {
            _leases = [];
            _snapshotNonce = null;
            AdvanceMutationEpochUnsafe();
        }
    }

    /// <summary>
    /// Projects a tree into a bounded snapshot. Unfiltered, the snapshot
    /// keeps the tree's shape: each branch is given a share of the bounds,
    /// enough to show a short branch whole, and a branch cut short is shown
    /// with how many of its children were left out and where they resume.
    /// A filter keeps the nodes that match and their ancestors instead.
    /// </summary>
    private NativeBrowserSnapshotResult BuildSnapshot(
        CefSemanticTree source,
        BrowserSnapshotQuery query,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        var byId = new Dictionary<string, CefSemanticNode>(StringComparer.Ordinal);
        foreach (var node in source.Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Id))
            {
                byId.TryAdd(node.Id, node);
            }
        }

        if (byId.Count == 0)
        {
            return NativeBrowserSnapshotResult.Invalid();
        }

        var entries = new List<SnapshotEntry>(byId.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var roots = new List<SnapshotEntry>();
        if (source.RootId is { } rootId)
        {
            if (!byId.TryGetValue(rootId, out var root))
            {
                return NativeBrowserSnapshotResult.Invalid();
            }

            roots.Add(Visit(root, null, insideActionable: false, offset, isRoot: true)!);
        }
        else
        {
            foreach (var node in source.Nodes)
            {
                if (node.ParentId is null || !byId.ContainsKey(node.ParentId))
                {
                    AddRoot(node);
                }
            }

            foreach (var node in source.Nodes)
            {
                AddRoot(node);
            }
        }

        // Children follow their parent, so each subtree is measured before
        // its parent.
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            entry.Size = entry.IsCandidate ? 1 : 0;
            entry.IsComplete = entry.UnreadFrom is null;
            var height = 0;
            foreach (var kid in entry.Kids)
            {
                entry.Size += kid.Size;
                entry.IsComplete &= kid.IsComplete;
                height = Math.Max(height, kid.Height);
            }

            entry.Height = height + (entry.IsCandidate ? 1 : 0);
        }

        var shown = query.Filter is not null || query.InteractiveOnly
            ? SelectMatches(roots, entries, query)
            : Shape(roots, query);
        if (shown.Nodes.Count == 0)
        {
            return NativeBrowserSnapshotResult.Invalid();
        }

        var nonce = NewIdentifier("sn_");
        long epoch;
        lock (_gate)
        {
            epoch = _mutationEpoch;
        }

        var leases = new Dictionary<string, ElementLease>(StringComparer.Ordinal);
        var output = new List<NativeBrowserSnapshotNode>(shown.Nodes.Count);
        foreach (var (entry, depth, hidden, next) in shown.Nodes)
        {
            NativeBrowserElementHandle? handle = null;
            // A branch cut short is referenced so a snapshot can be taken within it.
            if ((entry.IsActionable || hidden > 0)
                && entry.Node.BackendNodeId is { } backendNodeId)
            {
                var token = NewUniqueToken(leases);
                handle = new NativeBrowserElementHandle(nonce, token, epoch);
                leases.Add(
                    token,
                    new ElementLease(
                        backendNodeId,
                        entry.Node.Role,
                        entry.Node.Name,
                        nonce,
                        epoch));
            }

            output.Add(
                new NativeBrowserSnapshotNode(
                    depth,
                    entry.Role,
                    entry.Name,
                    entry.States,
                    handle,
                    hidden,
                    hidden > 0 ? next : 0));
        }

        lock (_gate)
        {
            _snapshotNonce = nonce;
            _leases = leases;
        }

        return NativeBrowserSnapshotResult.Success(
            new NativeBrowserSnapshot(output, shown.IsTruncated));

        void AddRoot(CefSemanticNode node)
        {
            if (Visit(node, null, insideActionable: false, firstChild: 0, isRoot: true) is { } entry)
            {
                roots.Add(entry);
            }
        }

        SnapshotEntry? Visit(
            CefSemanticNode node,
            SnapshotEntry? parentCandidate,
            bool insideActionable,
            int firstChild,
            bool isRoot)
        {
            if (!visited.Add(node.Id))
            {
                return null;
            }

            var role = NormalizeRole(node.Role);
            var entry = new SnapshotEntry(
                node,
                role,
                TruncateUtf8(node.Name, BrowserSnapshotNode.MaximumNameBytes),
                ProjectStates(node));
            entries.Add(entry);
            var childParent = parentCandidate;
            var childInsideActionable = insideActionable;
            if (!node.IsIgnored)
            {
                entry.IsActionable = node.BackendNodeId is not null
                    && ActionableRoles.Contains(role);
                entry.IsCandidate = isRoot || ShouldIncludeLeanNode(
                    node,
                    role,
                    entry.Name,
                    parentCandidate,
                    insideActionable);
                childInsideActionable |= entry.IsActionable;
            }

            // The root of a snapshot within an element is that element.
            entry.IsCandidate |= isRoot && source.RootId is not null;
            if (entry.IsCandidate)
            {
                entry.CandidateParent = parentCandidate;
                childParent = entry;
            }

            entry.FirstChild = firstChild;
            for (var index = firstChild; index < node.ChildIds.Count; index++)
            {
                if (!byId.TryGetValue(node.ChildIds[index], out var child))
                {
                    entry.UnreadFrom = index;
                    break;
                }

                // A node read with another parent belongs to that parent.
                if (child.ParentId is { } parentId
                    && !string.Equals(parentId, node.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Visit(child, childParent, childInsideActionable, firstChild: 0, isRoot: false) is { } kid)
                {
                    entry.Kids.Add(kid);
                    entry.KidOffsets.Add(index);
                }
            }

            return entry;
        }
    }

    /// <summary>
    /// The snapshot of the whole tree, or of as much of it as its bounds
    /// hold, in its shape.
    /// </summary>
    private static ShownNodes Shape(
        IReadOnlyList<SnapshotEntry> roots,
        BrowserSnapshotQuery query)
    {
        var maximumDepth = Math.Min(
            query.MaximumDepth ?? BrowserSnapshotNode.MaximumDepth,
            BrowserSnapshotNode.MaximumDepth);
        var nodes = new List<ShownNode>();
        var truncated = false;
        var budget = BrowserDocumentSnapshot.MaximumNodeCount;
        var shares = Share(
            [.. roots.Select(root => root.IsComplete ? root.Size + (root.IsCandidate ? 0 : 1) : int.MaxValue)],
            budget);
        for (var index = 0; index < roots.Count; index++)
        {
            if (shares[index] == 0)
            {
                truncated = true;
                break;
            }

            Show(roots[index], shares[index], depth: 0, isRoot: true);
        }

        return new ShownNodes(nodes, truncated);

        int Show(SnapshotEntry entry, int budget, int depth, bool isRoot)
        {
            var extra = isRoot && !entry.IsCandidate ? 1 : 0;
            var cost = entry.Size + extra;
            if (entry.IsComplete && cost <= budget
                && depth + entry.Height + extra - 1 <= maximumDepth)
            {
                ShowWhole(entry, depth, isRoot);
                return cost;
            }

            // Shown in part, the node itself is shown, so its children can
            // be read within it.
            var position = nodes.Count;
            nodes.Add(new ShownNode(entry, depth, 0, 0));
            var used = 1;
            var next = 0;
            if (depth < maximumDepth)
            {
                var kids = entry.Kids;
                var allotted = Share(
                    [.. kids.Select(kid => kid.IsComplete ? kid.Size : int.MaxValue)],
                    budget - 1);
                for (; next < kids.Count; next++)
                {
                    var kid = kids[next];
                    if (kid.IsComplete && kid.Size == 0)
                    {
                        continue;
                    }

                    if (allotted[next] == 0)
                    {
                        break;
                    }

                    used += Show(kid, allotted[next], depth + 1, isRoot: false);
                }
            }

            var resumeAt = next < entry.Kids.Count
                ? entry.KidOffsets[next]
                : entry.UnreadFrom ?? entry.Node.ChildIds.Count;
            var hidden = entry.Node.ChildIds.Count - resumeAt;
            if (hidden > 0)
            {
                truncated = true;
                nodes[position] = new ShownNode(entry, depth, hidden, resumeAt);
            }

            return used;
        }

        void ShowWhole(SnapshotEntry entry, int depth, bool isRoot)
        {
            var childDepth = depth;
            if (entry.IsCandidate || isRoot)
            {
                nodes.Add(new ShownNode(entry, depth, 0, 0));
                childDepth = depth + 1;
            }

            foreach (var kid in entry.Kids)
            {
                ShowWhole(kid, childDepth, isRoot: false);
            }
        }
    }

    /// <summary>
    /// Divides <paramref name="budget"/> among children of the given sizes,
    /// in document order: all of each when they fit; otherwise an equal share
    /// that shows every child it can, but never less than
    /// <see cref="MinimumChildShare"/>, so the first children are shown with
    /// their content rather than every child bare. Children past the budget
    /// get nothing.
    /// </summary>
    private static int[] Share(IReadOnlyList<int> sizes, int budget)
    {
        var shares = new int[sizes.Count];
        if (budget <= 0)
        {
            return shares;
        }

        if (Allotted(budget) <= budget)
        {
            for (var index = 0; index < sizes.Count; index++)
            {
                shares[index] = Math.Min(sizes[index], budget);
            }

            return shares;
        }

        var low = 0;
        var high = budget;
        while (low < high)
        {
            var level = low + (high - low + 1) / 2;
            if (Allotted(level) <= budget)
            {
                low = level;
            }
            else
            {
                high = level - 1;
            }
        }

        var share = Math.Max(low, Math.Min(MinimumChildShare, budget));
        var remaining = budget;
        for (var index = 0; index < sizes.Count && remaining > 0; index++)
        {
            shares[index] = Math.Min(Math.Min(sizes[index], share), remaining);
            remaining -= shares[index];
        }

        // What an equal share leaves over goes to the first children that
        // have more to show.
        for (var index = 0; index < sizes.Count && remaining > 0; index++)
        {
            var more = Math.Min(sizes[index] - shares[index], remaining);
            shares[index] += more;
            remaining -= more;
        }

        return shares;

        long Allotted(int level)
        {
            long total = 0;
            foreach (var size in sizes)
            {
                total += Math.Min(size, level);
            }

            return total;
        }
    }

    /// <summary>
    /// The nodes that match a filter, with their ancestors, in document
    /// order, as many as the bounds hold.
    /// </summary>
    private static ShownNodes SelectMatches(
        IReadOnlyList<SnapshotEntry> roots,
        IReadOnlyList<SnapshotEntry> entries,
        BrowserSnapshotQuery query)
    {
        var maximumDepth = Math.Min(
            query.MaximumDepth ?? BrowserSnapshotNode.MaximumDepth,
            BrowserSnapshotNode.MaximumDepth);
        var candidates = entries.Where(entry => entry.IsCandidate).ToArray();
        var depths = new Dictionary<SnapshotEntry, int>();
        foreach (var candidate in candidates)
        {
            depths[candidate] = candidate.CandidateParent is { } parent
                ? depths[parent] + 1
                : 0;
        }

        var selected = new HashSet<SnapshotEntry>();
        foreach (var candidate in candidates)
        {
            if (depths[candidate] > maximumDepth
                || (query.InteractiveOnly && !candidate.IsActionable)
                || (query.Filter is { } filter
                    && !candidate.Role.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !candidate.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            for (SnapshotEntry? current = candidate;
                current is not null && selected.Add(current);
                current = current.CandidateParent)
            {
            }
        }

        if (selected.Count == 0)
        {
            selected.UnionWith(candidates.Where(candidate => candidate.CandidateParent is null));
        }

        // What was not read could match too.
        var truncated = roots.Any(root => !root.IsComplete);
        var nodes = new List<ShownNode>();
        foreach (var candidate in candidates)
        {
            if (!selected.Contains(candidate))
            {
                continue;
            }

            if (nodes.Count == BrowserDocumentSnapshot.MaximumNodeCount)
            {
                truncated = true;
                break;
            }

            nodes.Add(new ShownNode(candidate, depths[candidate], 0, 0));
        }

        return new ShownNodes(nodes, truncated);
    }

    private static bool ShouldIncludeLeanNode(
        CefSemanticNode node,
        string role,
        string name,
        SnapshotEntry? parentCandidate,
        bool insideActionable)
    {
        if (parentCandidate is null || ActionableRoles.Contains(role))
        {
            return true;
        }

        if (insideActionable || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (parentCandidate.Name.Equals(
            name,
            StringComparison.Ordinal))
        {
            return false;
        }

        return LeanSemanticRoles.Contains(role) || node.ChildIds.Count == 0;
    }

    private bool TryGetLease(
        NativeBrowserElementHandle handle,
        out ElementLease lease)
    {
        lock (_gate)
        {
            if (_snapshotNonce is null
                || !string.Equals(
                    handle.SnapshotNonce,
                    _snapshotNonce,
                    StringComparison.Ordinal)
                || handle.MutationEpoch != _mutationEpoch
                || !_leases.TryGetValue(handle.ElementToken, out lease!)
                || !string.Equals(lease.SnapshotNonce, handle.SnapshotNonce
, StringComparison.Ordinal) || lease.MutationEpoch != handle.MutationEpoch)
            {
                lease = null!;
                return false;
            }

            return true;
        }
    }

    private async Task<CefSemanticNode?> RevalidateAsync(ElementLease lease)
    {
        var current = await _browser
            .ReadAccessibilityNodeAsync(lease.BackendNodeId)
            .ConfigureAwait(false);
        if (current is null
            || current.IsIgnored
            || current.BackendNodeId != lease.BackendNodeId
            || !string.Equals(current.Role, lease.Role, StringComparison.Ordinal)
            || !string.Equals(current.Name, lease.Name, StringComparison.Ordinal))
        {
            return null;
        }

        return current;
    }

    private async Task<CefSemanticNode?> ReadVerifiedNodeAsync(
        ElementLease lease,
        Func<CefSemanticNode, bool> postcondition)
    {
        CefSemanticNode? last = null;
        for (var attempt = 0; attempt < VerificationAttempts; attempt++)
        {
            var current = await RevalidateAsync(lease).ConfigureAwait(false);
            if (current is not null)
            {
                last = current;
                if (postcondition(current))
                {
                    return current;
                }
            }

            if (attempt + 1 < VerificationAttempts)
            {
                await Task.Delay(VerificationDelay).ConfigureAwait(false);
            }
        }

        return last;
    }

    private void ConsumeLeases()
    {
        lock (_gate)
        {
            _leases = [];
            _snapshotNonce = null;
            AdvanceMutationEpochUnsafe();
        }
    }

    private void AdvanceMutationEpochUnsafe() =>
        _mutationEpoch = _mutationEpoch
            >= NativeBrowserElementHandle.MaximumMutationEpoch
            ? 0
            : _mutationEpoch + 1;

    private static bool IsUnavailable(CefSemanticNode node) =>
        node.IsIgnored || PropertyIsTrue(node, "disabled");

    private static bool PropertyIsTrue(
        CefSemanticNode node,
        string propertyName) =>
        node.Properties.TryGetValue(propertyName, out var value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static BrowserSnapshotNodeState ProjectStates(
        CefSemanticNode node)
    {
        var states = BrowserSnapshotNodeState.None;
        Add("disabled", BrowserSnapshotNodeState.Disabled);
        Add("checked", BrowserSnapshotNodeState.Checked);
        Add("selected", BrowserSnapshotNodeState.Selected);
        Add("expanded", BrowserSnapshotNodeState.Expanded);
        Add("pressed", BrowserSnapshotNodeState.Pressed);
        Add("required", BrowserSnapshotNodeState.Required);
        Add("readonly", BrowserSnapshotNodeState.ReadOnly);
        return states;

        void Add(string property, BrowserSnapshotNodeState state)
        {
            if (PropertyIsTrue(node, property))
            {
                states |= state;
            }
        }
    }

    private static string NormalizeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return "generic";
        }

        var normalized = new StringBuilder(role.Length + 8);
        var previousWasSeparator = true;
        foreach (var character in role)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (char.IsUpper(character)
                    && !previousWasSeparator
                    && normalized.Length != 0)
                {
                    normalized.Append('-');
                }

                normalized.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && normalized.Length != 0)
            {
                normalized.Append('-');
                previousWasSeparator = true;
            }
        }

        while (normalized.Length != 0 && normalized[^1] == '-')
        {
            normalized.Length--;
        }

        if (normalized.Length == 0)
        {
            return "generic";
        }

        if (normalized.Length > BrowserSnapshotNode.MaximumRoleBytes)
        {
            normalized.Length = BrowserSnapshotNode.MaximumRoleBytes;
            while (normalized.Length != 0 && normalized[^1] == '-')
            {
                normalized.Length--;
            }
        }

        return normalized.Length == 0 ? "generic" : normalized.ToString();
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > maximumBytes)
            {
                break;
            }

            builder.Append(rune);
            used += rune.Utf8SequenceLength;
        }

        return builder.ToString();
    }

    private static string NewUniqueToken(
        IReadOnlyDictionary<string, ElementLease> leases)
    {
        while (true)
        {
            var token = NewIdentifier("el_");
            if (!leases.ContainsKey(token))
            {
                return token;
            }
        }
    }

    private static string NewIdentifier(string prefix) =>
        string.Concat(
            prefix,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'));

    private sealed record ElementLease(
        int BackendNodeId,
        string Role,
        string Name,
        string SnapshotNonce,
        long MutationEpoch);

    /// <summary>One node of the tree read, as the snapshot sees it.</summary>
    private sealed class SnapshotEntry(
        CefSemanticNode node,
        string role,
        string name,
        BrowserSnapshotNodeState states)
    {
        public CefSemanticNode Node { get; } = node;

        public string Role { get; } = role;

        public string Name { get; } = name;

        public BrowserSnapshotNodeState States { get; } = states;

        /// <summary>Shown when the snapshot reaches it.</summary>
        public bool IsCandidate { get; set; }

        public bool IsActionable { get; set; }

        public SnapshotEntry? CandidateParent { get; set; }

        public List<SnapshotEntry> Kids { get; } = [];

        /// <summary>Where each of <see cref="Kids"/> is among the node's child IDs.</summary>
        public List<int> KidOffsets { get; } = [];

        public int FirstChild { get; set; }

        /// <summary>The first child ID that was not read, if any.</summary>
        public int? UnreadFrom { get; set; }

        /// <summary>How many nodes showing the whole subtree takes.</summary>
        public int Size { get; set; }

        /// <summary>The subtree was read whole.</summary>
        public bool IsComplete { get; set; }

        /// <summary>How deep the shown nodes of the subtree reach.</summary>
        public int Height { get; set; }
    }

    private readonly record struct ShownNode(
        SnapshotEntry Entry,
        int Depth,
        int HiddenChildren,
        int NextChild);

    private sealed record ShownNodes(
        IReadOnlyList<ShownNode> Nodes,
        bool IsTruncated);
}
