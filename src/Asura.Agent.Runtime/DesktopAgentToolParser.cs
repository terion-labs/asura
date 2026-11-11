using System.Text.Json;
using Asura.Agent;
using Asura.Application;

namespace Asura.Agent.Runtime;

/// <summary>
/// Turns one <c>desktop.*</c> tool call into a closed intent, or rejects it
/// with a stable code. Nothing here consults the host or the policy, so a
/// malformed, out-of-range or secret-carrying call ends before any authority
/// is requested.
/// </summary>
internal static class DesktopAgentToolParser
{
    private const int DefaultWaitTimeoutMilliseconds = 5_000;
    private const int MaximumWindowIdLength = 128;

    public static DesktopAgentIntentResult Parse(AgentToolProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return Parse(proposal.ToolName, proposal.Arguments);
    }

    internal static DesktopAgentIntentResult Parse(string toolName, JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        if (!DesktopAgentToolNames.Owns(toolName))
        {
            return new DesktopAgentIntentResult.Rejected("unknown_tool");
        }

        try
        {
            return toolName switch
            {
                DesktopAgentToolNames.ListApps => ParseListApps(arguments),
                DesktopAgentToolNames.ListWindows => ParseListWindows(arguments),
                DesktopAgentToolNames.Snapshot => ParseSnapshot(arguments),
                DesktopAgentToolNames.Wait => ParseWait(arguments),
                DesktopAgentToolNames.Click => ParseClick(arguments),
                DesktopAgentToolNames.SetValue => ParseText(
                    toolName,
                    arguments,
                    text => target => new AgentDesktopRequest.SetValue(target, text)),
                DesktopAgentToolNames.TypeText => ParseText(
                    toolName,
                    arguments,
                    text => target => new AgentDesktopRequest.TypeText(target, text)),
                DesktopAgentToolNames.Clear => ParseClear(arguments),
                DesktopAgentToolNames.Select => ParseSelect(arguments),
                DesktopAgentToolNames.Toggle => ParseToggle(arguments),
                DesktopAgentToolNames.Expand => ParseExpand(arguments),
                DesktopAgentToolNames.Scroll => ParseScroll(arguments),
                DesktopAgentToolNames.PressKey => ParsePressKey(arguments),
                DesktopAgentToolNames.FocusWindow => ParseFocusWindow(arguments),
                DesktopAgentToolNames.LaunchApp => ParseLaunchApp(arguments),
                DesktopAgentToolNames.CloseApp => ParseCloseApp(arguments),
                _ => Invalid(),
            };
        }
        catch (ArgumentException)
        {
            // Raised by the argument reader and by the typed request
            // constructors, which own the bounds of every value.
            return Invalid();
        }
    }

    private static DesktopAgentIntentResult ParseListApps(JsonElement arguments)
    {
        _ = ArgumentReader.Read(arguments);
        return Direct(new AgentDesktopRequest.ListApps());
    }

