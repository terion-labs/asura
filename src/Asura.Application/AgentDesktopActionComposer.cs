using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Asura.Core;

namespace Asura.Application;

/// <summary>
/// Binds one closed desktop request to the Workspace run target and a
/// one-action authorization. The digest covers every field the engine will
/// act on, including the application, process and window behind a ref, so an
/// approval cannot be replayed against another element, window, process,
/// application, or snapshot.
/// </summary>
public sealed class AgentDesktopActionComposer
{
    private const string DigestDomain = "asura.agent-desktop-action";
    private const string DigestVersion = "1";

    // Every escape below at most doubles one input byte; the quotes add two.
    private const int MaximumQuotedInputTextBytes = (2 * AgentDesktopInputText.MaximumBytes) + 2;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public AgentDesktopAction Prepare(
        AgentActionEnvelope envelope,
        AgentContextSnapshot context,
        AgentDesktopRequest request)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        RequireWorkspaceTarget(context);

        var proposal = AgentActionProposal.FromContext(
            envelope.ActionId,
            envelope.RunId,
            envelope.Actor,
            request.ToolName,
            context,
            CreateArgumentDigest(envelope.ActionId, request),
            Presentation(request, envelope.CreatedAtUtc),
            envelope.PolicyGeneration,
            envelope.CreatedAtUtc,
            envelope.DeadlineUtc);
        return new AgentDesktopAction(request, proposal);
    }

    public AgentActionExecutionBinding BindForExecution(
        AgentDesktopAction action,
        AgentContextSnapshot freshContext)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(freshContext);
        var proposal = action.Proposal;
        if (!string.Equals(proposal.ToolName, action.Request.ToolName, StringComparison.Ordinal)
            || proposal.ArgumentDigest != CreateArgumentDigest(proposal.Id, action.Request))
        {
            throw new InvalidOperationException(
                "The prepared desktop action no longer matches its typed request.");
        }

        RequireWorkspaceTarget(freshContext);
        var targetIdentity = AgentTargetIdentity.Create(freshContext.Target);
        if (proposal.Target != freshContext.Target || proposal.TargetIdentity != targetIdentity)
        {
            throw new ArgumentException(
                "The fresh desktop target does not match the original run target.",
                nameof(freshContext));
        }

        return new AgentActionExecutionBinding(
            proposal.Id,
            proposal.RunId,
            proposal.Actor.Id,
            action.Request.ToolName,
            freshContext.Target,
            targetIdentity,
            freshContext.BindingFingerprint,
            proposal.ArgumentDigest,
            proposal.PolicyGeneration);
    }

    private static void RequireWorkspaceTarget(AgentContextSnapshot context)
    {
        // Desktop tools operate outside Asura's panels, so only the complete
        // workspace target may carry them (ADR 0061). A panel, tab, or selected
        // scope would suggest a containment that does not exist.
        if (context.Target is not AgentTarget.Workspace)
        {
            throw new ArgumentException(
                "Desktop automation requires the complete Workspace run target.",
                nameof(context));
        }
    }

    private static AgentApprovalPresentation Presentation(
        AgentDesktopRequest request,
        DateTimeOffset proposedAt)
    {
        var arguments = new List<AgentApprovalArgument>
        {
            new("operation", request.ToolName),
        };
        string? host = null;
        switch (request)
        {
            case AgentDesktopRequest.ListApps:
                break;
            case AgentDesktopRequest.ListWindows windows:
                host = windows.Application?.Value;
                break;
            case AgentDesktopRequest.Snapshot snapshot:
                host = snapshot.Application.Value;
                arguments.Add(new("surface", snapshot.Surface.ToString().ToLowerInvariant()));
                arguments.Add(new("interactive_only", snapshot.InteractiveOnly ? "true" : "false"));
                arguments.Add(new("max_depth", AgentDesktopText.Invariant(snapshot.MaximumDepth)));
                break;
            case AgentDesktopRequest.Wait wait:
                host = WaitApplication(wait.Condition);
                arguments.Add(new("condition", WaitConditionName(wait.Condition)));
                arguments.Add(new("timeout_ms", AgentDesktopText.Invariant(wait.TimeoutMilliseconds)));
                break;
            case AgentDesktopRequest.ElementAction element:
                host = element.Target.Application.Text;
                AppendElementTarget(arguments, element.Target, proposedAt);
                AppendElementMaterial(arguments, element);
                break;
            case AgentDesktopRequest.FocusWindow focus:
                host = focus.Window.Application.Value;
                arguments.Add(new("window_id", focus.Window.WindowId));
                arguments.Add(new("pid", AgentDesktopText.Invariant(focus.Window.ProcessId)));
                arguments.Add(IdentityArgument(focus.Window.ApplicationIdentity));
                arguments.Add(new("window", focus.Window.Title.Text));
                arguments.Add(AgeArgument("listing_age", focus.Window.ObservedAt, proposedAt));
                break;
            case AgentDesktopRequest.LaunchApp launch:
                host = launch.Application.Value;
                arguments.Add(new("effect", "starts the application if it is not running"));
                break;
            case AgentDesktopRequest.CloseApp close:
                host = close.Target.Application.Text;
                arguments.Add(new("pid", AgentDesktopText.Invariant(close.Target.ProcessId)));
                arguments.Add(IdentityArgument(close.Target.ApplicationIdentity));
                arguments.Add(AgeArgument("observation_age", close.Target.ObservedAt, proposedAt));
                arguments.Add(new("effect", "asks the application to quit; unsaved work may prompt"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }

        return new AgentApprovalPresentation(Title(request), host, workingDirectory: null, arguments);
    }

    private static string Title(AgentDesktopRequest request) => request switch
    {
        AgentDesktopRequest.ListApps or AgentDesktopRequest.ListWindows
            or AgentDesktopRequest.Snapshot or AgentDesktopRequest.Wait =>
            "Desktop app observation",
        AgentDesktopRequest.PressKey or AgentDesktopRequest.FocusWindow
            or AgentDesktopRequest.LaunchApp or AgentDesktopRequest.CloseApp =>
            "Desktop app control",
        AgentDesktopRequest.ElementAction => "Desktop app interaction",
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    private static void AppendElementTarget(
        List<AgentApprovalArgument> arguments,
        AgentDesktopElementTarget target,
        DateTimeOffset proposedAt)
    {
        arguments.Add(new("element_ref", target.Reference.Value));
        arguments.Add(new("pid", AgentDesktopText.Invariant(target.ProcessId)));
        arguments.Add(IdentityArgument(target.ApplicationIdentity));
        if (target.WindowTitle is { } title)
        {
            arguments.Add(new("window", title.Text));
        }

        arguments.Add(new("role", target.Role.Text));
        if (target.Name is { } name)
        {
            arguments.Add(new("element", name.Text));
        }

        arguments.Add(AgeArgument("snapshot_age", target.ObservedAt, proposedAt));
    }

    // The identity is whatever the other application or its file name says,
    // so it is shown as untrusted display text like the names beside it. The
    // digest binds the exact value; only the card's copy is bounded.
    private static AgentApprovalArgument IdentityArgument(AgentDesktopApplicationIdentity identity) =>
        new(
            identity.Kind switch
            {
                AgentDesktopApplicationIdentityKind.BundleIdentifier => "bundle_id",
                AgentDesktopApplicationIdentityKind.ExecutablePath => "executable",
                _ => throw new ArgumentOutOfRangeException(nameof(identity)),
            },
            AgentDesktopDisplayText.FromUntrusted(identity.Value, "unknown").Text);

    // How old the observation was when the action was proposed. A clock that
    // stepped back in between reads as zero rather than as a negative age.
    private static AgentApprovalArgument AgeArgument(
        string name,
        DateTimeOffset observedAt,
        DateTimeOffset proposedAt)
    {
        var age = proposedAt - observedAt;
        var seconds = age > TimeSpan.Zero ? (long)age.TotalSeconds : 0;
        return new(name, $"{seconds.ToString(CultureInfo.InvariantCulture)} s when proposed");
    }

    private static void AppendElementMaterial(
        List<AgentApprovalArgument> arguments,
        AgentDesktopRequest.ElementAction request)
    {
        switch (request)
        {
            case AgentDesktopRequest.Click click:
                arguments.Add(new("click", click.Kind.ToString().ToLowerInvariant()));
                break;
            case AgentDesktopRequest.SetValue setValue:
                arguments.Add(InputTextArgument("text", setValue.Text));
                break;
            case AgentDesktopRequest.TypeText typeText:
                arguments.Add(InputTextArgument("text", typeText.Text));
                break;
            case AgentDesktopRequest.Select select:
                arguments.Add(InputTextArgument("option", select.Option));
                break;
            case AgentDesktopRequest.Clear:
                break;
            case AgentDesktopRequest.Toggle toggle:
                arguments.Add(new("toggle", toggle.Kind.ToString().ToLowerInvariant()));
                break;
            case AgentDesktopRequest.Expand expand:
                arguments.Add(new("expand", expand.Kind.ToString().ToLowerInvariant()));
                break;
            case AgentDesktopRequest.Scroll scroll:
                arguments.Add(new(
                    "scroll",
                    scroll.Direction is { } direction
                        ? $"{direction.ToString().ToLowerInvariant()} {AgentDesktopText.Invariant(scroll.Amount)}"
                        : "into view"));
                break;
            case AgentDesktopRequest.PressKey press:
                arguments.Add(new("key", KeyName(press.Key)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    // Input text may hold tabs and line breaks, or nothing but whitespace.
    // Quoting it with reversible escapes shows exactly what will be entered
    // and keeps a line break from reshaping the approval card.
    private static AgentApprovalArgument InputTextArgument(string name, AgentDesktopInputText text)
    {
        var quoted = new StringBuilder(text.Value.Length + 2).Append('"');
        foreach (var character in text.Value)
        {
            _ = character switch
            {
                '\\' => quoted.Append(@"\\"),
                '"' => quoted.Append("\\\""),
                '\t' => quoted.Append(@"\t"),
                '\n' => quoted.Append(@"\n"),
                '\r' => quoted.Append(@"\r"),
                _ => quoted.Append(character),
            };
        }

        return new AgentApprovalArgument(
            name,
            quoted.Append('"').ToString(),
            MaximumQuotedInputTextBytes);
    }

    private static string KeyName(AgentDesktopKeyPress key) =>
        key.Modifier is { } modifier
            ? $"{AgentDesktopKeyPress.ModifierToken(modifier)}+{key.Key}"
            : key.Key;

    private static string? WaitApplication(AgentDesktopWaitCondition condition) => condition switch
    {
        AgentDesktopWaitCondition.Text text => text.Application.Value,
        AgentDesktopWaitCondition.WindowTitle title => title.Application.Value,
        _ => null,
    };

    private static string WaitConditionName(AgentDesktopWaitCondition condition) => condition switch
    {
        AgentDesktopWaitCondition.Text text => $"text appears: {text.Value}",
        AgentDesktopWaitCondition.WindowTitle windowTitle => $"window title containing: {windowTitle.Value}",
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    private static AgentActionDigest CreateArgumentDigest(
        AgentActionId actionId,
        AgentDesktopRequest request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, DigestDomain);
        Append(hash, DigestVersion);
        Append(hash, actionId.Value);
        Append(hash, request.ToolName);
        foreach (var field in DigestMaterial(request))
        {
            Append(hash, field);
        }

        return new AgentActionDigest(Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static IEnumerable<string?> DigestMaterial(AgentDesktopRequest request) => request switch
    {
        AgentDesktopRequest.ListApps => [],
        AgentDesktopRequest.ListWindows windows => [windows.Application?.Value],
        AgentDesktopRequest.Snapshot snapshot =>
        [
            snapshot.Application.Value,
            snapshot.Surface.ToString(),
            snapshot.InteractiveOnly ? "1" : "0",
            snapshot.Compact ? "1" : "0",
            AgentDesktopText.Invariant(snapshot.MaximumDepth),
        ],
        AgentDesktopRequest.Wait wait =>
        [
            wait.Condition.GetType().Name,
            WaitApplication(wait.Condition),
            wait.Condition switch
            {
                AgentDesktopWaitCondition.Text text => text.Value,
                AgentDesktopWaitCondition.WindowTitle title => title.Value,
                _ => null,
            },
            AgentDesktopText.Invariant(wait.TimeoutMilliseconds),
        ],
        AgentDesktopRequest.ElementAction element =>
        [
            .. ElementTargetMaterial(element.Target),
            .. ElementActionMaterial(element),
        ],
        AgentDesktopRequest.FocusWindow focus =>
        [
            focus.Window.Application.Value,
            .. IdentityMaterial(focus.Window.ApplicationIdentity),
            focus.Window.WindowId,
            focus.Window.Title.Text,
            AgentDesktopText.Invariant(focus.Window.ProcessId),
            focus.Window.ProcessInstance,
        ],
        // A launch binds only the name the model gave: an application that is
        // not running yet has no observed process or identity to bind.
        AgentDesktopRequest.LaunchApp launch =>
            [launch.Application.Value, AgentDesktopText.Invariant(launch.TimeoutMilliseconds)],
        // A close binds the observed process, its generation and identity,
        // and the name the card shows, never a name resolved at dispatch.
        AgentDesktopRequest.CloseApp close =>
        [
            close.Target.Application.Text,
            .. IdentityMaterial(close.Target.ApplicationIdentity),
            AgentDesktopText.Invariant(close.Target.ProcessId),
            close.Target.ProcessInstance,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    // The application, window and element text are what the approval card
    // shows for the ref, so they are bound with the identity the engine acts
    // on: an approval cannot be carried over to a proposal that displays
    // another application or window for the same ref. The observation time
    // is deliberately absent. It says how fresh the target is, not which
    // target it is.
    private static IEnumerable<string?> ElementTargetMaterial(AgentDesktopElementTarget target) =>
    [
        target.Reference.Value,
        target.Application.Text,
        .. IdentityMaterial(target.ApplicationIdentity),
        AgentDesktopText.Invariant(target.ProcessId),
        target.ProcessInstance,
        target.WindowId,
        target.WindowTitle?.Text,
        target.Role.Text,
        target.Name?.Text,
        target.IsSecure ? "secure" : "plain",
    ];

    private static IEnumerable<string?> IdentityMaterial(AgentDesktopApplicationIdentity identity) =>
        [identity.Kind.ToString(), identity.Value];

    private static IEnumerable<string?> ElementActionMaterial(AgentDesktopRequest.ElementAction request) =>
        request switch
        {
            AgentDesktopRequest.Click click => [click.Kind.ToString()],
            AgentDesktopRequest.SetValue setValue => [setValue.Text.Value],
            AgentDesktopRequest.TypeText typeText => [typeText.Text.Value],
            AgentDesktopRequest.Select select => [select.Option.Value],
            AgentDesktopRequest.Clear => [],
            AgentDesktopRequest.Toggle toggle => [toggle.Kind.ToString()],
            AgentDesktopRequest.Expand expand => [expand.Kind.ToString()],
            AgentDesktopRequest.Scroll scroll =>
                [scroll.Direction?.ToString(), AgentDesktopText.Invariant(scroll.Amount)],
            AgentDesktopRequest.PressKey press => [press.Key.Key, press.Key.Modifier?.ToString()],
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            hash.AppendData([0xff, 0xff, 0xff, 0xff]);
            return;
        }

        var bytes = StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
