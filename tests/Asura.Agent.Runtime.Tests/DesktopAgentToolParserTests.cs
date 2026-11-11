using System.Text.Json;
using Asura.Agent;
using Asura.Agent.Runtime;
using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

public sealed class DesktopAgentToolParserTests
{
    private const string Ref = "@s8f3k2p9:e5";
    private static readonly AgentDesktopApplicationIdentity TextEditIdentity = new(
        AgentDesktopApplicationIdentityKind.BundleIdentifier,
        "com.apple.TextEdit");

    [Fact]
    public void ToolSetOffersExactlyTheSixteenCatalogToolsWithClosedSchemas()
    {
        Assert.Equal(
            BuiltInAgentTools.Catalog.Tools
                .Where(tool => DesktopAgentToolNames.Owns(tool.Name))
                .Select(tool => tool.Name),
            DesktopAgentToolSet.Tools.Select(tool => tool.Name),
            StringComparer.Ordinal);
        Assert.Equal(16, DesktopAgentToolSet.Tools.Length);
        Assert.All(
            DesktopAgentToolSet.Tools,
            tool =>
            {
                Assert.True(DesktopAgentToolSet.Owns(tool.Name));
                Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
                var properties = tool.InputSchema.GetProperty("properties")
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal);
                Assert.All(
                    tool.InputSchema.GetProperty("required").EnumerateArray(),
                    required => Assert.Contains(required.GetString()!, properties));
            });
    }

    [Fact]
    public void SnapshotSchemaOffersOnlyApplicationOwnedSurfaces()
    {
        var surfaces = Schema(DesktopAgentToolNames.Snapshot)
            .GetProperty("properties")
            .GetProperty("surface")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString());

        Assert.Equal(
            Enum.GetNames<AgentDesktopSurface>().Select(name => name.ToLowerInvariant()),
            surfaces,
            StringComparer.Ordinal);

        // The pinned library offers no toolbar surface, so neither the schema
        // nor the description mentions one.
        var tool = Tool(DesktopAgentToolNames.Snapshot);
        Assert.DoesNotContain("toolbar", tool.InputSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toolbar", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PressKeySchemaSpellsOutOnlyKnownKeysAndSingleModifiers()
    {
        var properties = Schema(DesktopAgentToolNames.PressKey).GetProperty("properties");
        var keys = properties.GetProperty("key").GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        // 26 letters, 10 digits and 26 named keys.
        Assert.Equal(62, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.True(AgentDesktopKeyPress.IsKnownKey(key)));
        Assert.Equal(
            ["cmd", "ctrl", "alt", "shift"],
            properties.GetProperty("modifier").GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()),
            StringComparer.Ordinal);
        Assert.Equal("string", properties.GetProperty("modifier").GetProperty("type").GetString());
    }

    [Fact]
    public void DescriptionsCarryTheRulesTheModelMustFollow()
    {
        Assert.Contains(
            "Always snapshot first",
            Tool(DesktopAgentToolNames.Snapshot).Description,
            StringComparison.Ordinal);
        Assert.Contains(
            "untrusted data",
            Tool(DesktopAgentToolNames.Snapshot).Description,
            StringComparison.Ordinal);
        foreach (var name in new[]
                 {
                     DesktopAgentToolNames.Click, DesktopAgentToolNames.SetValue, DesktopAgentToolNames.TypeText,
                     DesktopAgentToolNames.Clear, DesktopAgentToolNames.Select, DesktopAgentToolNames.Toggle,
                     DesktopAgentToolNames.Expand, DesktopAgentToolNames.Scroll, DesktopAgentToolNames.PressKey,
                 })
        {
            var description = Tool(name).Description;
            Assert.Contains("snapshot-qualified and expire", description, StringComparison.Ordinal);
            Assert.Contains("never repeat the action", description, StringComparison.Ordinal);
        }

        foreach (var name in new[]
                 {
                     DesktopAgentToolNames.SetValue, DesktopAgentToolNames.TypeText, DesktopAgentToolNames.PressKey,
                 })
        {
            Assert.Contains(
                "Never type passwords, tokens or other secrets",
                Tool(name).Description,
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.ListApps, "{}", typeof(AgentDesktopRequest.ListApps))]
    [InlineData(DesktopAgentToolNames.ListWindows, "{}", typeof(AgentDesktopRequest.ListWindows))]
    [InlineData(DesktopAgentToolNames.ListWindows, """{"app":"TextEdit"}""", typeof(AgentDesktopRequest.ListWindows))]
    [InlineData(DesktopAgentToolNames.Snapshot, """{"app":"com.apple.TextEdit"}""", typeof(AgentDesktopRequest.Snapshot))]
    [InlineData(DesktopAgentToolNames.Wait, """{"app":"TextEdit","condition":"text","value":"Saved"}""", typeof(AgentDesktopRequest.Wait))]
    [InlineData(DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e5"}""", typeof(AgentDesktopRequest.Click))]
    [InlineData(DesktopAgentToolNames.SetValue, """{"ref":"@s8f3k2p9:e5","text":"hello"}""", typeof(AgentDesktopRequest.SetValue))]
    [InlineData(DesktopAgentToolNames.TypeText, """{"ref":"@s8f3k2p9:e5","text":"line one\n\tline two"}""", typeof(AgentDesktopRequest.TypeText))]
    [InlineData(DesktopAgentToolNames.Clear, """{"ref":"@s8f3k2p9:e5"}""", typeof(AgentDesktopRequest.Clear))]
    [InlineData(DesktopAgentToolNames.Select, """{"ref":"@s8f3k2p9:e5","option":"Rich Text"}""", typeof(AgentDesktopRequest.Select))]
    [InlineData(DesktopAgentToolNames.Toggle, """{"ref":"@s8f3k2p9:e5"}""", typeof(AgentDesktopRequest.Toggle))]
    [InlineData(DesktopAgentToolNames.Expand, """{"ref":"@s8f3k2p9:e5"}""", typeof(AgentDesktopRequest.Expand))]
    [InlineData(DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5"}""", typeof(AgentDesktopRequest.Scroll))]
    [InlineData(DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"return"}""", typeof(AgentDesktopRequest.PressKey))]
    [InlineData(DesktopAgentToolNames.FocusWindow, """{"window_id":"w-41"}""", typeof(AgentDesktopRequest.FocusWindow))]
    [InlineData(DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit"}""", typeof(AgentDesktopRequest.LaunchApp))]
    [InlineData(DesktopAgentToolNames.CloseApp, """{"app":"TextEdit"}""", typeof(AgentDesktopRequest.CloseApp))]
    public void ParsesEveryToolIntoItsClosedRequest(string toolName, string arguments, Type expected)
    {
        var request = Bind(Accepted(toolName, arguments));

        Assert.IsType(expected, request);
        Assert.Equal(toolName, request.ToolName);
    }

    [Fact]
    public void ACloseIsAnObservedApplicationLookupAndALaunchIsAName()
    {
        // The model's name for an application it wants closed is only a key
        // into the run's observations; a launch has nothing to observe.
        var close = Assert.IsType<DesktopAgentIntent.OnApplication>(
            Accepted(DesktopAgentToolNames.CloseApp, """{"app":"com.apple.TextEdit"}"""));
        var launch = Assert.IsType<DesktopAgentIntent.Direct>(
            Accepted(DesktopAgentToolNames.LaunchApp, """{"app":"com.apple.TextEdit"}"""));

        Assert.Equal("com.apple.TextEdit", close.Application.Value);
        Assert.Equal(DesktopAgentToolNames.CloseApp, close.ToolName);
        Assert.IsType<AgentDesktopRequest.LaunchApp>(launch.Request);
    }

    [Fact]
    public void SnapshotDefaultsToTheInteractiveCompactWindowAndAcceptsEveryBound()
    {
        var defaults = Assert.IsType<AgentDesktopRequest.Snapshot>(
            Bind(Accepted(DesktopAgentToolNames.Snapshot, """{"app":"TextEdit"}""")));
        Assert.Equal("TextEdit", defaults.Application.Value);
        Assert.Equal(AgentDesktopSurface.Window, defaults.Surface);
        Assert.True(defaults.InteractiveOnly);
        Assert.True(defaults.Compact);
        Assert.Equal(AgentDesktopRequest.DefaultSnapshotDepth, defaults.MaximumDepth);

        var explicitRequest = Assert.IsType<AgentDesktopRequest.Snapshot>(
            Bind(Accepted(
                DesktopAgentToolNames.Snapshot,
                """{"app":"TextEdit","surface":"sheet","interactive_only":false,"compact":false,"max_depth":32}""")));
        Assert.Equal(AgentDesktopSurface.Sheet, explicitRequest.Surface);
        Assert.False(explicitRequest.InteractiveOnly);
        Assert.False(explicitRequest.Compact);
        Assert.Equal(32, explicitRequest.MaximumDepth);

        foreach (var surface in Enum.GetValues<AgentDesktopSurface>())
        {
            var token = surface.ToString().ToLowerInvariant();
            var parsed = Assert.IsType<AgentDesktopRequest.Snapshot>(
                Bind(Accepted(DesktopAgentToolNames.Snapshot, $$"""{"app":"TextEdit","surface":"{{token}}"}""")));
            Assert.Equal(surface, parsed.Surface);
        }
    }

    [Fact]
    public void WaitParsesBothConditionsAndDefaultsItsTimeout()
    {
        var text = Assert.IsType<AgentDesktopRequest.Wait>(
            Bind(Accepted(
                DesktopAgentToolNames.Wait,
                """{"app":"TextEdit","condition":"text","value":"Saved"}""")));
        var textCondition = Assert.IsType<AgentDesktopWaitCondition.Text>(text.Condition);
        Assert.Equal("Saved", textCondition.Value);
        Assert.Equal("TextEdit", textCondition.Application.Value);
        Assert.Equal(5_000, text.TimeoutMilliseconds);

        var title = Assert.IsType<AgentDesktopRequest.Wait>(
            Bind(Accepted(
                DesktopAgentToolNames.Wait,
                """{"app":"TextEdit","condition":"window_title","value":"Untitled","timeout_ms":30000}""")));
        Assert.IsType<AgentDesktopWaitCondition.WindowTitle>(title.Condition);
        Assert.Equal(30_000, title.TimeoutMilliseconds);
    }

    [Theory]
    [InlineData("single", AgentDesktopClickKind.Single)]
    [InlineData("double", AgentDesktopClickKind.Double)]
    [InlineData("right", AgentDesktopClickKind.Right)]
    public void ClickParsesEveryKind(string token, AgentDesktopClickKind expected)
    {
        var click = Assert.IsType<AgentDesktopRequest.Click>(
            Bind(Accepted(DesktopAgentToolNames.Click, $$"""{"ref":"{{Ref}}","kind":"{{token}}"}""")));

        Assert.Equal(expected, click.Kind);
        Assert.Equal(Ref, click.Target.Reference.Value);
    }

    [Theory]
    [InlineData("toggle", AgentDesktopToggleKind.Toggle)]
    [InlineData("check", AgentDesktopToggleKind.Check)]
    [InlineData("uncheck", AgentDesktopToggleKind.Uncheck)]
    public void ToggleParsesEveryKind(string token, AgentDesktopToggleKind expected)
    {
        var toggle = Assert.IsType<AgentDesktopRequest.Toggle>(
            Bind(Accepted(DesktopAgentToolNames.Toggle, $$"""{"ref":"{{Ref}}","kind":"{{token}}"}""")));

        Assert.Equal(expected, toggle.Kind);
    }

    [Theory]
    [InlineData("expand", AgentDesktopExpandKind.Expand)]
    [InlineData("collapse", AgentDesktopExpandKind.Collapse)]
    public void ExpandParsesEveryKind(string token, AgentDesktopExpandKind expected)
    {
        var expand = Assert.IsType<AgentDesktopRequest.Expand>(
            Bind(Accepted(DesktopAgentToolNames.Expand, $$"""{"ref":"{{Ref}}","kind":"{{token}}"}""")));

        Assert.Equal(expected, expand.Kind);
    }

    [Fact]
    public void ScrollParsesADirectionWithAnAmountOrScrollsIntoView()
    {
        var intoView = Assert.IsType<AgentDesktopRequest.Scroll>(
            Bind(Accepted(DesktopAgentToolNames.Scroll, $$"""{"ref":"{{Ref}}"}""")));
        Assert.True(intoView.IsScrollIntoView);

        foreach (var direction in Enum.GetValues<AgentDesktopScrollDirection>())
        {
            var token = direction.ToString().ToLowerInvariant();
            var scroll = Assert.IsType<AgentDesktopRequest.Scroll>(
                Bind(Accepted(
                    DesktopAgentToolNames.Scroll,
                    $$"""{"ref":"{{Ref}}","direction":"{{token}}","amount":50}""")));
            Assert.Equal(direction, scroll.Direction);
            Assert.Equal(50, scroll.Amount);
        }
    }

    [Theory]
    [InlineData("cmd", AgentDesktopKeyModifier.Command)]
    [InlineData("ctrl", AgentDesktopKeyModifier.Control)]
    [InlineData("alt", AgentDesktopKeyModifier.Alt)]
    [InlineData("shift", AgentDesktopKeyModifier.Shift)]
    public void PressKeyParsesOneKeyWithOneModifier(string token, AgentDesktopKeyModifier expected)
    {
        var press = Assert.IsType<AgentDesktopRequest.PressKey>(
            Bind(Accepted(
                DesktopAgentToolNames.PressKey,
                $$"""{"ref":"{{Ref}}","key":"s","modifier":"{{token}}"}""")));

        Assert.Equal("s", press.Key.Key);
        Assert.Equal(expected, press.Key.Modifier);
    }

    [Fact]
    public void EveryAdvertisedKeyParses()
    {
        foreach (var key in DesktopAgentToolSet.Keys)
        {
            var press = Assert.IsType<AgentDesktopRequest.PressKey>(
                Bind(Accepted(DesktopAgentToolNames.PressKey, $$"""{"ref":"{{Ref}}","key":"{{key}}"}""")));
            Assert.Equal(key, press.Key.Key);
            Assert.Null(press.Key.Modifier);
        }
    }

    [Fact]
    public void LaunchParsesItsTimeoutAndTextKeepsItsExactValue()
    {
        var launch = Assert.IsType<AgentDesktopRequest.LaunchApp>(
            Bind(Accepted(DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit","timeout_ms":15000}""")));
        Assert.Equal(15_000, launch.TimeoutMilliseconds);

        var longest = new string('a', AgentDesktopInputText.MaximumBytes);
        var typed = Assert.IsType<AgentDesktopRequest.TypeText>(
            Bind(Accepted(
                DesktopAgentToolNames.TypeText,
                JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["ref"] = Ref, ["text"] = longest }))));
        Assert.Equal(longest, typed.Text.Value);
        Assert.Equal(AgentDesktopInputText.MaximumBytes, typed.Text.ByteCount);
    }

    [Fact]
    public void ElementIntentsCarryOnlyTheRefAndSayWhetherASecureFieldRefusesThem()
    {
        Assert.All(
            new (string Tool, string Arguments, bool RefusedOnSecureField)[]
            {
                (DesktopAgentToolNames.Click, $$"""{"ref":"{{Ref}}"}""", false),
                (DesktopAgentToolNames.Clear, $$"""{"ref":"{{Ref}}"}""", true),
                (DesktopAgentToolNames.Select, $$"""{"ref":"{{Ref}}","option":"Rich Text"}""", false),
                (DesktopAgentToolNames.Toggle, $$"""{"ref":"{{Ref}}"}""", false),
                (DesktopAgentToolNames.Expand, $$"""{"ref":"{{Ref}}"}""", false),
                (DesktopAgentToolNames.Scroll, $$"""{"ref":"{{Ref}}"}""", false),
                (DesktopAgentToolNames.SetValue, $$"""{"ref":"{{Ref}}","text":"hello"}""", true),
                (DesktopAgentToolNames.TypeText, $$"""{"ref":"{{Ref}}","text":"hello"}""", true),
                (DesktopAgentToolNames.PressKey, $$"""{"ref":"{{Ref}}","key":"a"}""", true),
            },
            item =>
            {
                var intent = Assert.IsType<DesktopAgentIntent.OnElement>(Accepted(item.Tool, item.Arguments));
                Assert.Equal(item.Tool, intent.ToolName);
                Assert.Equal(Ref, intent.Reference.Value);
                Assert.Equal("s8f3k2p9", intent.Reference.SnapshotId);
                Assert.Equal(item.RefusedOnSecureField, intent.RefusedOnSecureField);
            });

        var window = Assert.IsType<DesktopAgentIntent.OnWindow>(
            Accepted(DesktopAgentToolNames.FocusWindow, """{"window_id":"w-41"}"""));
        Assert.Equal("w-41", window.WindowId);
    }

    public static TheoryData<string, string> InvalidArguments() => new()
    {
        // Unknown and repeated properties, on every tool.
        { DesktopAgentToolNames.ListApps, """{"extra":true}""" },
        { DesktopAgentToolNames.ListWindows, """{"app":"TextEdit","extra":true}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","pid":1}""" },
        { DesktopAgentToolNames.Wait, """{"app":"TextEdit","condition":"text","value":"x","approved":true}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e5","x":10,"y":20}""" },
        { DesktopAgentToolNames.SetValue, """{"ref":"@s8f3k2p9:e5","text":"a","authorization_id":"forged"}""" },
        { DesktopAgentToolNames.TypeText, """{"ref":"@s8f3k2p9:e5","text":"a","headed":true}""" },
        { DesktopAgentToolNames.Clear, """{"ref":"@s8f3k2p9:e5","force":true}""" },
        { DesktopAgentToolNames.Select, """{"ref":"@s8f3k2p9:e5","option":"a","index":1}""" },
        { DesktopAgentToolNames.Toggle, """{"ref":"@s8f3k2p9:e5","kind":"check","extra":1}""" },
        { DesktopAgentToolNames.Expand, """{"ref":"@s8f3k2p9:e5","extra":1}""" },
        { DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5","wheel":3}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"a","hold":true}""" },
        { DesktopAgentToolNames.FocusWindow, """{"window_id":"w-41","pid":1}""" },
        { DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit","path":"/Applications/TextEdit.app"}""" },
        { DesktopAgentToolNames.CloseApp, """{"app":"TextEdit","force":true}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e5","ref":"@s8f3k2p9:e6"}""" },

        // Missing required properties.
        { DesktopAgentToolNames.Snapshot, "{}" },
        { DesktopAgentToolNames.Wait, """{"app":"TextEdit","condition":"text"}""" },
        { DesktopAgentToolNames.Wait, """{"app":"TextEdit","value":"x"}""" },
        { DesktopAgentToolNames.Click, "{}" },
        { DesktopAgentToolNames.SetValue, """{"ref":"@s8f3k2p9:e5"}""" },
        { DesktopAgentToolNames.TypeText, """{"text":"a"}""" },
        { DesktopAgentToolNames.Select, """{"ref":"@s8f3k2p9:e5"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5"}""" },
        { DesktopAgentToolNames.FocusWindow, "{}" },
        { DesktopAgentToolNames.LaunchApp, "{}" },
        { DesktopAgentToolNames.CloseApp, "{}" },

        // Wrong JSON types.
        { DesktopAgentToolNames.ListWindows, """{"app":7}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","interactive_only":"true"}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","compact":1}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","max_depth":"8"}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","max_depth":8.5}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":["TextEdit"]}""" },
        { DesktopAgentToolNames.Click, """{"ref":5}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e5","kind":null}""" },
        { DesktopAgentToolNames.TypeText, """{"ref":"@s8f3k2p9:e5","text":["a"]}""" },
        { DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5","direction":"down","amount":"3"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"a","modifier":["cmd","shift"]}""" },
        { DesktopAgentToolNames.FocusWindow, """{"window_id":41}""" },

        // Out-of-range values.
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","max_depth":0}""" },
        { DesktopAgentToolNames.Snapshot, """{"app":"TextEdit","max_depth":33}""" },
        { DesktopAgentToolNames.Wait, """{"app":"TextEdit","condition":"text","value":"x","timeout_ms":99}""" },
        { DesktopAgentToolNames.Wait, """{"app":"TextEdit","condition":"text","value":"x","timeout_ms":30001}""" },
        { DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5","direction":"down","amount":0}""" },
        { DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5","direction":"down","amount":51}""" },
        { DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5","amount":3}""" },
        { DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit","timeout_ms":999}""" },
        { DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit","timeout_ms":15001}""" },
        { DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit","timeout_ms":4294967296}""" },

        // Values outside the closed vocabularies.
        { DesktopAgentToolNames.Wait, """{"app":"TextEdit","condition":"element","value":"x"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e5","kind":"middle"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e5","kind":"Single"}""" },
        { DesktopAgentToolNames.Toggle, """{"ref":"@s8f3k2p9:e5","kind":"on"}""" },
        { DesktopAgentToolNames.Expand, """{"ref":"@s8f3k2p9:e5","kind":"open"}""" },
        { DesktopAgentToolNames.Scroll, """{"ref":"@s8f3k2p9:e5","direction":"diagonal","amount":1}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"enter"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"A"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"cmd+s"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"KEY_DOWN"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"s","modifier":"cmd+shift"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"s","modifier":"super"}""" },

        // Quitting an application through a key chord is not representable.
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8f3k2p9:e5","key":"q","modifier":"cmd"}""" },

        // Refs that are not snapshot-qualified.
        { DesktopAgentToolNames.Click, """{"ref":"@e5"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8:e5"}""" },
        { DesktopAgentToolNames.PressKey, """{"ref":"@s8:e5","key":"a"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"e5"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:e05"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3k2p9:x5"}""" },
        { DesktopAgentToolNames.Click, """{"ref":"@s8f3 k2p9:e5"}""" },
        { DesktopAgentToolNames.Click, """{"ref":""}""" },

        // Applications named by path, and malformed identifiers.
        { DesktopAgentToolNames.Snapshot, """{"app":"/Applications/TextEdit.app"}""" },
        { DesktopAgentToolNames.LaunchApp, """{"app":"..\\Tools\\cmd.exe"}""" },
        { DesktopAgentToolNames.CloseApp, """{"app":""}""" },
        { DesktopAgentToolNames.CloseApp, """{"app":"Text\nEdit"}""" },
        { DesktopAgentToolNames.FocusWindow, """{"window_id":""}""" },
        { DesktopAgentToolNames.FocusWindow, """{"window_id":"w\u0000"}""" },
        {
            DesktopAgentToolNames.FocusWindow,
            JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["window_id"] = new string('w', 129) })
        },

        // Text that is empty, oversized, or not strict Unicode.
        { DesktopAgentToolNames.TypeText, """{"ref":"@s8f3k2p9:e5","text":""}""" },
        { DesktopAgentToolNames.SetValue, """{"ref":"@s8f3k2p9:e5","text":"bell\u0007"}""" },
        { DesktopAgentToolNames.SetValue, """{"ref":"@s8f3k2p9:e5","text":"hidden\u202eoverride"}""" },
        { DesktopAgentToolNames.TypeText, """{"ref":"@s8f3k2p9:e5","text":"lone\ud800surrogate"}""" },
        {
            DesktopAgentToolNames.TypeText,
            TextArguments("text", new string('a', AgentDesktopInputText.MaximumBytes + 1))
        },
        {
            DesktopAgentToolNames.SetValue,
            TextArguments("text", new string('é', (AgentDesktopInputText.MaximumBytes / 2) + 1))
        },
        {
            DesktopAgentToolNames.Select,
            TextArguments("option", new string('a', AgentDesktopInputText.MaximumBytes + 1))
        },
        {
            DesktopAgentToolNames.Wait,
            JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app"] = "TextEdit",
                ["condition"] = "text",
                ["value"] = new string('a', 257),
            })
        },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsMalformedUnknownAndOutOfRangeArguments(string toolName, string arguments)
    {
        Assert.Equal(AgentDesktopStableCodes.InvalidArguments, Rejected(toolName, arguments));
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("dock")]
    [InlineData("spotlight")]
    [InlineData("menu_bar_extras")]
    [InlineData("menubar_extras")]
    [InlineData("taskbar")]
    [InlineData("system_tray")]
    [InlineData("start_menu")]
    [InlineData("action_center")]
    [InlineData("quick_settings")]
    [InlineData("notification_center")]
    [InlineData("Window")]
    [InlineData("toolbar")]
    public void RejectsSystemAndUnofferedSurfaces(string surface)
    {
        Assert.Equal(
            AgentDesktopStableCodes.InvalidArguments,
            Rejected(DesktopAgentToolNames.Snapshot, $$"""{"app":"Finder","surface":"{{surface}}"}"""));
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.TypeText, "text")]
    [InlineData(DesktopAgentToolNames.SetValue, "text")]
    [InlineData(DesktopAgentToolNames.Select, "option")]
    public void RefusesSecretShapedTextWithItsOwnCode(string toolName, string property)
    {
        const string secret = "password=hunter2-correct-horse";
        Assert.True(AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(secret));

        Assert.Equal(
            AgentDesktopStableCodes.PolicyDenied,
            Rejected(toolName, TextArguments(property, secret)));
    }

    [Theory]
    [InlineData("desktop.screenshot")]
    [InlineData("desktop.drag")]
    [InlineData("desktop.clipboard_read")]
    [InlineData("browser.click")]
    public void RejectsToolsOutsideTheClosedSet(string toolName)
    {
        Assert.Equal("unknown_tool", Rejected(toolName, "{}"));
    }

    [Fact]
    public void RejectsArgumentsThatAreNotAnObject()
    {
        Assert.Equal(
            AgentDesktopStableCodes.InvalidArguments,
            Rejected(DesktopAgentToolNames.ListApps, "[]"));
    }

    private static string TextArguments(string property, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["ref"] = Ref, [property] = value });

    private static DesktopAgentIntent Accepted(string toolName, string arguments) =>
        Assert.IsType<DesktopAgentIntentResult.Parsed>(Parse(toolName, arguments)).Intent;

    private static string Rejected(string toolName, string arguments) =>
        Assert.IsType<DesktopAgentIntentResult.Rejected>(Parse(toolName, arguments)).StableCode;

    private static DesktopAgentIntentResult Parse(string toolName, string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        return DesktopAgentToolParser.Parse(toolName, document.RootElement.Clone());
    }

    private static AgentDesktopRequest Bind(DesktopAgentIntent intent) => intent switch
    {
        DesktopAgentIntent.Direct direct => direct.Request,
        DesktopAgentIntent.OnElement element => element.Bind(new AgentDesktopElementTarget(
            element.Reference,
            AgentDesktopDisplayText.FromUntrusted("TextEdit", "unknown"),
            TextEditIdentity,
            processId: 4100,
            processInstance: "generation-1",
            windowId: "w-1",
            AgentDesktopDisplayText.FromUntrusted("Untitled"),
            DateTimeOffset.UnixEpoch,
            AgentDesktopDisplayText.FromUntrusted("textfield", "unknown"),
            AgentDesktopDisplayText.FromUntrusted("Body"),
            isSecure: false)),
        DesktopAgentIntent.OnWindow window => new AgentDesktopRequest.FocusWindow(
            new AgentDesktopWindowTarget(
                new AgentDesktopApplicationId("TextEdit"),
                TextEditIdentity,
                window.WindowId,
                AgentDesktopDisplayText.FromUntrusted("Untitled", "(untitled)"),
                processId: 4100,
                processInstance: "generation-1",
                DateTimeOffset.UnixEpoch)),
        DesktopAgentIntent.OnApplication application => new AgentDesktopRequest.CloseApp(
            new AgentDesktopApplicationTarget(
                AgentDesktopDisplayText.FromUntrusted(application.Application.Value, "unknown"),
                TextEditIdentity,
                processId: 4100,
                processInstance: "generation-1",
                DateTimeOffset.UnixEpoch)),
        _ => throw new ArgumentOutOfRangeException(nameof(intent)),
    };

    private static AgentToolDefinition Tool(string name) =>
        DesktopAgentToolSet.Tools.Single(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    private static JsonElement Schema(string name) => Tool(name).InputSchema;
}
