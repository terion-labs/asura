using System.Text.Json;
using Asura.Application;

namespace Asura.Browser.Tests;

public sealed class CefBrowserSemanticAdapterTests
{
    [Fact]
    public async Task RepeatedChromiumTextNodesDoNotHideCustomControls()
    {
        var generatedText = Node(-1, "InlineTextBox", "Rescue", parentId: "3") with { BackendNodeId = null };
        CefSemanticNode[] source =
        [
            Node(1, "RootWebArea", "Fixture", parentId: null, childIds: ["2"]),
            Node(2, "generic", "", childIds: ["3"]) with { IsIgnored = true },
            Node(3, "StaticText", "Rescue", parentId: "2", childIds: [generatedText.Id]),
            generatedText,
            generatedText,
        ];
        using var dom = JsonDocument.Parse("""
            {"documents":[{"nodes":{"backendNodeId":[1,2,3],"isClickable":{"index":[1]}}}]}
            """);
        var browser = new RecordingCefSemanticBrowser(CefClickableNodes.Augment(source, dom.RootElement));
        var adapter = new CefBrowserSemanticAdapter(browser);

        var snapshot = await adapter.CaptureSnapshotAsync(new BrowserSnapshotQuery(filter: "Rescue"));

        Assert.True(snapshot.IsSuccess);
        var target = Assert.Single(snapshot.Value!.Nodes, node => node.Name == "Rescue");
        Assert.Equal("clickable", target.Role);
        Assert.NotNull(target.Handle);
        Assert.Equal(NativeBrowserClickStatus.Activated, (await adapter.ClickAsync(target.Handle!)).Status);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("cell")]
    [InlineData("heading")]
    [InlineData("LayoutTableCell")]
    public async Task CustomClickListenerProducesUsableReferenceAndRevalidatesBeforeInput(string role)
    {
        CefSemanticNode[] source =
        [
            Node(1, "RootWebArea", "Fixture", parentId: null, childIds: ["2"]),
            Node(2, role, "", childIds: ["3"]) with { IsIgnored = true },
            Node(3, "StaticText", "Rescue", parentId: "2"),
        ];
        using var dom = JsonDocument.Parse("""
            {"documents":[{"nodes":{"backendNodeId":[1,2,3],"isClickable":{"index":[1]}}}]}
            """);
        var browser = new RecordingCefSemanticBrowser(CefClickableNodes.Augment(source, dom.RootElement));
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync(new BrowserSnapshotQuery(filter: "Rescue"));
        var target = Assert.Single(snapshot.Value!.Nodes, node => node.Name == "Rescue");
        Assert.Equal("clickable", target.Role);
        Assert.NotNull(target.Handle);
        Assert.Equal(NativeBrowserClickStatus.Activated, (await adapter.ClickAsync(target.Handle!)).Status);
        Assert.Equal(1, browser.ClickDispatchCount);

        var fresh = await adapter.CaptureSnapshotAsync(new BrowserSnapshotQuery(filter: "Rescue"));
        var current = Assert.Single(fresh.Value!.Nodes, node => string.Equals(node.Role, "clickable", StringComparison.Ordinal));
        browser.SetNode(source[1]); // The handler/semantics disappeared before dispatch.
        Assert.NotEqual(NativeBrowserClickStatus.Activated, (await adapter.ClickAsync(current.Handle!)).Status);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Fact]
    public void DelegatedContainerListenerDoesNotSwallowSemanticControls()
    {
        CefSemanticNode[] source =
        [
            Node(1, "generic", "Container", parentId: null, childIds: ["2"]),
            Node(2, "button", "Submit"),
        ];
        using var dom = JsonDocument.Parse("""
            {"documents":[{"nodes":{"backendNodeId":[1,2],"isClickable":{"index":[0,1]}}}]}
            """);
        Assert.Equal(source, CefClickableNodes.Augment(source, dom.RootElement));
    }

