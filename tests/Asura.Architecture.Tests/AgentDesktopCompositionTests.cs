using System.Reflection;
using Asura.Agent.Runtime;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Asura.Desktop;
using Asura.DesktopAutomation;
using Asura.SessionHost;
using Microsoft.Extensions.DependencyInjection;

namespace Asura.Architecture.Tests;

/// <summary>
/// The desktop family is composed like the web tools (ADR 0061): one engine
/// owned by the container, reached only through the one SessionHost client,
/// which every workspace runtime and the Settings row receive. Where the
/// library is absent the same graph composes and only reports Unavailable.
/// </summary>
public sealed class AgentDesktopCompositionTests
{
    [Fact]
    public async Task Desktop_automation_reaches_runtimes_and_settings_through_the_one_session_host()
    {
        AgentDesktopAutomationEngine engine;
        await using (var services = DesktopComposition.CreateServiceProvider())
        {
            var client = services.GetRequiredService<InMemorySessionHostClient>();
            var host = services.GetRequiredService<IAgentDesktopSessionHost>();
            var composer = services.GetRequiredService<AgentDesktopActionComposer>();
            engine = Assert.IsType<AgentDesktopAutomationEngine>(
                services.GetRequiredService<IAgentDesktopAutomationEngine>());

            Assert.Same(client, host);
            Assert.Same(engine, services.GetRequiredService<IAgentDesktopAutomationEngine>());
            Assert.Same(composer, services.GetRequiredService<AgentDesktopActionComposer>());
            Assert.Same(engine, Field(client, "_agentDesktopAutomationEngine"));
            Assert.Same(composer, Field(client, "_agentDesktopActionComposer"));

            var state = await host.GetAgentDesktopAccessAsync(CancellationToken.None);
            if (File.Exists(BundledLibraryPath()))
            {
                Assert.Null(engine.UnavailableReason);
                Assert.NotEqual(AgentDesktopAccessState.Unavailable, state);
            }
            else
            {
                Assert.NotNull(engine.UnavailableReason);
                Assert.Equal(AgentDesktopAccessState.Unavailable, state);
            }

            var factory = services.GetRequiredService<IAgentWorkspaceRuntimeFactory>();
            using (var runtime = factory.Create(
                       new WorkspaceInstanceId("desktop-automation-composition"),
                       new AgentConversationScopeId("desktop-automation-composition"),
                       AgentPolicy.Default))
            {
                var governed = Assert.IsType<GovernedAgentRuntime>(runtime);
                Assert.Same(client, Field(governed, "_agentDesktopHost"));
                Assert.Same(composer, Field(governed, "_desktopComposer"));
            }

            var settings = services.GetRequiredService<MainWindowViewModel>().AgentDesktopAccess;
            Assert.Same(client, Field(settings, "_host"));
        }

        // The container owns the loaded library and its run adapters.
        Assert.Equal(
            AgentDesktopAccessState.Unavailable,
            await engine.GetAccessStateAsync(CancellationToken.None));
    }

    private static string BundledLibraryPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsMacOS() ? "libagent_desktop_ffi.dylib" : "libagent_desktop_ffi.so");

    private static object? Field(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null, $"{instance.GetType().Name} no longer has {name}.");
        return field.GetValue(instance);
    }
}
