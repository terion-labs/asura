using System.Reflection;

namespace Asura.Application.Tests;

public sealed class AgentDesktopResultTests
{
    private const string SnapshotId = "s8f3k2p9";

    private static readonly AgentDesktopApplicationIdentity Identity = new(
        AgentDesktopApplicationIdentityKind.BundleIdentifier,
        "com.apple.TextEdit");

    private static readonly AgentDesktopFailureCode[] RetryableCodes =
    [
        AgentDesktopFailureCode.StaleRef,
        AgentDesktopFailureCode.AmbiguousTarget,
        AgentDesktopFailureCode.Timeout,
        AgentDesktopFailureCode.AppNotFound,
        AgentDesktopFailureCode.WindowNotFound,
        AgentDesktopFailureCode.ElementNotFound,
        AgentDesktopFailureCode.SnapshotNotFound,
        AgentDesktopFailureCode.PermissionDenied,
        AgentDesktopFailureCode.PolicyDenied,
        AgentDesktopFailureCode.ActionNotSupported,
    ];

    [Fact]
    public void SnapshotAcceptsFiveHundredTwelveNodesAndNoMore()
    {
        Assert.Equal(512, AgentDesktopResult.MaximumSnapshotNodes);

        var full = Snapshot(Enumerable.Range(0, 512).Select(index => Node(reference: $"@{SnapshotId}:e{index}")));

        Assert.Equal(512, full.Nodes.Count);
        Assert.Throws<ArgumentException>(() => Snapshot(Enumerable.Range(0, 513).Select(_ => Node())));
    }

    [Fact]
    public void SnapshotCopiesItsNodesAndRejectsMissingOnes()
    {
        var nodes = new List<AgentDesktopSnapshotNode> { Node() };

        var snapshot = Snapshot(nodes);
        nodes.Add(Node());

        Assert.Single(snapshot.Nodes);
        Assert.False(snapshot.Nodes is AgentDesktopSnapshotNode[]);
        Assert.Throws<ArgumentException>(() => Snapshot([Node(), null!]));
        Assert.Throws<ArgumentNullException>(() => Snapshot(null!));
    }

    [Fact]
    public void SnapshotRefusesARefMintedByAnotherSnapshot()
    {
        var foreign = Node(reference: "@t0000001:e1");

        Assert.Throws<ArgumentException>(() => Snapshot([Node(reference: $"@{SnapshotId}:e1"), foreign]));
        Assert.Equal(2, Snapshot([Node(reference: $"@{SnapshotId}:e1"), Node()]).Nodes.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("s")]
    [InlineData("s8")]
    [InlineData("s:1")]
    [InlineData("s/1")]
    [InlineData("@s8f3k2p9")]
    [InlineData("sé")]
    public void SnapshotIdentifierIsABoundedToken(string snapshotId)
    {
        Assert.ThrowsAny<ArgumentException>(() => Snapshot([], snapshotId));
    }

    [Fact]
    public void SnapshotRequiresALiveProcessAndSaneCounts()
    {
        var application = Display("TextEdit");

        Assert.Equal(64, Snapshot([], new string('s', 64)).SnapshotId.Length);
        Assert.Equal(3, Snapshot([], "s8f").SnapshotId.Length);
        Assert.Same(Identity, Snapshot([]).ApplicationIdentity);
        Assert.Equal("w-7", Snapshot([]).WindowId);
        Assert.Throws<ArgumentException>(() => Snapshot([], new string('s', 65)));
        Assert.Throws<ArgumentNullException>(() => Snapshot([], null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopResult.Snapshot(
            SnapshotId, application, Identity, 0, null, null, null, true, false, [], false, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopResult.Snapshot(
            SnapshotId, application, Identity, 4242, null, null, null, true, false, [], false, -1));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopResult.Snapshot(
            SnapshotId, null!, Identity, 4242, null, null, null, true, false, [], false, 0));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopResult.Snapshot(
            SnapshotId, application, null!, 4242, null, null, null, true, false, [], false, 0));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(32, true)]
    [InlineData(-1, false)]
    [InlineData(33, false)]
    public void NodeDepthIsBoundedToThirtyTwo(int depth, bool accepted)
    {
        if (accepted)
        {
            Assert.Equal(depth, Node(depth: depth).Depth);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Node(depth: depth));
        }
    }

