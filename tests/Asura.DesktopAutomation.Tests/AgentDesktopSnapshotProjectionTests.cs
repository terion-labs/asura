using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;
using Asura.SecurityCampaign.Tests;

namespace Asura.DesktopAutomation.Tests;

public sealed class AgentDesktopSnapshotProjectionTests
{
    private const string SnapshotId = "s8f3k2p9";
    private static readonly AgentDesktopRunningApp Notes = new("Notes", 4242, "com.example.notes");
    private static readonly AgentDesktopApplicationIdentity NotesIdentity = new(
        AgentDesktopApplicationIdentityKind.BundleIdentifier,
        "com.example.notes");

    [Fact]
    public void A_snapshot_is_flattened_in_document_order_with_depths()
    {
        var snapshot = Project(Data(
            Node("window", name: "Untitled", children:
            [
                Node("group", children:
                [
                    Node("button", name: "Save", reference: Ref(1)),
                    Node("textfield", name: "Title", value: "Hello", reference: Ref(2), states: ["focused", "enabled"]),
                ]),
                Node("button", name: "Close", reference: Ref(3)),
            ])));

        Assert.NotNull(snapshot);
        Assert.Equal(SnapshotId, snapshot.SnapshotId);
        Assert.Equal(
            ["window:0", "group:1", "button:2", "textfield:2", "button:1"],
            snapshot.Nodes.Select(node => $"{node.Role.Text}:{node.Depth}"),
            StringComparer.Ordinal);
        Assert.Equal(["Untitled", null, "Save", "Title", "Close"], Names(snapshot), StringComparer.Ordinal);
        Assert.Equal("Hello", snapshot.Nodes[3].Value?.Text);
        Assert.Equal(["focused", "enabled"], snapshot.Nodes[3].States);
        Assert.Equal([null, null, Ref(1), Ref(2), Ref(3)], References(snapshot), StringComparer.Ordinal);
        Assert.Equal(3, snapshot.RefCount);
        Assert.False(snapshot.NodesTruncated);
        Assert.True(snapshot.Complete);
        Assert.False(snapshot.IdentityUnproven);
    }

    [Fact]
    public void The_application_and_process_come_from_the_resolved_application_not_from_the_tree()
    {
        var data = Data(Node("window"));
        data["app"] = "nOTES";
        data["pid"] = 1;
        data["process_instance"] = "forged";
        data["bundle_id"] = "com.example.forged";

        var snapshot = Project(data, processInstance: "generation-7");

        Assert.NotNull(snapshot);
        Assert.Equal("Notes", snapshot.Application.Text);
        Assert.Same(NotesIdentity, snapshot.ApplicationIdentity);
        Assert.Equal(4242u, snapshot.ProcessId);
        Assert.Equal("generation-7", snapshot.ProcessInstance);
    }

    [Theory]
    [InlineData("\"Other\"")]
    [InlineData("\"Notes \"")]
    [InlineData("\"Notes Helper\"")]
    [InlineData("\"com.example.notes\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[\"Notes\"]")]
    public void A_tree_that_claims_to_describe_another_application_is_refused(string app)
    {
        var data = Data(Node("window"));
        data["app"] = JsonNode.Parse(app);

        Assert.Null(Project(data));
    }