    [Fact]
    public void CheckboxKeyboardActivationIncludesSpaceTextForChromium()
    {
        using var parameters = JsonDocument.Parse(
            CefSemanticBrowser.SerializeKeyEvent(
                "keyDown",
                " ",
                "Space",
                32,
                text: " "));

        Assert.Equal(
            " ",
            parameters.RootElement.GetProperty("text").GetString());
        Assert.Equal(
            " ",
            parameters.RootElement.GetProperty("unmodifiedText").GetString());
        Assert.Equal(
            "Space",
            parameters.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SnapshotProjectsBoundedAxNodesAndOpaqueActionLeases()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);

        var result = await adapter.CaptureSnapshotAsync();

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!.Nodes,
            root =>
            {
                Assert.Equal("root-web-area", root.Role);
                Assert.Null(root.Handle);
            },
            button =>
            {
                Assert.Equal("button", button.Role);
                Assert.NotNull(button.Handle);
                Assert.StartsWith("el_", button.Handle!.ElementToken);
                Assert.NotEqual("2", button.Handle.ElementToken, StringComparer.Ordinal);
            },
            textbox =>
            {
                Assert.Equal("textbox", textbox.Role);
                Assert.Equal(
                    BrowserSnapshotNodeState.Required,
                    textbox.States);
                Assert.NotNull(textbox.Handle);
            },
            checkbox =>
            {
                Assert.Equal("checkbox", checkbox.Role);
                Assert.NotNull(checkbox.Handle);
            });
    }

    [Fact]
    public async Task APageReadOnlyInPartShowsWhereItWasNotReadInto()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        Assert.False((await adapter.CaptureSnapshotAsync()).Value!.IsTruncated);

        // Too large to read whole: what was read is usable, and the node
        // whose children were not read says where they resume.
        browser.UnreadNodeIds.Add("4");
        var result = await adapter.CaptureSnapshotAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Contains(result.Value.Nodes, node => string.Equals(node.Role, "button", StringComparison.Ordinal) && node.Handle is not null);
        var root = result.Value.Nodes[0];
        Assert.Equal(1, root.HiddenChildCount);
        Assert.Equal(2, root.NextChildOffset);
        Assert.NotNull(root.Handle);
    }

    [Fact]
    public async Task ALongDiscussionKeepsItsShapeAndOpensWhereItWasCutShort()
    {
        var browser = new RecordingCefSemanticBrowser(LongDiscussion(links: 300, comments: 1500));
        var adapter = new CefBrowserSemanticAdapter(browser);

        var result = await adapter.CaptureSnapshotAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value!;
        Assert.True(snapshot.IsTruncated);
        Assert.True(snapshot.Nodes.Count <= BrowserDocumentSnapshot.MaximumNodeCount);
        // Both branches are shown, each cut short where it says, and the
        // first comments are shown with their content, not as bare items.
        var navigation = Assert.Single(snapshot.Nodes, node => node.Role == "navigation");
        var main = Assert.Single(snapshot.Nodes, node => node.Role == "main");
        Assert.Contains(snapshot.Nodes, node => node.Name == "Link 0");
        Assert.True(navigation.HiddenChildCount > 0);
        Assert.Equal(300 - navigation.HiddenChildCount, navigation.NextChildOffset);
        Assert.Contains(snapshot.Nodes, node => node.Name == "Commenter 0");
        Assert.Contains(snapshot.Nodes, node => node.Name == "Body 0");
        Assert.Contains(snapshot.Nodes, node => node.Name == "Reply 0" && node.Handle is not null);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Name == "Commenter 1499");
        Assert.True(main.HiddenChildCount > 0);
        Assert.True(main.NextChildOffset > 10);
        Assert.NotNull(main.Handle);

        var within = await adapter.CaptureSnapshotAsync(
            new BrowserSnapshotQuery(within: new BrowserElementReferenceId("main"), offset: 1490),
            main.Handle);

        Assert.True(within.IsSuccess);
        Assert.Equal("main", within.Value!.Nodes[0].Role);
        Assert.Equal(0, within.Value.Nodes[0].Depth);
        Assert.Contains(within.Value.Nodes, node => node.Name == "Commenter 1499");
        Assert.Contains(within.Value.Nodes, node => node.Name == "Reply 1490" && node.Handle is not null);
        Assert.DoesNotContain(within.Value.Nodes, node => node.Name == "Commenter 0");
        Assert.False(within.Value.IsTruncated);
    }

    [Fact]
    public async Task ASnapshotWithinAnElementFromAnEarlierSnapshotIsStale()
    {
        var browser = new RecordingCefSemanticBrowser(LongDiscussion(links: 1, comments: 600));
        var adapter = new CefBrowserSemanticAdapter(browser);
        var first = await adapter.CaptureSnapshotAsync();
        var main = Assert.Single(first.Value!.Nodes, node => node.Role == "main");
        _ = await adapter.CaptureSnapshotAsync();

        var result = await adapter.CaptureSnapshotAsync(
            new BrowserSnapshotQuery(within: new BrowserElementReferenceId("main")),
            main.Handle);

        Assert.Equal(NativeBrowserSnapshotFailure.Stale, result.Failure);
    }

    private static CefSemanticNode[] LongDiscussion(int links, int comments)
    {
        const int MainId = 3;
        const int FirstLink = 10;
        var firstComment = FirstLink + links;
        var nodes = new List<CefSemanticNode>
        {
            Node(1, "RootWebArea", "Long discussion", parentId: null, childIds: ["2", "3"]),
            Node(2, "navigation", "Site", childIds: [.. Enumerable.Range(FirstLink, links).Select(Id)]),
            Node(MainId, "main", "Discussion", childIds: [.. Enumerable.Range(0, comments).Select(index => Id(firstComment + index * 5))]),
        };
        nodes.AddRange(Enumerable.Range(0, links).Select(index => Node(FirstLink + index, "link", $"Link {index}", parentId: "2")));
        for (var index = 0; index < comments; index++)
        {
            var article = firstComment + index * 5;
            nodes.Add(Node(article, "article", "", parentId: Id(MainId), childIds: [Id(article + 1), Id(article + 2), Id(article + 3)]));
            nodes.Add(Node(article + 1, "heading", $"Commenter {index}", parentId: Id(article)));
            nodes.Add(Node(article + 2, "button", $"Reply {index}", parentId: Id(article)));
            nodes.Add(Node(article + 3, "paragraph", "", parentId: Id(article), childIds: [Id(article + 4)]));
            nodes.Add(Node(article + 4, "StaticText", $"Body {index}", parentId: Id(article + 3)));
        }

        return [.. nodes];

        static string Id(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task FilterRunsBeforeNodeCapAndReturnsAUsableLateReference()
    {
        var childIds = Enumerable.Range(2, 700)
            .Select(value => value.ToString(
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var nodes = new List<CefSemanticNode>
        {
            Node(
                1,
                "RootWebArea",
                "Large results page",
                parentId: null,
                childIds: childIds),
        };
        nodes.AddRange(
            Enumerable.Range(2, 699)
                .Select(value => Node(
                    value,
                    "button",
                    $"Header action {value}")));
        nodes.Add(Node(701, "link", "Target result"));
        var browser = new RecordingCefSemanticBrowser(nodes);
        var adapter = new CefBrowserSemanticAdapter(browser);

        var result = await adapter.CaptureSnapshotAsync(
            new BrowserSnapshotQuery(filter: "Target result"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Collection(
            result.Value.Nodes,
            root => Assert.Equal("root-web-area", root.Role),
            target =>
            {
                Assert.Equal("link", target.Role);
                Assert.Equal("Target result", target.Name);
                Assert.NotNull(target.Handle);
            });

        var click = await adapter.ClickAsync(result.Value.Nodes[1].Handle!);
        Assert.Equal(NativeBrowserClickStatus.Activated, click.Status);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task InteractiveSnapshotOmitsDecorativeTextButKeepsAncestors()
    {
        var browser = new RecordingCefSemanticBrowser(
        [
            Node(
                1,
                "RootWebArea",
                "Example",
                parentId: null,
                childIds: ["2", "3"]),
            Node(2, "StaticText", "Decorative copy"),
            Node(3, "link", "Continue"),
        ]);
        var adapter = new CefBrowserSemanticAdapter(browser);

        var result = await adapter.CaptureSnapshotAsync(
            new BrowserSnapshotQuery(interactiveOnly: true));

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!.Nodes,
            root => Assert.Equal("root-web-area", root.Role),
            link => Assert.Equal("Continue", link.Name));
    }

    [Fact]
    public async Task ASecondSnapshotInvalidatesEveryPriorNativeLease()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var first = await adapter.CaptureSnapshotAsync();
        var oldHandle = first.Value!.Nodes[1].Handle!;

        _ = await adapter.CaptureSnapshotAsync();
        var result = await adapter.ClickAsync(oldHandle);

        Assert.Equal(NativeBrowserClickStatus.Stale, result.Status);
        Assert.Equal(0, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task IdentityChangeBeforeDispatchFailsStaleWithoutInput()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();
        var handle = snapshot.Value!.Nodes[1].Handle!;
        browser.SetNode(Node(2, "button", "Replaced"));

        var result = await adapter.ClickAsync(handle);

        Assert.Equal(NativeBrowserClickStatus.Stale, result.Status);
        Assert.Equal(0, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task GeometryOrHitTestFailureBeforeDispatchIsNotAmbiguous()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();
        var handle = snapshot.Value!.Nodes[1].Handle!;
        browser.HitTestResult = false;

        var result = await adapter.ClickAsync(handle);

        Assert.Equal(
            NativeBrowserClickStatus.NotInteractable,
            result.Status);
        Assert.Equal(0, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task SemanticClickRetriesGeometryAfterScrollingAnOffscreenElement()
    {
        var browser = BrowserWithInteractiveNodes();
        browser.HitTestFailuresBeforeSuccess = 1;
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.ClickAsync(
            snapshot.Value!.Nodes[1].Handle!);

        Assert.Equal(NativeBrowserClickStatus.Activated, result.Status);
        Assert.Equal(2, browser.PrepareClickPointCount);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task FailureAfterInputDispatchBeginsIsOutcomeUnknown()
    {
        var browser = BrowserWithInteractiveNodes();
        browser.ThrowDuringClickDispatch = true;
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.ClickAsync(
            snapshot.Value!.Nodes[1].Handle!);

        Assert.Equal(
            NativeBrowserClickStatus.OutcomeUnknown,
            result.Status);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task FillRequiresExactObservedValueAfterAcknowledgedInput()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.FillAsync(
            snapshot.Value!.Nodes[2].Handle!,
            "Ada");

        Assert.Equal(NativeBrowserFillStatus.Filled, result.Status);
        Assert.Equal(1, browser.ReplaceTextCount);
        Assert.Equal("Ada", browser.ReadNode(3)?.Value);
    }

    [Fact]
    public async Task FillMismatchIsKnownUnsupportedValueNotSuccess()
    {
        var browser = BrowserWithInteractiveNodes();
        browser.PreserveValueOnReplace = true;
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.FillAsync(
            snapshot.Value!.Nodes[2].Handle!,
            "Ada");

        Assert.Equal(
            NativeBrowserFillStatus.ValueNotSupported,
            result.Status);
        Assert.Equal(1, browser.ReplaceTextCount);
    }

    [Fact]
    public async Task CheckRequiresTheCheckedPostcondition()
    {
        var browser = BrowserWithInteractiveNodes();
        browser.CheckNodeAfterClick = 4;
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.CheckAsync(
            snapshot.Value!.Nodes[3].Handle!);

        Assert.Equal(NativeBrowserCheckStatus.Checked, result.Status);
        Assert.Equal(1, browser.ClickDispatchCount);
        Assert.Equal("true", browser.ReadNode(4)?.Properties["checked"]);
    }

    [Fact]
    public async Task CheckWithoutPostconditionReportsObservedUncheckedState()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.CheckAsync(
            snapshot.Value!.Nodes[3].Handle!);

        Assert.Equal(
            NativeBrowserCheckStatus.Unchecked,
            result.Status);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task CheckUsesExactlyOneStateChangingActivation()
    {
        var browser = BrowserWithInteractiveNodes();
        browser.CheckNodeAfterClick = 4;
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();

        var result = await adapter.CheckAsync(
            snapshot.Value!.Nodes[3].Handle!);

        Assert.Equal(NativeBrowserCheckStatus.Checked, result.Status);
        Assert.Equal(1, browser.ClickDispatchCount);
        Assert.Equal("true", browser.ReadNode(4)?.Properties["checked"]);
    }

    [Fact]
    public async Task ElementStateObservationRevalidatesWithoutConsumingTheOpaqueLease()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();
        var handle = snapshot.Value!.Nodes[3].Handle!;

        var first = await adapter.ReadElementStateAsync(handle);
        var second = await adapter.ReadElementStateAsync(handle);
        var click = await adapter.ClickAsync(handle);

        Assert.True(first.IsSuccess);
        Assert.True(first.Value!.Visible);
        Assert.True(first.Value.Enabled);
        Assert.False(first.Value.Checked);
        Assert.True(second.IsSuccess);
        Assert.Equal(NativeBrowserClickStatus.Activated, click.Status);
        Assert.Equal(1, browser.ClickDispatchCount);
    }

    [Fact]
    public async Task ElementStateIdentityDriftReturnsStaleWithoutInput()
    {
        var browser = BrowserWithInteractiveNodes();
        var adapter = new CefBrowserSemanticAdapter(browser);
        var snapshot = await adapter.CaptureSnapshotAsync();
        var handle = snapshot.Value!.Nodes[1].Handle!;
        browser.SetNode(Node(2, "button", "Replacement"));

        var result = await adapter.ReadElementStateAsync(handle);

        Assert.False(result.IsSuccess);
        Assert.Equal(NativeBrowserElementStateFailure.Stale, result.Failure);
        Assert.Equal(0, browser.ClickDispatchCount);
    }

    private static RecordingCefSemanticBrowser BrowserWithInteractiveNodes() =>
        new(
        [
            Node(
                1,
                "RootWebArea",
                "Example",
                parentId: null,
                childIds: ["2", "3", "4"]),
            Node(2, "button", "Continue", parentId: "1"),
            Node(
                3,
                "textbox",
                "Name",
                parentId: "1",
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["required"] = "true",
                }),
            Node(
                4,
                "checkbox",
                "Remember",
                parentId: "1",
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["checked"] = "false",
                }),
        ]);

    private static CefSemanticNode Node(
        int backendNodeId,
        string role,
        string name,
        string? parentId = "1",
        IReadOnlyList<string>? childIds = null,
        IReadOnlyDictionary<string, string>? properties = null,
        string value = "") =>
        new(
            backendNodeId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            backendNodeId,
            IsIgnored: false,
            role,
            name,
            parentId,
            childIds ?? [],
            properties
                ?? new Dictionary<string, string>(StringComparer.Ordinal),
            value);

    private sealed class RecordingCefSemanticBrowser(
        IReadOnlyList<CefSemanticNode> initialNodes)
        : ICefSemanticBrowser
    {
        private readonly Dictionary<int, CefSemanticNode> _nodes =
            initialNodes.Where(node => node.BackendNodeId.HasValue)
                .ToDictionary(node => node.BackendNodeId!.Value);
        private readonly IReadOnlyList<CefSemanticNode> _generatedNodes =
            [.. initialNodes.Where(node => node.BackendNodeId is null)];

        public bool HitTestResult { get; set; } = true;

        public int HitTestFailuresBeforeSuccess { get; set; }

        public bool ThrowDuringClickDispatch { get; set; }

        public bool PreserveValueOnReplace { get; set; }

        public int? CheckNodeAfterClick { get; set; }

        public int ClickDispatchCount { get; private set; }

        public int PrepareClickPointCount { get; private set; }

        public int ReplaceTextCount { get; private set; }

        /// <summary>Nodes left out of the tree, as from a page too large to read whole.</summary>
        public HashSet<string> UnreadNodeIds { get; } = new(StringComparer.Ordinal);

        public Task<CefSemanticTree?> ReadAccessibilityTreeAsync(
            int? withinBackendNodeId = null,
            int offset = 0)
        {
            if (withinBackendNodeId is { } id && !_nodes.ContainsKey(id))
            {
                return Task.FromResult<CefSemanticTree?>(null);
            }

            return Task.FromResult<CefSemanticTree?>(new CefSemanticTree(
                [.. _nodes.Values.Concat(_generatedNodes)
                    .Where(node => !UnreadNodeIds.Contains(node.Id))
                    .OrderBy(node => node.BackendNodeId)],
                withinBackendNodeId is { } within ? _nodes[within].Id : null));
        }

        public Task<CefSemanticNode?> ReadAccessibilityNodeAsync(
            int backendNodeId) =>
            Task.FromResult(ReadNode(backendNodeId));

        public Task<CefSemanticPoint?> PrepareClickPointAsync(
            int backendNodeId, CancellationToken cancellationToken = default)
        {
            PrepareClickPointCount++;
            return Task.FromResult<CefSemanticPoint?>(new(20, 30));
        }

        public Task<bool> HitTestIncludesAsync(
            CefSemanticPoint point,
            int backendNodeId)
        {
            if (HitTestFailuresBeforeSuccess > 0)
            {
                HitTestFailuresBeforeSuccess--;
                return Task.FromResult(false);
            }

            return Task.FromResult(HitTestResult);
        }

        public Task<bool> DispatchClickAsync(
            CefSemanticPoint point,
            int backendNodeId, CancellationToken cancellationToken = default)
        {
            ClickDispatchCount++;
            if (ThrowDuringClickDispatch)
            {
                throw new InvalidOperationException("release was not acknowledged");
            }

            if (CheckNodeAfterClick is { } nodeId
                && _nodes.TryGetValue(nodeId, out var node))
            {
                var properties = node.Properties.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
                properties["checked"] = "true";
                _nodes[nodeId] = node with { Properties = properties };
            }

            return Task.FromResult(true);
        }

        public Task ReplaceFocusedTextAsync(int backendNodeId, string text, CancellationToken cancellationToken = default)
        {
            ReplaceTextCount++;
            if (!PreserveValueOnReplace
                && _nodes.TryGetValue(backendNodeId, out var node))
            {
                _nodes[backendNodeId] = node with { Value = text };
            }

            return Task.CompletedTask;
        }

        public Task<bool> IsVisibleAsync(int backendNodeId) =>
            Task.FromResult(true);

        public CefSemanticNode? ReadNode(int backendNodeId) =>
            _nodes.GetValueOrDefault(backendNodeId);

        public void SetNode(CefSemanticNode node) =>
            _nodes[node.BackendNodeId!.Value] = node;
    }
}