    [Fact]
    public void NodeKeepsOnlyBoundedLowercaseStates()
    {
        var node = Node(states:
        [
            "focused", "focused", "Enabled", "has space", "with-dash", "digit1", string.Empty,
            new string('a', 32), new string('b', 33), "checked",
        ]);
        var many = Node(states: Enumerable.Range(0, 26).Select(index => "state" + (char)('a' + index)));

        Assert.Equal(["focused", new string('a', 32), "checked"], node.States);
        Assert.Equal(AgentDesktopSnapshotNode.MaximumStates, many.States.Count);
        Assert.Equal(16, AgentDesktopSnapshotNode.MaximumStates);
        Assert.Throws<ArgumentException>(() => Node(states: ["focused", null!]));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopSnapshotNode(0, Display("button"), null, null, null, null!, subtreeTruncated: false));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopSnapshotNode(0, null!, null, null, null, [], subtreeTruncated: false));
    }

    [Fact]
    public void SecureFieldNeverCarriesAValue()
    {
        var secure = Node(states: ["secure", "focused"]);

        Assert.True(secure.IsSecure);
        Assert.Null(secure.Value);
        Assert.False(Node(states: ["focused"]).IsSecure);
        Assert.Throws<ArgumentException>(() => Node(value: "hunter2", states: ["secure"]));
        Assert.Equal("draft", Node(value: "draft", states: ["focused"]).Value?.Text);
    }

    [Fact]
    public void ListsAcceptOneHundredTwentyEightEntriesAndNoMore()
    {
        Assert.Equal(128, AgentDesktopResult.MaximumListEntries);

        var apps = Enumerable.Range(1, 129).Select(index => App((uint)index)).ToArray();
        var windows = Enumerable.Range(1, 129).Select(index => WindowEntry($"w-{index}")).ToArray();

        Assert.Equal(128, new AgentDesktopResult.AppList(apps.Take(128), truncated: true).Apps.Count);
        Assert.True(new AgentDesktopResult.AppList(apps.Take(128), truncated: true).Truncated);
        Assert.Equal(128, new AgentDesktopResult.WindowList(windows.Take(128), truncated: false).Windows.Count);
        Assert.Throws<ArgumentException>(() => new AgentDesktopResult.AppList(apps, truncated: true));
        Assert.Throws<ArgumentException>(() => new AgentDesktopResult.WindowList(windows, truncated: true));
    }

    [Fact]
    public void ListsCopyTheirEntriesAndRejectMissingOnes()
    {
        var apps = new List<AgentDesktopApplicationEntry> { App(1) };
        var windows = new List<AgentDesktopWindowEntry> { WindowEntry("w-1") };

        var appList = new AgentDesktopResult.AppList(apps, truncated: false);
        var windowList = new AgentDesktopResult.WindowList(windows, truncated: false);
        apps.Add(App(2));
        windows.Add(WindowEntry("w-2"));

        Assert.Single(appList.Apps);
        Assert.Single(windowList.Windows);
        Assert.Throws<ArgumentException>(() => new AgentDesktopResult.AppList([App(1), null!], truncated: false));
        Assert.Throws<ArgumentException>(() => new AgentDesktopResult.WindowList([null!], truncated: false));
        Assert.Same(Identity, windowList.Windows[0].ApplicationIdentity);
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopWindowEntry(
            "w-1", Display("Untitled"), Display("TextEdit"), null!, 4242, null, IsFocused: false));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopResult.AppList(null!, truncated: false));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopResult.WindowList(null!, truncated: false));
    }

    [Fact]
    public void DeliveredActionIsVerifiedOnlyWhenTheLibrarySaysSo()
    {
        Assert.All(Enum.GetValues<AgentDesktopDelivery>(), delivery =>
            Assert.Equal(
                delivery == AgentDesktopDelivery.DeliveredVerified,
                new AgentDesktopResult.ActionDelivered(delivery, null, null, null).Verified));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopResult.ActionDelivered((AgentDesktopDelivery)5, null, null, null));

        var delivered = new AgentDesktopResult.ActionDelivered(
            AgentDesktopDelivery.DeliveredVerified,
            Display("checkbox"),
            Display("1"),
            ["checked", "checked", "Not A State", "focused"]);

        Assert.Equal(["checked", "focused"], delivered.PostStates);
        Assert.Empty(new AgentDesktopResult.ActionDelivered(
            AgentDesktopDelivery.DeliveredUnverified, null, null, null).PostStates);
        var missingState = Assert.Throws<ArgumentException>(() => new AgentDesktopResult.ActionDelivered(
            AgentDesktopDelivery.DeliveredVerified, null, null, ["checked", null!]));
        Assert.Equal("postStates", missingState.ParamName);
        Assert.Equal(
            AgentDesktopSnapshotNode.MaximumStates,
            new AgentDesktopResult.ActionDelivered(
                AgentDesktopDelivery.DeliveredVerified,
                null,
                null,
                Enumerable.Range(0, 26).Select(index => "state" + (char)('a' + index))).PostStates.Count);
    }

    [Fact]
    public void ResultAndExecutionUnionsAreClosed()
    {
        Assert.Equal(
            [
                typeof(AgentDesktopResult.ActionDelivered),
                typeof(AgentDesktopResult.AppCloseRequested),
                typeof(AgentDesktopResult.AppLaunched),
                typeof(AgentDesktopResult.AppList),
                typeof(AgentDesktopResult.Snapshot),
                typeof(AgentDesktopResult.WaitCompleted),
                typeof(AgentDesktopResult.WindowList),
            ],
            typeof(AgentDesktopResult).GetNestedTypes(BindingFlags.Public)
                .OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.Equal(
            [typeof(AgentDesktopExecutionResult.Failed), typeof(AgentDesktopExecutionResult.Succeeded)],
            typeof(AgentDesktopExecutionResult).GetNestedTypes(BindingFlags.Public)
                .OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopExecutionResult.Succeeded(null!));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopExecutionResult.Failed(null!));
    }

    [Fact]
    public void FailureDefaultsToNotDeliveredAndRejectsUndefinedValues()
    {
        var failure = new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef);

        Assert.Equal(AgentDesktopFailureCode.StaleRef, failure.Code);
        Assert.Equal(AgentDesktopDelivery.NotDelivered, failure.Delivery);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopFailure((AgentDesktopFailureCode)18));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopFailure((AgentDesktopFailureCode)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef, (AgentDesktopDelivery)5));
        Assert.Equal(18, Enum.GetValues<AgentDesktopFailureCode>().Length);
        Assert.Equal(5, Enum.GetValues<AgentDesktopDelivery>().Length);
    }

    [Fact]
    public void OnlyAnUndeliveredRefusalThatCanClearIsRetryable()
    {
        foreach (var code in Enum.GetValues<AgentDesktopFailureCode>())
        {
            foreach (var delivery in Enum.GetValues<AgentDesktopDelivery>())
            {
                var expected = delivery == AgentDesktopDelivery.NotDelivered && RetryableCodes.Contains(code);

                Assert.True(
                    expected == new AgentDesktopFailure(code, delivery).Retryable,
                    $"{code} with {delivery} delivery: expected retryable {expected}.");
            }
        }
    }

    [Fact]
    public void RetryableFailuresReportExactlyTheRetryableCodesOfTheDecision()
    {
        // ADR 0061, "Outcome mapping": not_delivered with retry safe.
        string[] decided =
        [
            "desktop_stale_ref",
            "desktop_ambiguous_target",
            "desktop_timeout_before_delivery",
            "desktop_policy_denied",
            "desktop_permission_denied",
            "desktop_app_not_found",
            "desktop_window_not_found",
            "desktop_not_supported",
        ];

        var reported = Enum.GetValues<AgentDesktopFailureCode>()
            .Select(code => new AgentDesktopFailure(code))
            .Where(failure => failure.Retryable)
            .Select(AgentDesktopStableCodes.For)
            .Distinct(StringComparer.Ordinal);

        Assert.Equal(
            decided.Order(StringComparer.Ordinal),
            reported.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(AgentDesktopFailureCode.Unavailable, "desktop_unavailable")]
    [InlineData(AgentDesktopFailureCode.PermissionDenied, "desktop_permission_denied")]
    [InlineData(AgentDesktopFailureCode.SelfTarget, "desktop_self_target")]
    [InlineData(AgentDesktopFailureCode.AppNotFound, "desktop_app_not_found")]
    [InlineData(AgentDesktopFailureCode.WindowNotFound, "desktop_window_not_found")]
    [InlineData(AgentDesktopFailureCode.ElementNotFound, "desktop_stale_ref")]
    [InlineData(AgentDesktopFailureCode.StaleRef, "desktop_stale_ref")]
    [InlineData(AgentDesktopFailureCode.AmbiguousTarget, "desktop_ambiguous_target")]
    [InlineData(AgentDesktopFailureCode.SnapshotNotFound, "desktop_stale_ref")]
    [InlineData(AgentDesktopFailureCode.Timeout, "desktop_timeout_before_delivery")]
    [InlineData(AgentDesktopFailureCode.PolicyDenied, "desktop_policy_denied")]
    [InlineData(AgentDesktopFailureCode.ActionFailed, "desktop_action_failed")]
    [InlineData(AgentDesktopFailureCode.ActionNotSupported, "desktop_not_supported")]
    [InlineData(AgentDesktopFailureCode.InvalidArguments, "desktop_invalid_arguments")]
    [InlineData(AgentDesktopFailureCode.SecureField, "desktop_secure_field")]
    [InlineData(AgentDesktopFailureCode.OutcomeUnknown, "desktop_action_outcome_unknown")]
    [InlineData(AgentDesktopFailureCode.EngineFaulted, "desktop_adapter_faulted")]
    [InlineData(AgentDesktopFailureCode.Cancelled, "desktop_cancelled")]
    public void UndeliveredFailureHasOneStableCode(AgentDesktopFailureCode code, string stableCode)
    {
        Assert.Equal(stableCode, AgentDesktopStableCodes.For(new AgentDesktopFailure(code)));
    }

    [Fact]
    public void StableCodeMappingIsTotalAndClosed()
    {
        var declared = DeclaredStableCodes();

        Assert.Equal(18, declared.Length);
        Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
        Assert.All(declared, code => Assert.Matches("^desktop_[a-z]+(?:_[a-z]+)*$", code));
        foreach (var code in Enum.GetValues<AgentDesktopFailureCode>())
        {
            foreach (var delivery in Enum.GetValues<AgentDesktopDelivery>())
            {
                Assert.Contains(
                    AgentDesktopStableCodes.For(new AgentDesktopFailure(code, delivery)),
                    declared,
                    StringComparer.Ordinal);
            }
        }

        Assert.Throws<ArgumentNullException>(() => AgentDesktopStableCodes.For(null!));
    }

    [Fact]
    public void AnyFailureThatMayHaveReachedTheApplicationIsAnUnknownOutcome()
    {
        // A timeout, cancellation or refusal reported together with anything
        // but "not delivered" must never read as safe to repeat, and must make
        // the runtime reconcile before the next desktop action.
        foreach (var code in Enum.GetValues<AgentDesktopFailureCode>())
        {
            foreach (var delivery in Enum.GetValues<AgentDesktopDelivery>()
                .Where(delivery => delivery != AgentDesktopDelivery.NotDelivered))
            {
                var failure = new AgentDesktopFailure(code, delivery);
                var stableCode = AgentDesktopStableCodes.For(failure);

                Assert.Equal(
                    code switch
                    {
                        AgentDesktopFailureCode.EngineFaulted => AgentDesktopStableCodes.AdapterFaulted,
                        AgentDesktopFailureCode.SelfTarget => AgentDesktopStableCodes.SelfTargetLaunched,
                        _ => AgentDesktopStableCodes.ActionOutcomeUnknown,
                    },
                    stableCode);
                Assert.True(AgentDesktopStableCodes.RequiresReconciliation(stableCode));
                Assert.False(failure.Retryable);
            }
        }
    }

    [Theory]
    [InlineData(AgentDesktopDelivery.Unknown)]
    [InlineData(AgentDesktopDelivery.Uncertain)]
    [InlineData(AgentDesktopDelivery.DeliveredUnverified)]
    [InlineData(AgentDesktopDelivery.DeliveredVerified)]
    public void ALaunchThatStartedAsuraIsReportedAsALaunchedSelfTargetAndReconciles(AgentDesktopDelivery delivery)
    {
        // The engine reports a launch whose process turned out to be Asura's
        // as a self-target that was delivered. The audit trail must name it as
        // a self-target, and the runtime, which sees only the code, must still
        // treat the desktop as changed. The refusal before any input keeps the
        // plain code and does not reconcile.
        var launched = new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget, delivery);
        var refused = new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget);

        Assert.Equal(AgentDesktopStableCodes.SelfTargetLaunched, AgentDesktopStableCodes.For(launched));
        Assert.True(AgentDesktopStableCodes.RequiresReconciliation(AgentDesktopStableCodes.For(launched)));
        Assert.False(launched.Retryable);

        Assert.Equal(AgentDesktopStableCodes.SelfTarget, AgentDesktopStableCodes.For(refused));
        Assert.False(AgentDesktopStableCodes.RequiresReconciliation(AgentDesktopStableCodes.For(refused)));
    }

    [Fact]
    public void AnUndeliveredFailureReconcilesOnlyForAnUnknownOutcomeOrAnAdapterFault()
    {
        foreach (var code in Enum.GetValues<AgentDesktopFailureCode>())
        {
            Assert.Equal(
                code is AgentDesktopFailureCode.OutcomeUnknown or AgentDesktopFailureCode.EngineFaulted,
                AgentDesktopStableCodes.RequiresReconciliation(
                    AgentDesktopStableCodes.For(new AgentDesktopFailure(code))));
        }
    }

    [Fact]
    public void OnlyUnknownOutcomeAdapterFaultAndALaunchedSelfTargetRequireReconciliation()
    {
        Assert.Equal(
            [
                AgentDesktopStableCodes.ActionOutcomeUnknown,
                AgentDesktopStableCodes.AdapterFaulted,
                AgentDesktopStableCodes.SelfTargetLaunched,
            ],
            DeclaredStableCodes().Where(AgentDesktopStableCodes.RequiresReconciliation).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.False(AgentDesktopStableCodes.RequiresReconciliation("desktop_action_outcome_unknown "));
        Assert.False(AgentDesktopStableCodes.RequiresReconciliation("DESKTOP_ACTION_OUTCOME_UNKNOWN"));
        Assert.False(AgentDesktopStableCodes.RequiresReconciliation(string.Empty));

        // Neither can ever be reported as retryable.
        Assert.DoesNotContain(
            Enum.GetValues<AgentDesktopFailureCode>()
                .SelectMany(code => Enum.GetValues<AgentDesktopDelivery>()
                    .Select(delivery => new AgentDesktopFailure(code, delivery))),
            failure => failure.Retryable
                && AgentDesktopStableCodes.RequiresReconciliation(AgentDesktopStableCodes.For(failure)));
    }

    private static string[] DeclaredStableCodes() =>
    [
        .. typeof(AgentDesktopStableCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    private static AgentDesktopDisplayText Display(string value) =>
        AgentDesktopDisplayText.FromUntrusted(value, "element");

    private static AgentDesktopApplicationEntry App(uint processId) =>
        new(Display("TextEdit"), processId, Display("com.apple.TextEdit"));

    private static AgentDesktopWindowEntry WindowEntry(string id) =>
        new(
            id,
            Display("Untitled"),
            Display("TextEdit"),
            Identity,
            4242,
            "generation-7",
            IsFocused: false);

    private static AgentDesktopSnapshotNode Node(
        int depth = 0,
        string? value = null,
        string? reference = null,
        IEnumerable<string>? states = null) =>
        new(
            depth,
            Display("text field"),
            Display("Body"),
            AgentDesktopDisplayText.FromUntrusted(value),
            reference is null ? null : new AgentDesktopElementRef(reference),
            states ?? [],
            subtreeTruncated: false);

    private static AgentDesktopResult.Snapshot Snapshot(
        IEnumerable<AgentDesktopSnapshotNode> nodes,
        string snapshotId = SnapshotId) =>
        new(
            snapshotId,
            Display("TextEdit"),
            Identity,
            4242,
            "generation-7",
            "w-7",
            Display("Untitled"),
            complete: true,
            identityUnproven: false,
            nodes,
            nodesTruncated: false,
            refCount: 0);
}