    private static DesktopAgentIntentResult ParseListWindows(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "app");
        return Direct(new AgentDesktopRequest.ListWindows(
            reader.OptionalString("app") is { } application
                ? new AgentDesktopApplicationId(application)
                : null));
    }

    private static DesktopAgentIntentResult ParseSnapshot(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(
            arguments,
            "app",
            "surface",
            "interactive_only",
            "compact",
            "max_depth");
        return Direct(new AgentDesktopRequest.Snapshot(
            new AgentDesktopApplicationId(reader.RequiredString("app")),
            reader.OptionalString("surface") switch
            {
                null or "window" => AgentDesktopSurface.Window,
                "focused" => AgentDesktopSurface.Focused,
                "menu" => AgentDesktopSurface.Menu,
                "menubar" => AgentDesktopSurface.Menubar,
                "sheet" => AgentDesktopSurface.Sheet,
                "popover" => AgentDesktopSurface.Popover,
                "alert" => AgentDesktopSurface.Alert,

                // The Dock, the desktop, Spotlight and every other system
                // surface belong to no application and are never offered.
                _ => throw new ArgumentException("The surface is not an application-owned surface."),
            },
            reader.OptionalBoolean("interactive_only") ?? true,
            reader.OptionalBoolean("compact") ?? true,
            reader.OptionalInteger("max_depth") ?? AgentDesktopRequest.DefaultSnapshotDepth));
    }

    private static DesktopAgentIntentResult ParseWait(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "app", "condition", "value", "timeout_ms");
        var application = new AgentDesktopApplicationId(reader.RequiredString("app"));
        var value = reader.RequiredString("value");
        AgentDesktopWaitCondition condition = reader.RequiredString("condition") switch
        {
            "text" => new AgentDesktopWaitCondition.Text(application, value),
            "window_title" => new AgentDesktopWaitCondition.WindowTitle(application, value),
            _ => throw new ArgumentException("The wait condition is not supported."),
        };
        return Direct(new AgentDesktopRequest.Wait(
            condition,
            reader.OptionalInteger("timeout_ms") ?? DefaultWaitTimeoutMilliseconds));
    }

    private static DesktopAgentIntentResult ParseClick(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "kind");
        var kind = reader.OptionalString("kind") switch
        {
            null or "single" => AgentDesktopClickKind.Single,
            "double" => AgentDesktopClickKind.Double,
            "right" => AgentDesktopClickKind.Right,
            _ => throw new ArgumentException("The click kind is not supported."),
        };
        return OnElement(
            DesktopAgentToolNames.Click,
            reader,
            refusedOnSecureField: false,
            target => new AgentDesktopRequest.Click(target, kind));
    }

    private static DesktopAgentIntentResult ParseText(
        string toolName,
        JsonElement arguments,
        Func<AgentDesktopInputText, Func<AgentDesktopElementTarget, AgentDesktopRequest>> bind)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "text");
        return TryReadInputText(reader, "text", out var text) is { } refusal
            ? refusal
            : OnElement(toolName, reader, refusedOnSecureField: true, bind(text));
    }

    // Clearing a secure field destroys what the user typed there, and only
    // the user handles credentials.
    private static DesktopAgentIntentResult ParseClear(JsonElement arguments) =>
        OnElement(
            DesktopAgentToolNames.Clear,
            ArgumentReader.Read(arguments, "ref"),
            refusedOnSecureField: true,
            target => new AgentDesktopRequest.Clear(target));

    private static DesktopAgentIntentResult ParseSelect(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "option");
        return TryReadInputText(reader, "option", out var option) is { } refusal
            ? refusal
            : OnElement(
                DesktopAgentToolNames.Select,
                reader,
                refusedOnSecureField: false,
                target => new AgentDesktopRequest.Select(target, option));
    }

    private static DesktopAgentIntentResult ParseToggle(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "kind");
        var kind = reader.OptionalString("kind") switch
        {
            null or "toggle" => AgentDesktopToggleKind.Toggle,
            "check" => AgentDesktopToggleKind.Check,
            "uncheck" => AgentDesktopToggleKind.Uncheck,
            _ => throw new ArgumentException("The toggle kind is not supported."),
        };
        return OnElement(
            DesktopAgentToolNames.Toggle,
            reader,
            refusedOnSecureField: false,
            target => new AgentDesktopRequest.Toggle(target, kind));
    }

    private static DesktopAgentIntentResult ParseExpand(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "kind");
        var kind = reader.OptionalString("kind") switch
        {
            null or "expand" => AgentDesktopExpandKind.Expand,
            "collapse" => AgentDesktopExpandKind.Collapse,
            _ => throw new ArgumentException("The expand kind is not supported."),
        };
        return OnElement(
            DesktopAgentToolNames.Expand,
            reader,
            refusedOnSecureField: false,
            target => new AgentDesktopRequest.Expand(target, kind));
    }

    private static DesktopAgentIntentResult ParseScroll(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "direction", "amount");
        AgentDesktopScrollDirection? direction = reader.OptionalString("direction") switch
        {
            null => null,
            "up" => AgentDesktopScrollDirection.Up,
            "down" => AgentDesktopScrollDirection.Down,
            "left" => AgentDesktopScrollDirection.Left,
            "right" => AgentDesktopScrollDirection.Right,
            _ => throw new ArgumentException("The scroll direction is not supported."),
        };
        var amount = reader.OptionalInteger("amount");

        // The request checks the range only once a target exists, which is
        // after the ledger; an amount is checked here so it fails first. An
        // amount without a direction would be silently ignored by a
        // scroll-into-view, so it is refused instead.
        if (amount is < AgentDesktopRequest.MinimumScrollAmount or > AgentDesktopRequest.MaximumScrollAmount
            || (amount is not null && direction is null))
        {
            return Invalid();
        }

        return OnElement(
            DesktopAgentToolNames.Scroll,
            reader,
            refusedOnSecureField: false,
            target => new AgentDesktopRequest.Scroll(
                target,
                direction,
                amount ?? AgentDesktopRequest.MinimumScrollAmount));
    }

    private static DesktopAgentIntentResult ParsePressKey(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "ref", "key", "modifier");
        AgentDesktopKeyModifier? modifier = reader.OptionalString("modifier") switch
        {
            null => null,
            "cmd" => AgentDesktopKeyModifier.Command,
            "ctrl" => AgentDesktopKeyModifier.Control,
            "alt" => AgentDesktopKeyModifier.Alt,
            "shift" => AgentDesktopKeyModifier.Shift,
            _ => throw new ArgumentException("The key modifier is not supported."),
        };
        var key = new AgentDesktopKeyPress(reader.RequiredString("key"), modifier);

        // A key press is a keystroke into the element: letting it reach a
        // secure field would let a password be typed one key at a time.
        return OnElement(
            DesktopAgentToolNames.PressKey,
            reader,
            refusedOnSecureField: true,
            target => new AgentDesktopRequest.PressKey(target, key));
    }

    private static DesktopAgentIntentResult ParseFocusWindow(JsonElement arguments)
    {
        var windowId = ArgumentReader.Read(arguments, "window_id").RequiredString("window_id");
        return windowId.Length is 0 or > MaximumWindowIdLength || windowId.Any(char.IsControl)
            ? Invalid()
            : new DesktopAgentIntentResult.Parsed(new DesktopAgentIntent.OnWindow(windowId));
    }

    private static DesktopAgentIntentResult ParseLaunchApp(JsonElement arguments)
    {
        var reader = ArgumentReader.Read(arguments, "app", "timeout_ms");
        var application = new AgentDesktopApplicationId(reader.RequiredString("app"));
        return Direct(reader.OptionalInteger("timeout_ms") is { } timeout
            ? new AgentDesktopRequest.LaunchApp(application, timeout)
            : new AgentDesktopRequest.LaunchApp(application));
    }

    // Unlike a launch, a close acts on a running process, so the name has to
    // match an application the run observed; the ledger supplies the rest.
    private static DesktopAgentIntentResult ParseCloseApp(JsonElement arguments) =>
        new DesktopAgentIntentResult.Parsed(new DesktopAgentIntent.OnApplication(
            new AgentDesktopApplicationId(
                ArgumentReader.Read(arguments, "app").RequiredString("app"))));

    /// <summary>
    /// Reads text the model wants delivered to another application. A likely
    /// literal secret is refused with its own code rather than as a malformed
    /// argument, so the model stops instead of rephrasing the same secret.
    /// </summary>
    private static DesktopAgentIntentResult.Rejected? TryReadInputText(
        ArgumentReader reader,
        string name,
        out AgentDesktopInputText text)
    {
        var value = reader.RequiredString(name);

        // A UTF-16 length above the byte limit already exceeds it; checking
        // first keeps oversized input away from the secret classifier.
        if (value.Length > AgentDesktopInputText.MaximumBytes)
        {
            throw new ArgumentException("The input text exceeds its byte limit.");
        }

        if (AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(value))
        {
            text = null!;
            return new DesktopAgentIntentResult.Rejected(AgentDesktopStableCodes.PolicyDenied);
        }

        text = new AgentDesktopInputText(value);
        return null;
    }

    private static DesktopAgentIntentResult Direct(AgentDesktopRequest request) =>
        new DesktopAgentIntentResult.Parsed(new DesktopAgentIntent.Direct(request));

    private static DesktopAgentIntentResult OnElement(
        string toolName,
        ArgumentReader reader,
        bool refusedOnSecureField,
        Func<AgentDesktopElementTarget, AgentDesktopRequest> bind) =>
        new DesktopAgentIntentResult.Parsed(new DesktopAgentIntent.OnElement(
            toolName,
            new AgentDesktopElementRef(reader.RequiredString("ref")),
            refusedOnSecureField,
            bind));

    private static DesktopAgentIntentResult.Rejected Invalid() =>
        new(AgentDesktopStableCodes.InvalidArguments);

    /// <summary>
    /// A strict view of one arguments object: every property must be allowed,
    /// present at most once, and of exactly the JSON type it is read as.
    /// </summary>
    private readonly struct ArgumentReader
    {
        private readonly Dictionary<string, JsonElement> _properties;

        private ArgumentReader(Dictionary<string, JsonElement> properties) =>
            _properties = properties;

        public static ArgumentReader Read(JsonElement arguments, params ReadOnlySpan<string> allowed)
        {
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Tool arguments must be a JSON object.");
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in arguments.EnumerateObject())
            {
                if (!allowed.Contains(property.Name) || !properties.TryAdd(property.Name, property.Value))
                {
                    throw new ArgumentException("Tool arguments contain an unknown or repeated property.");
                }
            }

            return new ArgumentReader(properties);
        }

        public string RequiredString(string name) =>
            OptionalString(name) ?? throw new ArgumentException("A required property is missing.");

        public string? OptionalString(string name)
        {
            if (!_properties.TryGetValue(name, out var element))
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("The property must be a string.");
            }

            try
            {
                return element.GetString();
            }
            catch (InvalidOperationException)
            {
                // An escaped lone surrogate is valid JSON but not text.
                throw new ArgumentException("The property is not well-formed text.");
            }
        }

        public bool? OptionalBoolean(string name)
        {
            if (!_properties.TryGetValue(name, out var element))
            {
                return null;
            }

            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new ArgumentException("The property must be a boolean."),
            };
        }

        public int? OptionalInteger(string name)
        {
            if (!_properties.TryGetValue(name, out var element))
            {
                return null;
            }

            return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
                ? value
                : throw new ArgumentException("The property must be an integer.");
        }
    }
}