    [Fact]
    public void A_tree_that_names_no_application_is_refused()
    {
        var data = Data(Node("window"));
        Assert.True(data.Remove("app"));

        Assert.Null(Project(data));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"has space\"")]
    [InlineData("\"with:colon\"")]
    [InlineData("\"../escape\"")]
    [InlineData("\"s\\u0000\"")]
    [InlineData("\"sñ\"")]
    [InlineData("\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("null")]
    [InlineData("7")]
    public void A_snapshot_identifier_that_could_not_qualify_a_reference_is_refused(string snapshotId)
    {
        var data = Data(Node("window"));
        data["snapshot_id"] = JsonNode.Parse(snapshotId);

        Assert.Null(Project(data));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"window\"")]
    [InlineData("3")]
    public void A_tree_that_is_not_an_object_is_refused(string tree)
    {
        var data = Data(Node("window"));
        data["tree"] = JsonNode.Parse(tree);

        Assert.Null(Project(data));
    }

    [Fact]
    public void A_tree_of_exactly_the_node_ceiling_is_kept_whole()
    {
        var snapshot = Project(Data(Node(
            "window",
            children: [.. Enumerable.Range(1, AgentDesktopResult.MaximumSnapshotNodes - 1).Select(Leaf)])));

        Assert.NotNull(snapshot);
        Assert.Equal(AgentDesktopResult.MaximumSnapshotNodes, snapshot.Nodes.Count);
        Assert.False(snapshot.NodesTruncated);
    }

    [Fact]
    public void A_tree_beyond_the_node_ceiling_stops_at_512_nodes_and_says_so()
    {
        var snapshot = Project(Data(Node("window", children: [.. Enumerable.Range(1, 2_000).Select(Leaf)])));

        Assert.NotNull(snapshot);
        Assert.Equal(512, snapshot.Nodes.Count);
        Assert.True(snapshot.NodesTruncated);
        Assert.Equal(Ref(511), snapshot.Nodes[^1].Reference?.Value);

        // Without the library's own count, only references that survived
        // the cut are counted.
        Assert.Equal(511, snapshot.RefCount);
    }

    [Fact]
    public void A_chain_of_exactly_the_depth_ceiling_is_kept_whole()
    {
        var snapshot = Project(DataFromJson(Chain(AgentDesktopRequest.MaximumSnapshotDepth)));

        Assert.NotNull(snapshot);
        Assert.Equal(Enumerable.Range(0, 33), snapshot.Nodes.Select(node => node.Depth));
        Assert.False(snapshot.NodesTruncated);
        Assert.DoesNotContain(snapshot.Nodes, node => node.SubtreeTruncated);
    }

    [Fact]
    public void A_chain_beyond_the_depth_ceiling_is_cut_at_depth_32_and_the_cut_node_says_so()
    {
        var snapshot = Project(DataFromJson(Chain(40)));

        Assert.NotNull(snapshot);
        Assert.Equal(Enumerable.Range(0, 33), snapshot.Nodes.Select(node => node.Depth));
        Assert.True(snapshot.NodesTruncated);
        Assert.True(snapshot.Nodes[^1].SubtreeTruncated);
        Assert.DoesNotContain(snapshot.Nodes.Take(32), node => node.SubtreeTruncated);
    }

    [Fact]
    public void A_cut_subtree_does_not_hide_the_siblings_that_follow_it()
    {
        var tree = "{\"role\":\"window\",\"children\":[" + Chain(40) + ",{\"role\":\"button\",\"name\":\"After\"}]}";

        var snapshot = Project(DataFromJson(tree));

        Assert.NotNull(snapshot);
        Assert.Equal("After", snapshot.Nodes[^1].Name?.Text);
        Assert.Equal(1, snapshot.Nodes[^1].Depth);
        Assert.True(snapshot.NodesTruncated);
    }

    [Fact]
    public void Hostile_nesting_far_beyond_both_ceilings_is_bounded_without_recursion()
    {
        // Every level is a group of twenty that each open another level: wide
        // enough to hit the node ceiling and deep enough to hit the depth one.
        var level = new StringBuilder("{\"role\":\"leaf\"}");
        for (var depth = 0; depth < 60; depth++)
        {
            var siblings = string.Join(',', Enumerable.Repeat("{\"role\":\"pad\",\"children\":[{\"role\":\"pad\"}]}", 19));
            level.Insert(0, "{\"role\":\"group\",\"children\":[").Append(',').Append(siblings).Append("]}");
        }

        var snapshot = Project(DataFromJson(level.ToString()));

        Assert.NotNull(snapshot);
        Assert.Equal(512, snapshot.Nodes.Count);
        Assert.True(snapshot.NodesTruncated);
        Assert.All(snapshot.Nodes, node => Assert.InRange(node.Depth, 0, 32));
    }

    [Fact]
    public void A_secure_field_carries_no_value_and_keeps_the_secure_state_first()
    {
        var snapshot = Project(Data(Node("window", children:
        [
            Node(
                "textfield",
                name: "Password",
                value: SecurityCampaignCanaries.VaultResolved,
                reference: Ref(1),
                states: ["focused", "enabled", "secure"]),
        ])));

        Assert.NotNull(snapshot);
        var field = snapshot.Nodes[1];
        Assert.True(field.IsSecure);
        Assert.Null(field.Value);
        Assert.Equal(["secure", "focused", "enabled"], field.States);
        Assert.Equal("Password", field.Name?.Text);
        Assert.Equal(Ref(1), field.Reference?.Value);
    }

    [Theory]
    [InlineData("role", "AXSecureTextField")]
    [InlineData("role", "passwordfield")]
    [InlineData("subrole", "AXSecureTextField")]
    [InlineData("role_description", "secure text field")]
    [InlineData("role_description", "Password Text Field")]
    public void A_field_only_its_role_text_marks_as_secret_is_secure_all_the_same(string property, string text)
    {
        var field = Node("textfield", value: SecurityCampaignCanaries.VaultResolved, states: ["enabled"]);
        field[property] = text;

        var snapshot = Project(Data(Node("window", children: [field])));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Nodes[1].IsSecure);
        Assert.Null(snapshot.Nodes[1].Value);
        Assert.Equal(["secure", "enabled"], snapshot.Nodes[1].States);
    }

    [Fact]
    public void The_sixteen_state_cap_cannot_drop_the_secure_state()
    {
        var states = Enumerable.Range(0, 30).Select(index => "state" + (char)('a' + index % 26) + (char)('a' + index / 26));

        var snapshot = Project(Data(Node(
            "textfield",
            value: SecurityCampaignCanaries.VaultResolved,
            states: [.. states, "secure"])));

        Assert.NotNull(snapshot);
        Assert.Equal(AgentDesktopSnapshotNode.MaximumStates, snapshot.Nodes[0].States.Count);
        Assert.Equal("secure", snapshot.Nodes[0].States[0]);
        Assert.Null(snapshot.Nodes[0].Value);
    }

    [Fact]
    public void Padding_the_state_list_cannot_push_the_secure_state_out_of_sight()
    {
        var padding = Enumerable.Range(0, 5_000).Select(_ => "enabled");

        var snapshot = Project(Data(Node(
            "textfield",
            value: SecurityCampaignCanaries.VaultResolved,
            states: [.. padding, "secure", "focused"])));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Nodes[0].IsSecure);
        Assert.Null(snapshot.Nodes[0].Value);
        Assert.Equal(["secure", "enabled"], snapshot.Nodes[0].States);
    }

    [Fact]
    public void No_part_of_a_projected_snapshot_repeats_a_secure_value()
    {
        var snapshot = Project(Data(Node("window", children:
        [
            Node("textfield", name: "API key", value: SecurityCampaignCanaries.ApplicationManaged, states: ["secure"]),
            Node("textfield", name: "Passphrase", value: SecurityCampaignCanaries.VaultResolved, states: ["secure"]),
        ])));

        Assert.NotNull(snapshot);
        var everything = string.Join(
            '\n',
            snapshot.Nodes.SelectMany(node => new[] { node.Role.Text, node.Name?.Text, node.Value?.Text }.Concat(node.States)));
        Assert.DoesNotContain(SecurityCampaignCanaries.ApplicationManaged, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(SecurityCampaignCanaries.VaultResolved, everything, StringComparison.Ordinal);
    }

    [Fact]
    public void States_outside_the_lowercase_vocabulary_are_dropped()
    {
        var states = new JsonArray("enabled", "Focused", "has space", string.Empty, 7, null, new JsonObject(), "enabled", "x1");
        var node = Node("button");
        node["states"] = states;

        var snapshot = Project(Data(node));

        Assert.NotNull(snapshot);
        Assert.Equal(["enabled"], snapshot.Nodes[0].States);
    }

    [Theory]
    [InlineData("\"enabled\"")]
    [InlineData("{\"secure\":false}")]
    [InlineData("null")]
    public void A_state_list_that_is_not_an_array_reads_as_no_states(string states)
    {
        var node = Node("button", value: "visible");
        node["states"] = JsonNode.Parse(states);

        var snapshot = Project(Data(node));

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Nodes[0].States);
        Assert.Equal("visible", snapshot.Nodes[0].Value?.Text);
    }

    [Fact]
    public void A_reference_from_another_snapshot_is_dropped_not_relabelled()
    {
        var snapshot = Project(Data(Node("window", children:
        [
            Node("button", name: "Mine", reference: Ref(1)),
            Node("button", name: "Foreign", reference: "@sOTHER000:e2"),
            Node("button", name: "Bare", reference: "@e3"),
            Node("button", name: "Unqualified", reference: "e4"),
            Node("button", name: "Padded", reference: $"@{SnapshotId}:e05"),
            Node("button", name: "Prefixed", reference: $" @{SnapshotId}:e6"),
            Node("button", name: "Lookalike", reference: $"@{SnapshotId}x:e7"),
        ])));

        Assert.NotNull(snapshot);
        Assert.Equal([null, Ref(1), null, null, null, null, null, null], References(snapshot), StringComparer.Ordinal);
        Assert.Equal(1, snapshot.RefCount);
        Assert.All(
            snapshot.Nodes.Where(node => node.Reference is not null),
            node => Assert.Equal(SnapshotId, node.Reference!.Value.SnapshotId));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("[\"@s8f3k2p9:e1\"]")]
    [InlineData("{\"ref\":\"@s8f3k2p9:e1\"}")]
    public void A_reference_that_is_not_text_is_dropped(string reference)
    {
        var node = Node("button");
        node["ref_id"] = JsonNode.Parse(reference);

        var snapshot = Project(Data(node));

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Nodes[0].Reference);
        Assert.Equal(0, snapshot.RefCount);
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("0", 0)]
    [InlineData("-1", 1)]
    [InlineData("\"9\"", 1)]
    [InlineData("1.5", 1)]
    [InlineData("99999999999", 1)]
    [InlineData("null", 1)]
    public void The_reference_count_is_the_librarys_when_it_is_a_count_and_the_surviving_references_otherwise(
        string reported,
        int expected)
    {
        var data = Data(Node("button", reference: Ref(1)));
        data["ref_count"] = JsonNode.Parse(reported);

        var snapshot = Project(data);

        Assert.NotNull(snapshot);
        Assert.Equal(expected, snapshot.RefCount);
    }

    [Fact]
    public void Children_that_are_not_objects_are_ignored()
    {
        var root = Node("window");
        root["children"] = new JsonArray(
            1,
            "button",
            null,
            true,
            new JsonArray(Node("button", name: "Nested in an array")),
            Node("button", name: "Real"),
            2.5);

        var snapshot = Project(Data(root));

        Assert.NotNull(snapshot);
        Assert.Equal(["window", "button"], Roles(snapshot), StringComparer.Ordinal);
        Assert.Equal("Real", snapshot.Nodes[1].Name?.Text);
        Assert.False(snapshot.NodesTruncated);
    }

    [Theory]
    [InlineData("{\"role\":\"button\"}")]
    [InlineData("\"button\"")]
    [InlineData("null")]
    [InlineData("3")]
    [InlineData("[]")]
    public void A_children_value_that_is_not_a_filled_array_means_no_children(string children)
    {
        var root = Node("window");
        root["children"] = JsonNode.Parse(children);

        var snapshot = Project(Data(root));

        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Nodes);
        Assert.False(snapshot.Nodes[0].SubtreeTruncated);
    }

    [Fact]
    public void Text_from_the_tree_is_bounded_and_unsafe_text_is_replaced()
    {
        var snapshot = Project(Data(Node("window", children:
        [
            Node("button", name: new string('n', 500), value: new string('v', 500)),
            Node("button", name: "line one\nline two", value: "bell\u0007"),
            Node("button", name: "\u202Eevil", value: "zero\u200Bwidth"),
        ])));

        Assert.NotNull(snapshot);
        var bounded = snapshot.Nodes[1];
        Assert.Equal(AgentDesktopDisplayText.MaximumTextBytes, bounded.Name?.Text.Length);
        Assert.True(bounded.Name?.Truncated);
        Assert.Equal(AgentDesktopDisplayText.MaximumTextBytes, bounded.Value?.Text.Length);
        foreach (var node in snapshot.Nodes.Skip(2))
        {
            Assert.Equal(AgentDesktopDisplayText.Redaction, node.Name?.Text);
            Assert.Equal(AgentDesktopDisplayText.Redaction, node.Value?.Text);
        }
    }

    [Fact]
    public void A_node_without_a_role_or_name_gets_a_placeholder_role_and_the_next_best_label()
    {
        var described = new JsonObject { ["description"] = "From description", ["placeholder"] = "From placeholder" };
        var placeheld = new JsonObject { ["role"] = 7, ["name"] = "   ", ["placeholder"] = "From placeholder" };
        var bare = new JsonObject();
        var root = Node("window");
        root["children"] = new JsonArray(described, placeheld, bare);

        var snapshot = Project(Data(root));

        Assert.NotNull(snapshot);
        Assert.Equal(["window", "unknown", "unknown", "unknown"], Roles(snapshot), StringComparer.Ordinal);
        Assert.Equal([null, "From description", "From placeholder", null], Names(snapshot), StringComparer.Ordinal);
    }

    [Fact]
    public void The_window_identity_is_kept_only_when_it_is_a_bounded_token()
    {
        Assert.Equal("w-12", WithWindow("{\"id\":\"w-12\",\"title\":\"Untitled\"}")?.WindowId);
        Assert.Equal("Untitled", WithWindow("{\"id\":\"w-12\",\"title\":\"Untitled\"}")?.WindowTitle?.Text);
        Assert.Null(WithWindow("{\"id\":\"w-12\\nforged\",\"title\":\"Untitled\"}")?.WindowId);
        Assert.Null(WithWindow("{\"id\":\"" + new string('w', 129) + "\"}")?.WindowId);
        Assert.Null(WithWindow("{\"id\":\"   \"}")?.WindowId);
        Assert.Null(WithWindow("{\"id\":12}")?.WindowId);
        Assert.Null(WithWindow("{\"title\":\"Untitled\"}")?.WindowId);
        Assert.Null(WithWindow("\"w-12\"")?.WindowId);
        Assert.Null(WithWindow("\"w-12\"")?.WindowTitle);
        Assert.Equal(AgentDesktopDisplayText.Redaction, WithWindow("{\"id\":\"w-12\",\"title\":\"a\\u0000b\"}")?.WindowTitle?.Text);
    }

    [Fact]
    public void A_snapshot_is_complete_only_when_the_library_says_so_without_reservation()
    {
        Assert.True(WithFlags(complete: true, truncated: null)?.Complete);
        Assert.False(WithFlags(complete: true, truncated: true)?.Complete);
        Assert.False(WithFlags(complete: false, truncated: null)?.Complete);
        Assert.False(WithFlags(complete: null, truncated: null)?.Complete);
        Assert.False(WithFlagText("\"complete\":\"true\"")?.Complete);
        Assert.False(WithFlagText("\"complete\":1")?.Complete);
        Assert.True(WithFlagText("\"complete\":true,\"identity_unproven\":true")?.IdentityUnproven);
        Assert.False(WithFlagText("\"complete\":true,\"identity_unproven\":\"true\"")?.IdentityUnproven);
    }

    [Fact]
    public void A_subtree_the_library_cut_short_is_reported_on_its_node()
    {
        var cut = Node("list", name: "Rows");
        cut["subtree_truncated"] = true;
        var merelyClaimed = Node("list", name: "Other");
        merelyClaimed["subtree_truncated"] = "true";
        var root = Node("window");
        root["children"] = new JsonArray(cut, merelyClaimed);

        var snapshot = Project(Data(root));

        Assert.NotNull(snapshot);
        Assert.Equal([false, true, false], snapshot.Nodes.Select(node => node.SubtreeTruncated));
        Assert.False(snapshot.NodesTruncated);
    }

    [Fact]
    public void Secure_detection_reads_the_state_or_the_role_texts()
    {
        Assert.True(AgentDesktopSnapshotProjection.IsSecure(["secure"]));
        Assert.True(AgentDesktopSnapshotProjection.IsSecure([], "AXSecureTextField"));
        Assert.True(AgentDesktopSnapshotProjection.IsSecure([], null, "PASSWORD field"));
        Assert.False(AgentDesktopSnapshotProjection.IsSecure([]));
        Assert.False(AgentDesktopSnapshotProjection.IsSecure(["Secure", "enabled"], "textfield", null));
    }

    private static string Ref(int index) => $"@{SnapshotId}:e{index}";

    private static IEnumerable<string?> Roles(AgentDesktopResult.Snapshot snapshot) =>
        snapshot.Nodes.Select(node => node.Role.Text);

    private static IEnumerable<string?> Names(AgentDesktopResult.Snapshot snapshot) =>
        snapshot.Nodes.Select(node => node.Name?.Text);

    private static IEnumerable<string?> References(AgentDesktopResult.Snapshot snapshot) =>
        snapshot.Nodes.Select(node => node.Reference?.Value);

    private static JsonObject Leaf(int index) => Node("button", reference: Ref(index));

    private static JsonObject Node(
        string role,
        string? name = null,
        string? value = null,
        string? reference = null,
        string[]? states = null,
        JsonObject[]? children = null)
    {
        var node = new JsonObject { ["role"] = role };
        if (name is not null)
        {
            node["name"] = name;
        }

        if (value is not null)
        {
            node["value"] = value;
        }

        if (reference is not null)
        {
            node["ref_id"] = reference;
        }

        if (states is not null)
        {
            node["states"] = new JsonArray([.. states.Select(state => (JsonNode)state)]);
        }

        if (children is not null)
        {
            node["children"] = new JsonArray([.. children]);
        }

        return node;
    }

    private static JsonObject Data(JsonObject tree) => new()
    {
        ["app"] = Notes.Name,
        ["snapshot_id"] = SnapshotId,
        ["complete"] = true,
        ["tree"] = tree,
    };

    /// <summary>A tree given as text, for nesting deeper than the node writer allows.</summary>
    private static string DataFromJson(string tree, string flags = "\"complete\":true") =>
        "{\"app\":\"Notes\",\"snapshot_id\":\"" + SnapshotId + "\"," + flags + ",\"tree\":" + tree + "}";

    /// <summary>One node per level, <paramref name="depth"/> levels below the root.</summary>
    private static string Chain(int depth)
    {
        var chain = new StringBuilder("{\"role\":\"leaf\"}");
        for (var level = 0; level < depth; level++)
        {
            chain.Insert(0, "{\"role\":\"group\",\"children\":[").Append("]}");
        }

        return chain.ToString();
    }

    private static AgentDesktopResult.Snapshot? Project(JsonObject data, string? processInstance = null) =>
        Project(data.ToJsonString(), processInstance);

    private static AgentDesktopResult.Snapshot? Project(string data, string? processInstance = null)
    {
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 512 });
        return AgentDesktopSnapshotProjection.Project(document.RootElement, Notes, NotesIdentity, processInstance);
    }

    private static AgentDesktopResult.Snapshot? WithWindow(string window)
    {
        var data = Data(Node("window"));
        data["window"] = JsonNode.Parse(window);
        return Project(data);
    }

    private static AgentDesktopResult.Snapshot? WithFlags(bool? complete, bool? truncated)
    {
        var data = Data(Node("window"));
        Assert.True(data.Remove("complete"));
        if (complete is { } isComplete)
        {
            data["complete"] = isComplete;
        }

        if (truncated is { } isTruncated)
        {
            data["truncated"] = isTruncated;
        }

        return Project(data);
    }

    private static AgentDesktopResult.Snapshot? WithFlagText(string flags) =>
        Project(DataFromJson("{\"role\":\"window\"}", flags));
}
